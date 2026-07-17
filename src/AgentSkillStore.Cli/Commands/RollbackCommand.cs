// -----------------------------------------------------------------------
// <copyright file="RollbackCommand.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using AgentSkillStore.Client;
using AgentSkillStore.Cli.Output;

namespace AgentSkillStore.Cli.Commands;

internal static class RollbackCommand
{
    public static async Task<int> ExecuteAsync(ParsedArgs args, AgentSkillStoreClient client, string server)
    {
        if (args.Help || args.Positional.Count == 0)
        {
            PrintHelp();
            return args.Help ? 0 : 1;
        }

        try
        {
            var name = args.Positional[0];
            var target = InstallCommand.ResolveTarget(args);
            var installer = new SkillInstaller(client, new InstallationRegistry(), server);
            var version = args.VersionOverride ?? (args.Positional.Count > 1 ? args.Positional[1] : null);
            var result = await installer.RollbackAsync(
                name,
                target,
                version,
                args.Force,
                args.AllowNonStable,
                args.Yes);
            InstallCommand.WriteResult(result, args.OutputFormat);
            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or InvalidDataException or ArgumentException or HttpRequestException)
        {
            ConsoleOutput.WriteError($"Error: {ex.Message}");
            return 1;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage: skillstore rollback <name> [version] [options]");
        Console.WriteLine("  --version <version>         Roll back to a specific version");
        Console.WriteLine("  --target <codex|claude|pi>");
        Console.WriteLine("  --allow-non-stable");
        Console.WriteLine("  --yes, -y                   Accept a declared permission expansion");
        Console.WriteLine("  --force");
    }
}
