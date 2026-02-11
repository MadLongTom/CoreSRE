using AutoMapper;
using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Interfaces;
using CoreSRE.Application.Tools.DTOs;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using CoreSRE.Domain.ValueObjects;
using MediatR;
using System.Threading.Channels;

namespace CoreSRE.Application.Tools.Commands.UpdateTool;

/// <summary>
/// 更新工具命令处理器
/// </summary>
public class UpdateToolCommandHandler : IRequestHandler<UpdateToolCommand, Result<ToolRegistrationDto>>
{
    private readonly IToolRegistrationRepository _repository;
    private readonly ICredentialEncryptionService _encryptionService;
    private readonly IMapper _mapper;
    private readonly Channel<Guid>? _mcpDiscoveryChannel;

    public UpdateToolCommandHandler(
        IToolRegistrationRepository repository,
        ICredentialEncryptionService encryptionService,
        IMapper mapper,
        Channel<Guid>? mcpDiscoveryChannel = null)
    {
        _repository = repository;
        _encryptionService = encryptionService;
        _mapper = mapper;
        _mcpDiscoveryChannel = mcpDiscoveryChannel;
    }

    public async Task<Result<ToolRegistrationDto>> Handle(
        UpdateToolCommand request,
        CancellationToken cancellationToken)
    {
        var tool = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (tool is null)
            return Result<ToolRegistrationDto>.NotFound($"Tool with ID '{request.Id}' not found.");

        // Check unique name (if name changed)
        if (!string.Equals(tool.Name, request.Name, StringComparison.Ordinal))
        {
            var existing = await _repository.GetByNameAsync(request.Name, cancellationToken);
            if (existing is not null)
                return Result<ToolRegistrationDto>.Conflict($"Tool with name '{request.Name}' already exists.");
        }

        // Build ConnectionConfigVO
        var connectionConfig = new ConnectionConfigVO
        {
            Endpoint = request.ConnectionConfig.Endpoint,
            TransportType = Enum.Parse<TransportType>(request.ConnectionConfig.TransportType),
            HttpMethod = request.ConnectionConfig.HttpMethod?.ToUpperInvariant() ?? "POST"
        };

        // Build AuthConfigVO with re-encrypted credentials
        var authConfig = BuildAuthConfig(request.AuthConfig);

        // Update entity (domain method validates type-specific invariants)
        tool.Update(request.Name, request.Description, connectionConfig, authConfig);

        // Set tool schema if InputSchema provided
        if (!string.IsNullOrWhiteSpace(request.InputSchema))
        {
            tool.SetToolSchema(new ToolSchemaVO
            {
                InputSchema = request.InputSchema
            });
        }

        await _repository.UpdateAsync(tool, cancellationToken);

        // If McpServer and connection config changed, re-trigger discovery
        if (tool.ToolType == ToolType.McpServer && _mcpDiscoveryChannel is not null)
        {
            await _mcpDiscoveryChannel.Writer.WriteAsync(tool.Id, cancellationToken);
        }

        var dto = _mapper.Map<ToolRegistrationDto>(tool);
        return Result<ToolRegistrationDto>.Ok(dto);
    }

    private AuthConfigVO BuildAuthConfig(RegisterTool.RegisterToolAuthConfig authConfig)
    {
        var authType = Enum.Parse<AuthType>(authConfig.AuthType);

        return new AuthConfigVO
        {
            AuthType = authType,
            EncryptedCredential = !string.IsNullOrEmpty(authConfig.Credential)
                ? _encryptionService.Encrypt(authConfig.Credential)
                : null,
            ApiKeyHeaderName = authConfig.ApiKeyHeaderName,
            TokenEndpoint = authConfig.TokenEndpoint,
            ClientId = authConfig.ClientId,
            EncryptedClientSecret = !string.IsNullOrEmpty(authConfig.ClientSecret)
                ? _encryptionService.Encrypt(authConfig.ClientSecret)
                : null
        };
    }
}
