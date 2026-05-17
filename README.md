# Orders Realtime — SSE Demo

A small playground built to try out the native **Server-Sent Events** support that shipped with **.NET 10** (`TypedResults.ServerSentEvents`). The scenario is order tracking: a client opens a long-lived HTTP connection and receives status updates as they happen.

Reference: [What's new in ASP.NET Core in .NET 10 — Server-Sent Events](https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-10.0?view=aspnetcore-9.0).

## Structure

```
server-sent-events/
├── Orders.Realtime.Api/         # ASP.NET Core 10 minimal API
│   ├── Program.cs               # Host setup, CORS, Kestrel limits, endpoint mapping
│   ├── OrdersStream.cs          # The 3 endpoints (stream, patch status, get status)
│   ├── OrderStatusHub.cs        # In-memory pub/sub over System.Threading.Channels
│   ├── OrderStatus.cs           # Status enum (Pending / Processing / Ok / ...)
│   └── Dockerfile
├── ui/
│   └── index.html               # Static page using the browser EventSource API
├── docker-compose.yml
└── Sse.Demo.slnx
```

### Endpoints

| Method | Route                              | Purpose                                                |
| ------ | ---------------------------------- | ------------------------------------------------------ |
| GET    | `/orders/{orderId}/status/stream`  | Subscribe via SSE to status changes for a given order  |
| PATCH  | `/orders/{orderId}/status`         | Publish a new status to all subscribers of that order  |
| GET    | `/orders/{orderId}/status`         | Fallback short-poll endpoint (returns hard-coded value)|

### Running locally

```bash
docker compose up --build
```

API listens on `https://localhost:7176`. Open `ui/index.html` in a browser to consume the stream.

## Current Issues

This is a learning project — it is **not** production-ready. Known gaps:

- **Single-instance only.** `OrderStatusHub` stores subscribers in an in-process `ConcurrentDictionary`. Horizontal scaling breaks the flow (see next section).
- **Subscribe overwrites state.** `OrderStatusHub.Subscribe` rebuilds the inner dictionary on every call, so a single subscriber tracking multiple orders loses earlier subscriptions.
- **UI points at a non-existent endpoint.** `ui/index.html` connects to `/orders/realtime`; the API only exposes `/orders/{orderId}/status/stream`.
- **No persistence.** `PATCH /status` only fans out in memory. The `GET /status` fallback returns a hard-coded `Processing`.
- **Bounded channel silently drops events.** `Channel.CreateBounded<OrderStatus>(10)` + `TryWrite` means a slow consumer loses messages with no signal.
- **No `Last-Event-ID` replay.** A disconnect/reconnect cycle misses anything published in between.
- **Hard 5-minute timeout.** Long-lived legitimate subscribers get cut off.
- **No auth, no rate limiting, no health checks.** Kestrel is also capped at 50 concurrent connections for demo purposes.

## What Production Needs — Redis Pub/Sub

The biggest gap is that the in-memory hub only works on **one instance**. Once the API is behind a load balancer with multiple replicas, the following happens:

```
                 ┌──────────────┐
   Consumer ───► │  Instance A  │   GET /orders/123/status/stream   (subscribed here)
                 └──────────────┘
                 ┌──────────────┐
   Producer ───► │  Instance B  │   PATCH /orders/123/status        (published here)
                 └──────────────┘
```

Instance B has no idea that Instance A has a subscriber for order `123`, so the consumer never receives the update.

The fix is to swap the in-process hub for a **Redis Pub/Sub** backplane:

- `PATCH /orders/{orderId}/status` publishes the new status to a Redis channel (e.g. `orders:{orderId}:status`).
- Every API instance subscribes to that channel pattern and forwards messages to any local SSE subscribers it owns.
- Redis handles the broadcast across instances, so it does not matter which instance receives the PATCH or which instance holds the SSE connection.

Additional production changes worth doing alongside the Redis work:

- Persist status to a database before publishing (source of truth for the short-poll fallback and reconnection replay).
- Implement `Last-Event-ID` to replay missed events from persistence on reconnect.
- Add authentication/authorization on both the stream and the PATCH endpoint.
- Add structured logging, metrics, and health checks.
- Configure proxy/load-balancer to disable response buffering and allow long-lived connections.
- Raise (or remove) the artificial Kestrel connection limits and the 5-minute hard timeout, replacing them with a heartbeat/keep-alive comment frame.
- Fix the `Subscribe` overwrite bug and switch the channel write strategy so backpressure is observable instead of silent.

Sources:
- [What's new in ASP.NET Core in .NET 10 | Microsoft Learn](https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-10.0?view=aspnetcore-9.0)
- [TypedResults.ServerSentEvents Method | Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.http.typedresults.serversentevents?view=aspnetcore-10.0)
