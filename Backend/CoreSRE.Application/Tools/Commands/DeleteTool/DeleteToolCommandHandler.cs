using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Tools.Commands.DeleteTool;

/// <summary>
/// 删除工具命令处理器
/// </summary>
public class DeleteToolCommandHandler : IRequestHandler<DeleteToolCommand, Result<bool>>
{
    private readonly IToolRegistrationRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteToolCommandHandler(IToolRegistrationRepository repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<bool>> Handle(
        DeleteToolCommand request,
        CancellationToken cancellationToken)
    {
        var tool = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (tool is null)
            return Result<bool>.NotFound($"Tool with ID '{request.Id}' not found.");

        await _repository.DeleteAsync(request.Id, cancellationToken);

        return Result<bool>.Ok(true);
    }
}
