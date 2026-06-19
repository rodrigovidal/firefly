module Firefly.Tests.ContentNegotiationTests

open System.Collections.Generic
open Microsoft.AspNetCore.Http
open Xunit
open FsUnit.Xunit
open Firefly

let makeCtx (method': string) (path: string) (headers: (string * string) list) (contentType: string option) =
    let ctx = DefaultHttpContext()
    ctx.Request.Method <- method'
    ctx.Request.Path <- PathString(path)
    for (k, v) in headers do
        ctx.Request.Headers.[k] <- Microsoft.Extensions.Primitives.StringValues(v)
    match contentType with
    | Some ct -> ctx.Request.ContentType <- ct
    | None -> ()
    ctx :> HttpContext

let emptyParams = Dictionary<string, string>() :> IReadOnlyDictionary<_, _>

[<Fact>]
let ``Accepts true when Accept header contains type`` () =
    let ctx = makeCtx "GET" "/" [("Accept", "application/json, text/html")] None
    let req = Request(ctx, emptyParams)
    req.Accepts "application/json" |> should equal true

[<Fact>]
let ``Accepts false when doesn't contain type`` () =
    let ctx = makeCtx "GET" "/" [("Accept", "text/html")] None
    let req = Request(ctx, emptyParams)
    req.Accepts "application/json" |> should equal false

[<Fact>]
let ``Accepts false when no Accept header`` () =
    let ctx = makeCtx "GET" "/" [] None
    let req = Request(ctx, emptyParams)
    req.Accepts "application/json" |> should equal false

[<Fact>]
let ``ContentType returns Some when present`` () =
    let ctx = makeCtx "POST" "/" [] (Some "application/json")
    let req = Request(ctx, emptyParams)
    req.ContentType |> should equal (Some "application/json")

[<Fact>]
let ``ContentType returns None when not set`` () =
    let ctx = makeCtx "GET" "/" [] None
    let req = Request(ctx, emptyParams)
    req.ContentType |> should equal None
