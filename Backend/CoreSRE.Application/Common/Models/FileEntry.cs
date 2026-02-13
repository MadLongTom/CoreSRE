namespace CoreSRE.Application.Common.Models;

/// <summary>
/// S3 对象存储文件条目
/// </summary>
public sealed record FileEntry(
    string Key,
    long Size,
    DateTime LastModified,
    string? ContentType);
