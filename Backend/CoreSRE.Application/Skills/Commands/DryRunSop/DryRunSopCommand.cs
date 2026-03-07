using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Skills.DTOs;
using MediatR;

namespace CoreSRE.Application.Skills.Commands.DryRunSop;

/// <summary>
/// 对 SOP 执行干运行（Mock 数据源，不产生副作用）
/// </summary>
public record DryRunSopCommand(Guid SkillId) : IRequest<Result<DryRunResultDto>>;
