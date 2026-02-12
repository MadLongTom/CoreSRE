using System.Text.Json;

namespace CoreSRE.Infrastructure.Services.Sandbox.Kubernetes;

/// <summary>
/// 从 AIFunctionArguments 中安全提取参数值。
/// 处理 JsonElement 与原始类型的差异。
/// </summary>
internal static class ArgHelper
{
    public static string? GetString(object? value) => value switch
    {
        null => null,
        string s => s,
        JsonElement { ValueKind: JsonValueKind.String } je => je.GetString(),
        JsonElement je => je.ToString(),
        _ => value.ToString(),
    };

    public static int? GetInt(object? value) => value switch
    {
        null => null,
        int i => i,
        long l => (int)l,
        JsonElement { ValueKind: JsonValueKind.Number } je => je.GetInt32(),
        string s when int.TryParse(s, out var i) => i,
        _ => null,
    };

    public static bool? GetBool(object? value) => value switch
    {
        null => null,
        bool b => b,
        JsonElement { ValueKind: JsonValueKind.True } => true,
        JsonElement { ValueKind: JsonValueKind.False } => false,
        string s when bool.TryParse(s, out var b) => b,
        _ => null,
    };
}
