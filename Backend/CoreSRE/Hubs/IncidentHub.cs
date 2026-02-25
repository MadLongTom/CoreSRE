using Microsoft.AspNetCore.SignalR;

namespace CoreSRE.Hubs;

/// <summary>
/// Incident 实时推送 Hub。
/// 客户端可以 JoinIncidentList（接收所有新建/状态变更）或 JoinIncident（接收特定 Incident 详情事件）。
/// </summary>
public class IncidentHub(ILogger<IncidentHub> logger) : Hub<IIncidentClient>
{
    private const string ListGroup = "incident:list";

    /// <summary>加入 Incident 列表组（接收新建/状态变更事件）</summary>
    public async Task JoinIncidentList()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, ListGroup);
        logger.LogDebug("Client {ConnectionId} joined incident list group.", Context.ConnectionId);
    }

    /// <summary>离开 Incident 列表组</summary>
    public async Task LeaveIncidentList()
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, ListGroup);
    }

    /// <summary>加入特定 Incident 详情组（接收 Timeline / Chat / 状态事件）</summary>
    public async Task JoinIncident(Guid incidentId)
    {
        var groupName = $"incident:{incidentId}";
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        logger.LogDebug("Client {ConnectionId} joined incident:{IncidentId}.", Context.ConnectionId, incidentId);
    }

    /// <summary>离开特定 Incident 详情组</summary>
    public async Task LeaveIncident(Guid incidentId)
    {
        var groupName = $"incident:{incidentId}";
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        logger.LogInformation("Client {ConnectionId} disconnected from IncidentHub.", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
