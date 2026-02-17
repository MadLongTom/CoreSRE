using AutoMapper;
using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Interfaces;
using CoreSRE.Application.DataSources.DTOs;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using CoreSRE.Domain.ValueObjects;
using MediatR;

namespace CoreSRE.Application.DataSources.Commands.RegisterDataSource;

public class RegisterDataSourceCommandHandler
    : IRequestHandler<RegisterDataSourceCommand, Result<DataSourceRegistrationDto>>
{
    private readonly IDataSourceRegistrationRepository _repository;
    private readonly ICredentialEncryptionService _encryptionService;
    private readonly IMapper _mapper;

    public RegisterDataSourceCommandHandler(
        IDataSourceRegistrationRepository repository,
        ICredentialEncryptionService encryptionService,
        IMapper mapper)
    {
        _repository = repository;
        _encryptionService = encryptionService;
        _mapper = mapper;
    }

    public async Task<Result<DataSourceRegistrationDto>> Handle(
        RegisterDataSourceCommand request,
        CancellationToken cancellationToken)
    {
        // Check for duplicate name
        var existing = await _repository.GetByNameAsync(request.Name, cancellationToken);
        if (existing is not null)
            return Result<DataSourceRegistrationDto>.Conflict($"DataSource with name '{request.Name}' already exists.");

        var category = Enum.Parse<DataSourceCategory>(request.Category, ignoreCase: true);
        var product = Enum.Parse<DataSourceProduct>(request.Product, ignoreCase: true);

        // Encrypt credential if provided
        string? encryptedCredential = null;
        if (!string.IsNullOrEmpty(request.ConnectionConfig.Credential))
        {
            encryptedCredential = _encryptionService.Encrypt(request.ConnectionConfig.Credential);
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

        // Use factory method matching the category
        var dataSource = category switch
        {
            DataSourceCategory.Metrics => DataSourceRegistration.CreateMetrics(request.Name, request.Description, product, connectionConfig),
            DataSourceCategory.Logs => DataSourceRegistration.CreateLogs(request.Name, request.Description, product, connectionConfig),
            DataSourceCategory.Tracing => DataSourceRegistration.CreateTracing(request.Name, request.Description, product, connectionConfig),
            DataSourceCategory.Alerting => DataSourceRegistration.CreateAlerting(request.Name, request.Description, product, connectionConfig),
            DataSourceCategory.Deployment => DataSourceRegistration.CreateDeployment(request.Name, request.Description, product, connectionConfig),
            DataSourceCategory.Git => DataSourceRegistration.CreateGit(request.Name, request.Description, product, connectionConfig),
            _ => throw new ArgumentOutOfRangeException(nameof(category))
        };

        // Set default query config if provided
        if (queryConfig is not null)
        {
            dataSource.Update(dataSource.Name, dataSource.Description, dataSource.ConnectionConfig, queryConfig);
        }

        // Generate and store available function names in metadata
        var functionNames = dataSource.GenerateAvailableFunctionNames();
        dataSource.UpdateMetadata(new DataSourceMetadataVO
        {
            DiscoveredAt = DateTime.UtcNow,
            AvailableFunctions = functionNames
        });

        await _repository.AddAsync(dataSource);

        var dto = _mapper.Map<DataSourceRegistrationDto>(dataSource);
        return Result<DataSourceRegistrationDto>.Ok(dto);
    }
}
