module Flame.Tests.SchemaTests

open Xunit
open FsUnit.Xunit
open Flame

let todoSchema = schema {
    let! title = Schema.required "title" Schema.string [ Schema.minLength 3 ]
    let! completed = Schema.optional "completed" Schema.bool false []
    return {| Title = title; Completed = completed |}
}

[<Fact>]
let ``Flame standalone: parses valid JSON`` () =
    match Schema.parseString todoSchema """{"title":"test"}""" with
    | Ok t -> t.Title |> should equal "test"
    | Error e -> failwith $"expected Ok, got {e}"

[<Fact>]
let ``Flame standalone: validates rules`` () =
    match Schema.parseString todoSchema """{"title":"ab"}""" with
    | Error errs -> errs |> List.exists (fun e -> e.Contains("at least 3")) |> should be True
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Flame standalone: generates JSON Schema`` () =
    let js = Schema.toJsonSchema todoSchema
    js |> should haveSubstring "title"

[<Fact>]
let ``Flame standalone: fromType works`` () =
    let s = Schema.fromType<{| Name: string; Age: int |}>()
    match Schema.parseString s """{"Name":"Alice","Age":30}""" with
    | Ok r -> r.Name |> should equal "Alice"
    | Error e -> failwith $"expected Ok, got {e}"

[<Fact>]
let ``Flame standalone: transforms work`` () =
    let s = schema {
        let! email = Schema.required "email" Schema.string [ Schema.trim; Schema.lowercase ]
        return {| Email = email |}
    }
    match Schema.parseString s """{"email":"  ALICE@TEST.COM  "}""" with
    | Ok r -> r.Email |> should equal "alice@test.com"
    | Error e -> failwith $"expected Ok, got {e}"
