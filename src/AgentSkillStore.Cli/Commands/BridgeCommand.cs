// -----------------------------------------------------------------------
// <copyright file="BridgeCommand.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;
using AgentSkillStore.Cli.Json;
using AgentSkillStore.Cli.Output;

namespace AgentSkillStore.Cli.Commands;

internal static class BridgeCommand
{
    public static int Execute(ParsedArgs args)
    {
        if (args.Help)
        {
            PrintHelp();
            return 0;
        }

        try
        {
            return args.SubCommand switch
            {
                "add" => Add(args),
                "remove" => Remove(args),
                "list" => List(args),
                _ => ShowHelp()
            };
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
        {
            ConsoleOutput.WriteError($"Error: {exception.Message}");
            return 1;
        }
    }

    private static int Add(ParsedArgs args)
    {
        var (registry, context) = ResolveContext(args);
        var target = ResolveBridgeTarget(args);
        using var operationLock = SkillStoreOperationLock.Acquire(context.OperationLockPath);
        var record = ResolveRecord(args, registry, context);
        if (record.Bridges.Any(bridge => string.Equals(bridge.Target, target, StringComparison.OrdinalIgnoreCase)))
        {
            ConsoleOutput.WriteInfo($"Bridge already exists for {target}.");
            return 0;
        }

        var stateBefore = registry.Load();
        var bridge = SkillBridgeManager.CreateClaudeBridge(context, record.PackageName, record.ActivePath);
        try
        {
            registry.Upsert(record with { Bridges = [.. record.Bridges, bridge] }, previous: null);
            GitExcludeManager.AddManagedPaths(context, record.PackageName, hasClaudeBridge: true);
        }
        catch
        {
            SkillBridgeManager.RemoveBridge(bridge);
            registry.Restore(stateBefore);
            throw;
        }

        ConsoleOutput.WriteSuccess($"Created {target} bridge: {bridge.Path}");
        return 0;
    }

    private static int Remove(ParsedArgs args)
    {
        var (registry, context) = ResolveContext(args);
        var target = ResolveBridgeTarget(args);
        using var operationLock = SkillStoreOperationLock.Acquire(context.OperationLockPath);
        var record = ResolveRecord(args, registry, context);
        var bridge = record.Bridges.FirstOrDefault(candidate =>
            string.Equals(candidate.Target, target, StringComparison.OrdinalIgnoreCase));
        if (bridge is null)
        {
            ConsoleOutput.WriteInfo($"No managed {target} bridge exists.");
            return 0;
        }

        SkillBridgeManager.RemoveBridge(bridge);
        try
        {
            registry.Upsert(record with
            {
                Bridges = record.Bridges.Where(candidate => candidate != bridge).ToArray()
            }, previous: null);
        }
        catch
        {
            DirectoryLinkManager.Create(bridge.Path, bridge.TargetPath);
            throw;
        }

        ConsoleOutput.WriteSuccess($"Removed {target} bridge: {bridge.Path}");
        return 0;
    }

    private static int List(ParsedArgs args)
    {
        var context = SkillStoreContext.Resolve(
            InstallCommand.ResolveScope(args),
            args.InstallRoot);
        var bridges = new InstallationRegistry().Load().Installations
            .Where(record => string.Equals(record.Layout, "shared", StringComparison.Ordinal) &&
                             string.Equals(record.Scope, context.Scope, StringComparison.Ordinal) &&
                             ProjectRootsEqual(record.ProjectRoot, context.ProjectRoot))
            .SelectMany(record => record.Bridges.Select(bridge => new SkillBridgeSummary
            {
                Name = record.Name,
                Target = bridge.Target,
                Path = bridge.Path,
                LinkKind = bridge.LinkKind,
                TargetPath = bridge.TargetPath
            }))
            .ToList();

        if (args.OutputFormat == "json")
        {
            Console.WriteLine(JsonSerializer.Serialize(
                (IReadOnlyList<SkillBridgeSummary>)bridges,
                CliJsonContext.Default.IReadOnlyListSkillBridgeSummary));
            return 0;
        }
        if (bridges.Count == 0)
        {
            ConsoleOutput.WriteInfo("No managed bridges found.");
            return 0;
        }

        ConsoleOutput.WriteTable(
            ["SKILL", "TARGET", "KIND", "PATH"],
            bridges.Select(item => new[] { item.Name, item.Target, item.LinkKind, item.Path }).ToList());
        return 0;
    }

    private static (InstallationRegistry Registry, SkillStoreContext Context) ResolveContext(ParsedArgs args)
    {
        var context = SkillStoreContext.Resolve(InstallCommand.ResolveScope(args), args.InstallRoot);
        return (new InstallationRegistry(), context);
    }

    private static SkillInstallRecord ResolveRecord(
        ParsedArgs args,
        InstallationRegistry registry,
        SkillStoreContext context)
    {
        if (args.Positional.Count == 0)
            throw new ArgumentException("Skill name is required.");
        var reference = SkillReference.Parse(args.Positional[0]);
        return registry.Find(reference.Name, context)
               ?? throw new InvalidOperationException($"'{reference.Name}' is not installed in {context.Scope} scope.");
    }

    private static string ResolveBridgeTarget(ParsedArgs args)
    {
        var target = string.IsNullOrWhiteSpace(args.Target) ? "claude" : args.Target;
        if (!string.Equals(target, "claude", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Claude is the only bridge target supported in v0.2.0.");
        return "claude";
    }

    private static bool ProjectRootsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right);
        return DirectoryLinkManager.PathsEqual(left, right);
    }

    private static int ShowHelp()
    {
        PrintHelp();
        return 1;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage: skillstore bridge <add|remove|list> [skill] [options]");
        Console.WriteLine("  --target claude");
        Console.WriteLine("  --scope <user|project>");
        Console.WriteLine("  --output <text|json>");
    }
}
