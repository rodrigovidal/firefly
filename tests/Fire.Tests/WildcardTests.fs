module Fire.Tests.WildcardTests

open Xunit
open FsUnit.Xunit
open Fire

[<Fact>]
let ``Wildcard captures remaining path segments`` () =
    let trie =
        Trie.empty
        |> Trie.add "GET" "/static/*path" [] (fun _ -> task { return Response.ok })
    let result = Trie.lookup "GET" "/static/css/app.css" trie
    result |> Option.isSome |> should be True
    let (_, ps) = result.Value
    ps.["path"] |> should equal "css/app.css"

[<Fact>]
let ``Wildcard captures single segment`` () =
    let trie =
        Trie.empty
        |> Trie.add "GET" "/files/*name" [] (fun _ -> task { return Response.ok })
    let result = Trie.lookup "GET" "/files/readme.txt" trie
    result |> Option.isSome |> should be True
    let (_, ps) = result.Value
    ps.["name"] |> should equal "readme.txt"

[<Fact>]
let ``Wildcard captures deeply nested path`` () =
    let trie =
        Trie.empty
        |> Trie.add "GET" "/assets/*path" [] (fun _ -> task { return Response.ok })
    let result = Trie.lookup "GET" "/assets/js/lib/vue/dist/vue.min.js" trie
    result |> Option.isSome |> should be True
    let (_, ps) = result.Value
    ps.["path"] |> should equal "js/lib/vue/dist/vue.min.js"

[<Fact>]
let ``Static takes priority over wildcard`` () =
    let trie =
        Trie.empty
        |> Trie.add "GET" "/files/special" [] (fun _ -> task { return Response.text "static" })
        |> Trie.add "GET" "/files/*path" [] (fun _ -> task { return Response.text "wildcard" })
    let (h, _) = (Trie.lookup "GET" "/files/special" trie).Value
    let r = h (Unchecked.defaultof<Request>) |> Async.AwaitTask |> Async.RunSynchronously
    r.Body |> should equal (ResponseBody.Text "static")

[<Fact>]
let ``Param takes priority over wildcard`` () =
    let trie =
        Trie.empty
        |> Trie.add "GET" "/users/:id" [] (fun _ -> task { return Response.text "param" })
        |> Trie.add "GET" "/users/*rest" [] (fun _ -> task { return Response.text "wildcard" })
    let (h, _) = (Trie.lookup "GET" "/users/42" trie).Value
    let r = h (Unchecked.defaultof<Request>) |> Async.AwaitTask |> Async.RunSynchronously
    r.Body |> should equal (ResponseBody.Text "param")

[<Fact>]
let ``Wildcard returns None when no segments to capture`` () =
    let trie =
        Trie.empty
        |> Trie.add "GET" "/static/*path" [] (fun _ -> task { return Response.ok })
    let result = Trie.lookup "GET" "/static" trie
    result |> Option.isNone |> should be True

[<Fact>]
let ``Wildcard distinguishes methods`` () =
    let trie =
        Trie.empty
        |> Trie.add "GET" "/api/*rest" [] (fun _ -> task { return Response.text "get" })
        |> Trie.add "POST" "/api/*rest" [] (fun _ -> task { return Response.text "post" })
    let (hGet, _) = (Trie.lookup "GET" "/api/foo/bar" trie).Value
    let (hPost, _) = (Trie.lookup "POST" "/api/foo/bar" trie).Value
    let rGet = hGet (Unchecked.defaultof<Request>) |> Async.AwaitTask |> Async.RunSynchronously
    let rPost = hPost (Unchecked.defaultof<Request>) |> Async.AwaitTask |> Async.RunSynchronously
    rGet.Body |> should equal (ResponseBody.Text "get")
    rPost.Body |> should equal (ResponseBody.Text "post")
