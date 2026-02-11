using CoreSRE.Application.Common.Models;
using MediatR;

namespace CoreSRE.Application.Workflows.Commands.DeleteWorkflow;

public record DeleteWorkflowCommand(Guid Id) : IRequest<Result<bool>>;
