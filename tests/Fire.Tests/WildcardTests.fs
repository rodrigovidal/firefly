module Fire.Tests.WildcardTests

open Xunit
open FsUnit.Xunit
open Firefly

[<Fact>]
let ``Wildcard captures remaining path segments`` () =
    let trie =
        Trie.empty
        |> Trie.add "GET" "/static/*path" [] (fun _ -> task { return Response.ok })
    let result = Trie.lookup "GET" "/static/css/app.css" trie
    result |> ValueOption.isSome |> should be True
    let struct(_, ps) = result.Value
    ps.["path"] |> should equal "css/app.css"

[<Fact>]
let ``Wildcard captures single segment`` () =
    let trie =
        Trie.empty
        |> Trie.add "GET" "/files/*name" [] (fun _ -> task { return Response.ok })
    let result = Trie.lookup "GET" "/files/readme.txt" trie
    result |> ValueOption.isSome |> should be True
    let struct(_, ps) = result.Value
    ps.["name"] |> should equal "readme.txt"

[<Fact>]
let ``Wildcard captures deeply nested path`` () =
    let trie =
        Trie.empty
        |> Trie.add "GET" "/assets/*path" [] (fun _ -> task { return Response.ok })
    let result = Trie.lookup "GET" "/assets/js/lib/vue/dist/vue.min.js" trie
    result |> ValueOption.isSome |> should be True
    let struct(_, ps) = result.Value
    ps.["path"] |> should equal "js/lib/vue/dist/vue.min.js"

[<Fact>]
let ``Static takes priority over wildcard`` () =
    let trie =
        Trie.empty
        |> Trie.add "GET" "/files/special" [] (fun _ -> task { return Response.text "static" })
        |> Trie.add "GET" "/files/*path" [] (fun _ -> task { return Response.text "wildcard" })
    let struct(h, _) = (Trie.lookup "GET" "/files/special" trie).Value
    let r = h (Unchecked.defaultof<Request>) |> Async.AwaitTask |> Async.RunSynchronously
    match r.Body with
    | ResponseBody.Text s -> s |> should equal "static"
    | _ -> failwith "expected Text body"

[<Fact>]
let ``Param takes priority over wildcard`` () =
    let trie =
        Trie.empty
        |> Trie.add "GET" "/users/:id" [] (fun _ -> task { return Response.text "param" })
        |> Trie.add "GET" "/users/*rest" [] (fun _ -> task { return Response.text "wildcard" })
    let struct(h, _) = (Trie.lookup "GET" "/users/42" trie).Value
    let r = h (Unchecked.defaultof<Request>) |> Async.AwaitTask |> Async.RunSynchronously
    match r.Body with
    | ResponseBody.Text s -> s |> should equal "param"
    | _ -> failwith "expected Text body"

[<Fact>]
let ``Wildcard returns None when no segments to capture`` () =
    let trie =
        Trie.empty
        |> Trie.add "GET" "/static/*path" [] (fun _ -> task { return Response.ok })
    let result = Trie.lookup "GET" "/static" trie
    result |> ValueOption.isNone |> should be True

[<Fact>]
let ``Wildcard distinguishes methods`` () =
    let trie =
        Trie.empty
        |> Trie.add "GET" "/api/*rest" [] (fun _ -> task { return Response.text "get" })
        |> Trie.add "POST" "/api/*rest" [] (fun _ -> task { return Response.text "post" })
    let struct(hGet, _) = (Trie.lookup "GET" "/api/foo/bar" trie).Value
    let struct(hPost, _) = (Trie.lookup "POST" "/api/foo/bar" trie).Value
    let rGet = hGet (Unchecked.defaultof<Request>) |> Async.AwaitTask |> Async.RunSynchronously
    let rPost = hPost (Unchecked.defaultof<Request>) |> Async.AwaitTask |> Async.RunSynchronously
    match rGet.Body with
    | ResponseBody.Text s -> s |> should equal "get"
    | _ -> failwith "expected Text body"
    match rPost.Body with
    | ResponseBody.Text s -> s |> should equal "post"
    | _ -> failwith "expected Text body"
