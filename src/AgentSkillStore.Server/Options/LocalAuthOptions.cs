// -----------------------------------------------------------------------
// <copyright file="LocalAuthOptions.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

namespace AgentSkillStore.Server.Options;

public sealed class LocalAuthOptions
{
    public const string SectionName = "AgentSkillStore:Auth";

    public string Mode { get; init; } = "local";

    public List<LocalUserOptions> Users { get; init; } = [];
}

public sealed class LocalUserOptions
{
    public string Username { get; init; } = "";

    public string Password { get; init; } = "";

    public string DisplayName { get; init; } = "";

    public string Team { get; init; } = "";

    public List<string> Roles { get; init; } = [];
}
