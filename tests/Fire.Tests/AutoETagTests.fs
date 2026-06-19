module Fire.Tests.AutoETagTests

open Xunit
open FsUnit.Xunit
open Firefly

let private findHeader name (headers: (string * string) list) =
    headers |> List.tryFind (fun (k, _) -> k = name) |> Option.map snd

[<Fact>]
let ``AutoETag adds ETag to GET 200 responses`` () = task {
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task { return Response.text "hello world" })
    let config = App.defaults |> App.middleware AutoETag.middleware
    let client = TestClient.createWith routes config
    let! response = client |> TestClient.get "/test"
    response.Status |> should equal 200
    let etag = findHeader "ETag" response.Headers
    etag.IsSome |> should equal true
    etag.Value |> should haveSubstring "\""
}

[<Fact>]
let ``AutoETag returns 304 when If-None-Match matches`` () = task {
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task { return Response.text "hello world" })
    let config = App.defaults |> App.middleware AutoETag.middleware
    let client = TestClient.createWith routes config

    // First request to get the ETag
    let! response1 = client |> TestClient.get "/test"
    response1.Status |> should equal 200
    let etag = (findHeader "ETag" response1.Headers).Value

    // Second request with If-None-Match
    let clientWithEtag =
        TestClient.createWith routes config
        |> TestClient.withHeader "If-None-Match" etag
    let! response2 = clientWithEtag |> TestClient.get "/test"
    response2.Status |> should equal 304
}

[<Fact>]
let ``AutoETag does not add ETag to POST responses`` () = task {
    let routes =
        Route.start
        |> Route.post "/test" (fun _ -> task { return Response.text "created" })
    let config = App.defaults |> App.middleware AutoETag.middleware
    let client = TestClient.createWith routes config
    let! response = client |> TestClient.post "/test" "{}"
    findHeader "ETag" response.Headers |> should equal None
}

[<Fact>]
let ``AutoETag does not add ETag to non-200 responses`` () = task {
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task { return Response.text "not found" |> Response.status 404 })
    let config = App.defaults |> App.middleware AutoETag.middleware
    let client = TestClient.createWith routes config
    let! response = client |> TestClient.get "/test"
    response.Status |> should equal 404
    findHeader "ETag" response.Headers |> should equal None
}

[<Fact>]
let ``AutoETag does not add ETag to Stream responses`` () = task {
    let routes =
        Route.start
        |> Route.get "/test" (fun _ -> task {
            let stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes("streamed"))
            return Response.stream stream
        })
    let config = App.defaults |> App.middleware AutoETag.middleware
    let client = TestClient.createWith routes config
    let! response = client |> TestClient.get "/test"
    response.Status |> should equal 200
    findHeader "ETag" response.Headers |> should equal None
}
