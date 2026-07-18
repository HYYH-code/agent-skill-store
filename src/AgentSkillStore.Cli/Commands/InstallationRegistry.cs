// -----------------------------------------------------------------------
// <copyright file="InstallationRegistry.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;
using System.Runtime.InteropServices;
using AgentSkillStore.Cli.Config;
using AgentSkillStore.Cli.Json;

namespace AgentSkillStore.Cli.Commands;

internal sealed class InstallationRegistry
{
    public static readonly string RegistryPath = Path.Combine(
        ConfigResolver.ConfigDirectory,
        "installed.json");

    private readonly string _path;

    public InstallationRegistry(string? path = null)
    {
        _path = path ?? RegistryPath;
    }

    public SkillInstallState Load()
    {
        if (!File.Exists(_path))
            return new SkillInstallState();

        var json = File.ReadAllText(_path);
        var state = JsonSerializer.Deserialize(json, CliJsonContext.Default.SkillInstallState)
                    ?? new SkillInstallState();
        if (state.SchemaVersion >= 2)
            return state;

        return state with
        {
            SchemaVersion = 2,
            Installations = state.Installations.Select(NormalizeLegacy).ToList(),
            History = state.History.Select(NormalizeLegacy).ToList()
        };
    }

    public void Save(SkillInstallState state)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(state, CliJsonContext.Default.SkillInstallState);
        var tempPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _path, true);
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                File.SetUnixFileMode(_path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    public SkillInstallRecord? Find(string name, string target)
    {
        return Load().Installations.FirstOrDefault(r =>
            string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(r.Target, target, StringComparison.OrdinalIgnoreCase));
    }

    public SkillInstallRecord? Find(string name, SkillStoreContext context)
    {
        return Load().Installations.FirstOrDefault(record =>
            string.Equals(record.Name, name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(record.Layout, "shared", StringComparison.Ordinal) &&
            string.Equals(record.Scope, context.Scope, StringComparison.Ordinal) &&
            ProjectRootsEqual(record.ProjectRoot, context.ProjectRoot));
    }

    public SkillInstallRecord? FindLegacy(string name, string target)
    {
        return Load().Installations.FirstOrDefault(record =>
            string.Equals(record.Name, name, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(record.Layout, "shared", StringComparison.Ordinal) &&
            string.Equals(record.Target, target, StringComparison.OrdinalIgnoreCase));
    }

    public SkillInstallRecord? FindAnyLegacy(string name)
    {
        return Load().Installations.FirstOrDefault(record =>
            string.Equals(record.Name, name, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(record.Layout, "shared", StringComparison.Ordinal));
    }

    public void Upsert(SkillInstallRecord record, SkillInstallRecord? previous)
    {
        var state = Load();
        state.Installations.RemoveAll(r =>
            string.Equals(r.Name, record.Name, StringComparison.OrdinalIgnoreCase) &&
            SameInstallationIdentity(r, record));

        if (previous is not null)
            state.History.Add(previous);

        state.Installations.Add(record);
        Save(state);
    }

    public void Remove(SkillInstallRecord record)
    {
        var state = Load();
        state.Installations.RemoveAll(r =>
            string.Equals(r.Name, record.Name, StringComparison.OrdinalIgnoreCase) &&
            SameInstallationIdentity(r, record));
        state.History.Add(record);
        Save(state);
    }

    public void Restore(SkillInstallState state) => Save(state);

    public void Replace(SkillInstallRecord oldRecord, SkillInstallRecord newRecord)
    {
        var state = Load();
        state.Installations.RemoveAll(record =>
            string.Equals(record.Name, oldRecord.Name, StringComparison.OrdinalIgnoreCase) &&
            SameInstallationIdentity(record, oldRecord));
        state.Installations.Add(newRecord);
        Save(state);
    }

    public SkillInstallRecord? GetRollback(string name, string target, string? version = null)
    {
        var state = Load();
        for (var i = state.History.Count - 1; i >= 0; i--)
        {
            var candidate = state.History[i];
            if (!string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(candidate.Target, target, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(version) &&
                 !string.Equals(candidate.Version, version, StringComparison.OrdinalIgnoreCase)))
                continue;

            return candidate;
        }

        return null;
    }

    public SkillInstallRecord? GetRollback(string name, SkillStoreContext context, string? version = null)
    {
        var state = Load();
        for (var index = state.History.Count - 1; index >= 0; index--)
        {
            var candidate = state.History[index];
            if (!string.Equals(candidate.Layout, "shared", StringComparison.Ordinal) ||
                !string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(candidate.Scope, context.Scope, StringComparison.Ordinal) ||
                !ProjectRootsEqual(candidate.ProjectRoot, context.ProjectRoot) ||
                (!string.IsNullOrWhiteSpace(version) &&
                 !string.Equals(candidate.Version, version, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            return candidate;
        }

        return null;
    }

    public void RemoveHistory(SkillInstallRecord record)
    {
        var state = Load();
        var index = state.History.FindLastIndex(candidate =>
            string.Equals(candidate.Name, record.Name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.Target, record.Target, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.Version, record.Version, StringComparison.OrdinalIgnoreCase) &&
            candidate.InstalledAt == record.InstalledAt);
        if (index < 0)
            return;

        state.History.RemoveAt(index);
        Save(state);
    }

    private static SkillInstallRecord NormalizeLegacy(SkillInstallRecord record) => record with
    {
        Layout = string.IsNullOrWhiteSpace(record.Layout) ? "legacy-direct" : record.Layout,
        Scope = string.IsNullOrWhiteSpace(record.Scope) ? "user" : record.Scope,
        ActivePath = string.IsNullOrWhiteSpace(record.ActivePath) ? record.InstallPath : record.ActivePath
    };

    private static bool SameInstallationIdentity(SkillInstallRecord left, SkillInstallRecord right)
    {
        if (string.Equals(left.Layout, "shared", StringComparison.Ordinal) ||
            string.Equals(right.Layout, "shared", StringComparison.Ordinal))
        {
            return string.Equals(left.Layout, right.Layout, StringComparison.Ordinal) &&
                   string.Equals(left.Scope, right.Scope, StringComparison.Ordinal) &&
                   ProjectRootsEqual(left.ProjectRoot, right.ProjectRoot);
        }

        return string.Equals(left.Target, right.Target, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ProjectRootsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), comparison);
    }
}
