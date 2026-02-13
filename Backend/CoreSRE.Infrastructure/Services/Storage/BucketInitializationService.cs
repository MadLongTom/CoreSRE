using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Minio;
using Minio.DataModel.Args;

namespace CoreSRE.Infrastructure.Services.Storage;

/// <summary>
/// 应用启动时确保所有必要的 S3 Bucket 存在。
/// </summary>
public sealed class BucketInitializationService : IHostedService
{
    private readonly IMinioClient _client;
    private readonly ILogger<BucketInitializationService> _logger;

    /// <summary>系统需要的 Bucket 列表</summary>
    private static readonly string[] RequiredBuckets =
    [
        "coresre-skills",
        "coresre-sandboxes",
        "coresre-uploads"
    ];

    public BucketInitializationService(IMinioClient client, ILogger<BucketInitializationService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[BucketInit] Ensuring required S3 buckets exist...");

        foreach (var bucket in RequiredBuckets)
        {
            try
            {
                var exists = await _client.BucketExistsAsync(
                    new BucketExistsArgs().WithBucket(bucket), cancellationToken);

                if (!exists)
                {
                    await _client.MakeBucketAsync(
                        new MakeBucketArgs().WithBucket(bucket), cancellationToken);
                    _logger.LogInformation("[BucketInit] Created bucket: {Bucket}", bucket);
                }
                else
                {
                    _logger.LogDebug("[BucketInit] Bucket already exists: {Bucket}", bucket);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[BucketInit] Failed to ensure bucket '{Bucket}' (non-fatal)", bucket);
            }
        }

        _logger.LogInformation("[BucketInit] Bucket initialization complete");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
