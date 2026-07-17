// -----------------------------------------------------------------------
// <copyright file="MachineIdentityProviderTests.cs" company="Agent Skill Store contributors">
//      Copyright (C) 2026 Agent Skill Store contributors
// </copyright>
// -----------------------------------------------------------------------

using AgentSkillStore.Cli.Config;
using Xunit;

namespace AgentSkillStore.Cli.Tests;

public sealed class MachineIdentityProviderTests
{
    [Fact]
    public void Create_UsesAgentSkillStorePrefixForDerivedAgentId()
    {
        var machineHash = new string('a', 64);

        var identity = MachineIdentityProvider.Create(machineHash: machineHash);

        Assert.Equal("skillstore-agent-aaaaaaaaaaaaaaaa", identity.AgentId);
        Assert.Equal(machineHash, identity.MachineHash);
    }
}
