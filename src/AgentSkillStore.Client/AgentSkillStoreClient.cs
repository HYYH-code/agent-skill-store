// -----------------------------------------------------------------------
// <copyright file="AgentSkillStoreClient.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace AgentSkillStore.Client;

/// <summary>
/// Client for consuming Agent Skill Store APIs.
/// </summary>
public sealed partial class AgentSkillStoreClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly string _apiBase;

    public AgentSkillStoreClient(string serverUrl, string? apiKey = null)
    {
        _httpClient = new HttpClient { BaseAddress = new Uri(serverUrl.TrimEnd('/') + "/") };
        _ownsHttpClient = true;
        _apiBase = "api/v1";
        if (!string.IsNullOrEmpty(apiKey))
            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        _jsonOptions = new JsonSerializerOptions
        {
            TypeInfoResolver = AgentSkillStoreClientJsonContext.Default
        };
    }

    public AgentSkillStoreClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _ownsHttpClient = false;
        _apiBase = "api/v1";
        _jsonOptions = new JsonSerializerOptions
        {
            TypeInfoResolver = AgentSkillStoreClientJsonContext.Default
        };
    }

    private string Api(string path) => $"{_apiBase}/{path.TrimStart('/')}";

    /// <summary>
    /// Gets the RFC-compliant skill index per Cloudflare Agent Skills Discovery RFC v0.2.0.
    /// </summary>
    public async Task<RfcSkillIndex?> GetRfcIndexAsync(CancellationToken ct = default)
    {
        return await _httpClient.GetFromJsonAsync(
            ".well-known/agent-skills/index.json",
            AgentSkillStoreClientJsonContext.Default.RfcSkillIndex,
            ct);
    }

    /// <summary>
    /// Lists all skills with optional pagination.
    /// </summary>
    public async Task<IReadOnlyList<SkillSummary>> ListSkillsAsync(
        int? skip = null,
        int? take = null,
        CancellationToken ct = default)
    {
        var query = new List<string>();
        if (skip.HasValue) query.Add($"skip={skip.Value}");
        if (take.HasValue) query.Add($"take={take.Value}");

        var url = query.Count > 0 ? $"{Api("skills")}?{string.Join("&", query)}" : Api("skills");

        var result = await _httpClient.GetFromJsonAsync(
            url,
            AgentSkillStoreClientJsonContext.Default.IReadOnlyListSkillSummary,
            ct);
        return result ?? [];
    }

    /// <summary>
    /// Gets all versions of a skill.
    /// </summary>
    public async Task<IReadOnlyList<SkillVersionSummary>> GetSkillVersionsAsync(string name, CancellationToken ct = default)
    {
        var result = await _httpClient.GetFromJsonAsync(
            $"{Api("skills")}/{Uri.EscapeDataString(name)}",
            AgentSkillStoreClientJsonContext.Default.IReadOnlyListSkillVersionSummary,
            ct);
        return result ?? [];
    }

    /// <summary>
    /// Gets a specific skill version.
    /// </summary>
    public async Task<SkillVersionSummary?> GetVersionAsync(string name, string version, CancellationToken ct = default)
    {
        return await _httpClient.GetFromJsonAsync(
            $"{Api("skills")}/{Uri.EscapeDataString(name)}/{Uri.EscapeDataString(version)}",
            AgentSkillStoreClientJsonContext.Default.SkillVersionSummary,
            ct);
    }

    /// <summary>
    /// Downloads a skill file.
    /// </summary>
    public async Task<Stream> GetSkillFileAsync(string name, string version, string path = "SKILL.md", CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync(
            $"{Api("skills")}/{Uri.EscapeDataString(name)}/{Uri.EscapeDataString(version)}/{path}",
            ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync(ct);
    }

    /// <summary>
    /// Downloads a skill file as a string.
    /// </summary>
    public async Task<string> GetSkillFileAsStringAsync(string name, string version, string path = "SKILL.md", CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync(
            $"{Api("skills")}/{Uri.EscapeDataString(name)}/{Uri.EscapeDataString(version)}/{path}",
            ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    /// <summary>
    /// Downloads a blob by digest.
    /// </summary>
    public async Task<Stream> GetBlobAsync(string digest, CancellationToken ct = default)
    {
        var normalizedDigest = digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            ? digest[7..]
            : digest;

        var response = await _httpClient.GetAsync($"{Api("blobs/sha256")}/{normalizedDigest}", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync(ct);
    }

    /// <summary>
    /// Verifies a skill file matches its expected digest.
    /// </summary>
    public async Task<bool> VerifyDigestAsync(string name, string version, string expectedDigest, CancellationToken ct = default)
    {
        var expectedHex = NormalizeSha256Digest(expectedDigest);

        await using var stream = await GetSkillFileAsync(name, version, "SKILL.md", ct);
        using var sha256 = SHA256.Create();
        var hashBytes = await sha256.ComputeHashAsync(stream, ct);
        var actualHex = Convert.ToHexString(hashBytes).ToLowerInvariant();

        return actualHex == expectedHex;
    }

    /// <summary>
    /// Searches skills using full-text search.
    /// </summary>
    public async Task<IReadOnlyList<SkillSummary>> SearchSkillsAsync(
        string query, int? skip = null, int? take = null, CancellationToken ct = default)
    {
        var queryParams = new List<string> { $"q={Uri.EscapeDataString(query)}" };
        if (skip.HasValue) queryParams.Add($"skip={skip.Value}");
        if (take.HasValue) queryParams.Add($"take={take.Value}");

        var url = $"{Api("skills")}?{string.Join("&", queryParams)}";

        var result = await _httpClient.GetFromJsonAsync(
            url,
            AgentSkillStoreClientJsonContext.Default.IReadOnlyListSkillSummary,
            ct);
        return result ?? [];
    }

    /// <summary>
    /// Gets the latest version of a skill.
    /// </summary>
    public async Task<SkillVersionSummary?> GetLatestVersionAsync(string name, CancellationToken ct = default)
    {
        return await _httpClient.GetFromJsonAsync(
            $"{Api("skills")}/{Uri.EscapeDataString(name)}/latest",
            AgentSkillStoreClientJsonContext.Default.SkillVersionSummary,
            ct);
    }

    /// <summary>
    /// Gets the governed local-install contract for a published skill version.
    /// </summary>
    public async Task<InstallReferenceResponse?> GetInstallReferenceAsync(
        string name,
        string version,
        CancellationToken ct = default)
    {
        using var response = await _httpClient.GetAsync(
            $"api/enterprise/v1/skills/{Uri.EscapeDataString(name)}/install-reference?version={Uri.EscapeDataString(version)}",
            ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(
            AgentSkillStoreClientJsonContext.Default.InstallReferenceResponse,
            ct);
    }

    /// <summary>
    /// Downloads an install artifact only when the server-provided URL is same-origin.
    /// </summary>
    public async Task<Stream> DownloadInstallArtifactAsync(string sourceUrl, CancellationToken ct = default)
    {
        if (_httpClient.BaseAddress is null)
            throw new InvalidOperationException("The client requires a base address for governed artifact downloads.");
        if (!Uri.TryCreate(_httpClient.BaseAddress, sourceUrl, out var artifactUri)
            || artifactUri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("The install reference contains an invalid artifact URL.");
        if (!SameOrigin(_httpClient.BaseAddress, artifactUri))
            throw new InvalidOperationException("The install reference artifact URL must use the configured AgentSkillStore origin.");

        var response = await _httpClient.GetAsync(artifactUri, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync(ct);
    }

    private static bool SameOrigin(Uri left, Uri right) =>
        left.Scheme.Equals(right.Scheme, StringComparison.OrdinalIgnoreCase)
        && left.Host.Equals(right.Host, StringComparison.OrdinalIgnoreCase)
        && left.Port == right.Port;

    public async Task<IReadOnlyList<CheckUpdateResponse>> CheckUpdatesAsync(
        IReadOnlyList<CheckUpdateRequest> items, CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsJsonAsync(Api("skills/check-updates"), items,
            AgentSkillStoreClientJsonContext.Default.IReadOnlyListCheckUpdateRequest, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync(
            AgentSkillStoreClientJsonContext.Default.IReadOnlyListCheckUpdateResponse, ct);
        return result ?? [];
    }

    public async Task<SkillUploadResponse> UploadSkillAsync(
        string name, string version, Stream skillMdContent, string? category = null,
        CancellationToken ct = default)
    {
        return await UploadSkillWithResourceUploadsAsync(name, version, skillMdContent, Array.Empty<SkillResourceUpload>(), category, ct);
    }

    public async Task<SkillUploadResponse> UploadSkillWithResourcesAsync(
        string name, string version, Stream skillMdContent,
        IReadOnlyList<(string RelativePath, Stream Content)> resources,
        string? category = null, CancellationToken ct = default)
    {
        return await UploadSkillWithResourceUploadsAsync(
            name,
            version,
            skillMdContent,
            resources.Select(r => new SkillResourceUpload(r.RelativePath, r.Content)).ToList(),
            category,
            ct);
    }

    public async Task<SkillUploadResponse> UploadSkillWithResourceUploadsAsync(
        string name, string version, Stream skillMdContent,
        IReadOnlyList<SkillResourceUpload> resources,
        string? category = null, CancellationToken ct = default)
    {
        using var response = await PostSkillAsync(name, version, skillMdContent, resources, category, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync(
            AgentSkillStoreClientJsonContext.Default.SkillUploadResponse, ct))!;
    }

    public async Task<SkillUploadResponse?> UploadSkillIfNotExistsAsync(
        string name, string version, Stream skillMdContent,
        IReadOnlyList<(string RelativePath, Stream Content)> resources,
        string? category = null, CancellationToken ct = default)
    {
        return await UploadSkillIfNotExistsWithResourceUploadsAsync(
            name,
            version,
            skillMdContent,
            resources.Select(r => new SkillResourceUpload(r.RelativePath, r.Content)).ToList(),
            category,
            ct);
    }

    public async Task<SkillUploadResponse?> UploadSkillIfNotExistsWithResourceUploadsAsync(
        string name, string version, Stream skillMdContent,
        IReadOnlyList<SkillResourceUpload> resources,
        string? category = null, CancellationToken ct = default)
    {
        using var response = await PostSkillAsync(name, version, skillMdContent, resources, category, ct);

        if (response.StatusCode == HttpStatusCode.Conflict)
            return null;

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync(
            AgentSkillStoreClientJsonContext.Default.SkillUploadResponse, ct))!;
    }

    private async Task<HttpResponseMessage> PostSkillAsync(
        string name, string version, Stream skillMdContent,
        IReadOnlyList<SkillResourceUpload> resources,
        string? category, CancellationToken ct)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(name), "name");
        content.Add(new StringContent(version), "version");
        if (category is not null)
            content.Add(new StringContent(category), "category");
        content.Add(new StreamContent(skillMdContent), "file", "SKILL.md");

        foreach (var resource in resources)
            content.Add(new StreamContent(resource.Content), "resources", resource.RelativePath);

        if (resources.Any(resource => resource.UnixMode.HasValue))
        {
            var metadata = resources
                .Where(resource => resource.UnixMode.HasValue)
                .Select(resource => new SkillResourceUploadMetadata
                {
                    Path = resource.RelativePath,
                    UnixMode = resource.UnixMode
                })
                .ToList();
            var json = JsonSerializer.Serialize(
                metadata,
                AgentSkillStoreClientJsonContext.Default.IReadOnlyListSkillResourceUploadMetadata);
            content.Add(new StringContent(json), "resourceMetadata");
        }

        return await _httpClient.PostAsync(Api("skills"), content, ct);
    }

    public async Task DeleteVersionAsync(string name, string version, CancellationToken ct = default)
    {
        var response = await _httpClient.DeleteAsync(
            $"{Api("skills")}/{Uri.EscapeDataString(name)}/{Uri.EscapeDataString(version)}", ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<CreateApiKeyResponse> CreateApiKeyAsync(
        string label,
        DateTimeOffset? expiresAt = null,
        string? agentId = null,
        string? machineHash = null,
        IReadOnlyList<string>? scopes = null,
        CancellationToken ct = default)
    {
        var request = new CreateApiKeyRequest
        {
            Label = label,
            AgentId = agentId,
            MachineHash = machineHash,
            Scopes = scopes,
            ExpiresAt = expiresAt
        };
        var response = await _httpClient.PostAsJsonAsync(Api("api-keys"),
            request, AgentSkillStoreClientJsonContext.Default.CreateApiKeyRequest, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync(
            AgentSkillStoreClientJsonContext.Default.CreateApiKeyResponse, ct))!;
    }

    public async Task<PublicationResponse> SubmitPublicationAsync(
        PublicationUploadRequest request,
        CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(request.PackagePath);
        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(stream), "package", Path.GetFileName(request.PackagePath));
        content.Add(new StringContent(request.Name), "name");
        content.Add(new StringContent(request.DisplayName), "displayName");
        content.Add(new StringContent(request.Namespace), "namespace");
        content.Add(new StringContent(request.Version), "version");
        content.Add(new StringContent(request.Owner), "owner");
        content.Add(new StringContent(request.RiskLevel), "riskLevel");
        content.Add(new StringContent(request.NetworkPolicy), "networkPolicy");
        content.Add(new StringContent(request.Changelog), "changelog");

        var response = await _httpClient.PostAsync("api/enterprise/v1/publications", content, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync(
            AgentSkillStoreClientJsonContext.Default.PublicationResponse, ct))!;
    }

    public async Task<IReadOnlyList<PublicationSummary>> ListMyPublicationsAsync(CancellationToken ct = default)
    {
        var result = await _httpClient.GetFromJsonAsync(
            "api/enterprise/v1/publications/mine",
            AgentSkillStoreClientJsonContext.Default.IReadOnlyListPublicationSummary,
            ct);
        return result ?? [];
    }

    public async Task<IReadOnlyList<ApiKeySummary>> ListApiKeysAsync(CancellationToken ct = default)
    {
        var result = await _httpClient.GetFromJsonAsync(Api("api-keys"),
            AgentSkillStoreClientJsonContext.Default.IReadOnlyListApiKeySummary, ct);
        return result ?? [];
    }

    public async Task DeleteApiKeyAsync(long id, CancellationToken ct = default)
    {
        var response = await _httpClient.DeleteAsync($"{Api("api-keys")}/{id}", ct);
        response.EnsureSuccessStatusCode();
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }
}
