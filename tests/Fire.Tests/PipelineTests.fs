module Fire.Tests.PipelineTests

open Xunit
open FsUnit.Xunit
open Fire

[<Fact>]
let ``Pipeline.create sets name and empty middlewares`` () =
    let p = Pipeline.create "browser"
    p.Name |> should equal "browser"
    p.Middlewares |> should equal List.empty<Middleware>

[<Fact>]
let ``Pipeline.plug adds middleware`` () =
    let mw : Middleware = fun next req -> next req
    let p = Pipeline.create "test" |> Pipeline.plug mw
    p.Middlewares |> should haveLength 1

[<Fact>]
let ``Pipeline.plug preserves order`` () =
    let mutable order = []
    let mw1 : Middleware = fun next req -> task {
        order <- order @ ["mw1"]
        return! next req
    }
    let mw2 : Middleware = fun next req -> task {
        order <- order @ ["mw2"]
        return! next req
    }
    let p = Pipeline.create "test" |> Pipeline.plug mw1 |> Pipeline.plug mw2
    // Compose and execute
    let handler : Handler = fun _ -> task { order <- order @ ["handler"]; return Response.ok }
    let composed = List.foldBack (fun mw h -> mw h) p.Middlewares handler
    composed (Unchecked.defaultof<Request>) |> Async.AwaitTask |> Async.RunSynchronously |> ignore
    order |> should equal ["mw1"; "mw2"; "handler"]

[<Fact>]
let ``Pipeline.empty has name empty and no middlewares`` () =
    Pipeline.empty.Name |> should equal "empty"
    Pipeline.empty.Middlewares |> should equal List.empty<Middleware>

[<Fact>]
let ``Route.pipe applies pipeline middlewares to routes`` () =
    let mutable called = false
    let mw : Middleware = fun next req -> task {
        called <- true
        return! next req
    }
    let pipeline = Pipeline.create "test" |> Pipeline.plug mw
    let routes =
        Route.start
        |> Route.pipe "/api" pipeline (fun api ->
            api |> Route.get "/hello" (fun (_req: Request) -> task { return Response.text "hi" }))
    // The route should have the middleware
    routes.Routes |> should haveLength 1
    routes.Routes.[0].Middlewares |> should haveLength 1

[<Fact>]
let ``Route.pipe sets prefix`` () =
    let routes =
        Route.start
        |> Route.pipe "/api" Pipeline.empty (fun api ->
            api |> Route.get "/users" (fun (_req: Request) -> task { return Response.ok }))
    routes.Routes.[0].Pattern |> should equal "/api/users"

[<Fact>]
let ``Route.pipe with empty pipeline adds no middlewares`` () =
    let routes =
        Route.start
        |> Route.pipe "/web" Pipeline.empty (fun web ->
            web |> Route.get "/" (fun (_req: Request) -> task { return Response.ok }))
    routes.Routes.[0].Middlewares |> should equal List.empty<Middleware>

[<Fact>]
let ``Route.pipe composes with parent middlewares`` () =
    let mw1 : Middleware = fun next req -> next req
    let mw2 : Middleware = fun next req -> next req
    let pipeline = Pipeline.create "inner" |> Pipeline.plug mw2
    let routes =
        Route.start
        |> Route.middleware mw1
        |> Route.pipe "/api" pipeline (fun api ->
            api |> Route.get "/test" (fun (_req: Request) -> task { return Response.ok }))
    // Should have both parent middleware and pipeline middleware
    routes.Routes.[0].Middlewares |> should haveLength 2

[<Fact>]
let ``Multiple Route.pipe calls on same table`` () =
    let browser = Pipeline.create "browser"
    let api = Pipeline.create "api"
    let routes =
        Route.start
        |> Route.pipe "/" browser (fun web ->
            web |> Route.get "/" (fun (_req: Request) -> task { return Response.ok }))
        |> Route.pipe "/api" api (fun api ->
            api |> Route.get "/users" (fun (_req: Request) -> task { return Response.ok }))
    routes.Routes |> should haveLength 2
