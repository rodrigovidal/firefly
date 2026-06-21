module Firefly.Tests.SessionTests

open Xunit
open FsUnit.Xunit
open Firefly
open System.Collections.Concurrent
open Microsoft.Extensions.Caching.Distributed
open Microsoft.Extensions.Caching.Memory
open Microsoft.Extensions.Options
open Microsoft.Extensions.DependencyInjection

let private newCache () : IDistributedCache =
    new MemoryDistributedCache(Options.Create(MemoryDistributedCacheOptions())) :> IDistributedCache

let private findHeader name (headers: (string * string) list) =
    headers |> List.tryFind (fun (k, _) -> k = name) |> Option.map snd

let private extractSessionId (setCookie: string) =
    // Format: _fire_session=<id>
    let prefix = "_fire_session="
    let startIdx = setCookie.IndexOf(prefix)
    if startIdx < 0 then ""
    else setCookie.Substring(startIdx + prefix.Length)

[<Fact>]
let ``Session get and set round-trip`` () = task {
    let store = Session.SessionStore()
    let routes =
        Route.start
        |> Route.get "/set" (fun (req: Request) -> task {
            Session.set "name" "Alice" req
            return Response.text "set"
        })
        |> Route.get "/get" (fun (req: Request) -> task {
            let name = Session.get<string> "name" req |> Option.defaultValue "missing"
            return Response.text name
        })
    let config = App.defaults |> App.middleware (Session.withStore store)
    let client = TestClient.createWith routes config

    let! setResponse = client |> TestClient.get "/set"
    setResponse.Status |> should equal 200

    // Extract session cookie
    let setCookie = findHeader "Set-Cookie" setResponse.Headers
    setCookie.IsSome |> should equal true
    let sessionId = extractSessionId setCookie.Value

    // Make a second request with the session cookie
    let clientWithSession =
        TestClient.createWith routes config
        |> TestClient.withHeader "Cookie" $"_fire_session={sessionId}"
    let! getResponse = clientWithSession |> TestClient.get "/get"
    getResponse.Status |> should equal 200
    getResponse.Body |> should equal "Alice"
}

[<Fact>]
let ``Session persists across requests with same cookie`` () = task {
    let store = Session.SessionStore()
    let routes =
        Route.start
        |> Route.get "/inc" (fun (req: Request) -> task {
            let count = Session.get<int> "count" req |> Option.defaultValue 0
            Session.set "count" (count + 1) req
            return Response.text (string (count + 1))
        })
    let config = App.defaults |> App.middleware (Session.withStore store)
    let client = TestClient.createWith routes config

    let! r1 = client |> TestClient.get "/inc"
    r1.Body |> should equal "1"

    let sessionId = extractSessionId (findHeader "Set-Cookie" r1.Headers).Value
    let clientWithSession =
        TestClient.createWith routes config
        |> TestClient.withHeader "Cookie" $"_fire_session={sessionId}"

    let! r2 = clientWithSession |> TestClient.get "/inc"
    r2.Body |> should equal "2"

    let! r3 = clientWithSession |> TestClient.get "/inc"
    r3.Body |> should equal "3"
}

[<Fact>]
let ``Session clear removes all data`` () = task {
    let store = Session.SessionStore()
    let routes =
        Route.start
        |> Route.get "/set" (fun (req: Request) -> task {
            Session.set "name" "Alice" req
            return Response.text "set"
        })
        |> Route.get "/clear" (fun (req: Request) -> task {
            Session.clear req
            return Response.text "cleared"
        })
        |> Route.get "/get" (fun (req: Request) -> task {
            let name = Session.get<string> "name" req |> Option.defaultValue "missing"
            return Response.text name
        })
    let config = App.defaults |> App.middleware (Session.withStore store)
    let client = TestClient.createWith routes config

    let! setR = client |> TestClient.get "/set"
    let sessionId = extractSessionId (findHeader "Set-Cookie" setR.Headers).Value
    let clientWithSession =
        TestClient.createWith routes config
        |> TestClient.withHeader "Cookie" $"_fire_session={sessionId}"

    let! _ = clientWithSession |> TestClient.get "/clear"
    let! getR = clientWithSession |> TestClient.get "/get"
    getR.Body |> should equal "missing"
}

