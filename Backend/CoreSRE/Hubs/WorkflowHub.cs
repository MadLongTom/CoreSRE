using Microsoft.AspNetCore.SignalR;

namespace CoreSRE.Hubs;

/// <summary>
/// 工作流执行 SignalR Hub — 强类型 Hub，使用 IWorkflowClient 定义客户端方法。
/// 观察者通过 JoinExecution 加入执行组，通过 LeaveExecution 离开。
/// </summary>
public class WorkflowHub : Hub<IWorkflowClient>
{
    private readonly ILogger<WorkflowHub> _logger;

    public WorkflowHub(ILogger<WorkflowHub> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 加入工作流执行观察组。
    /// </summary>
    public async Task JoinExecution(Guid executionId)
    {
        var groupName = $"execution:{executionId}";
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        _logger.LogInformation("客户端 {ConnectionId} 加入执行组 {GroupName}", Context.ConnectionId, groupName);
    }

    /// <summary>
    /// 离开工作流执行观察组。
    /// </summary>
    public async Task LeaveExecution(Guid executionId)
    {
        var groupName = $"execution:{executionId}";
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        _logger.LogInformation("客户端 {ConnectionId} 离开执行组 {GroupName}", Context.ConnectionId, groupName);
    }

    /// <summary>
    /// 断开连接时记录日志。框架会自动从所有组中移除断开的连接。
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("客户端 {ConnectionId} 断开连接: {Reason}",
            Context.ConnectionId, exception?.Message ?? "正常断开");
        await base.OnDisconnectedAsync(exception);
    }
}
