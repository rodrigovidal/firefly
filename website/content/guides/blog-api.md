---
title: "Blog API"
description: "A blog REST API showing nested route groups, schema validation, content negotiation, ETags, and cookies."
group: "Guides"
order: 2
---

# Blog API

The blog-api example is an in-memory REST API for posts, comments, and tags. It is a single F# module that wires up nested route groups, schema-validated request bodies, and rich response building. Use it as a tour of how Firefly's routing, validation, and response helpers fit together in a realistic app.

## What you'll learn

- Nested route groups with `Route.group` and typed path parameters (`/%i`)
- Schema validation two ways: `Schema.fromType` and the manual `schema { }` builder
- Parsing request bodies with `Schema.parseRequest`
- Content negotiation via `req.Accepts` (JSON vs. plain text)
- Conditional responses with ETags and `If-None-Match` (304 Not Modified)
- Setting `Cache-Control`, custom headers, and cookies on responses
- Redirects and a global error handler
- App startup, port config, and the `Log.toConsole` middleware

## Types and schemas

Records model the domain. Firefly generates a JSON schema straight from a record type with `Schema.fromType`; optional fields (like `Tags`) become optional in the schema.

```fsharp
type CreatePostInput = { Title: string; Body: string; Tags: string list option }

// fromType: auto-generates schema from record type (Tags is optional list)
let createPostSchema = Schema.fromType<CreatePostInput>()
```

When you need validation rules, use the `schema { }` computation expression. Each `Schema.required` field names the field, its type, and a list of constraints.

```fsharp
let createCommentSchema = schema {
    let! author = Schema.required "Author" Schema.string [ Schema.nonempty; Schema.maxLength 50; Schema.trim ]
    let! body = Schema.required "Body" Schema.string [ Schema.nonempty; Schema.maxLength 1000 ]
    return {| Author = author; Body = body |}
}
```

## Content negotiation

The list handler reads an optional `?tag=` query parameter, then inspects the `Accept` header. If the client accepts `text/plain` it returns a plain-text listing; otherwise it returns JSON.

```fsharp
let listPosts: Handler =
    fun req ->
        task {
            let items =
                match req.QueryParam "tag" with
                | Some tag ->
                    posts
                    |> Seq.filter (fun p ->
                        p.Tags
                        |> List.exists (fun t -> String.Equals(t, tag, StringComparison.OrdinalIgnoreCase)))
                    |> Seq.toList
                | None -> posts |> Seq.toList

            if req.Accepts "text/plain" then
                let text =
                    items |> List.map (fun p -> $"[{p.Id}] {p.Title}") |> String.concat "\n"
                return Response.text text
            else
                return Response.json items
        }
```

## ETags, cache headers, and cookies

`getPost` computes an ETag for the post. If the client's `If-None-Match` matches, it returns a bare `304`. Otherwise it builds a JSON response and pipes it through `Response.etag`, `Response.cacheControl`, and `Cookie.set`.

```fsharp
let getPost (id: int) (req: Request) =
    task {
        match posts |> Seq.tryFind (fun p -> p.Id = id) with
        | Some post ->
            let etag = computeETag post
            match req.Header "If-None-Match" with
            | Some clientTag when clientTag = etag ->
                return { Status = 304; Headers = []; Body = ResponseBody.Empty }
            | _ ->
                let now = DateTime.UtcNow.ToString("o")
                return
                    Response.json post
                    |> Response.etag etag
                    |> Response.cacheControl "public, max-age=60"
                    |> Cookie.set "last-visited" $"post-{id}-at-{now}" Cookie.defaults
        | None -> return notFoundJson "Post not found"
    }
```

The ETag is a quoted hash of the post's fields:

```fsharp
let computeETag (post: Post) =
    let hash = HashCode.Combine(post.Id, post.Title, post.Body, post.CreatedAt)
    $"\"{hash:x}\""
```

## Parsing request bodies

`Schema.parseRequest` runs a schema against the request body and returns `Result`. On success you get the typed input; on failure you return the collected errors with a 400. The handler also sets a `201` status and a `Location` header.

```fsharp
let createPost (req: Request) =
    task {
        match! Schema.parseRequest createPostSchema req with
        | Ok input ->
            let id = nextPostId
            nextPostId <- nextPostId + 1
            let post =
                { Id = id; Title = input.Title; Body = input.Body
                  Tags = input.Tags |> Option.defaultValue []
                  CreatedAt = DateTime.UtcNow }
            posts.Add(post)
            return
                Response.json post
                |> Response.status 201
                |> Response.header "Location" $"/api/posts/{id}"
        | Error errors ->
            return Response.json {| errors = errors |} |> Response.status 400
    }
```

## Nested routing

Routes are built by piping `Route.start` through nested `Route.group` calls. The `/%i` segments bind a typed `int` path parameter that is passed to the handler as its first argument. A top-level `/feed` route lives outside the `/api` group.

```fsharp
let routes =
    Route.start
    |> Route.group "/api" (fun api ->
        api
        |> Route.group "/posts" (fun postsGroup ->
            postsGroup
            |> Route.get "" listPosts
            |> Route.post "" createPost
            |> Route.get "/%i" getPost
            |> Route.get "/%i/comments" listComments
            |> Route.post "/%i/comments" createComment)
        |> Route.get "/tags" listTags)
    |> Route.get "/feed" feedRedirect
```

The `/feed` handler shows a redirect, built from `Response.ok` and `Response.redirect`:

```fsharp
let feedRedirect () =
    task { return Response.ok |> Response.redirect "/api/posts" 302 }
```

## App startup

`App.create` returns the routes plus a config built from `App.defaults`, with a port and a global error handler. The error handler turns any unhandled exception into a 500 JSON response.

```fsharp
let errorHandler (ex: exn) (_req: Request) =
    task {
        Console.Error.WriteLine($"[ERROR] {ex.Message}")
        return Response.json {| error = "Internal server error" |} |> Response.status 500
    }

let config =
    App.defaults
    |> App.port 3000
    |> App.onError errorHandler
```

`Program.fs` adds the `Log.toConsole` middleware and runs the app:

```fsharp
open System.Threading
open Firefly
open BlogApi

let (routes, config) = App.create ()
let config' = config |> App.middleware Log.toConsole

App.run routes config' CancellationToken.None
|> fun t -> t.GetAwaiter().GetResult()
```

## Running it

```bash
dotnet run --project examples/blog-api
```

The server listens on `http://localhost:3000`. Try the routes:

```bash
# List posts as JSON, or filter by tag
curl http://localhost:3000/api/posts
curl "http://localhost:3000/api/posts?tag=fsharp"

# Negotiate plain text instead of JSON
curl -H "Accept: text/plain" http://localhost:3000/api/posts

# Get one post (note the ETag and Set-Cookie headers)
curl -i http://localhost:3000/api/posts/1

# Create a post
curl -X POST http://localhost:3000/api/posts \
  -H "Content-Type: application/json" \
  -d '{"Title":"Hello","Body":"First post","Tags":["intro"]}'
```

## Source

Full source: [`examples/blog-api/`](https://github.com/) — `App.fs` (handlers, routing, config) and `Program.fs` (middleware and startup).
