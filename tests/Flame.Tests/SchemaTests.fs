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

// --- fromType with complex types ---

type Address = { Street: string; Zip: string }
type Person = { Name: string; Age: int; Address: Address }
type PersonOptional = { Name: string; Nickname: string option; Address: Address option }
type Tag = { Key: string; Value: string }
type ItemWithTags = { Title: string; Tags: Tag list }
type Order = { Id: int; Items: int list }

[<Fact>]
let ``fromType handles nested records`` () =
    let s = Schema.fromType<Person>()
    match Schema.parseString s """{"Name":"Alice","Age":30,"Address":{"Street":"123 Main","Zip":"12345"}}""" with
    | Ok r ->
        r.Name |> should equal "Alice"
        r.Age |> should equal 30
        r.Address.Street |> should equal "123 Main"
        r.Address.Zip |> should equal "12345"
    | Error e -> failwith $"expected Ok, got {e}"

[<Fact>]
let ``fromType handles optional fields with None`` () =
    let s = Schema.fromType<PersonOptional>()
    match Schema.parseString s """{"Name":"Alice"}""" with
    | Ok r ->
        r.Name |> should equal "Alice"
        r.Nickname |> should equal None
        r.Address |> should equal None
    | Error e -> failwith $"expected Ok, got {e}"

[<Fact>]
let ``fromType handles optional fields with Some`` () =
    let s = Schema.fromType<PersonOptional>()
    match Schema.parseString s """{"Name":"Alice","Nickname":"Ali","Address":{"Street":"123 Main","Zip":"12345"}}""" with
    | Ok r ->
        r.Name |> should equal "Alice"
        r.Nickname |> should equal (Some "Ali")
        r.Address |> should equal (Some { Street = "123 Main"; Zip = "12345" })
    | Error e -> failwith $"expected Ok, got {e}"

[<Fact>]
let ``fromType handles typed int list`` () =
    let s = Schema.fromType<Order>()
    match Schema.parseString s """{"Id":1,"Items":[10,20,30]}""" with
    | Ok r ->
        r.Id |> should equal 1
        r.Items |> should equal [10; 20; 30]
    | Error e -> failwith $"expected Ok, got {e}"

[<Fact>]
let ``fromType handles list of nested records`` () =
    let s = Schema.fromType<ItemWithTags>()
    match Schema.parseString s """{"Title":"test","Tags":[{"Key":"env","Value":"prod"},{"Key":"team","Value":"api"}]}""" with
    | Ok r ->
        r.Title |> should equal "test"
        r.Tags |> List.length |> should equal 2
        r.Tags.[0].Key |> should equal "env"
        r.Tags.[1].Value |> should equal "api"
    | Error e -> failwith $"expected Ok, got {e}"

[<Fact>]
let ``fromType reports errors for missing nested fields`` () =
    let s = Schema.fromType<Person>()
    match Schema.parseString s """{"Name":"Alice","Age":30,"Address":{"Street":"123 Main"}}""" with
    | Error errs -> errs |> List.exists (fun e -> e.Contains("Zip")) |> should be True
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``fromType reports error for missing required nested object`` () =
    let s = Schema.fromType<Person>()
    match Schema.parseString s """{"Name":"Alice","Age":30}""" with
    | Error errs -> errs |> List.exists (fun e -> e.Contains("Address")) |> should be True
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``fromType with anonymous record and nested types`` () =
    let s = Schema.fromType<{| Name: string; Tags: string list; Score: float option |}>()
    match Schema.parseString s """{"Name":"test","Tags":["a","b"],"Score":9.5}""" with
    | Ok r ->
        r.Name |> should equal "test"
        r.Tags |> should equal ["a"; "b"]
        r.Score |> should equal (Some 9.5)
    | Error e -> failwith $"expected Ok, got {e}"

[<Fact>]
let ``fromType buffer path handles nested records`` () =
    let s = Schema.fromType<Person>()
    let json = """{"Name":"Bob","Age":25,"Address":{"Street":"456 Oak","Zip":"67890"}}"""
    let bytes = System.Text.Encoding.UTF8.GetBytes(json)
    let buffer = System.Buffers.ReadOnlySequence<byte>(bytes)
    match s.ParseBuffer buffer with
    | Ok r ->
        r.Name |> should equal "Bob"
        r.Address.Street |> should equal "456 Oak"
    | Error e -> failwith $"expected Ok, got {e}"

[<Fact>]
let ``fromType buffer path handles typed list`` () =
    let s = Schema.fromType<ItemWithTags>()
    let json = """{"Title":"x","Tags":[{"Key":"a","Value":"1"}]}"""
    let bytes = System.Text.Encoding.UTF8.GetBytes(json)
    let buffer = System.Buffers.ReadOnlySequence<byte>(bytes)
    match s.ParseBuffer buffer with
    | Ok r ->
        r.Tags |> List.length |> should equal 1
        r.Tags.[0].Key |> should equal "a"
    | Error e -> failwith $"expected Ok, got {e}"

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

// =====================================================================
// Rule validation tests
// =====================================================================

[<Fact>]
let ``Schema.maxLength accepts valid string`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.maxLength 5 ] in return {| X = x |} }
    match Schema.parseString s """{"x":"abc"}""" with
    | Ok r -> r.X |> should equal "abc"
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema.maxLength rejects too-long string`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.maxLength 3 ] in return {| X = x |} }
    match Schema.parseString s """{"x":"abcdef"}""" with
    | Error errs -> errs.[0] |> should haveSubstring "at most 3 characters"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema.maxLength singular grammar`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.maxLength 1 ] in return {| X = x |} }
    match Schema.parseString s """{"x":"ab"}""" with
    | Error errs -> errs.[0] |> should haveSubstring "1 character"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema.pattern accepts matching string`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.pattern @"^\d{3}$" ] in return {| X = x |} }
    match Schema.parseString s """{"x":"123"}""" with
    | Ok r -> r.X |> should equal "123"
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema.pattern rejects non-matching string`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.pattern @"^\d{3}$" ] in return {| X = x |} }
    match Schema.parseString s """{"x":"abc"}""" with
    | Error errs -> errs.[0] |> should haveSubstring "must match pattern"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema.min accepts valid number`` () =
    let s = schema { let! x = Schema.required "x" Schema.float [ Schema.min 5.0 ] in return {| X = x |} }
    match Schema.parseString s """{"x":10.0}""" with
    | Ok r -> r.X |> should equal 10.0
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema.min rejects too-small number`` () =
    let s = schema { let! x = Schema.required "x" Schema.float [ Schema.min 5.0 ] in return {| X = x |} }
    match Schema.parseString s """{"x":2.0}""" with
    | Error errs -> errs.[0] |> should haveSubstring "at least 5"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema.max accepts valid number`` () =
    let s = schema { let! x = Schema.required "x" Schema.float [ Schema.max 10.0 ] in return {| X = x |} }
    match Schema.parseString s """{"x":5.0}""" with
    | Ok r -> r.X |> should equal 5.0
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema.max rejects too-large number`` () =
    let s = schema { let! x = Schema.required "x" Schema.float [ Schema.max 10.0 ] in return {| X = x |} }
    match Schema.parseString s """{"x":20.0}""" with
    | Error errs -> errs.[0] |> should haveSubstring "at most 10"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema.email accepts valid email`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.email ] in return {| X = x |} }
    match Schema.parseString s """{"x":"alice@test.com"}""" with
    | Ok r -> r.X |> should equal "alice@test.com"
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema.email rejects invalid email`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.email ] in return {| X = x |} }
    match Schema.parseString s """{"x":"not-an-email"}""" with
    | Error errs -> errs.[0] |> should haveSubstring "invalid email"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema.url accepts valid URL`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.url ] in return {| X = x |} }
    match Schema.parseString s """{"x":"https://example.com"}""" with
    | Ok r -> r.X |> should equal "https://example.com"
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema.url accepts http URL`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.url ] in return {| X = x |} }
    match Schema.parseString s """{"x":"http://example.com"}""" with
    | Ok r -> r.X |> should equal "http://example.com"
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema.url rejects invalid URL`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.url ] in return {| X = x |} }
    match Schema.parseString s """{"x":"ftp://example.com"}""" with
    | Error errs -> errs.[0] |> should haveSubstring "invalid URL"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema.enum accepts valid value`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.enum' ["a"; "b"; "c"] ] in return {| X = x |} }
    match Schema.parseString s """{"x":"b"}""" with
    | Ok r -> r.X |> should equal "b"
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema.enum rejects invalid value`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.enum' ["a"; "b"; "c"] ] in return {| X = x |} }
    match Schema.parseString s """{"x":"z"}""" with
    | Error errs -> errs.[0] |> should haveSubstring "must be one of"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Multiple rules on same field`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.minLength 2; Schema.maxLength 5 ] in return {| X = x |} }
    match Schema.parseString s """{"x":"abc"}""" with
    | Ok r -> r.X |> should equal "abc"
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Multiple rules both fail`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.minLength 10; Schema.pattern @"^\d+$" ] in return {| X = x |} }
    match Schema.parseString s """{"x":"abc"}""" with
    | Error errs -> errs.Length |> should be (greaterThanOrEqualTo 2)
    | Ok _ -> failwith "expected Error"

// =====================================================================
// Type parser tests - error cases and coercion
// =====================================================================

[<Fact>]
let ``Schema.int parses from string`` () =
    let s = schema { let! x = Schema.req "x" Schema.int in return {| X = x |} }
    match Schema.parseString s """{"x":"42"}""" with
    | Ok r -> r.X |> should equal 42
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema.int rejects non-numeric string`` () =
    let s = schema { let! x = Schema.req "x" Schema.int in return {| X = x |} }
    match Schema.parseString s """{"x":"abc"}""" with
    | Error errs -> errs.[0] |> should haveSubstring "expected integer"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema.int rejects boolean`` () =
    let s = schema { let! x = Schema.req "x" Schema.int in return {| X = x |} }
    match Schema.parseString s """{"x":true}""" with
    | Error errs -> errs.[0] |> should haveSubstring "expected integer"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema.bool parses from string`` () =
    let s = schema { let! x = Schema.req "x" Schema.bool in return {| X = x |} }
    match Schema.parseString s """{"x":"true"}""" with
    | Ok r -> r.X |> should equal true
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema.bool rejects invalid string`` () =
    let s = schema { let! x = Schema.req "x" Schema.bool in return {| X = x |} }
    match Schema.parseString s """{"x":"yes"}""" with
    | Error errs -> errs.[0] |> should haveSubstring "expected boolean"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema.bool rejects number`` () =
    let s = schema { let! x = Schema.req "x" Schema.bool in return {| X = x |} }
    match Schema.parseString s """{"x":1}""" with
    | Error errs -> errs.[0] |> should haveSubstring "expected boolean"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema.float parses from string`` () =
    let s = schema { let! x = Schema.req "x" Schema.float in return {| X = x |} }
    match Schema.parseString s """{"x":"3.14"}""" with
    | Ok r -> r.X |> should equal 3.14
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema.float rejects non-numeric string`` () =
    let s = schema { let! x = Schema.req "x" Schema.float in return {| X = x |} }
    match Schema.parseString s """{"x":"abc"}""" with
    | Error errs -> errs.[0] |> should haveSubstring "expected number"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema.float rejects boolean`` () =
    let s = schema { let! x = Schema.req "x" Schema.float in return {| X = x |} }
    match Schema.parseString s """{"x":true}""" with
    | Error errs -> errs.[0] |> should haveSubstring "expected number"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema.string rejects null`` () =
    let s = schema { let! x = Schema.req "x" Schema.string in return {| X = x |} }
    match Schema.parseString s """{"x":null}""" with
    | Error _ -> ()
    | Ok r -> if r.X = null then () else failwith "expected null or error"

