using CoreSRE.Application.Common.Models;
using CoreSRE.Domain.ValueObjects;
using MediatR;

namespace CoreSRE.Application.DataSources.Commands.DiscoverMetadata;

/// <summary>发现数据源元数据命令（标签/服务/索引等）</summary>
public record DiscoverDataSourceMetadataCommand(Guid DataSourceId)
    : IRequest<Result<DataSourceMetadataVO>>;
