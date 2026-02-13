using CoreSRE.Application.Common.Models;

namespace CoreSRE.Application.Interfaces;

/// <summary>
/// S3 兼容对象存储服务接口 — 所有文件存储操作的统一抽象。
/// 底层由 MinIO 实现，上层模块（Skills 文件包、沙箱工作区、临时上传）通过此接口访问。
/// </summary>
public interface IFileStorageService
{
    /// <summary>上传文件到指定 Bucket/Key</summary>
    Task<string> UploadAsync(string bucket, string key, Stream content,
                             string contentType, CancellationToken ct = default);

    /// <summary>下载文件，返回内容流</summary>
    Task<Stream> DownloadAsync(string bucket, string key, CancellationToken ct = default);

    /// <summary>删除单个文件</summary>
    Task DeleteAsync(string bucket, string key, CancellationToken ct = default);

    /// <summary>列出指定前缀下的所有对象</summary>
    Task<IReadOnlyList<FileEntry>> ListAsync(string bucket, string prefix,
                                              CancellationToken ct = default);

    /// <summary>判断文件是否存在</summary>
    Task<bool> ExistsAsync(string bucket, string key, CancellationToken ct = default);

    /// <summary>获取预签名下载 URL</summary>
    Task<string> GetPresignedUrlAsync(string bucket, string key,
                                      TimeSpan expiry, CancellationToken ct = default);

    /// <summary>删除指定前缀下的所有对象</summary>
    Task DeletePrefixAsync(string bucket, string prefix, CancellationToken ct = default);
}