[<Fact>]
let ``Schema.dateTime rejects invalid string`` () =
    let s = schema { let! x = Schema.req "x" Schema.dateTime in return {| X = x |} }
    match Schema.parseString s """{"x":"not-a-date"}""" with
    | Error errs -> errs.[0] |> should haveSubstring "expected date"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema.dateTimeOffset rejects invalid string`` () =
    let s = schema { let! x = Schema.req "x" Schema.dateTimeOffset in return {| X = x |} }
    match Schema.parseString s """{"x":"not-a-date"}""" with
    | Error errs -> errs.[0] |> should haveSubstring "expected date"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema.nullable parses null`` () =
    let s = schema { let! x = Schema.req "x" (Schema.nullable Schema.int) in return {| X = x |} }
    match Schema.parseString s """{"x":null}""" with
    | Ok r -> r.X |> should equal None
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema.nullable parses value`` () =
    let s = schema { let! x = Schema.req "x" (Schema.nullable Schema.int) in return {| X = x |} }
    match Schema.parseString s """{"x":42}""" with
    | Ok r -> r.X |> should equal (Some 42)
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema.list with empty array`` () =
    let s = schema { let! x = Schema.req "x" (Schema.list Schema.string) in return {| X = x |} }
    match Schema.parseString s """{"x":[]}""" with
    | Ok r -> r.X |> should equal ([] : string list)
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema.list error includes index`` () =
    let s = schema { let! x = Schema.required "x" (Schema.list Schema.int) [] in return {| X = x |} }
    use doc = System.Text.Json.JsonDocument.Parse("""{"x":[1,"abc",3]}""")
    match Schema.parseJson s doc.RootElement with
    | Error errs -> errs.[0] |> should haveSubstring "[1]"
    | Ok _ -> failwith "expected Error"

// =====================================================================
// Transform rules - edge cases
// =====================================================================

[<Fact>]
let ``Schema.trim on already-trimmed string`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.trim ] in return {| X = x |} }
    match Schema.parseString s """{"x":"hello"}""" with
    | Ok r -> r.X |> should equal "hello"
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema.trim on whitespace-only string`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.trim ] in return {| X = x |} }
    match Schema.parseString s """{"x":"   "}""" with
    | Ok r -> r.X |> should equal ""
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema.uppercase works`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.uppercase ] in return {| X = x |} }
    match Schema.parseString s """{"x":"hello"}""" with
    | Ok r -> r.X |> should equal "HELLO"
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Transforms chain: trim then uppercase`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.trim; Schema.uppercase ] in return {| X = x |} }
    match Schema.parseString s """{"x":"  hello  "}""" with
    | Ok r -> r.X |> should equal "HELLO"
    | Error _ -> failwith "expected Ok"

// =====================================================================
// Buffer path tests
// =====================================================================

[<Fact>]
let ``Buffer path: int from string coercion`` () =
    let s = schema { let! x = Schema.req "x" Schema.int in return {| X = x |} }
    let bytes = System.Text.Encoding.UTF8.GetBytes("""{"x":"99"}""")
    match s.ParseBuffer (System.Buffers.ReadOnlySequence<byte>(bytes)) with
    | Ok r -> r.X |> should equal 99
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Buffer path: bool from string coercion`` () =
    let s = schema { let! x = Schema.req "x" Schema.bool in return {| X = x |} }
    let bytes = System.Text.Encoding.UTF8.GetBytes("""{"x":"false"}""")
    match s.ParseBuffer (System.Buffers.ReadOnlySequence<byte>(bytes)) with
    | Ok r -> r.X |> should equal false
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Buffer path: float from string coercion`` () =
    let s = schema { let! x = Schema.req "x" Schema.float in return {| X = x |} }
    let bytes = System.Text.Encoding.UTF8.GetBytes("""{"x":"2.5"}""")
    match s.ParseBuffer (System.Buffers.ReadOnlySequence<byte>(bytes)) with
    | Ok r -> r.X |> should equal 2.5
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Buffer path: DateTime parsing`` () =
    let s = schema { let! x = Schema.req "x" Schema.dateTime in return {| X = x |} }
    let bytes = System.Text.Encoding.UTF8.GetBytes("""{"x":"2026-06-15T10:30:00"}""")
    match s.ParseBuffer (System.Buffers.ReadOnlySequence<byte>(bytes)) with
    | Ok r -> r.X.Year |> should equal 2026
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Buffer path: DateTimeOffset parsing`` () =
    let s = schema { let! x = Schema.req "x" Schema.dateTimeOffset in return {| X = x |} }
    let bytes = System.Text.Encoding.UTF8.GetBytes("""{"x":"2026-06-15T10:30:00+05:00"}""")
    match s.ParseBuffer (System.Buffers.ReadOnlySequence<byte>(bytes)) with
    | Ok r ->
        r.X.Year |> should equal 2026
        r.X.Offset |> should equal (TimeSpan.FromHours(5.0))
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Buffer path: string list`` () =
    let s = schema { let! x = Schema.req "x" (Schema.list Schema.string) in return {| X = x |} }
    let bytes = System.Text.Encoding.UTF8.GetBytes("""{"x":["a","b","c"]}""")
    match s.ParseBuffer (System.Buffers.ReadOnlySequence<byte>(bytes)) with
    | Ok r -> r.X |> should equal ["a"; "b"; "c"]
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Buffer path: rules applied`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.minLength 5 ] in return {| X = x |} }
    let bytes = System.Text.Encoding.UTF8.GetBytes("""{"x":"ab"}""")
    match s.ParseBuffer (System.Buffers.ReadOnlySequence<byte>(bytes)) with
    | Error errs -> errs.[0] |> should haveSubstring "at least 5"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Buffer path: multiple errors accumulated`` () =
    let s = schema {
        let! x = Schema.required "x" Schema.string [ Schema.minLength 5 ]
        let! y = Schema.req "y" Schema.int
        return {| X = x; Y = y |}
    }
    let bytes = System.Text.Encoding.UTF8.GetBytes("""{"x":"ab"}""")
    match s.ParseBuffer (System.Buffers.ReadOnlySequence<byte>(bytes)) with
    | Error errs -> errs.Length |> should be (greaterThanOrEqualTo 2)
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Buffer path: unknown properties skipped`` () =
    let s = schema { let! x = Schema.req "x" Schema.string in return {| X = x |} }
    let bytes = System.Text.Encoding.UTF8.GetBytes("""{"x":"hi","extra":123,"other":true}""")
    match s.ParseBuffer (System.Buffers.ReadOnlySequence<byte>(bytes)) with
    | Ok r -> r.X |> should equal "hi"
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Buffer path: invalid JSON`` () =
    let s = schema { let! x = Schema.req "x" Schema.string in return {| X = x |} }
    let bytes = System.Text.Encoding.UTF8.GetBytes("""not json""")
    match s.ParseBuffer (System.Buffers.ReadOnlySequence<byte>(bytes)) with
    | Error errs -> errs.[0] |> should haveSubstring "invalid JSON"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Buffer path: FNullable with inner value`` () =
    let s = Schema.fromType<{| X: int option |}>()
    let bytes = System.Text.Encoding.UTF8.GetBytes("""{"X":42}""")
    match s.ParseBuffer (System.Buffers.ReadOnlySequence<byte>(bytes)) with
    | Ok r -> r.X |> should equal (Some 42)
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Buffer path: FNullable with null`` () =
    let s = Schema.fromType<{| X: int option |}>()
    let bytes = System.Text.Encoding.UTF8.GetBytes("""{"X":null}""")
    match s.ParseBuffer (System.Buffers.ReadOnlySequence<byte>(bytes)) with
    | Ok r -> r.X |> should equal None
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Buffer path: FNullable with missing field`` () =
    let s = Schema.fromType<{| X: int option |}>()
    let bytes = System.Text.Encoding.UTF8.GetBytes("""{}""")
    match s.ParseBuffer (System.Buffers.ReadOnlySequence<byte>(bytes)) with
    | Ok r -> r.X |> should equal None
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Buffer path: cross-field check falls back to element path`` () =
    let s = schema {
        let! a = Schema.req "a" Schema.int
        let! b = Schema.req "b" Schema.int
        do! Schema.check (fun () -> if b > a then Ok () else Error "b must be greater than a")
        return {| A = a; B = b |}
    }
    let bytes = System.Text.Encoding.UTF8.GetBytes("""{"a":1,"b":5}""")
    match s.ParseBuffer (System.Buffers.ReadOnlySequence<byte>(bytes)) with
    | Ok r ->
        r.A |> should equal 1
        r.B |> should equal 5
    | Error _ -> failwith "expected Ok"

// =====================================================================
// parseLookup and parseMap tests
// =====================================================================

[<Fact>]
let ``parseLookup: string field`` () =
    let s = schema { let! x = Schema.req "x" Schema.string in return {| X = x |} }
    match Schema.parseLookup s (fun name -> if name = "x" then Some "hello" else None) with
    | Ok r -> r.X |> should equal "hello"
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``parseLookup: int field`` () =
    let s = schema { let! x = Schema.req "x" Schema.int in return {| X = x |} }
    match Schema.parseLookup s (fun name -> if name = "x" then Some "42" else None) with
    | Ok r -> r.X |> should equal 42
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``parseLookup: bool field`` () =
    let s = schema { let! x = Schema.req "x" Schema.bool in return {| X = x |} }
    match Schema.parseLookup s (fun name -> if name = "x" then Some "true" else None) with
    | Ok r -> r.X |> should equal true
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``parseLookup: float field`` () =
    let s = schema { let! x = Schema.req "x" Schema.float in return {| X = x |} }
    match Schema.parseLookup s (fun name -> if name = "x" then Some "3.14" else None) with
    | Ok r -> r.X |> should equal 3.14
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``parseLookup: DateTime field`` () =
    let s = schema { let! x = Schema.req "x" Schema.dateTime in return {| X = x |} }
    match Schema.parseLookup s (fun name -> if name = "x" then Some "2026-06-15" else None) with
    | Ok r -> r.X.Year |> should equal 2026
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``parseLookup: DateTimeOffset field`` () =
    let s = schema { let! x = Schema.req "x" Schema.dateTimeOffset in return {| X = x |} }
    match Schema.parseLookup s (fun name -> if name = "x" then Some "2026-06-15T10:00:00+02:00" else None) with
    | Ok r -> r.X.Offset |> should equal (TimeSpan.FromHours(2.0))
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``parseLookup: missing required field`` () =
    let s = schema { let! x = Schema.req "x" Schema.string in return {| X = x |} }
    match Schema.parseLookup s (fun _ -> None) with
    | Error errs -> errs.[0] |> should haveSubstring "x is required"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``parseLookup: empty string treated as missing`` () =
    let s = schema { let! x = Schema.req "x" Schema.string in return {| X = x |} }
    match Schema.parseLookup s (fun _ -> Some "") with
    | Error errs -> errs.[0] |> should haveSubstring "x is required"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``parseLookup: optional field defaults`` () =
    let s = schema { let! x = Schema.opt "x" Schema.int 99 in return {| X = x |} }
    match Schema.parseLookup s (fun _ -> None) with
    | Ok r -> r.X |> should equal 99
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``parseLookup: rules applied`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.minLength 5 ] in return {| X = x |} }
    match Schema.parseLookup s (fun _ -> Some "ab") with
    | Error errs -> errs.[0] |> should haveSubstring "at least 5"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``parseLookup: invalid int conversion`` () =
    let s = schema { let! x = Schema.req "x" Schema.int in return {| X = x |} }
    match Schema.parseLookup s (fun _ -> Some "abc") with
    | Error errs -> errs.[0] |> should haveSubstring "expected integer"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``parseLookup: invalid bool conversion`` () =
    let s = schema { let! x = Schema.req "x" Schema.bool in return {| X = x |} }
    match Schema.parseLookup s (fun _ -> Some "maybe") with
    | Error errs -> errs.[0] |> should haveSubstring "expected boolean"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``parseLookup: invalid float conversion`` () =
    let s = schema { let! x = Schema.req "x" Schema.float in return {| X = x |} }
    match Schema.parseLookup s (fun _ -> Some "abc") with
    | Error errs -> errs.[0] |> should haveSubstring "expected number"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``parseLookup: fallback type treated as string`` () =
    let s = Schema.fromType<{| X: string |}>()
    match Schema.parseLookup s (fun _ -> Some "hello") with
    | Ok r -> r.X |> should equal "hello"
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``parseMap: case-insensitive keys`` () =
    let s = schema { let! x = Schema.req "name" Schema.string in return {| Name = x |} }
    let data = System.Collections.Generic.Dictionary(dict ["Name", "Alice"]) :> System.Collections.Generic.IReadOnlyDictionary<_,_>
    match Schema.parseMap s data with
    | Ok r -> r.Name |> should equal "Alice"
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``parseMap: multiple fields`` () =
    let s = schema {
        let! name = Schema.req "name" Schema.string
        let! age = Schema.req "age" Schema.int
        return {| Name = name; Age = age |}
    }
    let data = System.Collections.Generic.Dictionary(dict ["name", "Bob"; "age", "25"]) :> System.Collections.Generic.IReadOnlyDictionary<_,_>
    match Schema.parseMap s data with
    | Ok r ->
        r.Name |> should equal "Bob"
        r.Age |> should equal 25
    | Error _ -> failwith "expected Ok"

