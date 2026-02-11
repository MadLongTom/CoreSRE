using AutoMapper;
using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Interfaces;
using CoreSRE.Application.Tools.DTOs;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Tools.Commands.InvokeTool;

/// <summary>
/// 统一工具调用处理器。
/// 加载工具注册 → 验证活跃状态 → 选择 Invoker → 执行调用 → 返回结果。
/// </summary>
public class InvokeToolCommandHandler : IRequestHandler<InvokeToolCommand, Result<ToolInvocationResultDto>>
{
    private readonly IToolRegistrationRepository _toolRepo;
    private readonly IToolInvokerFactory _invokerFactory;

    public InvokeToolCommandHandler(
        IToolRegistrationRepository toolRepo,
        IToolInvokerFactory invokerFactory)
    {
        _toolRepo = toolRepo;
        _invokerFactory = invokerFactory;
    }

    public async Task<Result<ToolInvocationResultDto>> Handle(
        InvokeToolCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Load tool registration
        var tool = await _toolRepo.GetByIdAsync(request.ToolRegistrationId, cancellationToken);
        if (tool is null)
        {
            return Result<ToolInvocationResultDto>.NotFound(
                $"Tool registration '{request.ToolRegistrationId}' not found.");
        }

        // 2. Validate active status — return 503 if inactive/circuit-open
        if (tool.Status != ToolStatus.Active)
        {
            return Result<ToolInvocationResultDto>.Fail(
                $"Tool '{tool.Name}' is not active (current status: {tool.Status}). Cannot invoke.",
                errorCode: 503);
        }

        // 3. Resolve invoker via factory
        IToolInvoker invoker;
        try
        {
            invoker = _invokerFactory.GetInvoker(tool.ToolType);
        }
        catch (NotSupportedException ex)
        {
            return Result<ToolInvocationResultDto>.Fail(ex.Message, errorCode: 400);
        }

        // 4. Execute invocation
        try
        {
            var result = await invoker.InvokeAsync(
                tool,
                request.McpToolName,
                request.Parameters,
                request.QueryParameters,
                request.HeaderParameters,
                cancellationToken);

            if (!result.Success)
            {
                return Result<ToolInvocationResultDto>.Fail(
                    result.Error ?? "Tool invocation failed.",
                    errorCode: 502);
            }

            return Result<ToolInvocationResultDto>.Ok(result);
        }
        catch (Exception ex)
        {
            return Result<ToolInvocationResultDto>.Fail(
                $"Tool invocation error: {ex.Message}",
                errorCode: 502);
        }
    }
}
