namespace FireApp.Controllers

open Firefly
open FireApp

module TodoController =

    let list (_req: Request) = task {
        return Response.json (Todos.all ())
    }

    let create (req: Request) = task {
        match! Schema.parseRequest Todos.inputSchema req with
        | Ok input ->
            let todo = Todos.add input.Title input.Completed
            return Response.json todo |> Response.status 201
        | Error errors ->
            return Response.json {| errors = errors |} |> Response.status 400
    }
