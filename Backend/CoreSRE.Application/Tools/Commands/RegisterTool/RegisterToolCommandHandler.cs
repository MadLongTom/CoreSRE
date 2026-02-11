using AutoMapper;
using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Interfaces;
using CoreSRE.Application.Tools.DTOs;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using CoreSRE.Domain.ValueObjects;
using MediatR;
using System.Threading.Channels;

namespace CoreSRE.Application.Tools.Commands.RegisterTool;

/// <summary>
/// 注册工具命令处理器
/// </summary>
public class RegisterToolCommandHandler : IRequestHandler<RegisterToolCommand, Result<ToolRegistrationDto>>
{
    private readonly IToolRegistrationRepository _repository;
    private readonly ICredentialEncryptionService _encryptionService;
    private readonly IMapper _mapper;
    private readonly Channel<Guid>? _mcpDiscoveryChannel;

    public RegisterToolCommandHandler(
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
        RegisterToolCommand request,
        CancellationToken cancellationToken)
    {
        // Check unique name
        var existing = await _repository.GetByNameAsync(request.Name, cancellationToken);
        if (existing is not null)
            return Result<ToolRegistrationDto>.Conflict($"Tool with name '{request.Name}' already exists.");

        var toolType = Enum.Parse<ToolType>(request.ToolType);

        // Build AuthConfigVO with encrypted credentials
        var authConfig = BuildAuthConfig(request.AuthConfig);

        // Create entity via factory method
        ToolRegistration tool = toolType switch
        {
            ToolType.RestApi => ToolRegistration.CreateRestApi(
                request.Name,
                request.Description,
                request.ConnectionConfig.Endpoint,
                authConfig,
                request.ConnectionConfig.HttpMethod),

            ToolType.McpServer => ToolRegistration.CreateMcpServer(
                request.Name,
                request.Description,
                request.ConnectionConfig.Endpoint,
                Enum.Parse<TransportType>(request.ConnectionConfig.TransportType),
                authConfig),

            _ => throw new ArgumentException($"Unsupported tool type: {request.ToolType}")
        };

        // Set tool schema if InputSchema provided
        if (!string.IsNullOrWhiteSpace(request.InputSchema))
        {
            tool.SetToolSchema(new ToolSchemaVO
            {
                InputSchema = request.InputSchema
            });
        }

        await _repository.AddAsync(tool, cancellationToken);

        // For McpServer, publish to MCP discovery channel for background handshake
        if (toolType == ToolType.McpServer && _mcpDiscoveryChannel is not null)
        {
            await _mcpDiscoveryChannel.Writer.WriteAsync(tool.Id, cancellationToken);
        }

        var dto = _mapper.Map<ToolRegistrationDto>(tool);
        return Result<ToolRegistrationDto>.Ok(dto);
    }

    private AuthConfigVO BuildAuthConfig(RegisterToolAuthConfig authConfig)
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