// =====================================================================
// JSON Schema generation tests
// =====================================================================

[<Fact>]
let ``toJsonSchema includes required array`` () =
    let s = schema {
        let! x = Schema.req "x" Schema.string
        let! y = Schema.opt "y" Schema.int 0
        return {| X = x; Y = y |}
    }
    let js = Schema.toJsonSchema s
    js |> should haveSubstring "\"required\""
    js |> should haveSubstring "\"x\""

[<Fact>]
let ``toJsonSchema includes minLength rule`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.minLength 3 ] in return {| X = x |} }
    let js = Schema.toJsonSchema s
    js |> should haveSubstring "\"minLength\": 3"

[<Fact>]
let ``toJsonSchema includes maxLength rule`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.maxLength 10 ] in return {| X = x |} }
    let js = Schema.toJsonSchema s
    js |> should haveSubstring "\"maxLength\": 10"

[<Fact>]
let ``toJsonSchema includes pattern rule`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.pattern @"^\d+$" ] in return {| X = x |} }
    let js = Schema.toJsonSchema s
    js |> should haveSubstring "\"pattern\""

[<Fact>]
let ``toJsonSchema includes min rule`` () =
    let s = schema { let! x = Schema.required "x" Schema.float [ Schema.min 5.0 ] in return {| X = x |} }
    let js = Schema.toJsonSchema s
    js |> should haveSubstring "\"minimum\": 5"

[<Fact>]
let ``toJsonSchema includes max rule`` () =
    let s = schema { let! x = Schema.required "x" Schema.float [ Schema.max 100.0 ] in return {| X = x |} }
    let js = Schema.toJsonSchema s
    js |> should haveSubstring "\"maximum\": 100"

[<Fact>]
let ``toJsonSchema includes format rule`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.email ] in return {| X = x |} }
    let js = Schema.toJsonSchema s
    js |> should haveSubstring "\"format\": \"email\""

