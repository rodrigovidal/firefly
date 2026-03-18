namespace Fire

open System
open System.Collections.Concurrent
open System.Threading.Tasks

type CachedResponse = {
    CachedStatus: int
    CachedHeaders: (string * string) list
    CachedBody: string
}

type IdempotencyStore =
    abstract TryGet : key:string -> Task<CachedResponse option>
    abstract Set : key:string * response:CachedResponse * ttl:TimeSpan -> Task<unit>

[<RequireQualifiedAccess>]
module Idempotent =

    type private MemoryEntry = { Response: CachedResponse; Expiry: DateTime }

    let inMemory () : IdempotencyStore =
        let store = ConcurrentDictionary<string, MemoryEntry>()
        { new IdempotencyStore with
            member _.TryGet(key) = task {
                match store.TryGetValue(key) with
                | true, entry when entry.Expiry > DateTime.UtcNow ->
                    return Some entry.Response
                | true, _ ->
                    store.TryRemove(key) |> ignore
                    return None
                | false, _ -> return None
            }
            member _.Set(key, response, ttl) = task {
                let entry = { Response = response; Expiry = DateTime.UtcNow.Add(ttl) }
                store.[key] <- entry
                if store.Count > 100 then
                    let now = DateTime.UtcNow
                    for kvp in store do
                        if kvp.Value.Expiry <= now then
                            store.TryRemove(kvp.Key) |> ignore
            }
        }

    let private isStateChanging (method': string) =
        match method'.ToUpperInvariant() with
        | "POST" | "PUT" | "PATCH" -> true
        | _ -> false

    let private toCached (response: Response) : CachedResponse =
        let body =
            match response.Body with
            | Text s -> s
            | Json bytes -> System.Text.Encoding.UTF8.GetString(bytes)
            | _ -> ""
        { CachedStatus = response.Status; CachedHeaders = response.Headers; CachedBody = body }

    let private toResponse (cached: CachedResponse) : Response =
        { Status = cached.CachedStatus
          Headers = cached.CachedHeaders
          Body = if cached.CachedBody = "" then Empty else Text cached.CachedBody }

    /// Middleware that caches responses for requests with an Idempotency-Key header.
    /// Only applies to POST/PUT/PATCH. GET/DELETE/HEAD pass through.
    /// Missing Idempotency-Key on POST also passes through (opt-in per client).
    let middleware (store: IdempotencyStore) (ttl: TimeSpan) : Middleware =
        fun next req -> task {
            if not (isStateChanging req.Method) then
                return! next req
            else
                match req.Header "Idempotency-Key" with
                | None -> return! next req
                | Some key ->
                    match! store.TryGet(key) with
                    | Some cached ->
                        return toResponse cached |> Response.header "Idempotency-Replayed" "true"
                    | None ->
                        let! response = next req
                        let cached = toCached response
                        do! store.Set(key, cached, ttl)
                        return response
        }
