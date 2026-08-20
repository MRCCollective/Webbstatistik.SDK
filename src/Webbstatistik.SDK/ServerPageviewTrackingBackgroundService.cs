namespace Webbstatistik.SDK;

internal sealed class ServerPageviewTrackingBackgroundService : BackgroundService
{
    private readonly ServerPageviewTrackingQueue _queue;
    private readonly IServerPageviewTrackingTransport _transport;
    private readonly ILogger<ServerPageviewTrackingBackgroundService> _logger;
    private readonly TimeSpan _flushInterval;
    private readonly TimeSpan _failureDelay;
    private readonly int _maxBatchSize;

    public ServerPageviewTrackingBackgroundService(
        ServerPageviewTrackingQueue queue,
        IServerPageviewTrackingTransport transport,
        ServerPageviewTrackingOptions options,
        ILogger<ServerPageviewTrackingBackgroundService> logger)
    {
        _queue = queue;
        _transport = transport;
        _logger = logger;
        _maxBatchSize = Math.Max(1, options.MaxBatchSize);
        _flushInterval = TimeSpan.FromSeconds(Math.Max(1, options.FlushIntervalSeconds));
        _failureDelay = TimeSpan.FromSeconds(Math.Max(1, options.FlushIntervalSeconds));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var skipWaitForNextBatch = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            ServerPageviewTrackingEvent first;
            try
            {
                first = await _queue.ReadAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            var batch = new List<ServerPageviewTrackingEvent>(_maxBatchSize)
            {
                first
            };

            DrainAvailable(batch);

            if (!skipWaitForNextBatch && batch.Count < _maxBatchSize)
            {
                var startedUtc = DateTime.UtcNow;
                while (batch.Count < _maxBatchSize)
                {
                    var remaining = _flushInterval - (DateTime.UtcNow - startedUtc);
                    if (remaining <= TimeSpan.Zero)
                    {
                        break;
                    }

                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    timeoutCts.CancelAfter(remaining);

                    try
                    {
                        batch.Add(await _queue.ReadAsync(timeoutCts.Token));
                        DrainAvailable(batch);
                    }
                    catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                }
            }

            try
            {
                var flushStartedUtc = DateTime.UtcNow;
                await _transport.SendBatchAsync(batch, stoppingToken);
                skipWaitForNextBatch = DateTime.UtcNow - flushStartedUtc >= _flushInterval;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Failed to flush {Count} queued server pageviews. Requeueing for a later retry.",
                    batch.Count);

                if (!_queue.TryRequeue(batch))
                {
                    _logger.LogWarning(
                        "One or more queued server pageviews were dropped after a failed flush because the retry queue was full.");
                }

                try
                {
                    await Task.Delay(_failureDelay, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private void DrainAvailable(ICollection<ServerPageviewTrackingEvent> batch)
    {
        while (batch.Count < _maxBatchSize && _queue.TryDequeue(out var queuedEvent))
        {
            batch.Add(queuedEvent);
        }
    }
}
