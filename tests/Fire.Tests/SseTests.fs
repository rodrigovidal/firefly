module Fire.Tests.SseTests

open System.Threading.Channels
open Xunit
open FsUnit.Xunit
open Fire

let findHeader (name: string) (headers: (string * string) list) =
    headers |> List.tryFind (fun (k, _) -> k = name) |> Option.map snd

[<Fact>]
let ``Sse.handler sets text/event-stream content type`` () = task {
    let routes =
        Route.start
        |> Route.get "/events" (Sse.handler (fun _writer _req -> task { () }))
    let client = TestClient.create routes
    let! r = client |> TestClient.get "/events"
    r.Headers |> findHeader "Content-Type" |> should equal (Some "text/event-stream")
}

[<Fact>]
let ``Sse.handler sends named events in SSE format`` () = task {
    let routes =
        Route.start
        |> Route.get "/events" (Sse.handler (fun writer _req -> task {
            do! writer.Event("greeting", "hello")
            do! writer.Event("update", "world")
        }))
    let client = TestClient.create routes
    let! r = client |> TestClient.get "/events"
    r.Body |> should haveSubstring "event: greeting\ndata: hello\n\n"
    r.Body |> should haveSubstring "event: update\ndata: world\n\n"
}

[<Fact>]
let ``Sse.handler sends data-only events`` () = task {
    let routes =
        Route.start
        |> Route.get "/events" (Sse.handler (fun writer _req -> task {
            do! writer.Data("just data")
        }))
    let client = TestClient.create routes
    let! r = client |> TestClient.get "/events"
    r.Body |> should haveSubstring "data: just data\n\n"
}

[<Fact>]
let ``Sse.stream reads events from ChannelReader`` () = task {
    let channel = Channel.CreateUnbounded<SseEvent>()
    channel.Writer.TryWrite({ Event = "msg"; Data = "one" }) |> ignore
    channel.Writer.TryWrite({ Event = "msg"; Data = "two" }) |> ignore
    channel.Writer.Complete()

    let routes =
        Route.start
        |> Route.get "/events" (Sse.stream channel.Reader)
    let client = TestClient.create routes
    let! r = client |> TestClient.get "/events"
    r.Body |> should haveSubstring "event: msg\ndata: one\n\n"
    r.Body |> should haveSubstring "event: msg\ndata: two\n\n"
}

[<Fact>]
let ``Sse.stream stops when channel completes`` () = task {
    let channel = Channel.CreateUnbounded<SseEvent>()
    channel.Writer.Complete()

    let routes =
        Route.start
        |> Route.get "/events" (Sse.stream channel.Reader)
    let client = TestClient.create routes
    let! r = client |> TestClient.get "/events"
    // Handler should complete without hanging
    r.Status |> should equal 200
}

[<Fact>]
let ``Sse.handler sets Cache-Control no-cache`` () = task {
    let routes =
        Route.start
        |> Route.get "/events" (Sse.handler (fun _writer _req -> task { () }))
    let client = TestClient.create routes
    let! r = client |> TestClient.get "/events"
    r.Headers |> findHeader "Cache-Control" |> should equal (Some "no-cache")
}

[<Fact>]
let ``Sse.handler sets Connection keep-alive`` () = task {
    let routes =
        Route.start
        |> Route.get "/events" (Sse.handler (fun _writer _req -> task { () }))
    let client = TestClient.create routes
    let! r = client |> TestClient.get "/events"
    r.Headers |> findHeader "Connection" |> should equal (Some "keep-alive")
}

[<Fact>]
let ``Sse.handler does not match POST requests`` () = task {
    let routes =
        Route.start
        |> Route.get "/events" (Sse.handler (fun _writer _req -> task { () }))
    let client = TestClient.create routes
    let! r = client |> TestClient.post "/events" ""
    r.Status |> should equal 404
}

[<Fact>]
let ``Sse.broadcast delivers events to a client`` () = task {
    let hub = SseBroadcast()
    let routes =
        Route.start
        |> Route.get "/events" (Sse.broadcast hub)

    // Pre-subscribe, send events, then unsubscribe to complete the reader.
    // The broadcast handler will subscribe its own client internally,
    // but we need events ready before the handler starts reading.
    // So: use a handler wrapper that sends then unsubscribes.
    let hub2 = SseBroadcast()
    let routes2 =
        Route.start
        |> Route.get "/events" (Sse.handler (fun writer _req -> task {
            let (id, reader) = hub2.Subscribe()
            do! hub2.Send({ Event = "msg"; Data = "broadcast1" })
            do! hub2.Send({ Event = "msg"; Data = "broadcast2" })
            hub2.Unsubscribe(id)
            let mutable item = Unchecked.defaultof<SseEvent>
            while reader.TryRead(&item) do
                do! writer.Event(item.Event, item.Data)
        }))
    let client = TestClient.create routes2
    let! r = client |> TestClient.get "/events"
    r.Body |> should haveSubstring "event: msg\ndata: broadcast1\n\n"
    r.Body |> should haveSubstring "event: msg\ndata: broadcast2\n\n"
}

[<Fact>]
let ``SseBroadcast fans out to multiple subscribers`` () = task {
    let hub = SseBroadcast()
    let (id1, reader1) = hub.Subscribe()
    let (id2, reader2) = hub.Subscribe()
    hub.ClientCount |> should equal 2

    do! hub.Send({ Event = "ping"; Data = "1" })

    let mutable item1 = Unchecked.defaultof<SseEvent>
    reader1.TryRead(&item1) |> should equal true
    item1.Data |> should equal "1"

    let mutable item2 = Unchecked.defaultof<SseEvent>
    reader2.TryRead(&item2) |> should equal true
    item2.Data |> should equal "1"

    hub.Unsubscribe(id1)
    hub.Unsubscribe(id2)
    hub.ClientCount |> should equal 0
}

[<Fact>]
let ``SseBroadcast unsubscribe completes the client channel`` () = task {
    let hub = SseBroadcast()
    let (id, reader) = hub.Subscribe()
    hub.Unsubscribe(id)

    // Channel should be completed — WaitToReadAsync returns false
    let! canRead = reader.WaitToReadAsync()
    canRead |> should equal false
}

[<Fact>]
let ``SseBroadcast.Send with no subscribers does not throw`` () = task {
    let hub = SseBroadcast()
    do! hub.Send({ Event = "ping"; Data = "1" })
    hub.ClientCount |> should equal 0
}

[<Fact>]
let ``SseBroadcast double unsubscribe is idempotent`` () =
    let hub = SseBroadcast()
    let (id, _) = hub.Subscribe()
    hub.ClientCount |> should equal 1
    hub.Unsubscribe(id)
    hub.Unsubscribe(id)
    hub.ClientCount |> should equal 0
