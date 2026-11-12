using System.Text;

namespace VelaRay.Core.Runtime;

/// <summary>
/// Thread-safe, bounded runtime log storage. Producers never perform disk I/O.
/// A single background task checks for a sufficiently large batch once per minute.
/// Sub-threshold entries are intentionally not persisted automatically.
/// </summary>
public sealed class RuntimeLogStore : IDisposable
{
    public const int MaxVisibleLines = 1_000;
    public const int DefaultMaxPendingLines = 2_048;
    public const int DefaultMinimumWriteBatchLines = 1_000;
    public const int MaxEntryCharacters = 16_384;
    public const long DefaultMaxFileBytes = 10L * 1024 * 1024;
    public const int DefaultArchiveFiles = 5;
    public static readonly TimeSpan DefaultFlushInterval = TimeSpan.FromMinutes(1);

    private readonly object _queueLock = new();
    private readonly object _fileLock = new();
    private readonly Queue<string> _visible = new();
    private readonly Queue<string> _pending = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _writerTask;
    private readonly TimeSpan _flushInterval;
    private readonly long _maxFileBytes;
    private readonly int _maxArchiveFiles;
    private readonly int _maxPendingLines;
    private readonly int _minimumWriteBatchLines;
    private StreamWriter? _writer;
    private long _currentFileBytes;
    private long _droppedPendingLines;
    private bool _fileLoggingFailed;
    private string? _fileWriteError;
    private bool _disposed;

