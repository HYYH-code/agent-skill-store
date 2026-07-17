// -----------------------------------------------------------------------
// <copyright file="Models.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------
using System.Text.Json.Serialization;

namespace AgentSkillStore.Client;

/// <summary>
/// RFC-compliant skill index.
/// </summary>
public sealed record RfcSkillIndex
{
    [JsonPropertyName("$schema")]
    public string Schema { get; init; } = "";

    [JsonPropertyName("skills")]
    public IReadOnlyList<RfcSkillEntry> Skills { get; init; } = [];
}

/// <summary>
/// RFC skill entry.
/// </summary>
public sealed record RfcSkillEntry
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("type")]
    public string Type { get; init; } = "";

    [JsonPropertyName("description")]
    public string Description { get; init; } = "";

    [JsonPropertyName("url")]
    public string Url { get; init; } = "";

    [JsonPropertyName("digest")]
    public string Digest { get; init; } = "";

    [JsonPropertyName("version")]
    public string? Version { get; init; }

    [JsonPropertyName("resources")]
    public IReadOnlyList<RfcResourceEntry>? Resources { get; init; }
}

/// <summary>
/// RFC resource entry.
/// </summary>
public sealed record RfcResourceEntry
{
    [JsonPropertyName("path")]
    public string Path { get; init; } = "";

    [JsonPropertyName("digest")]
    public string Digest { get; init; } = "";

    [JsonPropertyName("url")]
    public string Url { get; init; } = "";

    [JsonPropertyName("unixMode")]
    public int? UnixMode { get; init; }
}

public sealed record SkillResourceUpload(string RelativePath, Stream Content, int? UnixMode = null);

public sealed record SkillResourceUploadMetadata
{
    [JsonPropertyName("path")]
    public string Path { get; init; } = "";

    [JsonPropertyName("unixMode")]
    public int? UnixMode { get; init; }
}

/// <summary>
/// Skill summary.
/// </summary>
public sealed record SkillSummary
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("description")]
    public string Description { get; init; } = "";

    [JsonPropertyName("latestVersion")]
    public string LatestVersion { get; init; } = "";

    [JsonPropertyName("category")]
    public string? Category { get; init; }

    [JsonPropertyName("versionCount")]
    public int VersionCount { get; init; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// Skill version summary.
/// </summary>
public sealed record SkillVersionSummary
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("version")]
    public string Version { get; init; } = "";

    [JsonPropertyName("description")]
    public string Description { get; init; } = "";

    [JsonPropertyName("category")]
    public string? Category { get; init; }

    [JsonPropertyName("sha256")]
    public string Sha256 { get; init; } = "";

    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = "";

    [JsonPropertyName("installable")]
    public bool Installable { get; init; }

    [JsonPropertyName("artifactType")]
    public string ArtifactType { get; init; } = "";

    [JsonPropertyName("artifactSha256")]
    public string ArtifactSha256 { get; init; } = "";

    [JsonPropertyName("artifactSizeBytes")]
    public long ArtifactSizeBytes { get; init; }

    [JsonPropertyName("archiveUrl")]
    public string? ArchiveUrl { get; init; }

    [JsonPropertyName("publishedAt")]
    public DateTimeOffset PublishedAt { get; init; }

    [JsonPropertyName("isLatest")]
    public bool IsLatest { get; init; }

    [JsonPropertyName("fileCount")]
    public int FileCount { get; init; }
}

public sealed record CheckUpdateRequest
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("version")]
    public string Version { get; init; } = "";
}

public sealed record CheckUpdateResponse
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("currentVersion")]
    public string CurrentVersion { get; init; } = "";

    [JsonPropertyName("latestVersion")]
    public string LatestVersion { get; init; } = "";

    [JsonPropertyName("latestDigest")]
    public string LatestDigest { get; init; } = "";

    [JsonPropertyName("latestPublishedAt")]
    public DateTimeOffset LatestPublishedAt { get; init; }

    [JsonPropertyName("hasUpdate")]
    public bool HasUpdate { get; init; }
}

public sealed record SkillUploadResponse
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("version")]
    public string Version { get; init; } = "";

    [JsonPropertyName("sha256")]
    public string Sha256 { get; init; } = "";

    [JsonPropertyName("url")]
    public string Url { get; init; } = "";
}

public sealed record CreateApiKeyResponse
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("label")]
    public string Label { get; init; } = "";

    [JsonPropertyName("key")]
    public string Key { get; init; } = "";

    [JsonPropertyName("agentId")]
    public string AgentId { get; init; } = "";

    [JsonPropertyName("machineHash")]
    public string MachineHash { get; init; } = "";

    [JsonPropertyName("scopes")]
    public IReadOnlyList<string> Scopes { get; init; } = [];

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset? ExpiresAt { get; init; }
}

public sealed record ApiKeySummary
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("label")]
    public string Label { get; init; } = "";

    [JsonPropertyName("agentId")]
    public string AgentId { get; init; } = "";

    [JsonPropertyName("machineHash")]
    public string MachineHash { get; init; } = "";

    [JsonPropertyName("scopes")]
    public IReadOnlyList<string> Scopes { get; init; } = [];

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset? ExpiresAt { get; init; }
}

public sealed record CreateApiKeyRequest
{
    [JsonPropertyName("label")]
    public required string Label { get; init; }

    [JsonPropertyName("agentId")]
    public string? AgentId { get; init; }

    [JsonPropertyName("machineHash")]
    public string? MachineHash { get; init; }

    [JsonPropertyName("scopes")]
    public IReadOnlyList<string>? Scopes { get; init; }

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset? ExpiresAt { get; init; }
}

