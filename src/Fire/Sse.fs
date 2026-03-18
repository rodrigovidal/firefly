namespace Fire

open System
open System.Collections.Concurrent
open System.Threading.Channels
open System.Threading.Tasks
open Microsoft.AspNetCore.Http

type SseEvent = { Event: string; Data: string }

type SseWriter(ctx: HttpContext) =
    member _.Event(event: string, data: string) : Task = task {
        let ct = ctx.RequestAborted
        do! ctx.Response.WriteAsync($"event: {event}\ndata: {data}\n\n", ct)
        do! ctx.Response.Body.FlushAsync(ct)
    }
    member _.Data(data: string) : Task = task {
        let ct = ctx.RequestAborted
        do! ctx.Response.WriteAsync($"data: {data}\n\n", ct)
        do! ctx.Response.Body.FlushAsync(ct)
    }

/// Multi-client broadcast hub. Each connected client gets its own channel.
/// Write events via Send — all connected clients receive every message.
type SseBroadcast() =
    let subscribers = ConcurrentDictionary<Guid, ChannelWriter<SseEvent>>()

    member _.Subscribe() : Guid * ChannelReader<SseEvent> =
        let ch = Channel.CreateUnbounded<SseEvent>()
        let id = Guid.NewGuid()
        subscribers.TryAdd(id, ch.Writer) |> ignore
        (id, ch.Reader)

    member _.Unsubscribe(id: Guid) =
        match subscribers.TryRemove(id) with
        | true, writer -> writer.TryComplete() |> ignore
        | false, _ -> ()

    member _.Send(event: SseEvent) : Task =
        for kvp in subscribers do
            if not (kvp.Value.TryWrite(event)) then
                subscribers.TryRemove(kvp.Key) |> ignore
        Task.CompletedTask

    member _.ClientCount = subscribers.Count

[<RequireQualifiedAccess>]
module Sse =

    let private setSseHeaders (ctx: HttpContext) =
        ctx.Response.ContentType <- "text/event-stream"
        ctx.Response.Headers.["Cache-Control"] <- "no-cache"
        ctx.Response.Headers.["Connection"] <- "keep-alive"

    let private streamFromReader (ctx: HttpContext) (reader: ChannelReader<SseEvent>) = task {
        let ct = ctx.RequestAborted
        let mutable cont = true
        while cont && not ct.IsCancellationRequested do
            match! reader.WaitToReadAsync(ct) with
            | true ->
                let mutable item = Unchecked.defaultof<SseEvent>
                while reader.TryRead(&item) do
                    do! ctx.Response.WriteAsync($"event: {item.Event}\ndata: {item.Data}\n\n", ct)
                do! ctx.Response.Body.FlushAsync(ct)
            | false -> cont <- false
    }

    /// Handler-driven SSE. The function receives an SseWriter and Request,
    /// and pushes events until done or client disconnects.
    let handler (fn: SseWriter -> Request -> Task<unit>) : Handler =
        fun (req: Request) -> task {
            let ctx = req.Raw
            setSseHeaders ctx
            let writer = SseWriter(ctx)
            try
                do! fn writer req
            with :? OperationCanceledException -> ()
            return { Status = 200; Headers = []; Body = Empty }
        }

    /// Single-consumer channel-driven SSE. Reads SseEvents from a ChannelReader
    /// and streams them until the channel completes or client disconnects.
    /// Each message is consumed by one client only.
    let stream (reader: ChannelReader<SseEvent>) : Handler =
        fun (req: Request) -> task {
            let ctx = req.Raw
            setSseHeaders ctx
            try
                do! streamFromReader ctx reader
            with :? OperationCanceledException -> ()
            return { Status = 200; Headers = []; Body = Empty }
        }

    /// Multi-client broadcast SSE. Each connected client subscribes to the
    /// SseBroadcast and receives all events sent via broadcast.Send.
    let broadcast (hub: SseBroadcast) : Handler =
        fun (req: Request) -> task {
            let ctx = req.Raw
            setSseHeaders ctx
            let (id, reader) = hub.Subscribe()
            try
                try
                    do! streamFromReader ctx reader
                with :? OperationCanceledException -> ()
            finally
                hub.Unsubscribe(id)
            return { Status = 200; Headers = []; Body = Empty }
        }
