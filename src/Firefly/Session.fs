namespace Firefly

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Text.Json
open Microsoft.Extensions.Caching.Distributed
open Microsoft.Extensions.DependencyInjection

[<RequireQualifiedAccess>]
module Session =

    let private sessionKey = "fire.session"
    let private dirtyKey = "fire.session.dirty"
    let private cookieName = "_fire_session"
    let private keyPrefix = "fire.session:"

    /// In-process session store: session id -> (entry key -> JSON value).
    type SessionStore = ConcurrentDictionary<string, ConcurrentDictionary<string, string>>

    let private defaultStore = SessionStore()

    let private generateId () =
        Guid.NewGuid().ToString("N")

    let private currentDict (req: Request) : ConcurrentDictionary<string, string> option =
        match req.Raw.Items.TryGetValue(sessionKey) with
        | true, session -> Some(session :?> ConcurrentDictionary<string, string>)
        | false, _ -> None

    let private markDirty (req: Request) =
        match req.Raw.Items.TryGetValue(dirtyKey) with
        | true, (:? (bool ref) as r) -> r.Value <- true
        | _ -> ()

    /// Read a typed value from the session. Values are stored as JSON, so 'T
    /// must be JSON-deserializable.
    let get<'T> (key: string) (req: Request) : 'T option =
        match currentDict req with
        | Some dict ->
            match dict.TryGetValue(key) with
            | true, json ->
                try Some(JsonSerializer.Deserialize<'T>(json))
                with _ -> None
            | false, _ -> None
        | None -> None

    /// Write a value to the session. The value is JSON-serialized, so it must
    /// be JSON-serializable (records, primitives, collections — not live objects).
    let set (key: string) (value: 'T) (req: Request) : unit =
        match currentDict req with
        | Some dict ->
            dict.[key] <- JsonSerializer.Serialize(value)
            markDirty req
        | None -> ()

    let remove (key: string) (req: Request) : unit =
        match currentDict req with
        | Some dict ->
            dict.TryRemove(key) |> ignore
            markDirty req
        | None -> ()

    let clear (req: Request) : unit =
        match currentDict req with
        | Some dict ->
            dict.Clear()
            markDirty req
        | None -> ()

    // --- In-process backend (default, zero setup) ---

    let withStore (store: SessionStore) : Middleware =
        fun next req -> task {
            let sessionId =
                match req.Cookie cookieName with
                | Some id when store.ContainsKey(id) -> id
                | _ -> generateId()

            let data = store.GetOrAdd(sessionId, fun _ -> ConcurrentDictionary<string, string>())
            req.Raw.Items.[sessionKey] <- data
            req.Raw.Items.[dirtyKey] <- ref false

            let! response = next req
            return response |> Response.cookie cookieName sessionId
        }

    let middleware : Middleware = withStore defaultStore

    // --- Distributed backend over IDistributedCache (opt-in) ---

    let private deserializeBlob (blob: byte[]) : ConcurrentDictionary<string, string> =
        if isNull blob || blob.Length = 0 then
            ConcurrentDictionary<string, string>()
        else
            try
                match JsonSerializer.Deserialize<Dictionary<string, string>>(blob) with
                | null -> ConcurrentDictionary<string, string>()
                | d -> ConcurrentDictionary<string, string>(d)
            with _ -> ConcurrentDictionary<string, string>()

    /// Session middleware backed by an explicit IDistributedCache, with custom
    /// cache-entry options (TTL/expiration).
    let withCacheOptions (cache: IDistributedCache) (options: DistributedCacheEntryOptions) : Middleware =
        fun next req -> task {
            let ct = req.Raw.RequestAborted
            let sessionId =
                match req.Cookie cookieName with
                | Some id -> id
                | None -> generateId()

            let! blob = cache.GetAsync(keyPrefix + sessionId, ct)
            let dict = deserializeBlob blob
            let dirty = ref false
            req.Raw.Items.[sessionKey] <- dict
            req.Raw.Items.[dirtyKey] <- dirty

            let! response = next req

            if dirty.Value then
                let bytes = JsonSerializer.SerializeToUtf8Bytes(dict)
                do! cache.SetAsync(keyPrefix + sessionId, bytes, options, ct)

            return response |> Response.cookie cookieName sessionId
        }

    let private defaultOptions () =
        DistributedCacheEntryOptions(SlidingExpiration = Nullable(TimeSpan.FromMinutes 20.0))

    /// Session middleware backed by an explicit IDistributedCache (20-minute
    /// sliding expiration). Register the cache with AddDistributedMemoryCache()
    /// for local use, or AddStackExchangeRedisCache(...) for a shared backend.
    let withCache (cache: IDistributedCache) : Middleware =
        withCacheOptions cache (defaultOptions ())

    /// Distributed session middleware resolving IDistributedCache from DI.
    /// Requires a registered IDistributedCache — register one with
    /// AddDistributedMemoryCache() or AddStackExchangeRedisCache(...).
    let distributed : Middleware =
        fun next req -> task {
            let cache = req.Raw.RequestServices.GetService<IDistributedCache>()
            if isNull (box cache) then
                failwith "Session.distributed requires a registered IDistributedCache. Add one with App.services [ Service.raw (fun s -> s.AddDistributedMemoryCache() |> ignore) ] or AddStackExchangeRedisCache(...)."
            return! (withCache cache) next req
        }
