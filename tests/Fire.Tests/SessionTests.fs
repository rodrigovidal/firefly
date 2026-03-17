module Fire.Tests.SessionTests

open Xunit
open FsUnit.Xunit
open Fire
open System.Collections.Concurrent

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
