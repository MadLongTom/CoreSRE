using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Workflows.Commands.DeleteWorkflow;

public class DeleteWorkflowCommandHandler : IRequestHandler<DeleteWorkflowCommand, Result<bool>>
{
    private readonly IWorkflowDefinitionRepository _workflowRepo;

    public DeleteWorkflowCommandHandler(IWorkflowDefinitionRepository workflowRepo)
    {
        _workflowRepo = workflowRepo;
    }

    public async Task<Result<bool>> Handle(
        DeleteWorkflowCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Fetch existing
        var workflow = await _workflowRepo.GetByIdAsync(request.Id, cancellationToken);
        if (workflow is null)
            return Result<bool>.NotFound();

        // 2. Status guard (Draft only)
        try
        {
            workflow.GuardCanDelete();
        }
        catch (InvalidOperationException ex)
        {
            return Result<bool>.Fail(ex.Message);
        }

        // 3. Agent reference guard
        if (await _workflowRepo.IsReferencedByAgentAsync(request.Id, cancellationToken))
            return Result<bool>.Fail("该工作流已被 Agent 引用，无法删除。");

        // 4. Delete
        await _workflowRepo.DeleteAsync(request.Id, cancellationToken);

        return Result<bool>.Ok(true);
    }
}