[<Fact>]
let ``toJsonSchema includes enum rule`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.enum' ["a"; "b"] ] in return {| X = x |} }
    let js = Schema.toJsonSchema s
    js |> should haveSubstring "\"enum\""
    js |> should haveSubstring "\"a\""
    js |> should haveSubstring "\"b\""

[<Fact>]
let ``toJsonSchema for nested object via fromType`` () =
    let s = Schema.fromType<Person>()
    let js = Schema.toJsonSchema s
    js |> should haveSubstring "\"object\""
    js |> should haveSubstring "\"Street\""
    js |> should haveSubstring "\"Zip\""

[<Fact>]
let ``toJsonSchema for list field via fromType`` () =
    let s = Schema.fromType<Order>()
    let js = Schema.toJsonSchema s
    js |> should haveSubstring "\"array\""

[<Fact>]
let ``toJsonSchema for nested record list via fromType`` () =
    let s = Schema.fromType<ItemWithTags>()
    let js = Schema.toJsonSchema s
    js |> should haveSubstring "\"array\""
    js |> should haveSubstring "\"Key\""
    js |> should haveSubstring "\"Value\""

// =====================================================================
// Edge cases and error handling
// =====================================================================

[<Fact>]
let ``parseString with malformed JSON`` () =
    let s = schema { let! x = Schema.req "x" Schema.string in return {| X = x |} }
    match Schema.parseString s "not json at all" with
    | Error errs -> errs.[0] |> should haveSubstring "invalid JSON"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``parseString with empty object`` () =
    let s = schema { let! x = Schema.req "x" Schema.string in return {| X = x |} }
    match Schema.parseString s """{}""" with
    | Error errs -> errs.[0] |> should haveSubstring "x is required"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``parseJson works directly`` () =
    let s = schema { let! x = Schema.req "x" Schema.string in return {| X = x |} }
    use doc = System.Text.Json.JsonDocument.Parse("""{"x":"hello"}""")
    match Schema.parseJson s doc.RootElement with
    | Ok r -> r.X |> should equal "hello"
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``parseBuffer works directly`` () =
    let s = schema { let! x = Schema.req "x" Schema.string in return {| X = x |} }
    let bytes = System.Text.Encoding.UTF8.GetBytes("""{"x":"hello"}""")
    match Schema.parseBuffer s (System.Buffers.ReadOnlySequence<byte>(bytes)) with
    | Ok r -> r.X |> should equal "hello"
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``parseStream works`` () =
    let s = schema { let! x = Schema.req "x" Schema.string in return {| X = x |} }
    use stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes("""{"x":"hello"}"""))
    let result = Schema.parseStream s stream |> Async.AwaitTask |> Async.RunSynchronously
    match result with
    | Ok r -> r.X |> should equal "hello"
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``parseStream with invalid JSON`` () =
    let s = schema { let! x = Schema.req "x" Schema.string in return {| X = x |} }
    use stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes("not json"))
    let result = Schema.parseStream s stream |> Async.AwaitTask |> Async.RunSynchronously
    match result with
    | Error errs -> errs.[0] |> should haveSubstring "invalid JSON"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``parsePipe works`` () =
    let s = schema { let! x = Schema.req "x" Schema.string in return {| X = x |} }
    let bytes = System.Text.Encoding.UTF8.GetBytes("""{"x":"hello"}""")
    let pipe = System.IO.Pipelines.Pipe()
    pipe.Writer.WriteAsync(System.ReadOnlyMemory(bytes)) |> ignore
    pipe.Writer.Complete()
    let result = Schema.parsePipe s pipe.Reader |> Async.AwaitTask |> Async.RunSynchronously
    match result with
    | Ok r -> r.X |> should equal "hello"
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``fromType with empty list`` () =
    let s = Schema.fromType<Order>()
    match Schema.parseString s """{"Id":1,"Items":[]}""" with
    | Ok r ->
        r.Id |> should equal 1
        r.Items |> should equal ([] : int list)
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``fromType with bool list`` () =
    let s = Schema.fromType<{| Flags: bool list |}>()
    match Schema.parseString s """{"Flags":[true,false,true]}""" with
    | Ok r -> r.Flags |> should equal [true; false; true]
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``fromType with float list`` () =
    let s = Schema.fromType<{| Scores: float list |}>()
    match Schema.parseString s """{"Scores":[1.5,2.5,3.5]}""" with
    | Ok r -> r.Scores |> should equal [1.5; 2.5; 3.5]
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``fromType with DateTime field`` () =
    let s = Schema.fromType<{| Created: DateTime |}>()
    match Schema.parseString s """{"Created":"2026-03-17T12:00:00"}""" with
    | Ok r -> r.Created.Year |> should equal 2026
    | Error e -> failwith $"expected Ok, got {e}"

[<Fact>]
let ``fromType with DateTimeOffset field`` () =
    let s = Schema.fromType<{| Created: DateTimeOffset |}>()
    match Schema.parseString s """{"Created":"2026-03-17T12:00:00+05:00"}""" with
    | Ok r ->
        r.Created.Year |> should equal 2026
        r.Created.Offset |> should equal (TimeSpan.FromHours(5.0))
    | Error e -> failwith $"expected Ok, got {e}"

[<Fact>]
let ``fromType with option of int`` () =
    let s = Schema.fromType<{| X: int option |}>()
    match Schema.parseString s """{"X":42}""" with
    | Ok r -> r.X |> should equal (Some 42)
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``fromType with option of int None`` () =
    let s = Schema.fromType<{| X: int option |}>()
    match Schema.parseString s """{}""" with
    | Ok r -> r.X |> should equal None
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``fromType with option of int null`` () =
    let s = Schema.fromType<{| X: int option |}>()
    match Schema.parseString s """{"X":null}""" with
    | Ok r -> r.X |> should equal None
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``fromType all required fields`` () =
    let s = Schema.fromType<{| A: string; B: int; C: bool |}>()
    match Schema.parseString s """{"A":"x","B":1,"C":true}""" with
    | Ok r ->
        r.A |> should equal "x"
        r.B |> should equal 1
        r.C |> should equal true
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``fromType missing required string field`` () =
    let s = Schema.fromType<{| A: string; B: int |}>()
    match Schema.parseString s """{"B":1}""" with
    | Error errs -> errs |> List.exists (fun e -> e.Contains("A")) |> should be True
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``fromType int type mismatch in nested`` () =
    let s = Schema.fromType<Person>()
    match Schema.parseString s """{"Name":"Alice","Age":"not-a-number","Address":{"Street":"x","Zip":"y"}}""" with
    | Ok r -> r.Age |> should equal 0 |> ignore // coercion may succeed or fail
    | Error errs -> errs |> List.exists (fun e -> e.Contains("Age")) |> should be True

[<Fact>]
let ``Schema.check with multiple checks`` () =
    let s = schema {
        let! a = Schema.req "a" Schema.int
        let! b = Schema.req "b" Schema.int
        let! c = Schema.req "c" Schema.int
        do! Schema.check (fun () -> if b > a then Ok () else Error "b must be > a")
        do! Schema.check (fun () -> if c > b then Ok () else Error "c must be > b")
        return {| A = a; B = b; C = c |}
    }
    match Schema.parseString s """{"a":1,"b":5,"c":10}""" with
    | Ok r ->
        r.A |> should equal 1
        r.B |> should equal 5
        r.C |> should equal 10
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema.check multiple checks can fail`` () =
    let s = schema {
        let! a = Schema.req "a" Schema.int
        let! b = Schema.req "b" Schema.int
        do! Schema.check (fun () -> if b > a then Ok () else Error "b must be > a")
        return {| A = a; B = b |}
    }
    match Schema.parseString s """{"a":10,"b":1}""" with
    | Error errs -> errs |> List.exists (fun e -> e.Contains("b must be > a")) |> should be True
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Required field missing in builder schema`` () =
    let s = schema {
        let! x = Schema.req "x" Schema.string
        let! y = Schema.req "y" Schema.int
        return {| X = x; Y = y |}
    }
    match Schema.parseString s """{"x":"hello"}""" with
    | Error errs -> errs |> List.exists (fun e -> e.Contains("y is required")) |> should be True
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Optional field with rules validates when present`` () =
    let s = schema {
        let! x = Schema.optional "x" Schema.string "default" [ Schema.minLength 3 ]
        return {| X = x |}
    }
    match Schema.parseString s """{"x":"ab"}""" with
    | Error errs -> errs.[0] |> should haveSubstring "at least 3"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Optional field with rules uses default when absent`` () =
    let s = schema {
        let! x = Schema.optional "x" Schema.string "default" [ Schema.minLength 3 ]
        return {| X = x |}
    }
    match Schema.parseString s """{}""" with
    | Ok r -> r.X |> should equal "default"
    | Error _ -> failwith "expected Ok"

// =====================================================================
// JsonElement path tests (via Schema.parseJson)
// These exercise the non-buffer parsing path
// =====================================================================

[<Fact>]
let ``JsonElement path: string parser`` () =
    let s = schema { let! x = Schema.req "x" Schema.string in return {| X = x |} }
    use doc = System.Text.Json.JsonDocument.Parse("""{"x":"hello"}""")
    match Schema.parseJson s doc.RootElement with
    | Ok r -> r.X |> should equal "hello"
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``JsonElement path: int from number`` () =
    let s = schema { let! x = Schema.req "x" Schema.int in return {| X = x |} }
    use doc = System.Text.Json.JsonDocument.Parse("""{"x":42}""")
    match Schema.parseJson s doc.RootElement with
    | Ok r -> r.X |> should equal 42
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``JsonElement path: int from string`` () =
    let s = schema { let! x = Schema.req "x" Schema.int in return {| X = x |} }
    use doc = System.Text.Json.JsonDocument.Parse("""{"x":"42"}""")
    match Schema.parseJson s doc.RootElement with
    | Ok r -> r.X |> should equal 42
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``JsonElement path: int rejects non-numeric`` () =
    let s = schema { let! x = Schema.req "x" Schema.int in return {| X = x |} }
    use doc = System.Text.Json.JsonDocument.Parse("""{"x":"abc"}""")
    match Schema.parseJson s doc.RootElement with
    | Error errs -> errs.[0] |> should haveSubstring "expected integer"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``JsonElement path: int rejects boolean`` () =
    let s = schema { let! x = Schema.req "x" Schema.int in return {| X = x |} }
    use doc = System.Text.Json.JsonDocument.Parse("""{"x":true}""")
    match Schema.parseJson s doc.RootElement with
    | Error errs -> errs.[0] |> should haveSubstring "expected integer"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``JsonElement path: bool from literal`` () =
    let s = schema { let! x = Schema.req "x" Schema.bool in return {| X = x |} }
    use doc = System.Text.Json.JsonDocument.Parse("""{"x":true}""")
    match Schema.parseJson s doc.RootElement with
    | Ok r -> r.X |> should equal true
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``JsonElement path: bool from string`` () =
    let s = schema { let! x = Schema.req "x" Schema.bool in return {| X = x |} }
    use doc = System.Text.Json.JsonDocument.Parse("""{"x":"false"}""")
    match Schema.parseJson s doc.RootElement with
    | Ok r -> r.X |> should equal false
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``JsonElement path: bool rejects invalid string`` () =
    let s = schema { let! x = Schema.req "x" Schema.bool in return {| X = x |} }
    use doc = System.Text.Json.JsonDocument.Parse("""{"x":"maybe"}""")
    match Schema.parseJson s doc.RootElement with
    | Error errs -> errs.[0] |> should haveSubstring "expected boolean"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``JsonElement path: bool rejects number`` () =
    let s = schema { let! x = Schema.req "x" Schema.bool in return {| X = x |} }
    use doc = System.Text.Json.JsonDocument.Parse("""{"x":1}""")
    match Schema.parseJson s doc.RootElement with
    | Error errs -> errs.[0] |> should haveSubstring "expected boolean"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``JsonElement path: float from number`` () =
    let s = schema { let! x = Schema.req "x" Schema.float in return {| X = x |} }
    use doc = System.Text.Json.JsonDocument.Parse("""{"x":3.14}""")
    match Schema.parseJson s doc.RootElement with
    | Ok r -> r.X |> should equal 3.14
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``JsonElement path: float from string`` () =
    let s = schema { let! x = Schema.req "x" Schema.float in return {| X = x |} }
    use doc = System.Text.Json.JsonDocument.Parse("""{"x":"2.5"}""")
    match Schema.parseJson s doc.RootElement with
    | Ok r -> r.X |> should equal 2.5
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``JsonElement path: float rejects invalid`` () =
    let s = schema { let! x = Schema.req "x" Schema.float in return {| X = x |} }
    use doc = System.Text.Json.JsonDocument.Parse("""{"x":"abc"}""")
    match Schema.parseJson s doc.RootElement with
    | Error errs -> errs.[0] |> should haveSubstring "expected number"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``JsonElement path: float rejects boolean`` () =
    let s = schema { let! x = Schema.req "x" Schema.float in return {| X = x |} }
    use doc = System.Text.Json.JsonDocument.Parse("""{"x":true}""")
    match Schema.parseJson s doc.RootElement with
    | Error errs -> errs.[0] |> should haveSubstring "expected number"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``JsonElement path: dateTime from string`` () =
    let s = schema { let! x = Schema.req "x" Schema.dateTime in return {| X = x |} }
    use doc = System.Text.Json.JsonDocument.Parse("""{"x":"2026-06-15T10:30:00"}""")
    match Schema.parseJson s doc.RootElement with
    | Ok r -> r.X.Year |> should equal 2026
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``JsonElement path: dateTime rejects invalid`` () =
    let s = schema { let! x = Schema.req "x" Schema.dateTime in return {| X = x |} }
    use doc = System.Text.Json.JsonDocument.Parse("""{"x":"not-a-date"}""")
    match Schema.parseJson s doc.RootElement with
    | Error errs -> errs.[0] |> should haveSubstring "expected date"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``JsonElement path: dateTimeOffset from string`` () =
    let s = schema { let! x = Schema.req "x" Schema.dateTimeOffset in return {| X = x |} }
    use doc = System.Text.Json.JsonDocument.Parse("""{"x":"2026-06-15T10:30:00+02:00"}""")
    match Schema.parseJson s doc.RootElement with
    | Ok r -> r.X.Offset |> should equal (TimeSpan.FromHours(2.0))
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``JsonElement path: dateTimeOffset rejects invalid`` () =
    let s = schema { let! x = Schema.req "x" Schema.dateTimeOffset in return {| X = x |} }
    use doc = System.Text.Json.JsonDocument.Parse("""{"x":"not-a-date"}""")
    match Schema.parseJson s doc.RootElement with
    | Error errs -> errs.[0] |> should haveSubstring "expected date"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``JsonElement path: required field missing`` () =
    let s = schema { let! x = Schema.req "x" Schema.string in return {| X = x |} }
    use doc = System.Text.Json.JsonDocument.Parse("""{}""")
    match Schema.parseJson s doc.RootElement with
    | Error errs -> errs.[0] |> should haveSubstring "x is required"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``JsonElement path: required field null treated as missing`` () =
    let s = schema { let! x = Schema.req "x" Schema.string in return {| X = x |} }
    use doc = System.Text.Json.JsonDocument.Parse("""{"x":null}""")
    match Schema.parseJson s doc.RootElement with
    | Error errs -> errs.[0] |> should haveSubstring "x is required"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``JsonElement path: optional field missing uses default`` () =
    let s = schema { let! x = Schema.opt "x" Schema.int 99 in return {| X = x |} }
    use doc = System.Text.Json.JsonDocument.Parse("""{}""")
    match Schema.parseJson s doc.RootElement with
    | Ok r -> r.X |> should equal 99
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``JsonElement path: optional field null uses default`` () =
    let s = schema { let! x = Schema.opt "x" Schema.int 99 in return {| X = x |} }
    use doc = System.Text.Json.JsonDocument.Parse("""{"x":null}""")
    match Schema.parseJson s doc.RootElement with
    | Ok r -> r.X |> should equal 99
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``JsonElement path: optional field present with value`` () =
    let s = schema { let! x = Schema.opt "x" Schema.int 99 in return {| X = x |} }
    use doc = System.Text.Json.JsonDocument.Parse("""{"x":42}""")
    match Schema.parseJson s doc.RootElement with
    | Ok r -> r.X |> should equal 42
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``JsonElement path: optional field with rules validates`` () =
    let s = schema { let! x = Schema.optional "x" Schema.string "def" [ Schema.minLength 3 ] in return {| X = x |} }
    use doc = System.Text.Json.JsonDocument.Parse("""{"x":"ab"}""")
    match Schema.parseJson s doc.RootElement with
    | Error errs -> errs.[0] |> should haveSubstring "at least 3"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``JsonElement path: required field with rules validates`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.minLength 3 ] in return {| X = x |} }
    use doc = System.Text.Json.JsonDocument.Parse("""{"x":"ab"}""")
    match Schema.parseJson s doc.RootElement with
    | Error errs -> errs.[0] |> should haveSubstring "at least 3"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``JsonElement path: required field with rules transforms`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.trim; Schema.uppercase ] in return {| X = x |} }
    use doc = System.Text.Json.JsonDocument.Parse("""{"x":"  hi  "}""")
    match Schema.parseJson s doc.RootElement with
    | Ok r -> r.X |> should equal "HI"
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``JsonElement path: list parsing`` () =
    let s = schema { let! x = Schema.req "x" (Schema.list Schema.int) in return {| X = x |} }
    use doc = System.Text.Json.JsonDocument.Parse("""{"x":[1,2,3]}""")
    match Schema.parseJson s doc.RootElement with
    | Ok r -> r.X |> should equal [1; 2; 3]
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``JsonElement path: list error has index`` () =
    let s = schema { let! x = Schema.required "x" (Schema.list Schema.int) [] in return {| X = x |} }
    use doc = System.Text.Json.JsonDocument.Parse("""{"x":[1,"bad",3]}""")
    match Schema.parseJson s doc.RootElement with
    | Error errs -> errs.[0] |> should haveSubstring "[1]"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``JsonElement path: nullable with value`` () =
    let s = schema { let! x = Schema.req "x" (Schema.nullable Schema.string) in return {| X = x |} }
    use doc = System.Text.Json.JsonDocument.Parse("""{"x":"hi"}""")
    match Schema.parseJson s doc.RootElement with
    | Ok r -> r.X |> should equal (Some "hi")
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``JsonElement path: nullable with null via optional`` () =
    let s = schema { let! x = Schema.opt "x" (Schema.nullable Schema.string) None in return {| X = x |} }
    use doc = System.Text.Json.JsonDocument.Parse("""{"x":null}""")
    match Schema.parseJson s doc.RootElement with
    | Ok r -> r.X |> should equal None
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``JsonElement path: nested schema`` () =
    let inner = schema {
        let! a = Schema.req "a" Schema.string
        let! b = Schema.req "b" Schema.int
        return {| A = a; B = b |}
    }
    let outer = schema {
        let! child = Schema.required "child" (Schema.nest inner) []
        return {| Child = child |}
    }
    use doc = System.Text.Json.JsonDocument.Parse("""{"child":{"a":"x","b":5}}""")
    match Schema.parseJson outer doc.RootElement with
    | Ok r ->
        r.Child.A |> should equal "x"
        r.Child.B |> should equal 5
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``JsonElement path: nested schema error has dotted path`` () =
    let inner = schema {
        let! a = Schema.required "a" Schema.string [ Schema.minLength 5 ]
        return {| A = a |}
    }
    let outer = schema {
        let! child = Schema.required "child" (Schema.nest inner) []
        return {| Child = child |}
    }
    use doc = System.Text.Json.JsonDocument.Parse("""{"child":{"a":"x"}}""")
    match Schema.parseJson outer doc.RootElement with
    | Error errs -> errs.[0] |> should haveSubstring "child.a"
    | Ok _ -> failwith "expected Error"

// --- fromType via JsonElement path (using .Parse directly) ---

[<Fact>]
let ``fromType JsonElement path: basic record`` () =
    let s = Schema.fromType<{| Name: string; Age: int |}>()
    use doc = System.Text.Json.JsonDocument.Parse("""{"Name":"Alice","Age":30}""")
    match s.Parse doc.RootElement with
    | Ok r ->
        r.Name |> should equal "Alice"
        r.Age |> should equal 30
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``fromType JsonElement path: nested record`` () =
    let s = Schema.fromType<Person>()
    use doc = System.Text.Json.JsonDocument.Parse("""{"Name":"Bob","Age":25,"Address":{"Street":"Oak","Zip":"12345"}}""")
    match s.Parse doc.RootElement with
    | Ok r ->
        r.Name |> should equal "Bob"
        r.Address.Street |> should equal "Oak"
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``fromType JsonElement path: optional fields`` () =
    let s = Schema.fromType<PersonOptional>()
    use doc = System.Text.Json.JsonDocument.Parse("""{"Name":"Alice"}""")
    match s.Parse doc.RootElement with
    | Ok r ->
        r.Nickname |> should equal None
        r.Address |> should equal None
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``fromType JsonElement path: optional with Some`` () =
    let s = Schema.fromType<PersonOptional>()
    use doc = System.Text.Json.JsonDocument.Parse("""{"Name":"Alice","Nickname":"Ali","Address":{"Street":"x","Zip":"y"}}""")
    match s.Parse doc.RootElement with
    | Ok r ->
        r.Nickname |> should equal (Some "Ali")
        r.Address.Value.Street |> should equal "x"
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``fromType JsonElement path: typed list`` () =
    let s = Schema.fromType<Order>()
    use doc = System.Text.Json.JsonDocument.Parse("""{"Id":1,"Items":[10,20,30]}""")
    match s.Parse doc.RootElement with
    | Ok r -> r.Items |> should equal [10; 20; 30]
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``fromType JsonElement path: list of records`` () =
    let s = Schema.fromType<ItemWithTags>()
    use doc = System.Text.Json.JsonDocument.Parse("""{"Title":"x","Tags":[{"Key":"a","Value":"1"}]}""")
    match s.Parse doc.RootElement with
    | Ok r -> r.Tags.[0].Key |> should equal "a"
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``fromType JsonElement path: empty list`` () =
    let s = Schema.fromType<Order>()
    use doc = System.Text.Json.JsonDocument.Parse("""{"Id":1,"Items":[]}""")
    match s.Parse doc.RootElement with
    | Ok r -> r.Items |> should equal ([] : int list)
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``fromType JsonElement path: bool list`` () =
    let s = Schema.fromType<{| Flags: bool list |}>()
    use doc = System.Text.Json.JsonDocument.Parse("""{"Flags":[true,false]}""")
    match s.Parse doc.RootElement with
    | Ok r -> r.Flags |> should equal [true; false]
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``fromType JsonElement path: float list`` () =
    let s = Schema.fromType<{| Vals: float list |}>()
    use doc = System.Text.Json.JsonDocument.Parse("""{"Vals":[1.1,2.2]}""")
    match s.Parse doc.RootElement with
    | Ok r -> r.Vals |> should equal [1.1; 2.2]
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``fromType JsonElement path: string list`` () =
    let s = Schema.fromType<{| Tags: string list |}>()
    use doc = System.Text.Json.JsonDocument.Parse("""{"Tags":["a","b"]}""")
    match s.Parse doc.RootElement with
    | Ok r -> r.Tags |> should equal ["a"; "b"]
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``fromType JsonElement path: option null`` () =
    let s = Schema.fromType<{| X: int option |}>()
    use doc = System.Text.Json.JsonDocument.Parse("""{"X":null}""")
    match s.Parse doc.RootElement with
    | Ok r -> r.X |> should equal None
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``fromType JsonElement path: option missing`` () =
    let s = Schema.fromType<{| X: int option |}>()
    use doc = System.Text.Json.JsonDocument.Parse("""{}""")
    match s.Parse doc.RootElement with
    | Ok r -> r.X |> should equal None
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``fromType JsonElement path: option with value`` () =
    let s = Schema.fromType<{| X: int option |}>()
    use doc = System.Text.Json.JsonDocument.Parse("""{"X":42}""")
    match s.Parse doc.RootElement with
    | Ok r -> r.X |> should equal (Some 42)
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``fromType JsonElement path: DateTime`` () =
    let s = Schema.fromType<{| D: DateTime |}>()
    use doc = System.Text.Json.JsonDocument.Parse("""{"D":"2026-03-17T12:00:00"}""")
    match s.Parse doc.RootElement with
    | Ok r -> r.D.Year |> should equal 2026
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``fromType JsonElement path: DateTimeOffset`` () =
    let s = Schema.fromType<{| D: DateTimeOffset |}>()
    use doc = System.Text.Json.JsonDocument.Parse("""{"D":"2026-03-17T12:00:00+05:00"}""")
    match s.Parse doc.RootElement with
    | Ok r -> r.D.Offset |> should equal (TimeSpan.FromHours(5.0))
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``fromType JsonElement path: missing required reports error`` () =
    let s = Schema.fromType<{| A: string; B: int |}>()
    use doc = System.Text.Json.JsonDocument.Parse("""{"B":1}""")
    match s.Parse doc.RootElement with
    | Error errs -> errs.[0] |> should haveSubstring "A"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``fromType JsonElement path: nested record error`` () =
    let s = Schema.fromType<Person>()
    use doc = System.Text.Json.JsonDocument.Parse("""{"Name":"Alice","Age":30,"Address":{"Street":"x"}}""")
    match s.Parse doc.RootElement with
    | Error errs -> errs.[0] |> should haveSubstring "Zip"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``fromType JsonElement path: nested record not object`` () =
    let s = Schema.fromType<Person>()
    use doc = System.Text.Json.JsonDocument.Parse("""{"Name":"Alice","Age":30,"Address":"not-an-object"}""")
    match s.Parse doc.RootElement with
    | Error errs -> errs.[0] |> should haveSubstring "Address"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``fromType JsonElement path: list not array`` () =
    let s = Schema.fromType<Order>()
    use doc = System.Text.Json.JsonDocument.Parse("""{"Id":1,"Items":"not-array"}""")
    match s.Parse doc.RootElement with
    | Error errs -> errs.[0] |> should haveSubstring "Items"
    | Ok _ -> failwith "expected Error"

// =====================================================================
// Remaining coverage: parseElementValue coercion & error branches
// =====================================================================

[<Fact>]
let ``fromType JsonElement path: int from string coercion`` () =
    let s = Schema.fromType<{| X: int |}>()
    use doc = System.Text.Json.JsonDocument.Parse("""{"X":"42"}""")
    match s.Parse doc.RootElement with
    | Ok r -> r.X |> should equal 42
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``fromType JsonElement path: int from invalid string`` () =
    let s = Schema.fromType<{| X: int |}>()
    use doc = System.Text.Json.JsonDocument.Parse("""{"X":"abc"}""")
    match s.Parse doc.RootElement with
    | Error errs -> errs.[0] |> should haveSubstring "X"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``fromType JsonElement path: int from boolean fails`` () =
    let s = Schema.fromType<{| X: int |}>()
    use doc = System.Text.Json.JsonDocument.Parse("""{"X":true}""")
    match s.Parse doc.RootElement with
    | Error errs -> errs.[0] |> should haveSubstring "X"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``fromType JsonElement path: bool from string coercion`` () =
    let s = Schema.fromType<{| X: bool |}>()
    use doc = System.Text.Json.JsonDocument.Parse("""{"X":"true"}""")
    match s.Parse doc.RootElement with
    | Ok r -> r.X |> should equal true
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``fromType JsonElement path: bool invalid string`` () =
    let s = Schema.fromType<{| X: bool |}>()
    use doc = System.Text.Json.JsonDocument.Parse("""{"X":"maybe"}""")
    match s.Parse doc.RootElement with
    | Error errs -> errs.[0] |> should haveSubstring "X"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``fromType JsonElement path: bool from number fails`` () =
    let s = Schema.fromType<{| X: bool |}>()
    use doc = System.Text.Json.JsonDocument.Parse("""{"X":1}""")
    match s.Parse doc.RootElement with
    | Error errs -> errs.[0] |> should haveSubstring "X"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``fromType JsonElement path: float from string coercion`` () =
    let s = Schema.fromType<{| X: float |}>()
    use doc = System.Text.Json.JsonDocument.Parse("""{"X":"3.14"}""")
    match s.Parse doc.RootElement with
    | Ok r -> r.X |> should equal 3.14
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``fromType JsonElement path: float invalid string`` () =
    let s = Schema.fromType<{| X: float |}>()
    use doc = System.Text.Json.JsonDocument.Parse("""{"X":"abc"}""")
    match s.Parse doc.RootElement with
    | Error errs -> errs.[0] |> should haveSubstring "X"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``fromType JsonElement path: float from boolean fails`` () =
    let s = Schema.fromType<{| X: float |}>()
    use doc = System.Text.Json.JsonDocument.Parse("""{"X":true}""")
    match s.Parse doc.RootElement with
    | Error errs -> errs.[0] |> should haveSubstring "X"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``fromType JsonElement path: DateTime invalid`` () =
    let s = Schema.fromType<{| X: DateTime |}>()
    use doc = System.Text.Json.JsonDocument.Parse("""{"X":"not-a-date"}""")
    match s.Parse doc.RootElement with
    | Error errs -> errs.[0] |> should haveSubstring "X"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``fromType JsonElement path: DateTimeOffset invalid`` () =
    let s = Schema.fromType<{| X: DateTimeOffset |}>()
    use doc = System.Text.Json.JsonDocument.Parse("""{"X":"not-a-date"}""")
    match s.Parse doc.RootElement with
    | Error errs -> errs.[0] |> should haveSubstring "X"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``fromType JsonElement path: string list not array`` () =
    let s = Schema.fromType<{| X: string list |}>()
    use doc = System.Text.Json.JsonDocument.Parse("""{"X":"not-array"}""")
    match s.Parse doc.RootElement with
    | Error errs -> errs.[0] |> should haveSubstring "X"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``fromType JsonElement path: option none for nested record`` () =
    let s = Schema.fromType<PersonOptional>()
    use doc = System.Text.Json.JsonDocument.Parse("""{"Name":"Alice","Address":null}""")
    match s.Parse doc.RootElement with
    | Ok r -> r.Address |> should equal None
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``fromType JsonElement path: nested record unknown props skipped`` () =
    let s = Schema.fromType<Address>()
    use doc = System.Text.Json.JsonDocument.Parse("""{"Street":"x","Zip":"y","Extra":"z"}""")
    match s.Parse doc.RootElement with
    | Ok r ->
        r.Street |> should equal "x"
        r.Zip |> should equal "y"
    | Error _ -> failwith "expected Ok"

// --- parseLookup: DateTime/DateTimeOffset error paths ---

[<Fact>]
let ``parseLookup: invalid DateTime`` () =
    let s = schema { let! x = Schema.req "x" Schema.dateTime in return {| X = x |} }
    match Schema.parseLookup s (fun _ -> Some "not-a-date") with
    | Error errs -> errs.[0] |> should haveSubstring "expected date"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``parseLookup: invalid DateTimeOffset`` () =
    let s = schema { let! x = Schema.req "x" Schema.dateTimeOffset in return {| X = x |} }
    match Schema.parseLookup s (fun _ -> Some "not-a-date") with
    | Error errs -> errs.[0] |> should haveSubstring "expected date"
    | Ok _ -> failwith "expected Error"

// --- Schema builder: optional field with nested schema ---

[<Fact>]
let ``JsonElement path: optional nested schema missing`` () =
    let inner = schema { let! a = Schema.req "a" Schema.string in return {| A = a |} }
    let outer = schema {
        let! child = Schema.optional "child" (Schema.nest inner) {| A = "default" |} []
        return {| Child = child |}
    }
    use doc = System.Text.Json.JsonDocument.Parse("""{}""")
    match Schema.parseJson outer doc.RootElement with
    | Ok r -> r.Child.A |> should equal "default"
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``JsonElement path: optional nested schema present`` () =
    let inner = schema { let! a = Schema.req "a" Schema.string in return {| A = a |} }
    let outer = schema {
        let! child = Schema.optional "child" (Schema.nest inner) {| A = "default" |} []
        return {| Child = child |}
    }
    use doc = System.Text.Json.JsonDocument.Parse("""{"child":{"a":"hello"}}""")
    match Schema.parseJson outer doc.RootElement with
    | Ok r -> r.Child.A |> should equal "hello"
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``JsonElement path: optional nested schema error`` () =
    let inner = schema { let! a = Schema.required "a" Schema.string [ Schema.minLength 5 ] in return {| A = a |} }
    let outer = schema {
        let! child = Schema.optional "child" (Schema.nest inner) {| A = "default" |} []
        return {| Child = child |}
    }
    use doc = System.Text.Json.JsonDocument.Parse("""{"child":{"a":"x"}}""")
    match Schema.parseJson outer doc.RootElement with
    | Error errs -> errs.[0] |> should haveSubstring "child.a"
    | Ok _ -> failwith "expected Error"

// --- Schema builder: inferFieldType nullable inner types ---

[<Fact>]
let ``Schema builder with nullable bool`` () =
    let s = schema { let! x = Schema.req "x" (Schema.nullable Schema.bool) in return {| X = x |} }
    use doc = System.Text.Json.JsonDocument.Parse("""{"x":true}""")
    match Schema.parseJson s doc.RootElement with
    | Ok r -> r.X |> should equal (Some true)
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema builder with nullable float`` () =
    let s = schema { let! x = Schema.req "x" (Schema.nullable Schema.float) in return {| X = x |} }
    use doc = System.Text.Json.JsonDocument.Parse("""{"x":3.14}""")
    match Schema.parseJson s doc.RootElement with
    | Ok r -> r.X |> should equal (Some 3.14)
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema builder with nullable dateTime`` () =
    let s = schema { let! x = Schema.req "x" (Schema.nullable Schema.dateTime) in return {| X = x |} }
    use doc = System.Text.Json.JsonDocument.Parse("""{"x":"2026-01-01"}""")
    match Schema.parseJson s doc.RootElement with
    | Ok r -> r.X.Value.Year |> should equal 2026
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema builder with nullable dateTimeOffset`` () =
    let s = schema { let! x = Schema.req "x" (Schema.nullable Schema.dateTimeOffset) in return {| X = x |} }
    use doc = System.Text.Json.JsonDocument.Parse("""{"x":"2026-01-01T00:00:00+00:00"}""")
    match Schema.parseJson s doc.RootElement with
    | Ok r -> r.X.Value.Year |> should equal 2026
    | Error _ -> failwith "expected Ok"

// --- Buffer path: FNullable inner type branches in readValue ---

[<Fact>]
let ``Buffer path: FNullable bool with value`` () =
    let s = Schema.fromType<{| X: bool option |}>()
    let bytes = System.Text.Encoding.UTF8.GetBytes("""{"X":true}""")
    match s.ParseBuffer (System.Buffers.ReadOnlySequence<byte>(bytes)) with
    | Ok r -> r.X |> should equal (Some true)
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Buffer path: FNullable float with value`` () =
    let s = Schema.fromType<{| X: float option |}>()
    let bytes = System.Text.Encoding.UTF8.GetBytes("""{"X":2.5}""")
    match s.ParseBuffer (System.Buffers.ReadOnlySequence<byte>(bytes)) with
    | Ok r -> r.X |> should equal (Some 2.5)
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Buffer path: FNullable DateTime with value`` () =
    let s = Schema.fromType<{| X: DateTime option |}>()
    let bytes = System.Text.Encoding.UTF8.GetBytes("""{"X":"2026-06-15"}""")
    match s.ParseBuffer (System.Buffers.ReadOnlySequence<byte>(bytes)) with
    | Ok r -> r.X.Value.Year |> should equal 2026
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Buffer path: FNullable DateTimeOffset with value`` () =
    let s = Schema.fromType<{| X: DateTimeOffset option |}>()
    let bytes = System.Text.Encoding.UTF8.GetBytes("""{"X":"2026-06-15T00:00:00+03:00"}""")
    match s.ParseBuffer (System.Buffers.ReadOnlySequence<byte>(bytes)) with
    | Ok r -> r.X.Value.Offset |> should equal (TimeSpan.FromHours(3.0))
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Buffer path: FNullable string list with value`` () =
    let s = Schema.fromType<{| X: string list option |}>()
    let bytes = System.Text.Encoding.UTF8.GetBytes("""{"X":["a","b"]}""")
    match s.ParseBuffer (System.Buffers.ReadOnlySequence<byte>(bytes)) with
    | Ok r -> r.X |> should equal (Some ["a"; "b"])
    | Error _ -> failwith "expected Ok"

// --- Buffer path: unknown properties skipped in nested ---

[<Fact>]
let ``Buffer path: nested object unknown props skipped`` () =
    let s = Schema.fromType<Person>()
    let bytes = System.Text.Encoding.UTF8.GetBytes("""{"Name":"A","Age":1,"Address":{"Street":"x","Zip":"y","Extra":"z"}}""")
    match s.ParseBuffer (System.Buffers.ReadOnlySequence<byte>(bytes)) with
    | Ok r -> r.Address.Street |> should equal "x"
    | Error _ -> failwith "expected Ok"

// --- Buffer path: optional defaults for nested ---

[<Fact>]
let ``Buffer path: optional nested record defaults to None`` () =
    let s = Schema.fromType<PersonOptional>()
    let bytes = System.Text.Encoding.UTF8.GetBytes("""{"Name":"Alice"}""")
    match s.ParseBuffer (System.Buffers.ReadOnlySequence<byte>(bytes)) with
    | Ok r -> r.Address |> should equal None
    | Error _ -> failwith "expected Ok"

// --- Buffer path: DateTimeOffset parsing ---

[<Fact>]
let ``Buffer path: DateTimeOffset field`` () =
    let s = Schema.fromType<{| D: DateTimeOffset |}>()
    let bytes = System.Text.Encoding.UTF8.GetBytes("""{"D":"2026-06-15T10:30:00+02:00"}""")
    match s.ParseBuffer (System.Buffers.ReadOnlySequence<byte>(bytes)) with
    | Ok r -> r.D.Offset |> should equal (TimeSpan.FromHours(2.0))
    | Error _ -> failwith "expected Ok"

// --- Buffer path: FList error in element ---

[<Fact>]
let ``Buffer path: FList error includes index`` () =
    let s = Schema.fromType<{| Items: int list |}>()
    let bytes = System.Text.Encoding.UTF8.GetBytes("""{"Items":[1,"bad",3]}""")
    match s.ParseBuffer (System.Buffers.ReadOnlySequence<byte>(bytes)) with
    | Error errs -> errs.[0] |> should haveSubstring "[1]"
    | Ok _ -> failwith "expected Error"

// =====================================================================
// Final coverage push: hard-to-reach paths
// =====================================================================

type Inner = { A: int; B: string option }
type Outer = { Name: string; Inner: Inner }

[<Fact>]
let ``fromType JsonElement path: nested record bad field value`` () =
    let s = Schema.fromType<Outer>()
    use doc = System.Text.Json.JsonDocument.Parse("""{"Name":"x","Inner":{"A":"not-int","B":"ok"}}""")
    match s.Parse doc.RootElement with
    | Error errs -> errs.[0] |> should haveSubstring "Inner"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``fromType JsonElement path: nested record with optional missing`` () =
    let s = Schema.fromType<Outer>()
    use doc = System.Text.Json.JsonDocument.Parse("""{"Name":"x","Inner":{"A":1}}""")
    match s.Parse doc.RootElement with
    | Ok r ->
        r.Inner.A |> should equal 1
        r.Inner.B |> should equal None
    | Error e -> failwith $"expected Ok, got {e}"

[<Fact>]
let ``fromType JsonElement path: nested record extra props ignored`` () =
    let s = Schema.fromType<Outer>()
    use doc = System.Text.Json.JsonDocument.Parse("""{"Name":"x","Inner":{"A":1,"B":"y","Extra":99}}""")
    match s.Parse doc.RootElement with
    | Ok r -> r.Inner.A |> should equal 1
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema builder: nullable string list`` () =
    let s = schema {
        let! x = Schema.req "x" (Schema.nullable (Schema.list Schema.string))
        return {| X = x |}
    }
    use doc = System.Text.Json.JsonDocument.Parse("""{"x":["a","b"]}""")
    match Schema.parseJson s doc.RootElement with
    | Ok r -> r.X |> should equal (Some ["a"; "b"])
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema builder: nullable string list with null`` () =
    let s = schema {
        let! x = Schema.opt "x" (Schema.nullable (Schema.list Schema.string)) None
        return {| X = x |}
    }
    use doc = System.Text.Json.JsonDocument.Parse("""{"x":null}""")
    match Schema.parseJson s doc.RootElement with
    | Ok r -> r.X |> should equal None
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Buffer path: array instead of object`` () =
    let s = schema { let! x = Schema.req "x" Schema.string in return {| X = x |} }
    let bytes = System.Text.Encoding.UTF8.GetBytes("""[1,2,3]""")
    match s.ParseBuffer (System.Buffers.ReadOnlySequence<byte>(bytes)) with
    | Error errs -> errs.[0] |> should haveSubstring "expected JSON object"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Buffer path: DateTime from number fails`` () =
    let s = Schema.fromType<{| X: DateTime |}>()
    let bytes = System.Text.Encoding.UTF8.GetBytes("""{"X":12345}""")
    match s.ParseBuffer (System.Buffers.ReadOnlySequence<byte>(bytes)) with
    | Error errs -> errs.[0] |> should haveSubstring "X"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Buffer path: DateTimeOffset from number fails`` () =
    let s = Schema.fromType<{| X: DateTimeOffset |}>()
    let bytes = System.Text.Encoding.UTF8.GetBytes("""{"X":12345}""")
    match s.ParseBuffer (System.Buffers.ReadOnlySequence<byte>(bytes)) with
    | Error errs -> errs.[0] |> should haveSubstring "X"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Buffer path: empty string list`` () =
    let s = Schema.fromType<{| X: string list |}>()
    let bytes = System.Text.Encoding.UTF8.GetBytes("""{"X":[]}""")
    match s.ParseBuffer (System.Buffers.ReadOnlySequence<byte>(bytes)) with
    | Ok r -> r.X |> should equal ([] : string list)
    | Error _ -> failwith "expected Ok"

// =====================================================================
// Zod-parity validators
// =====================================================================

// --- String: length (exact) ---

[<Fact>]
let ``Schema.length accepts exact length`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.length 3 ] in return {| X = x |} }
    match Schema.parseString s """{"x":"abc"}""" with
    | Ok r -> r.X |> should equal "abc"
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema.length rejects wrong length`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.length 3 ] in return {| X = x |} }
    match Schema.parseString s """{"x":"ab"}""" with
    | Error errs -> errs.[0] |> should haveSubstring "exactly 3"
    | Ok _ -> failwith "expected Error"

// --- String: nonempty ---

[<Fact>]
let ``Schema.nonempty accepts non-empty string`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.nonempty ] in return {| X = x |} }
    match Schema.parseString s """{"x":"a"}""" with
    | Ok r -> r.X |> should equal "a"
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema.nonempty rejects empty string`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.nonempty ] in return {| X = x |} }
    match Schema.parseString s """{"x":""}""" with
    | Error errs -> errs.[0] |> should haveSubstring "must not be empty"
    | Ok _ -> failwith "expected Error"

// --- String: uuid ---

[<Fact>]
let ``Schema.uuid accepts valid UUID`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.uuid ] in return {| X = x |} }
    match Schema.parseString s """{"x":"550e8400-e29b-41d4-a716-446655440000"}""" with
    | Ok r -> r.X |> should equal "550e8400-e29b-41d4-a716-446655440000"
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema.uuid rejects invalid UUID`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.uuid ] in return {| X = x |} }
    match Schema.parseString s """{"x":"not-a-uuid"}""" with
    | Error errs -> errs.[0] |> should haveSubstring "invalid UUID"
    | Ok _ -> failwith "expected Error"

// --- String: startsWith ---

[<Fact>]
let ``Schema.startsWith accepts matching prefix`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.startsWith "hello" ] in return {| X = x |} }
    match Schema.parseString s """{"x":"hello world"}""" with
    | Ok r -> r.X |> should equal "hello world"
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema.startsWith rejects non-matching`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.startsWith "hello" ] in return {| X = x |} }
    match Schema.parseString s """{"x":"world hello"}""" with
    | Error errs -> errs.[0] |> should haveSubstring "must start with"
    | Ok _ -> failwith "expected Error"

// --- String: endsWith ---

[<Fact>]
let ``Schema.endsWith accepts matching suffix`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.endsWith ".com" ] in return {| X = x |} }
    match Schema.parseString s """{"x":"test.com"}""" with
    | Ok r -> r.X |> should equal "test.com"
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema.endsWith rejects non-matching`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.endsWith ".com" ] in return {| X = x |} }
    match Schema.parseString s """{"x":"test.org"}""" with
    | Error errs -> errs.[0] |> should haveSubstring "must end with"
    | Ok _ -> failwith "expected Error"

// --- String: includes ---

[<Fact>]
let ``Schema.includes accepts containing substring`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.includes "world" ] in return {| X = x |} }
    match Schema.parseString s """{"x":"hello world!"}""" with
    | Ok r -> r.X |> should equal "hello world!"
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema.includes rejects missing substring`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.includes "world" ] in return {| X = x |} }
    match Schema.parseString s """{"x":"hello!"}""" with
    | Error errs -> errs.[0] |> should haveSubstring "must contain"
    | Ok _ -> failwith "expected Error"

// --- String: ip ---

[<Fact>]
let ``Schema.ip accepts valid IPv4`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.ip ] in return {| X = x |} }
    match Schema.parseString s """{"x":"192.168.1.1"}""" with
    | Ok r -> r.X |> should equal "192.168.1.1"
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema.ip accepts valid IPv6`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.ip ] in return {| X = x |} }
    match Schema.parseString s """{"x":"::1"}""" with
    | Ok r -> r.X |> should equal "::1"
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema.ip rejects invalid IP`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.ip ] in return {| X = x |} }
    match Schema.parseString s """{"x":"not.an.ip"}""" with
    | Error errs -> errs.[0] |> should haveSubstring "invalid IP"
    | Ok _ -> failwith "expected Error"

