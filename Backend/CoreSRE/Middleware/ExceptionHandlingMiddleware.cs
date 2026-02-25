using System.Text.Json;
using CoreSRE.Application.Sandboxes;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CoreSRE.Middleware;

/// <summary>
/// 全局异常处理中间件。
/// 捕获 ValidationException → 400，DbUpdateException (23505) → 409，其他异常 → 500。
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception) when (context.Response.HasStarted)
        {
            // Response already started (e.g., SSE streaming) — cannot write error response.
            // Just log and let the connection close naturally.
            _logger.LogWarning("Exception after response started for {Path} — cannot write error body", context.Request.Path);
            throw; // Re-throw to let Kestrel handle connection cleanup
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning(ex, "Validation failed for request {Path}", context.Request.Path);
            await WriteErrorResponse(context, 400, "Validation failed.",
                ex.Errors.Select(e => e.ErrorMessage).ToList());
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            _logger.LogWarning(ex, "Unique constraint violation for request {Path}", context.Request.Path);
            await WriteErrorResponse(context, 409, "A resource with the same name already exists.");
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Argument error for request {Path}", context.Request.Path);
            await WriteErrorResponse(context, 400, ex.Message);
        }
        catch (SandboxOperationException ex)
        {
            _logger.LogWarning(ex.InnerException, "Sandbox operation '{Operation}' failed for '{Sandbox}': {Message}",
                ex.Operation, ex.SandboxName, ex.Message);
            await WriteErrorResponse(context, 422, ex.Message, ex.Hints.ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception for request {Path}", context.Request.Path);
            await WriteErrorResponse(context, 500, "An unexpected error occurred.");
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        return ex.InnerException is PostgresException pgEx && pgEx.SqlState == "23505";
    }

    private static async Task WriteErrorResponse(
        HttpContext context,
        int statusCode,
        string message,
        List<string>? errors = null)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        var response = new
        {
            success = false,
            message,
            errors
        };

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        await context.Response.WriteAsync(JsonSerializer.Serialize(response, options));
    }
}
