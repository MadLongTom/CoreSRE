using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Interfaces;

namespace CoreSRE.Endpoints;

/// <summary>
/// 文件存储 REST API 端点 — 上传/列出/下载/删除 S3 对象
/// </summary>
public static class FileEndpoints
{
    private static readonly HashSet<string> AllowedBuckets =
        ["coresre-skills", "coresre-sandboxes", "coresre-uploads"];

    public static IEndpointRouteBuilder MapFileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/files")
            .WithTags("Files")
            .WithOpenApi();

        group.MapPost("/{bucket}", UploadFile).DisableAntiforgery();
        group.MapGet("/{bucket}", ListFiles);
        group.MapGet("/{bucket}/{**key}", DownloadFile);
        group.MapDelete("/{bucket}/{**key}", DeleteFile);

        return app;
    }

    /// <summary>POST /api/files/{bucket} — 上传文件 (multipart/form-data)</summary>
    private static async Task<IResult> UploadFile(
        string bucket,
        IFormFile file,
        IFileStorageService storage,
        string? prefix = null)
    {
        if (!AllowedBuckets.Contains(bucket))
            return Results.BadRequest(new { success = false, message = $"Bucket '{bucket}' is not allowed." });

        if (file is null || file.Length == 0)
            return Results.BadRequest(new { success = false, message = "File is required." });

        if (file.Length > 100 * 1024 * 1024) // 100MB limit
            return Results.BadRequest(new { success = false, message = "File size must not exceed 100MB." });

        var key = string.IsNullOrWhiteSpace(prefix)
            ? file.FileName
            : $"{prefix.TrimEnd('/')}/{file.FileName}";

        using var stream = file.OpenReadStream();
        await storage.UploadAsync(bucket, key, stream,
            file.ContentType ?? "application/octet-stream");

        return Results.Created($"/api/files/{bucket}/{key}",
            Result<object>.Ok(new { bucket, key, size = file.Length }));
    }

    /// <summary>GET /api/files/{bucket}?prefix=xxx — 列出文件</summary>
    private static async Task<IResult> ListFiles(
        string bucket,
        IFileStorageService storage,
        string? prefix = null)
    {
        if (!AllowedBuckets.Contains(bucket))
            return Results.BadRequest(new { success = false, message = $"Bucket '{bucket}' is not allowed." });

        var entries = await storage.ListAsync(bucket, prefix ?? string.Empty);
        return Results.Ok(Result<IReadOnlyList<FileEntry>>.Ok(entries));
    }

    /// <summary>GET /api/files/{bucket}/{*key} — 下载 (redirect to presigned URL)</summary>
    private static async Task<IResult> DownloadFile(
        string bucket,
        string key,
        IFileStorageService storage)
    {
        if (!AllowedBuckets.Contains(bucket))
            return Results.BadRequest(new { success = false, message = $"Bucket '{bucket}' is not allowed." });

        var exists = await storage.ExistsAsync(bucket, key);
        if (!exists)
            return Results.NotFound(Result<object>.NotFound($"File '{key}' not found in bucket '{bucket}'."));

        var url = await storage.GetPresignedUrlAsync(bucket, key, TimeSpan.FromMinutes(5));
        return Results.Redirect(url);
    }

    /// <summary>DELETE /api/files/{bucket}/{*key} — 删除文件</summary>
    private static async Task<IResult> DeleteFile(
        string bucket,
        string key,
        IFileStorageService storage)
    {
        if (!AllowedBuckets.Contains(bucket))
            return Results.BadRequest(new { success = false, message = $"Bucket '{bucket}' is not allowed." });

        var exists = await storage.ExistsAsync(bucket, key);
        if (!exists)
            return Results.NotFound(Result<object>.NotFound($"File '{key}' not found in bucket '{bucket}'."));

        await storage.DeleteAsync(bucket, key);
        return Results.NoContent();
    }
}
