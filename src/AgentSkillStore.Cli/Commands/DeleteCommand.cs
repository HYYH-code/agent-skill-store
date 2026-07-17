// -----------------------------------------------------------------------
// <copyright file="DeleteCommand.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using AgentSkillStore.Client;
using AgentSkillStore.Cli.Output;

namespace AgentSkillStore.Cli.Commands;

internal static class DeleteCommand
{
    public static async Task<int> ExecuteAsync(ParsedArgs args, AgentSkillStoreClient client)
    {
        if (args.Help || args.Positional.Count < 2)
        {
            PrintHelp();
            return args.Help ? 0 : 1;
        }

        var name = args.Positional[0];
        var version = args.Positional[1];

        if (!args.Yes)
        {
            Console.Write($"Delete {name}@{version}? [y/N]: ");
            var response = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (response is not "y" and not "yes")
            {
                ConsoleOutput.WriteInfo("Cancelled.");
                return 0;
            }
        }

        try
        {
            await client.DeleteVersionAsync(name, version);
            ConsoleOutput.WriteSuccess($"Deleted {name}@{version}");
            return 0;
        }
        catch (HttpRequestException ex)
        {
            return ConsoleOutput.HandleHttpError(ex);
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage: skillstore delete <name> <version> [options]");
        Console.WriteLine();
        Console.WriteLine("Legacy physical deletion for controlled upstream migrations.");
        Console.WriteLine("The server rejects this command unless direct-publish compatibility mode is enabled.");
        Console.WriteLine();
        Console.WriteLine("Arguments:");
        Console.WriteLine("  <name>                Skill name");
        Console.WriteLine("  <version>             Version to delete");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --yes, -y             Skip confirmation prompt");
    }
}
