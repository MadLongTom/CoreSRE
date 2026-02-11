using AutoMapper;
using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Interfaces;
using CoreSRE.Application.Tools.DTOs;
using CoreSRE.Domain.Entities;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
using CoreSRE.Domain.ValueObjects;
using MediatR;

namespace CoreSRE.Application.Tools.Commands.ImportOpenApi;

/// <summary>
/// OpenAPI 导入命令处理器
/// </summary>
public class ImportOpenApiCommandHandler : IRequestHandler<ImportOpenApiCommand, Result<OpenApiImportResultDto>>
{
    private readonly IOpenApiParserService _parserService;
    private readonly IToolRegistrationRepository _repository;
    private readonly ICredentialEncryptionService _encryptionService;
    private readonly IMapper _mapper;

    public ImportOpenApiCommandHandler(
        IOpenApiParserService parserService,
        IToolRegistrationRepository repository,
        ICredentialEncryptionService encryptionService,
        IMapper mapper)
    {
        _parserService = parserService;
        _repository = repository;
        _encryptionService = encryptionService;
        _mapper = mapper;
    }

    public async Task<Result<OpenApiImportResultDto>> Handle(
        ImportOpenApiCommand request,
        CancellationToken cancellationToken)
    {
        var parseResult = await _parserService.ParseAsync(request.Document, request.BaseUrl, cancellationToken);
        if (!parseResult.Success || parseResult.Data is null)
            return Result<OpenApiImportResultDto>.Fail(parseResult.Message ?? "Failed to parse OpenAPI document.", parseResult.Errors);

        var parsedTools = parseResult.Data;
        var importResult = new OpenApiImportResultDto
        {
            TotalOperations = parsedTools.Count
        };

        // Build shared auth config if provided
        AuthConfigVO? authConfig = null;
        if (request.AuthConfig is not null)
        {
            var authType = Enum.Parse<AuthType>(request.AuthConfig.AuthType);
            authConfig = new AuthConfigVO
            {
                AuthType = authType,
                EncryptedCredential = !string.IsNullOrEmpty(request.AuthConfig.Credential)
                    ? _encryptionService.Encrypt(request.AuthConfig.Credential)
                    : null,
                ApiKeyHeaderName = request.AuthConfig.ApiKeyHeaderName,
                TokenEndpoint = request.AuthConfig.TokenEndpoint,
                ClientId = request.AuthConfig.ClientId,
                EncryptedClientSecret = !string.IsNullOrEmpty(request.AuthConfig.ClientSecret)
                    ? _encryptionService.Encrypt(request.AuthConfig.ClientSecret)
                    : null
            };
        }

        foreach (var parsed in parsedTools)
        {
            try
            {
                // Check for duplicate name
                var existing = await _repository.GetByNameAsync(parsed.Name, cancellationToken);
                if (existing is not null)
                {
                    importResult.SkippedCount++;
                    importResult.Errors.Add($"Skipped '{parsed.Name}': tool with this name already exists.");
                    continue;
                }

                var tool = ToolRegistration.CreateFromOpenApi(
                    parsed.Name,
                    parsed.Description,
                    parsed.Endpoint,
                    authConfig,
                    parsed.ToolSchema ?? new ToolSchemaVO(),
                    request.ImportSource,
                    parsed.HttpMethod);

                await _repository.AddAsync(tool, cancellationToken);

                var dto = _mapper.Map<ToolRegistrationDto>(tool);
                importResult.Tools.Add(dto);
                importResult.ImportedCount++;
            }
            catch (Exception ex)
            {
                importResult.SkippedCount++;
                importResult.Errors.Add($"Failed to import '{parsed.Name}': {ex.Message}");
            }
        }

        return Result<OpenApiImportResultDto>.Ok(importResult);
    }
}
