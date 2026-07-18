// -----------------------------------------------------------------------
// <copyright file="DirectoryLinkManager.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Diagnostics;

namespace AgentSkillStore.Cli.Commands;

internal static class DirectoryLinkManager
{
    public static string LinkKind => OperatingSystem.IsWindows() ? "junction" : "symlink";

    public static bool Exists(string path)
    {
        try
        {
            _ = File.GetAttributes(path);
            return true;
        }
        catch (FileNotFoundException)
        {
            return HasLinkTarget(path);
        }
        catch (DirectoryNotFoundException)
        {
            return HasLinkTarget(path);
        }
        catch (IOException)
        {
            return HasLinkTarget(path);
        }
    }

    public static bool IsDirectoryLink(string path)
    {
        try
        {
            var info = new DirectoryInfo(path);
            if (!string.IsNullOrWhiteSpace(info.LinkTarget))
                return true;

            var attributes = info.Attributes;
            return (attributes & FileAttributes.Directory) != 0 &&
                   (attributes & FileAttributes.ReparsePoint) != 0;
        }
        catch (FileNotFoundException)
        {
            return HasLinkTarget(path);
        }
        catch (DirectoryNotFoundException)
        {
            return HasLinkTarget(path);
        }
        catch (IOException)
        {
            return HasLinkTarget(path);
        }
    }

    public static string? ResolveTarget(string path)
    {
        if (!IsDirectoryLink(path))
            return null;
        var info = new DirectoryInfo(path);
        var target = info.LinkTarget;
        if (string.IsNullOrWhiteSpace(target))
            return null;
        if (!Path.IsPathRooted(target))
            target = Path.Combine(info.Parent?.FullName ?? Directory.GetCurrentDirectory(), target);
        return Path.GetFullPath(target);
    }

    public static void Create(string linkPath, string targetPath)
    {
        var normalizedLink = Path.GetFullPath(linkPath);
        var normalizedTarget = Path.GetFullPath(targetPath);
        if (!Directory.Exists(normalizedTarget))
            throw new DirectoryNotFoundException($"Link target does not exist: {normalizedTarget}");
        if (Exists(normalizedLink))
            throw new IOException($"Refusing to replace existing path: {normalizedLink}");

        Directory.CreateDirectory(Path.GetDirectoryName(normalizedLink)!);
        if (OperatingSystem.IsWindows())
            CreateWindowsJunction(normalizedLink, normalizedTarget);
        else
            Directory.CreateSymbolicLink(normalizedLink, normalizedTarget);

        var resolved = ResolveTarget(normalizedLink);
        if (!PathsEqual(resolved, normalizedTarget))
        {
            Remove(normalizedLink);
            throw new IOException($"Created link does not resolve to the expected target: {normalizedLink}");
        }
    }

    public static string? Replace(string linkPath, string targetPath, string? expectedCurrentTarget = null)
    {
        var normalizedLink = Path.GetFullPath(linkPath);
        var normalizedTarget = Path.GetFullPath(targetPath);
        var previousTarget = ResolveTarget(normalizedLink);
        if (Exists(normalizedLink) && previousTarget is null)
            throw new IOException($"Refusing to replace non-link path: {normalizedLink}");
        if (expectedCurrentTarget is not null &&
            !PathsEqual(previousTarget, Path.GetFullPath(expectedCurrentTarget)))
        {
            throw new IOException($"Link target changed outside skillstore: {normalizedLink}");
        }
        if (PathsEqual(previousTarget, normalizedTarget))
            return previousTarget;

        var suffix = Guid.NewGuid().ToString("N");
        var nextPath = $"{normalizedLink}.next-{suffix}";
        var oldPath = $"{normalizedLink}.old-{suffix}";
        Create(nextPath, normalizedTarget);
        var movedOld = false;
        try
        {
            if (Exists(normalizedLink))
            {
                Directory.Move(normalizedLink, oldPath);
                movedOld = true;
            }

            Directory.Move(nextPath, normalizedLink);
            if (movedOld)
                Remove(oldPath);
            return previousTarget;
        }
        catch
        {
            if (Exists(normalizedLink))
                Remove(normalizedLink);
            if (movedOld && Exists(oldPath))
                Directory.Move(oldPath, normalizedLink);
            throw;
        }
        finally
        {
            if (Exists(nextPath))
                Remove(nextPath);
            if (Exists(oldPath))
                Remove(oldPath);
        }
    }

    public static void Remove(string linkPath, string? expectedTarget = null)
    {
        if (!Exists(linkPath))
            return;
        var resolved = ResolveTarget(linkPath);
        if (resolved is null)
            throw new IOException($"Refusing to remove non-link path: {linkPath}");
        if (expectedTarget is not null && !PathsEqual(resolved, Path.GetFullPath(expectedTarget)))
            throw new IOException($"Refusing to remove link with unexpected target: {linkPath}");
        Directory.Delete(linkPath);
    }

    public static bool PathsEqual(string? left, string? right)
    {
        if (left is null || right is null)
            return left is null && right is null;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            comparison);
    }

    public static IReadOnlyList<string> EnumerateRegularFiles(string root)
    {
        var normalizedRoot = Path.GetFullPath(root);
        if (!Directory.Exists(normalizedRoot))
            throw new DirectoryNotFoundException($"Managed directory does not exist: {normalizedRoot}");

        var files = new List<string>();
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(normalizedRoot));
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Refusing to traverse directory link: {directory.FullName}");

            foreach (var item in directory.EnumerateFileSystemInfos())
            {
                if ((item.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"Refusing to traverse linked content: {item.FullName}");
                if (item is DirectoryInfo childDirectory)
                    pending.Push(childDirectory);
                else
                    files.Add(item.FullName);
            }
        }

        return files;
    }

    private static void CreateWindowsJunction(string linkPath, string targetPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(linkPath);
        startInfo.ArgumentList.Add(targetPath);

        using var process = Process.Start(startInfo)
                            ?? throw new IOException("Unable to start cmd.exe for junction creation.");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd().Trim();
            var output = process.StandardOutput.ReadToEnd().Trim();
            throw new IOException($"Unable to create junction '{linkPath}': {error} {output}".Trim());
        }
    }

    private static bool HasLinkTarget(string path)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(new DirectoryInfo(path).LinkTarget);
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
