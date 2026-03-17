module Fire.Tests.TrieTests

open Xunit
open FsUnit.Xunit
open Fire

[<Fact>]
let ``Trie matches static route`` () =
    let trie =
        Trie.empty
        |> Trie.add "GET" "/hello" [] (fun _ -> task { return Response.ok })
    let result = Trie.lookup "GET" "/hello" trie
    result |> Option.isSome |> should be True

[<Fact>]
let ``Trie returns None for unmatched path`` () =
    let trie =
        Trie.empty
        |> Trie.add "GET" "/hello" [] (fun _ -> task { return Response.ok })
    let result = Trie.lookup "GET" "/world" trie
    result |> Option.isNone |> should be True

[<Fact>]
let ``Trie returns None for unmatched method`` () =
    let trie =
        Trie.empty
        |> Trie.add "GET" "/hello" [] (fun _ -> task { return Response.ok })
    let result = Trie.lookup "POST" "/hello" trie
    result |> Option.isNone |> should be True

[<Fact>]
let ``Trie captures route params`` () =
    let trie =
        Trie.empty
        |> Trie.add "GET" "/users/:id" [] (fun _ -> task { return Response.ok })
    let result = Trie.lookup "GET" "/users/42" trie
    result |> Option.isSome |> should be True
    let (_, ps) = result.Value
    ps.["id"] |> should equal "42"

[<Fact>]
let ``Trie captures multiple route params`` () =
    let trie =
        Trie.empty
        |> Trie.add "GET" "/users/:userId/posts/:postId" [] (fun _ -> task { return Response.ok })
    let result = Trie.lookup "GET" "/users/7/posts/99" trie
    result |> Option.isSome |> should be True
    let (_, ps) = result.Value
    ps.["userId"] |> should equal "7"
    ps.["postId"] |> should equal "99"

[<Fact>]
let ``Trie distinguishes between methods on same path`` () =
    let handlerGet = fun _ -> task { return Response.text "get" }
    let handlerPost = fun _ -> task { return Response.text "post" }
    let trie =
        Trie.empty
        |> Trie.add "GET" "/items" [] handlerGet
        |> Trie.add "POST" "/items" [] handlerPost
    let (hGet, _) = (Trie.lookup "GET" "/items" trie).Value
    let (hPost, _) = (Trie.lookup "POST" "/items" trie).Value
    let rGet = hGet (Unchecked.defaultof<Request>) |> Async.AwaitTask |> Async.RunSynchronously
    let rPost = hPost (Unchecked.defaultof<Request>) |> Async.AwaitTask |> Async.RunSynchronously
    rGet.Body |> should equal (Text "get")
    rPost.Body |> should equal (Text "post")

[<Fact>]
let ``Trie matches root path`` () =
    let trie =
        Trie.empty
        |> Trie.add "GET" "/" [] (fun _ -> task { return Response.ok })
    let result = Trie.lookup "GET" "/" trie
    result |> Option.isSome |> should be True

[<Fact>]
let ``Trie static segment takes priority over param`` () =
    let trie =
        Trie.empty
        |> Trie.add "GET" "/users/me" [] (fun _ -> task { return Response.text "me" })
        |> Trie.add "GET" "/users/:id" [] (fun _ -> task { return Response.text "param" })
    let (hMe, _) = (Trie.lookup "GET" "/users/me" trie).Value
    let (hParam, _) = (Trie.lookup "GET" "/users/42" trie).Value
    let rMe = hMe (Unchecked.defaultof<Request>) |> Async.AwaitTask |> Async.RunSynchronously
    let rParam = hParam (Unchecked.defaultof<Request>) |> Async.AwaitTask |> Async.RunSynchronously
    rMe.Body |> should equal (Text "me")
    rParam.Body |> should equal (Text "param")

// --- Coverage: tryWildcard with wrong method (line 101) ---

[<Fact>]
let ``Trie wildcard returns None for wrong method`` () =
    let trie =
        Trie.empty
        |> Trie.add "GET" "/files/*path" [] (fun _ -> task { return Response.ok })
    let result = Trie.lookup "POST" "/files/a/b/c" trie
    result |> Option.isNone |> should be True

// --- Coverage: tryParam falls through to tryWildcard (line 89) ---

[<Fact>]
let ``Trie param child falls through to wildcard when param path has no handler`` () =
    let trie =
        Trie.empty
        |> Trie.add "GET" "/:id/details" [] (fun _ -> task { return Response.text "details" })
        |> Trie.add "GET" "/*path" [] (fun _ -> task { return Response.text "wildcard" })
    // "/something" does not match "/:id/details" (too short), should fallback to wildcard
    let result = Trie.lookup "GET" "/something" trie
    result |> Option.isSome |> should be True

// --- Coverage: Trie root path no method match (line 109) ---

[<Fact>]
let ``Trie root path returns None for unmatched method`` () =
    let trie =
        Trie.empty
        |> Trie.add "GET" "/" [] (fun _ -> task { return Response.ok })
    let result = Trie.lookup "POST" "/" trie
    result |> Option.isNone |> should be True

// --- Coverage: Trie.add with existing wildcard child (line 50) ---

[<Fact>]
let ``Trie add wildcard with existing wildcard adds method`` () =
    let trie =
        Trie.empty
        |> Trie.add "GET" "/files/*path" [] (fun _ -> task { return Response.text "get" })
        |> Trie.add "POST" "/files/*path" [] (fun _ -> task { return Response.text "post" })
    let getResult = Trie.lookup "GET" "/files/a/b" trie
    let postResult = Trie.lookup "POST" "/files/a/b" trie
    getResult |> Option.isSome |> should be True
    postResult |> Option.isSome |> should be True

[<Fact>]
let ``Trie pre-composes middleware chain`` () =
    let mutable callOrder = []
    let mw1 : Middleware = fun next req -> task {
        callOrder <- callOrder @ ["mw1"]
        return! next req
    }
    let mw2 : Middleware = fun next req -> task {
        callOrder <- callOrder @ ["mw2"]
        return! next req
    }
    let handler : Handler = fun _ -> task {
        callOrder <- callOrder @ ["handler"]
        return Response.ok
    }
    let trie =
        Trie.empty
        |> Trie.add "GET" "/test" [mw1; mw2] handler
    let (h, _) = (Trie.lookup "GET" "/test" trie).Value
    h (Unchecked.defaultof<Request>) |> Async.AwaitTask |> Async.RunSynchronously |> ignore
    callOrder |> should equal ["mw1"; "mw2"; "handler"]

// --- Coverage: ParamChild reuse (line 50) ---

[<Fact>]
let ``Trie reuses existing param child node`` () =
    let trie =
        Trie.empty
        |> Trie.add "GET" "/items/:id" [] (fun _ -> task { return Response.text "get" })
        |> Trie.add "POST" "/items/:id" [] (fun _ -> task { return Response.text "post" })
    let (hGet, ps1) = (Trie.lookup "GET" "/items/42" trie).Value
    let (hPost, ps2) = (Trie.lookup "POST" "/items/42" trie).Value
    ps1.["id"] |> should equal "42"
    ps2.["id"] |> should equal "42"
    let rGet = hGet (Unchecked.defaultof<Request>) |> Async.AwaitTask |> Async.RunSynchronously
    let rPost = hPost (Unchecked.defaultof<Request>) |> Async.AwaitTask |> Async.RunSynchronously
    rGet.Body |> should equal (Text "get")
    rPost.Body |> should equal (Text "post")
