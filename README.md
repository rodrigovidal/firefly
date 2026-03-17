# Fire

A minimal F# web framework built on Kestrel

## Install

```bash
dotnet add package Fire
```

## Hello World

```fsharp
open Fire

let routes =
    Route.start
    |> Route.get "/" (fun _ -> task { return Response.text "Hello, World!" })
    |> Route.group "/api" (fun api ->
        api
        |> Route.get "/users/:id" (fun req -> task {
            return Response.json {| id = req.Params.["id"] |}
        })
    )

App.defaults
|> App.port 3000
|> App.run routes
|> fun t -> t.Wait()
```

## Features

- Routing with groups
- Middleware
- JSON
- Cookies
- CORS
- Logging
- Static files
- Rate limiting
- Timeouts
- OpenAPI
- Validation
- JWT auth
- Testing helpers

## License

MIT
