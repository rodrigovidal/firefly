# Testing

Fire provides two testing modes: **Direct** (in-process, no HTTP overhead) and **Integration** (real HTTP server on a random port).

## Direct Mode

`TestClient.create` builds a test client that dispatches requests through the trie router without starting an HTTP server. Fast and ideal for unit-style tests.

```fsharp
open Fire

let routes =
    Route.start
    |> Route.get "/hello" (fun _ -> task {
        return Response.text "Hello!"
    })
    |> Route.post "/echo" (fun (req: Request) -> task {
        let! body = req.Text()
        return Response.text body
    })

let client = TestClient.create routes
```

### Making Requests

```fsharp
// GET
let! response = client |> TestClient.get "/hello"
assert (response.Status = 200)
assert (response.Body = "Hello!")

// POST with body
let! response = client |> TestClient.post "/echo" """{"name":"Fire"}"""
assert (response.Status = 200)

// PUT
let! response = client |> TestClient.put "/users/1" """{"name":"Updated"}"""

// DELETE
let! response = client |> TestClient.delete "/users/1"
```

### With Configuration

If your routes use middleware or services, pass a config:

```fsharp
let config =
    App.defaults
    |> App.middleware RequestId.middleware
    |> App.services [ Service.instance myService ]

let client = TestClient.createWith routes config
```

### Setting Default Headers

```fsharp
let client =
    TestClient.create routes
    |> TestClient.withHeader "Authorization" "Bearer test-token"
    |> TestClient.withHeader "Content-Type" "application/json"
```

## Integration Mode

`TestClient.start` launches a real Kestrel server on a random available port. Use this for end-to-end tests that need real HTTP behavior (WebSockets, compression, etc.).

```fsharp
let! client = TestClient.start routes App.defaults

// Same API as direct mode
let! response = client |> TestClient.get "/hello"
assert (response.Status = 200)

// Clean up when done
do! TestClient.stop client
```

### Integration Test Pattern

```fsharp
open Xunit

[<Fact>]
let ``GET /users returns 200`` () = task {
    let routes =
        Route.start
        |> Route.get "/users" (fun _ -> task {
            return Response.json [| {| id = 1; name = "Alice" |} |]
        })

    let! client = TestClient.start routes App.defaults
    try
        let! response = client |> TestClient.get "/users"
        Assert.Equal(200, response.Status)
        Assert.Contains("Alice", response.Body)
    finally
        TestClient.stop client |> Async.AwaitTask |> Async.RunSynchronously
}
```

## TestResponse

Both modes return a `TestResponse`:

```fsharp
type TestResponse = {
    Status: int
    Headers: (string * string) list
    Body: string
}
```

Check headers:

```fsharp
let! response = client |> TestClient.get "/api/data"
let contentType =
    response.Headers
    |> List.tryFind (fun (k, _) -> k = "Content-Type")
    |> Option.map snd
```

## Testing with JSON

Parse response bodies using `System.Text.Json`:

```fsharp
open System.Text.Json

let! response = client |> TestClient.get "/api/users/1"
let user = JsonSerializer.Deserialize<User>(response.Body)
Assert.Equal("Alice", user.Name)
```

## Testing Middleware

Test that middleware is applied correctly:

```fsharp
[<Fact>]
let ``rate limiter returns 429 after limit`` () = task {
    let routes =
        Route.start
        |> Route.middleware (RateLimit.fixedWindow 2 (TimeSpan.FromMinutes 1.0) (fun _ -> "test"))
        |> Route.get "/api" (fun _ -> task { return Response.ok })

    let client = TestClient.createWith routes App.defaults

    let! r1 = client |> TestClient.get "/api"
    let! r2 = client |> TestClient.get "/api"
    let! r3 = client |> TestClient.get "/api"

    Assert.Equal(200, r1.Status)
    Assert.Equal(200, r2.Status)
    Assert.Equal(429, r3.Status)
}
```

## Direct vs Integration

| Feature | Direct | Integration |
|---------|--------|-------------|
| Speed | Very fast | Slower (real TCP) |
| HTTP fidelity | Simulated | Real HTTP |
| WebSocket testing | No | Yes |
| Compression | No | Yes |
| Kestrel features | No | Yes |
| Setup | `TestClient.create` | `TestClient.start` (async) |
| Cleanup | None needed | `TestClient.stop` |

Use Direct mode for most tests. Switch to Integration when you need real HTTP behavior.
