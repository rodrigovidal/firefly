namespace Firefly

type CorsConfig = {
    Origins: string list
    Methods: string list
    Headers: string list
    MaxAge: int option
}

[<RequireQualifiedAccess>]
module Cors =
    let defaults = { Origins = []; Methods = []; Headers = []; MaxAge = None }
    let origins o (config: CorsConfig) = { config with Origins = o }
    let methods m (config: CorsConfig) = { config with Methods = m }
    let headers h (config: CorsConfig) = { config with Headers = h }
    let maxAge s (config: CorsConfig) = { config with MaxAge = Some s }

    let private defaultMethods = "GET, POST, PUT, PATCH, DELETE, HEAD, OPTIONS"

    let build (config: CorsConfig) : Middleware =
        fun next req ->
            let origin = req.Header "Origin"
            let isPreflight = req.Method = "OPTIONS"

            let allowedOrigin =
                match config.Origins with
                | [] -> Some "*"
                | origins ->
                    match origin with
                    | Some o when origins |> List.contains o -> Some o
                    | _ -> None

            match allowedOrigin with
            | None -> next req
            | Some allowOrigin ->
                if isPreflight then
                    task {
                        let methodsValue =
                            match config.Methods with
                            | [] -> defaultMethods
                            | m -> System.String.Join(", ", m)
                        let headersValue =
                            match config.Headers with
                            | [] -> "*"
                            | h -> System.String.Join(", ", h)
                        let mutable r =
                            { Status = 204; Headers = []; Body = Empty }
                            |> Response.header "Access-Control-Allow-Origin" allowOrigin
                            |> Response.header "Access-Control-Allow-Methods" methodsValue
                            |> Response.header "Access-Control-Allow-Headers" headersValue
                        match config.MaxAge with
                        | Some age -> r <- r |> Response.header "Access-Control-Max-Age" (string age)
                        | None -> ()
                        return r
                    }
                else
                    task {
                        let! response = next req
                        return response |> Response.header "Access-Control-Allow-Origin" allowOrigin
                    }

    let allowAll : Middleware = defaults |> build
