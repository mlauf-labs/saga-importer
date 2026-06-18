using System.Threading;
using System.Threading.Channels;
using SagaImporter.Models;

namespace SagaImporter.Services;

/// <summary>
/// A concurrent import queue with a configurable degree of parallelism
/// (<see cref="AppSettings.MaxConcurrentImports"/>). De-duplicates files that are already
/// queued or in flight, supports pause/resume, and processes each item through the
/// <see cref="ImportPipeline"/>. Several files can be uploaded/awaited at once to drain a
/// backlog faster; it idles on an async channel when empty.
/// </summary>
public sealed class ImportQueue : IDisposable
{
    private readonly ImportPipeline _pipeline;
    private readonly LogService _log;
    private readonly Func<(AppSettings Settings, string Token)> _configProvider;

    private readonly Channel<ImportItem> _channel =
        Channel.CreateUnbounded<ImportItem>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false,
        });

    private readonly HashSet<ImportItem> _known = new();
    private readonly Lock _knownGate = new();
    private readonly ManualResetEventSlim _resumeGate = new(initialState: true);

    private CancellationTokenSource? _cts;
    private Task[]? _workers;

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

    public int Imported => Volatile.Read(ref _imported);

    public int Failed => Volatile.Read(ref _failed);

    public int Skipped => Volatile.Read(ref _skipped);

    private int _imported;
    private int _failed;
    private int _skipped;

    public void Start()
    {
        if (_workers is not null)
        {
            return;
        }

        int concurrency = Math.Clamp(_configProvider().Settings.MaxConcurrentImports, 1, 16);
        _cts = new CancellationTokenSource();
        CancellationToken ct = _cts.Token;
        _log.Info($"Import queue started with {concurrency} concurrent worker(s).");
        _workers = new Task[concurrency];
        for (int i = 0; i < concurrency; i++)
        {
            _workers[i] = Task.Run(() => RunAsync(ct));
        }
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
                Interlocked.Increment(ref _imported);
                break;
            case ImportOutcome.Failed:
                Interlocked.Increment(ref _failed);
                break;
            case ImportOutcome.Skipped:
                Interlocked.Increment(ref _skipped);
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
            if (_workers is { Length: > 0 } workers)
            {
                Task.WaitAll(workers, TimeSpan.FromSeconds(5));
            }
        }
        catch (Exception ex) when (ex is AggregateException or OperationCanceledException)
        {
            // Ignore shutdown races.
        }
        finally
        {
            _workers = null;
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