public sealed record PublicationResponse
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("state")]
    public string State { get; init; } = "";
}

public sealed record PublicationSummary
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("skillName")]
    public string SkillName { get; init; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = "";

    [JsonPropertyName("namespace")]
    public string Namespace { get; init; } = "";

    [JsonPropertyName("version")]
    public string Version { get; init; } = "";

    [JsonPropertyName("submitterId")]
    public string SubmitterId { get; init; } = "";

    [JsonPropertyName("submitter")]
    public string Submitter { get; init; } = "";

    [JsonPropertyName("state")]
    public string State { get; init; } = "";

    [JsonPropertyName("riskLevel")]
    public string RiskLevel { get; init; } = "";

    [JsonPropertyName("networkPolicy")]
    public string NetworkPolicy { get; init; } = "";

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("submittedAt")]
    public DateTimeOffset? SubmittedAt { get; init; }
}

public sealed record PublicationUploadRequest(
    string PackagePath,
    string Name,
    string DisplayName,
    string Namespace,
    string Version,
    string Owner,
    string RiskLevel,
    string NetworkPolicy,
    string Changelog);

public sealed record ErrorResponse
{
    [JsonPropertyName("error")]
    public string Error { get; init; } = "";

    [JsonPropertyName("message")]
    public string Message { get; init; } = "";
}

public sealed record InstallFilesystemPermissions
{
    [JsonPropertyName("read")]
    public IReadOnlyList<string> Read { get; init; } = [];

    [JsonPropertyName("write")]
    public IReadOnlyList<string> Write { get; init; } = [];
}

public sealed record InstallNetworkPermissions
{
    [JsonPropertyName("policy")]
    public string Policy { get; init; } = "deny-all";

    [JsonPropertyName("allow")]
    public IReadOnlyList<string> Allow { get; init; } = [];
}

public sealed record InstallPermissions
{
    [JsonPropertyName("filesystem")]
    public InstallFilesystemPermissions Filesystem { get; init; } = new();

    [JsonPropertyName("network")]
    public InstallNetworkPermissions Network { get; init; } = new();

    [JsonPropertyName("commands")]
    public IReadOnlyList<string> Commands { get; init; } = [];

    [JsonPropertyName("secrets")]
    public IReadOnlyList<string> Secrets { get; init; } = [];
}

public sealed record InstallReferenceResponse
{
    [JsonPropertyName("registry")]
    public string Registry { get; init; } = "";

    [JsonPropertyName("skill")]
    public string Skill { get; init; } = "";

    [JsonPropertyName("version")]
    public string Version { get; init; } = "";

    [JsonPropertyName("digest")]
    public string Digest { get; init; } = "";

    [JsonPropertyName("sourceUrl")]
    public string SourceUrl { get; init; } = "";

    [JsonPropertyName("status")]
    public string Status { get; init; } = "";

    [JsonPropertyName("riskLevel")]
    public string RiskLevel { get; init; } = "";

    [JsonPropertyName("requestedPermissions")]
    public InstallPermissions RequestedPermissions { get; init; } = new();
}

/// <summary>
/// JSON serialization context for AOT support.
/// </summary>
[JsonSerializable(typeof(RfcSkillIndex))]
[JsonSerializable(typeof(NativeRootManifest))]
[JsonSerializable(typeof(NativeSkillCollectionIndex))]
[JsonSerializable(typeof(NativeSkillCollectionPage))]
[JsonSerializable(typeof(NativeSkillIdentityIndex))]
[JsonSerializable(typeof(NativeSkillVersionDetail))]
[JsonSerializable(typeof(NativeSubAgentCollectionIndex))]
[JsonSerializable(typeof(NativeSubAgentCollectionPage))]
[JsonSerializable(typeof(NativeSubAgentIdentityIndex))]
[JsonSerializable(typeof(NativeSubAgentVersionDetail))]
[JsonSerializable(typeof(IReadOnlyList<SkillSummary>))]
[JsonSerializable(typeof(IReadOnlyList<SkillVersionSummary>))]
[JsonSerializable(typeof(SkillVersionSummary))]
[JsonSerializable(typeof(SkillUploadResponse))]
[JsonSerializable(typeof(SubAgentUploadResponse))]
[JsonSerializable(typeof(SubAgentSummary))]
[JsonSerializable(typeof(SubAgentVersionSummary))]
[JsonSerializable(typeof(IReadOnlyList<SubAgentSummary>))]
[JsonSerializable(typeof(IReadOnlyList<SubAgentVersionSummary>))]
[JsonSerializable(typeof(CreateApiKeyResponse))]
[JsonSerializable(typeof(ApiKeySummary))]
[JsonSerializable(typeof(IReadOnlyList<ApiKeySummary>))]
[JsonSerializable(typeof(PublicationResponse))]
[JsonSerializable(typeof(PublicationSummary))]
[JsonSerializable(typeof(IReadOnlyList<PublicationSummary>))]
[JsonSerializable(typeof(IReadOnlyList<CheckUpdateRequest>))]
[JsonSerializable(typeof(IReadOnlyList<CheckUpdateResponse>))]
[JsonSerializable(typeof(IReadOnlyList<SkillResourceUploadMetadata>))]
[JsonSerializable(typeof(CreateApiKeyRequest))]
[JsonSerializable(typeof(IReadOnlyList<string>))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(InstallFilesystemPermissions))]
[JsonSerializable(typeof(InstallNetworkPermissions))]
[JsonSerializable(typeof(InstallPermissions))]
[JsonSerializable(typeof(InstallReferenceResponse))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public sealed partial class AgentSkillStoreClientJsonContext : JsonSerializerContext;
