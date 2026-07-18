// -----------------------------------------------------------------------
// <copyright file="GitExcludeManager.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

namespace AgentSkillStore.Cli.Commands;

internal static class GitExcludeManager
{
    public static void AddManagedPaths(SkillStoreContext context, string packageName, bool hasClaudeBridge)
    {
        if (context.Scope != "project" || context.ProjectRoot is null)
            return;

        var excludePath = ResolveExcludePath(context.ProjectRoot);
        if (excludePath is null)
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(excludePath)!);
        var existing = File.Exists(excludePath)
            ? File.ReadAllLines(excludePath).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<string>
        {
            "/.agents/.skillstore/",
            $"/.agents/skills/{packageName}"
        };
        if (hasClaudeBridge)
            entries.Add($"/.claude/skills/{packageName}");

        var additions = entries.Where(existing.Add).ToList();
        if (additions.Count == 0)
            return;

        using var writer = new StreamWriter(excludePath, append: true);
        if (new FileInfo(excludePath).Length > 0)
            writer.WriteLine();
        writer.WriteLine("# Agent Skill Store managed paths");
        foreach (var entry in additions)
            writer.WriteLine(entry);
    }

    private static string? ResolveExcludePath(string projectRoot)
    {
        var dotGit = Path.Combine(projectRoot, ".git");
        if (Directory.Exists(dotGit))
            return Path.Combine(dotGit, "info", "exclude");
        if (!File.Exists(dotGit))
            return null;

        var line = File.ReadLines(dotGit).FirstOrDefault();
        if (line is null || !line.StartsWith("gitdir:", StringComparison.OrdinalIgnoreCase))
            return null;
        var gitDirectory = line[7..].Trim();
        if (!Path.IsPathRooted(gitDirectory))
            gitDirectory = Path.GetFullPath(Path.Combine(projectRoot, gitDirectory));
        return Path.Combine(gitDirectory, "info", "exclude");
    }
}
