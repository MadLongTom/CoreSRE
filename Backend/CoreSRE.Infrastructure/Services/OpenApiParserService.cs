using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.ValueObjects;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using System.Text.Json;

namespace CoreSRE.Infrastructure.Services;

/// <summary>
/// OpenAPI 文档解析服务实现。解析 OpenAPI JSON/YAML 文档并提取操作为工具定义。
/// </summary>
public class OpenApiParserService : IOpenApiParserService
{
    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<ParsedToolDefinition>>> ParseAsync(
        Stream document,
        string? baseUrl = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var settings = new OpenApiReaderSettings();
            var readResult = await OpenApiDocument.LoadAsync(document, settings: settings, cancellationToken: cancellationToken);

            if (readResult.Diagnostic.Errors.Count > 0)
            {
                var errors = readResult.Diagnostic.Errors
                    .Select(e => e.Message)
                    .ToList();
                return Result<IReadOnlyList<ParsedToolDefinition>>.Fail(
                    "OpenAPI document contains errors.",
                    errors);
            }

            var openApiDoc = readResult.Document;
            if (openApiDoc?.Paths is null || openApiDoc.Paths.Count == 0)
            {
                return Result<IReadOnlyList<ParsedToolDefinition>>.Fail(
                    "OpenAPI document contains no paths/operations.");
            }

            // Determine base URL: command parameter > document servers > fallback
            var resolvedBaseUrl = baseUrl
                ?? openApiDoc.Servers?.FirstOrDefault()?.Url
                ?? string.Empty;

            var tools = new List<ParsedToolDefinition>();

            foreach (var pathItem in openApiDoc.Paths)
            {
                var path = pathItem.Key;
                var operations = pathItem.Value.Operations;

                foreach (var operation in operations)
                {
                    var method = operation.Key.Method.ToLower();
                    var op = operation.Value;

                    // Name: operationId or fallback to {method}_{sanitized_path}
                    var name = !string.IsNullOrWhiteSpace(op.OperationId)
                        ? op.OperationId
                        : $"{method}_{SanitizePath(path)}";

                    var description = op.Summary ?? op.Description;

                    // Build endpoint
                    var endpoint = $"{resolvedBaseUrl.TrimEnd('/')}{path}";

                    // Extract input schema (merge parameters + requestBody)
                    var inputSchema = ExtractInputSchema(op);

                    // Extract output schema (from 200/2xx response)
                    var outputSchema = ExtractOutputSchema(op);

                    var toolSchema = new ToolSchemaVO
                    {
                        InputSchema = inputSchema,
                        OutputSchema = outputSchema
                    };

                    tools.Add(new ParsedToolDefinition
                    {
                        Name = name,
                        Description = description,
                        Endpoint = endpoint,
                        HttpMethod = method.ToUpperInvariant(),
                        ToolSchema = toolSchema
                    });
                }
            }

            return Result<IReadOnlyList<ParsedToolDefinition>>.Ok(tools);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<ParsedToolDefinition>>.Fail(
                $"Failed to parse OpenAPI document: {ex.Message}");
        }
    }

    private static string SanitizePath(string path)
    {
        return path
            .Replace("/", "_")
            .Replace("{", "")
            .Replace("}", "")
            .Trim('_');
    }

    private static string? ExtractInputSchema(OpenApiOperation operation)
    {
        var schemaProperties = new Dictionary<string, object>();

        // Add parameters
        if (operation.Parameters is not null)
        {
            foreach (var param in operation.Parameters)
            {
                schemaProperties[param.Name] = new
                {
                    type = param.Schema?.Type.ToString() ?? "string",
                    description = param.Description ?? string.Empty,
                    @in = param.In?.ToString()?.ToLower()
                };
            }
        }

        // Add request body schema
        if (operation.RequestBody?.Content is not null)
        {
            var jsonContent = operation.RequestBody.Content
                .FirstOrDefault(c => c.Key.Contains("json", StringComparison.OrdinalIgnoreCase));

            if (jsonContent.Value?.Schema is not null)
            {
                schemaProperties["body"] = SerializeOpenApiSchema(jsonContent.Value.Schema);
            }
        }

        if (schemaProperties.Count == 0)
            return null;

        return JsonSerializer.Serialize(new { type = "object", properties = schemaProperties });
    }

    private static string? ExtractOutputSchema(OpenApiOperation operation)
    {
        if (operation.Responses is null)
            return null;

        // Try 200 first, then any 2xx
        var successResponse = operation.Responses
            .Where(r => r.Key.StartsWith("2"))
            .OrderBy(r => r.Key)
            .FirstOrDefault();

        if (successResponse.Value?.Content is null)
            return null;

        var jsonContent = successResponse.Value.Content
            .FirstOrDefault(c => c.Key.Contains("json", StringComparison.OrdinalIgnoreCase));

        if (jsonContent.Value?.Schema is null)
            return null;

        var schemaObj = SerializeOpenApiSchema(jsonContent.Value.Schema);
        return JsonSerializer.Serialize(schemaObj);
    }

    private static object SerializeOpenApiSchema(IOpenApiSchema schema)
    {
        var result = new Dictionary<string, object>();

        if (schema.Type != default)
            result["type"] = schema.Type.ToString();

        if (schema.Description is not null)
            result["description"] = schema.Description;

        if (schema.Properties is not null && schema.Properties.Count > 0)
        {
            var props = new Dictionary<string, object>();
            foreach (var prop in schema.Properties)
            {
                props[prop.Key] = SerializeOpenApiSchema(prop.Value);
            }
            result["properties"] = props;
        }

        if (schema.Items is not null)
        {
            result["items"] = SerializeOpenApiSchema(schema.Items);
        }

        return result;
    }
}
