using CoreSRE.Application.Alerts.Interfaces;
using CoreSRE.Application.Alerts.Services;
using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using CoreSRE.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CoreSRE.Application.Alerts.Commands.GenerateSopFromIncident;

/// <summary>
/// 链路 C Handler：从 RCA 结果自动生成 SOP。
/// 1. 构造 SOP 生成 Prompt
/// 2. 调用总结 Agent 生成 SOP Markdown
/// 3. 解析 → 创建 SkillRegistration
/// 4. 更新 AlertRule（SopId）
/// 5. 更新 Incident（GeneratedSopId）
/// </summary>
public class GenerateSopFromIncidentCommandHandler(
    IIncidentRepository incidentRepository,
    IAlertRuleRepository alertRuleRepository,
    ISkillRegistrationRepository skillRepository,
    IAgentCaller agentCaller,
    ISopParserService sopParser,
    ILogger<GenerateSopFromIncidentCommandHandler> logger)
    : IRequestHandler<GenerateSopFromIncidentCommand, Result<Guid?>>
{
    public async Task<Result<Guid?>> Handle(
        GenerateSopFromIncidentCommand request,
        CancellationToken cancellationToken)
    {
        // 0. 前置检查
        if (request.SummarizerAgentId is null || request.SummarizerAgentId == Guid.Empty)
        {
            logger.LogInformation(
                "No SummarizerAgentId for Incident {IncidentId}. Skipping SOP generation.",
                request.IncidentId);
            return Result<Guid?>.Ok(null);
        }

        var incident = await incidentRepository.GetByIdAsync(request.IncidentId, cancellationToken);
        if (incident is null)
            return Result<Guid?>.NotFound($"Incident '{request.IncidentId}' not found.");

        var alertRule = await alertRuleRepository.GetByIdAsync(request.AlertRuleId, cancellationToken);
        if (alertRule is null)
            return Result<Guid?>.NotFound($"AlertRule '{request.AlertRuleId}' not found.");

        // 1. 构造 SOP 生成 Prompt
        var conversationHistory = $"(来自 Incident {request.IncidentId}, Conversation {incident.ConversationId} 的团队对话)";
        var prompt = SopGenerationPromptBuilder.Build(
            request.AlertName,
            request.AlertLabels,
            request.RootCause,
            conversationHistory);

        // 2. 调用总结 Agent
        var sopConversationId = Guid.NewGuid().ToString();
        string sopMarkdown;

        try
        {
            sopMarkdown = await agentCaller.SendMessageAsync(
                request.SummarizerAgentId.Value,
                sopConversationId,
                prompt,
                timeout: TimeSpan.FromMinutes(10),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("SOP generation timed out for Incident {IncidentId}.", request.IncidentId);
            sopMarkdown = string.Empty;
        }

        if (string.IsNullOrWhiteSpace(sopMarkdown))
        {
            logger.LogWarning("SOP generation returned empty result for Incident {IncidentId}.", request.IncidentId);
            return Result<Guid?>.Ok(null);
        }

        // 3. 解析 SOP
        var parseResult = sopParser.Parse(sopMarkdown, request.AlertName);

        // 4. 创建 SkillRegistration
        var skill = SkillRegistration.Create(
            name: parseResult.Name.Length > 64 ? parseResult.Name[..64] : parseResult.Name,
            description: parseResult.Description,
            category: "sop",
            content: parseResult.Content);

        await skillRepository.AddAsync(skill, cancellationToken);

        // 5. 更新 AlertRule（SopId — ResponderAgentId 暂留空，待自动创建 Agent 后填充）
        alertRule.BindSop(skill.Id, Guid.Empty);

        // 6. 更新 Incident
        incident.SetGeneratedSop(skill.Id);
        incident.AddTimelineEvent(IncidentTimelineVO.Create(
            TimelineEventType.SopGenerated,
            $"SOP 已自动生成: {parseResult.Name}",
            $"工具依赖: {string.Join(", ", parseResult.ReferencedToolNames)}"));

        await incidentRepository.UpdateAsync(incident, cancellationToken);
        await alertRuleRepository.UpdateAsync(alertRule, cancellationToken);

        logger.LogInformation(
            "SOP generated for Incident {IncidentId}: Skill={SkillName}, Tools={ToolCount}",
            request.IncidentId, parseResult.Name, parseResult.ReferencedToolNames.Count);

        return Result<Guid?>.Ok(skill.Id);
    }
}
