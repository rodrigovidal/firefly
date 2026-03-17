module Fire.IntegrationTests.BlogApiTests

open System
open System.Collections.Generic
open System.Threading
open Xunit
open FsUnit.Xunit
open Fire

type Post = { Id: int; Title: string; Body: string; Tags: string list; CreatedAt: DateTime }
type Comment = { Id: int; PostId: int; Author: string; Body: string; CreatedAt: DateTime }
type CreatePost = { Title: string; Body: string; Tags: string list }
type CreateComment = { Author: string; Body: string }

let buildBlogApp () =
    let posts = ResizeArray<Post>()
    let comments = ResizeArray<Comment>()
    let mutable nextPostId = 1
    let mutable nextCommentId = 1

    // Seed
    let now = DateTime.UtcNow
    posts.AddRange([
        { Id = 1; Title = "First Post"; Body = "Hello world"; Tags = ["fsharp"; "fire"]; CreatedAt = now }
        { Id = 2; Title = "Second Post"; Body = "More content"; Tags = ["dotnet"]; CreatedAt = now }
    ])
    nextPostId <- 3
    comments.Add({ Id = 1; PostId = 1; Author = "Alice"; Body = "Great post!"; CreatedAt = now })
    nextCommentId <- 2

    let routes =
        Route.start
        |> Route.group "/api" (fun api ->
            api
            |> Route.group "/posts" (fun postsGroup ->
                postsGroup
                |> Route.get "" (fun req -> task {
                    let items =
                        match req.QueryParam "tag" with
                        | Some tag -> posts |> Seq.filter (fun p -> p.Tags |> List.contains tag) |> Seq.toList
                        | None -> posts |> Seq.toList
                    if req.Accepts "text/plain" then
                        return Response.text (items |> List.map (fun p -> $"[{p.Id}] {p.Title}") |> String.concat "\n")
                    else
                        return Response.json items
                })
                |> Route.get "/:id" (fun req -> task {
                    let id = int req.Params.["id"]
                    match posts |> Seq.tryFind (fun p -> p.Id = id) with
                    | Some post ->
                        return
                            Response.json post
                            |> Response.etag $"\"{post.Id}-{post.Title.GetHashCode()}\""
                            |> Response.cacheControl "public, max-age=60"
                            |> Cookie.set "last-visited" $"post-{id}" Cookie.defaults
                    | None -> return Response.json {| error = "not found" |} |> Response.status 404
                })
                |> Route.post "" (fun req -> task {
                    let! input = req.Json<CreatePost>()
                    if String.IsNullOrWhiteSpace input.Title then
                        return Response.json {| error = "Title required" |} |> Response.status 400
                    else
                        let id = nextPostId
                        nextPostId <- nextPostId + 1
                        let post = { Id = id; Title = input.Title; Body = input.Body; Tags = input.Tags; CreatedAt = DateTime.UtcNow }
                        posts.Add(post)
                        return Response.json post |> Response.status 201
                })
                |> Route.group "/:id/comments" (fun cg ->
                    cg
                    |> Route.get "" (fun req -> task {
                        let id = int req.Params.["id"]
                        let items = comments |> Seq.filter (fun c -> c.PostId = id) |> Seq.toList
                        return Response.json items
                    })
                    |> Route.post "" (fun req -> task {
                        let id = int req.Params.["id"]
                        let! input = req.Json<CreateComment>()
                        if String.IsNullOrWhiteSpace input.Author then
                            return Response.json {| error = "Author required" |} |> Response.status 400
                        else
                            let id = nextCommentId
                            nextCommentId <- nextCommentId + 1
                            let comment = { Id = id; PostId = id; Author = input.Author; Body = input.Body; CreatedAt = DateTime.UtcNow }
                            comments.Add(comment)
                            return Response.json comment |> Response.status 201
                    })
                )
            )
            |> Route.get "/tags" (fun _ -> task {
                let tags = posts |> Seq.collect (fun p -> p.Tags) |> Seq.distinct |> Seq.sort |> Seq.toList
                return Response.json tags
            })
        )
        |> Route.get "/feed" (fun _ -> task {
            return Response.ok |> Response.redirect "/api/posts" 302
        })

    let config =
        App.defaults
        |> App.port 0
        |> App.onError (fun ex _ -> task {
            return Response.json {| error = ex.Message |} |> Response.status 500
        })

    (routes, config)

// --- Tests ---

