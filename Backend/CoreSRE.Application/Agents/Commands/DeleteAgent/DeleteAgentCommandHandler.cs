using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Agents.Commands.DeleteAgent;

/// <summary>
/// 注销（删除）Agent 命令处理器
/// </summary>
public class DeleteAgentCommandHandler : IRequestHandler<DeleteAgentCommand, Result<bool>>
{
    private readonly IAgentRegistrationRepository _repository;

    public DeleteAgentCommandHandler(IAgentRegistrationRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<bool>> Handle(DeleteAgentCommand request, CancellationToken cancellationToken)
    {
        var agent = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (agent is null)
            return Result<bool>.NotFound($"Agent with ID '{request.Id}' not found.");

        await _repository.DeleteAsync(request.Id, cancellationToken);

        return Result<bool>.Ok(true);
    }
}
