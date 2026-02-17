using CoreSRE.Application.Common.Models;
using CoreSRE.Application.DataSources.Commands.DeleteDataSource;
using CoreSRE.Application.DataSources.Commands.DiscoverMetadata;
using CoreSRE.Application.DataSources.Commands.QueryDataSource;
using CoreSRE.Application.DataSources.Commands.RegisterDataSource;
using CoreSRE.Application.DataSources.Commands.TestDataSourceConnection;
using CoreSRE.Application.DataSources.Commands.UpdateDataSource;
using CoreSRE.Application.DataSources.Queries.GetDataSourceById;
using CoreSRE.Application.DataSources.Queries.GetDataSources;
using CoreSRE.Domain.Enums;
using CoreSRE.Domain.ValueObjects;
using MediatR;

namespace CoreSRE.Endpoints;

/// <summary>
/// DataSource 数据源注册与管理端点
/// </summary>
public static class DataSourceEndpoints
{
    public static IEndpointRouteBuilder MapDataSourceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/datasources")
            .WithTags("DataSources")
            .WithOpenApi();

        group.MapPost("/", RegisterDataSource);
        group.MapGet("/", GetDataSources);
        group.MapGet("/{id:guid}", GetDataSourceById);
        group.MapPut("/{id:guid}", UpdateDataSource);
        group.MapDelete("/{id:guid}", DeleteDataSource);
        group.MapPost("/{id:guid}/query", QueryDataSource);
        group.MapPost("/{id:guid}/test", TestDataSourceConnection);
        group.MapPost("/{id:guid}/discover", DiscoverMetadata);

        return app;
    }

    /// <summary>POST /api/datasources — 注册新数据源</summary>
    private static async Task<IResult> RegisterDataSource(
        RegisterDataSourceCommand command,
        ISender sender)
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

        return Results.Created($"/api/datasources/{result.Data!.Id}", result);
    }

    /// <summary>GET /api/datasources — 查询数据源列表（支持分页、分类/状态过滤、关键词搜索）</summary>
    private static async Task<IResult> GetDataSources(
        ISender sender,
        string? category = null,
        string? status = null,
        string? search = null,
        int page = 1,
        int pageSize = 20)
    {
        DataSourceCategory? parsedCategory = null;
        if (category is not null)
        {
            if (!Enum.TryParse<DataSourceCategory>(category, ignoreCase: true, out var c))
                return Results.BadRequest(new { success = false, message = $"Invalid category. Must be one of: {string.Join(", ", Enum.GetNames<DataSourceCategory>())}." });
            parsedCategory = c;
        }

        DataSourceStatus? parsedStatus = null;
        if (status is not null)
        {
            if (!Enum.TryParse<DataSourceStatus>(status, ignoreCase: true, out var s))
                return Results.BadRequest(new { success = false, message = $"Invalid status. Must be one of: {string.Join(", ", Enum.GetNames<DataSourceStatus>())}." });
            parsedStatus = s;
        }

        var result = await sender.Send(new GetDataSourcesQuery(parsedCategory, parsedStatus, search, page, pageSize));
        return Results.Ok(result);
    }

    /// <summary>GET /api/datasources/{id} — 获取数据源详情</summary>
    private static async Task<IResult> GetDataSourceById(
        Guid id,
        ISender sender)
    {
        var result = await sender.Send(new GetDataSourceByIdQuery(id));

        if (!result.Success)
        {
            return result.ErrorCode switch
            {
                404 => Results.NotFound(result),
                _ => Results.BadRequest(result)
            };
        }

        return Results.Ok(result);
    }

    /// <summary>PUT /api/datasources/{id} — 更新数据源配置</summary>
    private static async Task<IResult> UpdateDataSource(
        Guid id,
        UpdateDataSourceCommand command,
        ISender sender)
    {
        var commandWithId = command with { Id = id };
        var result = await sender.Send(commandWithId);

        if (!result.Success)
        {
            return result.ErrorCode switch
            {
                404 => Results.NotFound(result),
                409 => Results.Conflict(result),
                _ => Results.BadRequest(result)
            };
        }

        return Results.Ok(result);
    }

    /// <summary>DELETE /api/datasources/{id} — 删除数据源</summary>
    private static async Task<IResult> DeleteDataSource(
        Guid id,
        ISender sender)
    {
        var result = await sender.Send(new DeleteDataSourceCommand(id));

        if (!result.Success)
        {
            return result.ErrorCode switch
            {
                404 => Results.NotFound(result),
                _ => Results.BadRequest(result)
            };
        }

        return Results.NoContent();
    }

    /// <summary>POST /api/datasources/{id}/query — 统一数据源查询</summary>
    private static async Task<IResult> QueryDataSource(
        Guid id,
        DataSourceQueryVO query,
        ISender sender)
    {
        var result = await sender.Send(new QueryDataSourceCommand { DataSourceId = id, Query = query });

        if (!result.Success)
        {
            return result.ErrorCode switch
            {
                404 => Results.NotFound(result),
                502 => Results.Json(result, statusCode: 502),
                _ => Results.BadRequest(result)
            };
        }

        return Results.Ok(result);
    }

    /// <summary>POST /api/datasources/{id}/test — 测试数据源连接</summary>
    private static async Task<IResult> TestDataSourceConnection(
        Guid id,
        ISender sender)
    {
        var result = await sender.Send(new TestDataSourceConnectionCommand(id));

        if (!result.Success)
        {
            return result.ErrorCode switch
            {
                404 => Results.NotFound(result),
                502 => Results.Json(result, statusCode: 502),
                _ => Results.BadRequest(result)
            };
        }

        return Results.Ok(result);
    }

    /// <summary>POST /api/datasources/{id}/discover — 发现数据源元数据</summary>
    private static async Task<IResult> DiscoverMetadata(
        Guid id,
        ISender sender)
    {
        var result = await sender.Send(new DiscoverDataSourceMetadataCommand(id));

        if (!result.Success)
        {
            return result.ErrorCode switch
            {
                404 => Results.NotFound(result),
                502 => Results.Json(result, statusCode: 502),
                _ => Results.BadRequest(result)
            };
        }

        return Results.Ok(result);
    }
}
