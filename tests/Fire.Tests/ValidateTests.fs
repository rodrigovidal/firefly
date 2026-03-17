module Fire.Tests.ValidateTests

open Xunit
open FsUnit.Xunit
open Fire

type CreateUser = { Name: string; Email: string }

// --- Record-level validator tests ---

[<Fact>]
let ``Validate.required fails on empty string`` () =
    let v = Validate.required "name" (fun (u: CreateUser) -> u.Name)
    let result = v { Name = ""; Email = "a@b.com" }
    match result with
    | Error errs -> errs |> should equal ["name is required"]
    | Ok _ -> failwith "expected error"

[<Fact>]
let ``Validate.required passes on non-empty string`` () =
    let v = Validate.required "name" (fun (u: CreateUser) -> u.Name)
    let result = v { Name = "Alice"; Email = "a@b.com" }
    match result with
    | Ok u -> u |> should equal { Name = "Alice"; Email = "a@b.com" }
    | Error _ -> failwith "expected ok"

[<Fact>]
let ``Validate.minLength fails when too short`` () =
    let v = Validate.minLength "name" 3 (fun (u: CreateUser) -> u.Name)
    let result = v { Name = "Al"; Email = "a@b.com" }
    match result with
    | Error errs -> errs |> should equal ["name must be at least 3 characters"]
    | Ok _ -> failwith "expected error"

[<Fact>]
let ``Validate.maxLength fails when too long`` () =
    let v = Validate.maxLength "name" 5 (fun (u: CreateUser) -> u.Name)
    let result = v { Name = "Alexander"; Email = "a@b.com" }
    match result with
    | Error errs -> errs |> should equal ["name must be at most 5 characters"]
    | Ok _ -> failwith "expected error"

[<Fact>]
let ``Validate.pattern fails on non-match`` () =
    let v = Validate.pattern "email" @"^.+@.+\..+$" (fun (u: CreateUser) -> u.Email)
    let result = v { Name = "Alice"; Email = "not-an-email" }
    match result with
    | Error errs -> errs |> should equal ["email has invalid format"]
    | Ok _ -> failwith "expected error"

[<Fact>]
let ``Validate.combine collects all errors`` () =
    let v = Validate.combine [
        Validate.required "name" (fun (u: CreateUser) -> u.Name)
        Validate.minLength "email" 5 (fun (u: CreateUser) -> u.Email)
    ]
    let result = v { Name = ""; Email = "a@b" }
    match result with
    | Error errs -> errs |> List.length |> should equal 2
    | Ok _ -> failwith "expected error"

[<Fact>]
let ``Validate.combine passes when all valid`` () =
    let v = Validate.combine [
        Validate.required "name" (fun (u: CreateUser) -> u.Name)
        Validate.required "email" (fun (u: CreateUser) -> u.Email)
    ]
    let result = v { Name = "Alice"; Email = "a@b.com" }
    match result with
    | Ok u -> u |> should equal { Name = "Alice"; Email = "a@b.com" }
    | Error _ -> failwith "expected ok"

[<Fact>]
let ``Validate.body returns 400 with errors on invalid body`` () = task {
    let routes =
        Route.start
        |> Route.post "/users" (
            Validate.body
                (Validate.combine [
                    Validate.required "name" (fun (u: CreateUser) -> u.Name)
                ])
                (fun user -> task {
                    return Response.json {| name = user.Name |} |> Response.status 201
                })
        )
    let client = TestClient.create routes
    let! r = client |> TestClient.post "/users" """{"Name":"","Email":"a@b.com"}"""
    r.Status |> should equal 400
    r.Body |> should haveSubstring "name is required"
}

[<Fact>]
let ``Validate.body calls handler on valid body`` () = task {
    let routes =
        Route.start
        |> Route.post "/users" (
            Validate.body
                (Validate.required "name" (fun (u: CreateUser) -> u.Name))
                (fun user -> task {
                    return Response.json {| name = user.Name |} |> Response.status 201
                })
        )
    let client = TestClient.create routes
    let! r = client |> TestClient.post "/users" """{"Name":"Alice","Email":"a@b.com"}"""
    r.Status |> should equal 201
    r.Body |> should haveSubstring "Alice"
}

// --- Query/param/header validation tests ---

[<Fact>]
let ``Validate.query returns 400 when required query param missing`` () = task {
    let routes =
        Route.start
        |> Route.get "/search" (
            Validate.query ["q", Validate.isRequired] (fun req -> task {
                return Response.text (req.QueryParam "q" |> Option.defaultValue "")
            })
        )
    let client = TestClient.create routes
    let! r = client |> TestClient.get "/search"
    r.Status |> should equal 400
    r.Body |> should haveSubstring "q is required"
}

[<Fact>]
let ``Validate.query passes with valid params`` () = task {
    let routes =
        Route.start
        |> Route.get "/search" (
            Validate.query ["q", Validate.isRequired] (fun req -> task {
                return Response.text (req.QueryParam "q" |> Option.defaultValue "")
            })
        )
    let client = TestClient.create routes
    let! r = client |> TestClient.get "/search?q=fire"
    r.Status |> should equal 200
    r.Body |> should equal "fire"
}

[<Fact>]
let ``Validate.param validates route params`` () = task {
    let routes =
        Route.start
        |> Route.get "/users/:id" (
            Validate.param ["id", Validate.isInt] (fun req -> task {
                return Response.text req.Params.["id"]
            })
        )
    let client = TestClient.create routes
    let! r = client |> TestClient.get "/users/abc"
    r.Status |> should equal 400
    r.Body |> should haveSubstring "id must be an integer"
}

[<Fact>]
let ``Validate.headerValues validates headers`` () = task {
    let routes =
        Route.start
        |> Route.get "/api" (
            Validate.headerValues ["X-API-Key", Validate.isRequired] (fun req -> task {
                return Response.ok
            })
        )
    let client = TestClient.create routes
    let! r = client |> TestClient.get "/api"
    r.Status |> should equal 400
    r.Body |> should haveSubstring "X-API-Key is required"
}
