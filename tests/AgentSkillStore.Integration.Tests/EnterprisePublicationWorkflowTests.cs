// -----------------------------------------------------------------------
// <copyright file="EnterprisePublicationWorkflowTests.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using AgentSkillStore.Server.Models;
using Xunit;

namespace AgentSkillStore.Integration.Tests;

[Collection("AgentSkillStore")]
public sealed class EnterprisePublicationWorkflowTests
{
    private readonly AgentSkillStoreFixture _fixture;

    public EnterprisePublicationWorkflowTests(AgentSkillStoreFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Publication_RequiresIndependentReview_AndPublishesInstallReference()
    {
        var ct = TestContext.Current.CancellationToken;
        var skillName = $"review-{Guid.NewGuid():N}"[..20];
        using var submitter = _fixture.CreateClient();
        using var reviewer = _fixture.CreateClient();

        submitter.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", AgentSkillStoreFixture.TestApiKey);
        await LoginAsync(reviewer, "platform-admin", "platform-admin-password", ct);

        using var publicationContent = CreatePublicationContent(skillName);
        using var publicationResponse = await submitter.PostAsync(
            "/api/enterprise/v1/publications",
            publicationContent,
            ct);
        Assert.Equal(HttpStatusCode.Created, publicationResponse.StatusCode);
        var publication = await publicationResponse.Content.ReadFromJsonAsync<PublicationResponse>(cancellationToken: ct);
        Assert.NotNull(publication);
        Assert.Equal("pending", publication!.State);

        var myPublications = await submitter.GetFromJsonAsync<IReadOnlyList<EnterprisePublicationSummary>>(
            "/api/enterprise/v1/publications/mine",
            ct);
        Assert.Contains(myPublications!, item => item.Id == publication.Id && item.SubmitterId.StartsWith("agent:", StringComparison.Ordinal));

        var publicSkillsBeforeApproval = await _fixture.HttpClient.GetFromJsonAsync<IReadOnlyList<EnterpriseSkillSummary>>(
            "/api/enterprise/v1/skills",
            ct);
        Assert.DoesNotContain(publicSkillsBeforeApproval!, item => item.Name == skillName);

        var reviews = await reviewer.GetFromJsonAsync<IReadOnlyList<EnterpriseReviewSummary>>(
            "/api/enterprise/v1/reviews",
            ct);
        var review = Assert.Single(reviews!, item => item.PublicationId == publication.Id);
        Assert.Equal("high", review.RiskLevel);
        Assert.Equal("allow-list", review.Permissions.Network.Policy);
        Assert.Contains("api.internal.example", review.Permissions.Network.Allow);

        using var approvalResponse = await reviewer.PostAsJsonAsync(
            $"/api/enterprise/v1/reviews/{review.Id}/approve",
            new ReviewDecisionRequest { Comment = "Independent review complete.", Channel = "stable" },
            ct);
        Assert.Equal(HttpStatusCode.NoContent, approvalResponse.StatusCode);

        var latest = await _fixture.Client.GetLatestVersionAsync(skillName, ct);
        Assert.NotNull(latest);
        Assert.Equal("stable", latest!.Status);
        Assert.True(latest.Installable);

        var publicSkillsAfterApproval = await _fixture.HttpClient.GetFromJsonAsync<IReadOnlyList<EnterpriseSkillSummary>>(
            "/api/enterprise/v1/skills",
            ct);
        Assert.Contains(publicSkillsAfterApproval!, item => item.Name == skillName && item.Status == "stable");

        using var archiveResponse = await _fixture.HttpClient.GetAsync(latest.ArchiveUrl, ct);
        Assert.Equal(HttpStatusCode.OK, archiveResponse.StatusCode);

        using var installReferenceResponse = await _fixture.AuthenticatedHttpClient.GetAsync(
            $"/api/enterprise/v1/skills/{skillName}/install-reference?version=1.0.0",
            ct);
        installReferenceResponse.EnsureSuccessStatusCode();
        using var referenceDocument = await JsonDocument.ParseAsync(
            await installReferenceResponse.Content.ReadAsStreamAsync(ct),
            cancellationToken: ct);
        var reference = referenceDocument.RootElement;
        Assert.Equal("1.0.0", reference.GetProperty("version").GetString());
        Assert.Equal(latest.ArtifactSha256, reference.GetProperty("digest").GetString());
        Assert.Equal("stable", reference.GetProperty("status").GetString());
        Assert.Equal("high", reference.GetProperty("riskLevel").GetString());
        Assert.Equal("allow-list", reference.GetProperty("requestedPermissions").GetProperty("network").GetProperty("policy").GetString());

        var audit = await reviewer.GetFromJsonAsync<IReadOnlyList<EnterpriseAuditEvent>>(
            "/api/enterprise/v1/audit",
            ct);
        Assert.Contains(audit!, item => item.Action == "publication.submit" && item.Resource.Contains(skillName, StringComparison.Ordinal));
        Assert.Contains(audit!, item => item.Action == "skill.version.approve" && item.Resource.Contains(skillName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AgentKey_CanSubmitButCannotManageKeys()
    {
        var ct = TestContext.Current.CancellationToken;
        using var createResponse = await _fixture.AuthenticatedHttpClient.PostAsJsonAsync(
            "/api/v1/api-keys",
            new CreateApiKeyRequest
            {
                Label = "agent-only",
                AgentId = "skillstore-agent-only",
                MachineHash = "machine-agent-only",
                Scopes = ["skills:read", "skills:submit"]
            },
            ct);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<AgentSkillStore.Client.CreateApiKeyResponse>(cancellationToken: ct);
        Assert.NotNull(created);

        using var agentClient = _fixture.CreateClient();
        agentClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", created!.Key);

        using var listKeysResponse = await agentClient.GetAsync("/api/v1/api-keys", ct);
        Assert.Equal(HttpStatusCode.Forbidden, listKeysResponse.StatusCode);

        var skillName = $"agent-{Guid.NewGuid():N}"[..20];
        using var publicationContent = CreatePublicationContent(skillName);
        using var publicationResponse = await agentClient.PostAsync(
            "/api/enterprise/v1/publications",
            publicationContent,
            ct);
        Assert.Equal(HttpStatusCode.Created, publicationResponse.StatusCode);
    }

    [Fact]
    public async Task DevelopmentAccounts_EnforceAuthorReviewerAndEngineerBoundaries()
    {
        var ct = TestContext.Current.CancellationToken;
        using var author = _fixture.CreateClient();
        using var reviewer = _fixture.CreateClient();
        using var engineer = _fixture.CreateClient();
        await LoginAsync(author, "author", "author", ct);
        await LoginAsync(reviewer, "reviewer", "reviewer", ct);
        await LoginAsync(engineer, "engineer", "engineer", ct);

        Assert.Equal(HttpStatusCode.OK,
            (await author.GetAsync("/api/enterprise/v1/publications/mine", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await author.GetAsync("/api/enterprise/v1/reviews", ct)).StatusCode);

        Assert.Equal(HttpStatusCode.OK,
            (await reviewer.GetAsync("/api/enterprise/v1/reviews", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await reviewer.GetAsync("/api/enterprise/v1/publications/mine", ct)).StatusCode);

        Assert.Equal(HttpStatusCode.OK,
            (await engineer.GetAsync("/api/enterprise/v1/skills", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await engineer.GetAsync("/api/enterprise/v1/publications/mine", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await engineer.GetAsync("/api/enterprise/v1/reviews", ct)).StatusCode);
    }

    [Fact]
    public async Task Publication_SubmitterCannotApproveOwnVersion()
    {
        var ct = TestContext.Current.CancellationToken;
        var skillName = $"self-{Guid.NewGuid():N}"[..20];
        using var platformAdmin = _fixture.CreateClient();
        await LoginAsync(platformAdmin, "platform-admin", "platform-admin-password", ct);

        using var publicationContent = CreatePublicationContent(skillName);
        using var publicationResponse = await platformAdmin.PostAsync(
            "/api/enterprise/v1/publications",
            publicationContent,
            ct);
        publicationResponse.EnsureSuccessStatusCode();
        var publication = await publicationResponse.Content.ReadFromJsonAsync<PublicationResponse>(cancellationToken: ct);
        Assert.NotNull(publication);

        var reviews = await platformAdmin.GetFromJsonAsync<IReadOnlyList<EnterpriseReviewSummary>>(
            "/api/enterprise/v1/reviews",
            ct);
        var review = Assert.Single(reviews!, item => item.PublicationId == publication!.Id);

        using var response = await platformAdmin.PostAsJsonAsync(
            $"/api/enterprise/v1/reviews/{review.Id}/approve",
            new ReviewDecisionRequest { Comment = "Self approval must fail.", Channel = "stable" },
            ct);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DirectPublish_IsDisabledUnlessCompatibilityModeIsExplicitlyEnabled()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AgentSkillStore:Compatibility:AllowDirectPublish"] = "false"
                })));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", AgentSkillStoreFixture.TestApiKey);

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("compatibility-test"), "name");
        content.Add(new StringContent("1.0.0"), "version");
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("---\nname: compatibility-test\nversion: 1.0.0\n---\n")), "file", "SKILL.md");

        using var response = await client.PostAsync("/api/v1/skills", content, ct);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(cancellationToken: ct);
        Assert.Equal("direct_publish_disabled", error?.Error);
    }

    [Fact]
    public async Task Publication_RejectsEmbeddedAgentSkillStoreApiKey()
    {
        var ct = TestContext.Current.CancellationToken;
        var skillName = $"secret-{Guid.NewGuid():N}"[..20];
        using var submitter = _fixture.CreateClient();
        submitter.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", AgentSkillStoreFixture.TestApiKey);

        using var content = CreatePublicationContent(
            skillName,
            "config.env",
            "AGENT_SKILL_STORE_API_KEY=example-test-key");
        using var response = await submitter.PostAsync("/api/enterprise/v1/publications", content, ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<EnterpriseError>(cancellationToken: ct);
        Assert.Equal("PACKAGE_UNSAFE", error?.Code);
        Assert.Contains("Potential embedded secret detected: config.env", error?.Message);
    }

    private static async Task LoginAsync(
        HttpClient client,
        string username,
        string password,
        CancellationToken ct)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/enterprise/v1/session/login",
            new { username, password },
            ct);
        response.EnsureSuccessStatusCode();
    }

