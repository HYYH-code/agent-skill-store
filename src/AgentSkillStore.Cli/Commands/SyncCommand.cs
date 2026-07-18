// -----------------------------------------------------------------------
// <copyright file="SyncCommand.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using AgentSkillStore.Client;
using AgentSkillStore.Cli.Output;

namespace AgentSkillStore.Cli.Commands;

internal static class SyncCommand
{
    public static async Task<int> ExecuteAsync(ParsedArgs args, AgentSkillStoreClient client, string server)
    {
        try
        {
            var scope = InstallCommand.ResolveScope(args);
            var context = SkillStoreContext.Resolve(scope, args.InstallRoot);
            var registry = new InstallationRegistry();
            var installer = new SkillInstaller(client, registry, server);
            if (scope == "project")
            {
                if (context.LockFilePath is null || !File.Exists(context.LockFilePath))
                    throw new InvalidOperationException("No skillstore.lock.json found at the project root.");
                var lockFile = SkillStoreLockFileManager.Load(context.LockFilePath);
                if (lockFile.SchemaVersion != 1)
                    throw new InvalidDataException($"Unsupported lockfile schema: {lockFile.SchemaVersion}");
                if (!ServersEqual(lockFile.Server, server))
                    throw new InvalidOperationException(
                        $"Lockfile server '{lockFile.Server}' does not match configured server '{server}'.");

                foreach (var entry in lockFile.Skills)
                {
                    await installer.InstallAsync(
                        entry.Name,
                        entry.Version,
                        "all",
                        args.InstallRoot,
                        args.Force,
                        allowNonStable: true,
                        acceptPermissionExpansion: args.Yes,
                        scope: "project",
                        expectedDigest: entry.Digest,
                        expectedPermissions: entry.GrantedPermissions,
                        updateLockFile: false);
                }

                var lockedNames = lockFile.Skills.Select(entry => entry.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var stale = registry.Load().Installations.Where(record =>
                    string.Equals(record.Layout, "shared", StringComparison.Ordinal) &&
                    string.Equals(record.Scope, "project", StringComparison.Ordinal) &&
                    DirectoryLinkManager.PathsEqual(record.ProjectRoot, context.ProjectRoot) &&
                    !lockedNames.Contains(record.Name)).ToList();
                foreach (var record in stale)
                {
                    await installer.UninstallAsync(
                        record.Name,
                        "all",
                        args.Force,
                        "project",
                        args.InstallRoot,
                        updateLockFile: false);
                }

                ConsoleOutput.WriteSuccess($"Synchronized {lockFile.Skills.Count} project Skill(s).");
                return 0;
            }

            var userRecords = registry.Load().Installations.Where(record =>
                string.Equals(record.Layout, "shared", StringComparison.Ordinal) &&
                string.Equals(record.Scope, "user", StringComparison.Ordinal)).ToList();
            foreach (var record in userRecords)
            {
                await installer.InstallAsync(
                    record.Name,
                    record.Version,
                    "all",
                    args.InstallRoot,
                    args.Force,
                    allowNonStable: true,
                    acceptPermissionExpansion: args.Yes,
                    scope: "user",
                    expectedDigest: record.ArchiveHash,
                    expectedPermissions: record.GrantedPermissions,
                    updateLockFile: false);
            }

            ConsoleOutput.WriteSuccess($"Synchronized {userRecords.Count} user Skill(s).");
            return 0;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or InvalidDataException or ArgumentException or HttpRequestException)
        {
            ConsoleOutput.WriteError($"Error: {exception.Message}");
            return 1;
        }
    }

    private static bool ServersEqual(string left, string right)
    {
        if (!Uri.TryCreate(left, UriKind.Absolute, out var leftUri) ||
            !Uri.TryCreate(right, UriKind.Absolute, out var rightUri))
        {
            return string.Equals(left.TrimEnd('/'), right.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
        }
        return string.Equals(
            leftUri.GetLeftPart(UriPartial.Path).TrimEnd('/'),
            rightUri.GetLeftPart(UriPartial.Path).TrimEnd('/'),
            StringComparison.OrdinalIgnoreCase);
    }
}
