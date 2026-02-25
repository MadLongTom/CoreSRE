using CoreSRE.Application.Alerts.Commands.GenerateSopFromIncident;
using CoreSRE.Application.Alerts.Interfaces;
using CoreSRE.Application.Alerts.Services;
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

namespace CoreSRE.Infrastructure.Services;

/// <summary>
/// Incident 后台处置派发器。
/// 接收 Incident ID 后在后台执行 Agent 对话（SOP / RCA）。
/// </summary>
public class IncidentDispatcherService(
    IServiceScopeFactory scopeFactory,
    AgentSessionStore sessionStore,
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

        var incident = await incidentRepo.GetByIdAsync(incidentId, cancellationToken);
        if (incident is null)
        {
            logger.LogError("Incident {IncidentId} not found during SOP dispatch.", incidentId);
            return;
        }

        var conversationId = incident.ConversationId?.ToString() ?? Guid.NewGuid().ToString();

        try
        {
            // 1. 解析 Agent
            var resolved = await agentResolver.ResolveAsync(agentId, conversationId, cancellationToken);
            var aiAgent = resolved.Agent;

            // 2. 构造首条消息
            var userMessage = SopMessageTemplates.BuildSopExecutionMessage(
                alertName, alertLabels, alertAnnotations);

            var messages = new List<ChatMessage>
            {
                new(ChatRole.User, userMessage)
            };

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

            // 4. 带超时执行 Agent
            using var timeoutCts = new CancellationTokenSource(SopTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutCts.Token);

            var fullResponse = new System.Text.StringBuilder();

            try
            {
                await foreach (var update in aiAgent.RunStreamingAsync(messages, session, cancellationToken: linkedCts.Token))
                {
                    foreach (var content in update.Contents)
                    {
                        if (content is TextContent textContent && !string.IsNullOrEmpty(textContent.Text))
                        {
                            fullResponse.Append(textContent.Text);
                        }
                    }
                }

                // 5. 执行成功 → 更新 Incident
                incident.Resolve(fullResponse.ToString());
                incident.SetTimeToDetect(incident.StartedAt); // MTTD = 0（自动触发）
                incident.AddTimelineEvent(IncidentTimelineVO.Create(
                    TimelineEventType.Resolved,
                    "SOP 自动执行完成",
                    fullResponse.ToString()));
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                // 超时 → 保持 Investigating，需人工介入
                incident.AddTimelineEvent(IncidentTimelineVO.Create(
                    TimelineEventType.Timeout,
                    $"SOP 执行超时 ({SopTimeout.TotalMinutes} 分钟) — 需人工介入",
                    fullResponse.ToString()));

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

        var incident = await incidentRepo.GetByIdAsync(incidentId, cancellationToken);
        if (incident is null)
        {
            logger.LogError("Incident {IncidentId} not found during RCA dispatch.", incidentId);
            return;
        }

        var conversationId = incident.ConversationId?.ToString() ?? Guid.NewGuid().ToString();

        try
        {
            // 1. 解析 Team Agent
            var resolved = await agentResolver.ResolveAsync(teamAgentId, conversationId, cancellationToken);
            var aiAgent = resolved.Agent;

            // 2. 构造首条消息
            var userMessage = RcaMessageTemplates.BuildRootCauseAnalysisMessage(
                alertName, alertLabels, alertAnnotations);

            var messages = new List<ChatMessage>
            {
                new(ChatRole.User, userMessage)
            };

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

            // 4. 带超时执行 Team Agent
            using var timeoutCts = new CancellationTokenSource(RcaTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutCts.Token);

            var fullResponse = new System.Text.StringBuilder();

            try
            {
                await foreach (var update in aiAgent.RunStreamingAsync(messages, session, cancellationToken: linkedCts.Token))
                {
                    foreach (var content in update.Contents)
                    {
                        if (content is TextContent textContent && !string.IsNullOrEmpty(textContent.Text))
                        {
                            fullResponse.Append(textContent.Text);
                        }
                    }
                }

                // 5. 提取根因
                var rootCause = fullResponse.ToString();
                incident.SetRootCause(rootCause);
                incident.TransitionTo(IncidentStatus.Mitigated);
                incident.AddTimelineEvent(IncidentTimelineVO.Create(
                    TimelineEventType.RcaCompleted,
                    "根因分析完成",
                    rootCause));

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
                            RootCause = rootCause
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
                    $"根因分析超时 ({RcaTimeout.TotalMinutes} 分钟) — 需人工介入",
                    fullResponse.ToString()));

                logger.LogWarning(
                    "RCA execution timed out for Incident {IncidentId} after {Timeout} minutes.",
                    incidentId, RcaTimeout.TotalMinutes);
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
    }
}