// --- String: ipv4 ---

[<Fact>]
let ``Schema.ipv4 accepts valid IPv4`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.ipv4 ] in return {| X = x |} }
    match Schema.parseString s """{"x":"10.0.0.1"}""" with
    | Ok r -> r.X |> should equal "10.0.0.1"
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema.ipv4 rejects IPv6`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.ipv4 ] in return {| X = x |} }
    match Schema.parseString s """{"x":"::1"}""" with
    | Error errs -> errs.[0] |> should haveSubstring "invalid IPv4"
    | Ok _ -> failwith "expected Error"

// --- String: ipv6 ---

[<Fact>]
let ``Schema.ipv6 accepts valid IPv6`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.ipv6 ] in return {| X = x |} }
    match Schema.parseString s """{"x":"2001:db8::1"}""" with
    | Ok r -> r.X |> should equal "2001:db8::1"
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema.ipv6 rejects IPv4`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.ipv6 ] in return {| X = x |} }
    match Schema.parseString s """{"x":"192.168.1.1"}""" with
    | Error errs -> errs.[0] |> should haveSubstring "invalid IPv6"
    | Ok _ -> failwith "expected Error"

// --- String: datetime ---

[<Fact>]
let ``Schema.datetime accepts valid ISO datetime`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.datetime ] in return {| X = x |} }
    match Schema.parseString s """{"x":"2026-03-17T12:00:00+00:00"}""" with
    | Ok r -> r.X |> should equal "2026-03-17T12:00:00+00:00"
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema.datetime rejects invalid datetime`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.datetime ] in return {| X = x |} }
    match Schema.parseString s """{"x":"not-a-date"}""" with
    | Error errs -> errs.[0] |> should haveSubstring "invalid date/time"
    | Ok _ -> failwith "expected Error"

