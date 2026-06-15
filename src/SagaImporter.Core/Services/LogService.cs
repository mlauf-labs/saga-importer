using System.IO;
using System.Threading;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace SagaImporter.Services;

public sealed record LogEntry(DateTimeOffset Timestamp, LogEventLevel Level, string Message)
{
    public string Display =>
        $"{Timestamp.LocalDateTime:yyyy-MM-dd HH:mm:ss} [{Level.ToString()[..3].ToUpperInvariant()}] {Message}";
}

/// <summary>
/// Central logging. Writes to a rolling daily file and raises <see cref="EntryAdded"/>
/// for each entry so consumers (UI, journald) can display a live log. Also keeps a
/// bounded in-memory buffer for late subscribers.
/// </summary>
public sealed class LogService : IDisposable
{
    private const int MaxBufferedEntries = 1000;

    private readonly Logger _logger;

    // C# 13 / .NET 9 dedicated synchronization primitive (faster than locking on object).
    private readonly Lock _gate = new();
    private readonly LinkedList<LogEntry> _buffer = new();

    public LogService(string? logDirectory = null)
    {
        string dir = logDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SagaImporter",
            "logs");
        Directory.CreateDirectory(dir);

        LogDirectory = dir;

        _logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                Path.Combine(dir, "importer-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true)
            .WriteTo.Sink(new RelaySink(Append))
            .CreateLogger();
    }

    public string LogDirectory { get; }

    /// <summary>Raised on every log entry (may be on a background thread).</summary>
    public event Action<LogEntry>? EntryAdded;

    public IReadOnlyList<LogEntry> Snapshot()
    {
        lock (_gate)
        {
            return _buffer.ToList();
        }
    }

    public void Info(string message) => _logger.Information("{Msg}", message);

    public void Warn(string message) => _logger.Warning("{Msg}", message);

    public void Error(string message) => _logger.Error("{Msg}", message);

    public void Debug(string message) => _logger.Debug("{Msg}", message);

    private void Append(LogEvent logEvent)
    {
        var entry = new LogEntry(
            logEvent.Timestamp,
            logEvent.Level,
            logEvent.RenderMessage());

        lock (_gate)
        {
            _buffer.AddLast(entry);
            while (_buffer.Count > MaxBufferedEntries)
            {
                _buffer.RemoveFirst();
            }
        }

        EntryAdded?.Invoke(entry);
    }

    public void Dispose() => _logger.Dispose();

    private sealed class RelaySink(Action<LogEvent> relay) : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => relay(logEvent);
    }
}
