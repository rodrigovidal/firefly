namespace FireApp

open Fire
open FireApp.Controllers

module Router =

    let routes =
        Route.start
        |> Route.get "/" PageController.home
        |> Route.get "/health" (fun _ -> task { return Response.json {| status = "ok" |} })
        |> Route.group "/assets" (fun routes ->
            routes |> Route.get "/*path" (Static.serve "./Assets"))
        |> Route.group "/static" (fun routes ->
            routes |> Route.get "/*path" (Static.serve "./Static"))
