// -----------------------------------------------------------------------
// <copyright file="MigrateCommand.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text.Json;
using AgentSkillStore.Client;
using AgentSkillStore.Cli.Json;
using AgentSkillStore.Cli.Output;

namespace AgentSkillStore.Cli.Commands;

internal static class MigrateCommand
{
    public static Task<int> ExecuteAsync(ParsedArgs args, AgentSkillStoreClient client, string server)
    {
        _ = client;
        _ = server;
        try
        {
            if (args.DryRun && args.Apply)
                throw new ArgumentException("Choose either --dry-run or --apply, not both.");

            var registry = new InstallationRegistry();
            var legacyRecords = registry.Load().Installations
                .Where(record => !string.Equals(record.Layout, "shared", StringComparison.Ordinal))
                .ToList();
            var duplicateNames = legacyRecords.GroupBy(record => record.Name, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var items = legacyRecords.Select(record => Classify(record, duplicateNames.Contains(record.Name))).ToList();

            if (args.OutputFormat == "json")
            {
                Console.WriteLine(JsonSerializer.Serialize(
                    (IReadOnlyList<SkillMigrationItem>)items,
                    CliJsonContext.Default.IReadOnlyListSkillMigrationItem));
            }
            else if (items.Count == 0)
            {
                ConsoleOutput.WriteInfo("No legacy skillstore installations found.");
            }
            else
            {
                ConsoleOutput.WriteTable(
                    ["SKILL", "CLASSIFICATION", "PATH", "DETAIL"],
                    items.Select(item => new[] { item.Name, item.Classification, item.SourcePath, item.Message }).ToList());
            }

            if (!args.Apply)
            {
                if (args.OutputFormat != "json")
                    ConsoleOutput.WriteInfo("Dry run only. Re-run with --apply to migrate eligible records.");
                return Task.FromResult(0);
            }

            foreach (var item in items.Where(item => item.Classification == "migratable"))
            {
                var record = legacyRecords.Single(candidate =>
                    string.Equals(candidate.Name, item.Name, StringComparison.OrdinalIgnoreCase));
                ApplyMigration(registry, record);
            }

            if (items.Any(item => item.Classification is "conflict" or "modified"))
                return Task.FromResult(1);
            if (args.OutputFormat != "json")
                ConsoleOutput.WriteSuccess("Legacy migration completed.");
            return Task.FromResult(0);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or InvalidDataException or ArgumentException)
        {
            ConsoleOutput.WriteError($"Error: {exception.Message}");
            return Task.FromResult(1);
        }
    }

    internal static SkillMigrationItem Classify(SkillInstallRecord record, bool duplicate)
    {
        if (duplicate)
            return Item(record, "conflict", "Multiple legacy targets contain the same Skill; migrate them manually.");
        if (!Directory.Exists(record.InstallPath))
            return Item(record, "conflict", "Managed directory is missing.");
        if ((File.GetAttributes(record.InstallPath) & FileAttributes.ReparsePoint) != 0)
            return Item(record, "conflict", "Legacy installation is already a link with unknown ownership.");
        var normalizedDigest = NormalizeDigest(record.ArchiveHash);
        if (normalizedDigest.Length != 71 || normalizedDigest[7..].Any(character => !Uri.IsHexDigit(character)))
            return Item(record, "conflict", "Legacy record has no valid archive digest.");

        IReadOnlyList<string> actualFiles;
        try
        {
            actualFiles = DirectoryLinkManager.EnumerateRegularFiles(record.InstallPath);
        }
        catch (IOException exception)
        {
            return Item(record, "conflict", exception.Message);
        }

        var expectedFiles = record.Files
            .Select(file => file.Replace('\\', '/'))
            .ToHashSet(StringComparer.Ordinal);
        var untrackedFile = actualFiles
            .Select(path => Path.GetRelativePath(record.InstallPath, path).Replace('\\', '/'))
            .FirstOrDefault(file => !expectedFiles.Contains(file));
        if (untrackedFile is not null)
            return Item(record, "modified", $"Managed directory contains an untracked file: {untrackedFile}");

        foreach (var file in record.Files)
        {
            var path = SkillStoreContext.EnsureChildPath(record.InstallPath, file);
            if (!File.Exists(path) || !record.FileHashes.TryGetValue(file, out var expected))
                return Item(record, "modified", $"Managed file is missing or untracked: {file}");
            var actual = $"sha256:{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant()}";
            if (!string.Equals(actual, NormalizeDigest(expected), StringComparison.OrdinalIgnoreCase))
                return Item(record, "modified", $"Managed file was modified: {file}");
        }
        return Item(record, "migratable", "CLI-managed files are unchanged.");
    }

    private static void ApplyMigration(InstallationRegistry registry, SkillInstallRecord record)
    {
        var context = SkillStoreContext.Resolve("user");
        using var operationLock = SkillStoreOperationLock.Acquire(context.OperationLockPath);
        var packagePath = context.GetPackagePath(record.PackageName, record.Version, record.ArchiveHash);
        var activePath = context.GetActivePath(record.PackageName);
        if (Directory.Exists(packagePath) || DirectoryLinkManager.Exists(activePath))
            throw new IOException($"Shared destination already exists for '{record.Name}'.");

        var moved = false;
        var activeCreated = false;
        SkillBridgeRecord? bridge = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
            Directory.Move(record.InstallPath, packagePath);
            moved = true;
            DirectoryLinkManager.Create(activePath, packagePath);
            activeCreated = true;
            if (string.Equals(record.Target, "claude", StringComparison.OrdinalIgnoreCase))
                bridge = SkillBridgeManager.CreateClaudeBridge(context, record.PackageName, activePath);

            var migrated = record with
            {
                Target = "all",
                InstallPath = activePath,
                Layout = "shared",
                Scope = "user",
                StorePath = packagePath,
                ActivePath = activePath,
                Bridges = bridge is null ? [] : [bridge],
                ReusesUserInstall = false
            };
            registry.Replace(record, migrated);
        }
        catch
        {
            if (bridge is not null)
                SkillBridgeManager.RemoveBridge(bridge);
            if (activeCreated)
                DirectoryLinkManager.Remove(activePath, packagePath);
            if (moved && Directory.Exists(packagePath) && !Directory.Exists(record.InstallPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(record.InstallPath)!);
                Directory.Move(packagePath, record.InstallPath);
            }
            throw;
        }
    }

    private static SkillMigrationItem Item(SkillInstallRecord record, string classification, string message) => new()
    {
        Name = record.Name,
        SourcePath = record.InstallPath,
        Classification = classification,
        Message = message
    };

    private static string NormalizeDigest(string value) =>
        value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            ? value.ToLowerInvariant()
            : $"sha256:{value.ToLowerInvariant()}";
}
