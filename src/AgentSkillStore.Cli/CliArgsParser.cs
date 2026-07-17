// -----------------------------------------------------------------------
// <copyright file="CliArgsParser.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

namespace AgentSkillStore.Cli;

internal sealed class ParsedArgs
{
    public string Command { get; init; } = "";
    public string SubCommand { get; init; } = "";
    public IReadOnlyList<string> Positional { get; init; } = [];
    public string? ServerUrl { get; init; }
    public string? ApiKey { get; init; }
    public string? OutputFormat { get; init; }
    public string? Target { get; init; }
    public string? InstallRoot { get; init; }
    public bool Verbose { get; init; }
    public bool Help { get; init; }
    public bool Version { get; init; }
    public bool Force { get; init; }
    public bool DryRun { get; init; }
    public bool Yes { get; init; }
    public bool AllowNonStable { get; init; }
    public string? VersionOverride { get; init; }
    public string? Search { get; init; }
    public int? Skip { get; init; }
    public int? Take { get; init; }
    public string? Label { get; init; }
    public string? ExpiresAt { get; init; }
    public string? AgentId { get; init; }
    public string? MachineHash { get; init; }
    public IReadOnlyList<string> Scopes { get; init; } = [];
    public string? Name { get; init; }
    public string? DisplayName { get; init; }
    public string? Namespace { get; init; }
    public string? Owner { get; init; }
    public string? RiskLevel { get; init; }
    public string? NetworkPolicy { get; init; }
    public string? Changelog { get; init; }
    public string? Key { get; init; }
    public string? Value { get; init; }
}

internal static class CliArgsParser
{
    public static ParsedArgs Parse(string[] args)
    {
        var positional = new List<string>();
        string? serverUrl = null, apiKey = null, outputFormat = null, target = null, installRoot = null;
        string? versionOverride = null, search = null, label = null, expiresAt = null;
        string? agentId = null, machineHash = null, name = null, displayName = null, ns = null;
        string? owner = null, riskLevel = null, networkPolicy = null, changelog = null;
        var scopes = new List<string>();
        string? configKey = null, configValue = null;
        int? skip = null, take = null;
        bool verbose = false, help = false, version = false, force = false, dryRun = false, yes = false;
        var allowNonStable = false;

        var command = "";
        var subCommand = "";
        var commandParsed = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (!commandParsed && !arg.StartsWith('-'))
            {
                if (command == "")
                {
                    command = arg.ToLowerInvariant();

                    if (command is "api-key" or "config" or "publish-all")
                    {
                        if (command is "api-key" or "config" && i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                        {
                            subCommand = args[++i].ToLowerInvariant();
                        }

                        commandParsed = true;
                    }
                    else
                    {
                        commandParsed = true;
                    }

                    continue;
                }
            }

            switch (arg)
            {
                case "--server-url" or "--server" when i + 1 < args.Length:
                    serverUrl = args[++i];
                    break;
                case "--api-key" when i + 1 < args.Length:
                    apiKey = args[++i];
                    break;
                case "--output" when i + 1 < args.Length:
                    outputFormat = args[++i].ToLowerInvariant();
                    break;
                case "--target" or "--agent" when i + 1 < args.Length:
                    target = args[++i].ToLowerInvariant();
                    break;
                case "--install-root" when i + 1 < args.Length:
                    installRoot = args[++i];
                    break;
                case "--version" when command == "":
                    version = true;
                    break;
                case "--version" when i + 1 < args.Length:
                    versionOverride = args[++i];
                    break;
                case "--search" when i + 1 < args.Length:
                    search = args[++i];
                    break;
                case "--skip" when i + 1 < args.Length:
                    if (int.TryParse(args[++i], out var s)) skip = s;
                    break;
                case "--take" when i + 1 < args.Length:
                    if (int.TryParse(args[++i], out var t)) take = t;
                    break;
                case "--label" when i + 1 < args.Length:
                    label = args[++i];
                    break;
                case "--expires-at" when i + 1 < args.Length:
                    expiresAt = args[++i];
                    break;
                case "--agent-id" when i + 1 < args.Length:
                    agentId = args[++i];
                    break;
                case "--machine-hash" when i + 1 < args.Length:
                    machineHash = args[++i];
                    break;
                case "--scope" when i + 1 < args.Length:
                    scopes.Add(args[++i]);
                    break;
                case "--name" when i + 1 < args.Length:
                    name = args[++i];
                    break;
                case "--display-name" when i + 1 < args.Length:
                    displayName = args[++i];
                    break;
                case "--namespace" when i + 1 < args.Length:
                    ns = args[++i];
                    break;
                case "--owner" when i + 1 < args.Length:
                    owner = args[++i];
                    break;
                case "--risk-level" when i + 1 < args.Length:
                    riskLevel = args[++i].ToLowerInvariant();
                    break;
                case "--network-policy" when i + 1 < args.Length:
                    networkPolicy = args[++i].ToLowerInvariant();
                    break;
                case "--changelog" when i + 1 < args.Length:
                    changelog = args[++i];
                    break;
                case "--verbose" or "-v":
                    verbose = true;
                    break;
                case "--help" or "-h":
                    help = true;
                    break;
                case "--force" or "-f":
                    force = true;
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--yes" or "-y":
                    yes = true;
                    break;
                case "--allow-non-stable":
                    allowNonStable = true;
                    break;
                case "-V":
                    version = true;
                    break;
                default:
                    if (!arg.StartsWith('-'))
                    {
                        if (command == "config" && subCommand == "set")
                        {
                            if (configKey is null)
                                configKey = arg;
                            else
                                configValue ??= arg;
                        }
                        else
                        {
                            positional.Add(arg);
                        }
                    }
                    break;
            }
        }

        return new ParsedArgs
        {
            Command = command,
            SubCommand = subCommand,
            Positional = positional,
            ServerUrl = serverUrl,
            ApiKey = apiKey,
            OutputFormat = outputFormat,
            Target = target,
            InstallRoot = installRoot,
            Verbose = verbose,
            Help = help,
            Version = version,
            Force = force,
            DryRun = dryRun,
            Yes = yes,
            AllowNonStable = allowNonStable,
            VersionOverride = versionOverride,
            Search = search,
            Skip = skip,
            Take = take,
            Label = label,
            ExpiresAt = expiresAt,
            AgentId = agentId,
            MachineHash = machineHash,
            Scopes = scopes,
            Name = name,
            DisplayName = displayName,
            Namespace = ns,
            Owner = owner,
            RiskLevel = riskLevel,
            NetworkPolicy = networkPolicy,
            Changelog = changelog,
            Key = configKey,
            Value = configValue
        };
    }
}
