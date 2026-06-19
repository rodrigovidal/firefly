module Fire.Tests.MiddlewareTests

open Xunit
open FsUnit.Xunit
open Firefly

[<Fact>]
let ``applies middleware when predicate is true`` () = task {
    let addHeader : Middleware = fun next req -> task {
        let! response = next req
        return response |> Response.header "X-Applied" "yes"
    }
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task { return Response.text "ok" })
    let config =
        App.defaults
        |> App.middleware (MiddlewareHelpers.when' (fun req -> req.Path = "/test") addHeader)
    let client = TestClient.createWith routes config
    let! r = client |> TestClient.get "/test"
    r.Status |> should equal 200
    r.Headers |> List.exists (fun (k, _) -> k = "X-Applied") |> should equal true
}

[<Fact>]
let ``skips middleware when predicate is false`` () = task {
    let addHeader : Middleware = fun next req -> task {
        let! response = next req
        return response |> Response.header "X-Applied" "yes"
    }
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task { return Response.text "ok" })
    let config =
        App.defaults
        |> App.middleware (MiddlewareHelpers.when' (fun req -> req.Path = "/other") addHeader)
    let client = TestClient.createWith routes config
    let! r = client |> TestClient.get "/test"
    r.Status |> should equal 200
    r.Headers |> List.exists (fun (k, _) -> k = "X-Applied") |> should equal false
}

[<Fact>]
let ``composes with other middleware`` () = task {
    let mw1 : Middleware = fun next req -> task {
        let! response = next req
        return response |> Response.header "X-First" "1"
    }
    let mw2 : Middleware = fun next req -> task {
        let! response = next req
        return response |> Response.header "X-Second" "2"
    }
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task { return Response.text "ok" })
    let config =
        App.defaults
        |> App.middleware mw1
        |> App.middleware (MiddlewareHelpers.when' (fun _ -> true) mw2)
    let client = TestClient.createWith routes config
    let! r = client |> TestClient.get "/test"
    r.Headers |> List.exists (fun (k, _) -> k = "X-First") |> should equal true
    r.Headers |> List.exists (fun (k, _) -> k = "X-Second") |> should equal true
}

[<Fact>]
let ``works with Route.middleware`` () = task {
    let addHeader : Middleware = fun next req -> task {
        let! response = next req
        return response |> Response.header "X-RouteLevel" "yes"
    }
    let routes =
        Route.start
        |> Route.middleware (MiddlewareHelpers.when' (fun req -> req.Path.StartsWith("/api")) addHeader)
        |> Route.get "/api/data" (fun _ -> task { return Response.text "data" })
        |> Route.get "/page" (fun _ -> task { return Response.text "page" })
    let client = TestClient.create routes
    let! r1 = client |> TestClient.get "/api/data"
    r1.Headers |> List.exists (fun (k, _) -> k = "X-RouteLevel") |> should equal true
    let! r2 = client |> TestClient.get "/page"
    r2.Headers |> List.exists (fun (k, _) -> k = "X-RouteLevel") |> should equal false
}