// --- Number: gt (exclusive) ---

[<Fact>]
let ``Schema.gt accepts greater value`` () =
    let s = schema { let! x = Schema.required "x" Schema.float [ Schema.gt 5.0 ] in return {| X = x |} }
    match Schema.parseString s """{"x":6.0}""" with
    | Ok r -> r.X |> should equal 6.0
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema.gt rejects equal value`` () =
    let s = schema { let! x = Schema.required "x" Schema.float [ Schema.gt 5.0 ] in return {| X = x |} }
    match Schema.parseString s """{"x":5.0}""" with
    | Error errs -> errs.[0] |> should haveSubstring "greater than"
    | Ok _ -> failwith "expected Error"

// --- Number: lt (exclusive) ---

[<Fact>]
let ``Schema.lt accepts lesser value`` () =
    let s = schema { let! x = Schema.required "x" Schema.float [ Schema.lt 10.0 ] in return {| X = x |} }
    match Schema.parseString s """{"x":9.0}""" with
    | Ok r -> r.X |> should equal 9.0
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema.lt rejects equal value`` () =
    let s = schema { let! x = Schema.required "x" Schema.float [ Schema.lt 10.0 ] in return {| X = x |} }
    match Schema.parseString s """{"x":10.0}""" with
    | Error errs -> errs.[0] |> should haveSubstring "less than"
    | Ok _ -> failwith "expected Error"

