---
title: "Parsing & Errors"
description: "Type parsers, nested schemas, cross-field checks, parse sources, and error reporting."
group: "Flame"
order: 3
---

# Parsing & Errors

How Flame turns JSON, byte buffers, streams, query strings, and forms into typed values — and how it reports every error at once.

## Type Parsers

Flame provides parsers for common types. Each parser handles coercion from strings automatically — `"42"` parses as `int`, `"true"` as `bool`, etc. Coercion is consistent across all parsing paths.

| Parser | F# Type | Accepts |
|--------|---------|---------|
| `Schema.string` | `string` | JSON strings |
| `Schema.int` | `int` | JSON numbers, numeric strings (`"42"`) |
| `Schema.float` | `float` | JSON numbers, numeric strings (`"3.14"`) |
| `Schema.bool` | `bool` | JSON `true`/`false`, strings `"true"`/`"false"` |
| `Schema.dateTime` | `DateTime` | ISO 8601 strings |
| `Schema.dateTimeOffset` | `DateTimeOffset` | ISO 8601 with offset |
| `Schema.list parser` | `'T list` | JSON arrays |
| `Schema.nullable parser` | `'T option` | JSON null becomes `None` |
| `Schema.nest schema` | nested object | JSON objects |

### Lists

```fsharp
let! tags   = Schema.required "tags"   (Schema.list Schema.string) [ Schema.nonEmpty; Schema.maxItems 10 ]
let! scores = Schema.required "scores" (Schema.list Schema.int) []
```

### Nullable fields

```fsharp
let! note = Schema.required "note" (Schema.nullable Schema.string) []
// note : string option — JSON null becomes None, a string becomes Some "..."
```

### Lists of nested objects

```fsharp
let itemSchema = schema {
    let! name = Schema.required "name" Schema.string [ Schema.nonempty ]
    let! qty  = Schema.required "qty"  Schema.int    [ Schema.positive ]
    return {| Name = name; Qty = qty |}
}

let orderSchema = schema {
    let! items = Schema.required "items" (Schema.list (Schema.nest itemSchema)) [ Schema.nonEmpty ]
    return {| Items = items |}
}
```

## Nested Schemas

Compose schemas with `Schema.nest` to validate nested JSON objects:

```fsharp
let addressSchema = schema {
    let! street = Schema.required "street" Schema.string [ Schema.nonempty ]
    let! city   = Schema.required "city"   Schema.string [ Schema.nonempty ]
    let! zip    = Schema.required "zip"    Schema.string [ Schema.pattern @"^\d{5}$" ]
    return {| Street = street; City = city; Zip = zip |}
}

let userSchema = schema {
    let! name    = Schema.required "name"    Schema.string [ Schema.nonempty ]
    let! address = Schema.required "address" (Schema.nest addressSchema) []
    return {| Name = name; Address = address |}
}
```

Errors from nested schemas use dotted paths: `address.zip: must match pattern ^\d{5}$`

## Cross-field Validation

Use `Schema.check` with `do!` to validate relationships between fields:

```fsharp
let dateRangeSchema = schema {
    let! startDate = Schema.required "start" Schema.dateTime []
    let! endDate   = Schema.required "end"   Schema.dateTime []
    do! Schema.check (fun () ->
        if endDate > startDate then Ok ()
        else Error "end: must be after start"
    )
    return {| Start = startDate; End = endDate |}
}
```

Multiple checks can be chained:

```fsharp
let registrationSchema = schema {
    let! password = Schema.required "password" Schema.string [ Schema.minLength 8 ]
    let! confirm  = Schema.required "confirm"  Schema.string []
    do! Schema.check (fun () ->
        if password = confirm then Ok () else Error "confirm: must match password"
    )
    return {| Password = password |}
}
```

Note: schemas with `Schema.check` use the element parsing path instead of the zero-alloc buffer path, since cross-field checks use closures that capture field values.

## Parsing

All parse functions return `Result<'T, string list>`.

| Function | Input | Notes |
|----------|-------|-------|
| `Schema.parseString` | `string` | Default. Uses `Utf8JsonReader` internally. |
| `Schema.parseBuffer` | `ReadOnlySequence<byte>` | Zero-alloc. Best for Kestrel request bodies. |
| `Schema.parseJson` | `JsonElement` | When you already have a `JsonDocument`. |
| `Schema.parseStream` | `Stream` | Async. For request body streams. |
| `Schema.parsePipe` | `PipeReader` | Async. For Kestrel's PipeReader. |
| `Schema.parseLookup` | `string -> string option` | Zero-alloc. For query strings, route params, form data. |
| `Schema.parseMap` | `IReadOnlyDictionary<string, string>` | Delegates to `parseLookup`. |

### Two parsing paths

Flame has two internal paths. The **buffer path** (`parseString`, `parseBuffer`) parses directly from raw bytes using `Utf8JsonReader` with `ArrayPool<obj>` — no `JsonDocument` allocated. The **element path** (`parseJson`, `parseStream`) works with `JsonElement`. Schemas with `Schema.check` fall back to the element path.

### Parsing from query strings and forms

`parseLookup` skips JSON entirely — values are coerced from strings to the target type:

```fsharp
let paginationSchema = schema {
    let! page  = Schema.optional "page"  Schema.int 1  [ Schema.positive ]
    let! limit = Schema.optional "limit" Schema.int 20 [ Schema.min 1; Schema.max 100 ]
    return {| Page = page; Limit = limit |}
}

let result = Schema.parseLookup paginationSchema (fun name ->
    match name with
    | "page" -> Some "2"
    | "limit" -> Some "50"
    | _ -> None
)
```

## Error Handling

Flame collects all validation errors — it never short-circuits. This lets users fix all problems at once.

```fsharp
match Schema.parseString mySchema """{"name":"","email":"bad","age":-1}""" with
| Error errors ->
    // [
    //   "name: must not be empty"
    //   "email: invalid email format"
    //   "age: must be at least 0"
    // ]
```

| Situation | Format | Example |
|-----------|--------|---------|
| Field validation | `"field: message"` | `"email: invalid email format"` |
| Missing required field | `"field is required"` | `"name is required"` |
| Nested field | `"parent.field: message"` | `"address.zip: must match pattern ^\d{5}$"` |
| List item | `"field.[index]: message"` | `"items.[2]: expected integer"` |
