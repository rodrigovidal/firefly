module Fire.Tests.TestClientTests

open Xunit
open FsUnit.Xunit
open Fire

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
