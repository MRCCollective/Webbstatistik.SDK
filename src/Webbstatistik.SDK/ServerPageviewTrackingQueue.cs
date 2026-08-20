using System.Threading.Channels;
using System.Diagnostics.CodeAnalysis;

namespace Webbstatistik.SDK;

internal sealed class ServerPageviewTrackingQueue
{
    private readonly Channel<ServerPageviewTrackingEvent> _channel;

    public ServerPageviewTrackingQueue(ServerPageviewTrackingOptions options)
    {
        var capacity = Math.Max(1, options.QueueCapacity);
        _channel = Channel.CreateBounded<ServerPageviewTrackingEvent>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public bool TryEnqueue(ServerPageviewTrackingEvent trackingEvent)
    {
        return _channel.Writer.TryWrite(trackingEvent);
    }

    public ValueTask<ServerPageviewTrackingEvent> ReadAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAsync(cancellationToken);
    }

    public bool TryDequeue([MaybeNullWhen(false)] out ServerPageviewTrackingEvent trackingEvent)
    {
        return _channel.Reader.TryRead(out trackingEvent);
    }

    public bool TryRequeue(IReadOnlyList<ServerPageviewTrackingEvent> trackingEvents)
    {
        var success = true;

        for (var i = 0; i < trackingEvents.Count; i++)
        {
            if (!_channel.Writer.TryWrite(trackingEvents[i]))
            {
                success = false;
            }
        }

        return success;
    }
}
