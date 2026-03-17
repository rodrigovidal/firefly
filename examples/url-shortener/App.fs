module UrlShortener.App

open System
open System.Collections.Concurrent
open Fire

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

type ShortUrl = { Code: string; Url: string; Clicks: int; CreatedAt: DateTime }
type CreateUrl = { Url: string }

// ---------------------------------------------------------------------------
// HTML landing page
// ---------------------------------------------------------------------------

let landingPage = """<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>Fire URL Shortener</title>
  <style>
    * { box-sizing: border-box; margin: 0; padding: 0; }
    body { font-family: system-ui, sans-serif; background: #0f172a; color: #e2e8f0; min-height: 100vh; display: flex; align-items: center; justify-content: center; }
    .container { max-width: 480px; width: 100%; padding: 2rem; }
    h1 { font-size: 2rem; margin-bottom: 0.25rem; }
    h1 span { color: #f97316; }
    p.sub { color: #94a3b8; margin-bottom: 2rem; }
    form { display: flex; gap: 0.5rem; }
    input { flex: 1; padding: 0.75rem 1rem; border-radius: 0.5rem; border: 1px solid #334155; background: #1e293b; color: #e2e8f0; font-size: 1rem; }
    input:focus { outline: none; border-color: #f97316; }
    button { padding: 0.75rem 1.5rem; border-radius: 0.5rem; border: none; background: #f97316; color: #fff; font-weight: 600; font-size: 1rem; cursor: pointer; }
    button:hover { background: #ea580c; }
    .stats { margin-top: 2rem; font-size: 0.875rem; color: #64748b; }
    .stats a { color: #f97316; text-decoration: none; }
  </style>
</head>
<body>
  <div class="container">
    <h1><span>Fire</span> URL Shortener</h1>
    <p class="sub">Paste a URL and get a short link.</p>
    <form method="POST" action="/api/shorten" enctype="application/x-www-form-urlencoded">
      <input type="url" name="url" placeholder="https://example.com/very/long/url" required />
      <button type="submit">Shorten</button>
    </form>
    <p class="stats">View all links &rarr; <a href="/api/stats">/api/stats</a></p>
  </div>
</body>
</html>"""

// ---------------------------------------------------------------------------
// Default code generator
// ---------------------------------------------------------------------------

let defaultGenerateCode () =
    let chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"
    let rng = Random.Shared
    String [| for _ in 1..6 -> chars.[rng.Next(chars.Length)] |]

// ---------------------------------------------------------------------------
// App factory
// ---------------------------------------------------------------------------

let createWith (generateCode: unit -> string) =
    let store = ConcurrentDictionary<string, ShortUrl>()

    let homePage : Handler = fun _req -> task {
        return
            Response.text landingPage
            |> Response.header "Content-Type" "text/html; charset=utf-8"
    }

    let createShortUrl : Handler = fun req -> task {
        let! url =
            match req.ContentType with
            | Some ct when ct.Contains("application/json") ->
                task {
                    let! body = req.Json<CreateUrl>()
                    return body.Url
                }
            | _ ->
                task {
                    let! form = req.Form()
                    return
                        match form.TryGetValue("url") with
                        | true, v -> v
                        | false, _ -> ""
                }

        if String.IsNullOrWhiteSpace(url) then
            return Response.json {| error = "url is required" |} |> Response.status 400
        elif not (url.StartsWith("http://") || url.StartsWith("https://")) then
            return Response.json {| error = "url must start with http:// or https://" |} |> Response.status 400
        else
            let code = generateCode ()
            let entry = { Code = code; Url = url; Clicks = 0; CreatedAt = DateTime.UtcNow }
            store.[code] <- entry
            return Response.json {| code = code; shortUrl = $"/{code}"; originalUrl = url |} |> Response.status 201
    }

    let getStats : Handler = fun _req -> task {
        let urls = store.Values |> Seq.toList
        return Response.json {| count = urls.Length; urls = urls |}
    }

    let getStatsForCode : Handler = fun req -> task {
        let code = req.Params.["code"]
        match store.TryGetValue(code) with
        | true, entry ->
            return Response.json entry
        | false, _ ->
            return Response.json {| error = "not found" |} |> Response.status 404
    }

    let redirectToUrl : Handler = fun req -> task {
        let code = req.Params.["code"]
        match store.TryGetValue(code) with
        | true, entry ->
            store.[code] <- { entry with Clicks = entry.Clicks + 1 }
            return Response.ok |> Response.redirect entry.Url 302
        | false, _ ->
            return Response.json {| error = "short URL not found" |} |> Response.status 404
    }

    let custom404 : Handler = fun _req -> task {
        return
            Response.text "404 — Nothing here. Try creating a short URL at /"
            |> Response.status 404
    }

    let createRateLimit =
        RateLimit.fixedWindow 10 (TimeSpan.FromMinutes 1.0) RateLimit.byIp

    let routes =
        Route.start
        |> Route.get("/", homePage)
        |> Route.group("/api", fun api ->
            api
            |> Route.middleware(createRateLimit)
            |> Route.post("/shorten", createShortUrl)
            |> Route.get("/stats", getStats)
            |> Route.get("/stats/:code", getStatsForCode)
        )
        |> Route.get("/:code", redirectToUrl)

    let config =
        App.defaults
        |> App.port 3000
        |> App.notFound custom404

    (routes, config)

let create () = createWith defaultGenerateCode
