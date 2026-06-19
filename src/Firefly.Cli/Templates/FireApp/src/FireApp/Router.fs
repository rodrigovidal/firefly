namespace FireApp

open Firefly
open FireApp.Controllers

module Router =

    let browser =
        Pipeline.create "browser"
        |> Pipeline.plug (Vite.dev ())

    let routes =
        Route.start
        |> Route.pipe "/" browser (fun web ->
            web
            |> Route.get "/" PageController.home
            |> Route.get "/health" (fun _ -> task { return Response.json {| status = "ok" |} }))
        |> Route.group "/assets" (fun routes ->
            routes |> Route.get "/*path" (Static.serve "./Assets"))
        |> Route.group "/static" (fun routes ->
            routes |> Route.get "/*path" (Static.serve "./Static"))
        |> Route.group "/wwwroot" (fun routes ->
            routes |> Route.get "/*path" (Static.serve "./wwwroot"))
