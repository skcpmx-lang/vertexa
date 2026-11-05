using System.Text.Json;
using Merqo.Vertexa.Shared;

namespace Merqo.Vertexa.Core.Logging;

/// <summary>
/// Append-only JSONL logger with size-based rotation. Deliberately dependency-free and
/// never throws: logging must not be able to take the product down.
/// </summary>
public sealed class FileLogger : IAppLogger, IDisposable
{
    private readonly object _gate = new();
    private readonly string _directory;
    private readonly string _filePath;
    private readonly long _maxBytes;
    private StreamWriter? _writer;
    private long _written;
    private bool _disposed;

    public FileLogger(string directory, string fileName = "vertexa.log.jsonl", long maxBytes = 4L * 1024 * 1024)
    {
        _directory = directory;
        _maxBytes = maxBytes;
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, fileName);
    }

    public string CurrentPath => _filePath;

    public void Log(LogLevel level, string area, string message, Exception? error = null, IReadOnlyDictionary<string, object?>? data = null)
    {
        try
        {
            var entry = new Dictionary<string, object?>
            {
                ["ts"] = DateTimeOffset.UtcNow.ToString("O"),
                ["level"] = level.ToString(),
                ["area"] = area,
                ["msg"] = message,
                ["pid"] = Environment.ProcessId,
            };
            if (error is not null)
            {
                entry["ex"] = error.GetType().FullName;
                entry["exMsg"] = error.Message;
                entry["stack"] = error.StackTrace;
            }
            if (data is not null && data.Count > 0) entry["data"] = data;

            var line = JsonSerializer.Serialize(entry);
            lock (_gate)
            {
                if (_disposed) return;
                EnsureWriter();
                _writer!.WriteLine(line);
                _writer.Flush();
                _written += line.Length;
                if (_written >= _maxBytes) Rotate();
            }
        }
        catch
        {
            // Logging must never throw into caller code.
        }
    }

    private void EnsureWriter()
    {
        _writer ??= new StreamWriter(new FileStream(_filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = false };
    }

    private void Rotate()
    {
        try
        {
            _writer?.Dispose();
            _writer = null;
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var rotated = Path.Combine(_directory, $"vertexa-{stamp}.log.jsonl");
            if (File.Exists(_filePath)) File.Move(_filePath, rotated, overwrite: true);
            _written = 0;
            PruneOldLogs();
        }
        catch
        {
            // ignore rotation errors
        }
    }

    private void PruneOldLogs()
    {
        try
        {
            var files = new DirectoryInfo(_directory).GetFiles("vertexa-*.log.jsonl")
                .OrderByDescending(f => f.LastWriteTimeUtc).Skip(5);
            foreach (var f in files) f.Delete();
        }
        catch
        {
            // ignore
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _writer?.Dispose();
            _writer = null;
        }
    }
}
