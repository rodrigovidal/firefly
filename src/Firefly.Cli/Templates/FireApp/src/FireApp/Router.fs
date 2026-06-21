namespace FireApp

open Firefly
open FireApp.Controllers

module Router =

    let routes =
        Route.start
        |> Route.get "/" HomeController.home
        |> Route.get "/health" HomeController.health
        |> Route.get "/api/todos" TodoController.list
        |> Route.post "/api/todos" TodoController.create
