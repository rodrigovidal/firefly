module Flame.Tests.SchemaTests

open System
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

[<Fact>]
let ``Nested errors have dotted paths`` () =
    let address = schema {
        let! street = Schema.required "street" Schema.string [ Schema.minLength 1 ]
        let! zip = Schema.required "zip" Schema.string [ Schema.pattern @"^\d{5}$" ]
        return {| Street = street; Zip = zip |}
    }
    let user = schema {
        let! name = Schema.required "name" Schema.string []
        let! address = Schema.required "address" (Schema.nest address) []
        return {| Name = name; Address = address |}
    }
    use doc = System.Text.Json.JsonDocument.Parse("""{"name":"Alice","address":{"street":"","zip":"bad"}}""")
    match Schema.parseJson user doc.RootElement with
    | Error errs ->
        errs |> List.exists (fun e -> e.Contains("address.")) |> should be True
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema.req works without rules`` () =
    let s = schema {
        let! name = Schema.req "name" Schema.string
        return {| Name = name |}
    }
    match Schema.parseString s """{"name":"Alice"}""" with
    | Ok r -> r.Name |> should equal "Alice"
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema.opt works without rules`` () =
    let s = schema {
        let! name = Schema.req "name" Schema.string
        let! age = Schema.opt "age" Schema.int 0
        return {| Name = name; Age = age |}
    }
    match Schema.parseString s """{"name":"Alice"}""" with
    | Ok r -> r.Age |> should equal 0
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema.dateTime parses ISO date`` () =
    let s = schema {
        let! date = Schema.req "date" Schema.dateTime
        return {| Date = date |}
    }
    match Schema.parseString s """{"date":"2026-03-17T12:00:00"}""" with
    | Ok r -> r.Date.Year |> should equal 2026
    | Error e -> failwith $"expected Ok, got {e}"

[<Fact>]
let ``Schema.dateTimeOffset parses ISO date with offset`` () =
    let s = schema {
        let! date = Schema.req "date" Schema.dateTimeOffset
        return {| Date = date |}
    }
    match Schema.parseString s """{"date":"2026-03-17T12:00:00+03:00"}""" with
    | Ok r ->
        r.Date.Year |> should equal 2026
        r.Date.Offset |> should equal (TimeSpan.FromHours(3.0))
    | Error e -> failwith $"expected Ok, got {e}"

[<Fact>]
let ``Schema.list with nested objects via JsonElement`` () =
    let item = schema {
        let! name = Schema.req "name" Schema.string
        let! qty = Schema.req "qty" Schema.int
        return {| Name = name; Qty = qty |}
    }
    let order = schema {
        let! items = Schema.required "items" (Schema.list (Schema.nest item)) []
        return {| Items = items |}
    }
    use doc = System.Text.Json.JsonDocument.Parse("""{"items":[{"name":"A","qty":1},{"name":"B","qty":2}]}""")
    match Schema.parseJson order doc.RootElement with
    | Ok r ->
        r.Items |> List.length |> should equal 2
        r.Items.[0].Name |> should equal "A"
        r.Items.[1].Qty |> should equal 2
    | Error e -> failwith $"expected Ok, got {e}"

[<Fact>]
let ``Error messages use correct grammar for singular`` () =
    let s = schema {
        let! x = Schema.required "x" Schema.string [ Schema.minLength 1 ]
        return {| X = x |}
    }
    match Schema.parseString s """{"x":""}""" with
    | Error errs -> errs.[0] |> should haveSubstring "1 character"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Error messages use correct grammar for plural`` () =
    let s = schema {
        let! x = Schema.required "x" Schema.string [ Schema.minLength 3 ]
        return {| X = x |}
    }
    match Schema.parseString s """{"x":"ab"}""" with
    | Error errs -> errs.[0] |> should haveSubstring "3 characters"
    | Ok _ -> failwith "expected Error"

// --- Cross-field validation ---

[<Fact>]
let ``Schema.check validates cross-field constraints`` () =
    let s = schema {
        let! password = Schema.req "password" Schema.string
        let! confirm = Schema.req "confirm" Schema.string
        do! Schema.check (fun () ->
            if password = confirm then Ok ()
            else Error "confirm: must match password"
        )
        return {| Password = password |}
    }
    match Schema.parseString s """{"password":"abc","confirm":"abc"}""" with
    | Ok r -> r.Password |> should equal "abc"
    | Error e -> failwith $"expected Ok, got {e}"

[<Fact>]
let ``Schema.check rejects when cross-field constraint fails`` () =
    let s = schema {
        let! password = Schema.req "password" Schema.string
        let! confirm = Schema.req "confirm" Schema.string
        do! Schema.check (fun () ->
            if password = confirm then Ok ()
            else Error "confirm: must match password"
        )
        return {| Password = password |}
    }
    match Schema.parseString s """{"password":"abc","confirm":"xyz"}""" with
    | Error errs -> errs |> List.exists (fun e -> e.Contains("must match")) |> should be True
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema.check works with date comparison`` () =
    let s = schema {
        let! startDate = Schema.req "start" Schema.dateTime
        let! endDate = Schema.req "end" Schema.dateTime
        do! Schema.check (fun () ->
            if endDate > startDate then Ok ()
            else Error "end: must be after start"
        )
        return {| Start = startDate; End = endDate |}
    }
    match Schema.parseString s """{"start":"2026-01-01","end":"2026-12-31"}""" with
    | Ok r -> r.Start.Year |> should equal 2026
    | Error e -> failwith $"expected Ok, got {e}"

[<Fact>]
let ``Schema.check rejects invalid date order`` () =
    let s = schema {
        let! startDate = Schema.req "start" Schema.dateTime
        let! endDate = Schema.req "end" Schema.dateTime
        do! Schema.check (fun () ->
            if endDate > startDate then Ok ()
            else Error "end: must be after start"
        )
        return {| Start = startDate; End = endDate |}
    }
    match Schema.parseString s """{"start":"2026-12-31","end":"2026-01-01"}""" with
    | Error errs -> errs |> List.exists (fun e -> e.Contains("must be after")) |> should be True
    | Ok _ -> failwith "expected Error"
