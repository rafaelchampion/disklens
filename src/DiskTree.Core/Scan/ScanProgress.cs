namespace DiskTree.Core.Scan;

public sealed record ScanSnapshot(
    ulong Files,
    ulong Dirs,
    ulong Bytes,
    ulong Errors,
    bool Finished,
    bool Cancelled,
    string? Phase,
    IReadOnlyList<string> Messages
);

public sealed class ScanProgress
{
    private const int MaxErrorDetail = 50;

    private long _files;
    private long _dirs;
    private long _bytes;
    private long _errors;
    private int _finished;
    private int _cancelled;
    private string? _phase;
    private readonly Lock _lock = new();
    private readonly List<string> _messages = [];

    public void CountFile(ulong bytes)
    {
        Interlocked.Increment(ref _files);
        Interlocked.Add(ref _bytes, (long)bytes);
    }

    public void CountDir()
    {
        Interlocked.Increment(ref _dirs);
    }

    public void RecordError(string path, string error)
    {
        Interlocked.Increment(ref _errors);
        lock (_lock)
        {
            if (_messages.Count < MaxErrorDetail)
            {
                _messages.Add($"{path}: {error}");
            }
        }
    }

    public void Finish()
    {
        Interlocked.Exchange(ref _finished, 1);
    }

    public void Cancel()
    {
        Interlocked.Exchange(ref _cancelled, 1);
    }

    public void SetPhase(string phase)
    {
        Volatile.Write(ref _phase, phase);
    }

    public bool IsCancelled => Volatile.Read(ref _cancelled) == 1;

    public ScanSnapshot Snapshot()
    {
        List<string> msgs;
        lock (_lock)
        {
            msgs = [.. _messages];
        }

        return new ScanSnapshot(
            (ulong)Math.Max(0, Volatile.Read(ref _files)),
            (ulong)Math.Max(0, Volatile.Read(ref _dirs)),
            (ulong)Math.Max(0, Volatile.Read(ref _bytes)),
            (ulong)Math.Max(0, Volatile.Read(ref _errors)),
            Volatile.Read(ref _finished) == 1,
            Volatile.Read(ref _cancelled) == 1,
            Volatile.Read(ref _phase),
            msgs
        );
    }
}
