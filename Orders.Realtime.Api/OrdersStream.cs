using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Orders.Realtime.Api;

public static class OrdersStream
{
    public static void MapOrdersEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/orders/{orderId}/status/stream", async (string orderId, OrderStatusHub hub, CancellationToken ct) =>
        {
            var subscriberId = Guid.CreateVersion7().ToString();

            var subscription = hub.Subscribe(subscriberId, orderId);

            return TypedResults.ServerSentEvents(ReadOrderStatus(subscription.subscriberId, subscription.reader, hub, orderId, ct));
        });

        app.MapPatch("/orders/{orderId}/status", (string orderId, OrderStatus newStatus, OrderStatusHub hub) =>
        {
            // In a real application, we should persist the new status to a database before publishing it to the subscribers.

            hub.Publish(orderId, newStatus);
            return Results.NoContent();
        });

        app.MapGet("/orders/{orderId}/status", async (string orderId) =>
        {
            // In a real application, we should read the current status from a database and return it to the client.
            // This would be the short polling endpoint for clients that don't support Server-Sent Events or WebSockets.
            return Results.Ok(OrderStatus.Processing);
        });
    }

    private static async IAsyncEnumerable<SseItem<OrderStatus>> ReadOrderStatus(
        string subscriberId,
        ChannelReader<OrderStatus> reader,
        OrderStatusHub hub,
        string orderId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // Combine client disconnect token with a hard timeout
        // to close connections opened to long without a status update
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            ct,
            new CancellationTokenSource(TimeSpan.FromMinutes(5)).Token);

        try
        {
            await foreach (var status in reader.ReadAllAsync(ct))
            {
                yield return new SseItem<OrderStatus>(status, eventType: "orders")
                {
                    EventId = subscriberId + orderId
                };

                if (status == OrderStatus.Ok)
                    yield break;
            }
        }
        finally
        {
            hub.Unsubscribe(subscriberId, orderId);
        }
    }
}