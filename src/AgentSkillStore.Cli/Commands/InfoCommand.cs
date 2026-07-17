// -----------------------------------------------------------------------
// <copyright file="InfoCommand.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;
using AgentSkillStore.Client;
using AgentSkillStore.Cli.Json;
using AgentSkillStore.Cli.Output;

namespace AgentSkillStore.Cli.Commands;

internal static class InfoCommand
{
    public static async Task<int> ExecuteAsync(ParsedArgs args, AgentSkillStoreClient client)
    {
        if (args.Help || args.Positional.Count == 0)
        {
            PrintHelp();
            return args.Help ? 0 : 1;
        }

        var version = args.VersionOverride ?? (args.Positional.Count > 1 ? args.Positional[1] : null);
        var reference = SkillReference.Parse(args.Positional[0], version);
        var info = string.IsNullOrWhiteSpace(reference.Version)
            ? await client.GetLatestVersionAsync(reference.PackageName)
            : await client.GetVersionAsync(reference.PackageName, reference.Version!);

        if (info is null)
        {
            ConsoleOutput.WriteError($"Skill '{reference.Name}' was not found.");
            return 1;
        }

        if (args.OutputFormat == "json")
        {
            Console.WriteLine(JsonSerializer.Serialize(info, CliJsonContext.Default.SkillVersionSummary));
            return 0;
        }

        Console.WriteLine($"{info.Name}@{info.Version}");
        Console.WriteLine($"Description: {info.Description}");
        Console.WriteLine($"Category: {info.Category ?? "-"}");
        Console.WriteLine($"SHA-256: {info.Sha256}");
        Console.WriteLine($"Files: {info.FileCount}");
        Console.WriteLine($"Published: {info.PublishedAt:yyyy-MM-dd}");
        Console.WriteLine($"Status: {info.Status}");
        Console.WriteLine($"Installable: {(info.Installable ? "yes" : "no")}");
        if (!string.IsNullOrWhiteSpace(info.ArtifactSha256))
            Console.WriteLine($"Archive SHA-256: {info.ArtifactSha256}");
        return 0;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage: skillstore info <name> [version] [options]");
        Console.WriteLine();
        Console.WriteLine("Show skill version metadata.");
    }
}