[<Fact>]
let ``New session for new client`` () = task {
    let store = Session.SessionStore()
    let routes =
        Route.start
        |> Route.get "/set" (fun (req: Request) -> task {
            Session.set "name" "Alice" req
            return Response.text "set"
        })
        |> Route.get "/get" (fun (req: Request) -> task {
            let name = Session.get<string> "name" req |> Option.defaultValue "missing"
            return Response.text name
        })
    let config = App.defaults |> App.middleware (Session.withStore store)
    let client = TestClient.createWith routes config

    // Set a value in one session
    let! _ = client |> TestClient.get "/set"

    // New client without session cookie should not see the value
    let newClient = TestClient.createWith routes config
    let! getR = newClient |> TestClient.get "/get"
    getR.Body |> should equal "missing"
}

// --- Distributed backend (IDistributedCache) ---

let private setGetRoutes =
    Route.start
    |> Route.get "/set" (fun (req: Request) -> task {
        Session.set "name" "Alice" req
        return Response.text "set"
    })
    |> Route.get "/get" (fun (req: Request) -> task {
        let name = Session.get<string> "name" req |> Option.defaultValue "missing"
        return Response.text name
    })

[<Fact>]
let ``Distributed session round-trips across requests via the cache`` () = task {
    let cache = newCache ()
    let config = App.defaults |> App.middleware (Session.withCache cache)
    let client = TestClient.createWith setGetRoutes config

    let! setR = client |> TestClient.get "/set"
    let sessionId = extractSessionId (findHeader "Set-Cookie" setR.Headers).Value

    // Separately-built client, but the SAME cache instance: data lives in the cache.
    let client2 =
        TestClient.createWith setGetRoutes config
        |> TestClient.withHeader "Cookie" $"_fire_session={sessionId}"
    let! getR = client2 |> TestClient.get "/get"
    getR.Body |> should equal "Alice"
}

[<Fact>]
let ``Distributed session data is isolated per cache instance`` () = task {
    let configA = App.defaults |> App.middleware (Session.withCache (newCache ()))
    let! setR = TestClient.createWith setGetRoutes configA |> TestClient.get "/set"
    let sessionId = extractSessionId (findHeader "Set-Cookie" setR.Headers).Value

    // Same cookie, but a DIFFERENT cache: the value is not found.
    let configB = App.defaults |> App.middleware (Session.withCache (newCache ()))
    let clientB =
        TestClient.createWith setGetRoutes configB
        |> TestClient.withHeader "Cookie" $"_fire_session={sessionId}"
    let! getR = clientB |> TestClient.get "/get"
    getR.Body |> should equal "missing"
}

[<Fact>]
let ``Distributed session clear removes data`` () = task {
    let cache = newCache ()
    let routes =
        setGetRoutes
        |> Route.get "/clear" (fun (req: Request) -> task {
            Session.clear req
            return Response.text "cleared"
        })
    let config = App.defaults |> App.middleware (Session.withCache cache)

    let! setR = TestClient.createWith routes config |> TestClient.get "/set"
    let sessionId = extractSessionId (findHeader "Set-Cookie" setR.Headers).Value
    let withCookie () =
        TestClient.createWith routes config
        |> TestClient.withHeader "Cookie" $"_fire_session={sessionId}"

    let! _ = withCookie () |> TestClient.get "/clear"
    let! getR = withCookie () |> TestClient.get "/get"
    getR.Body |> should equal "missing"
}

[<Fact>]
let ``Session.distributed resolves the cache from DI`` () = task {
    let config =
        App.defaults
        |> App.port 0
        |> App.services [ Service.raw (fun s -> s.AddDistributedMemoryCache() |> ignore) ]
        |> App.middleware Session.distributed
    let! client = TestClient.start setGetRoutes config

    let! setR = client |> TestClient.get "/set"
    let sessionId = extractSessionId (findHeader "Set-Cookie" setR.Headers).Value
    let clientWithSession = client |> TestClient.withHeader "Cookie" $"_fire_session={sessionId}"
    let! getR = clientWithSession |> TestClient.get "/get"
    getR.Body |> should equal "Alice"

    do! TestClient.stop client
}

