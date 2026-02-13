using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Interfaces;
using CoreSRE.Application.Skills.Commands.DeleteSkill;
using CoreSRE.Application.Skills.Commands.RegisterSkill;
using CoreSRE.Application.Skills.Commands.UpdateSkill;
using CoreSRE.Application.Skills.Queries.GetSkillById;
using CoreSRE.Application.Skills.Queries.GetSkills;
using CoreSRE.Domain.Enums;
using MediatR;

namespace CoreSRE.Endpoints;

/// <summary>
/// Agent Skills 管理 REST API 端点
/// </summary>
public static class SkillEndpoints
{
    public static IEndpointRouteBuilder MapSkillEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/skills")
            .WithTags("Skills")
            .WithOpenApi();

        group.MapPost("/", RegisterSkill);
        group.MapGet("/", GetSkills);
        group.MapGet("/{id:guid}", GetSkillById);
        group.MapPut("/{id:guid}", UpdateSkill);
        group.MapDelete("/{id:guid}", DeleteSkill);
        group.MapPost("/{id:guid}/files", UploadSkillFiles).DisableAntiforgery();
        group.MapGet("/{id:guid}/files", ListSkillFiles);
        group.MapDelete("/{id:guid}/files/{**key}", DeleteSkillFile);

        return app;
    }

    private static async Task<IResult> RegisterSkill(
        RegisterSkillCommand command, ISender sender)
    {
        var result = await sender.Send(command);
        if (!result.Success)
        {
            return result.ErrorCode switch
            {
                409 => Results.Conflict(result),
                _ => Results.BadRequest(result)
            };
        }
        return Results.Created($"/api/skills/{result.Data!.Id}", result);
    }

    private static async Task<IResult> GetSkills(
        ISender sender,
        string? scope = null,
        string? status = null,
        string? category = null,
        string? search = null,
        int page = 1,
        int pageSize = 20)
    {
        SkillScope? parsedScope = null;
        if (scope is not null)
        {
            if (!Enum.TryParse<SkillScope>(scope, true, out var sc))
                return Results.BadRequest(new { success = false, message = "Invalid scope." });
            parsedScope = sc;
        }

        SkillStatus? parsedStatus = null;
        if (status is not null)
        {
            if (!Enum.TryParse<SkillStatus>(status, true, out var st))
                return Results.BadRequest(new { success = false, message = "Invalid status." });
            parsedStatus = st;
        }

        var result = await sender.Send(new GetSkillsQuery(parsedScope, parsedStatus, category, search, page, pageSize));
        return Results.Ok(result);
    }

    private static async Task<IResult> GetSkillById(Guid id, ISender sender)
    {
        var result = await sender.Send(new GetSkillByIdQuery(id));
        return result.Success ? Results.Ok(result) : Results.NotFound(result);
    }

    private static async Task<IResult> UpdateSkill(
        Guid id, UpdateSkillCommand command, ISender sender)
    {
        var result = await sender.Send(command with { Id = id });
        if (!result.Success)
            return result.ErrorCode switch
            {
                404 => Results.NotFound(result),
                409 => Results.Conflict(result),
                _ => Results.BadRequest(result)
            };
        return Results.Ok(result);
    }

    private static async Task<IResult> DeleteSkill(Guid id, ISender sender)
    {
        var result = await sender.Send(new DeleteSkillCommand(id));
        if (!result.Success) return Results.NotFound(result);
        return Results.NoContent();
    }

    /// <summary>POST /api/skills/{id}/files — 上传文件到 Skill 文件包（支持多文件）</summary>
    private static async Task<IResult> UploadSkillFiles(
        Guid id,
        IFormFileCollection files,
        IFileStorageService storage,
        ISender sender,
        string? prefix = null)
    {
        // Verify skill exists
        var skillResult = await sender.Send(new GetSkillByIdQuery(id));
        if (!skillResult.Success) return Results.NotFound(skillResult);

        if (files is null || files.Count == 0)
            return Results.BadRequest(new { success = false, message = "At least one file is required." });

        var uploaded = new List<object>();
        foreach (var file in files)
        {
            if (file.Length == 0) continue;

            var key = string.IsNullOrWhiteSpace(prefix)
                ? $"{id}/{file.FileName}"
                : $"{id}/{prefix.TrimEnd('/')}/{file.FileName}";

            using var stream = file.OpenReadStream();
            await storage.UploadAsync("coresre-skills", key, stream,
                file.ContentType ?? "application/octet-stream");

            uploaded.Add(new { key, size = file.Length });
        }

        return Results.Created($"/api/skills/{id}/files",
            Result<object>.Ok(new { uploaded = uploaded.Count, files = uploaded }));
    }

    /// <summary>GET /api/skills/{id}/files — 列出 Skill 文件包</summary>
    private static async Task<IResult> ListSkillFiles(
        Guid id,
        IFileStorageService storage)
    {
        var entries = await storage.ListAsync("coresre-skills", $"{id}/");
        return Results.Ok(Result<IReadOnlyList<FileEntry>>.Ok(entries));
    }

    /// <summary>DELETE /api/skills/{id}/files/{*key} — 删除 Skill 文件包中的文件</summary>
    private static async Task<IResult> DeleteSkillFile(
        Guid id,
        string key,
        IFileStorageService storage)
    {
        var fullKey = $"{id}/{key}";
        var exists = await storage.ExistsAsync("coresre-skills", fullKey);
        if (!exists) return Results.NotFound();

        await storage.DeleteAsync("coresre-skills", fullKey);
        return Results.NoContent();
    }
}
