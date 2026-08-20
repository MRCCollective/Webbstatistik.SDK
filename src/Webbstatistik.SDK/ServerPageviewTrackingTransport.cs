using System.Net.Http.Json;

namespace Webbstatistik.SDK;

internal interface IServerPageviewTrackingTransport
{
    Task SendBatchAsync(IReadOnlyList<ServerPageviewTrackingEvent> trackingEvents, CancellationToken cancellationToken);
}

internal sealed class ServerPageviewTrackingTransport : IServerPageviewTrackingTransport
{
    internal const string HttpClientName = "Webbstatistik.ServerPageviewTracking";
    private const string SiteKeyHeaderName = "x-webbstatistik-site-key";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ServerPageviewTrackingOptions _options;

    public ServerPageviewTrackingTransport(
        IHttpClientFactory httpClientFactory,
        ServerPageviewTrackingOptions options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
    }

    public async Task SendBatchAsync(IReadOnlyList<ServerPageviewTrackingEvent> trackingEvents, CancellationToken cancellationToken)
    {
        if (trackingEvents.Count == 0 ||
            string.IsNullOrWhiteSpace(_options.WebbstatistikBaseUrl) ||
            string.IsNullOrWhiteSpace(_options.SiteKey) ||
            string.IsNullOrWhiteSpace(_options.WebsiteId))
        {
            return;
        }

        var client = _httpClientFactory.CreateClient(HttpClientName);
        var payload = trackingEvents
            .Select(trackingEvent => new ServerPageviewRequest(
                "1.0",
                new ServerPageviewPayload(
                    _options.WebsiteId,
                    trackingEvent.Url,
                    trackingEvent.Referrer,
                    trackingEvent.RequestId,
                    trackingEvent.ClientIp,
                    trackingEvent.UserAgent,
                    trackingEvent.TimestampUtc)))
            .ToArray();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/server/pageview/batch")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add(SiteKeyHeaderName, _options.SiteKey);

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<ServerPageviewBatchResponse>(cancellationToken);
        if (body is null)
        {
            throw new InvalidOperationException("Server pageview batch response body was empty.");
        }

        if (body.Rejected > 0)
        {
            throw new InvalidOperationException(
                $"Server pageview batch rejected {body.Rejected} of {body.Received} queued pageviews.");
        }
    }

    private sealed record ServerPageviewRequest(string Version, ServerPageviewPayload Payload);

    private sealed record ServerPageviewPayload(
        string WebsiteId,
        string Url,
        string? Referrer,
        string RequestId,
        string? ClientIp,
        string? UserAgent,
        DateTime TimestampUtc);

    private sealed record ServerPageviewBatchResponse(
        int Received,
        int Accepted,
        int Rejected,
        IReadOnlyList<int> EventIds,
        IReadOnlyList<RejectedItem> RejectedItems,
        string? Cache);

    private sealed record RejectedItem(int Index, RejectedError Error);

    private sealed record RejectedError(string Code, string Message, IReadOnlyList<RejectedDetail> Errors);

    private sealed record RejectedDetail(string Field, string Message);
}
