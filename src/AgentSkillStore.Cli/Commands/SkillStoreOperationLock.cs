// -----------------------------------------------------------------------
// <copyright file="SkillStoreOperationLock.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

namespace AgentSkillStore.Cli.Commands;

internal sealed class SkillStoreOperationLock : IDisposable
{
    private readonly FileStream _stream;

    private SkillStoreOperationLock(FileStream stream)
    {
        _stream = stream;
    }

    public static SkillStoreOperationLock Acquire(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            stream.SetLength(0);
            using var writer = new StreamWriter(stream, leaveOpen: true);
            writer.Write($"{Environment.ProcessId} {DateTimeOffset.UtcNow:O}");
            writer.Flush();
            stream.Position = 0;
            return new SkillStoreOperationLock(stream);
        }
        catch (IOException exception)
        {
            throw new IOException($"Another skillstore operation is active for '{Path.GetDirectoryName(path)}'.", exception);
        }
    }

    public void Dispose()
    {
        _stream.Dispose();
    }
}
