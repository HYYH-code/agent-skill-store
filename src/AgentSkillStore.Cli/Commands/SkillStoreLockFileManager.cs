// -----------------------------------------------------------------------
// <copyright file="SkillStoreLockFileManager.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;
using AgentSkillStore.Cli.Json;

namespace AgentSkillStore.Cli.Commands;

internal static class SkillStoreLockFileManager
{
    public static SkillStoreLockFile Load(string path)
    {
        if (!File.Exists(path))
            return new SkillStoreLockFile();
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize(json, CliJsonContext.Default.SkillStoreLockFile)
               ?? new SkillStoreLockFile();
    }

    public static void Upsert(string path, string server, SkillInstallRecord record)
    {
        var lockFile = Load(path);
        lockFile.Skills.RemoveAll(entry =>
            string.Equals(entry.Name, record.Name, StringComparison.OrdinalIgnoreCase));
        lockFile.Skills.Add(new SkillStoreLockEntry
        {
            Name = record.Name,
            Version = record.Version,
            Digest = record.ArchiveHash,
            GrantedPermissions = record.GrantedPermissions.Order(StringComparer.Ordinal).ToArray()
        });
        Save(path, lockFile with
        {
            Server = server,
            Skills = lockFile.Skills.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ToList()
        });
    }

    public static void Remove(string path, string name)
    {
        var lockFile = Load(path);
        if (lockFile.Skills.RemoveAll(entry =>
                string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase)) == 0)
        {
            return;
        }

        Save(path, lockFile);
    }

    public static void Save(string path, SkillStoreLockFile lockFile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(lockFile, CliJsonContext.Default.SkillStoreLockFile);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
