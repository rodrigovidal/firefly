namespace Firefly

open System
open System.Collections.Concurrent
open System.Net.WebSockets
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Channels
open System.Threading.Tasks

type WsMessage =
    | WsText of string
    | WsBinary of byte[]
    | WsClose

type WsConn(ws: WebSocket, ct: CancellationToken) =
    member _.Send(text: string) : Task = task {
        let bytes = Encoding.UTF8.GetBytes(text)
        do! ws.SendAsync(ReadOnlyMemory(bytes), WebSocketMessageType.Text, true, ct)
    }

    member _.SendBytes(data: byte[]) : Task = task {
        do! ws.SendAsync(ReadOnlyMemory(data), WebSocketMessageType.Binary, true, ct)
    }

    member _.Receive() : Task<WsMessage> = task {
        use ms = new System.IO.MemoryStream()
        let buffer = Array.zeroCreate<byte> 4096
        let mutable result = Unchecked.defaultof<ValueWebSocketReceiveResult>
        let mutable cont = true
        while cont do
            let! r = ws.ReceiveAsync(Memory(buffer), ct)
            result <- r
            if r.MessageType = WebSocketMessageType.Close then
                cont <- false
            else
                ms.Write(buffer, 0, r.Count)
                if r.EndOfMessage then cont <- false
        match result.MessageType with
        | WebSocketMessageType.Text ->
            return WsText(Encoding.UTF8.GetString(ms.ToArray()))
        | WebSocketMessageType.Binary ->
            return WsBinary(ms.ToArray())
        | _ ->
            return WsClose
    }

    member _.Close(?status: WebSocketCloseStatus, ?reason: string) : Task = task {
        let s = defaultArg status WebSocketCloseStatus.NormalClosure
        let r = defaultArg reason ""
        if ws.State = WebSocketState.Open then
            do! ws.CloseAsync(s, r, ct)
    }

    member _.IsOpen = ws.State = WebSocketState.Open
    member _.CancellationToken = ct

/// Wire format used to fan WsHub broadcasts across an IPubSub backplane.
[<CLIMutable>]
type WsEnvelope = { origin: string; room: string; all: bool; payload: string }

