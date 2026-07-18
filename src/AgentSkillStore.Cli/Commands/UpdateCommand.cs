// -----------------------------------------------------------------------
// <copyright file="UpdateCommand.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using AgentSkillStore.Client;
using AgentSkillStore.Cli.Output;

namespace AgentSkillStore.Cli.Commands;

internal static class UpdateCommand
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
            var result = await installer.UpdateAsync(
                name,
                target,
                args.InstallRoot,
                args.Force,
                args.AllowNonStable,
                args.Yes,
                InstallCommand.ResolveScope(args));
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
        Console.WriteLine("Usage: skillstore update <name> [options]");
        Console.WriteLine("  --target <all|codex|claude|pi|opencode|agents>");
        Console.WriteLine("  --scope <user|project>");
        Console.WriteLine("  --allow-non-stable");
        Console.WriteLine("  --yes, -y                   Accept a declared permission expansion");
        Console.WriteLine("  --force");
    }
}
