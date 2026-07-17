// -----------------------------------------------------------------------
// <copyright file="InstallCommand.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;
using AgentSkillStore.Client;
using AgentSkillStore.Cli.Json;
using AgentSkillStore.Cli.Output;

namespace AgentSkillStore.Cli.Commands;

internal static class InstallCommand
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
            var target = ResolveTarget(args);
            var name = args.Positional[0];
            var version = args.VersionOverride ?? (args.Positional.Count > 1 ? args.Positional[1] : null);
            var installer = new SkillInstaller(client, new InstallationRegistry(), server);
            var result = await installer.InstallAsync(
                name,
                version,
                target,
                args.InstallRoot,
                args.Force,
                args.AllowNonStable,
                args.Yes);
            WriteResult(result, args.OutputFormat);
            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or InvalidDataException or ArgumentException or HttpRequestException)
        {
            ConsoleOutput.WriteError($"Error: {ex.Message}");
            return 1;
        }
    }

    internal static string ResolveTarget(ParsedArgs args)
    {
        return string.IsNullOrWhiteSpace(args.Target) ? "codex" : args.Target;
    }

    internal static void WriteResult(InstallResult result, string? outputFormat)
    {
        if (outputFormat == "json")
        {
            Console.WriteLine(JsonSerializer.Serialize(result, CliJsonContext.Default.InstallResult));
            return;
        }

        ConsoleOutput.WriteSuccess(
            $"Installed {result.Record.Name}@{result.Record.Version} for {result.Record.Target} to {result.Record.InstallPath}");
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage: skillstore install <name> [version] [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --target <codex|claude|pi>  Install target (default: codex)");
        Console.WriteLine("  --install-root <path>       Override target install root");
        Console.WriteLine("  --force                     Overwrite modified or unmanaged files");
        Console.WriteLine("  --allow-non-stable          Allow an explicit beta/deprecated version");
        Console.WriteLine("  --yes, -y                   Accept a declared permission expansion");
        Console.WriteLine("  --output <text|json>        Output format");
    }
}
