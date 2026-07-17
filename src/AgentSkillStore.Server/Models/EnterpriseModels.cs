// -----------------------------------------------------------------------
// <copyright file="EnterpriseModels.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

namespace AgentSkillStore.Server.Models;

public sealed record EnterpriseSessionUser
{
    public required string Id { get; init; }
    public required string Username { get; init; }
    public required string DisplayName { get; init; }
    public required string Team { get; init; }
    public required IReadOnlyList<string> Roles { get; init; }
    public IReadOnlyList<string> Scopes { get; init; } = [];
}

public sealed record LocalLoginRequest
{
    public required string Username { get; init; }
    public required string Password { get; init; }
}

public sealed record EnterpriseOwner
{
    public required string Id { get; init; }
    public required string Name { get; init; }
}

public sealed record EnterpriseCategory
{
    public required string Id { get; init; }
    public required string Name { get; init; }
}

public sealed record EnterpriseFilesystemPermissions
{
    public IReadOnlyList<string> Read { get; init; } = ["work/**"];
    public IReadOnlyList<string> Write { get; init; } = ["work/output/**"];
}

public sealed record EnterpriseNetworkPermissions
{
    public string Policy { get; init; } = "deny-all";
    public IReadOnlyList<string> Allow { get; init; } = [];
}

public sealed record EnterprisePermissions
{
    public EnterpriseFilesystemPermissions Filesystem { get; init; } = new();
    public EnterpriseNetworkPermissions Network { get; init; } = new();
    public IReadOnlyList<string> Commands { get; init; } = [];
    public IReadOnlyList<string> Secrets { get; init; } = [];
}

public sealed record EnterpriseSkillVersionSummary
{
    public required string Version { get; init; }
    public required string Status { get; init; }
    public required string RiskLevel { get; init; }
    public required string Digest { get; init; }
    public required DateTimeOffset PublishedAt { get; init; }
    public required string Changelog { get; init; }
    public required long SizeBytes { get; init; }
    public required int FileCount { get; init; }
}

public sealed record EnterpriseSkillSummary
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Namespace { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public required EnterpriseOwner Owner { get; init; }
    public required EnterpriseCategory Category { get; init; }
    public required string LatestVersion { get; init; }
    public required string RecommendedVersion { get; init; }
    public required string Status { get; init; }
    public required string RiskLevel { get; init; }
    public IReadOnlyList<string> Runtime { get; init; } = ["Pi"];
    public EnterprisePermissions Permissions { get; init; } = new();
    public required string Digest { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public required bool Featured { get; init; }
    public IReadOnlyList<EnterpriseSkillVersionSummary> Versions { get; init; } = [];
    public string SkillMarkdown { get; init; } = "";
    public IReadOnlyList<EnterpriseSkillFileSummary> Files { get; init; } = [];
    public IReadOnlyList<EnterpriseReviewHistory> ReviewHistory { get; init; } = [];
}

public sealed record EnterpriseReviewHistory
{
    public required string Id { get; init; }
    public required string Version { get; init; }
    public required string SubmitterId { get; init; }
    public required string Submitter { get; init; }
    public required DateTimeOffset SubmittedAt { get; init; }
    public required string State { get; init; }
    public string? Reviewer { get; init; }
    public string? Decision { get; init; }
    public string? Channel { get; init; }
    public string? Comment { get; init; }
    public DateTimeOffset? DecidedAt { get; init; }
}

public sealed record EnterpriseSkillFileSummary
{
    public required string Path { get; init; }
    public required long SizeBytes { get; init; }
    public required string Sha256 { get; init; }
}

public sealed record CreatePublicationRequest
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required string Namespace { get; init; }
    public required string Version { get; init; }
    public required string Owner { get; init; }
    public required string RiskLevel { get; init; }
    public required string NetworkPolicy { get; init; }
    public required string Changelog { get; init; }
}

public sealed record PublicationResponse
{
    public required string Id { get; init; }
    public required string State { get; init; }
}

public sealed record EnterprisePublicationSummary
{
    public required string Id { get; init; }
    public required string SkillName { get; init; }
    public required string DisplayName { get; init; }
    public required string Namespace { get; init; }
    public required string Version { get; init; }
    public required string SubmitterId { get; init; }
    public required string Submitter { get; init; }
    public required string State { get; init; }
    public required string RiskLevel { get; init; }
    public required string NetworkPolicy { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? SubmittedAt { get; init; }
}

public sealed record EnterpriseReviewSummary
{
    public required string Id { get; init; }
    public required string PublicationId { get; init; }
    public required string SkillName { get; init; }
    public required string DisplayName { get; init; }
    public required string Version { get; init; }
    public required string Submitter { get; init; }
    public required string RiskLevel { get; init; }
    public required DateTimeOffset SubmittedAt { get; init; }
    public required string State { get; init; }
    public EnterprisePermissions Permissions { get; init; } = new();
    public string? Comment { get; init; }
}

public sealed record ReviewDecisionRequest
{
    public string? Comment { get; init; }
    public string? Channel { get; init; }
}

public sealed record EnterpriseAuditEvent
{
    public required string Id { get; init; }
    public required string Actor { get; init; }
    public required string Role { get; init; }
    public required string Action { get; init; }
    public required string Resource { get; init; }
    public required string Result { get; init; }
    public required string RequestId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}

public sealed record InstallReference
{
    public string Registry { get; init; } = "skillstore";
    public required string Skill { get; init; }
    public required string Version { get; init; }
    public required string Digest { get; init; }
    public required string SourceUrl { get; init; }
    public required string Status { get; init; }
    public required string RiskLevel { get; init; }
    public EnterprisePermissions RequestedPermissions { get; init; } = new();
}

public sealed record EnterpriseError
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public required string RequestId { get; init; }
}

public sealed record EnterpriseSystemStatus
{
    public required string Registry { get; init; }
    public required string Storage { get; init; }
    public required int DatabaseSchema { get; init; }
    public required EnterpriseCliStatus Cli { get; init; }
}

public sealed record EnterpriseCliStatus
{
    public required string Mode { get; init; }
    public required IReadOnlyList<string> SupportedOperations { get; init; }
}

public sealed record GovernanceTeam
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Code { get; init; }
    public required string Status { get; init; }
}

public sealed record GovernanceCategory
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? ParentId { get; init; }
    public required int SortOrder { get; init; }
}
