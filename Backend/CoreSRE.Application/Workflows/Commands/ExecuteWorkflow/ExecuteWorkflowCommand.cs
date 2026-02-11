using System.Text.Json;
using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Workflows.DTOs;
using MediatR;

namespace CoreSRE.Application.Workflows.Commands.ExecuteWorkflow;

/// <summary>
/// 启动工作流执行命令
/// </summary>
public record ExecuteWorkflowCommand : IRequest<Result<WorkflowExecutionDto>>
{
    /// <summary>工作流定义 ID</summary>
    public Guid WorkflowDefinitionId { get; init; }

    /// <summary>执行输入数据（省略或 null 时默认 {}）</summary>
    public JsonElement? Input { get; init; }
}
