module Fire.Tests.ValidateTests

open Xunit
open FsUnit.Xunit
open Firefly

type CreateUser = { Name: string; Email: string }

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

// --- Coverage: Validate.pattern passes on match (line 23) ---

[<Fact>]
let ``Validate.pattern passes on match`` () =
    let v = Validate.pattern "email" @"^.+@.+\..+$" (fun (u: CreateUser) -> u.Email)
    let result = v { Name = "Alice"; Email = "alice@test.com" }
    match result with
    | Ok u -> u.Email |> should equal "alice@test.com"
    | Error _ -> failwith "expected ok"

// --- Coverage: Validate.minLength passes ---

[<Fact>]
let ``Validate.minLength passes when long enough`` () =
    let v = Validate.minLength "name" 3 (fun (u: CreateUser) -> u.Name)
    let result = v { Name = "Alice"; Email = "a@b.com" }
    match result with
    | Ok u -> u.Name |> should equal "Alice"
    | Error _ -> failwith "expected ok"

// --- Coverage: Validate.maxLength passes ---

[<Fact>]
let ``Validate.maxLength passes when short enough`` () =
    let v = Validate.maxLength "name" 10 (fun (u: CreateUser) -> u.Name)
    let result = v { Name = "Alice"; Email = "a@b.com" }
    match result with
    | Ok u -> u.Name |> should equal "Alice"
    | Error _ -> failwith "expected ok"

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
