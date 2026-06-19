module Firefly.Tests.TestClientTests

open Xunit
open FsUnit.Xunit
open Firefly

let routes =
    Route.start
    |> Route.get "/hello" (fun _ -> task { return Response.text "world" })
    |> Route.get "/users/:id" (fun (req: Request) -> task {
        return Response.json {| id = req.Params.["id"] |}
    })
    |> Route.post "/echo" (fun (req: Request) -> task {
        let! body = req.Text()
        return Response.text body
    })
    |> Route.put "/echo" (fun (req: Request) -> task {
        let! body = req.Text()
        return Response.text $"updated: {body}"
    })
    |> Route.delete "/items/:id" (fun (req: Request) -> task {
        return Response.noContent
    })
    |> Route.get "/header-check" (fun (req: Request) -> task {
        let v = req.Header "X-Custom" |> Option.defaultValue "none"
        return Response.text v
    })

[<Fact>]
let ``Direct: GET returns correct status and body`` () = task {
    let client = TestClient.create routes
    let! r = client |> TestClient.get "/hello"
    r.Status |> should equal 200
    r.Body |> should equal "world"
}

[<Fact>]
let ``Direct: GET with route params`` () = task {
    let client = TestClient.create routes
    let! r = client |> TestClient.get "/users/42"
    r.Status |> should equal 200
    r.Body |> should haveSubstring "42"
}

[<Fact>]
let ``Direct: POST with body`` () = task {
    let client = TestClient.create routes
    let! r = client |> TestClient.post "/echo" "hello fire"
    r.Status |> should equal 200
    r.Body |> should equal "hello fire"
}

[<Fact>]
let ``Direct: returns 404 for unknown route`` () = task {
    let client = TestClient.create routes
    let! r = client |> TestClient.get "/nope"
    r.Status |> should equal 404
}

[<Fact>]
let ``Direct: withHeader adds header to request`` () = task {
    let client = TestClient.create routes |> TestClient.withHeader "X-Custom" "test-val"
    let! r = client |> TestClient.get "/header-check"
    r.Body |> should equal "test-val"
}

[<Fact>]
let ``Direct: createWith applies global middleware`` () = task {
    let mw : Middleware = fun next req -> task {
        let! response = next req
        return response |> Response.header "X-MW" "applied"
    }
    let config = App.defaults |> App.middleware mw
    let client = TestClient.createWith routes config
    let! r = client |> TestClient.get "/hello"
    r.Headers |> List.exists (fun (k, _) -> k = "X-MW") |> should be True
}

[<Fact>]
let ``Integration: GET returns correct status and body`` () = task {
    let! client = TestClient.start routes (App.defaults |> App.port 0)
    let! r = client |> TestClient.get "/hello"
    r.Status |> should equal 200
    r.Body |> should equal "world"
    do! TestClient.stop client
}

[<Fact>]
let ``Integration: POST with body`` () = task {
    let! client = TestClient.start routes (App.defaults |> App.port 0)
    let! r = client |> TestClient.post "/echo" "hello fire"
    r.Status |> should equal 200
    r.Body |> should equal "hello fire"
    do! TestClient.stop client
}

[<Fact>]
let ``Direct: PUT with body`` () = task {
    let client = TestClient.create routes
    let! r = client |> TestClient.put "/echo" "hello"
    r.Status |> should equal 200
    r.Body |> should equal "updated: hello"
}

[<Fact>]
let ``Direct: DELETE returns correct status`` () = task {
    let client = TestClient.create routes
    let! r = client |> TestClient.delete "/items/42"
    r.Status |> should equal 204
}

// --- Coverage: executeDirect query string parsing (lines 102-104) ---

[<Fact>]
let ``Direct: GET with query string parameters`` () = task {
    let qsRoutes =
        Route.start
        |> Route.get "/search" (fun (req: Request) -> task {
            let q = req.QueryParam "q" |> Option.defaultValue "none"
            return Response.text q
        })
    let client = TestClient.create qsRoutes
    let! r = client |> TestClient.get "/search?q=fire"
    r.Status |> should equal 200
    r.Body |> should equal "fire"
}

// --- Coverage: executeDirect error handling (lines 129-138) ---

[<Fact>]
let ``Direct: error handler is called on exception`` () = task {
    let errorRoutes =
        Route.start
        |> Route.get "/boom" (fun _ -> task {
            return failwith "kaboom"
        })
    let config =
        App.defaults
        |> App.onError (fun ex _req -> task {
            return Response.text $"error: {ex.Message}" |> Response.status 500
        })
    let client = TestClient.createWith errorRoutes config
    let! r = client |> TestClient.get "/boom"
    r.Status |> should equal 500
    r.Body |> should equal "error: kaboom"
}

[<Fact>]
let ``Direct: unhandled exception returns 500 without error handler`` () = task {
    let errorRoutes =
        Route.start
        |> Route.get "/boom" (fun _ -> task {
            return failwith "kaboom"
        })
    let client = TestClient.create errorRoutes
    let! r = client |> TestClient.get "/boom"
    r.Status |> should equal 500
}

// --- Coverage: writeResponse Stream body in TestClient (lines 76-77) ---

[<Fact>]
let ``Direct: Stream body in response`` () = task {
    let streamRoutes =
        Route.start
        |> Route.get "/stream" (fun _ -> task {
            let bytes = System.Text.Encoding.UTF8.GetBytes("streamed content")
            let ms = new System.IO.MemoryStream(bytes) :> System.IO.Stream
            return Response.stream ms
        })
    let client = TestClient.create streamRoutes
    let! r = client |> TestClient.get "/stream"
    r.Status |> should equal 200
    r.Body |> should equal "streamed content"
}

// --- Coverage: dispatchRequest NotFound handler in TestClient (lines 90-91) ---

[<Fact>]
let ``Direct: custom not-found handler`` () = task {
    let nfRoutes =
        Route.start
        |> Route.get "/exists" (fun _ -> task { return Response.text "yes" })
    let config =
        App.defaults
        |> App.notFound (fun _req -> task {
            return Response.text "custom not found" |> Response.status 404
        })
    let client = TestClient.createWith nfRoutes config
    let! r = client |> TestClient.get "/missing"
    r.Status |> should equal 404
    r.Body |> should equal "custom not found"
}

// --- Coverage: TestClient.stop on Direct mode (line 214) ---

[<Fact>]
let ``Direct: stop is a no-op`` () = task {
    let client = TestClient.create routes
    do! TestClient.stop client
    // Should not throw, just return completed task
}

// --- Coverage: Integration mode withHeader (line 160) ---

[<Fact>]
let ``Integration: withHeader adds header`` () = task {
    let! client = TestClient.start routes (App.defaults |> App.port 0)
    let clientWithHeader = client |> TestClient.withHeader "X-Custom" "integration-val"
    let! r = clientWithHeader |> TestClient.get "/header-check"
    r.Status |> should equal 200
    r.Body |> should equal "integration-val"
    do! TestClient.stop client
}

// --- Coverage: error handler that itself throws (lines 132-136) ---

[<Fact>]
let ``Direct: error handler that throws returns 500`` () = task {
    let errorRoutes =
        Route.start
        |> Route.get "/boom" (fun _ -> task {
            return failwith "kaboom"
        })
    let config =
        App.defaults
        |> App.onError (fun _ex _req -> task {
            return failwith "error handler also fails"
        })
    let client = TestClient.createWith errorRoutes config
    let! r = client |> TestClient.get "/boom"
    r.Status |> should equal 500
}
