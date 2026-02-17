using AutoMapper;
using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Interfaces;
using CoreSRE.Application.DataSources.DTOs;
using CoreSRE.Domain.Interfaces;
using CoreSRE.Domain.ValueObjects;
using MediatR;

namespace CoreSRE.Application.DataSources.Commands.UpdateDataSource;

public class UpdateDataSourceCommandHandler
    : IRequestHandler<UpdateDataSourceCommand, Result<DataSourceRegistrationDto>>
{
    private readonly IDataSourceRegistrationRepository _repository;
    private readonly ICredentialEncryptionService _encryptionService;
    private readonly IMapper _mapper;

    public UpdateDataSourceCommandHandler(
        IDataSourceRegistrationRepository repository,
        ICredentialEncryptionService encryptionService,
        IMapper mapper)
    {
        _repository = repository;
        _encryptionService = encryptionService;
        _mapper = mapper;
    }

    public async Task<Result<DataSourceRegistrationDto>> Handle(
        UpdateDataSourceCommand request,
        CancellationToken cancellationToken)
    {
        var dataSource = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (dataSource is null)
            return Result<DataSourceRegistrationDto>.NotFound($"DataSource with ID '{request.Id}' not found.");

        // Check unique name if changed
        if (!string.Equals(dataSource.Name, request.Name, StringComparison.Ordinal))
        {
            var existing = await _repository.GetByNameAsync(request.Name, cancellationToken);
            if (existing is not null)
                return Result<DataSourceRegistrationDto>.Conflict($"DataSource with name '{request.Name}' already exists.");
        }

        // Encrypt credential if provided
        string? encryptedCredential = null;
        if (!string.IsNullOrEmpty(request.ConnectionConfig.Credential))
        {
            encryptedCredential = _encryptionService.Encrypt(request.ConnectionConfig.Credential);
        }
        else
        {
            // Keep existing credential if not provided
            encryptedCredential = dataSource.ConnectionConfig.EncryptedCredential;
        }

        var connectionConfig = new DataSourceConnectionVO
        {
            BaseUrl = request.ConnectionConfig.BaseUrl.TrimEnd('/'),
            AuthType = request.ConnectionConfig.AuthType,
            EncryptedCredential = encryptedCredential,
            AuthHeaderName = request.ConnectionConfig.AuthHeaderName,
            TlsSkipVerify = request.ConnectionConfig.TlsSkipVerify,
            TimeoutSeconds = request.ConnectionConfig.TimeoutSeconds > 0 ? request.ConnectionConfig.TimeoutSeconds : 30,
            CustomHeaders = request.ConnectionConfig.CustomHeaders,
            Namespace = request.ConnectionConfig.Namespace,
            Organization = request.ConnectionConfig.Organization,
            KubeConfig = request.ConnectionConfig.KubeConfig
        };

        QueryConfigVO? queryConfig = request.DefaultQueryConfig is not null
            ? new QueryConfigVO
            {
                DefaultLabels = request.DefaultQueryConfig.DefaultLabels,
                DefaultNamespace = request.DefaultQueryConfig.DefaultNamespace,
                MaxResults = request.DefaultQueryConfig.MaxResults,
                DefaultStep = request.DefaultQueryConfig.DefaultStep,
                DefaultIndex = request.DefaultQueryConfig.DefaultIndex
            }
            : null;

        dataSource.Update(request.Name, request.Description, connectionConfig, queryConfig);

        await _repository.UpdateAsync(dataSource, cancellationToken);

        var dto = _mapper.Map<DataSourceRegistrationDto>(dataSource);
        return Result<DataSourceRegistrationDto>.Ok(dto);
    }
}
