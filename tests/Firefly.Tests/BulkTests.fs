module Firefly.Tests.BulkTests

open System.Threading.Tasks
open Xunit
open FsUnit.Xunit
open Firefly

let private bodyString (response: Response) =
    match (Internal.materializeJson response).Body with
    | ResponseBody.Json bytes -> System.Text.Encoding.UTF8.GetString(bytes)
    | ResponseBody.Text s -> s
    | _ -> failwith "expected JSON body"

let private okOp (x: int) : Task<Result<int, string>> = task { return Ok(x * 10) }
let private failOp (x: int) : Task<Result<int, string>> = task { return Error $"bad {x}" }
let private evenOp (x: int) : Task<Result<int, string>> = task {
    return (if x % 2 = 0 then Ok(x * 10) else Error $"odd {x}")
}

[<Fact>]
let ``execute all success returns 200 with counts`` () = task {
    let! response = Bulk.execute okOp [ 1; 2; 3 ]
    response.Status |> should equal 200
    let body = bodyString response
    body |> should haveSubstring "\"succeeded\":3"
    body |> should haveSubstring "\"failed\":0"
    body |> should haveSubstring "\"total\":3"
    body |> should haveSubstring "\"status\":\"success\""
}

[<Fact>]
let ``execute mixed results returns 207 multi-status`` () = task {
    let! response = Bulk.execute evenOp [ 2; 3; 4 ] // ok, error, ok
    response.Status |> should equal 207
    let body = bodyString response
    body |> should haveSubstring "\"succeeded\":2"
    body |> should haveSubstring "\"failed\":1"
    body |> should haveSubstring "\"status\":\"error\""
    body |> should haveSubstring "odd 3"
}

[<Fact>]
let ``execute all errors returns 422`` () = task {
    let! response = Bulk.execute failOp [ 1; 2 ]
    response.Status |> should equal 422
    let body = bodyString response
    body |> should haveSubstring "\"succeeded\":0"
    body |> should haveSubstring "\"failed\":2"
}

[<Fact>]
let ``execute empty list returns 200 with zero counts`` () = task {
    let! response = Bulk.execute okOp []
    response.Status |> should equal 200
    let body = bodyString response
    body |> should haveSubstring "\"succeeded\":0"
    body |> should haveSubstring "\"failed\":0"
    body |> should haveSubstring "\"total\":0"
}

[<Fact>]
let ``execute preserves input order in results`` () = task {
    let! response = Bulk.execute okOp [ 5; 6; 7 ]
    let body = bodyString response
    let i0 = body.IndexOf("\"index\":0")
    let i1 = body.IndexOf("\"index\":1")
    let i2 = body.IndexOf("\"index\":2")
    i0 |> should be (greaterThan -1)
    i0 |> should be (lessThan i1)
    i1 |> should be (lessThan i2)
}

// --- handler via TestClient ---

let private routes =
    Route.start
    |> Route.post "/bulk" (Bulk.handler okOp)

[<Fact>]
let ``handler processes a JSON array body`` () = task {
    let client = TestClient.create routes
    let! r = client |> TestClient.post "/bulk" "[1, 2, 3]"
    r.Status |> should equal 200
    r.Body |> should haveSubstring "\"succeeded\":3"
}

[<Fact>]
let ``handler returns 400 on malformed body`` () = task {
    let client = TestClient.create routes
    let! r = client |> TestClient.post "/bulk" "not json"
    r.Status |> should equal 400
    r.Body |> should haveSubstring "error"
}
