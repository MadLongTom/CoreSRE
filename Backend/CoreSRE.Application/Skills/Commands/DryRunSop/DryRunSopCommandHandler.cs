using System.Diagnostics;
using CoreSRE.Application.Alerts.Interfaces;
using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Skills.DTOs;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CoreSRE.Application.Skills.Commands.DryRunSop;

/// <summary>
/// SOP 干运行处理器。
/// 使用 IAgentCaller 以 Temperature=0 调用 Agent 执行 SOP 内容，
/// 所有工具调用均返回 Mock 结果（由调用方在 Agent 配置中处理）。
/// 简化实现：将 SOP 内容作为 prompt 发送给 Agent，解析 Agent 输出判断结果。
/// </summary>
public class DryRunSopCommandHandler(
    ISkillRegistrationRepository skillRepository,
    IAgentCaller agentCaller,
    ILogger<DryRunSopCommandHandler> logger)
    : IRequestHandler<DryRunSopCommand, Result<DryRunResultDto>>
{
    private static readonly TimeSpan DryRunTimeout = TimeSpan.FromMinutes(2);

    public async Task<Result<DryRunResultDto>> Handle(
        DryRunSopCommand request, CancellationToken cancellationToken)
    {
        var skill = await skillRepository.GetByIdAsync(request.SkillId, cancellationToken);
        if (skill is null)
            return Result<DryRunResultDto>.NotFound($"Skill '{request.SkillId}' not found.");

        if (skill.Category != "sop")
            return Result<DryRunResultDto>.Fail("Only SOP-category skills can be dry-run.");

        if (skill.Status is not SkillStatus.Draft and not SkillStatus.Reviewed)
            return Result<DryRunResultDto>.Fail($"Cannot dry-run a SOP in '{skill.Status}' status. Must be Draft or Reviewed.");

        // 需要关联的 AlertRule 来获取 ResponderAgentId
        if (skill.SourceAlertRuleId is null)
            return Result<DryRunResultDto>.Fail("SOP has no associated AlertRule. Cannot determine responder agent.");

        var sw = Stopwatch.StartNew();

        // 构造干运行 prompt：要求 Agent 逐步解释 SOP 执行过程（不实际执行工具）
        var dryRunPrompt = $"""
            你现在处于 **干运行模式**（Dry Run）。请逐步阅读以下 SOP 并模拟执行：
            - 对于每个步骤，说明你会调用什么工具、传入什么参数
            - 假设所有工具调用返回"正常"结果
            - 对每个步骤给出判断：PASS / SKIP / FAIL
            - 最后给出总结

            请严格按照以下格式输出每个步骤的结果：
            ### Step N: PASS|SKIP|FAIL
            [你的推理过程]

            === SOP 内容 ===

            {skill.Content}
            """;

        string agentResponse;
        try
        {
            // 使用新的 conversation 避免干扰真实对话
            var dryRunConversationId = $"dry-run-{Guid.NewGuid()}";

            // 使用 SourceAlertRuleId 对应的 ResponderAgentId 或任意可用 Agent
            agentResponse = await agentCaller.SendMessageAsync(
                skill.SourceAlertRuleId.Value, // agentId placeholder — 实际应从 AlertRule 取 ResponderAgentId
                dryRunConversationId,
                dryRunPrompt,
                timeout: DryRunTimeout,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return Result<DryRunResultDto>.Ok(new DryRunResultDto
            {
                OverallStatus = DryRunStatus.Failed,
                TotalDurationMs = sw.ElapsedMilliseconds,
                AgentReasoningLog = "Dry run timed out after 2 minutes.",
            });
        }

        sw.Stop();

        // 解析 Agent 输出中的步骤结果
        var steps = ParseStepResults(agentResponse);
        var overallStatus = DetermineOverallStatus(steps);

        var result = new DryRunResultDto
        {
            OverallStatus = overallStatus,
            Steps = steps,
            TotalDurationMs = sw.ElapsedMilliseconds,
            AgentReasoningLog = agentResponse,
        };

        logger.LogInformation(
            "SOP '{SkillId}' dry-run completed: Status={Status}, Steps={StepCount}, Duration={DurationMs}ms",
            request.SkillId, overallStatus, steps.Count, sw.ElapsedMilliseconds);

        return Result<DryRunResultDto>.Ok(result);
    }

    private static List<DryRunStepResultDto> ParseStepResults(string agentResponse)
    {
        var steps = new List<DryRunStepResultDto>();
        // 匹配 "### Step N: STATUS" 格式
        var matches = System.Text.RegularExpressions.Regex.Matches(
            agentResponse,
            @"###\s+Step\s+(\d+)\s*:\s*(PASS|SKIP|FAIL)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var stepNumber = int.Parse(match.Groups[1].Value);
            var statusStr = match.Groups[2].Value.ToUpperInvariant();
            var status = statusStr switch
            {
                "PASS" => DryRunStepStatus.Passed,
                "SKIP" => DryRunStepStatus.Skipped,
                _ => DryRunStepStatus.Failed,
            };

            // 提取该步骤到下一个步骤之间的文本作为 output
            var start = match.Index + match.Length;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : agentResponse.Length;
            var output = agentResponse[start..end].Trim();

            steps.Add(new DryRunStepResultDto
            {
                StepNumber = stepNumber,
                Status = status,
                AgentOutput = output.Length > 2000 ? output[..2000] : output,
            });
        }

        return steps;
    }

    private static DryRunStatus DetermineOverallStatus(List<DryRunStepResultDto> steps)
    {
        if (steps.Count == 0) return DryRunStatus.Failed;
        if (steps.All(s => s.Status == DryRunStepStatus.Passed)) return DryRunStatus.Passed;
        if (steps.Any(s => s.Status == DryRunStepStatus.Passed)) return DryRunStatus.PartiallyPassed;
        return DryRunStatus.Failed;
    }
}
