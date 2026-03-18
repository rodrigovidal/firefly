# SSE, Partial Responses & CSRF Polish — Design

Three independent features: Server-Sent Events (new capability), partial HTML responses (optimization for client-side navigation), and CSRF hardening (polish).

## 1. Server-Sent Events

### File: `src/Fire/Sse.fs`

Two entry points covering handler-driven and channel-driven patterns.

### Types

```fsharp
type SseEvent = { Event: string; Data: string }

type SseWriter(ctx: HttpContext) =
    member _.Event(event, data) : Task  // writes "event: ...\ndata: ...\n\n"
    member _.Data(data) : Task          // writes "data: ...\n\n" (no event name)
```

### Sse.handler

For cases where the handler itself produces events (progress, one-off streams). Takes a function that receives an `SseWriter` and the request:

```fsharp
Route.get "/progress" (Sse.handler (fun writer req -> task {
    for i in 1..100 do
        do! writer.Event("progress", $"""{{"pct":{i}}}""")
        do! Task.Delay(50)
}))
```

### Sse.stream

For channel-based pub/sub using `System.Threading.Channels`. Takes a `ChannelReader<SseEvent>` and streams until the channel completes or client disconnects:

```fsharp
let events = Channel.CreateUnbounded<SseEvent>()

Route.get "/events" (Sse.stream events.Reader)

// elsewhere:
do! events.Writer.WriteAsync({ Event = "update"; Data = """{"count":1}""" })
```

### Response Headers

Both set:
- `Content-Type: text/event-stream`
- `Cache-Control: no-cache`
- `Connection: keep-alive`

Both respect `HttpContext.RequestAborted` for client disconnect.

## 2. Partial Responses

### File: `src/Fire/Partial.fs`

A single middleware that optimizes client-side navigation by stripping the HTML shell when `X-Fire-Navigation: true` is present.

```fsharp
module Partial =
    val middleware : Middleware
```

### Behavior

1. Checks for `X-Fire-Navigation: true` header on the incoming request
2. Calls the next handler normally (layout, view, everything renders as usual)
3. If the response is HTML, extracts content between `<body>` and `</body>`
4. Extracts `<title>` text and sets `X-Fire-Title` response header
5. Returns just the body fragment as the response body

Non-HTML responses pass through unchanged — JSON APIs, redirects, etc. are unaffected.

### Client-side change

In `packages/fire-react/src/index.tsx`: after fetch, check for `X-Fire-Title` header. If present, use it for `document.title` and treat the entire response body as inner HTML. If absent (server doesn't have the middleware), fall back to the current `DOMParser` approach.

### Usage

Global middleware:

```fsharp
let config =
    App.defaults
    |> App.middleware Partial.middleware
```

Or scoped to a pipeline:

```fsharp
Pipeline.create "browser"
|> Pipeline.plug Partial.middleware
```

## 3. CSRF Polish

### File: Modify existing `src/Fire/Csrf.fs`

### Hardened cookie attributes

Currently `Response.cookie cookieName token` produces a bare `Set-Cookie`. Change to set `SameSite=Strict; HttpOnly; Path=/`. Add `Secure` only when the request scheme is HTTPS (so dev on localhost still works).

### View helper

```fsharp
Csrf.hiddenInput : Request -> Node
```

Returns `<input type="hidden" name="_csrf" value="...">` as a Node for embedding in forms:

```fsharp
Html.form [ Attr.method "post"; Attr.action "/contacts" ] [
    Csrf.hiddenInput req
    Html.input [ Attr.name "name"; Attr.type' "text" ]
    Html.button [] [ Html.text "Save" ]
]
```

### Meta tag helper

```fsharp
Csrf.metaTag : Request -> Node
```

Returns `<meta name="csrf-token" content="...">` for SPAs that read the token from the DOM and send it via `X-CSRF-Token` header. Add to `<head>` via `View.withHead`.

## Tests

### SSE (8 tests)

1. `Sse.handler sets text/event-stream content type`
2. `Sse.handler sends named events in SSE format`
3. `Sse.handler sends data-only events`
4. `Sse.stream reads events from ChannelReader`
5. `Sse.stream stops when channel completes`
6. `Sse.handler sets Cache-Control no-cache`
7. `Sse.handler sets Connection keep-alive`
8. `Sse.handler does not match POST requests`

### Partial (6 tests)

1. `middleware strips HTML shell when X-Fire-Navigation is present`
2. `middleware sets X-Fire-Title header`
3. `middleware passes through non-HTML responses unchanged`
4. `middleware passes through when header is absent`
5. `middleware handles missing title gracefully`
6. `middleware handles response without body tags`

### CSRF (5 tests)

1. `cookie includes SameSite=Strict`
2. `cookie includes HttpOnly`
3. `cookie includes Secure when HTTPS`
4. `hiddenInput returns input node with token`
5. `metaTag returns meta node with token`

## Files

### Create
- `src/Fire/Sse.fs` — SseEvent type, SseWriter, Sse.handler, Sse.stream
- `src/Fire/Partial.fs` — Partial.middleware
- `tests/Fire.Tests/SseTests.fs`
- `tests/Fire.Tests/PartialTests.fs`
- `tests/Fire.Tests/CsrfPolishTests.fs`

### Modify
- `src/Fire/Csrf.fs` — hardened cookie, hiddenInput, metaTag
- `src/Fire/Fire.fsproj` — add Sse.fs and Partial.fs compile entries (before App.fs)
- `tests/Fire.Tests/Fire.Tests.fsproj` — add test file compile entries
- `packages/fire-react/src/index.tsx` — check X-Fire-Title header in navigate()

### Compile order

```xml
<Compile Include="Redirect.fs" />
<Compile Include="Sse.fs" />
<Compile Include="Partial.fs" />
<Compile Include="App.fs" />
```

## Implementation Order

SSE first (independent, largest), then Partial (independent), then CSRF polish (smallest). All three are independent and could be parallelized.
