// -----------------------------------------------------------------------
// <copyright file="SubmitCommand.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;
using AgentSkillStore.Client;
using AgentSkillStore.Cli.Json;
using AgentSkillStore.Cli.Output;

namespace AgentSkillStore.Cli.Commands;

internal static class SubmitCommand
{
    public static async Task<int> ExecuteAsync(ParsedArgs args, AgentSkillStoreClient client)
    {
        if (args.Help)
        {
            PrintHelp();
            return 0;
        }

        if (args.Positional.Count == 0)
        {
            ConsoleOutput.WriteError("Usage: skillstore submit <package.zip> --name <name> --display-name <display> --namespace <namespace> --version <version> --owner <team> --risk-level <level> --network-policy <policy> --changelog <text>");
            return 1;
        }

        var packagePath = args.Positional[0];
        if (!File.Exists(packagePath))
        {
            ConsoleOutput.WriteError($"Error: package not found: {packagePath}");
            return 1;
        }

        var missing = MissingRequired(args).ToList();
        if (missing.Count > 0)
        {
            ConsoleOutput.WriteError($"Error: missing required option(s): {string.Join(", ", missing)}");
            return 1;
        }

        var request = new PublicationUploadRequest(
            packagePath,
            args.Name!,
            args.DisplayName!,
            args.Namespace!,
            args.VersionOverride!,
            args.Owner!,
            args.RiskLevel!,
            args.NetworkPolicy!,
            args.Changelog!);

        try
        {
            var result = await client.SubmitPublicationAsync(request);
            if (args.OutputFormat == "json")
            {
                Console.WriteLine(JsonSerializer.Serialize(
                    result,
                    CliJsonContext.Default.PublicationResponse));
            }
            else
            {
                ConsoleOutput.WriteSuccess($"Submitted {args.Namespace}/{args.Name}@{args.VersionOverride} for review.");
                ConsoleOutput.WriteInfo($"Publication: {result.Id}");
                ConsoleOutput.WriteInfo($"State: {result.State}");
            }

            return 0;
        }
        catch (HttpRequestException ex)
        {
            return ConsoleOutput.HandleHttpError(ex);
        }
    }

    private static IEnumerable<string> MissingRequired(ParsedArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.Name)) yield return "--name";
        if (string.IsNullOrWhiteSpace(args.DisplayName)) yield return "--display-name";
        if (string.IsNullOrWhiteSpace(args.Namespace)) yield return "--namespace";
        if (string.IsNullOrWhiteSpace(args.VersionOverride)) yield return "--version";
        if (string.IsNullOrWhiteSpace(args.Owner)) yield return "--owner";
        if (string.IsNullOrWhiteSpace(args.RiskLevel)) yield return "--risk-level";
        if (string.IsNullOrWhiteSpace(args.NetworkPolicy)) yield return "--network-policy";
        if (string.IsNullOrWhiteSpace(args.Changelog)) yield return "--changelog";
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage: skillstore submit <package.zip> [options]");
        Console.WriteLine();
        Console.WriteLine("Upload a governed Skill ZIP and create a pending review.");
        Console.WriteLine();
        Console.WriteLine("Required options:");
        Console.WriteLine("  --name <name>");
        Console.WriteLine("  --display-name <display>");
        Console.WriteLine("  --namespace <namespace>");
        Console.WriteLine("  --version <version>");
        Console.WriteLine("  --owner <team>");
        Console.WriteLine("  --risk-level <low|medium|high|critical>");
        Console.WriteLine("  --network-policy <deny-all|allow-list|unrestricted>");
        Console.WriteLine("  --changelog <text>");
    }
}
