namespace Firefly

[<RequireQualifiedAccess>]
module Redirect =

    let permanent (from: string) (to': string) (table: RouteTable) : RouteTable =
        table |> Route.get from (fun (_req: Request) -> task {
            return Response.ok |> Response.redirect to' 301
        })

    let temporary (from: string) (to': string) (table: RouteTable) : RouteTable =
        table |> Route.get from (fun (_req: Request) -> task {
            return Response.ok |> Response.redirect to' 302
        })
