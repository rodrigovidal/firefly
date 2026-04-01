namespace Fire

[<RequireQualifiedAccess>]
module Version =
    let url (version: string) (configure: RouteTable -> RouteTable) (table: RouteTable) : RouteTable =
        Route.group $"/{version}" configure table

    let header (headerName: string) (version: string) : Middleware =
        fun next req ->
            match req.Header headerName with
            | Some v when v = version -> next req
            | Some _ -> task { return Response.json {| error = "Unsupported API version" |} |> Response.status 400 }
            | None -> next req

    let headerRequired (headerName: string) (version: string) : Middleware =
        fun next req ->
            match req.Header headerName with
            | Some v when v = version -> next req
            | Some _ -> task { return Response.json {| error = "Unsupported API version" |} |> Response.status 400 }
            | None -> task { return Response.json {| error = $"Missing {headerName} header" |} |> Response.status 400 }
