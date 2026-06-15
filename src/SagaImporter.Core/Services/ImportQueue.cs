using System.Threading;
using System.Threading.Channels;
using SagaImporter.Models;

namespace SagaImporter.Services;

/// <summary>
/// A bounded-concurrency (single worker) import queue. De-duplicates files that are
/// already queued or in flight, supports pause/resume, and processes each item through
/// the <see cref="ImportPipeline"/>. Keeps the resource footprint low by doing one file
/// at a time and idling on an async channel when empty.
/// </summary>
public sealed class ImportQueue : IDisposable
{
    private readonly ImportPipeline _pipeline;
    private readonly LogService _log;
    private readonly Func<(AppSettings Settings, string Token)> _configProvider;

    private readonly Channel<ImportItem> _channel =
        Channel.CreateUnbounded<ImportItem>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

    private readonly HashSet<ImportItem> _known = new();
    private readonly Lock _knownGate = new();
    private readonly ManualResetEventSlim _resumeGate = new(initialState: true);

    private CancellationTokenSource? _cts;
    private Task? _worker;

    public ImportQueue(
        ImportPipeline pipeline,
        LogService log,
        Func<(AppSettings Settings, string Token)> configProvider)
    {
        _pipeline = pipeline;
        _log = log;
        _configProvider = configProvider;
    }

    public event Action? StateChanged;

    public event Action<ImportResult>? ItemCompleted;

    public bool IsPaused { get; private set; }

    public int QueueCount
    {
        get
        {
            lock (_knownGate)
            {
                return _known.Count;
            }
        }
    }

    public int Imported { get; private set; }

    public int Failed { get; private set; }

    public int Skipped { get; private set; }

    public void Start()
    {
        if (_worker is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _worker = Task.Run(() => RunAsync(_cts.Token));
    }

    public void Enqueue(string path)
    {
        var item = new ImportItem(path);
        lock (_knownGate)
        {
            if (!_known.Add(item))
            {
                return; // Already queued or in flight.
            }
        }

        _channel.Writer.TryWrite(item);
        StateChanged?.Invoke();
    }

    public void Pause()
    {
        if (IsPaused)
        {
            return;
        }

        IsPaused = true;
        _resumeGate.Reset();
        _log.Info("Import paused.");
        StateChanged?.Invoke();
    }

    public void Resume()
    {
        if (!IsPaused)
        {
            return;
        }

        IsPaused = false;
        _resumeGate.Set();
        _log.Info("Import resumed.");
        StateChanged?.Invoke();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await foreach (ImportItem item in _channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    _resumeGate.Wait(ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                await ProcessOneAsync(item, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    private async Task ProcessOneAsync(ImportItem item, CancellationToken ct)
    {
        ImportResult result;
        try
        {
            (AppSettings settings, string token) = _configProvider();
            result = await _pipeline.ProcessAsync(item.FullPath, settings, token, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error($"Unexpected error importing {item.FileName}: {ex.Message}");
            result = new ImportResult(item.FileName, ImportOutcome.Failed, Message: ex.Message);
        }
        finally
        {
            lock (_knownGate)
            {
                _known.Remove(item);
            }
        }

        switch (result.Outcome)
        {
            case ImportOutcome.Imported:
                Imported++;
                break;
            case ImportOutcome.Failed:
                Failed++;
                break;
            case ImportOutcome.Skipped:
                Skipped++;
                break;
        }

        ItemCompleted?.Invoke(result);
        StateChanged?.Invoke();
    }

    public void Stop()
    {
        try
        {
            _cts?.Cancel();
            _resumeGate.Set();
            _worker?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex) when (ex is AggregateException or OperationCanceledException)
        {
            // Ignore shutdown races.
        }
        finally
        {
            _worker = null;
            _cts?.Dispose();
            _cts = null;
        }
    }

    public void Dispose()
    {
        Stop();
        _resumeGate.Dispose();
    }
}
