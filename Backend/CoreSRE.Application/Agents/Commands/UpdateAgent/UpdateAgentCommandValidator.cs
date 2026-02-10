using FluentValidation;

namespace CoreSRE.Application.Agents.Commands.UpdateAgent;

/// <summary>
/// 更新 Agent 命令验证器。
/// 校验请求结构（名称必填/最大长度），类型特定校验在 Handler 中基于实体类型执行。
/// </summary>
public class UpdateAgentCommandValidator : AbstractValidator<UpdateAgentCommand>
{
    public UpdateAgentCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Agent ID is required.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(200).WithMessage("Name must not exceed 200 characters.");
    }
}
