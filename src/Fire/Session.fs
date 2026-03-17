namespace Fire

open System
open System.Collections.Concurrent
open System.Collections.Generic

[<RequireQualifiedAccess>]
module Session =

    let private sessionKey = "fire.session"
    let private cookieName = "_fire_session"

    type SessionStore = ConcurrentDictionary<string, ConcurrentDictionary<string, obj>>

    let private defaultStore = SessionStore()

    /// Generate a session ID
    let private generateId () =
        Guid.NewGuid().ToString("N")

    /// Get a value from the current session
    let get<'T> (key: string) (req: Request) : 'T option =
        match req.Raw.Items.TryGetValue(sessionKey) with
        | true, session ->
            let dict = session :?> ConcurrentDictionary<string, obj>
            match dict.TryGetValue(key) with
            | true, v -> Some (v :?> 'T)
            | false, _ -> None
        | false, _ -> None

    /// Set a value in the current session
    let set (key: string) (value: obj) (req: Request) : unit =
        match req.Raw.Items.TryGetValue(sessionKey) with
        | true, session ->
            let dict = session :?> ConcurrentDictionary<string, obj>
            dict.[key] <- value
        | false, _ -> ()

    /// Remove a value from the current session
    let remove (key: string) (req: Request) : unit =
        match req.Raw.Items.TryGetValue(sessionKey) with
        | true, session ->
            let dict = session :?> ConcurrentDictionary<string, obj>
            dict.TryRemove(key) |> ignore
        | false, _ -> ()

    /// Clear the entire session
    let clear (req: Request) : unit =
        match req.Raw.Items.TryGetValue(sessionKey) with
        | true, session ->
            let dict = session :?> ConcurrentDictionary<string, obj>
            dict.Clear()
        | false, _ -> ()

    /// Create session middleware with a specific store (for testing)
    let withStore (store: SessionStore) : Middleware =
        fun next req -> task {
            let sessionId =
                match req.Cookie cookieName with
                | Some id when store.ContainsKey(id) -> id
                | _ -> generateId()

            let data = store.GetOrAdd(sessionId, fun _ -> ConcurrentDictionary<string, obj>())
            req.Raw.Items.[sessionKey] <- data

            let! response = next req
            return response
                |> Response.cookie cookieName sessionId
        }

    /// Default session middleware using in-memory store
    let middleware : Middleware = withStore defaultStore
