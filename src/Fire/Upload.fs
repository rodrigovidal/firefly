namespace Fire

[<RequireQualifiedAccess>]
module Upload =
    let maxSize (maxBytes: int64) : Middleware =
        fun next req -> task {
            match req.Raw.Request.ContentLength with
            | v when v.HasValue && v.Value > maxBytes ->
                return Response.json {| error = "Request body too large" |} |> Response.status 413
            | _ ->
                let bodyFeature = req.Raw.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>()
                if bodyFeature <> null then
                    bodyFeature.MaxRequestBodySize <- System.Nullable(maxBytes)
                return! next req
        }
