# Tier 2 Features Design

Tier 2 covers five developer experience features: logging middleware, static file serving, content negotiation, redirect helper, and caching helpers.

## 1. Logging Middleware

Three entry points sharing a common `LogEntry` type:

```fsharp
type LogEntry = { Method: string; Path: string; Status: int; Duration: TimeSpan }

module Log =
    let withOutput (output: LogEntry -> unit) : Middleware   // core primitive
    let toConsole : Middleware                                // Console.WriteLine
    let toLogger (logger: ILogger) : Middleware               // Microsoft.Extensions.Logging
```

`withOutput` is the primitive — wraps `next`, measures duration with Stopwatch, calls user function after response. `toConsole` and `toLogger` are convenience wrappers.

New file: `src/Fire/Log.fs`

## 2. Static File Serving

Handler factory that serves files from a directory. Used with wildcard routes:

```fsharp
module Static =
    let serve (rootDir: string) : Handler

// Usage:
Route.get "/static/*path" (Static.serve "./wwwroot")
```

Built-in MIME type mapping for common extensions (.html, .css, .js, .json, .png, .jpg, .gif, .svg, .ico, .woff2, .txt). Falls back to `application/octet-stream`. Directory traversal prevented via `Path.GetFullPath` check.

New file: `src/Fire/Static.fs`

## 3. Content Negotiation

Two new members on Request:

```fsharp
member _.Accepts (mediaType: string) : bool
    // Checks if Accept header contains the media type

member _.ContentType : string option
    // Returns request Content-Type or None
```

Both are simple header reads, no complex quality-value parsing.

## 4. Redirect Helper

```fsharp
module Response =
    let redirect url code r =
        { r with Status = code; Headers = ("Location", url) :: r.Headers }
```

## 5. ETag & Cache-Control Helpers

```fsharp
module Response =
    let etag tag r = r |> header "ETag" tag
    let cacheControl value r = r |> header "Cache-Control" value
```

## File Changes

**New files:** `src/Fire/Log.fs`, `src/Fire/Static.fs`
**Modified:** `src/Fire/Request.fs`, `src/Fire/Response.fs`

**Compile order:** Request.fs, Response.fs, Cookie.fs, Types.fs, Trie.fs, Route.fs, Log.fs, Static.fs, Cors.fs, App.fs