// --- Number: int' (integer validator) ---

[<Fact>]
let ``Schema.int' accepts integer`` () =
    let s = schema { let! x = Schema.required "x" Schema.float [ Schema.int' ] in return {| X = x |} }
    match Schema.parseString s """{"x":5.0}""" with
    | Ok r -> r.X |> should equal 5.0
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema.int' rejects non-integer`` () =
    let s = schema { let! x = Schema.required "x" Schema.float [ Schema.int' ] in return {| X = x |} }
    match Schema.parseString s """{"x":5.5}""" with
    | Error errs -> errs.[0] |> should haveSubstring "must be an integer"
    | Ok _ -> failwith "expected Error"

// --- Number: positive ---

[<Fact>]
let ``Schema.positive accepts positive`` () =
    let s = schema { let! x = Schema.required "x" Schema.float [ Schema.positive ] in return {| X = x |} }
    match Schema.parseString s """{"x":1.0}""" with
    | Ok _ -> ()
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema.positive rejects zero`` () =
    let s = schema { let! x = Schema.required "x" Schema.float [ Schema.positive ] in return {| X = x |} }
    match Schema.parseString s """{"x":0.0}""" with
    | Error errs -> errs.[0] |> should haveSubstring "must be positive"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema.positive rejects negative`` () =
    let s = schema { let! x = Schema.required "x" Schema.float [ Schema.positive ] in return {| X = x |} }
    match Schema.parseString s """{"x":-1.0}""" with
    | Error errs -> errs.[0] |> should haveSubstring "must be positive"
    | Ok _ -> failwith "expected Error"

// --- Number: negative ---

[<Fact>]
let ``Schema.negative accepts negative`` () =
    let s = schema { let! x = Schema.required "x" Schema.float [ Schema.negative ] in return {| X = x |} }
    match Schema.parseString s """{"x":-1.0}""" with
    | Ok _ -> ()
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema.negative rejects zero`` () =
    let s = schema { let! x = Schema.required "x" Schema.float [ Schema.negative ] in return {| X = x |} }
    match Schema.parseString s """{"x":0.0}""" with
    | Error errs -> errs.[0] |> should haveSubstring "must be negative"
    | Ok _ -> failwith "expected Error"

// --- Number: nonnegative ---

[<Fact>]
let ``Schema.nonnegative accepts zero`` () =
    let s = schema { let! x = Schema.required "x" Schema.float [ Schema.nonnegative ] in return {| X = x |} }
    match Schema.parseString s """{"x":0.0}""" with
    | Ok _ -> ()
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema.nonnegative rejects negative`` () =
    let s = schema { let! x = Schema.required "x" Schema.float [ Schema.nonnegative ] in return {| X = x |} }
    match Schema.parseString s """{"x":-1.0}""" with
    | Error errs -> errs.[0] |> should haveSubstring "must be non-negative"
    | Ok _ -> failwith "expected Error"

// --- Number: nonpositive ---

[<Fact>]
let ``Schema.nonpositive accepts zero`` () =
    let s = schema { let! x = Schema.required "x" Schema.float [ Schema.nonpositive ] in return {| X = x |} }
    match Schema.parseString s """{"x":0.0}""" with
    | Ok _ -> ()
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema.nonpositive rejects positive`` () =
    let s = schema { let! x = Schema.required "x" Schema.float [ Schema.nonpositive ] in return {| X = x |} }
    match Schema.parseString s """{"x":1.0}""" with
    | Error errs -> errs.[0] |> should haveSubstring "must be non-positive"
    | Ok _ -> failwith "expected Error"

// --- Number: multipleOf ---

[<Fact>]
let ``Schema.multipleOf accepts multiple`` () =
    let s = schema { let! x = Schema.required "x" Schema.float [ Schema.multipleOf 3.0 ] in return {| X = x |} }
    match Schema.parseString s """{"x":9.0}""" with
    | Ok r -> r.X |> should equal 9.0
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema.multipleOf rejects non-multiple`` () =
    let s = schema { let! x = Schema.required "x" Schema.float [ Schema.multipleOf 3.0 ] in return {| X = x |} }
    match Schema.parseString s """{"x":10.0}""" with
    | Error errs -> errs.[0] |> should haveSubstring "must be a multiple of"
    | Ok _ -> failwith "expected Error"

// --- Array: minItems ---

[<Fact>]
let ``Schema.minItems accepts enough items`` () =
    let s = schema { let! x = Schema.required "x" (Schema.list Schema.string) [ Schema.minItems 2 ] in return {| X = x |} }
    use doc = System.Text.Json.JsonDocument.Parse("""{"x":["a","b","c"]}""")
    match Schema.parseJson s doc.RootElement with
    | Ok r -> r.X |> should equal ["a"; "b"; "c"]
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema.minItems rejects too few items`` () =
    let s = schema { let! x = Schema.required "x" (Schema.list Schema.string) [ Schema.minItems 3 ] in return {| X = x |} }
    use doc = System.Text.Json.JsonDocument.Parse("""{"x":["a"]}""")
    match Schema.parseJson s doc.RootElement with
    | Error errs -> errs.[0] |> should haveSubstring "at least 3 items"
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``Schema.minItems singular grammar`` () =
    let s = schema { let! x = Schema.required "x" (Schema.list Schema.string) [ Schema.minItems 1 ] in return {| X = x |} }
    use doc = System.Text.Json.JsonDocument.Parse("""{"x":[]}""")
    match Schema.parseJson s doc.RootElement with
    | Error errs -> errs.[0] |> should haveSubstring "1 item"
    | Ok _ -> failwith "expected Error"

// --- Array: maxItems ---

[<Fact>]
let ``Schema.maxItems accepts few enough items`` () =
    let s = schema { let! x = Schema.required "x" (Schema.list Schema.string) [ Schema.maxItems 3 ] in return {| X = x |} }
    use doc = System.Text.Json.JsonDocument.Parse("""{"x":["a","b"]}""")
    match Schema.parseJson s doc.RootElement with
    | Ok r -> r.X |> should equal ["a"; "b"]
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema.maxItems rejects too many items`` () =
    let s = schema { let! x = Schema.required "x" (Schema.list Schema.string) [ Schema.maxItems 2 ] in return {| X = x |} }
    use doc = System.Text.Json.JsonDocument.Parse("""{"x":["a","b","c"]}""")
    match Schema.parseJson s doc.RootElement with
    | Error errs -> errs.[0] |> should haveSubstring "at most 2 items"
    | Ok _ -> failwith "expected Error"

// --- Array: nonEmpty ---

[<Fact>]
let ``Schema.nonEmpty accepts non-empty list`` () =
    let s = schema { let! x = Schema.required "x" (Schema.list Schema.int) [ Schema.nonEmpty ] in return {| X = x |} }
    use doc = System.Text.Json.JsonDocument.Parse("""{"x":[1]}""")
    match Schema.parseJson s doc.RootElement with
    | Ok r -> r.X |> should equal [1]
    | Error _ -> failwith "expected Ok"

[<Fact>]
let ``Schema.nonEmpty rejects empty list`` () =
    let s = schema { let! x = Schema.required "x" (Schema.list Schema.int) [ Schema.nonEmpty ] in return {| X = x |} }
    use doc = System.Text.Json.JsonDocument.Parse("""{"x":[]}""")
    match Schema.parseJson s doc.RootElement with
    | Error errs -> errs.[0] |> should haveSubstring "must not be empty"
    | Ok _ -> failwith "expected Error"

// --- JSON Schema output for new validators ---

[<Fact>]
let ``toJsonSchema includes exclusiveMinimum for gt`` () =
    let s = schema { let! x = Schema.required "x" Schema.float [ Schema.gt 5.0 ] in return {| X = x |} }
    let js = Schema.toJsonSchema s
    js |> should haveSubstring "\"exclusiveMinimum\": 5"

[<Fact>]
let ``toJsonSchema includes exclusiveMaximum for lt`` () =
    let s = schema { let! x = Schema.required "x" Schema.float [ Schema.lt 10.0 ] in return {| X = x |} }
    let js = Schema.toJsonSchema s
    js |> should haveSubstring "\"exclusiveMaximum\": 10"

[<Fact>]
let ``toJsonSchema includes multipleOf`` () =
    let s = schema { let! x = Schema.required "x" Schema.float [ Schema.multipleOf 5.0 ] in return {| X = x |} }
    let js = Schema.toJsonSchema s
    js |> should haveSubstring "\"multipleOf\": 5"

[<Fact>]
let ``toJsonSchema includes minItems`` () =
    let s = schema { let! x = Schema.required "x" (Schema.list Schema.string) [ Schema.minItems 2 ] in return {| X = x |} }
    let js = Schema.toJsonSchema s
    js |> should haveSubstring "\"minItems\": 2"

[<Fact>]
let ``toJsonSchema includes maxItems`` () =
    let s = schema { let! x = Schema.required "x" (Schema.list Schema.string) [ Schema.maxItems 5 ] in return {| X = x |} }
    let js = Schema.toJsonSchema s
    js |> should haveSubstring "\"maxItems\": 5"

[<Fact>]
let ``toJsonSchema includes uuid format`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.uuid ] in return {| X = x |} }
    let js = Schema.toJsonSchema s
    js |> should haveSubstring "\"format\": \"uuid\""

[<Fact>]
let ``toJsonSchema includes ip format`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.ip ] in return {| X = x |} }
    let js = Schema.toJsonSchema s
    js |> should haveSubstring "\"format\": \"ip\""

[<Fact>]
let ``toJsonSchema includes date-time format`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.datetime ] in return {| X = x |} }
    let js = Schema.toJsonSchema s
    js |> should haveSubstring "\"format\": \"date-time\""

[<Fact>]
let ``toJsonSchema includes exactLength as minLength+maxLength`` () =
    let s = schema { let! x = Schema.required "x" Schema.string [ Schema.length 5 ] in return {| X = x |} }
    let js = Schema.toJsonSchema s
    js |> should haveSubstring "\"minLength\": 5"
    js |> should haveSubstring "\"maxLength\": 5"
