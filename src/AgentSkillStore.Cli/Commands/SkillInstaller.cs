// -----------------------------------------------------------------------
// <copyright file="SkillInstaller.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.IO.Compression;
using System.Security.Cryptography;
using AgentSkillStore.Client;

namespace AgentSkillStore.Cli.Commands;

internal sealed class SkillInstaller
{
    private const int MaxFiles = 2_000;
    private const long MaxArchiveBytes = 100L * 1024 * 1024;
    private const long MaxFileBytes = 50L * 1024 * 1024;
    private const long MaxExpandedBytes = 100L * 1024 * 1024;
    private const double MaxCompressionRatio = 100d;

    private readonly AgentSkillStoreClient _client;
    private readonly InstallationRegistry _registry;
    private readonly string _server;

    public SkillInstaller(AgentSkillStoreClient client, InstallationRegistry registry, string server)
    {
        _client = client;
        _registry = registry;
        _server = server;
    }

    public async Task<InstallResult> InstallAsync(
        string name,
        string? version,
        string target,
        string? installRoot,
        bool force,
        bool allowNonStable = false,
        bool acceptPermissionExpansion = false,
        string scope = "user",
        string? workingDirectory = null,
        string? expectedDigest = null,
        IReadOnlyList<string>? expectedPermissions = null,
        bool updateLockFile = true,
        CancellationToken ct = default)
    {
        var reference = SkillReference.Parse(name, version);
        target = string.IsNullOrWhiteSpace(target) ? "all" : target.ToLowerInvariant();
        SkillBridgeManager.ValidateTarget(target);
        var legacy = target == "all"
            ? _registry.FindAnyLegacy(reference.Name)
            : _registry.FindLegacy(reference.Name, target);
        if (legacy is not null)
        {
            return await InstallLegacyAsync(
                reference,
                version,
                legacy.Target,
                installRoot,
                force,
                allowNonStable,
                acceptPermissionExpansion,
                ct);
        }

        var context = SkillStoreContext.Resolve(scope, installRoot, workingDirectory);
        var selected = await ResolveVersionAsync(reference.PackageName, reference.Version, allowNonStable, ct);
        var installReference = await _client.GetInstallReferenceAsync(reference.PackageName, selected.Version, ct)
                               ?? throw new InvalidOperationException(
                                   $"No governed install reference found for '{reference.PackageName}@{selected.Version}'.");
        ValidateInstallReference(reference.PackageName, selected, installReference);
        var archiveDigest = NormalizeDigest(installReference.Digest);
        if (!string.IsNullOrWhiteSpace(expectedDigest) &&
            !string.Equals(archiveDigest, NormalizeDigest(expectedDigest), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"LOCK_DIGEST_MISMATCH: lockfile expects {NormalizeDigest(expectedDigest)}, server advertises {archiveDigest}.");
        }

        var grantedPermissions = FlattenPermissions(installReference.RequestedPermissions);
        if (expectedPermissions is not null &&
            !grantedPermissions.SequenceEqual(expectedPermissions.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            throw new InvalidDataException("LOCK_PERMISSION_MISMATCH: server permissions differ from skillstore.lock.json.");
        }

        using var operationLock = SkillStoreOperationLock.Acquire(context.OperationLockPath);
        var previous = _registry.Find(reference.Name, context);
        if (context.Scope == "user")
        {
            var conflictingProject = _registry.Load().Installations.FirstOrDefault(record =>
                string.Equals(record.Layout, "shared", StringComparison.Ordinal) &&
                string.Equals(record.Scope, "project", StringComparison.Ordinal) &&
                string.Equals(record.Name, reference.Name, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(NormalizeDigest(record.ArchiveHash), archiveDigest, StringComparison.OrdinalIgnoreCase));
            if (conflictingProject is not null)
            {
                throw new InvalidOperationException(
                    $"SCOPE_VERSION_CONFLICT: project installation {conflictingProject.Version} at '{conflictingProject.ProjectRoot}' conflicts with user version {selected.Version} for '{reference.Name}'.");
            }
        }

        var expandedPermissions = previous is null
            ? []
            : grantedPermissions.Except(previous.GrantedPermissions, StringComparer.Ordinal).ToArray();
        if (expandedPermissions.Length > 0 && !acceptPermissionExpansion)
        {
            throw new InvalidOperationException(
                $"PERMISSION_CONFIRMATION_REQUIRED: {string.Join(", ", expandedPermissions)}. Re-run with --yes after reviewing the change.");
        }

        if (context.Scope == "project")
        {
            var userContext = SkillStoreContext.Resolve("user");
            var userRecord = _registry.Find(reference.Name, userContext);
            if (userRecord is not null)
            {
                if (!string.Equals(NormalizeDigest(userRecord.ArchiveHash), archiveDigest, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"SCOPE_VERSION_CONFLICT: user installation {userRecord.Version} conflicts with project version {selected.Version} for '{reference.Name}'.");
                }

                if (previous is null || previous.ReusesUserInstall)
                {
                    var reusedBridges = previous?.Bridges.ToList() ?? [];
                    var createReusedClaudeBridge = SkillBridgeManager.ShouldCreateClaudeBridge(target);
                    var hasManagedReusedClaudeBridge = reusedBridges.Any(bridge =>
                        string.Equals(bridge.Target, "claude", StringComparison.OrdinalIgnoreCase));
                    var reusedClaudeBridgePath = context.GetClaudeBridgePath(reference.PackageName);
                    if (createReusedClaudeBridge || hasManagedReusedClaudeBridge)
                        PreflightBridge(reusedClaudeBridgePath, userRecord.ActivePath);

                    var reusedRecord = CreateSharedRecord(
                        reference,
                        selected.Version,
                        target,
                        context,
                        userRecord.StorePath,
                        userRecord.ActivePath,
                        userRecord.Files,
                        userRecord.FileHashes,
                        grantedPermissions,
                        archiveDigest,
                        previous,
                        reusedBridges,
                        reusesUserInstall: true);
                    var stateBeforeReuse = _registry.Load();
                    var lockFileBeforeReuse = context.LockFilePath is not null && File.Exists(context.LockFilePath)
                        ? File.ReadAllText(context.LockFilePath)
                        : null;
                    var reusedBridgeCreated = false;
                    try
                    {
                        if (createReusedClaudeBridge || hasManagedReusedClaudeBridge)
                        {
                            var claudeBridgeIndex = reusedBridges.FindIndex(bridge =>
                                string.Equals(bridge.Target, "claude", StringComparison.OrdinalIgnoreCase));
                            if (claudeBridgeIndex < 0)
                            {
                                reusedBridges.Add(SkillBridgeManager.CreateClaudeBridge(
                                    context,
                                    reference.PackageName,
                                    userRecord.ActivePath));
                                reusedBridgeCreated = true;
                            }
                            else if (!DirectoryLinkManager.Exists(reusedBridges[claudeBridgeIndex].Path))
                            {
                                reusedBridges[claudeBridgeIndex] = SkillBridgeManager.CreateClaudeBridge(
                                    context,
                                    reference.PackageName,
                                    userRecord.ActivePath);
                                reusedBridgeCreated = true;
                            }

                            reusedRecord = reusedRecord with { Bridges = reusedBridges };
                        }

                        _registry.Upsert(reusedRecord, previous);
                        if (updateLockFile && context.LockFilePath is not null)
                            SkillStoreLockFileManager.Upsert(context.LockFilePath, _server, reusedRecord);
                        GitExcludeManager.AddManagedPaths(context, reference.PackageName, reusedBridges.Count > 0);
                        return new InstallResult(reusedRecord, []);
                    }
                    catch
                    {
                        if (reusedBridgeCreated)
                            DirectoryLinkManager.Remove(reusedClaudeBridgePath, userRecord.ActivePath);
                        _registry.Restore(stateBeforeReuse);
                        RestoreLockFile(context.LockFilePath, lockFileBeforeReuse);
                        throw;
                    }
                }
            }
        }

        var archiveBytes = await DownloadArchiveAsync(installReference, selected, ct);
        var archiveHash = ComputeSha256Digest(archiveBytes);
        if (!string.Equals(archiveHash, archiveDigest, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"DIGEST_MISMATCH: expected {archiveDigest}, downloaded {archiveHash}.");
        }

        var entries = ReadArchiveEntries(archiveBytes);
        if (previous is not null)
            EnsureManagedFilesUnmodified(previous, force);

        var packagePath = context.GetPackagePath(reference.PackageName, selected.Version, archiveHash);
        var activePath = context.GetActivePath(reference.PackageName);
        PreflightManagedActivePath(previous, activePath);

        var bridges = previous?.Bridges.ToList() ?? [];
        var createClaudeBridge = SkillBridgeManager.ShouldCreateClaudeBridge(target);
        var hasManagedClaudeBridge = bridges.Any(bridge =>
            string.Equals(bridge.Target, "claude", StringComparison.OrdinalIgnoreCase));
        var claudeBridgePath = context.GetClaudeBridgePath(reference.PackageName);
        if (createClaudeBridge || hasManagedClaudeBridge)
            PreflightBridge(claudeBridgePath, activePath);

        var stateBefore = _registry.Load();
        var lockFileBefore = context.LockFilePath is not null && File.Exists(context.LockFilePath)
            ? File.ReadAllText(context.LockFilePath)
            : null;
        var operationId = Guid.NewGuid().ToString("N");
        var stagingRoot = SkillStoreContext.EnsureChildPath(context.StateRoot, Path.Combine("staging", operationId));
        var stagedPackagePath = SkillStoreContext.EnsureChildPath(stagingRoot, reference.PackageName);
        var packageCreated = false;
        var activeSwitched = false;
        var bridgeCreated = false;
        string? oldActiveTarget = null;

        try
        {
            if (Directory.Exists(packagePath))
            {
                EnsurePackageMatches(packagePath, entries);
            }
            else
            {
                Directory.CreateDirectory(stagedPackagePath);
                await WriteEntriesAsync(stagedPackagePath, entries, ct);
                Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
                Directory.Move(stagedPackagePath, packagePath);
                packageCreated = true;
            }

            oldActiveTarget = DirectoryLinkManager.Replace(
                activePath,
                packagePath,
                previous is { ReusesUserInstall: false } ? previous.StorePath : null);
            activeSwitched = true;

            if (createClaudeBridge || hasManagedClaudeBridge)
            {
                var claudeBridgeIndex = bridges.FindIndex(bridge =>
                    string.Equals(bridge.Target, "claude", StringComparison.OrdinalIgnoreCase));
                if (claudeBridgeIndex < 0)
                {
                    bridges.Add(SkillBridgeManager.CreateClaudeBridge(context, reference.PackageName, activePath));
                    bridgeCreated = true;
                }
                else if (!DirectoryLinkManager.Exists(bridges[claudeBridgeIndex].Path))
                {
                    bridges[claudeBridgeIndex] = SkillBridgeManager.CreateClaudeBridge(
                        context,
                        reference.PackageName,
                        activePath);
                    bridgeCreated = true;
                }
            }

            var files = entries.Select(entry => entry.RelativePath).ToArray();
            var fileHashes = entries.ToDictionary(
                entry => entry.RelativePath,
                entry => ComputeSha256Digest(entry.Content),
                StringComparer.Ordinal);
            var record = CreateSharedRecord(
                reference,
                selected.Version,
                target,
                context,
                packagePath,
                activePath,
                files,
                fileHashes,
                grantedPermissions,
                archiveHash,
                previous,
                bridges,
                reusesUserInstall: false);

            _registry.Upsert(record, previous);
            if (updateLockFile && context.LockFilePath is not null)
                SkillStoreLockFileManager.Upsert(context.LockFilePath, _server, record);
            GitExcludeManager.AddManagedPaths(context, reference.PackageName, bridges.Count > 0);
            var removed = previous?.Files.Except(files, StringComparer.Ordinal).ToArray() ?? [];
            return new InstallResult(record, removed);
        }
        catch
        {
            if (bridgeCreated)
                DirectoryLinkManager.Remove(claudeBridgePath, activePath);
            if (activeSwitched)
            {
                if (oldActiveTarget is null)
                    DirectoryLinkManager.Remove(activePath, packagePath);
                else
                    DirectoryLinkManager.Replace(activePath, oldActiveTarget, packagePath);
            }
            _registry.Restore(stateBefore);
            RestoreLockFile(context.LockFilePath, lockFileBefore);
            if (packageCreated)
                TryDeleteDirectory(packagePath);
            throw;
        }
        finally
        {
            TryDeleteDirectory(stagingRoot);
        }
    }

    public Task<IReadOnlyList<string>> UninstallAsync(
        string name,
        string target,
        bool force,
        CancellationToken ct) =>
        UninstallAsync(
            name,
            target,
            force,
            scope: "user",
            installRoot: null,
            workingDirectory: null,
            updateLockFile: true,
            ct: ct);

    public Task<IReadOnlyList<string>> UninstallAsync(
        string name,
        string target,
        bool force,
        string scope = "user",
        string? installRoot = null,
        string? workingDirectory = null,
        bool updateLockFile = true,
        CancellationToken ct = default)
    {
        _ = ct;
        var reference = SkillReference.Parse(name);
        target = string.IsNullOrWhiteSpace(target) ? "all" : target.ToLowerInvariant();
        var context = SkillStoreContext.Resolve(scope, installRoot, workingDirectory);
        var record = _registry.Find(reference.Name, context);
        if (record is null)
        {
            var legacy = target == "all"
                ? _registry.FindAnyLegacy(reference.Name)
                : _registry.FindLegacy(reference.Name, target);
            if (legacy is not null)
                return UninstallLegacy(legacy, force);
            throw new InvalidOperationException($"'{reference.Name}' is not installed for scope '{context.Scope}'.");
        }
        if (installRoot is null && !record.ReusesUserInstall)
            context = SkillStoreContext.FromRecord(record);

        using var operationLock = SkillStoreOperationLock.Acquire(context.OperationLockPath);
        var stateBefore = _registry.Load();
        var lockFileBefore = context.LockFilePath is not null && File.Exists(context.LockFilePath)
            ? File.ReadAllText(context.LockFilePath)
            : null;
        if (context.Scope == "user")
        {
            var dependentProjects = stateBefore.Installations.Where(candidate =>
                string.Equals(candidate.Layout, "shared", StringComparison.Ordinal) &&
                string.Equals(candidate.Scope, "project", StringComparison.Ordinal) &&
                candidate.ReusesUserInstall &&
                string.Equals(candidate.Name, record.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (dependentProjects.Count > 0)
            {
                throw new InvalidOperationException(
                    $"SCOPE_DEPENDENCY_CONFLICT: {dependentProjects.Count} project installation(s) reuse '{record.Name}'. Remove or sync those project lockfiles before uninstalling the user installation.");
            }
        }

        if (record.ReusesUserInstall)
        {
            var removedReusedBridges = new List<SkillBridgeRecord>();
            try
            {
                foreach (var bridge in record.Bridges)
                {
                    DirectoryLinkManager.Remove(bridge.Path, bridge.TargetPath);
                    removedReusedBridges.Add(bridge);
                }

                _registry.Remove(record);
                if (updateLockFile && context.LockFilePath is not null)
                    SkillStoreLockFileManager.Remove(context.LockFilePath, reference.Name);
                return Task.FromResult<IReadOnlyList<string>>([]);
            }
            catch
            {
                foreach (var bridge in removedReusedBridges)
                {
                    if (!DirectoryLinkManager.Exists(bridge.Path))
                        DirectoryLinkManager.Create(bridge.Path, bridge.TargetPath);
                }
                _registry.Restore(stateBefore);
                RestoreLockFile(context.LockFilePath, lockFileBefore);
                throw;
            }
        }

        EnsureManagedFilesUnmodified(record, force);
        var removedBridges = new List<SkillBridgeRecord>();
        var activeRemoved = false;

        try
        {
            foreach (var bridge in record.Bridges)
            {
                DirectoryLinkManager.Remove(bridge.Path, bridge.TargetPath);
                removedBridges.Add(bridge);
            }

            DirectoryLinkManager.Remove(record.ActivePath, record.StorePath);
            activeRemoved = true;
            _registry.Remove(record);
            if (updateLockFile && context.LockFilePath is not null)
                SkillStoreLockFileManager.Remove(context.LockFilePath, reference.Name);

            var packageFamily = Path.Combine(context.PackagesRoot, record.PackageName);
            if (!_registry.Load().Installations.Any(candidate =>
                    !candidate.ReusesUserInstall &&
                    IsPathWithin(candidate.StorePath, packageFamily)))
            {
                TryDeleteDirectory(packageFamily);
            }

            return Task.FromResult<IReadOnlyList<string>>(record.Files.ToArray());
        }
        catch
        {
            if (activeRemoved && Directory.Exists(record.StorePath))
                DirectoryLinkManager.Create(record.ActivePath, record.StorePath);
            foreach (var bridge in removedBridges)
            {
                if (!DirectoryLinkManager.Exists(bridge.Path))
                    DirectoryLinkManager.Create(bridge.Path, bridge.TargetPath);
            }
            _registry.Restore(stateBefore);
            RestoreLockFile(context.LockFilePath, lockFileBefore);
            throw;
        }
    }

    public async Task<InstallResult> UpdateAsync(
        string name,
        string target,
        string? installRoot,
        bool force,
        bool allowNonStable = false,
        bool acceptPermissionExpansion = false,
        string scope = "user",
        string? workingDirectory = null,
        CancellationToken ct = default)
    {
        var reference = SkillReference.Parse(name);
        var context = SkillStoreContext.Resolve(scope, installRoot, workingDirectory);
        var current = _registry.Find(reference.Name, context);
        if (current is null)
        {
            var legacy = target == "all"
                ? _registry.FindAnyLegacy(reference.Name)
                : _registry.FindLegacy(reference.Name, target);
            if (legacy is null)
                throw new InvalidOperationException($"'{reference.Name}' is not installed for scope '{context.Scope}'.");
            return await UpdateLegacyAsync(legacy, force, allowNonStable, acceptPermissionExpansion, ct);
        }
        if (installRoot is null && !current.ReusesUserInstall)
            context = SkillStoreContext.FromRecord(current);
        var latest = await ResolveVersionAsync(
            string.IsNullOrWhiteSpace(current.PackageName) ? reference.PackageName : current.PackageName,
            null,
            allowNonStable,
            ct);

        if (string.Equals(current.Version, latest.Version, StringComparison.OrdinalIgnoreCase))
            return new InstallResult(current, []);

        return await InstallAsync(
            current.Name,
            latest.Version,
            target,
            installRoot ?? context.AgentsRoot,
            force,
            allowNonStable,
            acceptPermissionExpansion,
            scope,
            workingDirectory,
            ct: ct);
    }

    public async Task<InstallResult> RollbackAsync(
        string name,
        string target,
        string? version,
        bool force,
        bool allowNonStable = false,
        bool acceptPermissionExpansion = false,
        string scope = "user",
        string? installRoot = null,
        string? workingDirectory = null,
        CancellationToken ct = default)
    {
        var reference = SkillReference.Parse(name, version);
        var context = SkillStoreContext.Resolve(scope, installRoot, workingDirectory);
        var current = _registry.Find(reference.Name, context);
        var previous = _registry.GetRollback(reference.Name, context, reference.Version);
        if (current is null && previous is null)
        {
            var legacy = target == "all"
                ? _registry.FindAnyLegacy(reference.Name)
                : _registry.FindLegacy(reference.Name, target);
            if (legacy is not null)
                return await RollbackLegacyAsync(legacy, reference.Version, force, allowNonStable, acceptPermissionExpansion, ct);
        }
        var contextRecord = current ?? previous;
        if (installRoot is null && contextRecord is { ReusesUserInstall: false })
            context = SkillStoreContext.FromRecord(contextRecord);
        var rollbackVersion = reference.Version ?? previous?.Version
                              ?? throw new InvalidOperationException(
                                  $"No rollback record found for '{reference.Name}' on target '{target}'.");

        var result = await InstallAsync(
            reference.Name,
            rollbackVersion,
            target,
            installRoot ?? context.AgentsRoot,
            force,
            allowNonStable || previous is not null,
            acceptPermissionExpansion,
            scope,
            workingDirectory,
            ct: ct);
        if (previous is not null)
            _registry.RemoveHistory(previous);
        return result;
    }

    private async Task<InstallResult> InstallLegacyAsync(
        SkillReference reference,
        string? version,
        string target,
        string? installRoot,
        bool force,
        bool allowNonStable,
        bool acceptPermissionExpansion,
        CancellationToken ct)
    {
        var selected = await ResolveVersionAsync(reference.PackageName, version, allowNonStable, ct);
        var root = ResolveLegacyInstallRoot(target, installRoot);
        var installPath = EnsureChildPath(root, reference.PackageName);
        var previous = _registry.FindLegacy(reference.Name, target);
        var installReference = await _client.GetInstallReferenceAsync(reference.PackageName, selected.Version, ct)
                               ?? throw new InvalidOperationException(
                                   $"No governed install reference found for '{reference.PackageName}@{selected.Version}'.");
        ValidateInstallReference(reference.PackageName, selected, installReference);
        var grantedPermissions = FlattenPermissions(installReference.RequestedPermissions);
        var expandedPermissions = previous is null
            ? []
            : grantedPermissions.Except(previous.GrantedPermissions, StringComparer.Ordinal).ToArray();
        if (expandedPermissions.Length > 0 && !acceptPermissionExpansion)
        {
            throw new InvalidOperationException(
                $"PERMISSION_CONFIRMATION_REQUIRED: {string.Join(", ", expandedPermissions)}. Re-run with --yes after reviewing the change.");
        }

        var archiveBytes = await DownloadArchiveAsync(installReference, selected, ct);
        var archiveHash = ComputeSha256Digest(archiveBytes);
        var expectedHash = NormalizeDigest(installReference.Digest);
        if (!string.Equals(archiveHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"DIGEST_MISMATCH: expected {expectedHash}, downloaded {archiveHash}.");
        var entries = ReadArchiveEntries(archiveBytes);
        if (previous is not null)
            EnsureManagedFilesUnmodified(previous, force);

        Directory.CreateDirectory(root);
        var operationId = Guid.NewGuid().ToString("N");
        var stagingRoot = EnsureChildPath(root, Path.Combine(".agent-skill-store-staging", operationId));
        var stagedInstallPath = EnsureChildPath(stagingRoot, reference.PackageName);
        var backupRoot = EnsureChildPath(root, Path.Combine(".agent-skill-store-backup", operationId));
        var backupPath = EnsureChildPath(backupRoot, reference.PackageName);
        var existingMoved = false;
        var installedMoved = false;
        try
        {
            Directory.CreateDirectory(stagedInstallPath);
            CopyUnmanagedFiles(installPath, stagedInstallPath, previous, entries, force);
            await WriteEntriesAsync(stagedInstallPath, entries, ct);
            if (Directory.Exists(installPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                Directory.Move(installPath, backupPath);
                existingMoved = true;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(installPath)!);
            Directory.Move(stagedInstallPath, installPath);
            installedMoved = true;
            var files = entries.Select(entry => entry.RelativePath).ToArray();
            var fileHashes = entries.ToDictionary(
                entry => entry.RelativePath,
                entry => ComputeSha256Digest(entry.Content),
                StringComparer.Ordinal);
            var record = new SkillInstallRecord
            {
                Name = reference.Name,
                PackageName = reference.PackageName,
                Version = selected.Version,
                Target = target,
                InstallPath = installPath,
                ActivePath = installPath,
                Files = files,
                FileHashes = fileHashes,
                GrantedPermissions = grantedPermissions,
                ArchiveHash = archiveHash,
                Server = _server,
                PreviousVersion = previous?.Version,
                InstalledAt = DateTimeOffset.UtcNow,
                Layout = "legacy-direct"
            };
            _registry.Upsert(record, previous);
            TryDeleteDirectory(backupRoot);
            return new InstallResult(record, previous?.Files.Except(files, StringComparer.Ordinal).ToArray() ?? []);
        }
        catch
        {
            if (installedMoved && Directory.Exists(installPath))
                Directory.Delete(installPath, true);
            if (existingMoved && Directory.Exists(backupPath))
                Directory.Move(backupPath, installPath);
            throw;
        }
        finally
        {
            TryDeleteDirectory(stagingRoot);
            TryDeleteDirectory(backupRoot);
        }
    }

    private Task<IReadOnlyList<string>> UninstallLegacy(SkillInstallRecord record, bool force)
    {
        EnsureManagedFilesUnmodified(record, force);
        var installPath = Path.GetFullPath(record.InstallPath);
        if (Directory.Exists(installPath))
            Directory.Delete(installPath, true);
        _registry.Remove(record);
        return Task.FromResult<IReadOnlyList<string>>(record.Files.ToArray());
    }

    private async Task<InstallResult> UpdateLegacyAsync(
        SkillInstallRecord current,
        bool force,
        bool allowNonStable,
        bool acceptPermissionExpansion,
        CancellationToken ct)
    {
        var latest = await ResolveVersionAsync(current.PackageName, null, allowNonStable, ct);
        if (string.Equals(current.Version, latest.Version, StringComparison.OrdinalIgnoreCase))
            return new InstallResult(current, []);
        return await InstallLegacyAsync(
            SkillReference.Parse(current.Name),
            latest.Version,
            current.Target,
            Path.GetDirectoryName(current.InstallPath),
            force,
            allowNonStable,
            acceptPermissionExpansion,
            ct);
    }

    private async Task<InstallResult> RollbackLegacyAsync(
        SkillInstallRecord current,
        string? version,
        bool force,
        bool allowNonStable,
        bool acceptPermissionExpansion,
        CancellationToken ct)
    {
        var previous = _registry.GetRollback(current.Name, current.Target, version);
        var rollbackVersion = version ?? previous?.Version
                              ?? throw new InvalidOperationException(
                                  $"No rollback record found for '{current.Name}' on target '{current.Target}'.");
        var result = await InstallLegacyAsync(
            SkillReference.Parse(current.Name),
            rollbackVersion,
            current.Target,
            Path.GetDirectoryName(current.InstallPath),
            force,
            allowNonStable || previous is not null,
            acceptPermissionExpansion,
            ct);
        if (previous is not null)
            _registry.RemoveHistory(previous);
        return result;
    }

    private static string ResolveLegacyInstallRoot(string target, string? explicitRoot)
    {
        if (!string.IsNullOrWhiteSpace(explicitRoot))
            return Path.GetFullPath(explicitRoot);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return target.ToLowerInvariant() switch
        {
            "codex" => Path.Combine(profile, ".codex", "skills"),
            "claude" => Path.Combine(profile, ".claude", "skills"),
            "pi" => Path.Combine(profile, ".pi", "agent", "skills"),
            _ => throw new ArgumentException("Legacy target must be one of: codex, claude, pi.", nameof(target))
        };
    }

    private SkillInstallRecord CreateSharedRecord(
        SkillReference reference,
        string version,
        string target,
        SkillStoreContext context,
        string storePath,
        string activePath,
        IReadOnlyList<string> files,
        Dictionary<string, string> fileHashes,
        IReadOnlyList<string> grantedPermissions,
        string archiveHash,
        SkillInstallRecord? previous,
        IReadOnlyList<SkillBridgeRecord> bridges,
        bool reusesUserInstall) => new()
        {
            Name = reference.Name,
            PackageName = reference.PackageName,
            Version = version,
            Target = target,
            InstallPath = activePath,
            Files = files,
            FileHashes = fileHashes,
            GrantedPermissions = grantedPermissions,
            ArchiveHash = archiveHash,
            Server = _server,
            PreviousVersion = previous?.Version,
            InstalledAt = DateTimeOffset.UtcNow,
            Layout = "shared",
            Scope = context.Scope,
            ProjectRoot = context.ProjectRoot,
            StorePath = storePath,
            ActivePath = activePath,
            Bridges = bridges,
            ReusesUserInstall = reusesUserInstall
        };

    private static void PreflightManagedActivePath(SkillInstallRecord? previous, string activePath)
    {
        if (!DirectoryLinkManager.Exists(activePath))
        {
            if (previous is not null && !previous.ReusesUserInstall)
                throw new IOException($"Managed Skill entry is missing: {activePath}");
            return;
        }

        var target = DirectoryLinkManager.ResolveTarget(activePath);
        if (target is null)
            throw new IOException($"Refusing to overwrite unmanaged Skill directory: {activePath}");
        if (previous is null || previous.ReusesUserInstall)
            throw new IOException($"Refusing to adopt unmanaged Skill link: {activePath}");
        if (!DirectoryLinkManager.PathsEqual(target, previous.StorePath))
            throw new IOException($"Managed Skill entry points to an unexpected target: {activePath}");
    }

    private static void PreflightBridge(string bridgePath, string activePath)
    {
        if (!DirectoryLinkManager.Exists(bridgePath))
            return;
        var target = DirectoryLinkManager.ResolveTarget(bridgePath);
        if (target is null)
            throw new IOException($"Refusing to overwrite unmanaged Claude Skill directory: {bridgePath}");
        if (!DirectoryLinkManager.PathsEqual(target, activePath))
            throw new IOException($"Claude Skill bridge points to an unexpected target: {bridgePath}");
    }

    private static void EnsurePackageMatches(
        string packagePath,
        IReadOnlyList<ArchiveEntryContent> entries)
    {
        var expectedFiles = entries.ToDictionary(
            entry => entry.RelativePath,
            entry => ComputeSha256Digest(entry.Content),
            StringComparer.Ordinal);
        var actualFiles = DirectoryLinkManager.EnumerateRegularFiles(packagePath)
            .ToDictionary(
                path => Path.GetRelativePath(packagePath, path).Replace('\\', '/'),
                path => ComputeSha256Digest(File.ReadAllBytes(path)),
                StringComparer.Ordinal);
        if (expectedFiles.Count != actualFiles.Count ||
            expectedFiles.Any(entry =>
                !actualFiles.TryGetValue(entry.Key, out var actualHash) ||
                !string.Equals(entry.Value, actualHash, StringComparison.OrdinalIgnoreCase)))
        {
            throw new IOException($"Immutable package directory has been modified: {packagePath}");
        }
    }

    private static void RestoreLockFile(string? path, string? content)
    {
        if (path is null)
            return;
        if (content is null)
        {
            if (File.Exists(path))
                File.Delete(path);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private async Task<SkillVersionSummary> ResolveVersionAsync(
        string packageName,
        string? version,
        bool allowNonStable,
        CancellationToken ct)
    {
        SkillVersionSummary? selected;
        if (!string.IsNullOrWhiteSpace(version))
        {
            selected = await _client.GetVersionAsync(packageName, version, ct);
        }
        else
        {
            var versions = await _client.GetSkillVersionsAsync(packageName, ct);
            selected = versions.FirstOrDefault(candidate =>
                candidate.Installable &&
                string.Equals(candidate.Status, "stable", StringComparison.OrdinalIgnoreCase));
            if (selected is null && allowNonStable)
                selected = versions.FirstOrDefault(candidate => candidate.Installable);
        }

        if (selected is null)
        {
            throw new InvalidOperationException(
                $"No installable stable version found for '{packageName}'. Specify @version with --allow-non-stable when appropriate.");
        }

        if (!selected.Installable)
            throw new InvalidOperationException($"VERSION_NOT_INSTALLABLE: {packageName}@{selected.Version} is {selected.Status}.");
        if (!string.Equals(selected.Status, "stable", StringComparison.OrdinalIgnoreCase) && !allowNonStable)
        {
            throw new InvalidOperationException(
                $"{packageName}@{selected.Version} is {selected.Status}. Re-run with --allow-non-stable after reviewing the risk.");
        }
        if (!string.Equals(selected.ArtifactType, "archive", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(selected.ArchiveUrl) ||
            string.IsNullOrWhiteSpace(selected.ArtifactSha256))
        {
            throw new InvalidOperationException($"{packageName}@{selected.Version} does not expose an installable archive.");
        }

        return selected;
    }

    private async Task<byte[]> DownloadArchiveAsync(
        InstallReferenceResponse installReference,
        SkillVersionSummary selected,
        CancellationToken ct)
    {
        await using var stream = await _client.DownloadInstallArtifactAsync(installReference.SourceUrl, ct);
        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, ct);
            if (read == 0)
                break;
            if (memory.Length + read > MaxArchiveBytes)
                throw new InvalidDataException($"Archive exceeds the {MaxArchiveBytes / 1024 / 1024} MB limit.");
            await memory.WriteAsync(buffer.AsMemory(0, read), ct);
        }

        var bytes = memory.ToArray();
        if (selected.ArtifactSizeBytes > 0 && bytes.LongLength != selected.ArtifactSizeBytes)
        {
            throw new InvalidDataException(
                $"Archive size mismatch: expected {selected.ArtifactSizeBytes}, downloaded {bytes.LongLength}.");
        }

        return bytes;
    }

    private static void ValidateInstallReference(
        string packageName,
        SkillVersionSummary selected,
        InstallReferenceResponse installReference)
    {
        if (!string.Equals(installReference.Version, selected.Version, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The install reference version does not match the selected version.");
        if (!string.Equals(installReference.Status, selected.Status, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The install reference status does not match the version metadata.");
        if (string.IsNullOrWhiteSpace(installReference.SourceUrl))
            throw new InvalidDataException("The install reference does not contain an artifact URL.");
        if (!string.Equals(
                NormalizeDigest(installReference.Digest),
                NormalizeDigest(selected.ArtifactSha256),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The install reference digest does not match the version metadata.");
        }

        var referencedPackage = installReference.Skill.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        if (!string.Equals(referencedPackage, packageName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The install reference identifies a different skill.");
    }

    private static IReadOnlyList<string> FlattenPermissions(InstallPermissions permissions)
    {
        var grants = new List<string>();
        grants.AddRange(permissions.Filesystem.Read.Select(path => $"filesystem.read:{path}"));
        grants.AddRange(permissions.Filesystem.Write.Select(path => $"filesystem.write:{path}"));
        if (permissions.Network.Policy.Equals("unrestricted", StringComparison.OrdinalIgnoreCase))
            grants.Add("network:unrestricted");
        else if (!permissions.Network.Policy.Equals("deny-all", StringComparison.OrdinalIgnoreCase))
            grants.AddRange(permissions.Network.Allow.Select(host => $"network.allow:{host}"));
        grants.AddRange(permissions.Commands.Select(command => $"command:{command}"));
        grants.AddRange(permissions.Secrets.Select(secret => $"secret:{secret}"));
        return grants.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<ArchiveEntryContent> ReadArchiveEntries(byte[] archiveBytes)
    {
        using var memory = new MemoryStream(archiveBytes);
        using var archive = new ZipArchive(memory, ZipArchiveMode.Read);
        var entries = new List<ArchiveEntryContent>();
        long expandedBytes = 0;

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
                continue;
            if (entries.Count >= MaxFiles)
                throw new InvalidDataException($"Archive exceeds the {MaxFiles} file limit.");
            if (entry.Length > MaxFileBytes)
                throw new InvalidDataException($"Archive file exceeds the {MaxFileBytes / 1024 / 1024} MB limit: {entry.FullName}");
            if (entry.CompressedLength > 0 && entry.Length / (double)entry.CompressedLength > MaxCompressionRatio)
                throw new InvalidDataException($"Archive file exceeds the compression ratio limit: {entry.FullName}");
            if (IsSymbolicLink(entry))
                throw new InvalidDataException($"Symbolic links are not allowed: {entry.FullName}");

            expandedBytes = checked(expandedBytes + entry.Length);
            if (expandedBytes > MaxExpandedBytes)
                throw new InvalidDataException($"Archive exceeds the {MaxExpandedBytes / 1024 / 1024} MB expanded size limit.");

            var relativePath = NormalizeArchivePath(entry.FullName);
            using var source = entry.Open();
            using var destinationBytes = new MemoryStream((int)entry.Length);
            source.CopyTo(destinationBytes);
            entries.Add(new ArchiveEntryContent(relativePath, destinationBytes.ToArray()));
        }

        if (entries.Count == 0)
            throw new InvalidDataException("Archive contains no files.");
        if (!entries.Any(entry => string.Equals(entry.RelativePath, "SKILL.md", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Archive must contain SKILL.md at its root.");
        return entries;
    }

    private static async Task WriteEntriesAsync(
        string installPath,
        IReadOnlyList<ArchiveEntryContent> entries,
        CancellationToken ct)
    {
        foreach (var entry in entries)
        {
            var destination = EnsureChildPath(installPath, entry.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await File.WriteAllBytesAsync(destination, entry.Content, ct);
        }
    }

    private static void CopyUnmanagedFiles(
        string sourceRoot,
        string destinationRoot,
        SkillInstallRecord? previous,
        IReadOnlyList<ArchiveEntryContent> newEntries,
        bool force)
    {
        if (!Directory.Exists(sourceRoot))
            return;

        var managedFiles = previous?.Files.ToHashSet(StringComparer.Ordinal) ?? [];
        var newFiles = newEntries.Select(entry => entry.RelativePath).ToHashSet(StringComparer.Ordinal);
        CopyDirectory(sourceRoot, sourceRoot, destinationRoot, managedFiles, newFiles, force);
    }

    private static void CopyDirectory(
        string root,
        string current,
        string destinationRoot,
        IReadOnlySet<string> managedFiles,
        IReadOnlySet<string> newFiles,
        bool force)
    {
        var directory = new DirectoryInfo(current);
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Refusing to manage reparse-point directory: {current}");

        foreach (var item in directory.EnumerateFileSystemInfos())
        {
            if ((item.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Refusing to manage reparse point: {item.FullName}");

            if (item is DirectoryInfo childDirectory)
            {
                CopyDirectory(root, childDirectory.FullName, destinationRoot, managedFiles, newFiles, force);
                continue;
            }

            var relativePath = Path.GetRelativePath(root, item.FullName).Replace('\\', '/');
            if (managedFiles.Contains(relativePath))
                continue;
            if (newFiles.Contains(relativePath))
            {
                if (!force)
                    throw new IOException($"Refusing to overwrite unmanaged file: {item.FullName}");
                continue;
            }

            var destination = EnsureChildPath(destinationRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(item.FullName, destination);
        }
    }

    private static void EnsureManagedFilesUnmodified(SkillInstallRecord record, bool force)
    {
        if (force)
            return;

        foreach (var relativePath in record.Files)
        {
            var path = EnsureChildPath(record.InstallPath, relativePath);
            if (!File.Exists(path))
                throw new IOException($"Refusing to modify installation with a missing managed file: {path}");
            if (!record.FileHashes.TryGetValue(relativePath, out var expected))
                throw new IOException($"Refusing to modify untracked installed file: {path}");

            var actual = ComputeSha256Digest(File.ReadAllBytes(path));
            if (!string.Equals(actual, NormalizeDigest(expected), StringComparison.OrdinalIgnoreCase))
                throw new IOException($"Refusing to overwrite modified installed file: {path}");
        }
    }

    private static string NormalizeArchivePath(string value)
    {
        var relativePath = value.Replace('\\', '/');
        if (Path.IsPathRooted(relativePath))
            throw new InvalidDataException($"Unsafe archive path: {value}");

        var segments = relativePath.Split('/');
        if (segments.Any(segment =>
                segment is "" or "." or ".." ||
                segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            throw new InvalidDataException($"Unsafe archive path: {value}");
        }

        return string.Join('/', segments);
    }

    private static bool IsSymbolicLink(ZipArchiveEntry entry)
    {
        var unixMode = (entry.ExternalAttributes >> 16) & 0xffff;
        var unixType = unixMode & 0xf000;
        var dosAttributes = (FileAttributes)(entry.ExternalAttributes & 0xffff);
        return unixType == 0xa000 || (dosAttributes & FileAttributes.ReparsePoint) != 0;
    }

    private static string EnsureChildPath(string root, string relativePath)
    {
        var normalizedRoot = Path.GetFullPath(root);
        var child = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
        if (!child.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, PathComparison) &&
            !string.Equals(child, normalizedRoot, PathComparison))
        {
            throw new IOException($"Path escapes the managed root: {relativePath}");
        }

        return child;
    }

    private static bool IsPathWithin(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        var normalizedPath = Path.GetFullPath(path);
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, PathComparison);
    }

    private static string NormalizeDigest(string value) =>
        value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            ? value.ToLowerInvariant()
            : $"sha256:{value.ToLowerInvariant()}";

    private static string ComputeSha256Digest(byte[] content) =>
        $"sha256:{Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant()}";

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch (IOException exception)
        {
            System.Diagnostics.Debug.WriteLine($"Unable to remove temporary directory '{path}': {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            System.Diagnostics.Debug.WriteLine($"Unable to remove temporary directory '{path}': {exception.Message}");
        }
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private sealed record ArchiveEntryContent(string RelativePath, byte[] Content);
}
