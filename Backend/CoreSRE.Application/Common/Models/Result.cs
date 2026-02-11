namespace CoreSRE.Application.Common.Models;

/// <summary>
/// 统一返回结果
/// </summary>
public class Result<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string? Message { get; set; }
    public List<string>? Errors { get; set; }

    /// <summary>
    /// 错误码，用于表达 HTTP 状态码语义（如 404, 409）。
    /// Application 层对 HTTP 无感知，仅是整数语义，API 层可直接映射。
    /// </summary>
    public int? ErrorCode { get; set; }

    public static Result<T> Ok(T data, string? message = null) => new()
    {
        Success = true,
        Data = data,
        Message = message
    };

    public static Result<T> Fail(string message, List<string>? errors = null, int? errorCode = null) => new()
    {
        Success = false,
        Message = message,
        Errors = errors,
        ErrorCode = errorCode
    };

    /// <summary>资源未找到（404 语义）</summary>
    public static Result<T> NotFound(string message = "Resource not found.") => new()
    {
        Success = false,
        Message = message,
        ErrorCode = 404
    };

    /// <summary>冲突（409 语义，如名称重复）</summary>
    public static Result<T> Conflict(string message = "Resource already exists.") => new()
    {
        Success = false,
        Message = message,
        ErrorCode = 409
    };

    /// <summary>网关错误（502 语义，如外部服务调用失败）</summary>
    public static Result<T> BadGateway(string message, List<string>? errors = null) => new()
    {
        Success = false,
        Message = message,
        Errors = errors,
        ErrorCode = 502
    };
}