/// Multi-client WebSocket hub with rooms. Each connected client subscribes to a
/// room and is backed by its own channel, so a socket is only ever written by its
/// own pump task (WebSocket.SendAsync is not safe under concurrent writers).
/// Messages of type 'T are JSON-serialized on the wire by the WS.hub handler.
///
/// Pass a shared `name` + `pubsub` backplane to fan broadcasts out across nodes:
/// every node running `WsHub<'T>(name, pubsub)` with the same name participates.
/// Without a backplane the hub is purely local (the default).
type WsHub<'T>(?name: string, ?pubsub: IPubSub) =
    // connId -> (room, outgoing writer)
    let subscribers = ConcurrentDictionary<Guid, string * ChannelWriter<'T>>()
    let nodeId = Guid.NewGuid().ToString("N")
    let topic = "wshub:" + (defaultArg name "default")

    let deliverLocal (room: string) (msg: 'T) =
        for kvp in subscribers do
            let (r, writer) = kvp.Value
            if r = room && not (writer.TryWrite(msg)) then
                subscribers.TryRemove(kvp.Key) |> ignore

    let deliverLocalAll (msg: 'T) =
        for kvp in subscribers do
            let (_, writer) = kvp.Value
            if not (writer.TryWrite(msg)) then
                subscribers.TryRemove(kvp.Key) |> ignore

    // Deliver a message that arrived from another node (skip our own echoes).
    let onRemote (payload: byte[]) =
        try
            let env = JsonSerializer.Deserialize<WsEnvelope>(payload)
            if not (obj.ReferenceEquals(env, null)) && env.origin <> nodeId then
                let msg = JsonSerializer.Deserialize<'T>(env.payload)
                if env.all then deliverLocalAll msg else deliverLocal env.room msg
        with _ -> ()

    let subscription : IDisposable option =
        pubsub |> Option.map (fun ps -> ps.Subscribe(topic, onRemote))

    let publish (room: string) (all: bool) (msg: 'T) =
        match pubsub with
        | Some ps ->
            let env = { origin = nodeId; room = room; all = all; payload = JsonSerializer.Serialize(msg) }
            ps.Publish(topic, JsonSerializer.SerializeToUtf8Bytes(env))
        | None -> ()

    /// Register a new subscriber in the given room (default room is "").
    /// Returns the connection id and the reader the WS.hub pump drains to the socket.
    member _.Subscribe(?room: string) : Guid * ChannelReader<'T> =
        let r = defaultArg room ""
        let ch = Channel.CreateUnbounded<'T>()
        let id = Guid.NewGuid()
        subscribers.TryAdd(id, (r, ch.Writer)) |> ignore
        (id, ch.Reader)

    /// Remove a subscriber and complete its channel (its pump loop ends).
    member _.Unsubscribe(id: Guid) =
        match subscribers.TryRemove(id) with
        | true, (_, writer) -> writer.TryComplete() |> ignore
        | false, _ -> ()

    /// Send a message to every member of a room — local, and across the backplane
    /// to every other node when one is configured.
    member _.Broadcast(room: string, msg: 'T) =
        deliverLocal room msg
        publish room false msg

    /// Send a message to every connected member of every room (all nodes).
    member _.BroadcastAll(msg: 'T) =
        deliverLocalAll msg
        publish "" true msg

    /// Send a message to a single connection on this node.
    member _.Send(connId: Guid, msg: 'T) =
        match subscribers.TryGetValue(connId) with
        | true, (_, writer) -> writer.TryWrite(msg) |> ignore
        | false, _ -> ()

    /// Number of connections currently in a room on this node.
    member _.RoomCount(room: string) =
        let mutable n = 0
        for kvp in subscribers do
            if fst kvp.Value = room then n <- n + 1
        n

    /// Total number of connected subscribers on this node across all rooms.
    member _.Count = subscribers.Count

    member _.Dispose() =
        subscription |> Option.iter (fun d -> d.Dispose())

    interface IDisposable with
        member this.Dispose() = this.Dispose()

[<RequireQualifiedAccess>]
module WS =

    let handler (fn: WsConn -> Request -> Task<unit>) : Handler =
        fun (req: Request) -> task {
            let ctx = req.Raw
            if ctx.WebSockets.IsWebSocketRequest then
                let! ws = ctx.WebSockets.AcceptWebSocketAsync()
                let conn = WsConn(ws, ctx.RequestAborted)
                try
                    do! fn conn req
                with
                | :? OperationCanceledException -> ()
                | :? WebSocketException -> ()
                try
                    if conn.IsOpen then do! conn.Close()
                with _ -> ()
                return { Status = 200; Headers = []; Body = Empty }
            else
                return
                    Response.text "WebSocket connection expected"
                    |> Response.status 400
        }

    /// Multi-client WebSocket handler backed by a WsHub. The connecting socket
    /// joins `room`, receives every 'T broadcast to that room (JSON-serialized),
    /// and each inbound text frame is JSON-deserialized to 'T and passed to
    /// `onMessage hub connId value` (malformed frames are ignored).
    let hub (h: WsHub<'T>) (room: string) (onMessage: WsHub<'T> -> Guid -> 'T -> Task<unit>) : Handler =
        fun (req: Request) -> task {
            let ctx = req.Raw
            if ctx.WebSockets.IsWebSocketRequest then
                let! ws = ctx.WebSockets.AcceptWebSocketAsync()
                let ct = ctx.RequestAborted
                let conn = WsConn(ws, ct)
                let (id, reader) = h.Subscribe(room)
                // Pump outgoing messages from this connection's channel to the socket.
                let pump = task {
                    try
                        let mutable cont = true
                        while cont && not ct.IsCancellationRequested do
                            match! reader.WaitToReadAsync(ct) with
                            | true ->
                                let mutable item = Unchecked.defaultof<'T>
                                while reader.TryRead(&item) do
                                    do! conn.Send(JsonSerializer.Serialize(item))
                            | false -> cont <- false
                    with
                    | :? OperationCanceledException -> ()
                    | :? WebSocketException -> ()
                }
                // Receive loop: deserialize inbound frames and dispatch to onMessage.
                try
                    try
                        let mutable cont = true
                        while cont do
                            let! msg = conn.Receive()
                            match msg with
                            | WsText text ->
                                let parsed =
                                    try Some(JsonSerializer.Deserialize<'T>(text))
                                    with _ -> None
                                match parsed with
                                | Some value -> do! onMessage h id value
                                | None -> ()
                            | WsBinary _ -> ()
                            | WsClose -> cont <- false
                    with
                    | :? OperationCanceledException -> ()
                    | :? WebSocketException -> ()
                finally
                    h.Unsubscribe(id)
                do! pump
                try
                    if conn.IsOpen then do! conn.Close()
                with _ -> ()
                return { Status = 200; Headers = []; Body = Empty }
            else
                return
                    Response.text "WebSocket connection expected"
                    |> Response.status 400
        }
