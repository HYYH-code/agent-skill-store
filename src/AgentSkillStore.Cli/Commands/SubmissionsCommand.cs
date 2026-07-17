// -----------------------------------------------------------------------
// <copyright file="SubmissionsCommand.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;
using AgentSkillStore.Client;
using AgentSkillStore.Cli.Json;
using AgentSkillStore.Cli.Output;

namespace AgentSkillStore.Cli.Commands;

internal static class SubmissionsCommand
{
    public static async Task<int> ExecuteAsync(ParsedArgs args, AgentSkillStoreClient client)
    {
        if (args.Help)
        {
            PrintHelp();
            return 0;
        }

        try
        {
            var submissions = await client.ListMyPublicationsAsync();
            if (args.OutputFormat == "json")
            {
                Console.WriteLine(JsonSerializer.Serialize(
                    submissions,
                    CliJsonContext.Default.IReadOnlyListPublicationSummary));
                return 0;
            }

            if (submissions.Count == 0)
            {
                ConsoleOutput.WriteInfo("No submissions found for this Agent Key.");
                return 0;
            }

            ConsoleOutput.WriteTable(
                ["ID", "SKILL", "VERSION", "STATE", "RISK", "SUBMITTED"],
                submissions.Select(item => new[]
                {
                    item.Id,
                    $"{item.Namespace}/{item.SkillName}",
                    item.Version,
                    item.State,
                    item.RiskLevel,
                    (item.SubmittedAt ?? item.CreatedAt).ToString("yyyy-MM-dd HH:mm")
                }).ToList());
            return 0;
        }
        catch (HttpRequestException ex)
        {
            return ConsoleOutput.HandleHttpError(ex);
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage: skillstore submissions [--output json]");
        Console.WriteLine();
        Console.WriteLine("List publications submitted by the current Agent Key.");
    }
}
