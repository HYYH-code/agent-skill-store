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
        return JsonSerializer.Deserialize(json, CliJsonContext.Default.SkillInstallState)
               ?? new SkillInstallState();
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

    public void Upsert(SkillInstallRecord record, SkillInstallRecord? previous)
    {
        var state = Load();
        state.Installations.RemoveAll(r =>
            string.Equals(r.Name, record.Name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(r.Target, record.Target, StringComparison.OrdinalIgnoreCase));

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
            string.Equals(r.Target, record.Target, StringComparison.OrdinalIgnoreCase));
        state.History.Add(record);
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
}
