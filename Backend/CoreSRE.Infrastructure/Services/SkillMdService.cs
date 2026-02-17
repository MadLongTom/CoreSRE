using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Entities;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace CoreSRE.Infrastructure.Services;

/// <summary>
/// Agent Skills 规范 SKILL.md 解析与导出服务。
/// 使用 YamlDotNet 进行 YAML frontmatter 的序列化与反序列化。
/// https://agentskills.io/specification
/// </summary>
public sealed partial class SkillMdService : ISkillMdService
{
    private readonly IFileStorageService _fileStorage;
    private const string Bucket = "coresre-skills";

    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(HyphenatedNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer YamlSerializer = new SerializerBuilder()
        .WithNamingConvention(HyphenatedNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitEmptyCollections)
        .Build();

    public SkillMdService(IFileStorageService fileStorage)
    {
        _fileStorage = fileStorage;
    }

    // ─────────── Parse ───────────

    /// <inheritdoc />
    public SkillMdParseResult Parse(string skillMdContent)
    {
        var result = new SkillMdParseResult();

        // Split frontmatter from body
        var match = FrontmatterPattern().Match(skillMdContent);
        if (!match.Success)
        {
            result.Errors.Add("SKILL.md must start with YAML frontmatter delimited by ---");
            return result;
        }

        var frontmatterYaml = match.Groups[1].Value;
        result.Body = skillMdContent[(match.Index + match.Length)..].TrimStart('\r', '\n');

        // Deserialize frontmatter via YamlDotNet
        try
        {
            var fm = YamlDeserializer.Deserialize<SkillFrontmatter>(frontmatterYaml);
            if (fm is null)
            {
                result.Errors.Add("Failed to parse YAML frontmatter.");
                return result;
            }

            result.Name = fm.Name ?? string.Empty;
            result.Description = fm.Description ?? string.Empty;
            result.License = fm.License;
            result.Compatibility = fm.Compatibility;
            result.AllowedTools = fm.AllowedTools;
            result.Metadata = fm.Metadata is { Count: > 0 } ? fm.Metadata : null;
        }
        catch (Exception ex)
        {
            result.Errors.Add($"YAML parse error: {ex.Message}");
            return result;
        }

        // Validate required fields
        if (string.IsNullOrWhiteSpace(result.Name))
            result.Errors.Add("'name' is required in frontmatter.");
        else if (!SkillRegistration.IsValidName(result.Name))
            result.Errors.Add($"name '{result.Name}' does not match Agent Skills spec (lowercase alphanumeric + hyphens, ≤64 chars).");

        if (string.IsNullOrWhiteSpace(result.Description))
            result.Errors.Add("'description' is required in frontmatter.");
        else if (result.Description.Length > 1024)
            result.Errors.Add("'description' must be ≤1024 characters.");

        return result;
    }

    // ─────────── Export SKILL.md ───────────

    /// <inheritdoc />
    public string Export(SkillRegistration skill,
        IReadOnlyDictionary<Guid, string>? resolvedAllowedToolNames = null)
    {
        var fm = new SkillFrontmatter
        {
            Name = skill.Name,
            Description = skill.Description,
            License = string.IsNullOrWhiteSpace(skill.License) ? null : skill.License,
            Compatibility = string.IsNullOrWhiteSpace(skill.Compatibility) ? null : skill.Compatibility,
            Metadata = skill.Metadata is { Count: > 0 } ? skill.Metadata : null,
        };

        // Resolve AllowedTools (Guid list) → space-separated tool names
        if (skill.AllowedTools is { Count: > 0 } && resolvedAllowedToolNames is { Count: > 0 })
        {
            var names = skill.AllowedTools
                .Where(resolvedAllowedToolNames.ContainsKey)
                .Select(id => resolvedAllowedToolNames[id])
                .ToList();
            if (names.Count > 0)
                fm.AllowedTools = string.Join(" ", names);
        }

        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.Append(YamlSerializer.Serialize(fm).TrimEnd());
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.Append(skill.Content);
        return sb.ToString();
    }

    // ─────────── Export ZIP ───────────

    /// <inheritdoc />
    public async Task<byte[]> ExportZipAsync(
        SkillRegistration skill,
        IReadOnlyDictionary<Guid, string>? resolvedAllowedToolNames = null,
        CancellationToken cancellationToken = default)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            // SKILL.md
            var skillMd = zip.CreateEntry($"{skill.Name}/SKILL.md");
            await using (var writer = new StreamWriter(skillMd.Open(), Encoding.UTF8))
            {
                await writer.WriteAsync(Export(skill, resolvedAllowedToolNames));
            }

            // Files from S3 (always attempt listing regardless of HasFiles flag)
            {
                var files = await _fileStorage.ListAsync(Bucket, $"{skill.Id}/", cancellationToken);
                foreach (var file in files)
                {
                    // file.Key = "{skillId}/scripts/foo.py" → entry = "{name}/scripts/foo.py"
                    var relativePath = file.Key;
                    if (relativePath.StartsWith($"{skill.Id}/"))
                        relativePath = relativePath[($"{skill.Id}/".Length)..];

                    var entry = zip.CreateEntry($"{skill.Name}/{relativePath}");
                    await using var entryStream = entry.Open();
                    using var sourceStream = await _fileStorage.DownloadAsync(
                        Bucket, file.Key, cancellationToken);
                    await sourceStream.CopyToAsync(entryStream, cancellationToken);
                }
            }
        }

