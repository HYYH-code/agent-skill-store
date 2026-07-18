// -----------------------------------------------------------------------
// <copyright file="DoctorCommand.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text.Json;
using AgentSkillStore.Cli.Json;
using AgentSkillStore.Cli.Output;

namespace AgentSkillStore.Cli.Commands;

internal static class DoctorCommand
{
    public static int Execute(ParsedArgs args)
    {
        try
        {
            var context = SkillStoreContext.Resolve(InstallCommand.ResolveScope(args), args.InstallRoot);
            var state = new InstallationRegistry().Load();
            var findings = Diagnose(state, context);
            if (args.OutputFormat == "json")
            {
                Console.WriteLine(JsonSerializer.Serialize(
                    (IReadOnlyList<SkillDoctorFinding>)findings,
                    CliJsonContext.Default.IReadOnlyListSkillDoctorFinding));
            }
            else if (findings.Count == 0)
            {
                ConsoleOutput.WriteSuccess($"No problems found in {context.Scope} scope.");
            }
            else
            {
                foreach (var finding in findings)
                    Console.WriteLine($"{finding.Severity.ToUpperInvariant(),-7} {finding.Code,-24} {finding.Skill} {finding.Message}");
            }

            return findings.Any(finding => finding.Severity == "error") ? 1 : 0;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException)
        {
            ConsoleOutput.WriteError($"Error: {exception.Message}");
            return 1;
        }
    }

    internal static List<SkillDoctorFinding> Diagnose(SkillInstallState state, SkillStoreContext context)
    {
        var findings = new List<SkillDoctorFinding>();
        foreach (var legacy in state.Installations.Where(record =>
                     !string.Equals(record.Layout, "shared", StringComparison.Ordinal) &&
                     string.Equals(record.Scope, context.Scope, StringComparison.Ordinal) &&
                     ProjectRootsEqual(record.ProjectRoot, context.ProjectRoot)))
        {
            findings.Add(Finding("warning", "LEGACY_LAYOUT", legacy.Name,
                $"Direct installation at {legacy.InstallPath}; run skillstore migrate --dry-run."));
        }

        var records = state.Installations.Where(record =>
            string.Equals(record.Layout, "shared", StringComparison.Ordinal) &&
            string.Equals(record.Scope, context.Scope, StringComparison.Ordinal) &&
            ProjectRootsEqual(record.ProjectRoot, context.ProjectRoot)).ToList();
        foreach (var record in records)
        {
            if (record.ReusesUserInstall)
            {
                var userRecord = state.Installations.FirstOrDefault(candidate =>
                    string.Equals(candidate.Layout, "shared", StringComparison.Ordinal) &&
                    string.Equals(candidate.Scope, "user", StringComparison.Ordinal) &&
                    string.Equals(candidate.Name, record.Name, StringComparison.OrdinalIgnoreCase));
                if (userRecord is null ||
                    !string.Equals(NormalizeDigest(userRecord.ArchiveHash), NormalizeDigest(record.ArchiveHash), StringComparison.OrdinalIgnoreCase))
                {
                    findings.Add(Finding("error", "REUSED_USER_MISSING", record.Name,
                        "Project lock reuses a user installation that is missing or has changed."));
                }
                DiagnoseBridges(findings, record);
                continue;
            }

            if (!Directory.Exists(record.StorePath))
            {
                findings.Add(Finding("error", "PACKAGE_MISSING", record.Name, record.StorePath));
                continue;
            }

            IReadOnlyList<string> actualFiles;
            try
            {
                actualFiles = DirectoryLinkManager.EnumerateRegularFiles(record.StorePath);
            }
            catch (IOException exception)
            {
                findings.Add(Finding("error", "PACKAGE_LINK_CONFLICT", record.Name, exception.Message));
                continue;
            }

            var expectedFiles = record.Files.ToHashSet(PathComparer);
            foreach (var actualFile in actualFiles)
            {
                var relativePath = Path.GetRelativePath(record.StorePath, actualFile).Replace('\\', '/');
                if (!expectedFiles.Contains(relativePath))
                    findings.Add(Finding("error", "FILE_UNTRACKED", record.Name, relativePath));
            }

            var activeTarget = DirectoryLinkManager.ResolveTarget(record.ActivePath);
            if (!DirectoryLinkManager.PathsEqual(activeTarget, record.StorePath))
                findings.Add(Finding("error", "ACTIVE_LINK_INVALID", record.Name, record.ActivePath));

            foreach (var file in record.Files)
            {
                var path = SkillStoreContext.EnsureChildPath(record.StorePath, file);
                if (!File.Exists(path))
                {
                    findings.Add(Finding("error", "FILE_MISSING", record.Name, file));
                    continue;
                }
                if (!record.FileHashes.TryGetValue(file, out var expected))
                {
                    findings.Add(Finding("error", "HASH_MISSING", record.Name, file));
                    continue;
                }
                var actual = $"sha256:{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant()}";
                if (!string.Equals(actual, NormalizeDigest(expected), StringComparison.OrdinalIgnoreCase))
                    findings.Add(Finding("error", "FILE_MODIFIED", record.Name, file));
            }

            DiagnoseBridges(findings, record);
        }

        if (context.Scope == "project")
        {
            foreach (var projectRecord in records)
            {
                var userRecord = state.Installations.FirstOrDefault(candidate =>
                    string.Equals(candidate.Layout, "shared", StringComparison.Ordinal) &&
                    string.Equals(candidate.Scope, "user", StringComparison.Ordinal) &&
                    string.Equals(candidate.Name, projectRecord.Name, StringComparison.OrdinalIgnoreCase));
                if (userRecord is not null &&
                    !string.Equals(NormalizeDigest(userRecord.ArchiveHash), NormalizeDigest(projectRecord.ArchiveHash), StringComparison.OrdinalIgnoreCase))
                {
                    findings.Add(Finding("error", "SCOPE_VERSION_CONFLICT", projectRecord.Name,
                        $"User {userRecord.Version} conflicts with project {projectRecord.Version}."));
                }
            }
        }

        var knownPackages = state.Installations.Concat(state.History)
            .Where(record => !string.IsNullOrWhiteSpace(record.StorePath))
            .Select(record => Path.GetFullPath(record.StorePath))
            .ToHashSet(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        if (Directory.Exists(context.PackagesRoot))
        {
            foreach (var digestDirectory in Directory.EnumerateDirectories(context.PackagesRoot, "*", SearchOption.AllDirectories)
                         .Where(path => Path.GetFileName(path).Length == 64))
            {
                if (!knownPackages.Contains(Path.GetFullPath(digestDirectory)))
                    findings.Add(Finding("warning", "ORPHAN_PACKAGE", "", digestDirectory));
            }
        }

        return findings;
    }

    private static SkillDoctorFinding Finding(string severity, string code, string skill, string message) => new()
    {
        Severity = severity,
        Code = code,
        Skill = skill,
        Message = message
    };

    private static void DiagnoseBridges(List<SkillDoctorFinding> findings, SkillInstallRecord record)
    {
        foreach (var bridge in record.Bridges)
        {
            var target = DirectoryLinkManager.ResolveTarget(bridge.Path);
            if (!DirectoryLinkManager.PathsEqual(target, bridge.TargetPath))
                findings.Add(Finding("error", "BRIDGE_INVALID", record.Name, bridge.Path));
        }
    }

    private static string NormalizeDigest(string value) =>
        value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            ? value.ToLowerInvariant()
            : $"sha256:{value.ToLowerInvariant()}";

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static bool ProjectRootsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right);
        return DirectoryLinkManager.PathsEqual(left, right);
    }
}
