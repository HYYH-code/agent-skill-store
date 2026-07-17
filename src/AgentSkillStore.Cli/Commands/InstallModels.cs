// -----------------------------------------------------------------------
// <copyright file="InstallModels.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

namespace AgentSkillStore.Cli.Commands;

internal sealed record SkillInstallRecord
{
    public string Name { get; init; } = "";
    public string PackageName { get; init; } = "";
    public string Version { get; init; } = "";
    public string Target { get; init; } = "";
    public string InstallPath { get; init; } = "";
    public IReadOnlyList<string> Files { get; init; } = [];
    public Dictionary<string, string> FileHashes { get; init; } = new(StringComparer.Ordinal);
    public IReadOnlyList<string> GrantedPermissions { get; init; } = [];
    public string ArchiveHash { get; init; } = "";
    public string Server { get; init; } = "";
    public DateTimeOffset InstalledAt { get; init; } = DateTimeOffset.UtcNow;
    public string? PreviousVersion { get; init; }
}

internal sealed record SkillInstallState
{
    public int SchemaVersion { get; init; } = 1;
    public List<SkillInstallRecord> Installations { get; init; } = [];
    public List<SkillInstallRecord> History { get; init; } = [];
}

internal sealed record InstallResult(SkillInstallRecord Record, IReadOnlyList<string> RemovedFiles);

internal readonly record struct SkillReference(string Name, string PackageName, string? Version)
{
    public static SkillReference Parse(string value, string? explicitVersion = null)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Skill name is required.", nameof(value));

        var normalized = value.Trim().Replace('\\', '/').Trim('/');
        string? embeddedVersion = null;
        var separator = normalized.LastIndexOf('@');
        if (separator > 0)
        {
            embeddedVersion = normalized[(separator + 1)..];
            normalized = normalized[..separator];
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(IsUnsafeSegment))
            throw new ArgumentException($"Invalid skill reference: '{value}'.", nameof(value));

        var version = string.IsNullOrWhiteSpace(explicitVersion) ? embeddedVersion : explicitVersion;
        return new SkillReference(string.Join('/', segments), segments[^1], version);
    }

    private static bool IsUnsafeSegment(string segment)
    {
        if (segment is "." or ".." || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return true;

        return segment.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
               segment.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
               segment.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
               segment.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
               (segment.Length == 4 &&
                (segment.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                 segment.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
                segment[3] is >= '1' and <= '9');
    }
}
