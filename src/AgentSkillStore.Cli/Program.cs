// -----------------------------------------------------------------------
// <copyright file="Program.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Net.Http.Headers;
using System.Reflection;
using AgentSkillStore.Client;
using AgentSkillStore.Cli;
using AgentSkillStore.Cli.Commands;
using AgentSkillStore.Cli.Config;
using AgentSkillStore.Cli.Output;

var parsedArgs = CliArgsParser.Parse(args);

if (parsedArgs.Version)
{
    var version = Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "unknown";
    Console.WriteLine($"skillstore {version}");
    return 0;
}

if (parsedArgs.Help && parsedArgs.Command == "")
{
    PrintHelp();
    return 0;
}

if (parsedArgs.Command == "")
{
    PrintHelp();
    return 1;
}

if (!string.IsNullOrWhiteSpace(parsedArgs.InstallRoot) &&
    parsedArgs.Command is "install" or "uninstall" or "remove" or "update" or "rollback" or "sync" or "doctor" or "bridge")
{
    ConsoleOutput.WriteWarning(
        "Compatibility notice: --install-root now overrides the .agents root instead of a client-specific skills directory.");
}

if (parsedArgs.Command == "config")
    return await ConfigCommand.ExecuteAsync(parsedArgs);

if (parsedArgs.Command == "login")
    return await LoginCommand.ExecuteAsync(parsedArgs);

// lint operates entirely on local files - no server URL or auth needed
if (parsedArgs.Command == "lint")
    return await LintCommand.ExecuteAsync(parsedArgs);

var isLocalInstallList = parsedArgs.Command == "list" &&
                         parsedArgs.Positional.Count > 0 &&
                         string.Equals(parsedArgs.Positional[0], "installed", StringComparison.OrdinalIgnoreCase);
var isOfflineApiKeyGenerate = parsedArgs.Command == "api-key" && parsedArgs.SubCommand == "generate";
var isLocalMaintenance = parsedArgs.Command is "uninstall" or "remove" or "doctor" or "bridge" or "migrate";
var isLocalOnly = isLocalMaintenance || isLocalInstallList || isOfflineApiKeyGenerate;
if (isLocalOnly)
{
    using var localClient = new AgentSkillStoreClient("http://placeholder");
    return await DispatchAsync(parsedArgs, localClient, "local");
}

// --help on subcommands works without auth
if (parsedArgs.Help)
{
    using var helpClient = new AgentSkillStoreClient("http://placeholder");
    return await DispatchAsync(parsedArgs, helpClient, "help");
}

var resolver = new ConfigResolver();
var config = resolver.Resolve(parsedArgs.ServerUrl, parsedArgs.ApiKey);

var requiresAuth = parsedArgs.Command is not "list"
                   and not "search"
                   and not "info"
                   and not "list-subagents"
                   and not "versions"
                   and not "verify"
                   and not "download-subagent";

if (!config.HasServerUrl)
{
    ConsoleOutput.WriteError("Error: Server URL not configured.");
    ConsoleOutput.WriteError("Set AGENT_SKILL_STORE_URL or run 'skillstore config init'");
    return 1;
}

if (requiresAuth && !config.HasApiKey)
{
    ConsoleOutput.WriteError("Error: Authentication required.");
    ConsoleOutput.WriteError("Set AGENT_SKILL_STORE_API_KEY or run 'skillstore config init'");
    return 1;
}

using var httpClient = CreateHttpClient(config, parsedArgs.Verbose);
using var client = new AgentSkillStoreClient(httpClient);
return await DispatchAsync(parsedArgs, client, config.ServerUrl!);

static async Task<int> DispatchAsync(ParsedArgs parsedArgs, AgentSkillStoreClient client, string server) =>
    parsedArgs.Command switch
    {
        "publish" => await PublishCommand.ExecuteAsync(parsedArgs, client),
        "submit" => await SubmitCommand.ExecuteAsync(parsedArgs, client),
        "submissions" => await SubmissionsCommand.ExecuteAsync(parsedArgs, client),
        "publish-subagent" => await PublishSubAgentCommand.ExecuteAsync(parsedArgs, client),
        "publish-subagents" => await PublishSubAgentsCommand.ExecuteAsync(parsedArgs, client),
        "download-subagent" => await DownloadSubAgentCommand.ExecuteAsync(parsedArgs, client),
        "publish-all" => await PublishAllCommand.ExecuteAsync(parsedArgs, client),
        "delete" => await DeleteCommand.ExecuteAsync(parsedArgs, client),
        "delete-subagent" => await DeleteSubAgentCommand.ExecuteAsync(parsedArgs, client),
        "list" => await ListCommand.ExecuteAsync(parsedArgs, client),
        "search" => await SearchCommand.ExecuteAsync(parsedArgs, client),
        "info" => await InfoCommand.ExecuteAsync(parsedArgs, client),
        "install" => await InstallCommand.ExecuteAsync(parsedArgs, client, server),
        "uninstall" or "remove" => await UninstallCommand.ExecuteAsync(parsedArgs, client, server),
        "update" or "upgrade" => await UpdateCommand.ExecuteAsync(parsedArgs, client, server),
        "rollback" => await RollbackCommand.ExecuteAsync(parsedArgs, client, server),
        "list-subagents" => await ListSubAgentsCommand.ExecuteAsync(parsedArgs, client),
        "versions" => await VersionsCommand.ExecuteAsync(parsedArgs, client),
        "verify" => await VerifyCommand.ExecuteAsync(parsedArgs, client),
        "sync" => await SyncCommand.ExecuteAsync(parsedArgs, client, server),
        "doctor" => DoctorCommand.Execute(parsedArgs),
        "bridge" => BridgeCommand.Execute(parsedArgs),
        "migrate" => await MigrateCommand.ExecuteAsync(parsedArgs, client, server),
        "api-key" => await ApiKeyCommand.ExecuteAsync(parsedArgs, client),
        _ => UnknownCommand(parsedArgs.Command)
    };

