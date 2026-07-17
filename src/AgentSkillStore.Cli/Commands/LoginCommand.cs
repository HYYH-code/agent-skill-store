// -----------------------------------------------------------------------
// <copyright file="LoginCommand.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using AgentSkillStore.Cli.Config;
using AgentSkillStore.Cli.Output;

namespace AgentSkillStore.Cli.Commands;

internal static class LoginCommand
{
    public static Task<int> ExecuteAsync(ParsedArgs args)
    {
        if (args.Help)
        {
            PrintHelp();
            return Task.FromResult(0);
        }

        var existing = ConfigResolver.LoadConfigFile() ?? new CliConfig();
        var serverUrl = args.ServerUrl ?? existing.ServerUrl;
        var apiKey = args.ApiKey ?? existing.ApiKey;

        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            Console.Write("Agent Skill Store服务地址: ");
            serverUrl = Console.ReadLine()?.Trim();
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Console.Write("API Key: ");
            apiKey = Console.ReadLine()?.Trim();
        }

        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            ConsoleOutput.WriteError("Error: A valid http/https server URL is required.");
            return Task.FromResult(1);
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            ConsoleOutput.WriteError("Error: API key is required.");
            return Task.FromResult(1);
        }

        ConfigResolver.SaveConfigFile(new CliConfig
        {
            ServerUrl = uri.ToString().TrimEnd('/'),
            ApiKey = apiKey
        });

        ConsoleOutput.WriteSuccess($"Connected to {uri.ToString().TrimEnd('/')}.");
        return Task.FromResult(0);
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage: skillstore login --server <url> --api-key <key>");
        Console.WriteLine();
        Console.WriteLine("Save the AgentSkillStore server URL and API key in ~/.agent-skill-store/config.json.");
    }
}
