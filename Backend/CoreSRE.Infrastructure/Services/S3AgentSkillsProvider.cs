using System.Security;
using System.Text;
using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Entities;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CoreSRE.Infrastructure.Services;

/// <summary>
/// An <see cref="AIContextProvider"/> that exposes Agent Skills backed by S3 (MinIO) storage.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors the progressive disclosure pattern from the
/// <see href="https://agentskills.io/">Agent Skills specification</see>
/// used by the framework's <c>FileAgentSkillsProvider</c>, but instead of reading
/// from the local filesystem, skills are loaded from the database
/// (<see cref="SkillRegistration.Content"/>) and resource files are read from S3
/// via <see cref="IFileStorageService"/>.
/// </para>
/// <list type="number">
///   <item><description><strong>Advertise</strong> — skill names and descriptions are injected into the system prompt (~100 tokens per skill).</description></item>
///   <item><description><strong>Load</strong> — the full SKILL.md body is returned via the <c>load_skill</c> tool.</description></item>
///   <item><description><strong>Read resources</strong> — supplementary files are read from S3 on demand via the <c>read_skill_resource</c> tool.</description></item>
/// </list>
/// </remarks>
public sealed partial class S3AgentSkillsProvider : AIContextProvider
{
    private const string SkillsBucket = "coresre-skills";

    private const string DefaultSkillsInstructionPrompt =
        """
        You have access to skills containing domain-specific knowledge and capabilities.
        Each skill provides specialized instructions, reference documents, and assets for specific tasks.

        <available_skills>
        {skills}
        </available_skills>

        When a task aligns with a skill's domain:
        - Use `load_skill` to retrieve the skill's instructions
        - Follow the provided guidance
        - Use `read_skill_resource` to read any references or other files mentioned by the skill, always using the full path as written (e.g. `references/FAQ.md`, not just `FAQ.md`)
        Only load what is needed, when it is needed.
        """;

    private readonly Dictionary<string, SkillRegistration> _skillMap;
    private readonly IFileStorageService? _fileStorage;
    private readonly ILogger _logger;
    private readonly IEnumerable<AITool> _tools;
    private readonly string? _skillsInstructionPrompt;

    /// <summary>
    /// Initializes a new instance of the <see cref="S3AgentSkillsProvider"/> class.
    /// </summary>
    /// <param name="skills">The pre-loaded skill registrations for this agent.</param>
    /// <param name="fileStorage">The S3-compatible file storage service (required when any skill has files).</param>
    /// <param name="sandboxEnabled">Whether the agent has sandbox enabled (controls read_skill_resource availability).</param>
    /// <param name="options">Optional configuration for prompt customization.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    public S3AgentSkillsProvider(
        IReadOnlyList<SkillRegistration> skills,
        IFileStorageService? fileStorage = null,
        bool sandboxEnabled = false,
        S3AgentSkillsProviderOptions? options = null,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(skills);

        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<S3AgentSkillsProvider>();
        _fileStorage = fileStorage;

        // Build case-insensitive name → entity map
        _skillMap = skills.ToDictionary(s => s.Name, s => s, StringComparer.OrdinalIgnoreCase);

        // Build prompt
        _skillsInstructionPrompt = BuildSkillsInstructionPrompt(options, _skillMap);

        // Register AIFunction tools
        var toolList = new List<AITool>
        {
            AIFunctionFactory.Create(
                LoadSkill,
                name: "load_skill",
                description: "Loads the full instructions for a specific skill."),
        };

        // Only register read_skill_resource when sandbox is enabled AND at least one skill has files
        var hasFileSkills = sandboxEnabled && skills.Any(s => s.HasFiles);
        if (hasFileSkills && _fileStorage is not null)
        {
            toolList.Add(AIFunctionFactory.Create(
                ReadSkillResourceAsync,
                name: "read_skill_resource",
                description: "Reads a file associated with a skill, such as references or assets."));
        }

