# Tier 1 Features Design

Tier 1 covers five features essential for building real APIs with Fire: body parsing, query helpers, wildcard routes, response cookies, and CORS middleware.

## 1. Request Extensions

Three new members on `[<Struct>] Request`:

```fsharp
member _.Text() : Task<string>
    // Reads body stream as UTF-8 string

member _.Form() : Task<IReadOnlyDictionary<string, string>>
    // Parses application/x-www-form-urlencoded body via ctx.Request.ReadFormAsync()

member _.QueryParam (name: string) : string option
    // Sync lookup into existing Query collection
```

`Text()` and `Form()` are async (read from body stream). `QueryParam` is sync (dictionary lookup). Body-reading methods bind `ctx.Request.Body` to a local before entering `task` block (struct limitation).

## 2. Wildcard Routes

The trie gains a `WildcardChild` for catch-all segments. A `*name` segment matches all remaining path segments and captures them joined with `/` (no leading slash).

```fsharp
type TrieNode = {
    StaticChildren: Map<string, TrieNode>
    ParamChild: (string * TrieNode) option
    WildcardChild: (string * Map<string, Handler>) option  // (name, handlers by method)
    Handlers: Map<string, Handler>
}
```

Wildcard is always a leaf — nothing can follow `*path`. Priority: static > param > wildcard.

```fsharp
// Registration
Route.get "/static/*path" handler

// GET /static/css/app.css -> path = "css/app.css"
// GET /static/js/lib/vue.js -> path = "js/lib/vue.js"
```

## 3. Response Cookies

Two functions: `Response.cookie` for bare cookies, `Response.cookieWith` for configured cookies.

```fsharp
type CookieOptions = {
    MaxAge: int option
    Path: string option
    Domain: string option
    Secure: bool
    HttpOnly: bool
    SameSite: string option  // "Strict", "Lax", "None"
}

[<RequireQualifiedAccess>]
module Cookie =
    let defaults = { MaxAge = None; Path = None; Domain = None
                     Secure = false; HttpOnly = false; SameSite = None }
    let maxAge seconds opts = { opts with MaxAge = Some seconds }
    let path p opts = { opts with Path = Some p }
    let domain d opts = { opts with Domain = Some d }
    let secure opts = { opts with Secure = true }
    let httpOnly opts = { opts with HttpOnly = true }
    let sameSite s opts = { opts with SameSite = Some s }
```

```fsharp
module Response =
    let cookie name value r =
        r |> header "Set-Cookie" $"{name}={value}"

    let cookieWith name value (opts: CookieOptions) r =
        // Builds Set-Cookie header with all options: Max-Age, Path, Domain, Secure, HttpOnly, SameSite
        r |> header "Set-Cookie" (buildCookieString name value opts)
```

Both produce `Set-Cookie` headers. Multiple cookies work because headers are `(string * string) list` with duplicate keys.

## 4. CORS Middleware

Two entry points: `Cors.allowAll` for dev, builder for production.

```fsharp
type CorsConfig = {
    Origins: string list      // empty = "*"
    Methods: string list      // empty = all standard methods
    Headers: string list      // empty = "*"
    MaxAge: int option
}

[<RequireQualifiedAccess>]
module Cors =
    let defaults = { Origins = []; Methods = []; Headers = []; MaxAge = None }
    let origins o config = { config with Origins = o }
    let methods m config = { config with Methods = m }
    let headers h config = { config with Headers = h }
    let maxAge s config = { config with MaxAge = Some s }

    let build (config: CorsConfig) : Middleware = ...
    let allowAll : Middleware = defaults |> build
```

Key behaviors:
- **Preflight (OPTIONS)**: Returns 204 with CORS headers, does not call `next`
- **Normal requests**: Calls `next`, adds `Access-Control-Allow-Origin` to response
- **Origin matching**: Empty `Origins` = `*`. Otherwise, check request `Origin` header against list and echo matching origin
- `allowAll` is just `defaults |> build`

## File Changes

**New files:**
- `src/Fire/Cookie.fs` — CookieOptions record + Cookie module
- `src/Fire/Cors.fs` — CorsConfig record + Cors module

**Modified files:**
- `src/Fire/Request.fs` — add `Text()`, `Form()`, `QueryParam`
- `src/Fire/Response.fs` — add `cookie`, `cookieWith`
- `src/Fire/Trie.fs` — add `WildcardChild` to TrieNode, update add/lookup

**Unchanged:** Route.fs, App.fs, Types.fs

## F# fsproj Compile Order

```
Request.fs, Response.fs, Cookie.fs, Types.fs, Trie.fs, Route.fs, Cors.fs, App.fs
```

Cookie.fs goes after Response.fs (cookie builders reference Response). Cors.fs goes after Types.fs (needs Middleware type).
