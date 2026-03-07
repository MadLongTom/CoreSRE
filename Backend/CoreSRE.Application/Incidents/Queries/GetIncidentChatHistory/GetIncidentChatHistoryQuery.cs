using CoreSRE.Application.Common.Models;
using MediatR;

namespace CoreSRE.Application.Incidents.Queries.GetIncidentChatHistory;

/// <summary>
/// 获取 Incident 关联的 Agent 对话历史。
/// </summary>
public record GetIncidentChatHistoryQuery(Guid IncidentId) : IRequest<Result<List<IncidentChatMessageDto>>>;

public class IncidentChatMessageDto
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? AgentName { get; set; }
    public DateTime Timestamp { get; set; }
}
