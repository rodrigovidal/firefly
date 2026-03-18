module Fire.Tests.QueryTests

open Xunit
open FsUnit.Xunit
open Fire

[<Fact>]
let ``QueryCache starts empty`` () =
    let cache = QueryCache()
    cache.Entries |> should equal List.empty<QueryEntry>

[<Fact>]
let ``QueryCache.Add stores entry`` () =
    let cache = QueryCache()
    cache.Add("user-1", {| name = "Alice" |})
    cache.Entries |> should haveLength 1
    cache.Entries.[0].Key |> should equal "user-1"

[<Fact>]
let ``QueryCache.Add stores multiple entries`` () =
    let cache = QueryCache()
    cache.Add("user-1", {| name = "Alice" |})
    cache.Add("user-2", {| name = "Bob" |})
    cache.Entries |> should haveLength 2

[<Fact>]
let ``DehydrateScript returns Empty when no entries`` () =
    let cache = QueryCache()
    cache.DehydrateScript() |> should equal Empty

[<Fact>]
let ``DehydrateScript returns Raw script with JSON`` () =
    let cache = QueryCache()
    cache.Add("user-1", {| name = "Alice" |})
    let node = cache.DehydrateScript()
    match node with
    | Raw s ->
        s |> should haveSubstring "<script>"
        s |> should haveSubstring "__FIRE_QUERY_STATE__"
        s |> should haveSubstring "user-1"
        s |> should haveSubstring "Alice"
    | _ -> failwith "expected Raw node"

[<Fact>]
let ``DehydrateScript produces valid TanStack Query dehydrate format`` () =
    let cache = QueryCache()
    cache.Add("key-1", {| x = 1 |})
    match cache.DehydrateScript() with
    | Raw s ->
        s |> should haveSubstring "\"mutations\":[]"
        s |> should haveSubstring "\"queries\":["
        s |> should haveSubstring "\"queryKey\""
        s |> should haveSubstring "\"status\":\"success\""
        s |> should haveSubstring "\"dataUpdateCount\":1"
        s |> should haveSubstring "\"data\""
    | _ -> failwith "expected Raw node"

[<Fact>]
let ``DehydrateScript with multiple entries separates with comma`` () =
    let cache = QueryCache()
    cache.Add("a", {| v = 1 |})
    cache.Add("b", {| v = 2 |})
    match cache.DehydrateScript() with
    | Raw s ->
        s |> should haveSubstring "\"a\""
        s |> should haveSubstring "\"b\""
        s |> should haveSubstring "},{"
    | _ -> failwith "expected Raw node"

[<Fact>]
let ``Query.prefetch executes fetch and stores in cache`` () = task {
    let cache = QueryCache()
    let! result = Query.prefetch "user-1" (fun () -> task { return {| name = "Alice" |} }) cache
    result.name |> should equal "Alice"
    cache.Entries |> should haveLength 1
    cache.Entries.[0].Key |> should equal "user-1"
}

[<Fact>]
let ``Query.prefetch returns the fetched value`` () = task {
    let cache = QueryCache()
    let! user = Query.prefetch "u" (fun () -> task { return {| id = 42 |} }) cache
    user.id |> should equal 42
}

[<Fact>]
let ``Query.prefetch multiple calls accumulate in cache`` () = task {
    let cache = QueryCache()
    let! _ = Query.prefetch "a" (fun () -> task { return 1 }) cache
    let! _ = Query.prefetch "b" (fun () -> task { return 2 }) cache
    cache.Entries |> should haveLength 2
}
