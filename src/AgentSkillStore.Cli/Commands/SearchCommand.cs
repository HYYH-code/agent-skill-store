// -----------------------------------------------------------------------
// <copyright file="SearchCommand.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using AgentSkillStore.Client;

namespace AgentSkillStore.Cli.Commands;

internal static class SearchCommand
{
    public static Task<int> ExecuteAsync(ParsedArgs args, AgentSkillStoreClient client)
    {
        if (args.Help || args.Positional.Count == 0)
        {
            PrintHelp();
            return Task.FromResult(args.Help ? 0 : 1);
        }

        var searchArgs = new ParsedArgs
        {
            Command = "list",
            Search = args.Positional[0],
            Skip = args.Skip,
            Take = args.Take,
            OutputFormat = args.OutputFormat
        };

        return ListCommand.ExecuteAsync(searchArgs, client);
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage: skillstore search <query> [options]");
        Console.WriteLine();
        Console.WriteLine("Search skills on the server.");
    }
}
