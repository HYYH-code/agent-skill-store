// -----------------------------------------------------------------------
// <copyright file="MigrateCommandTests.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Security.Cryptography;
using AgentSkillStore.Cli.Commands;
using Xunit;

namespace AgentSkillStore.Cli.Tests;

public sealed class MigrateCommandTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        $"agent-skill-store-migrate-tests-{Guid.NewGuid():N}");

    [Fact]
    public void Classify_RejectsUntrackedFiles()
    {
        Directory.CreateDirectory(_tempRoot);
        var skillPath = Path.Combine(_tempRoot, "my-skill");
        Directory.CreateDirectory(skillPath);
        var skillFile = Path.Combine(skillPath, "SKILL.md");
        File.WriteAllText(skillFile, "# Skill");
        File.WriteAllText(Path.Combine(skillPath, "user-notes.md"), "keep me");

        var item = MigrateCommand.Classify(CreateRecord(skillPath, skillFile), duplicate: false);

        Assert.Equal("modified", item.Classification);
        Assert.Contains("untracked file", item.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Classify_RejectsInvalidArchiveDigest()
    {
        Directory.CreateDirectory(_tempRoot);
        var skillPath = Path.Combine(_tempRoot, "my-skill");
        Directory.CreateDirectory(skillPath);
        var skillFile = Path.Combine(skillPath, "SKILL.md");
        File.WriteAllText(skillFile, "# Skill");
        var record = CreateRecord(skillPath, skillFile) with
        {
            ArchiveHash = $"sha256:{new string('x', 64)}"
        };

        var item = MigrateCommand.Classify(record, duplicate: false);

        Assert.Equal("conflict", item.Classification);
        Assert.Contains("archive digest", item.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static SkillInstallRecord CreateRecord(string skillPath, string skillFile) => new()
    {
        Name = "connected/my-skill",
        PackageName = "my-skill",
        Version = "1.0.0",
        Target = "codex",
        InstallPath = skillPath,
        ActivePath = skillPath,
        Files = ["SKILL.md"],
        FileHashes = new Dictionary<string, string>
        {
            ["SKILL.md"] = Digest(File.ReadAllBytes(skillFile))
        },
        ArchiveHash = $"sha256:{new string('a', 64)}",
        Layout = "legacy-direct",
        Scope = "user"
    };

    private static string Digest(byte[] content) =>
        $"sha256:{Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant()}";

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