static int UnknownCommand(string command)
{
    ConsoleOutput.WriteError($"Unknown command: '{command}'");
    Console.WriteLine();
    PrintHelp();
    return 1;
}

static HttpClient CreateHttpClient(ResolvedConfig config, bool verbose)
{
    HttpMessageHandler handler = verbose
        ? new VerboseLoggingHandler()
        : new HttpClientHandler();

    var client = new HttpClient(handler)
    {
        BaseAddress = new Uri(config.ServerUrl!.TrimEnd('/') + "/")
    };

    if (!string.IsNullOrEmpty(config.ApiKey))
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", config.ApiKey);

    return client;
}

static void PrintHelp()
{
    Console.WriteLine("Agent Skill Store CLI - Usage: skillstore <command> [options]");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  publish <path>            Legacy direct publish (server compatibility mode)");
    Console.WriteLine("  submit <package.zip>      Upload a Skill ZIP into the governed review workflow");
    Console.WriteLine("  submissions               List publications submitted by this Agent Key");
    Console.WriteLine("  publish-subagent <path>   Publish a sub-agent markdown file to the server");
    Console.WriteLine("  publish-subagents <path>  Batch-publish sub-agent markdown files");
    Console.WriteLine("  download-subagent <n> <v> <path> Download a verified sub-agent artifact");
    Console.WriteLine("  publish-all <path>        Batch-publish all skills in a directory");
    Console.WriteLine("  lint <path>               Validate skills or sub-agents (no auth required)");
    Console.WriteLine("  delete <name> <version>   Legacy physical delete (server compatibility mode)");
    Console.WriteLine("  delete-subagent <n> <v>   Delete a published sub-agent version");
    Console.WriteLine("  list                      List skills on the server");
    Console.WriteLine("  search <query>            Search skills on the server");
    Console.WriteLine("  info <name> [version]     Show skill version metadata");
    Console.WriteLine("  install <name> [version]  Install a skill archive");
    Console.WriteLine("  uninstall <name>          Uninstall a locally installed skill");
    Console.WriteLine("  update <name>             Update a locally installed skill");
    Console.WriteLine("  list installed            List locally installed skills");
    Console.WriteLine("  rollback <name>           Roll back to the previous installed version");
    Console.WriteLine("  list-subagents            List sub-agents on the server");
    Console.WriteLine("  versions <name>           List all versions of a skill");
    Console.WriteLine("  verify <path>             Verify local skill matches published version");
    Console.WriteLine("  sync                      Reconcile managed Skills and project lockfile");
    Console.WriteLine("  doctor                    Diagnose links, digests, conflicts, and legacy installs");
    Console.WriteLine("  bridge                    Manage non-native Agent bridges");
    Console.WriteLine("  migrate                   Inspect or migrate legacy direct installations");
    Console.WriteLine("  config                    Manage CLI configuration");
    Console.WriteLine("  login                     Save the server URL and API key");
    Console.WriteLine("  api-key                   Generate or manage API keys");
    Console.WriteLine();
    Console.WriteLine("Global options:");
    Console.WriteLine("  --server-url <url>        AgentSkillStore URL (overrides config/env)");
    Console.WriteLine("  --api-key <key>           API key (overrides config/env)");
    Console.WriteLine("  --output <text|json>      Output format (default: text)");
    Console.WriteLine("  --target <all|codex|claude|pi|opencode|agents> Bridge target");
    Console.WriteLine("  --scope <user|project>    Installation scope");
    Console.WriteLine("  --install-root <path>     Override the .agents root");
    Console.WriteLine("  --allow-non-stable       Allow an explicit beta/deprecated version");
    Console.WriteLine("  --yes, -y                Confirm destructive actions or permission expansion");
    Console.WriteLine("  --verbose, -v             Enable verbose output");
    Console.WriteLine("  --help, -h                Show help");
    Console.WriteLine("  --version                 Show version");
}
