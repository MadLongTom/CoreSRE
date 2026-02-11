using System.Threading.Channels;
using CoreSRE.Application.Workflows.Commands.ExecuteWorkflow;
using CoreSRE.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CoreSRE.Infrastructure.Services;

/// <summary>
/// 工作流执行后台服务。
/// 从 Channel&lt;ExecuteWorkflowRequest&gt; 消费执行请求，通过 IWorkflowEngine 执行工作流。
/// 匹配 McpDiscoveryBackgroundService 模式。
/// </summary>
public class WorkflowExecutionBackgroundService : BackgroundService
{
    private readonly Channel<ExecuteWorkflowRequest> _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WorkflowExecutionBackgroundService> _logger;

    public WorkflowExecutionBackgroundService(
        Channel<ExecuteWorkflowRequest> channel,
        IServiceScopeFactory scopeFactory,
        ILogger<WorkflowExecutionBackgroundService> logger)
    {
        _channel = channel;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("WorkflowExecutionBackgroundService 已启动");

        await foreach (var request in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessExecutionAsync(request, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "处理工作流执行请求失败: ExecutionId={ExecutionId}",
                    request.ExecutionId);
            }
        }

        _logger.LogInformation("WorkflowExecutionBackgroundService 已停止");
    }

    private async Task ProcessExecutionAsync(
        ExecuteWorkflowRequest request,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var executionRepo = scope.ServiceProvider.GetRequiredService<IWorkflowExecutionRepository>();
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();

        var execution = await executionRepo.GetByIdAsync(request.ExecutionId, cancellationToken);
        if (execution is null)
        {
            _logger.LogWarning("工作流执行记录不存在: {ExecutionId}", request.ExecutionId);
            return;
        }

        _logger.LogInformation(
            "开始处理工作流执行: ExecutionId={ExecutionId}, WorkflowDefinitionId={WorkflowDefinitionId}",
            execution.Id, execution.WorkflowDefinitionId);

        await engine.ExecuteAsync(execution, cancellationToken);

        _logger.LogInformation(
            "工作流执行结束: ExecutionId={ExecutionId}, Status={Status}",
            execution.Id, execution.Status);
    }
}
