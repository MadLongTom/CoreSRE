using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Chat.Commands.TouchConversation;

/// <summary>
/// 触碰对话命令处理器 — 更新 UpdatedAt，首次设置 Title
/// </summary>
public class TouchConversationCommandHandler : IRequestHandler<TouchConversationCommand, Result<bool>>
{
    private readonly IConversationRepository _repository;

    public TouchConversationCommandHandler(IConversationRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<bool>> Handle(
        TouchConversationCommand request,
        CancellationToken cancellationToken)
    {
        var conversation = await _repository.GetByIdAsync(request.ConversationId, cancellationToken);
        if (conversation is null)
            return Result<bool>.NotFound($"Conversation with ID '{request.ConversationId}' not found.");

        // 首次消息时自动设置标题（截取前 50 字符）
        if (request.FirstMessage is not null)
        {
            var title = request.FirstMessage.Length > 50
                ? request.FirstMessage[..50]
                : request.FirstMessage;
            conversation.SetTitle(title);
        }

        conversation.Touch();
        await _repository.UpdateAsync(conversation, cancellationToken);

        return Result<bool>.Ok(true);
    }
}
