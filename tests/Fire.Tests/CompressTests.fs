module Fire.Tests.CompressTests

open System.IO
open System.IO.Compression
open Xunit
open FsUnit.Xunit
open Firefly

[<Fact>]
let ``Compress.gzip compresses response when Accept-Encoding gzip`` () = task {
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task { return Response.text "hello world" })
    let config = App.defaults |> App.middleware Compress.gzip
    let client = TestClient.createWith routes config
                 |> TestClient.withHeader "Accept-Encoding" "gzip"
    let! response = client |> TestClient.get "/test"
    response.Status |> should equal 200
    // The response body should be different from the original (compressed)
    response.Headers |> List.exists (fun (k, v) -> k = "Content-Encoding" && v = "gzip") |> should equal true
}

[<Fact>]
let ``Compress.gzip passes through when no gzip accepted`` () = task {
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task { return Response.text "hello world" })
    let config = App.defaults |> App.middleware Compress.gzip
    let client = TestClient.createWith routes config
    let! response = client |> TestClient.get "/test"
    response.Status |> should equal 200
    response.Body |> should equal "hello world"
    response.Headers |> List.exists (fun (k, _) -> k = "Content-Encoding") |> should equal false
}

[<Fact>]
let ``Compress.auto selects brotli over gzip`` () = task {
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task { return Response.text "hello world" })
    let config = App.defaults |> App.middleware Compress.auto
    let client = TestClient.createWith routes config
                 |> TestClient.withHeader "Accept-Encoding" "gzip, br"
    let! response = client |> TestClient.get "/test"
    response.Status |> should equal 200
    response.Headers |> List.exists (fun (k, v) -> k = "Content-Encoding" && v = "br") |> should equal true
}

[<Fact>]
let ``Compress.gzip compresses JSON response`` () = task {
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task { return Response.json {| message = "hello" |} })
    let config = App.defaults |> App.middleware Compress.gzip
    let client = TestClient.createWith routes config
                 |> TestClient.withHeader "Accept-Encoding" "gzip"
    let! response = client |> TestClient.get "/test"
    response.Status |> should equal 200
    response.Headers |> List.exists (fun (k, v) -> k = "Content-Encoding" && v = "gzip") |> should equal true
}

[<Fact>]
let ``Compress.brotli compresses text response`` () = task {
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task { return Response.text "hello world" })
    let config = App.defaults |> App.middleware Compress.brotli
    let client = TestClient.createWith routes config
                 |> TestClient.withHeader "Accept-Encoding" "br"
    let! response = client |> TestClient.get "/test"
    response.Status |> should equal 200
    response.Headers |> List.exists (fun (k, v) -> k = "Content-Encoding" && v = "br") |> should equal true
}

[<Fact>]
let ``Compress.brotli compresses JSON response`` () = task {
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task { return Response.json {| data = "test" |} })
    let config = App.defaults |> App.middleware Compress.brotli
    let client = TestClient.createWith routes config
                 |> TestClient.withHeader "Accept-Encoding" "br"
    let! response = client |> TestClient.get "/test"
    response.Status |> should equal 200
    response.Headers |> List.exists (fun (k, v) -> k = "Content-Encoding" && v = "br") |> should equal true
}

[<Fact>]
let ``Compress.auto falls back to gzip when no brotli`` () = task {
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task { return Response.text "hello" })
    let config = App.defaults |> App.middleware Compress.auto
    let client = TestClient.createWith routes config
                 |> TestClient.withHeader "Accept-Encoding" "gzip"
    let! response = client |> TestClient.get "/test"
    response.Status |> should equal 200
    response.Headers |> List.exists (fun (k, v) -> k = "Content-Encoding" && v = "gzip") |> should equal true
}

[<Fact>]
let ``Compress.auto passes through when no encoding accepted`` () = task {
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task { return Response.text "hello" })
    let config = App.defaults |> App.middleware Compress.auto
    let client = TestClient.createWith routes config
    let! response = client |> TestClient.get "/test"
    response.Status |> should equal 200
    response.Body |> should equal "hello"
}

[<Fact>]
let ``Compress.gzip preserves explicit text content type`` () = task {
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task {
            return Response.text "<html></html>" |> Response.header "Content-Type" "text/html; charset=utf-8"
        })
    let config = App.defaults |> App.middleware Compress.gzip
    let client = TestClient.createWith routes config
                 |> TestClient.withHeader "Accept-Encoding" "gzip"
    let! response = client |> TestClient.get "/test"
    response.Headers |> List.exists (fun (k, v) -> k = "Content-Type" && v = "text/html; charset=utf-8") |> should equal true
}

[<Fact>]
let ``Compress.gzip passes through stream responses`` () = task {
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task {
            let bytes = System.Text.Encoding.UTF8.GetBytes("stream body")
            return Response.stream (new MemoryStream(bytes))
        })
    let config = App.defaults |> App.middleware Compress.gzip
    let client = TestClient.createWith routes config
                 |> TestClient.withHeader "Accept-Encoding" "gzip"
    let! response = client |> TestClient.get "/test"
    response.Body |> should equal "stream body"
    response.Headers |> List.exists (fun (k, _) -> k = "Content-Encoding") |> should equal false
}

[<Fact>]
let ``Compress.brotli passes through stream responses`` () = task {
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task {
            let bytes = System.Text.Encoding.UTF8.GetBytes("stream body")
            return Response.stream (new MemoryStream(bytes))
        })
    let config = App.defaults |> App.middleware Compress.brotli
    let client = TestClient.createWith routes config
                 |> TestClient.withHeader "Accept-Encoding" "br"
    let! response = client |> TestClient.get "/test"
    response.Body |> should equal "stream body"
    response.Headers |> List.exists (fun (k, _) -> k = "Content-Encoding") |> should equal false
}

[<Fact>]
let ``Compress.brotli passes through when no brotli accepted`` () = task {
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task { return Response.text "hello world" })
    let config = App.defaults |> App.middleware Compress.brotli
    let client = TestClient.createWith routes config
                 |> TestClient.withHeader "Accept-Encoding" "gzip"
    let! response = client |> TestClient.get "/test"
    response.Body |> should equal "hello world"
    response.Headers |> List.exists (fun (k, _) -> k = "Content-Encoding") |> should equal false
}

[<Fact>]
let ``Compress.auto ignores disabled encodings`` () = task {
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task { return Response.text "hello world" })
    let config = App.defaults |> App.middleware Compress.auto
    let client = TestClient.createWith routes config
                 |> TestClient.withHeader "Accept-Encoding" "br;q=0, gzip;q=0"
    let! response = client |> TestClient.get "/test"
    response.Body |> should equal "hello world"
    response.Headers |> List.exists (fun (k, _) -> k = "Content-Encoding") |> should equal false
}

[<Fact>]
let ``Compress ignores invalid quality values`` () = task {
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task { return Response.text "hello world" })
    let config = App.defaults |> App.middleware Compress.gzip
    let client = TestClient.createWith routes config
                 |> TestClient.withHeader "Accept-Encoding" "gzip;q=abc"
    let! response = client |> TestClient.get "/test"
    response.Body |> should equal "hello world"
    response.Headers |> List.exists (fun (k, _) -> k = "Content-Encoding") |> should equal false
}
