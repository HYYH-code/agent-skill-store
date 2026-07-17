// -----------------------------------------------------------------------
// <copyright file="SeedDataConfiguration.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

namespace AgentSkillStore.Server;

public sealed class SeedDataConfiguration
{
    public bool SeedData { get; set; } = false;
    public string? SeedPath { get; set; }
}