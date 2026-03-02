namespace CoreSRE.Infrastructure.Services;

/// <summary>
/// Configuration options for <see cref="S3AgentSkillsProvider"/>.
/// </summary>
public sealed class S3AgentSkillsProviderOptions
{
    /// <summary>
    /// Gets or sets a custom system prompt template for advertising skills.
    /// Use <c>{skills}</c> as the placeholder for the generated skills list.
    /// When <see langword="null"/>, a default template is used.
    /// </summary>
    public string? SkillsInstructionPrompt { get; set; }
}
