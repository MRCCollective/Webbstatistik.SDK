using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Options;

namespace Webbstatistik.SDK;

public sealed class ServerPageviewTrackingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ServerPageviewTrackingMiddleware> _logger;

    public ServerPageviewTrackingMiddleware(RequestDelegate next, ILogger<ServerPageviewTrackingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IServerPageviewTrackingClient trackingClient,
        IOptions<ServerPageviewTrackingOptions> optionsAccessor)
    {
        await _next(context);

        var options = optionsAccessor.Value;
        if (!ShouldTrack(context, options))
        {
            return;
        }

        var trackingEvent = new ServerPageviewTrackingEvent(
            context.Request.GetDisplayUrl(),
            context.Request.Headers.Referer.ToString(),
            context.TraceIdentifier,
            TrackingRequestClientIpResolver.ResolveClientIp(context)?.ToString(),
            context.Request.Headers.UserAgent.ToString(),
            DateTime.UtcNow);

        try
        {
            await trackingClient.SendAsync(trackingEvent);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to send server pageview for {Path}.", context.Request.Path);
        }
    }

    internal static bool ShouldTrack(HttpContext context, ServerPageviewTrackingOptions options)
    {
        if (!HttpMethods.IsGet(context.Request.Method))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(options.WebbstatistikBaseUrl) ||
            string.IsNullOrWhiteSpace(options.SiteKey) ||
            string.IsNullOrWhiteSpace(options.WebsiteId))
        {
            return false;
        }

        if (context.Response.StatusCode != StatusCodes.Status200OK)
        {
            return false;
        }

        if (Path.HasExtension(context.Request.Path))
        {
            return false;
        }

        if (options.ExcludedPathPrefixes.Any(prefix =>
                context.Request.Path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var contentType = context.Response.ContentType;
        return !string.IsNullOrWhiteSpace(contentType) &&
               contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase);
    }
}
