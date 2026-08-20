namespace Webbstatistik.SDK;

public interface IServerPageviewTrackingClient
{
    Task SendAsync(ServerPageviewTrackingEvent trackingEvent, CancellationToken cancellationToken = default);
}

internal sealed class ServerPageviewTrackingClient : IServerPageviewTrackingClient
{
    private readonly ServerPageviewTrackingQueue _queue;
    private readonly ServerPageviewTrackingOptions _options;
    private readonly ILogger<ServerPageviewTrackingClient> _logger;

    public ServerPageviewTrackingClient(
        ServerPageviewTrackingQueue queue,
        ServerPageviewTrackingOptions options,
        ILogger<ServerPageviewTrackingClient> logger)
    {
        _queue = queue;
        _options = options;
        _logger = logger;
    }

    public Task SendAsync(ServerPageviewTrackingEvent trackingEvent, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.WebbstatistikBaseUrl) ||
            string.IsNullOrWhiteSpace(_options.SiteKey) ||
            string.IsNullOrWhiteSpace(_options.WebsiteId))
        {
            return Task.CompletedTask;
        }

        if (_queue.TryEnqueue(trackingEvent))
        {
            return Task.CompletedTask;
        }

        _logger.LogWarning(
            "Dropping queued server pageview for {Url} because the in-memory queue is full.",
            trackingEvent.Url);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}

public sealed record ServerPageviewTrackingEvent(
    string Url,
    string? Referrer,
    string RequestId,
    string? ClientIp,
    string? UserAgent,
    DateTime TimestampUtc);
