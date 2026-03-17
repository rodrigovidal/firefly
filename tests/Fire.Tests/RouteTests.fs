module Fire.Tests.RouteTests

open Xunit
open FsUnit.Xunit
open Fire

let dummyHandler : Handler = fun _ -> task { return Response.ok }
let textHandler (t: string) : Handler = fun _ -> task { return Response.text t }

[<Fact>]
let ``Route.start creates empty table`` () =
    let table = Route.start
    table.Prefix |> should equal ""
    table.Middlewares |> should haveLength 0
    table.Routes |> should haveLength 0

[<Fact>]
let ``Route.get adds a GET route with prefix`` () =
    let table =
        Route.start
        |> Route.get "/hello" dummyHandler
    table.Routes |> List.length |> should equal 1
    table.Routes.[0].Method |> should equal "GET"
    table.Routes.[0].Pattern |> should equal "/hello"

[<Fact>]
let ``Route.group scopes prefix`` () =
    let table =
        Route.start
        |> Route.group "/api" (fun api ->
            api |> Route.get "/health" dummyHandler
        )
    table.Routes.[0].Pattern |> should equal "/api/health"

[<Fact>]
let ``Route.group nests prefixes`` () =
    let table =
        Route.start
        |> Route.group "/api" (fun api ->
            api
            |> Route.group "/v1" (fun v1 ->
                v1 |> Route.get "/users" dummyHandler
            )
        )
    table.Routes.[0].Pattern |> should equal "/api/v1/users"

[<Fact>]
let ``Route.middleware is scoped to group`` () =
    let mw : Middleware = fun next req -> next req
    let table =
        Route.start
        |> Route.group "/api" (fun api ->
            api
            |> Route.middleware mw
            |> Route.get "/inner" dummyHandler
        )
        |> Route.get "/outer" dummyHandler
    // Routes are stored in reverse insertion order internally
    let routes = table.Routes |> List.rev
    routes.[0].Middlewares |> List.length |> should equal 1
    routes.[1].Middlewares |> should haveLength 0

[<Fact>]
let ``Route registers all HTTP methods`` () =
    let table =
        Route.start
        |> Route.get "/a" dummyHandler
        |> Route.post "/b" dummyHandler
        |> Route.put "/c" dummyHandler
        |> Route.patch "/d" dummyHandler
        |> Route.delete "/e" dummyHandler
        |> Route.head "/f" dummyHandler
        |> Route.options "/g" dummyHandler
    let methods = table.Routes |> List.rev |> List.map (fun r -> r.Method)
    methods |> should equal ["GET"; "POST"; "PUT"; "PATCH"; "DELETE"; "HEAD"; "OPTIONS"]

[<Fact>]
let ``Route.method registers custom HTTP method`` () =
    let table =
        Route.start
        |> Route.method' "PURGE" "/cache" dummyHandler
    table.Routes.[0].Method |> should equal "PURGE"

[<Fact>]
let ``Sibling groups have independent middleware`` () =
    let mw1 : Middleware = fun next req -> next req
    let mw2 : Middleware = fun next req -> next req
    let table =
        Route.start
        |> Route.group "/a" (fun a ->
            a |> Route.middleware mw1 |> Route.get "" dummyHandler
        )
        |> Route.group "/b" (fun b ->
            b |> Route.middleware mw2 |> Route.get "" dummyHandler
        )
    table.Routes.[0].Middlewares |> List.length |> should equal 1
    table.Routes.[1].Middlewares |> List.length |> should equal 1
    table.Routes.[0].Middlewares.[0] |> should not' (be sameAs table.Routes.[1].Middlewares.[0])
