// -----------------------------------------------------------------------
// <copyright file="CliReadonlyContractTests.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using AgentSkillStore.Client;
using AgentSkillStore.Server.Data;
using Xunit;

namespace AgentSkillStore.Integration.Tests;

[Collection("AgentSkillStore")]
public sealed class CliReadonlyContractTests
{
    private readonly AgentSkillStoreFixture _fixture;

    public CliReadonlyContractTests(AgentSkillStoreFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ApiV1SkillVersions_ExposeInstallableArchiveContract()
    {
        var ct = TestContext.Current.CancellationToken;
        var skillName = $"cli-ro-{Guid.NewGuid():N}"[..20];

        await PublishResourcefulSkillAsync(skillName, "1.0.0", "First CLI contract version", ct);
        await PublishResourcefulSkillAsync(skillName, "2.0.0", "Second CLI contract version", ct);

        var latest = await _fixture.Client.GetLatestVersionAsync(skillName, ct);
        Assert.NotNull(latest);
        Assert.Equal("2.0.0", latest!.Version);
        Assert.True(latest.IsLatest);
        Assert.Equal("beta", latest.Status);
        Assert.True(latest.Installable);
        Assert.Equal("archive", latest.ArtifactType);
        Assert.NotEqual(latest.Sha256, latest.ArtifactSha256);
        Assert.True(latest.ArtifactSizeBytes > 0);
        Assert.Equal($"/api/v1/skills/{skillName}/2.0.0/archive.zip", latest.ArchiveUrl);
        Assert.Equal(1, latest.FileCount);

        var selected = await _fixture.Client.GetVersionAsync(skillName, "1.0.0", ct);
        Assert.NotNull(selected);
        Assert.Equal("1.0.0", selected!.Version);
        Assert.False(selected.IsLatest);
        Assert.Equal("archive", selected.ArtifactType);
        Assert.NotNull(selected.ArchiveUrl);

        var versions = await _fixture.Client.GetSkillVersionsAsync(skillName, ct);
        Assert.Equal(["2.0.0", "1.0.0"], versions.Select(v => v.Version).ToArray());
        Assert.All(versions, version =>
        {
            Assert.Equal("beta", version.Status);
            Assert.True(version.Installable);
            Assert.Equal("archive", version.ArtifactType);
            Assert.True(version.ArtifactSizeBytes > 0);
            Assert.NotNull(version.ArchiveUrl);
        });

        var archiveResponse = await _fixture.HttpClient.GetAsync(latest.ArchiveUrl, ct);
        Assert.Equal(HttpStatusCode.OK, archiveResponse.StatusCode);
        Assert.Equal("application/zip", archiveResponse.Content.Headers.ContentType?.MediaType);

        var archiveBytes = await archiveResponse.Content.ReadAsByteArrayAsync(ct);
        Assert.Equal(latest.ArtifactSizeBytes, archiveBytes.LongLength);
        Assert.Equal(latest.ArtifactSha256, ComputeSha256Digest(archiveBytes));

        using var archiveStream = new MemoryStream(archiveBytes);
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);
        Assert.NotNull(archive.GetEntry("SKILL.md"));
        Assert.NotNull(archive.GetEntry("references/cli.md"));
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("pending")]
    [InlineData("revoked")]
    public async Task NonInstallableVersion_IsReportedNonInstallable_AndArchiveDownloadIsBlocked(string status)
    {
        var ct = TestContext.Current.CancellationToken;
        var skillName = $"cli-block-{Guid.NewGuid():N}"[..20];

        await PublishResourcefulSkillAsync(skillName, "1.0.0", "Blocked CLI contract version", ct);
        var version = await _fixture.Client.GetVersionAsync(skillName, "1.0.0", ct);
        Assert.NotNull(version);

        await SetEnterpriseVersionStatusAsync(skillName, "1.0.0", version!.ArtifactSha256, status, ct);

        var blocked = await _fixture.Client.GetLatestVersionAsync(skillName, ct);
        Assert.NotNull(blocked);
        Assert.Equal(status, blocked!.Status);
        Assert.False(blocked.Installable);
        Assert.Equal("archive", blocked.ArtifactType);
        Assert.NotNull(blocked.ArchiveUrl);

        var archiveResponse = await _fixture.HttpClient.GetAsync(blocked.ArchiveUrl, ct);
        Assert.Equal(HttpStatusCode.Conflict, archiveResponse.StatusCode);
    }

    [Fact]
    public async Task EnterpriseSystemStatus_ReportsCliContract()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _fixture.CreateClient();

        var loginResponse = await client.PostAsJsonAsync(
            "/api/enterprise/v1/session/login",
            new { username = "platform-admin", password = "platform-admin-password" },
            ct);
        loginResponse.EnsureSuccessStatusCode();

        using var statusResponse = await client.GetAsync("/api/enterprise/v1/system/status", ct);
        statusResponse.EnsureSuccessStatusCode();

        using var document = await JsonDocument.ParseAsync(
            await statusResponse.Content.ReadAsStreamAsync(ct),
            cancellationToken: ct);
        var root = document.RootElement;
        Assert.Equal("healthy", root.GetProperty("registry").GetString());
        Assert.False(root.TryGetProperty("mode", out _));

        var cli = root.GetProperty("cli");
        Assert.Equal("local", cli.GetProperty("mode").GetString());
        Assert.Equal(
            ["install", "upgrade", "uninstall", "rollback"],
            cli.GetProperty("supportedOperations").EnumerateArray().Select(item => item.GetString() ?? "").ToArray());
    }

    private async Task PublishResourcefulSkillAsync(
        string skillName,
        string version,
        string description,
        CancellationToken ct)
    {
        var skillMd = $"""
            ---
            name: {skillName}
            version: {version}
            category: testing
            description: {description}
            ---

            # {skillName}

            Version {version}.
            """;

        using var skillStream = new MemoryStream(Encoding.UTF8.GetBytes(skillMd));
        using var resourceStream = new MemoryStream(Encoding.UTF8.GetBytes("# CLI\nInstallable archive resource."));

        await _fixture.AuthenticatedClient.UploadSkillWithResourcesAsync(
            skillName,
            version,
            skillStream,
            [("references/cli.md", resourceStream)],
            "testing",
            ct);
    }

    private async Task SetEnterpriseVersionStatusAsync(
        string skillName,
        string version,
        string digest,
        string status,
        CancellationToken ct)
    {
        var initializer = _fixture.Services.GetRequiredService<DatabaseInitializer>();
        await using var connection = new SqliteConnection(initializer.ConnectionString);
        await connection.OpenAsync(ct);

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO enterprise_skill_version(
                skill_name, version, digest, status, risk_level, manifest_json, changelog, created_at, updated_at)
            VALUES($skillName, $version, $digest, $status, 'medium', '{}', '', $now, $now)
            ON CONFLICT(skill_name, version) DO UPDATE SET
                digest = excluded.digest,
                status = excluded.status,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$skillName", skillName);
        command.Parameters.AddWithValue("$version", version);
        command.Parameters.AddWithValue("$digest", digest);
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(ct);
    }

    private static string ComputeSha256Digest(byte[] bytes) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