    private static MultipartFormDataContent CreatePublicationContent(
        string skillName,
        string? extraPath = null,
        string? extraContent = null)
    {
        var content = new MultipartFormDataContent();
        var package = new ByteArrayContent(CreatePackage(skillName, extraPath, extraContent));
        package.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");
        content.Add(package, "package", $"{skillName}.zip");
        content.Add(new StringContent(skillName), "name");
        content.Add(new StringContent("Review workflow skill"), "displayName");
        content.Add(new StringContent("testing"), "namespace");
        content.Add(new StringContent("1.0.0"), "version");
        content.Add(new StringContent("Platform Team"), "owner");
        content.Add(new StringContent("high"), "riskLevel");
        content.Add(new StringContent("allow-list"), "networkPolicy");
        content.Add(new StringContent("Initial governed release."), "changelog");
        return content;
    }

    private static byte[] CreatePackage(string skillName, string? extraPath = null, string? extraContent = null)
    {
        var skillMarkdown = $"""
            ---
            name: {skillName}
            version: 1.0.0
            category: testing
            description: Enterprise publication workflow test
            ---

            # {skillName}
            """;
        var manifest = $"""
            apiVersion: skills.agent-skill-store.dev/v1alpha1
            kind: AgentSkill
            metadata:
              name: {skillName}
              version: 1.0.0
              namespace: testing
              owner: Platform Team
            spec:
              riskLevel: high
              permissions:
                filesystem:
                  read:
                    - work/**
                  write:
                    - work/output/**
                network:
                  policy: allow-list
                  allow:
                    - api.internal.example
                commands:
                  - dotnet
                secrets: []
            """;

        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "SKILL.md", skillMarkdown);
            WriteEntry(archive, "manifest.yaml", manifest);
            if (extraPath is not null && extraContent is not null)
                WriteEntry(archive, extraPath, extraContent);
        }

        return output.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
