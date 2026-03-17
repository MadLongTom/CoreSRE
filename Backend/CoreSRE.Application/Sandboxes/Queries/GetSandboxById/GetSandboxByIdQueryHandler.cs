using AutoMapper;
using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Sandboxes.DTOs;
using CoreSRE.Domain.Interfaces;
using MediatR;

namespace CoreSRE.Application.Sandboxes.Queries.GetSandboxById;

public class GetSandboxByIdQueryHandler : IRequestHandler<GetSandboxByIdQuery, Result<SandboxInstanceDto>>
{
    private readonly ISandboxInstanceRepository _repository;
    private readonly IMapper _mapper;

    public GetSandboxByIdQueryHandler(ISandboxInstanceRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<Result<SandboxInstanceDto>> Handle(
        GetSandboxByIdQuery request, CancellationToken cancellationToken)
    {
        var sandbox = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (sandbox is null)
            return Result<SandboxInstanceDto>.NotFound();

        return Result<SandboxInstanceDto>.Ok(_mapper.Map<SandboxInstanceDto>(sandbox));
    }
}
