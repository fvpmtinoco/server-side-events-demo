using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Orders.Realtime.Api;

public class OrderStatusHub
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, Channel<OrderStatus>>> _subscribers = new();

    public (string subscriberId, ChannelReader<OrderStatus> reader) Subscribe(string subscriberId, string fileId)
    {
        var channel = Channel.CreateBounded<OrderStatus>(10);

        var fileDic = new ConcurrentDictionary<string, Channel<OrderStatus>>();
        fileDic[fileId] = channel;

        _subscribers[subscriberId] = fileDic;

        return (subscriberId, channel.Reader);
    }

    public void Unsubscribe(string subscriberId, string orderId)
    {
        if (_subscribers.TryGetValue(subscriberId, out var fileDic))
        {
            if (fileDic.TryRemove(orderId, out var channel))
            {
                channel.Writer.TryComplete();
            }

            if (fileDic.IsEmpty)
            {
                _subscribers.TryRemove(subscriberId, out _);
            }
        }
    }

    public void Publish(string fileId, OrderStatus status)
    {
        _subscribers.Where(sub => sub.Value.ContainsKey(fileId)).ToList().ForEach(sub =>
        {
            if (sub.Value.TryGetValue(fileId, out var channel))
            {
                channel.Writer.TryWrite(status);
            }
        });
    }
}
