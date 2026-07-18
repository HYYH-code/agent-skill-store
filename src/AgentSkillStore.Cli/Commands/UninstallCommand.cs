// -----------------------------------------------------------------------
// <copyright file="UninstallCommand.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;
using AgentSkillStore.Client;
using AgentSkillStore.Cli.Json;
using AgentSkillStore.Cli.Output;

namespace AgentSkillStore.Cli.Commands;

internal static class UninstallCommand
{
    public static async Task<int> ExecuteAsync(ParsedArgs args, AgentSkillStoreClient client, string server)
    {
        _ = client;
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
            var removed = await installer.UninstallAsync(
                name,
                target,
                args.Force,
                InstallCommand.ResolveScope(args),
                args.InstallRoot);
            if (args.OutputFormat == "json")
            {
                Console.WriteLine(JsonSerializer.Serialize(removed, CliJsonContext.Default.IReadOnlyListString));
                return 0;
            }

            ConsoleOutput.WriteSuccess($"Uninstalled {name}. Removed {removed.Count} managed file(s).");
            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or ArgumentException)
        {
            ConsoleOutput.WriteError($"Error: {ex.Message}");
            return 1;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage: skillstore uninstall <name> [options]");
        Console.WriteLine("  --target <all|codex|claude|pi|opencode|agents>");
        Console.WriteLine("  --scope <user|project>");
        Console.WriteLine("  --force                     Remove modified managed files");
    }
}
