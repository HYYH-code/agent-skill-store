// -----------------------------------------------------------------------
// <copyright file="PublishCommand.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using AgentSkillStore.Client;
using AgentSkillStore.Cli.Output;
using AgentSkillStore.Cli.Publishing;

namespace AgentSkillStore.Cli.Commands;

internal static class PublishCommand
{
    public static async Task<int> ExecuteAsync(ParsedArgs args, AgentSkillStoreClient client)
    {
        if (args.Help || args.Positional.Count == 0)
        {
            PrintHelp();
            return args.Help ? 0 : 1;
        }

        var path = args.Positional[0];
        var skill = SkillDirectoryScanner.ScanDirectory(path);

        if (skill is null)
        {
            ConsoleOutput.WriteError($"Error: No valid SKILL.md found in '{path}'.");
            ConsoleOutput.WriteError("The SKILL.md must have YAML frontmatter with 'name' and 'version' fields.");
            return 1;
        }

        var options = new PublishOptions(args.VersionOverride, args.Force, args.DryRun, args.Verbose);
        var version = options.VersionOverride ?? skill.Version;

        if (!args.DryRun)
            ConsoleOutput.WriteInfo($"Publishing {skill.Name}@{version}...");

        var orchestrator = new PublishOrchestrator(client);
        var result = await orchestrator.PublishAsync(skill, options);

        return PrintResult(result);
    }

    internal static int PrintResult(PublishResult result)
    {
        switch (result.Outcome)
        {
            case PublishOutcome.Published:
                ConsoleOutput.WriteSuccess($"Published {result.Name}@{result.Version}");
                if (result.Response is not null)
                {
                    ConsoleOutput.WriteDim($"  {result.Response.Sha256}");
                    ConsoleOutput.WriteDim($"  {result.Response.Url}");
                }
                return 0;

            case PublishOutcome.Skipped:
                ConsoleOutput.WriteWarning($"Skipped {result.Name}@{result.Version} ({result.Message})");
                return 0;

            case PublishOutcome.Failed:
                ConsoleOutput.WriteError($"Failed {result.Name}@{result.Version}: {result.Message}");
                return 1;
        }

        return 1;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage: skillstore publish <path> [options]");
        Console.WriteLine();
        Console.WriteLine("Legacy direct publish command. The server must explicitly enable compatibility mode.");
        Console.WriteLine();
        Console.WriteLine("Arguments:");
        Console.WriteLine("  <path>                Path to skill directory containing SKILL.md");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --version <version>   Override the version from frontmatter");
        Console.WriteLine("  --force, -f           Delete existing version before re-publishing");
        Console.WriteLine("  --dry-run             Show what would be published without uploading");
        Console.WriteLine("  --verbose, -v         Show detailed upload progress");
    }
}
