namespace Firefly

open System
open System.Collections.Concurrent

/// A minimal publish/subscribe backplane. Firefly's real-time features (WsHub
/// broadcast, Presence) fan out through an IPubSub so they can span nodes.
/// The in-process default keeps everything local; a cross-process transport
/// (Redis pub/sub, NATS, …) is an opt-in implementation — core never depends on one.
type IPubSub =
    /// Publish a payload to all subscribers of a topic (including the publisher).
    abstract Publish: topic: string * payload: byte[] -> unit
    /// Subscribe to a topic. Dispose the returned handle to unsubscribe.
    abstract Subscribe: topic: string * handler: (byte[] -> unit) -> IDisposable

/// Process-local pub/sub. A single shared instance fans messages out across all
/// hubs/presence trackers in the same process (and lets tests simulate several
/// nodes); it does not cross process boundaries.
type InProcessPubSub() =
    let topics = ConcurrentDictionary<string, ConcurrentDictionary<Guid, byte[] -> unit>>()

    interface IPubSub with
        member _.Publish(topic, payload) =
            match topics.TryGetValue(topic) with
            | true, handlers ->
                for kvp in handlers do
                    kvp.Value payload
            | false, _ -> ()

        member _.Subscribe(topic, handler) =
            let handlers = topics.GetOrAdd(topic, fun _ -> ConcurrentDictionary<Guid, byte[] -> unit>())
            let id = Guid.NewGuid()
            handlers.[id] <- handler
            { new IDisposable with
                member _.Dispose() = handlers.TryRemove(id) |> ignore }

[<RequireQualifiedAccess>]
module PubSub =
    /// Create a process-local backplane. Share one instance across hubs/presence
    /// trackers to fan out between them within a process.
    let inProcess () : IPubSub = InProcessPubSub() :> IPubSub
