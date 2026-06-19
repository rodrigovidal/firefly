module Fire.Tests.RequestTests

open System.Collections.Generic
open System.IO
open System.Text
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Xunit
open FsUnit.Xunit
open Firefly

let makeHttpContext (method': string) (path: string) (query: string) (headers: (string * string) list) (body: string option) =
    let ctx = DefaultHttpContext()
    ctx.Request.Method <- method'
    ctx.Request.Path <- PathString(path)
    ctx.Request.QueryString <- QueryString(query)
    for (k, v) in headers do
        ctx.Request.Headers.[k] <- Microsoft.Extensions.Primitives.StringValues(v)
    match body with
    | Some b ->
        let bytes = Encoding.UTF8.GetBytes(b)
        ctx.Request.Body <- new MemoryStream(bytes)
    | None -> ()
    ctx :> HttpContext

[<Fact>]
let ``Request exposes Path from HttpContext`` () =
    let ctx = makeHttpContext "GET" "/hello" "" [] None
    let req = Request(ctx, Dictionary<string, string>() :> IReadOnlyDictionary<_, _>)
    req.Path |> should equal "/hello"

[<Fact>]
let ``Request exposes Method from HttpContext`` () =
    let ctx = makeHttpContext "POST" "/" "" [] None
    let req = Request(ctx, Dictionary<string, string>() :> IReadOnlyDictionary<_, _>)
    req.Method |> should equal "POST"

[<Fact>]
let ``Request.Params returns route params`` () =
    let d = Dictionary<string, string>()
    d.["id"] <- "42"
    let ps = d :> IReadOnlyDictionary<_, _>
    let ctx = makeHttpContext "GET" "/users/42" "" [] None
    let req = Request(ctx, ps)
    req.Params.["id"] |> should equal "42"

[<Fact>]
let ``Request.Query parses query string`` () =
    let ctx = makeHttpContext "GET" "/search" "?q=fire&limit=10" [] None
    let req = Request(ctx, Dictionary<string, string>() :> IReadOnlyDictionary<_, _>)
    req.Query.["q"] |> should equal "fire"
    req.Query.["limit"] |> should equal "10"

[<Fact>]
let ``Request.Header returns header value`` () =
    let ctx = makeHttpContext "GET" "/" "" ["X-Custom", "test"] None
    let req = Request(ctx, Dictionary<string, string>() :> IReadOnlyDictionary<_, _>)
    req.Header "X-Custom" |> should equal (Some "test")

[<Fact>]
let ``Request.Header returns None for missing header`` () =
    let ctx = makeHttpContext "GET" "/" "" [] None
    let req = Request(ctx, Dictionary<string, string>() :> IReadOnlyDictionary<_, _>)
    req.Header "X-Missing" |> should equal None

[<Fact>]
let ``Request.Json deserializes body`` () = task {
    let ctx = makeHttpContext "POST" "/" "" [] (Some """{"name":"fire"}""")
    let req = Request(ctx, Dictionary<string, string>() :> IReadOnlyDictionary<_, _>)
    let! result = req.Json<{| name: string |}>()
    result.name |> should equal "fire"
}

[<Fact>]
let ``Request.Raw returns underlying HttpContext`` () =
    let ctx = makeHttpContext "GET" "/" "" [] None
    let req = Request(ctx, Dictionary<string, string>() :> IReadOnlyDictionary<_, _>)
    req.Raw |> should be (sameAs ctx)

[<Fact>]
let ``Request.Headers returns list for existing header`` () =
    let ctx = makeHttpContext "GET" "/" "" ["Accept", "text/html"] None
    let req = Request(ctx, Dictionary<string, string>() :> IReadOnlyDictionary<_, _>)
    let headers = req.Headers "Accept"
    headers |> List.isEmpty |> should be False

[<Fact>]
let ``Request.Headers returns empty list for missing header`` () =
    let ctx = makeHttpContext "GET" "/" "" [] None
    let req = Request(ctx, Dictionary<string, string>() :> IReadOnlyDictionary<_, _>)
    let headers = req.Headers "X-Missing"
    headers |> List.length |> should equal 0

[<Fact>]
let ``Request.Body returns request body stream`` () =
    let ctx = makeHttpContext "POST" "/" "" [] (Some "hello")
    let req = Request(ctx, Dictionary<string, string>() :> IReadOnlyDictionary<_, _>)
    req.Body |> should not' (be Null)
