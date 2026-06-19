module Fire.Tests.RequestExtensionsTests

open System.Collections.Generic
open System.IO
open System.Text
open Microsoft.AspNetCore.Http
open Xunit
open FsUnit.Xunit
open Firefly

let makeHttpContext (method': string) (path: string) (query: string) (headers: (string * string) list) (body: string option) (contentType: string option) =
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
    match contentType with
    | Some ct -> ctx.Request.ContentType <- ct
    | None -> ()
    ctx :> HttpContext

let emptyParams = Dictionary<string, string>() :> IReadOnlyDictionary<_, _>

[<Fact>]
let ``QueryParam returns Some for existing key`` () =
    let ctx = makeHttpContext "GET" "/search" "?q=fire" [] None None
    let req = Request(ctx, emptyParams)
    req.QueryParam "q" |> should equal (Some "fire")

[<Fact>]
let ``QueryParam returns None for missing key`` () =
    let ctx = makeHttpContext "GET" "/search" "" [] None None
    let req = Request(ctx, emptyParams)
    req.QueryParam "q" |> should equal None

[<Fact>]
let ``Text reads body as string`` () = task {
    let ctx = makeHttpContext "POST" "/" "" [] (Some "hello world") None
    let req = Request(ctx, emptyParams)
    let! text = req.Text()
    text |> should equal "hello world"
}

[<Fact>]
let ``Text returns empty string for empty body`` () = task {
    let ctx = makeHttpContext "POST" "/" "" [] None None
    let req = Request(ctx, emptyParams)
    let! text = req.Text()
    text |> should equal ""
}

[<Fact>]
let ``Form parses url-encoded body`` () = task {
    let body = "name=fire&version=1"
    let ctx = makeHttpContext "POST" "/" "" [] (Some body) (Some "application/x-www-form-urlencoded")
    let req = Request(ctx, emptyParams)
    let! form = req.Form()
    form.["name"] |> should equal "fire"
    form.["version"] |> should equal "1"
}
