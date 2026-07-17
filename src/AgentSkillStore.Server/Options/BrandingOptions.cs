// -----------------------------------------------------------------------
// <copyright file="BrandingOptions.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

namespace AgentSkillStore.Server.Options;

public sealed class BrandingOptions
{
    public const string SectionName = "Branding";

    public string BrandName { get; init; } = "Agent Skill Store";

    public string ProductName { get; init; } = "Agent Skill Store";

    public string ModuleName { get; init; } = "Skill Catalog";

    public string DepartmentName { get; init; } = "Open-source contributors";

    public string Slogan { get; init; } = "Discover. Govern. Install.";

    public string LogoUrl { get; init; } = "/assets/agent-skill-store-lockup.svg";

    public string MarkUrl { get; init; } = "/assets/agent-skill-store-mark.svg";
}
