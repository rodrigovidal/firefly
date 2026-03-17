module BlogApi.App

open System
open System.Collections.Generic
open Fire

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

type Post =
    { Id: int
      Title: string
      Body: string
      Tags: string list
      CreatedAt: DateTime }

type Comment =
    { Id: int
      PostId: int
      Author: string
      Body: string
      CreatedAt: DateTime }

type CreatePost = { Title: string; Body: string; Tags: string list }

type CreateComment = { Author: string; Body: string }

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

let badRequest msg =
    Response.json {| error = msg |} |> Response.status 400

let notFoundJson msg =
    Response.json {| error = msg |} |> Response.status 404

let computeETag (post: Post) =
    let hash = HashCode.Combine(post.Id, post.Title, post.Body, post.CreatedAt)
    $"\"{hash:x}\""

// ---------------------------------------------------------------------------
// App factory
// ---------------------------------------------------------------------------

let create () =
    let posts = ResizeArray<Post>()
    let comments = ResizeArray<Comment>()
    let mutable nextPostId = 1
    let mutable nextCommentId = 1

    // Seed data
    let now = DateTime.UtcNow

    let p1 =
        { Id = 1
          Title = "Getting Started with F#"
          Body = "F# is a functional-first language on .NET."
          Tags = [ "fsharp"; "dotnet" ]
          CreatedAt = now.AddHours(-2) }

    let p2 =
        { Id = 2
          Title = "Building APIs with Fire"
          Body = "Fire is a minimal F# web framework built on Kestrel."
          Tags = [ "fsharp"; "fire"; "web" ]
          CreatedAt = now.AddHours(-1) }

    let p3 =
        { Id = 3
          Title = "Functional Patterns"
          Body = "Discriminated unions, pattern matching, and pipelines."
          Tags = [ "fsharp"; "patterns" ]
          CreatedAt = now }

    posts.AddRange([ p1; p2; p3 ])
    nextPostId <- 4

    let c1 =
        { Id = 1
          PostId = 1
          Author = "Alice"
          Body = "Great intro!"
          CreatedAt = now.AddMinutes(-30) }

    let c2 =
        { Id = 2
          PostId = 2
          Author = "Bob"
          Body = "I love Fire!"
          CreatedAt = now.AddMinutes(-10) }

    comments.AddRange([ c1; c2 ])
    nextCommentId <- 3

    // Handlers
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
                        items
                        |> List.map (fun p -> $"[{p.Id}] {p.Title}")
                        |> String.concat "\n"

                    return Response.text text
                else
                    return Response.json items
            }

    let getPost: Handler =
        fun req ->
            task {
                match req.Params.TryGetValue "postId" with
                | true, idStr ->
                    match Int32.TryParse idStr with
                    | true, id ->
                        match posts |> Seq.tryFind (fun p -> p.Id = id) with
                        | Some post ->
                            let etag = computeETag post

                            match req.Header "If-None-Match" with
                            | Some clientTag when clientTag = etag ->
                                return { Status = 304; Headers = []; Body = Empty }
                            | _ ->
                                let now = DateTime.UtcNow.ToString("o")

                                return
                                    Response.json post
                                    |> Response.etag etag
                                    |> Response.cacheControl "public, max-age=60"
                                    |> Cookie.set "last-visited" $"post-{id}-at-{now}" Cookie.defaults
                        | None -> return notFoundJson "Post not found"
                    | _ -> return badRequest "Invalid post id"
                | _ -> return badRequest "Missing post id"
            }

    let createPost: Handler =
        fun req ->
            task {
                let! input = req.Json<CreatePost>()

                if String.IsNullOrWhiteSpace input.Title then
                    return badRequest "Title is required"
                elif String.IsNullOrWhiteSpace input.Body then
                    return badRequest "Body is required"
                else
                    let id = nextPostId
                    nextPostId <- nextPostId + 1

                    let post =
                        { Id = id
                          Title = input.Title
                          Body = input.Body
                          Tags = input.Tags
                          CreatedAt = DateTime.UtcNow }

                    posts.Add(post)

                    return
                        Response.json post
                        |> Response.status 201
                        |> Response.header "Location" $"/api/posts/{id}"
            }

    let listComments: Handler =
        fun req ->
            task {
                match req.Params.TryGetValue "postId" with
                | true, idStr ->
                    match Int32.TryParse idStr with
                    | true, postId ->
                        match posts |> Seq.tryFind (fun p -> p.Id = postId) with
                        | Some _ ->
                            let items =
                                comments |> Seq.filter (fun c -> c.PostId = postId) |> Seq.toList

                            return Response.json items
                        | None -> return notFoundJson "Post not found"
                    | _ -> return badRequest "Invalid post id"
                | _ -> return badRequest "Missing post id"
            }

    let createComment: Handler =
        fun req ->
            task {
                match req.Params.TryGetValue "postId" with
                | true, idStr ->
                    match Int32.TryParse idStr with
                    | true, postId ->
                        match posts |> Seq.tryFind (fun p -> p.Id = postId) with
                        | Some _ ->
                            let! input = req.Json<CreateComment>()

                            if String.IsNullOrWhiteSpace input.Author then
                                return badRequest "Author is required"
                            elif String.IsNullOrWhiteSpace input.Body then
                                return badRequest "Body is required"
                            else
                                let id = nextCommentId
                                nextCommentId <- nextCommentId + 1

                                let comment =
                                    { Id = id
                                      PostId = postId
                                      Author = input.Author
                                      Body = input.Body
                                      CreatedAt = DateTime.UtcNow }

                                comments.Add(comment)

                                return
                                    Response.json comment
                                    |> Response.status 201
                                    |> Response.header "Location" $"/api/posts/{postId}/comments/{id}"
                        | None -> return notFoundJson "Post not found"
                    | _ -> return badRequest "Invalid post id"
                | _ -> return badRequest "Missing post id"
            }

    let listTags: Handler =
        fun _req ->
            task {
                let tags =
                    posts
                    |> Seq.collect (fun p -> p.Tags)
                    |> Seq.distinct
                    |> Seq.sort
                    |> Seq.toList

                return Response.json tags
            }

    let feedRedirect: Handler =
        fun _req ->
            task {
                return Response.ok |> Response.redirect "/api/posts" 302
            }

    let errorHandler (ex: exn) (_req: Request) =
        task {
            Console.Error.WriteLine($"[ERROR] {ex.Message}")
            return Response.json {| error = "Internal server error" |} |> Response.status 500
        }

    let routes =
        Route.start
        |> Route.group("/api", fun api ->
            api
            |> Route.group("/posts", fun postsGroup ->
                postsGroup
                |> Route.get("", listPosts)
                |> Route.post("", createPost)
                |> Route.get("/:postId", getPost)
                |> Route.group("/:postId/comments", fun commentsGroup ->
                    commentsGroup
                    |> Route.get("", listComments)
                    |> Route.post("", createComment)))
            |> Route.get("/tags", listTags))
        |> Route.get("/feed", feedRedirect)

    let config =
        App.defaults
        |> App.port 3000
        |> App.onError errorHandler

    (routes, config)
