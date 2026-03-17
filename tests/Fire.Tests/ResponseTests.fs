module Fire.Tests.ResponseTests

open Xunit
open FsUnit.Xunit
open Fire

[<Fact>]
let ``Response.ok has status 200 and empty body`` () =
    let r = Response.ok
    r.Status |> should equal 200
    r.Headers |> should equal List.empty<string * string>
    r.Body |> should equal ResponseBody.Empty

[<Fact>]
let ``Response.text sets Text body`` () =
    let r = Response.text "hello"
    r.Status |> should equal 200
    r.Body |> should equal (Text "hello")

[<Fact>]
let ``Response.html sets HTML content type`` () =
    let r = Response.html "<h1>hello</h1>"
    r.Status |> should equal 200
    r.Body |> should equal (Text "<h1>hello</h1>")
    r.Headers |> should contain ("Content-Type", "text/html; charset=utf-8")

[<Fact>]
let ``Response.json serializes to UTF-8 bytes`` () =
    let r = Response.json {| name = "fire" |}
    r.Status |> should equal 200
    match r.Body with
    | Json bytes -> System.Text.Encoding.UTF8.GetString(bytes) |> should haveSubstring "fire"
    | _ -> failwith "expected Json body"

[<Fact>]
let ``Response.stream sets Stream body`` () =
    let ms = new System.IO.MemoryStream([|1uy; 2uy; 3uy|])
    let r = Response.stream ms
    r.Status |> should equal 200
    match r.Body with
    | Stream s -> s |> should be (sameAs ms)
    | _ -> failwith "expected Stream body"

[<Fact>]
let ``Response.status overrides status code`` () =
    let r = Response.ok |> Response.status 201
    r.Status |> should equal 201

[<Fact>]
let ``Response.header prepends header pair`` () =
    let r = Response.ok |> Response.header "X-Foo" "bar" |> Response.header "X-Baz" "qux"
    r.Headers |> should contain ("X-Foo", "bar")
    r.Headers |> should contain ("X-Baz", "qux")

[<Fact>]
let ``Response.header allows duplicate keys`` () =
    let r = Response.ok |> Response.header "Set-Cookie" "a=1" |> Response.header "Set-Cookie" "b=2"
    r.Headers |> List.filter (fun (k, _) -> k = "Set-Cookie") |> List.length |> should equal 2

[<Fact>]
let ``Response.notFound has status 404`` () =
    Response.notFound.Status |> should equal 404

[<Fact>]
let ``Response.unauthorized has status 401`` () =
    Response.unauthorized.Status |> should equal 401

[<Fact>]
let ``Response.ofResult maps Ok`` () =
    let r = Ok "hello" |> Response.ofResult Response.text (fun _ -> Response.notFound)
    r.Body |> should equal (Text "hello")

[<Fact>]
let ``Response.ofResult maps Error`` () =
    let r = Error "bad" |> Response.ofResult (fun _ -> Response.ok) (fun e -> Response.text e |> Response.status 400)
    r.Status |> should equal 400
    r.Body |> should equal (Text "bad")
