using CoreSRE.Application.Alerts.Commands.GenerateSopFromIncident;
using CoreSRE.Application.Alerts.Interfaces;
using CoreSRE.Application.Alerts.Services;
using CoreSRE.Application.Incidents.Models;
using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using CoreSRE.Domain.ValueObjects;
using MediatR;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace CoreSRE.Infrastructure.Services;

/// <summary>
/// Incident 后台处置派发器。
/// 接收 Incident ID 后在后台执行 Agent 对话（SOP / RCA）。
/// 支持实时推送 Agent 对话进度到 SignalR，以及人工消息注入。
/// </summary>
public class IncidentDispatcherService(
    IServiceScopeFactory scopeFactory,
    AgentSessionStore sessionStore,
    ActiveIncidentSessionTracker sessionTracker,
    ILogger<IncidentDispatcherService> logger) : IIncidentDispatcher
{
    private static readonly TimeSpan SopTimeout = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RcaTimeout = TimeSpan.FromMinutes(30);

    /// <inheritdoc />
    public async Task DispatchSopExecutionAsync(
        Guid incidentId,
        Guid agentId,
        Guid sopId,
        string alertName,
        Dictionary<string, string> alertLabels,
        Dictionary<string, string> alertAnnotations,
        CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var incidentRepo = scope.ServiceProvider.GetRequiredService<IIncidentRepository>();
        var agentResolver = scope.ServiceProvider.GetRequiredService<IAgentResolver>();
        var notifier = scope.ServiceProvider.GetRequiredService<IIncidentNotifier>();

        var incident = await incidentRepo.GetByIdAsync(incidentId, cancellationToken);
        if (incident is null)
        {
            logger.LogError("Incident {IncidentId} not found during SOP dispatch.", incidentId);
            return;
        }

        var conversationId = incident.ConversationId?.ToString() ?? Guid.NewGuid().ToString();

        // Register active session for human intervention support
        var proactiveChannel = sessionTracker.RegisterActive(incidentId, agentId, conversationId);

        try
        {
            // 1. 解析 Agent
            var resolved = await agentResolver.ResolveAsync(agentId, conversationId, cancellationToken);
            var aiAgent = resolved.Agent;

            // Notify: agent processing started
            await notifier.AgentProcessingChangedAsync(
                incidentId, true, aiAgent.Name, DateTime.UtcNow, cancellationToken);

            // 2. 构造首条消息
            var userMessage = SopMessageTemplates.BuildSopExecutionMessage(
                alertName, alertLabels, alertAnnotations);

            var messages = new List<ChatMessage>
            {
                new(ChatRole.User, userMessage)
            };

            // Push initial user message to SignalR
            await notifier.ChatMessageReceivedAsync(
                incidentId, "user", userMessage, null, DateTime.UtcNow, cancellationToken);

            // 3. 创建/加载 Session
            AgentSession? session;
            try
            {
                session = await sessionStore.GetSessionAsync(aiAgent, conversationId, cancellationToken);
            }
            catch
            {
                session = await aiAgent.CreateSessionAsync(cancellationToken);
            }

            // 3.5 设置 StateBag — SopContextInitProvider 读取
            await PopulateContextInitStateBagAsync(
                scope, session, sopId, alertLabels, incident.AlertRuleId, cancellationToken);

            // 4. 带超时执行 Agent（含人工介入循环）
            using var timeoutCts = new CancellationTokenSource(SopTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutCts.Token);

            try
            {
                var fullResponse = await RunAgentWithInterventionAsync(
                    aiAgent, session, messages, incidentId, notifier,
                    proactiveChannel.Reader, linkedCts.Token);

                // 5. 执行成功 → 更新 Incident
                incident.Resolve(fullResponse);
                incident.SetTimeToDetect(incident.StartedAt); // MTTD = 0（自动触发）
                incident.AddTimelineEvent(IncidentTimelineVO.Create(
                    TimelineEventType.Resolved,
                    "SOP 自动执行完成",
                    fullResponse));

                await notifier.IncidentResolvedAsync(incidentId, fullResponse, DateTime.UtcNow, cancellationToken);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                // 超时 → 保持 Investigating，需人工介入
                incident.AddTimelineEvent(IncidentTimelineVO.Create(
                    TimelineEventType.Timeout,
                    $"SOP 执行超时 ({SopTimeout.TotalMinutes} 分钟) — 需人工介入"));

                await notifier.IncidentTimeoutAsync(incidentId,
                    $"SOP 执行超时 ({SopTimeout.TotalMinutes} 分钟)", DateTime.UtcNow, cancellationToken);

                logger.LogWarning(
                    "SOP execution timed out for Incident {IncidentId} after {Timeout} minutes.",
                    incidentId, SopTimeout.TotalMinutes);
            }

            // 6. 保存 Session
            try
            {
                await sessionStore.SaveSessionAsync(aiAgent, conversationId, session, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to save agent session for Incident {IncidentId}.", incidentId);
            }

            await incidentRepo.UpdateAsync(incident, cancellationToken);

            logger.LogInformation(
                "SOP execution completed for Incident {IncidentId}. Status={Status}",
                incidentId, incident.Status);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SOP execution failed for Incident {IncidentId}.", incidentId);

            incident.AddTimelineEvent(IncidentTimelineVO.Create(
                TimelineEventType.Timeout,
                $"SOP 执行异常: {ex.Message}"));
            await incidentRepo.UpdateAsync(incident, cancellationToken);
        }
        finally
        {
            await notifier.AgentProcessingChangedAsync(
                incidentId, false, null, DateTime.UtcNow, cancellationToken);
            sessionTracker.UnregisterActive(incidentId);
        }
    }

    /// <inheritdoc />
    public async Task DispatchRootCauseAnalysisAsync(
        Guid incidentId,
        Guid teamAgentId,
        Guid? summarizerAgentId,
        string alertName,
        Dictionary<string, string> alertLabels,
        Dictionary<string, string> alertAnnotations,
        CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var incidentRepo = scope.ServiceProvider.GetRequiredService<IIncidentRepository>();
        var agentResolver = scope.ServiceProvider.GetRequiredService<IAgentResolver>();
        var notifier = scope.ServiceProvider.GetRequiredService<IIncidentNotifier>();

        var incident = await incidentRepo.GetByIdAsync(incidentId, cancellationToken);
        if (incident is null)
        {
            logger.LogError("Incident {IncidentId} not found during RCA dispatch.", incidentId);
            return;
        }

        var conversationId = incident.ConversationId?.ToString() ?? Guid.NewGuid().ToString();

        // Register active session for human intervention support
        var proactiveChannel = sessionTracker.RegisterActive(incidentId, teamAgentId, conversationId);

        try
        {
            // 1. 解析 Team Agent
            var resolved = await agentResolver.ResolveAsync(teamAgentId, conversationId, cancellationToken);
            var aiAgent = resolved.Agent;

            // Notify: agent processing started
            await notifier.AgentProcessingChangedAsync(
                incidentId, true, aiAgent.Name, DateTime.UtcNow, cancellationToken);

            // 2. 构造首条消息
            var userMessage = RcaMessageTemplates.BuildRootCauseAnalysisMessage(
                alertName, alertLabels, alertAnnotations);

            var messages = new List<ChatMessage>
            {
                new(ChatRole.User, userMessage)
            };

            // Push initial user message to SignalR
            await notifier.ChatMessageReceivedAsync(
                incidentId, "user", userMessage, null, DateTime.UtcNow, cancellationToken);

            // 3. 创建/加载 Session
            AgentSession? session;
            try
            {
                session = await sessionStore.GetSessionAsync(aiAgent, conversationId, cancellationToken);
            }
            catch
            {
                session = await aiAgent.CreateSessionAsync(cancellationToken);
            }

            // 4. 带超时执行 Team Agent（含人工介入循环）
            using var timeoutCts = new CancellationTokenSource(RcaTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutCts.Token);

            try
            {
                var fullResponse = await RunAgentWithInterventionAsync(
                    aiAgent, session, messages, incidentId, notifier,
                    proactiveChannel.Reader, linkedCts.Token);

                // 5. 提取根因
                incident.SetRootCause(fullResponse);
                incident.TransitionTo(IncidentStatus.Mitigated);
                incident.AddTimelineEvent(IncidentTimelineVO.Create(
                    TimelineEventType.RcaCompleted,
                    "根因分析完成",
                    fullResponse));

                await notifier.RcaCompletedAsync(incidentId, fullResponse, DateTime.UtcNow, cancellationToken);

                // 6. 触发链路 C — SOP 自动生成（fire-and-forget）
                if (summarizerAgentId.HasValue && summarizerAgentId != Guid.Empty)
                {
                    try
                    {
                        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
                        await sender.Send(new GenerateSopFromIncidentCommand
                        {
                            IncidentId = incidentId,
                            AlertRuleId = incident.AlertRuleId ?? Guid.Empty,
                            SummarizerAgentId = summarizerAgentId,
                            AlertName = alertName,
                            AlertLabels = alertLabels,
                            RootCause = fullResponse
                        }, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "SOP generation (Chain C) failed for Incident {IncidentId}.", incidentId);
                        incident.AddTimelineEvent(IncidentTimelineVO.Create(
                            TimelineEventType.ManualNote,
                            $"SOP 自动生成失败: {ex.Message}"));
                    }
                }
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                incident.AddTimelineEvent(IncidentTimelineVO.Create(
                    TimelineEventType.Timeout,
                    $"根因分析超时 ({RcaTimeout.TotalMinutes} 分钟) — 需人工介入"));

                await notifier.IncidentTimeoutAsync(incidentId,
                    $"根因分析超时 ({RcaTimeout.TotalMinutes} 分钟)", DateTime.UtcNow, cancellationToken);

                logger.LogWarning(
                    "RCA execution timed out for Incident {IncidentId} after {Timeout} minutes.",
                    incidentId, RcaTimeout.TotalMinutes);
            }

            // 保存 Session
            try
            {
                await sessionStore.SaveSessionAsync(aiAgent, conversationId, session, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to save agent session for Incident {IncidentId}.", incidentId);
            }

            await incidentRepo.UpdateAsync(incident, cancellationToken);

            logger.LogInformation(
                "RCA completed for Incident {IncidentId}. Status={Status}, HasRootCause={HasRootCause}",
                incidentId, incident.Status, incident.RootCause is not null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RCA execution failed for Incident {IncidentId}.", incidentId);

            incident.AddTimelineEvent(IncidentTimelineVO.Create(
                TimelineEventType.Timeout,
                $"根因分析异常: {ex.Message}"));
            await incidentRepo.UpdateAsync(incident, cancellationToken);
        }
        finally
        {
            await notifier.AgentProcessingChangedAsync(
                incidentId, false, null, DateTime.UtcNow, cancellationToken);
            sessionTracker.UnregisterActive(incidentId);
        }
    }

    /// <summary>
    /// 将 AlertRule.ContextProviders 和 SOP.ContextInitItems 合并后写入 Session.StateBag，
    /// 供 SopContextInitProvider 在 ProvideAIContextAsync 中读取并预查。
    /// </summary>
    private async Task PopulateContextInitStateBagAsync(
        IServiceScope scope,
        AgentSession session,
        Guid sopId,
        Dictionary<string, string> alertLabels,
        Guid? alertRuleId,
        CancellationToken ct)
    {
        try
        {
            var mergedItems = new List<ContextInitItemVO>();

            // 从 AlertRule 加载 ContextProviders
            if (alertRuleId.HasValue)
            {
                var alertRuleRepo = scope.ServiceProvider.GetRequiredService<IAlertRuleRepository>();
                var alertRule = await alertRuleRepo.GetByIdAsync(alertRuleId.Value, ct);
                if (alertRule?.ContextProviders is { Count: > 0 })
                    mergedItems.AddRange(alertRule.ContextProviders);
            }

            // 从 SOP (SkillRegistration) 加载 ContextInitItems
            var skillRepo = scope.ServiceProvider.GetRequiredService<ISkillRegistrationRepository>();
            var sop = await skillRepo.GetByIdAsync(sopId, ct);
            if (sop?.GetContextInitItems() is { Count: > 0 } sopItems)
                mergedItems.AddRange(sopItems);

            // 设置 StateBag
            if (mergedItems.Count > 0)
            {
                session.StateBag.SetValue(
                    SopContextInitProvider.ContextInitItemsKey,
                    new SopContextInitProvider.ContextInitState { Items = mergedItems });
            }

            session.StateBag.SetValue(
                SopContextInitProvider.AlertLabelsKey,
                new SopContextInitProvider.AlertLabelsState { Labels = alertLabels });

            logger.LogInformation(
                "Populated StateBag with {ItemCount} context init items for SOP {SopId}",
                mergedItems.Count, sopId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to populate StateBag for SOP context init. Continuing without pre-query.");
        }
    }

    /// <summary>
    /// 执行 Agent 对话循环，支持：
    /// - 实时 streaming 推送 (SignalR)
    /// - 工具审批 (Feature A) — Agent 调用工具时暂停等待审批
    /// - 结构化干预请求/响应 (Feature B)
    /// - 真正的暂停/恢复 (Feature C) — TaskCompletionSource 代替 30s 轮询
    /// - 主动人工消息注入 — 人工在两轮之间主动插话
    /// </summary>
    private async Task<string> RunAgentWithInterventionAsync(
        AIAgent aiAgent,
        AgentSession session,
        List<ChatMessage> initialMessages,
        Guid incidentId,
        IIncidentNotifier notifier,
        ChannelReader<ProactiveHumanMessage> proactiveReader,
        CancellationToken cancellationToken)
    {
        var fullResponse = new System.Text.StringBuilder();
        var currentMessages = new List<ChatMessage>(initialMessages);

        // First round: process initial messages
        var roundResponse = await StreamAgentRoundAsync(
            aiAgent, session, currentMessages, incidentId, notifier, cancellationToken);
        fullResponse.Append(roundResponse);

        // Intervention loop: after each agent round, wait briefly for proactive human messages.
        // Structured intervention requests (tool approval etc.) are handled WITHIN StreamAgentRoundAsync.
        var proactiveWaitTime = TimeSpan.FromSeconds(30);

        while (!cancellationToken.IsCancellationRequested)
        {
            ProactiveHumanMessage? proactiveMsg = null;
            try
            {
                using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                waitCts.CancelAfter(proactiveWaitTime);

                if (await proactiveReader.WaitToReadAsync(waitCts.Token))
                {
                    proactiveReader.TryRead(out proactiveMsg);
                }
            }
            catch (OperationCanceledException)
            {
                // Timeout waiting for proactive human input — agent round is fully done
                break;
            }
            catch (ChannelClosedException)
            {
                break;
            }

            if (proactiveMsg is null) break;

            // Push human message to SignalR
            await notifier.ChatMessageReceivedAsync(
                incidentId, "user", proactiveMsg.Content,
                proactiveMsg.OperatorName ?? "操作员", DateTime.UtcNow, cancellationToken);

            await notifier.TimelineEventAddedAsync(
                incidentId, nameof(TimelineEventType.HumanIntervention),
                $"人工介入: {(proactiveMsg.Content.Length > 100 ? proactiveMsg.Content[..100] + "…" : proactiveMsg.Content)}",
                DateTime.UtcNow, ct: cancellationToken);

            logger.LogInformation(
                "Human intervention injected for Incident {IncidentId}: {MessagePreview}",
                incidentId, proactiveMsg.Content.Length > 80 ? proactiveMsg.Content[..80] + "…" : proactiveMsg.Content);

            // Acknowledge intervention
            await notifier.HumanInterventionAcknowledgedAsync(
                incidentId, DateTime.UtcNow, cancellationToken);

            // Send intervention as new user message to agent
            var interventionMessages = new List<ChatMessage>
            {
                new(ChatRole.User, proactiveMsg.Content)
            };

            roundResponse = await StreamAgentRoundAsync(
                aiAgent, session, interventionMessages, incidentId, notifier, cancellationToken);
            fullResponse.AppendLine().Append(roundResponse);
        }

        return fullResponse.ToString();
    }

    /// <summary>
    /// Execute a single round of agent streaming, pushing each chunk to SignalR in real-time.
    /// Intercepts FunctionCallContent for tool approval (Feature A):
    /// - Creates structured InterventionRequest(ToolApproval)
    /// - Pushes to SignalR for frontend to render approve/reject UI
    /// - Awaits human response via TaskCompletionSource (Feature C — true pause)
    /// </summary>
    private async Task<string> StreamAgentRoundAsync(
        AIAgent aiAgent,
        AgentSession session,
        List<ChatMessage> messages,
        Guid incidentId,
        IIncidentNotifier notifier,
        CancellationToken cancellationToken)
    {
        var roundResponse = new System.Text.StringBuilder();
        string? currentAgentName = null;

        await foreach (var update in aiAgent.RunStreamingAsync(messages, session, cancellationToken: cancellationToken))
        {
            foreach (var content in update.Contents)
            {
                if (content is TextContent textContent && !string.IsNullOrEmpty(textContent.Text))
                {
                    roundResponse.Append(textContent.Text);

                    // Push each text chunk to SignalR in real-time
                    await notifier.ChatMessageReceivedAsync(
                        incidentId, "assistant", textContent.Text,
                        currentAgentName, DateTime.UtcNow, cancellationToken);
                }
                else if (content is FunctionCallContent functionCall)
                {
                    // ── Feature A: Tool Approval ──
                    // Create a structured intervention request for this tool call.
                    // The agent's tool execution may have already proceeded in the framework,
                    // but this signals the frontend and records the event.
                    var requestId = $"tool-{incidentId:N}-{Guid.NewGuid():N}"[..32];

                    var approvalRequest = new InterventionRequest(
                        RequestId: requestId,
                        IncidentId: incidentId,
                        Type: InterventionRequestType.ToolApproval,
                        Prompt: $"Agent 请求执行工具: {functionCall.Name}",
                        CreatedAt: DateTime.UtcNow,
                        ToolApproval: new ToolApprovalData(
                            ToolName: functionCall.Name,
                            CallId: functionCall.CallId,
                            Arguments: functionCall.Arguments?.ToDictionary(k => k.Key, v => v.Value)));

                    // Push tool call timeline event
                    await notifier.TimelineEventAddedAsync(
                        incidentId,
                        nameof(TimelineEventType.ToolApprovalRequested),
                        $"工具审批请求: {functionCall.Name}",
                        DateTime.UtcNow,
                        metadata: new Dictionary<string, string>
                        {
                            ["requestId"] = requestId,
                            ["toolName"] = functionCall.Name,
                            ["callId"] = functionCall.CallId
                        },
                        ct: cancellationToken);

                    // Push structured intervention request to SignalR
                    await notifier.InterventionRequestReceivedAsync(
                        incidentId, requestId,
                        nameof(InterventionRequestType.ToolApproval),
                        approvalRequest.Prompt, approvalRequest.CreatedAt,
                        toolName: functionCall.Name,
                        toolCallId: functionCall.CallId,
                        toolArguments: functionCall.Arguments?.ToDictionary(k => k.Key, v => v.Value),
                        ct: cancellationToken);

                    // ── Feature C: True pause via TaskCompletionSource ──
                    // Register and await approval. This blocks until human responds.
                    try
                    {
                        var response = await sessionTracker.RequestInterventionAsync(
                            approvalRequest, cancellationToken);

                        // Record approval result
                        var approved = response.Type == InterventionResponseType.Approved;
                        await notifier.TimelineEventAddedAsync(
                            incidentId,
                            nameof(TimelineEventType.ToolApprovalResponded),
                            approved
                                ? $"✅ 工具 {functionCall.Name} 已批准"
                                : $"❌ 工具 {functionCall.Name} 已拒绝",
                            DateTime.UtcNow,
                            metadata: new Dictionary<string, string>
                            {
                                ["requestId"] = requestId,
                                ["approved"] = approved.ToString(),
                                ["operatorName"] = response.OperatorName ?? ""
                            },
                            ct: cancellationToken);

                        // Notify frontend to clear the pending request
                        await notifier.InterventionRequestResolvedAsync(
                            incidentId, requestId,
                            response.Type.ToString(),
                            approved: approved,
                            operatorName: response.OperatorName,
                            timestamp: DateTime.UtcNow,
                            ct: cancellationToken);

                        logger.LogInformation(
                            "Tool {ToolName} {Result} for Incident {IncidentId} by {Operator}",
                            functionCall.Name,
                            approved ? "approved" : "rejected",
                            incidentId,
                            response.OperatorName ?? "unknown");
                    }
                    catch (OperationCanceledException)
                    {
                        logger.LogWarning(
                            "Tool approval request {RequestId} for {ToolName} was cancelled (incident {IncidentId}).",
                            requestId, functionCall.Name, incidentId);
                    }
                }
            }

            // Detect agent name from streaming metadata if available
            if (update.AdditionalProperties?.TryGetValue("agentName", out var agentName) == true
                && agentName is string name)
            {
                currentAgentName = name;
            }
        }

        return roundResponse.ToString();
    }
}
