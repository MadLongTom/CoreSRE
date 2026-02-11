namespace CoreSRE.Application.Tools.DTOs;

/// <summary>
/// OpenAPI 导入结果 DTO
/// </summary>
public class OpenApiImportResultDto
{
    /// <summary>OpenAPI 文档中的操作总数</summary>
    public int TotalOperations { get; set; }

    /// <summary>成功导入的工具数</summary>
    public int ImportedCount { get; set; }

    /// <summary>跳过的工具数（如名称冲突等）</summary>
    public int SkippedCount { get; set; }

    /// <summary>导入的工具列表</summary>
    public List<ToolRegistrationDto> Tools { get; set; } = [];

    /// <summary>导入过程中的错误信息</summary>
    public List<string> Errors { get; set; } = [];
}
