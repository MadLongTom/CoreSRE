namespace CoreSRE.Domain.ValueObjects;

/// <summary>
/// AlertRule 健康评分的单项因子
/// </summary>
public sealed record HealthFactor(
    string Name,
    int Weight,
    int Earned,
    string Detail);

/// <summary>
/// AlertRule 健康评分详情（嵌入 AlertRule 实体）
/// </summary>
public sealed record AlertRuleHealthVO
{
    /// <summary>总分（0-100）</summary>
    public int Score { get; init; }

    /// <summary>评分因子</summary>
    public List<HealthFactor> Factors { get; init; } = [];

    /// <summary>改进建议</summary>
    public List<string> Recommendations { get; init; } = [];

    public static AlertRuleHealthVO Create(
        int score,
        List<HealthFactor> factors,
        List<string> recommendations) =>
        new()
        {
            Score = score,
            Factors = factors,
            Recommendations = recommendations
        };
}
