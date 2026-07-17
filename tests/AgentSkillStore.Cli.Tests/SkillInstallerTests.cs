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
        Assert.Equal("# Version 1", await File.ReadAllTextAsync(Path.Combine(_tempRoot, "my-skill", "SKILL.md"), ct));
        Assert.Equal(Digest(archive), result.Record.ArchiveHash);
        Assert.Equal(1, installReferenceRequests);
        var stagingRoot = Path.Combine(_tempRoot, ".agent-skill-store-staging");
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
        Assert.False(Directory.Exists(Path.Combine(_tempRoot, "my-skill")));
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
        Assert.False(Directory.Exists(Path.Combine(_tempRoot, "my-skill")));
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

        Assert.Equal("# Version 1", await File.ReadAllTextAsync(Path.Combine(_tempRoot, "my-skill", "SKILL.md"), ct));
        Assert.Equal("1.0.0", new InstallationRegistry(RegistryPath).Find("my-skill", "codex")?.Version);
    }

    [Fact]
    public async Task Uninstall_ProtectsModifiedFilesUnlessForced()
    {
        var ct = TestContext.Current.CancellationToken;
        var archive = CreateArchive(("SKILL.md", "# Version 1"));
        var installer = CreateInstaller(new Dictionary<string, byte[]> { ["1.0.0"] = archive });
        await installer.InstallAsync("my-skill", "1.0.0", "codex", _tempRoot, false, ct: ct);
        var skillPath = Path.Combine(_tempRoot, "my-skill", "SKILL.md");
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
        Assert.Equal("# Version 1", await File.ReadAllTextAsync(Path.Combine(_tempRoot, "my-skill", "SKILL.md"), ct));
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
        Assert.False(Directory.Exists(Path.Combine(_tempRoot, "my-skill")));
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
        Assert.Equal("# Version 1", await File.ReadAllTextAsync(Path.Combine(_tempRoot, "my-skill", "SKILL.md"), ct));

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

    private string RegistryPath => Path.Combine(_tempRoot, "state", "installed.json");

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
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, true);
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
