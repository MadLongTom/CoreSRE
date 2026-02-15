using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Interfaces;
using CoreSRE.Application.Skills.Commands.DeleteSkill;
using CoreSRE.Application.Skills.Commands.RegisterSkill;
using CoreSRE.Application.Skills.Commands.UpdateSkill;
using CoreSRE.Application.Skills.Queries.GetSkillById;
using CoreSRE.Application.Skills.Queries.GetSkills;
using CoreSRE.Application.Tools.Queries.GetAvailableFunctions;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.Interfaces;
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
        group.MapGet("/{id:guid}/files/{**key}", DownloadSkillFile);
        group.MapDelete("/{id:guid}/files/{**key}", DeleteSkillFile);

        // Agent Skills 规范 SKILL.md 导入/导出
        group.MapGet("/{id:guid}/export", ExportSkillMd);
        group.MapGet("/{id:guid}/export/zip", ExportSkillZip);
        group.MapPost("/import", ImportSkillZip).DisableAntiforgery();

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

    /// <summary>GET /api/skills/{id}/files/{*key} — 下载 Skill 文件（流式输出）</summary>
    private static async Task<IResult> DownloadSkillFile(
        Guid id,
        string key,
        IFileStorageService storage)
    {
        var fullKey = $"{id}/{key}";
        var exists = await storage.ExistsAsync("coresre-skills", fullKey);
        if (!exists) return Results.NotFound();

        var stream = await storage.DownloadAsync("coresre-skills", fullKey);
        var contentType = GuessContentType(key);
        return Results.File(stream, contentType, enableRangeProcessing: true);
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

    private static string GuessContentType(string key)
    {
        var ext = Path.GetExtension(key).ToLowerInvariant();
        return ext switch
        {
            ".txt" or ".md" or ".markdown" => "text/plain; charset=utf-8",
            ".json" => "application/json",
            ".yaml" or ".yml" => "text/yaml; charset=utf-8",
            ".xml" => "application/xml",
            ".html" or ".htm" => "text/html; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".js" or ".mjs" => "application/javascript",
            ".ts" or ".tsx" => "text/typescript; charset=utf-8",
            ".py" => "text/x-python; charset=utf-8",
            ".sh" or ".bash" => "text/x-shellscript; charset=utf-8",
            ".ps1" => "text/plain; charset=utf-8",
            ".cs" => "text/plain; charset=utf-8",
            ".java" => "text/plain; charset=utf-8",
            ".go" => "text/plain; charset=utf-8",
            ".rs" => "text/plain; charset=utf-8",
            ".sql" => "text/plain; charset=utf-8",
            ".csv" => "text/csv; charset=utf-8",
            ".log" => "text/plain; charset=utf-8",
            ".toml" => "text/plain; charset=utf-8",
            ".ini" or ".cfg" or ".conf" => "text/plain; charset=utf-8",
            ".dockerfile" => "text/plain; charset=utf-8",
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".webp" => "image/webp",
            ".ico" => "image/x-icon",
            ".doc" or ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" or ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".ppt" or ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".zip" => "application/zip",
            ".tar" or ".gz" or ".tgz" => "application/octet-stream",
            _ => "application/octet-stream",
        };
    }

    // ─────────── Agent Skills 规范 Import / Export ───────────

    /// <summary>GET /api/skills/{id}/export — 导出为 SKILL.md 文本</summary>
    private static async Task<IResult> ExportSkillMd(
        Guid id,
        ISkillRegistrationRepository repo,
        ISkillMdService skillMdService,
        ISender sender)
    {
        var skill = await repo.GetByIdAsync(id);
        if (skill is null) return Results.NotFound();

        var resolvedNames = await ResolveToolNames(skill.AllowedTools, sender);
        var md = skillMdService.Export(skill, resolvedNames);
        return Results.Text(md, "text/markdown; charset=utf-8");
    }

    /// <summary>GET /api/skills/{id}/export/zip — 导出为 ZIP（含 SKILL.md + 文件包）</summary>
    private static async Task<IResult> ExportSkillZip(
        Guid id,
        ISkillRegistrationRepository repo,
        ISkillMdService skillMdService,
        ISender sender)
    {
        var skill = await repo.GetByIdAsync(id);
        if (skill is null) return Results.NotFound();

        var resolvedNames = await ResolveToolNames(skill.AllowedTools, sender);
        var zipBytes = await skillMdService.ExportZipAsync(skill, resolvedNames);
        return Results.File(zipBytes, "application/zip", $"{skill.Name}.zip");
    }

    /// <summary>POST /api/skills/import — 从 ZIP 导入（解析 SKILL.md + 上传文件包 → 自动创建 Skill）</summary>
    private static async Task<IResult> ImportSkillZip(
        IFormFile file,
        ISender sender,
        ISkillRegistrationRepository repo,
        ISkillMdService skillMdService)
    {
        if (file is null || file.Length == 0)
            return Results.BadRequest(Result<object>.Fail("A ZIP file is required."));

        using var stream = file.OpenReadStream();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        ms.Position = 0;

        // Peek: parse SKILL.md frontmatter from ZIP
        var importResult = await skillMdService.ImportZipAsync(ms, Guid.Empty);

        if (importResult.ParseResult is null || importResult.ParseResult.HasErrors)
        {
            var errors = importResult.Errors.Concat(importResult.ParseResult?.Errors ?? []);
            return Results.BadRequest(Result<object>.Fail(string.Join("; ", errors)));
        }

        var parsed = importResult.ParseResult;

        // Check name conflict
        var existing = await repo.GetByNameAsync(parsed.Name);
        if (existing is not null)
            return Results.Conflict(Result<object>.Fail($"Skill with name '{parsed.Name}' already exists."));

        // Create skill entity
        var skill = Domain.Entities.SkillRegistration.Create(
            name: parsed.Name,
            description: parsed.Description,
            category: string.Empty,
            content: parsed.Body,
            scope: SkillScope.User,
            license: parsed.License,
            compatibility: parsed.Compatibility,
            metadata: parsed.Metadata);

        // Resolve allowed-tools names → IDs
        if (!string.IsNullOrWhiteSpace(parsed.AllowedTools))
        {
            var toolIds = await ResolveToolIds(parsed.AllowedTools, sender);
            if (toolIds.Count > 0)
                skill.SetAllowedTools(toolIds);
        }

        await repo.AddAsync(skill);

        // Now re-import files with actual skill ID
        ms.Position = 0;
        var fileImport = await skillMdService.ImportZipAsync(ms, skill.Id);
        if (fileImport.HasFiles)
            skill.SetHasFiles(true);

        await repo.UpdateAsync(skill);

        return Results.Created($"/api/skills/{skill.Id}",
            Result<object>.Ok(new
            {
                skill.Id,
                skill.Name,
                filesUploaded = fileImport.UploadedFiles.Count,
                files = fileImport.UploadedFiles,
            }));
    }

    // ─────────── Tool resolution helpers ───────────

    /// <summary>Resolve tool GUIDs → name dictionary for SKILL.md export</summary>
    private static async Task<IReadOnlyDictionary<Guid, string>?> ResolveToolNames(
        List<Guid> toolIds, ISender sender)
    {
        if (toolIds is not { Count: > 0 }) return null;

        var result = await sender.Send(new GetAvailableFunctionsQuery());
        if (!result.Success || result.Data is null) return null;

        var idSet = toolIds.ToHashSet();
        return result.Data
            .Where(t => idSet.Contains(t.Id))
            .ToDictionary(t => t.Id, t => t.Name);
    }

    /// <summary>Resolve space-separated tool names → GUID list for SKILL.md import</summary>
    private static async Task<List<Guid>> ResolveToolIds(string allowedToolsRaw, ISender sender)
    {
        var names = allowedToolsRaw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (names.Length == 0) return [];

        var result = await sender.Send(new GetAvailableFunctionsQuery());
        if (!result.Success || result.Data is null) return [];

        var nameSet = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        return result.Data
            .Where(t => nameSet.Contains(t.Name))
            .Select(t => t.Id)
            .ToList();
    }
}
