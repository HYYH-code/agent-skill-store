// -----------------------------------------------------------------------
// <copyright file="ListCommand.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;
using AgentSkillStore.Client;
using AgentSkillStore.Cli.Json;
using AgentSkillStore.Cli.Output;

namespace AgentSkillStore.Cli.Commands;

internal static class ListCommand
{
    public static async Task<int> ExecuteAsync(ParsedArgs args, AgentSkillStoreClient client)
    {
        if (args.Help)
        {
            PrintHelp();
            return 0;
        }

        if (args.Positional.Count > 0 &&
            string.Equals(args.Positional[0], "installed", StringComparison.OrdinalIgnoreCase))
        {
            return ListInstalled(args);
        }

        IReadOnlyList<SkillSummary> skills;

        if (!string.IsNullOrWhiteSpace(args.Search))
            skills = await client.SearchSkillsAsync(args.Search, args.Skip, args.Take);
        else
            skills = await client.ListSkillsAsync(args.Skip, args.Take);

        if (args.OutputFormat == "json")
        {
            var json = JsonSerializer.Serialize(skills, CliJsonContext.Default.IReadOnlyListSkillSummary);
            Console.WriteLine(json);
            return 0;
        }

        if (skills.Count == 0)
        {
            ConsoleOutput.WriteInfo("No skills found.");
            return 0;
        }

        var headers = new[] { "NAME", "LATEST", "VERSIONS", "UPDATED" };
        var rows = skills.Select(s => new[]
        {
            s.Name,
            s.LatestVersion,
            s.VersionCount.ToString(),
            s.UpdatedAt.ToString("yyyy-MM-dd")
        }).ToList();

        ConsoleOutput.WriteTable(headers, rows);
        return 0;
    }

    private static int ListInstalled(ParsedArgs args)
    {
        var registry = new InstallationRegistry();
        var state = registry.Load();
        var installations = state.Installations.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(args.Scope))
        {
            installations = installations.Where(installation =>
                string.Equals(installation.Scope, args.Scope, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(args.Target))
        {
            installations = installations.Where(i =>
                string.Equals(i.Target, args.Target, StringComparison.OrdinalIgnoreCase));
        }

        var records = installations.ToList();
        if (args.OutputFormat == "json")
        {
            var json = JsonSerializer.Serialize(records, CliJsonContext.Default.IReadOnlyListSkillInstallRecord);
            Console.WriteLine(json);
            return 0;
        }

        if (records.Count == 0)
        {
            ConsoleOutput.WriteInfo("No installed skills found.");
            return 0;
        }

        var headers = new[] { "NAME", "VERSION", "SCOPE", "LAYOUT", "PATH" };
        var rows = records.Select(r => new[]
        {
            r.Name,
            r.Version,
            r.Scope,
            r.Layout,
            string.IsNullOrWhiteSpace(r.ActivePath) ? r.InstallPath : r.ActivePath
        }).ToList();

        ConsoleOutput.WriteTable(headers, rows);
        return 0;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage: skillstore list [installed] [options]");
        Console.WriteLine();
        Console.WriteLine("List skills on the server.");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --search <query>      Search skills by name or description");
        Console.WriteLine("  --skip <n>            Skip first n results");
        Console.WriteLine("  --take <n>            Limit to n results");
        Console.WriteLine("  --output <text|json>  Output format (default: text)");
    }
}
