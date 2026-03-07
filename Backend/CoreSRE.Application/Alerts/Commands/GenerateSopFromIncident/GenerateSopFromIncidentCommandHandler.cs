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
/// 3. 解析 → 创建 SkillRegistration（状态为 Draft，不立即绑定 AlertRule）
/// 4. 执行结构化校验
/// 5. 更新 Incident（GeneratedSopId）
/// </summary>
public class GenerateSopFromIncidentCommandHandler(
    IIncidentRepository incidentRepository,
    IAlertRuleRepository alertRuleRepository,
    ISkillRegistrationRepository skillRepository,
    IToolRegistrationRepository toolRepository,
    IAgentCaller agentCaller,
    ISopParserService sopParser,
    ISopValidator sopValidator,
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

        // 4. 版本管理：检查是否已存在同 AlertRule 的 SOP
        var (existingSkills, _) = await skillRepository.GetPagedAsync(
            scope: null, status: null, category: "sop", search: null,
            page: 1, pageSize: 100, cancellationToken);
        var existingSops = existingSkills
            .Where(s => s.SourceAlertRuleId == request.AlertRuleId)
            .OrderByDescending(s => s.Version)
            .ToList();
        var nextVersion = existingSops.Count > 0 ? existingSops[0].Version + 1 : 1;

        // 5. 创建 SkillRegistration（Draft 状态，不立即绑定 AlertRule）
        var skill = SkillRegistration.CreateSop(
            name: parseResult.Name.Length > 64 ? parseResult.Name[..64] : parseResult.Name,
            description: parseResult.Description,
            content: parseResult.Content,
            sourceIncidentId: request.IncidentId,
            sourceAlertRuleId: request.AlertRuleId,
            version: nextVersion);

        await skillRepository.AddAsync(skill, cancellationToken);

        // 6. 执行结构化校验
        var allTools = await toolRepository.GetByTypeAsync(null, cancellationToken);
        var toolNames = allTools.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var validationResult = sopValidator.Validate(skill.Content, toolNames);
        skill.SetValidationResult(validationResult);
        await skillRepository.UpdateAsync(skill, cancellationToken);

        // 7. 更新 Incident（不再更新 AlertRule — 等 Publish 时才绑定）
        incident.SetGeneratedSop(skill.Id);
        incident.AddTimelineEvent(IncidentTimelineVO.Create(
            TimelineEventType.SopGenerated,
            $"SOP 已自动生成 (v{nextVersion}, 状态: Draft): {parseResult.Name}",
            $"校验结果: {(validationResult.IsValid ? "通过" : "未通过")}; 工具依赖: {string.Join(", ", parseResult.ReferencedToolNames)}"));

        await incidentRepository.UpdateAsync(incident, cancellationToken);

        logger.LogInformation(
            "SOP generated for Incident {IncidentId}: Skill={SkillName}, Tools={ToolCount}",
            request.IncidentId, parseResult.Name, parseResult.ReferencedToolNames.Count);

        return Result<Guid?>.Ok(skill.Id);
    }
}
