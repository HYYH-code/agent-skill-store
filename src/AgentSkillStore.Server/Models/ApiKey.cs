// -----------------------------------------------------------------------
// <copyright file="ApiKey.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

namespace AgentSkillStore.Server.Models;

public sealed record ApiKey
{
    public required long Id { get; init; }
    public required string Label { get; init; }
    public required string KeyHash { get; init; }
    public required string AgentId { get; init; }
    public required string MachineHash { get; init; }
    public required string Scopes { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}
