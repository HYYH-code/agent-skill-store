// -----------------------------------------------------------------------
// <copyright file="SkillInstallerTests.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgentSkillStore.Client;
using AgentSkillStore.Cli.Commands;
using Xunit;

namespace AgentSkillStore.Cli.Tests;

public sealed class SkillInstallerTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"skillstore-cli-{Guid.NewGuid():N}");

    [Fact]
    public async Task Install_VerifiesArchiveAndWritesNamespacedSkillAtomically()
    {
        var ct = TestContext.Current.CancellationToken;
        var archive = CreateArchive(("SKILL.md", "# Version 1"), ("references/readme.md", "reference"));
        var installReferenceRequests = 0;
        var installer = CreateInstaller(
            new Dictionary<string, byte[]> { ["1.0.0"] = archive },
            onInstallReference: () => installReferenceRequests++);

        var result = await installer.InstallAsync(
            "connected/my-skill@1.0.0",
            null,
            "codex",
            _tempRoot,
            false,
            ct: ct);

        Assert.Equal("connected/my-skill", result.Record.Name);
        Assert.Equal("my-skill", result.Record.PackageName);
        Assert.Equal("1.0.0", result.Record.Version);
        Assert.Equal("# Version 1", await File.ReadAllTextAsync(Path.Combine(ActiveSkillPath, "SKILL.md"), ct));
        Assert.Equal(Digest(archive), result.Record.ArchiveHash);
        Assert.Equal("shared", result.Record.Layout);
        Assert.Equal("user", result.Record.Scope);
        Assert.True(DirectoryLinkManager.IsDirectoryLink(ActiveSkillPath));
        Assert.Equal(1, installReferenceRequests);
        var stagingRoot = Path.Combine(_tempRoot, ".skillstore", "staging");
        Assert.False(Directory.Exists(stagingRoot) && Directory.EnumerateFileSystemEntries(stagingRoot).Any());
    }

    [Fact]
    public async Task Install_DigestMismatchLeavesTargetUntouched()
    {
        var ct = TestContext.Current.CancellationToken;
        var archive = CreateArchive(("SKILL.md", "# Skill"));
        var installer = CreateInstaller(
            new Dictionary<string, byte[]> { ["1.0.0"] = archive },
            digestOverride: "sha256:" + new string('0', 64));

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => installer.InstallAsync(
            "my-skill",
            "1.0.0",
            "codex",
            _tempRoot,
            false,
            ct: ct));

        Assert.Contains("DIGEST_MISMATCH", error.Message);
        Assert.False(DirectoryLinkManager.Exists(ActiveSkillPath));
    }

    [Fact]
    public async Task Install_PathTraversalIsRejectedWithoutEscapingRoot()
    {
        var ct = TestContext.Current.CancellationToken;
        var archive = CreateArchive(("SKILL.md", "# Skill"), ("../escape.txt", "escape"));
        var installer = CreateInstaller(new Dictionary<string, byte[]> { ["1.0.0"] = archive });

        await Assert.ThrowsAsync<InvalidDataException>(() => installer.InstallAsync(
            "my-skill",
            "1.0.0",
            "codex",
            _tempRoot,
            false,
            ct: ct));

        Assert.False(File.Exists(Path.Combine(_tempRoot, "escape.txt")));
        Assert.False(DirectoryLinkManager.Exists(ActiveSkillPath));
    }

    [Fact]
    public async Task FailedUpgradePreservesCurrentInstallation()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = CreateArchive(("SKILL.md", "# Version 1"));
        var unsafeSecond = CreateArchive(("SKILL.md", "# Version 2"), ("../escape.txt", "escape"));
        var installer = CreateInstaller(new Dictionary<string, byte[]>
        {
            ["1.0.0"] = first,
            ["2.0.0"] = unsafeSecond
        });

        await installer.InstallAsync("my-skill", "1.0.0", "codex", _tempRoot, false, ct: ct);
        await Assert.ThrowsAsync<InvalidDataException>(() => installer.InstallAsync(
            "my-skill",
            "2.0.0",
            "codex",
            _tempRoot,
            false,
            ct: ct));

        Assert.Equal("# Version 1", await File.ReadAllTextAsync(Path.Combine(ActiveSkillPath, "SKILL.md"), ct));
        Assert.Equal("1.0.0", new InstallationRegistry(RegistryPath).Find("my-skill", "codex")?.Version);
    }

    [Fact]
    public async Task Uninstall_ProtectsModifiedFilesUnlessForced()
    {
        var ct = TestContext.Current.CancellationToken;
        var archive = CreateArchive(("SKILL.md", "# Version 1"));
        var installer = CreateInstaller(new Dictionary<string, byte[]> { ["1.0.0"] = archive });
        await installer.InstallAsync("my-skill", "1.0.0", "codex", _tempRoot, false, ct: ct);
        var skillPath = Path.Combine(ActiveSkillPath, "SKILL.md");
        await File.WriteAllTextAsync(skillPath, "user change", ct);

        await Assert.ThrowsAsync<IOException>(() => installer.UninstallAsync("my-skill", "codex", false, ct));
        Assert.True(File.Exists(skillPath));

        await installer.UninstallAsync("my-skill", "codex", true, ct);
        Assert.Null(new InstallationRegistry(RegistryPath).Find("my-skill", "codex"));
        Assert.False(File.Exists(skillPath));
    }

    [Fact]
    public async Task Rollback_ReinstallsPreviousVerifiedVersion()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = CreateArchive(("SKILL.md", "# Version 1"));
        var second = CreateArchive(("SKILL.md", "# Version 2"));
        var installer = CreateInstaller(new Dictionary<string, byte[]>
        {
            ["1.0.0"] = first,
            ["2.0.0"] = second
        });

        await installer.InstallAsync("my-skill", "1.0.0", "codex", _tempRoot, false, ct: ct);
        await installer.InstallAsync("my-skill", "2.0.0", "codex", _tempRoot, false, ct: ct);
        var result = await installer.RollbackAsync("my-skill", "codex", null, false, ct: ct);

        Assert.Equal("1.0.0", result.Record.Version);
        Assert.Equal("# Version 1", await File.ReadAllTextAsync(Path.Combine(ActiveSkillPath, "SKILL.md"), ct));
    }

    [Fact]
    public async Task NonStableVersionRequiresExplicitAcknowledgement()
    {
        var ct = TestContext.Current.CancellationToken;
        var archive = CreateArchive(("SKILL.md", "# Beta"));
        var installer = CreateInstaller(
            new Dictionary<string, byte[]> { ["1.0.0-beta.1"] = archive },
            status: "beta");

        await Assert.ThrowsAsync<InvalidOperationException>(() => installer.InstallAsync(
            "my-skill",
            "1.0.0-beta.1",
            "codex",
            _tempRoot,
            false,
            ct: ct));

        var result = await installer.InstallAsync(
            "my-skill",
            "1.0.0-beta.1",
            "codex",
            _tempRoot,
            false,
            allowNonStable: true,
            ct: ct);
        Assert.Equal("1.0.0-beta.1", result.Record.Version);
    }

    [Fact]
    public async Task Install_RejectsCrossOriginArtifactUrl()
    {
        var ct = TestContext.Current.CancellationToken;
        var archive = CreateArchive(("SKILL.md", "# Skill"));
        var installer = CreateInstaller(
            new Dictionary<string, byte[]> { ["1.0.0"] = archive },
            sourceUrlOverride: "https://attacker.example/archive.zip");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => installer.InstallAsync(
            "my-skill",
            "1.0.0",
            "codex",
            _tempRoot,
            false,
            ct: ct));

        Assert.Contains("configured AgentSkillStore origin", error.Message);
        Assert.False(DirectoryLinkManager.Exists(ActiveSkillPath));
    }

    [Fact]
    public async Task Update_PermissionExpansionRequiresExplicitAcknowledgement()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = CreateArchive(("SKILL.md", "# Version 1"));
        var second = CreateArchive(("SKILL.md", "# Version 2"));
        var permissions = new Dictionary<string, InstallPermissions>
        {
            ["1.0.0"] = new(),
            ["2.0.0"] = new() { Commands = ["dotnet"] }
        };
        var installer = CreateInstaller(
            new Dictionary<string, byte[]> { ["1.0.0"] = first, ["2.0.0"] = second },
            permissionsByVersion: permissions);

        await installer.InstallAsync("my-skill", "1.0.0", "codex", _tempRoot, false, ct: ct);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => installer.InstallAsync(
            "my-skill",
            "2.0.0",
            "codex",
            _tempRoot,
            false,
            ct: ct));
        Assert.Contains("PERMISSION_CONFIRMATION_REQUIRED", error.Message);
        Assert.Contains("command:dotnet", error.Message);
        Assert.Equal("# Version 1", await File.ReadAllTextAsync(Path.Combine(ActiveSkillPath, "SKILL.md"), ct));

        var result = await installer.InstallAsync(
            "my-skill",
            "2.0.0",
            "codex",
            _tempRoot,
            false,
            acceptPermissionExpansion: true,
            ct: ct);
        Assert.Contains("command:dotnet", result.Record.GrantedPermissions);
    }

    [Fact]
    public async Task ProjectInstall_WritesLockFileAndKeepsClaudeOnTheSharedEntry()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectRoot = Path.Combine(_tempRoot, "project");
        var agentsRoot = Path.Combine(projectRoot, ".agents");
        Directory.CreateDirectory(projectRoot);
        var first = CreateArchive(("SKILL.md", "# Version 1"));
        var second = CreateArchive(("SKILL.md", "# Version 2"));
        var installer = CreateInstaller(new Dictionary<string, byte[]>
        {
            ["1.0.0"] = first,
            ["2.0.0"] = second
        });

        var installed = await installer.InstallAsync(
            "connected/my-skill",
            "1.0.0",
            "claude",
            agentsRoot,
            false,
            scope: "project",
            workingDirectory: projectRoot,
            ct: ct);
        var bridgePath = Path.Combine(projectRoot, ".claude", "skills", "my-skill");
        Assert.True(DirectoryLinkManager.IsDirectoryLink(installed.Record.ActivePath));
        Assert.True(DirectoryLinkManager.IsDirectoryLink(bridgePath));
        Assert.True(DirectoryLinkManager.PathsEqual(
            DirectoryLinkManager.ResolveTarget(bridgePath),
            installed.Record.ActivePath));
        Assert.Equal("# Version 1", await File.ReadAllTextAsync(Path.Combine(bridgePath, "SKILL.md"), ct));

        var updated = await installer.UpdateAsync(
            "connected/my-skill",
            "claude",
            agentsRoot,
            false,
            scope: "project",
            workingDirectory: projectRoot,
            ct: ct);
        Assert.Equal("2.0.0", updated.Record.Version);
        Assert.Equal("# Version 2", await File.ReadAllTextAsync(Path.Combine(bridgePath, "SKILL.md"), ct));

        var lockFile = SkillStoreLockFileManager.Load(Path.Combine(projectRoot, "skillstore.lock.json"));
        var entry = Assert.Single(lockFile.Skills);
        Assert.Equal("connected/my-skill", entry.Name);
        Assert.Equal("2.0.0", entry.Version);
        Assert.Equal(Digest(second), entry.Digest);
    }

    [Fact]
    public async Task Install_RestoresRegisteredClaudeBridgeWhenItWasRemoved()
    {
        var ct = TestContext.Current.CancellationToken;
        var archive = CreateArchive(("SKILL.md", "# Version 1"));
        var installer = CreateInstaller(new Dictionary<string, byte[]> { ["1.0.0"] = archive });
        var projectRoot = Path.Combine(_tempRoot, "bridge-project");
        var agentsRoot = Path.Combine(projectRoot, ".agents");
        Directory.CreateDirectory(projectRoot);

        var first = await installer.InstallAsync(
            "my-skill",
            "1.0.0",
            "claude",
            agentsRoot,
            false,
            scope: "project",
            workingDirectory: projectRoot,
            ct: ct);
        var bridge = Assert.Single(first.Record.Bridges);
        DirectoryLinkManager.Remove(bridge.Path, bridge.TargetPath);
        Assert.False(DirectoryLinkManager.Exists(bridge.Path));

        var restored = await installer.InstallAsync(
            "my-skill",
            "1.0.0",
            "codex",
            agentsRoot,
            false,
            scope: "project",
            workingDirectory: projectRoot,
            ct: ct);

        Assert.True(DirectoryLinkManager.IsDirectoryLink(bridge.Path));
        Assert.True(DirectoryLinkManager.PathsEqual(
            DirectoryLinkManager.ResolveTarget(bridge.Path),
            restored.Record.ActivePath));
    }

    [Fact]
    public async Task ProjectInstall_ReusesIdenticalUserDigestWithoutDuplicateEntry()
    {
        var ct = TestContext.Current.CancellationToken;
        var archive = CreateArchive(("SKILL.md", "# Version 1"));
        var installer = CreateInstaller(new Dictionary<string, byte[]> { ["1.0.0"] = archive });
        var userRoot = Path.Combine(_tempRoot, "user-agents");
        var projectRoot = Path.Combine(_tempRoot, "project");
        var projectAgentsRoot = Path.Combine(projectRoot, ".agents");
        Directory.CreateDirectory(projectRoot);

        var user = await installer.InstallAsync(
            "connected/my-skill", "1.0.0", "codex", userRoot, false, ct: ct);
        var project = await installer.InstallAsync(
            "connected/my-skill",
            "1.0.0",
            "codex",
            projectAgentsRoot,
            false,
            scope: "project",
            workingDirectory: projectRoot,
            ct: ct);

        Assert.True(project.Record.ReusesUserInstall);
        Assert.Equal(user.Record.ActivePath, project.Record.ActivePath);
        Assert.False(DirectoryLinkManager.Exists(Path.Combine(projectAgentsRoot, "skills", "my-skill")));
    }

    [Fact]
    public async Task ProjectReuse_ExplicitClaudeCreatesAndRemovesProjectBridge()
    {
        var ct = TestContext.Current.CancellationToken;
        var archive = CreateArchive(("SKILL.md", "# Version 1"));
        var installer = CreateInstaller(new Dictionary<string, byte[]> { ["1.0.0"] = archive });
        var projectRoot = Path.Combine(_tempRoot, "project");
        var projectAgentsRoot = Path.Combine(projectRoot, ".agents");
        Directory.CreateDirectory(projectRoot);

        var user = await installer.InstallAsync(
            "connected/my-skill", "1.0.0", "codex", _tempRoot, false, ct: ct);
        var project = await installer.InstallAsync(
            "connected/my-skill",
            "1.0.0",
            "claude",
            projectAgentsRoot,
            false,
            scope: "project",
            workingDirectory: projectRoot,
            ct: ct);

        var bridge = Assert.Single(project.Record.Bridges);
        Assert.True(DirectoryLinkManager.PathsEqual(
            DirectoryLinkManager.ResolveTarget(bridge.Path),
            user.Record.ActivePath));

        await installer.UninstallAsync(
            "connected/my-skill",
            "all",
            false,
            scope: "project",
            installRoot: projectAgentsRoot,
            workingDirectory: projectRoot,
            ct: ct);

        Assert.False(DirectoryLinkManager.Exists(bridge.Path));
        Assert.True(DirectoryLinkManager.IsDirectoryLink(user.Record.ActivePath));
    }

    [Fact]
    public async Task ProjectReuse_UninstallRestoresRegistryWhenLockFileIsInvalid()
    {
        var ct = TestContext.Current.CancellationToken;
        var archive = CreateArchive(("SKILL.md", "# Version 1"));
        var installer = CreateInstaller(new Dictionary<string, byte[]> { ["1.0.0"] = archive });
        var projectRoot = Path.Combine(_tempRoot, "project");
        Directory.CreateDirectory(projectRoot);

        await installer.InstallAsync("connected/my-skill", "1.0.0", "codex", _tempRoot, false, ct: ct);
        await installer.InstallAsync(
            "connected/my-skill",
            "1.0.0",
            "codex",
            Path.Combine(projectRoot, ".agents"),
            false,
            scope: "project",
            workingDirectory: projectRoot,
            ct: ct);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "skillstore.lock.json"), "{", ct);

        await Assert.ThrowsAnyAsync<Exception>(() => installer.UninstallAsync(
            "connected/my-skill",
            "all",
            false,
            scope: "project",
            installRoot: Path.Combine(projectRoot, ".agents"),
            workingDirectory: projectRoot,
            ct: ct));

        var context = SkillStoreContext.Resolve(
            "project",
            Path.Combine(projectRoot, ".agents"),
            projectRoot);
        Assert.NotNull(new InstallationRegistry(RegistryPath).Find("connected/my-skill", context));
    }

    [Fact]
    public async Task ProjectInstall_RejectsDifferentUserDigest()
    {
        var ct = TestContext.Current.CancellationToken;
        var installer = CreateInstaller(new Dictionary<string, byte[]>
        {
            ["1.0.0"] = CreateArchive(("SKILL.md", "# Version 1")),
            ["2.0.0"] = CreateArchive(("SKILL.md", "# Version 2"))
        });
        var userRoot = Path.Combine(_tempRoot, "user-agents");
        var projectRoot = Path.Combine(_tempRoot, "project");
        Directory.CreateDirectory(projectRoot);
        await installer.InstallAsync(
            "connected/my-skill", "1.0.0", "codex", userRoot, false, ct: ct);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => installer.InstallAsync(
            "connected/my-skill",
            "2.0.0",
            "codex",
            Path.Combine(projectRoot, ".agents"),
            false,
            scope: "project",
            workingDirectory: projectRoot,
            ct: ct));

        Assert.Contains("SCOPE_VERSION_CONFLICT", error.Message);
    }

    [Fact]
    public async Task UserInstall_RejectsDifferentProjectDigest()
    {
        var ct = TestContext.Current.CancellationToken;
        var installer = CreateInstaller(new Dictionary<string, byte[]>
        {
            ["1.0.0"] = CreateArchive(("SKILL.md", "# Version 1")),
            ["2.0.0"] = CreateArchive(("SKILL.md", "# Version 2"))
        });
        var projectRoot = Path.Combine(_tempRoot, "project");
        Directory.CreateDirectory(projectRoot);
        await installer.InstallAsync(
            "connected/my-skill",
            "1.0.0",
            "codex",
            Path.Combine(projectRoot, ".agents"),
            false,
            scope: "project",
            workingDirectory: projectRoot,
            ct: ct);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => installer.InstallAsync(
            "connected/my-skill",
            "2.0.0",
            "codex",
            _tempRoot,
            false,
            ct: ct));

        Assert.Contains("SCOPE_VERSION_CONFLICT", error.Message);
    }

    [Fact]
    public async Task UserUninstall_RejectsWhenProjectReusesTheInstallation()
    {
        var ct = TestContext.Current.CancellationToken;
        var archive = CreateArchive(("SKILL.md", "# Version 1"));
        var installer = CreateInstaller(new Dictionary<string, byte[]> { ["1.0.0"] = archive });
        var projectRoot = Path.Combine(_tempRoot, "project");
        Directory.CreateDirectory(projectRoot);
        var user = await installer.InstallAsync(
            "connected/my-skill", "1.0.0", "codex", _tempRoot, false, ct: ct);
        await installer.InstallAsync(
            "connected/my-skill",
            "1.0.0",
            "codex",
            Path.Combine(projectRoot, ".agents"),
            false,
            scope: "project",
            workingDirectory: projectRoot,
            ct: ct);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => installer.UninstallAsync(
            "connected/my-skill",
            "all",
            false,
            scope: "user",
            installRoot: _tempRoot,
            ct: ct));

        Assert.Contains("SCOPE_DEPENDENCY_CONFLICT", error.Message);
        Assert.True(DirectoryLinkManager.PathsEqual(
            DirectoryLinkManager.ResolveTarget(user.Record.ActivePath),
            user.Record.StorePath));
    }

    [Fact]
    public async Task Doctor_DetectsMissingSharedEntry()
    {
        var ct = TestContext.Current.CancellationToken;
        var archive = CreateArchive(("SKILL.md", "# Version 1"));
        var installer = CreateInstaller(new Dictionary<string, byte[]> { ["1.0.0"] = archive });
        var result = await installer.InstallAsync(
            "connected/my-skill", "1.0.0", "codex", _tempRoot, false, ct: ct);
        DirectoryLinkManager.Remove(result.Record.ActivePath, result.Record.StorePath);

        var findings = DoctorCommand.Diagnose(
            new InstallationRegistry(RegistryPath).Load(),
            SkillStoreContext.Resolve("user", _tempRoot));

        Assert.Contains(findings, finding => finding.Code == "ACTIVE_LINK_INVALID");
    }

    [Fact]
    public async Task Doctor_DetectsUntrackedPackageFile()
    {
        var ct = TestContext.Current.CancellationToken;
        var archive = CreateArchive(("SKILL.md", "# Version 1"));
        var installer = CreateInstaller(new Dictionary<string, byte[]> { ["1.0.0"] = archive });
        var result = await installer.InstallAsync(
            "my-skill",
            "1.0.0",
            "agents",
            _tempRoot,
            false,
            ct: ct);
        await File.WriteAllTextAsync(Path.Combine(result.Record.StorePath, "user-notes.md"), "unexpected", ct);

        var findings = DoctorCommand.Diagnose(
            new InstallationRegistry(RegistryPath).Load(),
            SkillStoreContext.Resolve("user", _tempRoot));

        Assert.Contains(findings, finding => finding.Code == "FILE_UNTRACKED");
    }

    [Fact]
    public void Doctor_ProjectScopeDoesNotReportUserLegacyInstallations()
    {
        var projectRoot = Path.Combine(_tempRoot, "project");
        Directory.CreateDirectory(Path.Combine(projectRoot, ".git"));
        var state = new SkillInstallState
        {
            Installations =
            [
                new SkillInstallRecord
                {
                    Name = "legacy-skill",
                    Layout = "legacy-direct",
                    Scope = "user",
                    InstallPath = Path.Combine(_tempRoot, "legacy-skill")
                }
            ]
        };

        var findings = DoctorCommand.Diagnose(state, SkillStoreContext.Resolve("project", workingDirectory: projectRoot));

        Assert.DoesNotContain(findings, finding => finding.Code == "LEGACY_LAYOUT");
    }

    [Fact]
    public async Task ProjectUninstall_DeletesOnlyItsOwnUnreferencedPackageFamily()
    {
        var ct = TestContext.Current.CancellationToken;
        var archive = CreateArchive(("SKILL.md", "# Version 1"));
        var installer = CreateInstaller(new Dictionary<string, byte[]> { ["1.0.0"] = archive });
        var firstProject = Path.Combine(_tempRoot, "first-project");
        var secondProject = Path.Combine(_tempRoot, "second-project");
        Directory.CreateDirectory(Path.Combine(firstProject, ".git"));
        Directory.CreateDirectory(Path.Combine(secondProject, ".git"));
        var first = await installer.InstallAsync(
            "my-skill", "1.0.0", "agents", null, false,
            scope: "project", workingDirectory: firstProject, ct: ct);
        var second = await installer.InstallAsync(
            "my-skill", "1.0.0", "agents", null, false,
            scope: "project", workingDirectory: secondProject, ct: ct);
        var firstPackageFamily = Path.Combine(
            SkillStoreContext.Resolve("project", workingDirectory: firstProject).PackagesRoot,
            "my-skill");

        await installer.UninstallAsync(
            "my-skill", "agents", false,
            scope: "project", workingDirectory: firstProject, ct: ct);

        Assert.False(Directory.Exists(firstPackageFamily));
        Assert.True(Directory.Exists(second.Record.StorePath));
        Assert.True(DirectoryLinkManager.IsDirectoryLink(second.Record.ActivePath));
        Assert.False(DirectoryLinkManager.Exists(first.Record.ActivePath));
    }

    [Fact]
    public void OperationLock_BlocksConcurrentOperationsAndCanBeReacquired()
    {
        var lockPath = Path.Combine(_tempRoot, ".skillstore", "operation.lock");
        using (SkillStoreOperationLock.Acquire(lockPath))
        {
            var error = Assert.Throws<IOException>(() =>
            {
                using var competing = SkillStoreOperationLock.Acquire(lockPath);
            });
            Assert.Contains("Another skillstore operation is active", error.Message);
        }

        using var reacquired = SkillStoreOperationLock.Acquire(lockPath);
        Assert.True(File.Exists(lockPath));
    }

    private string RegistryPath => Path.Combine(_tempRoot, "state", "installed.json");
    private string ActiveSkillPath => Path.Combine(_tempRoot, "skills", "my-skill");

    private SkillInstaller CreateInstaller(
        IReadOnlyDictionary<string, byte[]> archives,
        string status = "stable",
        string? digestOverride = null,
        string? sourceUrlOverride = null,
        IReadOnlyDictionary<string, InstallPermissions>? permissionsByVersion = null,
        Action? onInstallReference = null)
    {
        var handler = new InstallerHttpHandler(
            archives,
            status,
            digestOverride,
            sourceUrlOverride,
            permissionsByVersion,
            onInstallReference);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://skillstore.test/") };
        var client = new AgentSkillStoreClient(httpClient);
        return new SkillInstaller(client, new InstallationRegistry(RegistryPath), "https://skillstore.test");
    }

    private static byte[] CreateArchive(params (string Path, string Content)[] files)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files)
            {
                var entry = archive.CreateEntry(file.Path, CompressionLevel.Fastest);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write(file.Content);
            }
        }

        return output.ToArray();
    }

    private static string Digest(byte[] bytes) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public void Dispose()
    {
        DeleteTreeWithoutFollowingLinks(_tempRoot);
    }

    private static void DeleteTreeWithoutFollowingLinks(string path)
    {
        if (!Directory.Exists(path))
            return;
        foreach (var item in new DirectoryInfo(path).EnumerateFileSystemInfos())
        {
            if ((item.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                if (item is DirectoryInfo)
                    Directory.Delete(item.FullName);
                else
                    File.Delete(item.FullName);
            }
            else if (item is DirectoryInfo directory)
            {
                DeleteTreeWithoutFollowingLinks(directory.FullName);
            }
            else
            {
                File.Delete(item.FullName);
            }
        }
        Directory.Delete(path);
    }

    private sealed class InstallerHttpHandler : HttpMessageHandler
    {
        private readonly IReadOnlyDictionary<string, byte[]> _archives;
        private readonly string _status;
        private readonly string? _digestOverride;
        private readonly string? _sourceUrlOverride;
        private readonly IReadOnlyDictionary<string, InstallPermissions> _permissionsByVersion;
        private readonly Action? _onInstallReference;

        public InstallerHttpHandler(
            IReadOnlyDictionary<string, byte[]> archives,
            string status,
            string? digestOverride,
            string? sourceUrlOverride,
            IReadOnlyDictionary<string, InstallPermissions>? permissionsByVersion,
            Action? onInstallReference)
        {
            _archives = archives;
            _status = status;
            _digestOverride = digestOverride;
            _sourceUrlOverride = sourceUrlOverride;
            _permissionsByVersion = permissionsByVersion ?? new Dictionary<string, InstallPermissions>();
            _onInstallReference = onInstallReference;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/install-reference", StringComparison.Ordinal))
            {
                _onInstallReference?.Invoke();
                var query = request.RequestUri.Query.TrimStart('?')
                    .Split('&', StringSplitOptions.RemoveEmptyEntries)
                    .Select(item => item.Split('=', 2))
                    .First(item => item[0] == "version");
                var version = Uri.UnescapeDataString(query[1]);
                var archive = _archives[version];
                _permissionsByVersion.TryGetValue(version, out var permissions);
                return Task.FromResult(JsonResponse(new InstallReferenceResponse
                {
                    Registry = "skillstore",
                    Skill = "connected/my-skill",
                    Version = version,
                    Digest = _digestOverride ?? Digest(archive),
                    SourceUrl = _sourceUrlOverride ?? $"https://skillstore.test/api/v1/skills/my-skill/{version}/archive.zip",
                    Status = _status,
                    RiskLevel = "medium",
                    RequestedPermissions = permissions ?? new InstallPermissions()
                }));
            }
            if (path.EndsWith("/archive.zip", StringComparison.Ordinal))
            {
                var version = path.Split('/')[5];
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(_archives[version])
                });
            }

            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 5)
            {
                var version = segments[4];
                return Task.FromResult(JsonResponse(CreateSummary(version)));
            }

            if (segments.Length == 4)
            {
                var summaries = _archives.Keys
                    .OrderByDescending(version => version, StringComparer.Ordinal)
                    .Select(CreateSummary)
                    .ToArray();
                return Task.FromResult(JsonResponse(summaries));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private SkillVersionSummary CreateSummary(string version)
        {
            var archive = _archives[version];
            return new SkillVersionSummary
            {
                Name = "my-skill",
                Version = version,
                Description = "Test skill",
                Status = _status,
                Installable = _status is not ("draft" or "pending" or "revoked"),
                ArtifactType = "archive",
                ArtifactSha256 = _digestOverride ?? Digest(archive),
                ArtifactSizeBytes = archive.LongLength,
                ArchiveUrl = $"/api/v1/skills/my-skill/{version}/archive.zip",
                FileCount = 1,
                PublishedAt = DateTimeOffset.UtcNow
            };
        }

        private static HttpResponseMessage JsonResponse<T>(T value) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
        };
    }
}