        _tools = toolList;
    }

    /// <summary>
    /// Gets the skills loaded into this provider, keyed by name (case-insensitive).
    /// </summary>
    public IReadOnlyDictionary<string, SkillRegistration> Skills => _skillMap;

    /// <inheritdoc />
    protected override ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        if (_skillMap.Count == 0)
        {
            return base.ProvideAIContextAsync(context, cancellationToken);
        }

        return new ValueTask<AIContext>(new AIContext
        {
            Instructions = _skillsInstructionPrompt,
            Tools = _tools,
        });
    }

    // ─────────────────────────────────────────────────────────────────
    // Tool implementations
    // ─────────────────────────────────────────────────────────────────

    private string LoadSkill(string skillName)
    {
        if (string.IsNullOrWhiteSpace(skillName))
        {
            return "Error: Skill name cannot be empty.";
        }

        if (!_skillMap.TryGetValue(skillName, out var skill))
        {
            return $"Error: Skill '{skillName}' not found. Available: {string.Join(", ", _skillMap.Keys)}";
        }

        LogSkillLoading(_logger, skillName);

        return $"# Skill: {skill.Name}\n\n{skill.Content}";
    }

    private async Task<string> ReadSkillResourceAsync(
        string skillName,
        string resourceName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(skillName))
        {
            return "Error: Skill name cannot be empty.";
        }

        if (string.IsNullOrWhiteSpace(resourceName))
        {
            return "Error: Resource name cannot be empty.";
        }

        if (!_skillMap.TryGetValue(skillName, out var skill))
        {
            return $"Error: Skill '{skillName}' not found.";
        }

        if (!skill.HasFiles)
        {
            return $"Error: Skill '{skillName}' has no file package.";
        }

        if (_fileStorage is null)
        {
            return "Error: File storage service is not available.";
        }

        // S3 key: {skillId}/{resourcePath}
        var key = $"{skill.Id}/{resourceName}";

        try
        {
            var exists = await _fileStorage.ExistsAsync(SkillsBucket, key, cancellationToken);
            if (!exists)
            {
                return $"Error: Resource '{resourceName}' not found in skill '{skillName}'.";
            }

            using var stream = await _fileStorage.DownloadAsync(SkillsBucket, key, cancellationToken);
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            LogResourceReadError(_logger, skillName, resourceName, ex);
            return $"Error: Failed to read resource '{resourceName}' from skill '{skillName}'.";
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // Prompt builder
    // ─────────────────────────────────────────────────────────────────

    private static string? BuildSkillsInstructionPrompt(
        S3AgentSkillsProviderOptions? options,
        Dictionary<string, SkillRegistration> skills)
    {
        if (skills.Count == 0)
        {
            return null;
        }

        var promptTemplate = options?.SkillsInstructionPrompt ?? DefaultSkillsInstructionPrompt;

        var sb = new StringBuilder();

        // Order by name for deterministic prompt output
        foreach (var skill in skills.Values.OrderBy(s => s.Name, StringComparer.Ordinal))
        {
            sb.AppendLine("  <skill>");
            sb.AppendLine($"    <name>{SecurityElement.Escape(skill.Name)}</name>");
            sb.AppendLine($"    <description>{SecurityElement.Escape(skill.Description)}</description>");

            if (skill.HasFiles)
            {
                sb.AppendLine("    <has_resources>true</has_resources>");
            }

            sb.AppendLine("  </skill>");
        }

        return promptTemplate.Replace("{skills}", sb.ToString().TrimEnd());
    }

    // ─────────────────────────────────────────────────────────────────
    // Logging
    // ─────────────────────────────────────────────────────────────────

    [LoggerMessage(LogLevel.Information, "Loading skill from DB: {SkillName}")]
    private static partial void LogSkillLoading(ILogger logger, string skillName);

    [LoggerMessage(LogLevel.Error, "Failed to read S3 resource '{ResourceName}' from skill '{SkillName}'")]
    private static partial void LogResourceReadError(ILogger logger, string skillName, string resourceName, Exception exception);
}
