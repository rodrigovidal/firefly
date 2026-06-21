namespace Firefly

open System
open System.Collections.Concurrent
open System.Text.Json

/// A presence change. `Joined` is true when a key is added/updated, false on leave.
/// `Meta` is the JSON metadata (empty on leave).
type PresenceDiff = { Topic: string; Key: string; Joined: bool; Meta: string }

/// Wire format used to replicate presence diffs across an IPubSub backplane.
[<CLIMutable>]
type PresenceWire = { origin: string; topic: string; key: string; joined: bool; meta: string }

/// Tracks who is present in each topic, optionally replicated across nodes via an
/// IPubSub backplane. Single-node by default; pass a shared (name, pubsub) so every
/// node sees the merged view.
///
/// v1 has no heartbeat/expiry: a crashed node's entries linger until untracked
/// (Phoenix uses a CRDT + heartbeats — future work). Single-node is exact.
type Presence(?name: string, ?pubsub: IPubSub) =
    // topic -> (key -> metaJson)
    let state = ConcurrentDictionary<string, ConcurrentDictionary<string, string>>()
    let listeners = ConcurrentDictionary<Guid, PresenceDiff -> unit>()
    let nodeId = Guid.NewGuid().ToString("N")
    let topicName = "presence:" + (defaultArg name "default")

    let topicMap (topic: string) =
        state.GetOrAdd(topic, fun _ -> ConcurrentDictionary<string, string>())

    let fire (diff: PresenceDiff) =
        for kvp in listeners do kvp.Value diff

    let applyLocal (topic: string) (key: string) (joined: bool) (meta: string) =
        let m = topicMap topic
        if joined then m.[key] <- meta
        else m.TryRemove(key) |> ignore
        fire { Topic = topic; Key = key; Joined = joined; Meta = meta }

    // Apply a diff that arrived from another node (skip our own echoes).
    let onRemote (payload: byte[]) =
        try
            let w = JsonSerializer.Deserialize<PresenceWire>(payload)
            if not (obj.ReferenceEquals(w, null)) && w.origin <> nodeId then
                applyLocal w.topic w.key w.joined w.meta
        with _ -> ()

    let subscription : IDisposable option =
        pubsub |> Option.map (fun ps -> ps.Subscribe(topicName, onRemote))

    let publish (topic: string) (key: string) (joined: bool) (meta: string) =
        match pubsub with
        | Some ps ->
            let w = { origin = nodeId; topic = topic; key = key; joined = joined; meta = meta }
            ps.Publish(topicName, JsonSerializer.SerializeToUtf8Bytes(w))
        | None -> ()

    /// Mark `key` present in `topic` with metadata `meta` (JSON-serialized).
    member _.Track(topic: string, key: string, meta: 'M) =
        let json = JsonSerializer.Serialize(meta)
        applyLocal topic key true json
        publish topic key true json

    /// Remove `key` from `topic`.
    member _.Untrack(topic: string, key: string) =
        applyLocal topic key false ""
        publish topic key false ""

    /// Everyone present in `topic`, with metadata deserialized to 'M.
    member _.List<'M>(topic: string) : (string * 'M) list =
        match state.TryGetValue(topic) with
        | true, m -> [ for kvp in m -> kvp.Key, JsonSerializer.Deserialize<'M>(kvp.Value) ]
        | false, _ -> []

    /// Number of entries present in `topic`.
    member _.Count(topic: string) =
        match state.TryGetValue(topic) with
        | true, m -> m.Count
        | false, _ -> 0

    /// Register a join/leave callback. Dispose the handle to stop listening.
    member _.OnChange(handler: PresenceDiff -> unit) : IDisposable =
        let id = Guid.NewGuid()
        listeners.[id] <- handler
        { new IDisposable with
            member _.Dispose() = listeners.TryRemove(id) |> ignore }

    /// Dispose the backplane subscription (a no-op for single-node use).
    member _.Dispose() =
        subscription |> Option.iter (fun d -> d.Dispose())
