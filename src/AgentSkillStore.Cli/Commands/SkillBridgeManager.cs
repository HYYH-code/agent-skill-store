// -----------------------------------------------------------------------
// <copyright file="SkillBridgeManager.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

namespace AgentSkillStore.Cli.Commands;

internal static class SkillBridgeManager
{
    private static readonly string[] SupportedTargets = ["all", "agents", "codex", "claude", "pi", "opencode"];

    public static void ValidateTarget(string target)
    {
        if (!SupportedTargets.Contains(target, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Target must be one of: all, agents, codex, claude, pi, opencode.", nameof(target));
    }

    public static bool ShouldCreateClaudeBridge(string target, bool? claudeDetected = null)
    {
        ValidateTarget(target);
        if (string.Equals(target, "claude", StringComparison.OrdinalIgnoreCase))
            return true;
        return string.Equals(target, "all", StringComparison.OrdinalIgnoreCase) &&
               (claudeDetected ?? IsClaudeDetected());
    }

    public static bool IsNativeAgentsTarget(string target) =>
        target is "agents" or "codex" or "pi" or "opencode";

    public static SkillBridgeRecord CreateClaudeBridge(
        SkillStoreContext context,
        string packageName,
        string activePath)
    {
        var bridgePath = context.GetClaudeBridgePath(packageName);
        if (DirectoryLinkManager.Exists(bridgePath))
        {
            var existingTarget = DirectoryLinkManager.ResolveTarget(bridgePath);
            if (!DirectoryLinkManager.PathsEqual(existingTarget, activePath))
                throw new IOException($"Claude bridge already exists with a different target: {bridgePath}");
        }
        else
        {
            DirectoryLinkManager.Create(bridgePath, activePath);
        }

        return new SkillBridgeRecord
        {
            Target = "claude",
            Path = bridgePath,
            LinkKind = DirectoryLinkManager.LinkKind,
            TargetPath = activePath
        };
    }

    public static void RemoveBridge(SkillBridgeRecord bridge)
    {
        DirectoryLinkManager.Remove(bridge.Path, bridge.TargetPath);
    }

    public static bool IsClaudeDetected()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (Directory.Exists(Path.Combine(home, ".claude")))
            return true;

        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? "";
        var candidates = OperatingSystem.IsWindows()
            ? new[] { "claude.exe", "claude.cmd", "claude.bat", "claude.ps1" }
            : new[] { "claude" };
        return pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(path => candidates.Any(candidate => File.Exists(Path.Combine(path.Trim(), candidate))));
    }
}
