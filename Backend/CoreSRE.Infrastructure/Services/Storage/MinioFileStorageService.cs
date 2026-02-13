using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Minio;
using Minio.DataModel.Args;

namespace CoreSRE.Infrastructure.Services.Storage;

/// <summary>
/// 基于 MinIO (S3 兼容) 的文件存储服务实现。
/// 通过 Aspire CommunityToolkit 的 AddMinioClient 自动注入 IMinioClient。
/// </summary>
public sealed class MinioFileStorageService : IFileStorageService
{
    private readonly IMinioClient _client;
    private readonly ILogger<MinioFileStorageService> _logger;

    public MinioFileStorageService(IMinioClient client, ILogger<MinioFileStorageService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<string> UploadAsync(
        string bucket, string key, Stream content,
        string contentType, CancellationToken ct = default)
    {
        await EnsureBucketAsync(bucket, ct);

        await _client.PutObjectAsync(new PutObjectArgs()
            .WithBucket(bucket)
            .WithObject(key)
            .WithStreamData(content)
            .WithObjectSize(content.Length)
            .WithContentType(contentType), ct);

        _logger.LogDebug("Uploaded {Key} to {Bucket} ({Size} bytes)", key, bucket, content.Length);
        return key;
    }

    public async Task<Stream> DownloadAsync(
        string bucket, string key, CancellationToken ct = default)
    {
        var ms = new MemoryStream();
        await _client.GetObjectAsync(new GetObjectArgs()
            .WithBucket(bucket)
            .WithObject(key)
            .WithCallbackStream(stream => stream.CopyTo(ms)), ct);

        ms.Position = 0;
        return ms;
    }

    public async Task DeleteAsync(
        string bucket, string key, CancellationToken ct = default)
    {
        await _client.RemoveObjectAsync(new RemoveObjectArgs()
            .WithBucket(bucket)
            .WithObject(key), ct);

        _logger.LogDebug("Deleted {Key} from {Bucket}", key, bucket);
    }

    public async Task<IReadOnlyList<FileEntry>> ListAsync(
        string bucket, string prefix, CancellationToken ct = default)
    {
        var entries = new List<FileEntry>();

        var listArgs = new ListObjectsArgs()
            .WithBucket(bucket)
            .WithPrefix(prefix)
            .WithRecursive(true);

        await foreach (var item in _client.ListObjectsEnumAsync(listArgs, ct))
        {
            if (!item.IsDir)
            {
                entries.Add(new FileEntry(
                    item.Key,
                    (long)item.Size,
                    item.LastModifiedDateTime ?? DateTime.UtcNow,
                    item.ContentType));
            }
        }

        return entries;
    }

    public async Task<bool> ExistsAsync(
        string bucket, string key, CancellationToken ct = default)
    {
        try
        {
            await _client.StatObjectAsync(new StatObjectArgs()
                .WithBucket(bucket)
                .WithObject(key), ct);
            return true;
        }
        catch (Minio.Exceptions.ObjectNotFoundException)
        {
            return false;
        }
    }

    public async Task<string> GetPresignedUrlAsync(
        string bucket, string key, TimeSpan expiry, CancellationToken ct = default)
    {
        var url = await _client.PresignedGetObjectAsync(new PresignedGetObjectArgs()
            .WithBucket(bucket)
            .WithObject(key)
            .WithExpiry((int)expiry.TotalSeconds));

        return url;
    }

    public async Task DeletePrefixAsync(
        string bucket, string prefix, CancellationToken ct = default)
    {
        var objects = await ListAsync(bucket, prefix, ct);
        foreach (var obj in objects)
        {
            await _client.RemoveObjectAsync(new RemoveObjectArgs()
                .WithBucket(bucket)
                .WithObject(obj.Key), ct);
        }

        _logger.LogDebug("Deleted {Count} objects under prefix {Prefix} in {Bucket}",
            objects.Count, prefix, bucket);
    }

    private async Task EnsureBucketAsync(string bucket, CancellationToken ct)
    {
        var exists = await _client.BucketExistsAsync(
            new BucketExistsArgs().WithBucket(bucket), ct);
        if (!exists)
        {
            await _client.MakeBucketAsync(
                new MakeBucketArgs().WithBucket(bucket), ct);
            _logger.LogInformation("Created bucket: {Bucket}", bucket);
        }
    }
}