    public RuntimeLogStore(string filePath, long maxFileBytes = DefaultMaxFileBytes,
        int maxArchiveFiles = DefaultArchiveFiles, TimeSpan? flushInterval = null,
        int maxPendingLines = DefaultMaxPendingLines,
        int minimumWriteBatchLines = DefaultMinimumWriteBatchLines)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFileBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(maxArchiveFiles);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPendingLines);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumWriteBatchLines);
        if (minimumWriteBatchLines > maxPendingLines)
            throw new ArgumentException("Minimum batch cannot exceed the bounded pending buffer.", nameof(minimumWriteBatchLines));
        FilePath = Path.GetFullPath(filePath);
        _maxFileBytes = maxFileBytes;
        _maxArchiveFiles = maxArchiveFiles;
        _maxPendingLines = maxPendingLines;
        _minimumWriteBatchLines = minimumWriteBatchLines;
        _flushInterval = flushInterval ?? DefaultFlushInterval;
        if (_flushInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(flushInterval));
        _writerTask = Task.Run(WriterLoopAsync);
    }

    public string FilePath { get; }
    public string? FileWriteError
    {
        get { lock (_queueLock) return _fileWriteError; }
    }

    public int VisibleCount
    {
        get { lock (_queueLock) return _visible.Count; }
    }

    public int PendingCount
    {
        get { lock (_queueLock) return _pending.Count; }
    }

    public void Append(string line)
    {
        // Cap a pathological core output line, not just the number of lines.
        // This bounds both the UI history and the pending disk-write queue.
        line ??= string.Empty;
        if (line.Length > MaxEntryCharacters)
            line = line[..MaxEntryCharacters] + " [truncated]";
        var entry = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] {line}";
        lock (_queueLock)
        {
            if (_disposed) return;
            AddVisible(entry);
            if (_fileLoggingFailed) return;
            if (_pending.Count >= _maxPendingLines)
            {
                _pending.Dequeue();
                _droppedPendingLines++;
                if (_droppedPendingLines == 1)
                    AddVisible($"[{DateTimeOffset.Now:HH:mm:ss}] WARNING: disk log buffer full; oldest pending entries are being dropped.");
            }
            _pending.Enqueue(entry);
        }
    }

    public string[] SnapshotVisible()
    {
        lock (_queueLock) return _visible.ToArray();
    }

    /// <summary>Clears only the visible history, never the pending or persisted entries.</summary>
    public void ClearVisible()
    {
        lock (_queueLock) _visible.Clear();
    }

    /// <summary>
    /// Called by the background timer. No disk I/O occurs while fewer than
    /// the minimum number of entries are pending.
    /// </summary>
    private void FlushIfThresholdReached()
    {
        lock (_fileLock) FlushPendingLocked(includePartialBatch: false);
    }

    /// <summary>
    /// Explicit user-requested export is the only operation that writes a partial batch.
    /// Rotated archives remain on disk.
    /// </summary>
    public string ExportCurrentFile()
    {
        lock (_fileLock)
        {
            lock (_queueLock) ObjectDisposedException.ThrowIf(_disposed, this);
            FlushPendingLocked(includePartialBatch: true);
            if (FileWriteError is { } error)
                throw new IOException("Runtime logs could not be saved: " + error);
            _writer?.Flush();
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            if (!File.Exists(FilePath)) File.WriteAllText(FilePath, string.Empty);
            var destination = Path.Combine(Path.GetDirectoryName(FilePath)!,
                $"app-export-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.log");
            File.Copy(FilePath, destination);
            return destination;
        }
    }

    private async Task WriterLoopAsync()
    {
        using var timer = new PeriodicTimer(_flushInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false))
                FlushIfThresholdReached();
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception ex) { ReportWriteFailure(ex); }
        finally
        {
            // Persist a full batch on graceful shutdown; deliberately discard
            // fewer than the threshold, matching the user's low-I/O policy.
            lock (_fileLock)
            {
                FlushPendingLocked(includePartialBatch: false);
                try { _writer?.Dispose(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    ReportWriteFailure(ex);
                }
                _writer = null;
            }
        }
    }

    private void FlushPendingLocked(bool includePartialBatch)
    {
        string[] batch;
        long dropped;
        lock (_queueLock)
        {
            if (_fileLoggingFailed || _pending.Count == 0 ||
                (!includePartialBatch && _pending.Count < _minimumWriteBatchLines)) return;
            batch = _pending.ToArray();
            _pending.Clear();
            dropped = _droppedPendingLines;
            _droppedPendingLines = 0;
        }
        try
        {
            if (dropped != 0)
                WriteEntry($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] WARNING: {dropped} entries dropped because the disk log queue was full.");
            foreach (var entry in batch) WriteEntry(entry);
            // Exactly one buffered flush per batch, rather than AutoFlush on each entry.
            _writer?.Flush();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            ReportWriteFailure(ex);
        }
    }

    private void ReportWriteFailure(Exception ex)
    {
        lock (_queueLock)
        {
            if (_fileLoggingFailed) return;
            _fileLoggingFailed = true;
            _fileWriteError = ex.Message;
            _pending.Clear();
            AddVisible($"[{DateTimeOffset.Now:HH:mm:ss}] ERROR: disk logging stopped: {ex.Message}");
        }
        try { _writer?.Dispose(); } catch { /* Best effort during a disk failure. */ }
        _writer = null;
    }

    private void AddVisible(string entry)
    {
        if (_visible.Count == MaxVisibleLines) _visible.Dequeue();
        _visible.Enqueue(entry);
    }

    private void WriteEntry(string entry)
    {
        if (_writer is null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            _currentFileBytes = File.Exists(FilePath) ? new FileInfo(FilePath).Length : 0;
            if (_currentFileBytes >= _maxFileBytes) RotateArchives();
            OpenWriter();
        }
        var byteCount = Encoding.UTF8.GetByteCount(entry) + Encoding.UTF8.GetByteCount(Environment.NewLine);
        if (_currentFileBytes > 0 && _currentFileBytes + byteCount > _maxFileBytes)
        {
            _writer!.Dispose();
            _writer = null;
            RotateArchives();
            OpenWriter();
        }
        _writer!.WriteLine(entry);
        _currentFileBytes += byteCount;
    }

    private void OpenWriter()
    {
        _writer = new StreamWriter(
            new FileStream(FilePath, FileMode.Append, FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete, bufferSize: 64 * 1024),
            new UTF8Encoding(false), bufferSize: 64 * 1024)
        { AutoFlush = false };
    }

    private string ArchivePath(int index) => FilePath + "." + index;

    private void RotateArchives()
    {
        if (_maxArchiveFiles == 0)
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
        }
        else
        {
            if (File.Exists(ArchivePath(_maxArchiveFiles))) File.Delete(ArchivePath(_maxArchiveFiles));
            for (var i = _maxArchiveFiles - 1; i >= 1; i--)
                if (File.Exists(ArchivePath(i))) File.Move(ArchivePath(i), ArchivePath(i + 1));
            if (File.Exists(FilePath)) File.Move(FilePath, ArchivePath(1));
        }
        _currentFileBytes = 0;
    }

    public void Dispose()
    {
        lock (_queueLock)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _stop.Cancel();
        // Normal shutdown persists only full batches; small buffers are deliberately
        // discarded. Abrupt OS termination can also lose a full unwritten batch.
        _writerTask.GetAwaiter().GetResult();
        _stop.Dispose();
    }
}
