using System.Text.Json;

namespace CoreSRE.Application.Tools.DTOs;

/// <summary>
/// 工具调用标准化结果 DTO
/// </summary>
public class ToolInvocationResultDto
{
    /// <summary>调用是否成功</summary>
    public bool Success { get; set; }

    /// <summary>调用返回数据</summary>
    public JsonElement? Data { get; set; }

    /// <summary>错误信息</summary>
    public string? Error { get; set; }

    /// <summary>调用耗时（毫秒）</summary>
    public long DurationMs { get; set; }

    /// <summary>关联的 ToolRegistration ID</summary>
    public Guid ToolRegistrationId { get; set; }

    /// <summary>调用时间</summary>
    public DateTime InvokedAt { get; set; }
}
