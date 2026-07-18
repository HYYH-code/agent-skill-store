// -----------------------------------------------------------------------
// <copyright file="SkillStoreContext.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

namespace AgentSkillStore.Cli.Commands;

internal sealed record SkillStoreContext
{
    public required string Scope { get; init; }
    public required string AgentsRoot { get; init; }
    public required string StateRoot { get; init; }
    public required string PackagesRoot { get; init; }
    public required string SkillsRoot { get; init; }
    public required string OperationLockPath { get; init; }
    public string? ProjectRoot { get; init; }
    public string? LockFilePath { get; init; }

    public static SkillStoreContext Resolve(
        string? scope,
        string? explicitAgentsRoot = null,
        string? workingDirectory = null)
    {
        var normalizedScope = string.IsNullOrWhiteSpace(scope) ? "user" : scope.Trim().ToLowerInvariant();
        if (normalizedScope is not ("user" or "project"))
            throw new ArgumentException("Scope must be one of: user, project.", nameof(scope));

        string agentsRoot;
        string? projectRoot = null;
        string? lockFilePath = null;
        if (!string.IsNullOrWhiteSpace(explicitAgentsRoot))
        {
            agentsRoot = Path.GetFullPath(explicitAgentsRoot);
            if (normalizedScope == "project")
            {
                projectRoot = FindProjectRoot(workingDirectory ?? Directory.GetCurrentDirectory());
                lockFilePath = Path.Combine(projectRoot, "skillstore.lock.json");
            }
        }
        else if (normalizedScope == "project")
        {
            projectRoot = FindProjectRoot(workingDirectory ?? Directory.GetCurrentDirectory());
            agentsRoot = Path.Combine(projectRoot, ".agents");
            lockFilePath = Path.Combine(projectRoot, "skillstore.lock.json");
        }
        else
        {
            agentsRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".agents");
        }

        var stateRoot = Path.Combine(agentsRoot, ".skillstore");
        return new SkillStoreContext
        {
            Scope = normalizedScope,
            AgentsRoot = agentsRoot,
            StateRoot = stateRoot,
            PackagesRoot = Path.Combine(stateRoot, "packages"),
            SkillsRoot = Path.Combine(agentsRoot, "skills"),
            OperationLockPath = Path.Combine(stateRoot, "operation.lock"),
            ProjectRoot = projectRoot,
            LockFilePath = lockFilePath
        };
    }

    public static SkillStoreContext FromRecord(SkillInstallRecord record)
    {
        if (!string.Equals(record.Layout, "shared", StringComparison.Ordinal))
            throw new ArgumentException("Only shared installation records have an .agents context.", nameof(record));
        var activePath = Path.GetFullPath(record.ActivePath);
        var skillsRoot = Path.GetDirectoryName(activePath)
                         ?? throw new IOException($"Invalid active Skill path: {activePath}");
        var agentsRoot = Path.GetDirectoryName(skillsRoot)
                         ?? throw new IOException($"Invalid .agents root: {skillsRoot}");
        var stateRoot = Path.Combine(agentsRoot, ".skillstore");
        return new SkillStoreContext
        {
            Scope = record.Scope,
            AgentsRoot = agentsRoot,
            StateRoot = stateRoot,
            PackagesRoot = Path.Combine(stateRoot, "packages"),
            SkillsRoot = skillsRoot,
            OperationLockPath = Path.Combine(stateRoot, "operation.lock"),
            ProjectRoot = record.ProjectRoot,
            LockFilePath = record.Scope == "project" && record.ProjectRoot is not null
                ? Path.Combine(record.ProjectRoot, "skillstore.lock.json")
                : null
        };
    }

    public string GetPackagePath(string packageName, string version, string digest)
    {
        var digestDirectory = NormalizeDigestDirectory(digest);
        return EnsureChildPath(PackagesRoot, Path.Combine(packageName, version, digestDirectory));
    }

    public string GetActivePath(string packageName) => EnsureChildPath(SkillsRoot, packageName);

    public string GetClaudeBridgePath(string packageName)
    {
        var root = Scope == "project"
            ? ProjectRoot ?? throw new InvalidOperationException("Project scope has no project root.")
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return EnsureChildPath(Path.Combine(root, ".claude", "skills"), packageName);
    }

    public static string FindProjectRoot(string startPath)
    {
        var current = new DirectoryInfo(Path.GetFullPath(startPath));
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")) ||
                File.Exists(Path.Combine(current.FullName, ".git")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return Path.GetFullPath(startPath);
    }

    public static string EnsureChildPath(string root, string relativePath)
    {
        var normalizedRoot = Path.GetFullPath(root);
        var child = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!child.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison) &&
            !string.Equals(child, normalizedRoot, comparison))
        {
            throw new IOException($"Path escapes the managed root: {relativePath}");
        }

        return child;
    }

    private static string NormalizeDigestDirectory(string digest)
    {
        var value = digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            ? digest[7..]
            : digest;
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException($"Invalid SHA-256 digest: {digest}");
        return value.ToLowerInvariant();
    }
}
