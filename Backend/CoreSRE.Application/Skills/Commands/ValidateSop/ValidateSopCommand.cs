using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.ValueObjects;
using MediatR;

namespace CoreSRE.Application.Skills.Commands.ValidateSop;

/// <summary>对 SOP 执行结构化校验</summary>
public record ValidateSopCommand(Guid SkillId) : IRequest<Result<SopValidationResultVO>>;
