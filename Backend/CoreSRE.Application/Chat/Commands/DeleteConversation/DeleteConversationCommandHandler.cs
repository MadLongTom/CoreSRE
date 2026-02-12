using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Chat.Commands.DeleteConversation;

/// <summary>
/// 删除对话命令处理器 — 删除 Conversation 实体 + 关联 AgentSessionRecord（如存在）。
/// AgentSessionRecord 使用 (agentId, conversationId) 复合主键。
/// </summary>
public class DeleteConversationCommandHandler : IRequestHandler<DeleteConversationCommand, Result<bool>>
{
    private readonly IConversationRepository _conversationRepository;
    private readonly IChatHistoryReader _chatHistoryReader;

    public DeleteConversationCommandHandler(
        IConversationRepository conversationRepository,
        IChatHistoryReader chatHistoryReader)
    {
        _conversationRepository = conversationRepository;
        _chatHistoryReader = chatHistoryReader;
    }

    public async Task<Result<bool>> Handle(
        DeleteConversationCommand request,
        CancellationToken cancellationToken)
    {
        var conversation = await _conversationRepository.GetByIdAsync(request.Id, cancellationToken);
        if (conversation is null)
            return Result<bool>.NotFound($"Conversation with ID '{request.Id}' not found.");

        // 解析 Agent ID 用于查找 AgentSessionRecord
        var agentId = conversation.AgentId.ToString();

        // 删除关联的 AgentSessionRecord（如果存在）
        await _chatHistoryReader.DeleteSessionAsync(
            agentId, conversation.Id.ToString(), cancellationToken);

        // 删除 Conversation 实体
        await _conversationRepository.DeleteAsync(conversation.Id, cancellationToken);

        return Result<bool>.Ok(true);
    }
}
