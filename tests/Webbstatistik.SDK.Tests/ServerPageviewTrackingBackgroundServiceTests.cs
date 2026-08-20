using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Webbstatistik.SDK.Tests;

public class ServerPageviewTrackingBackgroundServiceTests
{
    [Fact]
    public async Task BackgroundService_FlushesQueuedEventsAsSingleBatch()
    {
        var options = new ServerPageviewTrackingOptions
        {
            WebbstatistikBaseUrl = "https://analytics.example.test",
            SiteKey = "dds_123",
            WebsiteId = Guid.NewGuid().ToString(),
            FlushIntervalSeconds = 1,
            MaxBatchSize = 10,
            QueueCapacity = 10
        };

        var queue = new ServerPageviewTrackingQueue(options);
        var transport = new FakeServerPageviewTrackingTransport();
        using var service = new ServerPageviewTrackingBackgroundService(
            queue,
            transport,
            options,
            NullLogger<ServerPageviewTrackingBackgroundService>.Instance);

        Assert.True(queue.TryEnqueue(new ServerPageviewTrackingEvent(
            "https://example.test/docs/a",
            "https://example.test/",
            "srv-queue-1001",
            "203.0.113.10",
            "QueueTests/1.0",
            DateTime.UtcNow)));
        Assert.True(queue.TryEnqueue(new ServerPageviewTrackingEvent(
            "https://example.test/docs/b",
            "https://example.test/",
            "srv-queue-1002",
            "203.0.113.11",
            "QueueTests/1.0",
            DateTime.UtcNow)));

        await service.StartAsync(CancellationToken.None);

        var flushedBatch = await transport.BatchTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(2, flushedBatch.Count);
        Assert.Equal("srv-queue-1001", flushedBatch[0].RequestId);
        Assert.Equal("srv-queue-1002", flushedBatch[1].RequestId);
    }

    [Fact]
    public async Task BackgroundService_DoesNotOverlapFlushes_AndStartsNextImmediatelyAfterSlowBatch()
    {
        var options = new ServerPageviewTrackingOptions
        {
            WebbstatistikBaseUrl = "https://analytics.example.test",
            SiteKey = "dds_123",
            WebsiteId = Guid.NewGuid().ToString(),
            FlushIntervalSeconds = 1,
            MaxBatchSize = 1,
            QueueCapacity = 10
        };

        var queue = new ServerPageviewTrackingQueue(options);
        var transport = new SequentialFakeServerPageviewTrackingTransport();
        using var service = new ServerPageviewTrackingBackgroundService(
            queue,
            transport,
            options,
            NullLogger<ServerPageviewTrackingBackgroundService>.Instance);

        Assert.True(queue.TryEnqueue(new ServerPageviewTrackingEvent(
            "https://example.test/docs/a",
            null,
            "srv-queue-slow-1001",
            null,
            null,
            DateTime.UtcNow)));
        Assert.True(queue.TryEnqueue(new ServerPageviewTrackingEvent(
            "https://example.test/docs/b",
            null,
            "srv-queue-slow-1002",
            null,
            null,
            DateTime.UtcNow)));

        await service.StartAsync(CancellationToken.None);

        await transport.FirstCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(transport.SecondCallStarted.Task.IsCompleted);

        await Task.Delay(TimeSpan.FromMilliseconds(1200));
        Assert.False(transport.SecondCallStarted.Task.IsCompleted);

        transport.AllowFirstCallToFinish.SetResult();
        await transport.SecondCallStarted.Task.WaitAsync(TimeSpan.FromMilliseconds(300));

        await service.StopAsync(CancellationToken.None);

        Assert.Equal(2, transport.Batches.Count);
        Assert.Single(transport.Batches[0]);
        Assert.Single(transport.Batches[1]);
        Assert.Equal("srv-queue-slow-1001", transport.Batches[0][0].RequestId);
        Assert.Equal("srv-queue-slow-1002", transport.Batches[1][0].RequestId);
    }

    private sealed class FakeServerPageviewTrackingTransport : IServerPageviewTrackingTransport
    {
        public TaskCompletionSource<IReadOnlyList<ServerPageviewTrackingEvent>> BatchTcs { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task SendBatchAsync(IReadOnlyList<ServerPageviewTrackingEvent> trackingEvents, CancellationToken cancellationToken)
        {
            BatchTcs.TrySetResult(trackingEvents.ToArray());
            return Task.CompletedTask;
        }
    }

    private sealed class SequentialFakeServerPageviewTrackingTransport : IServerPageviewTrackingTransport
    {
        private int _callCount;

        public List<IReadOnlyList<ServerPageviewTrackingEvent>> Batches { get; } = [];
        public TaskCompletionSource FirstCallStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowFirstCallToFinish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondCallStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task SendBatchAsync(IReadOnlyList<ServerPageviewTrackingEvent> trackingEvents, CancellationToken cancellationToken)
        {
            var callNumber = Interlocked.Increment(ref _callCount);
            Batches.Add(trackingEvents.ToArray());

            if (callNumber == 1)
            {
                FirstCallStarted.TrySetResult();
                await AllowFirstCallToFinish.Task.WaitAsync(cancellationToken);
                return;
            }

            if (callNumber == 2)
            {
                SecondCallStarted.TrySetResult();
            }
        }
    }
}
