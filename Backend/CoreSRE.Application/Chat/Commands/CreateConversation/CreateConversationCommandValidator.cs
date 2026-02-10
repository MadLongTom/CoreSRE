using FluentValidation;

namespace CoreSRE.Application.Chat.Commands.CreateConversation;

/// <summary>
/// 创建对话命令验证器
/// </summary>
public class CreateConversationCommandValidator : AbstractValidator<CreateConversationCommand>
{
    public CreateConversationCommandValidator()
    {
        RuleFor(x => x.AgentId)
            .NotEmpty().WithMessage("AgentId is required.")
            .NotEqual(Guid.Empty).WithMessage("AgentId must not be empty.");
    }
}
