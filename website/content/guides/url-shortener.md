---
title: "URL Shortener"
description: "Build a URL shortener with Firefly using form and JSON parsing, route params, redirects, rate limiting, and a custom 404."
group: "Guides"
order: 3
---

# URL Shortener

This guide builds a small URL shortener on top of Firefly. It accepts a URL through an HTML form (or JSON), stores it under a random short code, and redirects visitors from `/{code}` to the original link. Along the way it shows how Firefly handles form and JSON input through one schema, route parameters, redirect responses, route groups with middleware, and a custom not-found handler.

## What you'll learn

- Serving an HTML page and setting `Content-Type` on a text response
- Parsing a request body with `Schema.parse` — the same schema handles both form-encoded and JSON bodies
- Reading route parameters via `req.Params.["code"]`
- Returning 301/302 redirects with `Response.redirect`
- Grouping routes under a prefix and attaching `RateLimit` middleware to the group
- Registering a custom 404 handler with `App.notFound`
- Wiring routes and config together and starting the app with `App.run`

## The schema

A `schema { ... }` block declares the expected input once. `Schema.required` validates a non-empty, trimmed, well-formed URL, and the same schema is reused for both form and JSON requests.

```fsharp
open Firefly

type ShortUrl = { Code: string; Url: string; Clicks: int; CreatedAt: DateTime }

let createUrlSchema = schema {
    let! url = Schema.required "Url" Schema.string [ Schema.nonempty; Schema.url; Schema.trim ]
    return {| Url = url |}
}
```

## Serving the landing page

The home handler returns the HTML form as text and sets the content type explicitly so browsers render it as a page. The form posts to `/api/shorten` as `application/x-www-form-urlencoded`.

```fsharp
let homePage : Handler = fun _req -> task {
    return
        Response.text landingPage
        |> Response.header "Content-Type" "text/html; charset=utf-8"
}
```

```html
<form method="POST" action="/api/shorten" enctype="application/x-www-form-urlencoded">
  <input type="url" name="url" placeholder="https://example.com/very/long/url" required />
  <button type="submit">Shorten</button>
</form>
```

## Parsing the body and creating a short URL

`Schema.parse` auto-detects the body format: a form POST and a JSON POST both flow through the same code. On success it generates a code, stores the entry, and returns `201`; on failure it returns the validation errors with `400`.

```fsharp
let createShortUrl : Handler = fun req -> task {
    // Auto-detects: JSON → zero-alloc buffer path, form → form path
    match! Schema.parse createUrlSchema req with
    | Ok input ->
        let code = generateCode ()
        let entry = { Code = code; Url = input.Url; Clicks = 0; CreatedAt = DateTime.UtcNow }
        store.[code] <- entry
        return Response.json {| code = code; shortUrl = $"/{code}"; originalUrl = input.Url |} |> Response.status 201
    | Error errors ->
        return Response.json {| errors = errors |} |> Response.status 400
}
```

## Reading route params and redirecting

The catch-all `/:code` route reads the parameter from `req.Params`. If the code exists, the handler bumps the click counter and issues a `302` redirect to the original URL; otherwise it returns a JSON `404`.

```fsharp
let redirectToUrl : Handler = fun req -> task {
    let code = req.Params.["code"]
    match store.TryGetValue(code) with
    | true, entry ->
        store.[code] <- { entry with Clicks = entry.Clicks + 1 }
        return Response.ok |> Response.redirect entry.Url 302
    | false, _ ->
        return Response.json {| error = "short URL not found" |} |> Response.status 404
}
```

Stats handlers follow the same pattern — `getStatsForCode` looks up `req.Params.["code"]` and returns the stored entry or a `404`.

## A custom 404

`App.notFound` registers a handler that runs when no route matches. Here it returns a friendly plain-text message with a `404` status.

```fsharp
let custom404 : Handler = fun _req -> task {
    return
        Response.text "404 — Nothing here. Try creating a short URL at /"
        |> Response.status 404
}
```

## Routes, groups, and rate limiting

Routes are built with the `Route.*` combinators. The `/api` group shares a rate-limit middleware (10 requests per minute, keyed by IP). The final `/:code` route handles redirects.

```fsharp
let createRateLimit =
    RateLimit.fixedWindow 10 (TimeSpan.FromMinutes 1.0) RateLimit.byIp

let routes =
    Route.start
    |> Route.get "/" homePage
    |> Route.group "/api" (fun api ->
        api
        |> Route.middleware createRateLimit
        |> Route.post "/shorten" createShortUrl
        |> Route.get "/stats" getStats
        |> Route.get "/stats/:code" getStatsForCode
    )
    |> Route.get "/:code" redirectToUrl
```

## App config and startup

The config pipeline sets the port and wires in the custom 404. `App.run` takes the routes, the config, and a cancellation token, then runs the server.

```fsharp
let config =
    App.defaults
    |> App.port 3000
    |> App.notFound custom404
```

```fsharp
open System.Threading
open Firefly
open UrlShortener

let (routes, config) = App.create()

printfn "Fire URL Shortener running on http://localhost:3000"

App.run routes config CancellationToken.None
|> fun t -> t.GetAwaiter().GetResult()
```

## Running it

```bash
dotnet run --project examples/url-shortener
```

```bash
# Create a short URL via the form-encoded endpoint
curl -i -X POST http://localhost:3000/api/shorten \
  -d "url=https://example.com/very/long/url"

# Same endpoint accepts JSON too
curl -X POST http://localhost:3000/api/shorten \
  -H "Content-Type: application/json" \
  -d '{"url":"https://example.com/very/long/url"}'

# Follow the redirect from a short code (use the code returned above)
curl -iL http://localhost:3000/abc123

# List every stored link
curl http://localhost:3000/api/stats
```

## Source

The full example lives at [`examples/url-shortener/`](https://github.com/firefly/firefly/tree/main/examples/url-shortener).