        return ms.ToArray();
    }

    // ─────────── Import ZIP ───────────

    /// <inheritdoc />
    public async Task<SkillMdImportResult> ImportZipAsync(
        Stream zipStream, Guid skillId, CancellationToken cancellationToken = default)
    {
        var result = new SkillMdImportResult();

        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read);

        // Find SKILL.md (may be at root or inside a single directory)
        var skillMdEntry = zip.Entries.FirstOrDefault(e =>
            e.Name.Equals("SKILL.md", StringComparison.OrdinalIgnoreCase)
            && e.FullName.Split('/').Length <= 2);

        if (skillMdEntry is null)
        {
            result.Errors.Add("ZIP must contain a SKILL.md file (at root or in a single subdirectory).");
            return result;
        }

        // Determine the prefix directory (e.g., "skill-name/")
        var prefix = skillMdEntry.FullName.Contains('/')
            ? skillMdEntry.FullName[..skillMdEntry.FullName.LastIndexOf('/')]
            : "";

        // Parse SKILL.md
        using var reader = new StreamReader(skillMdEntry.Open(), Encoding.UTF8);
        var content = await reader.ReadToEndAsync(cancellationToken);
        result.ParseResult = Parse(content);

        if (result.ParseResult.HasErrors)
        {
            result.Errors.AddRange(result.ParseResult.Errors);
            return result;
        }

        // Upload other files to S3 under skillId/
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName == skillMdEntry.FullName) continue;
            if (string.IsNullOrEmpty(entry.Name)) continue; // directory entry

            var relativePath = string.IsNullOrEmpty(prefix)
                ? entry.FullName
                : entry.FullName[(prefix.Length + 1)..];

            var key = $"{skillId}/{relativePath}";
            await using var entryStream = entry.Open();
            using var entryMs = new MemoryStream();
            await entryStream.CopyToAsync(entryMs, cancellationToken);
            entryMs.Position = 0;

            await _fileStorage.UploadAsync(
                Bucket, key, entryMs, "application/octet-stream", cancellationToken);
            result.UploadedFiles.Add(relativePath);
        }

        result.HasFiles = result.UploadedFiles.Count > 0;
        return result;
    }

    // ─────────── Internal YAML model ───────────

    /// <summary>YAML frontmatter POCO — used by YamlDotNet for (de)serialization</summary>
    private sealed class SkillFrontmatter
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? License { get; set; }
        public string? Compatibility { get; set; }
        [YamlMember(Alias = "allowed-tools")]
        public string? AllowedTools { get; set; }
        public Dictionary<string, string>? Metadata { get; set; }
    }

    [GeneratedRegex(@"^---\s*\n([\s\S]*?)\n---", RegexOptions.Multiline)]
    private static partial Regex FrontmatterPattern();
}
