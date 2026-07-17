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
        CancellationToken ct = default)
    {
        var reference = SkillReference.Parse(name, version);
        var selected = await ResolveVersionAsync(reference.PackageName, reference.Version, allowNonStable, ct);
        var root = ResolveInstallRoot(target, installRoot);
        var installPath = EnsureChildPath(root, reference.PackageName);
        var previous = _registry.Find(reference.Name, target);
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

        if (previous is not null &&
            !string.Equals(Path.GetFullPath(previous.InstallPath), installPath, PathComparison))
        {
            throw new InvalidOperationException(
                $"'{reference.Name}' is already managed at '{previous.InstallPath}'. Uninstall it before changing install root.");
        }

        var conflicting = _registry.Load().Installations.FirstOrDefault(record =>
            !string.Equals(record.Name, reference.Name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Path.GetFullPath(record.InstallPath), installPath, PathComparison));
        if (conflicting is not null)
        {
            throw new InvalidOperationException(
                $"Install path '{installPath}' is already managed by '{conflicting.Name}'.");
        }

        var archiveBytes = await DownloadArchiveAsync(installReference, selected, ct);
        var archiveHash = ComputeSha256Digest(archiveBytes);
        var expectedHash = NormalizeDigest(installReference.Digest);
        if (!string.Equals(archiveHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"DIGEST_MISMATCH: expected {expectedHash}, downloaded {archiveHash}.");
        }

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
            else if (File.Exists(installPath))
            {
                throw new IOException($"Install path is a file: {installPath}");
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
                Files = files,
                FileHashes = fileHashes,
                GrantedPermissions = grantedPermissions,
                ArchiveHash = archiveHash,
                Server = _server,
                PreviousVersion = previous?.Version,
                InstalledAt = DateTimeOffset.UtcNow
            };

            _registry.Upsert(record, previous);
            var removed = previous?.Files.Except(files, StringComparer.Ordinal).ToArray() ?? [];
            TryDeleteDirectory(backupRoot);
            return new InstallResult(record, removed);
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

    public Task<IReadOnlyList<string>> UninstallAsync(
        string name,
        string target,
        bool force,
        CancellationToken ct = default)
    {
        _ = ct;
        var reference = SkillReference.Parse(name);
        var record = _registry.Find(reference.Name, target)
                     ?? throw new InvalidOperationException(
                         $"'{reference.Name}' is not installed for target '{target}'.");

        EnsureManagedFilesUnmodified(record, force);
        var installPath = Path.GetFullPath(record.InstallPath);
        var root = Path.GetDirectoryName(installPath)
                   ?? throw new IOException($"Invalid install path: {installPath}");
        var operationId = Guid.NewGuid().ToString("N");
        var stagingRoot = EnsureChildPath(root, Path.Combine(".agent-skill-store-staging", operationId));
        var stagedInstallPath = EnsureChildPath(stagingRoot, Path.GetFileName(installPath));
        var backupRoot = EnsureChildPath(root, Path.Combine(".agent-skill-store-backup", operationId));
        var backupPath = EnsureChildPath(backupRoot, Path.GetFileName(installPath));
        var existingMoved = false;
        var stagedMoved = false;

        try
        {
            Directory.CreateDirectory(stagedInstallPath);
            CopyUnmanagedFiles(installPath, stagedInstallPath, record, [], force: false);

            if (Directory.Exists(installPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                Directory.Move(installPath, backupPath);
                existingMoved = true;
            }

            if (Directory.Exists(stagedInstallPath) &&
                Directory.EnumerateFileSystemEntries(stagedInstallPath).Any())
            {
                Directory.Move(stagedInstallPath, installPath);
                stagedMoved = true;
            }

            _registry.Remove(record);
            TryDeleteDirectory(backupRoot);
            return Task.FromResult<IReadOnlyList<string>>(record.Files.ToArray());
        }
        catch
        {
            if (stagedMoved && Directory.Exists(installPath))
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

    public async Task<InstallResult> UpdateAsync(
        string name,
        string target,
        string? installRoot,
        bool force,
        bool allowNonStable = false,
        bool acceptPermissionExpansion = false,
        CancellationToken ct = default)
    {
        var reference = SkillReference.Parse(name);
        var current = _registry.Find(reference.Name, target)
                      ?? throw new InvalidOperationException(
                          $"'{reference.Name}' is not installed for target '{target}'.");
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
            installRoot ?? Path.GetDirectoryName(current.InstallPath),
            force,
            allowNonStable,
            acceptPermissionExpansion,
            ct);
    }

    public async Task<InstallResult> RollbackAsync(
        string name,
        string target,
        string? version,
        bool force,
        bool allowNonStable = false,
        bool acceptPermissionExpansion = false,
        CancellationToken ct = default)
    {
        var reference = SkillReference.Parse(name, version);
        var current = _registry.Find(reference.Name, target);
        var previous = _registry.GetRollback(reference.Name, target, reference.Version);
        var rollbackVersion = reference.Version ?? previous?.Version
                              ?? throw new InvalidOperationException(
                                  $"No rollback record found for '{reference.Name}' on target '{target}'.");
        var installRoot = current is not null
            ? Path.GetDirectoryName(current.InstallPath)
            : previous is not null
                ? Path.GetDirectoryName(previous.InstallPath)
                : null;

        var result = await InstallAsync(
            reference.Name,
            rollbackVersion,
            target,
            installRoot,
            force,
            allowNonStable || previous is not null,
            acceptPermissionExpansion,
            ct);
        if (previous is not null)
            _registry.RemoveHistory(previous);
        return result;
    }

    public static string ResolveInstallRoot(string target, string? explicitRoot)
    {
        if (!string.IsNullOrWhiteSpace(explicitRoot))
            return Path.GetFullPath(explicitRoot);

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return target.ToLowerInvariant() switch
        {
            "codex" => Path.Combine(profile, ".codex", "skills"),
            "claude" => Path.Combine(profile, ".claude", "skills"),
            "pi" => Path.Combine(profile, ".pi", "agent", "skills"),
            _ => throw new ArgumentException("Target must be one of: codex, claude, pi.", nameof(target))
        };
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
