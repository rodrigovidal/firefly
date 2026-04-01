# Real-Time Communication

Fire supports WebSockets, Server-Sent Events (SSE), and streaming JSON responses.

## WebSockets

Use `WS.handler` to create a WebSocket endpoint:

```fsharp
let chat =
    WS.handler (fun conn req -> task {
        while conn.IsOpen do
            match! conn.Receive() with
            | WsText msg ->
                do! conn.Send $"Echo: {msg}"
            | WsBinary data ->
                do! conn.SendBytes data
            | WsClose ->
                ()
    })

Route.start
|> Route.get "/ws/chat" chat
```

### WsConn API

| Method | Description |
|--------|-------------|
| `conn.Send(text)` | Send a text message |
| `conn.SendBytes(data)` | Send binary data |
| `conn.Receive()` | Receive the next message (`WsText`, `WsBinary`, or `WsClose`) |
| `conn.Close(?status, ?reason)` | Close the connection |
| `conn.IsOpen` | Check if the connection is still open |
| `conn.CancellationToken` | Token cancelled when the client disconnects |

The handler automatically accepts the WebSocket upgrade, manages the connection lifecycle, and closes the socket when the function returns. Non-WebSocket requests receive a 400 response.

## Server-Sent Events (SSE)

### Handler-Driven SSE

Push events from within the handler function:

```fsharp
let countdown =
    Sse.handler (fun writer req -> task {
        for i in 10..-1..1 do
            do! writer.Event("countdown", string i)
            do! System.Threading.Tasks.Task.Delay(1000)
        do! writer.Data("Done!")
    })

Route.start
|> Route.get "/events/countdown" countdown
```

The `SseWriter` provides:

| Method | Description |
|--------|-------------|
| `writer.Event(event, data)` | Send a named event |
| `writer.Data(data)` | Send a data-only message |

### Channel-Driven SSE

Stream events from a `ChannelReader`:

```fsharp
open System.Threading.Channels

let channel = Channel.CreateUnbounded<SseEvent>()

// Producer (e.g., background service)
let sendEvent () = task {
    do! channel.Writer.WriteAsync({ Event = "update"; Data = """{"status":"ok"}""" })
}

Route.start
|> Route.get "/events/updates" (Sse.stream channel.Reader)
```

Each message is consumed by one client only.

### Broadcast SSE

Multi-client broadcast where every connected client receives every event:

```fsharp
let hub = SseBroadcast()

// Broadcast to all connected clients
let notify (req: Request) = task {
    do! hub.Send({ Event = "notification"; Data = "Hello everyone!" })
    return Response.ok
}

Route.start
|> Route.get "/events/live" (Sse.broadcast hub)
|> Route.post "/notify" notify
```

The `SseBroadcast` manages per-client channels internally:

| Member | Description |
|--------|-------------|
| `hub.Send(event)` | Send an event to all connected clients |
| `hub.ClientCount` | Number of currently connected clients |

Register the broadcast as a singleton for use across handlers:

```fsharp
let hub = SseBroadcast()

App.defaults
|> App.services [ Service.instance hub ]
```

## Streaming JSON (NDJSON)

Stream a sequence of JSON objects as newline-delimited JSON:

```fsharp
// From a sequence
let streamItems (req: Request) = task {
    let items = seq {
        for i in 1..100 do
            yield {| id = i; name = $"Item {i}" |}
    }
    return Response.streamJson items
}

// From an async enumerable
let streamAsync (req: Request) = task {
    let items = getItemsAsyncEnumerable()
    return Response.streamJsonAsync items
}
```

The response uses `application/x-ndjson` content type with one JSON object per line, flushed after each item.

### Custom Stream Callback

For full control over the response stream:

```fsharp
let customStream (req: Request) = task {
    return Response.streamCallback (fun ctx -> task {
        ctx.Response.ContentType <- "text/plain"
        for i in 1..10 do
            let bytes = System.Text.Encoding.UTF8.GetBytes($"Line {i}\n")
            do! ctx.Response.Body.WriteAsync(System.ReadOnlyMemory(bytes))
            do! ctx.Response.Body.FlushAsync()
            do! System.Threading.Tasks.Task.Delay(100)
        return ()
    })
}
```
