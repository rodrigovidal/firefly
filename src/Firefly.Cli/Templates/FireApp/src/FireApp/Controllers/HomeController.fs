namespace FireApp.Controllers

open Firefly

module HomeController =

    // A build-free landing page: plain server-rendered HTML, no asset pipeline.
    let private landing = """<!DOCTYPE html>
<html lang="en"><head><meta charset="utf-8"><title>FireApp</title>
<meta name="viewport" content="width=device-width, initial-scale=1">
<style>body{font-family:system-ui,-apple-system,sans-serif;background:#0b0e14;color:#e6e6e6;margin:0;display:grid;place-items:center;min-height:100vh}main{max-width:640px;padding:40px;text-align:center}h1{font-size:32px;margin:0 0 10px}p{color:#8b95a7;line-height:1.7}code{background:#161b26;padding:2px 7px;border-radius:6px;font-family:ui-monospace,monospace;color:#e6e6e6}a{color:#34d399}</style>
</head><body><main>
<h1>FireApp is running &#128293;</h1>
<p>Your Firefly app is up. This page is plain server-rendered HTML &mdash; no build step required.</p>
<p>Try the JSON API:<br><code>GET /health</code> &middot; <code>GET /api/todos</code> &middot; <code>POST /api/todos</code></p>
<p>Routes live in <code>Router.fs</code>, handlers in <code>Controllers/</code>, validation in <code>Todos.fs</code>.</p>
</main></body></html>"""

    let home (_req: Request) = task { return Response.html landing }

    let health (_req: Request) = task { return Response.json {| status = "ok" |} }
