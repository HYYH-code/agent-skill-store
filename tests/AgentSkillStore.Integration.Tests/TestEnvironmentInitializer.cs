// -----------------------------------------------------------------------
// <copyright file="TestEnvironmentInitializer.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------
using System.Runtime.CompilerServices;

namespace AgentSkillStore.Integration.Tests;

internal static class TestEnvironmentInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var dataPath = Path.Combine(
            Path.GetTempPath(),
            "agent-skill-store-tests",
            Guid.NewGuid().ToString("N"));

        Environment.SetEnvironmentVariable("DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE", "false");
        Environment.SetEnvironmentVariable("AGENTSKILLSTORE__DATAPATH", dataPath);
        Environment.SetEnvironmentVariable("AGENTSKILLSTORE__BOOTSTRAPAPIKEY", "sk-test-integration-key-12345");
        Environment.SetEnvironmentVariable("AGENTSKILLSTORE__SEEDDATA", "false");
        Environment.SetEnvironmentVariable("AGENTSKILLSTORE__COMPATIBILITY__ALLOWDIRECTPUBLISH", "true");
        Environment.SetEnvironmentVariable("AGENTSKILLSTORE__AUTH__USERS__0__USERNAME", "platform-admin");
        Environment.SetEnvironmentVariable("AGENTSKILLSTORE__AUTH__USERS__0__PASSWORD", "platform-admin-password");
        Environment.SetEnvironmentVariable("AGENTSKILLSTORE__AUTH__USERS__0__DISPLAYNAME", "Platform Admin");
        Environment.SetEnvironmentVariable("AGENTSKILLSTORE__AUTH__USERS__0__TEAM", "platform");
        Environment.SetEnvironmentVariable("AGENTSKILLSTORE__AUTH__USERS__0__ROLES__0", "platform_admin");
    }
}
