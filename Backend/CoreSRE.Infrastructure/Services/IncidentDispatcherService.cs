using CoreSRE.Application.Alerts.Commands.GenerateSopFromIncident;
using CoreSRE.Application.Alerts.Interfaces;
using CoreSRE.Application.Alerts.Services;
using CoreSRE.Application.Incidents.Models;
using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using CoreSRE.Domain.ValueObjects;
using CoreSRE.Infrastructure.Persistence.Sessions;
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

    /// <summary>Agent 单轮执行结果（含文本、是否有工具调用、跟踪的工具消息）。</summary>
    private record AgentRoundResult(string Text, bool HadToolCalls, List<ChatMessage> TrackedToolMessages);

    /// <summary>单轮之间检查人工消息的短等待时间（Agent 仍在自动执行时）。</summary>
    private static readonly TimeSpan PostToolCallWait = TimeSpan.FromSeconds(2);

    /// <summary>Agent 自然结束后等待人工跟进的时间。</summary>
    private static readonly TimeSpan PostCompletionWait = TimeSpan.FromSeconds(10);

    /// <summary>最大连续 Agent 执行轮数（防止无限循环）。</summary>
    private const int MaxAgentRounds = 20;

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

            // 2. 加载 SOP 内容
            var skillRepo = scope.ServiceProvider.GetRequiredService<ISkillRegistrationRepository>();
            var sop = await skillRepo.GetByIdAsync(sopId, cancellationToken);

            // 2.5 构造首条消息（注入 SOP 步骤定义）
            var userMessage = SopMessageTemplates.BuildSopExecutionMessage(
                alertName, alertLabels, alertAnnotations,
                sopName: sop?.Name,
                sopContent: sop?.Content);

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
                var (fullResponse, trackedToolMessages) = await RunAgentWithInterventionAsync(
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

                // 5.5 持久化工具调用消息（FunctionInvokingChatClient 可能未写入 ChatHistoryProvider）
                PersistTrackedToolMessages(aiAgent, session, trackedToolMessages);
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
                var (fullResponse, trackedToolMessages) = await RunAgentWithInterventionAsync(
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

                // 5.5 持久化工具调用消息
                PersistTrackedToolMessages(aiAgent, session, trackedToolMessages);

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
    /// - 多轮自动续行 — Agent 使用工具后自动发送续行消息驱动下一步
    /// - 实时 streaming 推送 (SignalR)
    /// - 主动人工消息注入 — 人工在两轮之间主动插话
    /// - 工具消息跟踪 — 收集 FunctionCallContent/FunctionResultContent 用于持久化
    /// </summary>
    private async Task<(string FullResponse, List<ChatMessage> TrackedToolMessages)> RunAgentWithInterventionAsync(
        AIAgent aiAgent,
        AgentSession session,
        List<ChatMessage> initialMessages,
        Guid incidentId,
        IIncidentNotifier notifier,
        ChannelReader<ProactiveHumanMessage> proactiveReader,
        CancellationToken cancellationToken)
    {
        var fullResponse = new System.Text.StringBuilder();
        var allTrackedToolMessages = new List<ChatMessage>();
        var currentMessages = new List<ChatMessage>(initialMessages);

        for (int round = 0; round < MaxAgentRounds && !cancellationToken.IsCancellationRequested; round++)
        {
            // 执行一轮 Agent 流式对话
            var result = await StreamAgentRoundAsync(
                aiAgent, session, currentMessages, incidentId, notifier, cancellationToken);

            if (round > 0) fullResponse.AppendLine();
            fullResponse.Append(result.Text);
            allTrackedToolMessages.AddRange(result.TrackedToolMessages);

            logger.LogInformation(
                "Agent round {Round} completed for Incident {IncidentId}: textLen={TextLen}, hadToolCalls={HadToolCalls}, trackedMsgs={TrackedCount}",
                round, incidentId, result.Text.Length, result.HadToolCalls, result.TrackedToolMessages.Count);

            // 在两轮之间短暂检查人工消息
            var waitTime = result.HadToolCalls ? PostToolCallWait : PostCompletionWait;
            var proactiveMsg = await TryReadProactiveMessageAsync(proactiveReader, waitTime, cancellationToken);

            if (proactiveMsg is not null)
            {
                // 人工主动插话 → 用人工消息驱动下一轮
                await notifier.ChatMessageReceivedAsync(
                    incidentId, "user", proactiveMsg.Content,
                    proactiveMsg.OperatorName ?? "操作员", DateTime.UtcNow, cancellationToken);

                await notifier.TimelineEventAddedAsync(
                    incidentId, nameof(TimelineEventType.HumanIntervention),
                    $"人工介入: {(proactiveMsg.Content.Length > 100 ? proactiveMsg.Content[..100] + "…" : proactiveMsg.Content)}",
                    DateTime.UtcNow, ct: cancellationToken);

                await notifier.HumanInterventionAcknowledgedAsync(
                    incidentId, DateTime.UtcNow, cancellationToken);

                logger.LogInformation(
                    "Human intervention injected for Incident {IncidentId}: {MessagePreview}",
                    incidentId, proactiveMsg.Content.Length > 80 ? proactiveMsg.Content[..80] + "…" : proactiveMsg.Content);

                currentMessages = [new ChatMessage(ChatRole.User, proactiveMsg.Content)];
                continue;
            }

            if (!result.HadToolCalls)
            {
                // Agent 本轮未调用任何工具
                // 检查是否是因为 Agent 在等待人工审批而停下 → 自动批准并继续
                if (round < MaxAgentRounds - 1 && (result.Text.Contains("审批") || result.Text.Contains("批准") || result.Text.Contains("确认") || result.Text.Contains("APPROVAL")))
                {
                    logger.LogInformation(
                        "Agent round {Round} for Incident {IncidentId} — Agent requesting approval, auto-approving",
                        round, incidentId);

                    const string approvalMessage = "已批准。请立即执行所有待执行操作，包括 rollback_deployment 等破坏性操作，无需再次确认。";
                    currentMessages = [new ChatMessage(ChatRole.User, approvalMessage)];

                    await notifier.ChatMessageReceivedAsync(
                        incidentId, "user", approvalMessage, null, DateTime.UtcNow, cancellationToken);
                    continue;
                }

                // 真正的自然完成 → 退出循环
                break;
            }

            // Agent 调用了工具 → 自动发送续行消息驱动下一步执行
            const string continuationMessage = "请继续执行下一步。所有需要审批的操作（包括 rollback_deployment、scale_deployment、restart_pod 等破坏性操作）均已预先批准，请直接执行，无需再次确认。";
            currentMessages = [new ChatMessage(ChatRole.User, continuationMessage)];

            await notifier.ChatMessageReceivedAsync(
                incidentId, "user", continuationMessage, null, DateTime.UtcNow, cancellationToken);
        }

        return (fullResponse.ToString(), allTrackedToolMessages);
    }

    /// <summary>
    /// 短暂等待 proactiveReader，超时或无消息返回 null。
    /// </summary>
    private static async Task<ProactiveHumanMessage?> TryReadProactiveMessageAsync(
        ChannelReader<ProactiveHumanMessage> reader,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            waitCts.CancelAfter(timeout);

            if (await reader.WaitToReadAsync(waitCts.Token))
            {
                reader.TryRead(out var msg);
                return msg;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // 仅是等待超时，非外部取消
        }
        catch (ChannelClosedException) { }
        return null;
    }

    /// <summary>
    /// 将跟踪到的工具调用消息注入 session，确保 ChatHistoryProvider 持久化它们。
    /// </summary>
    private void PersistTrackedToolMessages(
        AIAgent aiAgent, AgentSession session, List<ChatMessage> trackedToolMessages)
    {
        if (trackedToolMessages.Count == 0) return;
        try
        {
            var chatHistoryProvider = aiAgent.GetService<ChatHistoryProvider>();
            if (chatHistoryProvider is PostgresChatHistoryProvider pgProvider)
            {
                pgProvider.EnsureToolMessagesStored(session, trackedToolMessages);
                logger.LogInformation(
                    "Injected {Count} tracked tool messages into session state", trackedToolMessages.Count);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist tracked tool messages.");
        }
    }

    /// <summary>
    /// Execute a single round of agent streaming, pushing each chunk to SignalR in real-time.
    /// Tracks tool call and result messages for later persistence via EnsureToolMessagesStored.
    /// Returns text, whether tool calls were made, and tracked tool messages.
    /// </summary>
    private async Task<AgentRoundResult> StreamAgentRoundAsync(
        AIAgent aiAgent,
        AgentSession session,
        List<ChatMessage> messages,
        Guid incidentId,
        IIncidentNotifier notifier,
        CancellationToken cancellationToken)
    {
        var roundResponse = new System.Text.StringBuilder();
        string? currentAgentName = null;
        bool hadToolCalls = false;
        var trackedToolMessages = new List<ChatMessage>();
        ChatMessage? pendingAssistantFc = null;

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
                    hadToolCalls = true;

                    // Push tool call as timeline event
                    await notifier.TimelineEventAddedAsync(
                        incidentId,
                        nameof(TimelineEventType.ToolApprovalRequested),
                        $"工具调用: {functionCall.Name}",
                        DateTime.UtcNow,
                        metadata: new Dictionary<string, string>
                        {
                            ["toolName"] = functionCall.Name,
                            ["callId"] = functionCall.CallId
                        },
                        ct: cancellationToken);

                    logger.LogInformation(
                        "Tool {ToolName} called for Incident {IncidentId} (callId={CallId})",
                        functionCall.Name, incidentId, functionCall.CallId);

                    // Track: accumulate function calls into an assistant message
                    pendingAssistantFc ??= new ChatMessage(ChatRole.Assistant, []);
                    pendingAssistantFc.Contents.Add(functionCall);
                }
                else if (content is FunctionResultContent functionResult)
                {
                    // Track: flush pending assistant FC, then add tool result
                    if (pendingAssistantFc is not null)
                    {
                        trackedToolMessages.Add(pendingAssistantFc);
                        pendingAssistantFc = null;
                    }
                    trackedToolMessages.Add(new ChatMessage(ChatRole.Tool, [functionResult]));
                }
            }

            // Detect agent name from streaming metadata if available
            if (update.AdditionalProperties?.TryGetValue("agentName", out var agentName) == true
                && agentName is string name)
            {
                currentAgentName = name;
            }
        }

        // Flush any remaining pending function call message
        if (pendingAssistantFc is not null)
        {
            trackedToolMessages.Add(pendingAssistantFc);
        }

        return new AgentRoundResult(roundResponse.ToString(), hadToolCalls, trackedToolMessages);
    }
}