[<Fact>]
let ``Blog: list posts returns seeded data`` () = task {
    let (routes, config) = buildBlogApp ()
    let! client = TestClient.start routes config
    let! r = client |> TestClient.get "/api/posts"
    r.Status |> should equal 200
    r.Body |> should haveSubstring "First Post"
    r.Body |> should haveSubstring "Second Post"
    do! TestClient.stop client
}

[<Fact>]
let ``Blog: filter posts by tag`` () = task {
    let (routes, config) = buildBlogApp ()
    let! client = TestClient.start routes config
    let! r = client |> TestClient.get "/api/posts?tag=fire"
    r.Status |> should equal 200
    r.Body |> should haveSubstring "First Post"
    r.Body |> should not' (haveSubstring "Second Post")
    do! TestClient.stop client
}

[<Fact>]
let ``Blog: content negotiation returns plain text`` () = task {
    let (routes, config) = buildBlogApp ()
    let! client = TestClient.start routes config
    let c = client |> TestClient.withHeader "Accept" "text/plain"
    let! r = c |> TestClient.get "/api/posts"
    r.Status |> should equal 200
    r.Body |> should haveSubstring "[1] First Post"
    do! TestClient.stop client
}

[<Fact>]
let ``Blog: get post sets ETag and Cache-Control`` () = task {
    let (routes, config) = buildBlogApp ()
    let! client = TestClient.start routes config
    let! r = client |> TestClient.get "/api/posts/1"
    r.Status |> should equal 200
    r.Headers |> List.exists (fun (k, _) -> k = "ETag") |> should be True
    r.Headers |> List.exists (fun (k, _) -> k = "Cache-Control") |> should be True
    do! TestClient.stop client
}

[<Fact>]
let ``Blog: get post sets cookie`` () = task {
    let (routes, config) = buildBlogApp ()
    let! client = TestClient.start routes config
    let! r = client |> TestClient.get "/api/posts/1"
    r.Headers |> List.exists (fun (k, v) -> k = "Set-Cookie" && v.Contains("last-visited")) |> should be True
    do! TestClient.stop client
}

[<Fact>]
let ``Blog: create post`` () = task {
    let (routes, config) = buildBlogApp ()
    let! client = TestClient.start routes config
    let! r = client |> TestClient.post "/api/posts" """{"Title":"New","Body":"Content","Tags":["test"]}"""
    r.Status |> should equal 201
    r.Body |> should haveSubstring "New"
    do! TestClient.stop client
}

[<Fact>]
let ``Blog: create post validates title`` () = task {
    let (routes, config) = buildBlogApp ()
    let! client = TestClient.start routes config
    let! r = client |> TestClient.post "/api/posts" """{"Title":"","Body":"x","Tags":[]}"""
    r.Status |> should equal 400
    do! TestClient.stop client
}

[<Fact>]
let ``Blog: list and create comments`` () = task {
    let (routes, config) = buildBlogApp ()
    let! client = TestClient.start routes config
    // List existing
    let! r1 = client |> TestClient.get "/api/posts/1/comments"
    r1.Status |> should equal 200
    r1.Body |> should haveSubstring "Alice"
    // Create new
    let! r2 = client |> TestClient.post "/api/posts/1/comments" """{"Author":"Bob","Body":"Nice!"}"""
    r2.Status |> should equal 201
    r2.Body |> should haveSubstring "Bob"
    do! TestClient.stop client
}

[<Fact>]
let ``Blog: list tags`` () = task {
    let (routes, config) = buildBlogApp ()
    let! client = TestClient.start routes config
    let! r = client |> TestClient.get "/api/tags"
    r.Status |> should equal 200
    r.Body |> should haveSubstring "fsharp"
    r.Body |> should haveSubstring "fire"
    do! TestClient.stop client
}

[<Fact>]
let ``Blog: feed redirects to posts`` () = task {
    let (routes, config) = buildBlogApp ()
    let! client = TestClient.start routes config
    // HttpClient follows redirects automatically, so /feed -> /api/posts returns the posts data
    let! r = client |> TestClient.get "/feed"
    r.Status |> should equal 200
    r.Body |> should haveSubstring "First Post"
    do! TestClient.stop client
}

[<Fact>]
let ``Blog: get nonexistent post returns 404`` () = task {
    let (routes, config) = buildBlogApp ()
    let! client = TestClient.start routes config
    let! r = client |> TestClient.get "/api/posts/999"
    r.Status |> should equal 404
    do! TestClient.stop client
}
