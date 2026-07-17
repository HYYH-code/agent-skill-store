// -----------------------------------------------------------------------
// <copyright file="ApiKeyCommand.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;
using System.Security.Cryptography;
using AgentSkillStore.Client;
using AgentSkillStore.Cli.Config;
using AgentSkillStore.Cli.Json;
using AgentSkillStore.Cli.Output;

namespace AgentSkillStore.Cli.Commands;

internal static class ApiKeyCommand
{
    public static async Task<int> ExecuteAsync(ParsedArgs args, AgentSkillStoreClient client)
    {
        if (args.Help)
        {
            PrintHelp();
            return 0;
        }

        return args.SubCommand switch
        {
            "generate" => Generate(),
            "create" => await Create(args, client),
            "list" => await List(args, client),
            "delete" => await Delete(args, client),
            _ => ShowSubcommandHelp()
        };
    }

    private static int Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var key = $"sk-{Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')}";
        Console.WriteLine(key);
        ConsoleOutput.WriteInfo("Set AGENTSKILLSTORE__BOOTSTRAPAPIKEY to this value before the server's first start.");
        ConsoleOutput.WriteInfo("The server stores only its SHA-256 hash. Keep the original key in a secret manager.");
        return 0;
    }

    private static async Task<int> Create(ParsedArgs args, AgentSkillStoreClient client)
    {
        var label = args.Label;
        if (string.IsNullOrWhiteSpace(label))
        {
            ConsoleOutput.WriteError("Error: --label is required.");
            return 1;
        }

        DateTimeOffset? expiresAt = null;
        if (!string.IsNullOrWhiteSpace(args.ExpiresAt))
        {
            if (DateTimeOffset.TryParse(args.ExpiresAt, out var parsed))
                expiresAt = parsed;
            else
            {
                ConsoleOutput.WriteError($"Error: Invalid date format for --expires-at: '{args.ExpiresAt}'");
                return 1;
            }
        }

        try
        {
            var identity = MachineIdentityProvider.Create(args.AgentId, args.MachineHash);
            var scopes = args.Scopes.Count > 0 ? args.Scopes : ["skills:read", "skills:submit"];
            var response = await client.CreateApiKeyAsync(
                label,
                expiresAt,
                identity.AgentId,
                identity.MachineHash,
                scopes);
            ConsoleOutput.WriteSuccess($"Created API key: {response.Key}");
            ConsoleOutput.WriteInfo($"Label: {response.Label}");
            ConsoleOutput.WriteInfo($"ID: {response.Id}");
            ConsoleOutput.WriteInfo($"Agent ID: {response.AgentId}");
            ConsoleOutput.WriteInfo($"Machine: {response.MachineHash}");
            ConsoleOutput.WriteInfo($"Scopes: {string.Join(' ', response.Scopes)}");
            return 0;
        }
        catch (HttpRequestException ex)
        {
            return ConsoleOutput.HandleHttpError(ex);
        }
    }

    private static async Task<int> List(ParsedArgs args, AgentSkillStoreClient client)
    {
        try
        {
            var keys = await client.ListApiKeysAsync();

            if (args.OutputFormat == "json")
            {
                var json = JsonSerializer.Serialize(keys,
                    CliJsonContext.Default.IReadOnlyListApiKeySummary);
                Console.WriteLine(json);
                return 0;
            }

            if (keys.Count == 0)
            {
                ConsoleOutput.WriteInfo("No API keys found.");
                return 0;
            }

            var headers = new[] { "ID", "LABEL", "AGENT", "SCOPES", "CREATED", "EXPIRES" };
            var rows = keys.Select(k => new[]
            {
                k.Id.ToString(),
                k.Label,
                k.AgentId,
                string.Join(' ', k.Scopes),
                k.CreatedAt.ToString("yyyy-MM-dd"),
                k.ExpiresAt?.ToString("yyyy-MM-dd") ?? "never"
            }).ToList();

            ConsoleOutput.WriteTable(headers, rows);
            return 0;
        }
        catch (HttpRequestException ex)
        {
            return ConsoleOutput.HandleHttpError(ex);
        }
    }

    private static async Task<int> Delete(ParsedArgs args, AgentSkillStoreClient client)
    {
        if (args.Positional.Count == 0)
        {
            ConsoleOutput.WriteError("Usage: skillstore api-key delete <id>");
            return 1;
        }

        if (!long.TryParse(args.Positional[0], out var id))
        {
            ConsoleOutput.WriteError($"Error: Invalid API key ID: '{args.Positional[0]}'");
            return 1;
        }

        try
        {
            await client.DeleteApiKeyAsync(id);
            ConsoleOutput.WriteSuccess($"Deleted API key {id}");
            return 0;
        }
        catch (HttpRequestException ex)
        {
            return ConsoleOutput.HandleHttpError(ex);
        }
    }

    private static int ShowSubcommandHelp()
    {
        PrintHelp();
        return 1;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage: skillstore api-key <subcommand> [options]");
        Console.WriteLine();
        Console.WriteLine("Manage server API keys.");
        Console.WriteLine();
        Console.WriteLine("Subcommands:");
        Console.WriteLine("  generate              Generate an offline bootstrap key");
        Console.WriteLine("  create                Create a new API key");
        Console.WriteLine("  list                  List all API keys");
        Console.WriteLine("  delete <id>           Delete an API key");
        Console.WriteLine();
        Console.WriteLine("Create options:");
        Console.WriteLine("  --label <label>       Key label (required)");
        Console.WriteLine("  --agent-id <id>       Agent identity (default: derived from this machine)");
        Console.WriteLine("  --machine-hash <hash> Machine fingerprint (default: derived from this machine)");
        Console.WriteLine("  --scope <scope>       Scope to grant; repeatable (default: skills:read skills:submit)");
        Console.WriteLine("  --expires-at <date>   Expiration date (optional)");
    }
}
