module Firefly.Tests.ResponseHelpersTests

open Xunit
open FsUnit.Xunit
open Firefly

[<Fact>]
let ``redirect sets Location header and status 302`` () =
    let r = Response.ok |> Response.redirect "/login" 302
    r.Status |> should equal 302
    r.Headers |> should contain ("Location", "/login")

[<Fact>]
let ``redirect 301 for permanent`` () =
    let r = Response.ok |> Response.redirect "/new-path" 301
    r.Status |> should equal 301
    r.Headers |> should contain ("Location", "/new-path")

[<Fact>]
let ``etag sets ETag header`` () =
    let r = Response.ok |> Response.etag "\"abc123\""
    r.Headers |> should contain ("ETag", "\"abc123\"")

[<Fact>]
let ``cacheControl sets Cache-Control header`` () =
    let r = Response.ok |> Response.cacheControl "max-age=3600"
    r.Headers |> should contain ("Cache-Control", "max-age=3600")

[<Fact>]
let ``caching headers compose with other builders`` () =
    let r =
        Response.text "hello"
        |> Response.etag "\"v1\""
        |> Response.cacheControl "public, max-age=600"
        |> Response.header "X-Custom" "test"
    r.Headers |> should contain ("ETag", "\"v1\"")
    r.Headers |> should contain ("Cache-Control", "public, max-age=600")
    r.Headers |> should contain ("X-Custom", "test")
    match r.Body with
    | ResponseBody.Text s -> s |> should equal "hello"
    | _ -> failwith "expected Text body"
