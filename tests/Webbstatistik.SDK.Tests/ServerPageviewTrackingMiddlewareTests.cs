using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Webbstatistik.SDK.Tests;

public class ServerPageviewTrackingMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_TracksSuccessfulHtmlGetRequests()
    {
        var client = new FakeServerPageviewTrackingClient();
        var middleware = new ServerPageviewTrackingMiddleware(
            async context =>
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.WriteAsync("<html></html>");
            },
            NullLogger<ServerPageviewTrackingMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("example.test");
        context.Request.Path = "/docs/get-started";
        context.TraceIdentifier = "req-1001";
        context.Request.Headers.Referer = "https://example.test/";
        context.Request.Headers.UserAgent = "ServerMiddlewareTests/1.0";
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.10");

        await middleware.InvokeAsync(
            context,
            client,
            Options.Create(new ServerPageviewTrackingOptions
            {
                WebbstatistikBaseUrl = "https://analytics.example.test",
                SiteKey = "dds_123",
                WebsiteId = Guid.NewGuid().ToString()
            }));

        var trackedEvent = Assert.Single(client.Events);
        Assert.Equal("https://example.test/docs/get-started", trackedEvent.Url);
        Assert.Equal("req-1001", trackedEvent.RequestId);
        Assert.Equal("203.0.113.10", trackedEvent.ClientIp);
    }

    [Fact]
    public async Task InvokeAsync_SkipsExcludedPaths()
    {
        var client = new FakeServerPageviewTrackingClient();
        var middleware = new ServerPageviewTrackingMiddleware(
            context =>
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.ContentType = "text/html";
                return Task.CompletedTask;
            },
            NullLogger<ServerPageviewTrackingMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("example.test");
        context.Request.Path = "/api/health";

        await middleware.InvokeAsync(
            context,
            client,
            Options.Create(new ServerPageviewTrackingOptions
            {
                WebbstatistikBaseUrl = "https://analytics.example.test",
                SiteKey = "dds_123",
                WebsiteId = Guid.NewGuid().ToString()
            }));

        Assert.Empty(client.Events);
    }

    private sealed class FakeServerPageviewTrackingClient : IServerPageviewTrackingClient
    {
        public List<ServerPageviewTrackingEvent> Events { get; } = [];

        public Task SendAsync(ServerPageviewTrackingEvent trackingEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(trackingEvent);
            return Task.CompletedTask;
        }
    }
}
