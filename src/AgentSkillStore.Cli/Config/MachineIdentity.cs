// -----------------------------------------------------------------------
// <copyright file="MachineIdentity.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;

namespace AgentSkillStore.Cli.Config;

internal sealed record MachineIdentity(string AgentId, string MachineHash);

internal static class MachineIdentityProvider
{
    public static MachineIdentity Create(string? agentId = null, string? machineHash = null)
    {
        var hash = string.IsNullOrWhiteSpace(machineHash)
            ? ComputeMachineHash()
            : machineHash.Trim().ToLowerInvariant();

        var normalizedAgentId = string.IsNullOrWhiteSpace(agentId)
            ? $"skillstore-agent-{hash[..16]}"
            : agentId.Trim().ToLowerInvariant();

        return new MachineIdentity(normalizedAgentId, hash);
    }

    private static string ComputeMachineHash()
    {
        var material = string.Join("|", [
            Environment.MachineName,
            Environment.UserName,
            Environment.OSVersion.Platform.ToString(),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        ]);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
    }
}
