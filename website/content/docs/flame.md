---
title: "Flame"
description: "Schema validation for F# — parse, validate, and transform JSON in one zero-allocation pass."
group: "Flame"
order: 1
---

# Flame

Schema validation for F#. Parse, validate, and transform JSON in a single pass with zero-allocation performance.

## Install

```bash
dotnet add package Flame --prerelease
```

## Quick Start

Define an F# record type. Flame generates a schema automatically — option fields become optional, nested records and typed lists are handled recursively:

```fsharp
open Flame

type Address = { Street: string; City: string; Zip: string }
type Tag = { Key: string; Value: string }

type CreateUser = {
    Name: string                // required, rejects empty strings
    Email: string               // required, rejects empty strings
    Age: int                    // required
    Address: Address            // required nested object
    Tags: Tag list              // required list of nested objects
    Bio: string option          // optional, defaults to None
}

let userSchema = Schema.fromType<CreateUser>()
```

Parse JSON in one call:

```fsharp
let json = """
{
  "Name": "Alice",
  "Email": "alice@example.com",
  "Age": 30,
  "Address": { "Street": "123 Main St", "City": "Springfield", "Zip": "62701" },
  "Tags": [{ "Key": "role", "Value": "admin" }],
  "Bio": "Hello world"
}"""

match Schema.parseString userSchema json with
| Ok user -> printfn $"Hello, {user.Name} from {user.Address.City}"
| Error errors -> errors |> List.iter (printfn "  %s")
```

That's it. No boilerplate, no manual field mapping, no separate validation step. Flame handles required/optional detection, nested records, typed lists, and error reporting with dotted paths (`Address.Zip is required`).

### A more complete example

A product catalog API with nested types, lists, and optional fields:

```fsharp
open Flame

// Domain types
type Dimension = { Width: float; Height: float; Depth: float; Unit: string }
type Image = { Url: string; Alt: string option }
type Variant = { Sku: string; Color: string; Size: string; Price: float; Stock: int }

type CreateProduct = {
    Name: string                    // required, rejects empty
    Description: string option      // optional
    Category: string                // required
    Dimensions: Dimension option    // optional nested object
    Images: Image list              // required list of nested objects
    Variants: Variant list          // required list of nested objects
    Draft: bool option              // optional, defaults to None
}

// Schema is generated once and cached
let productSchema = Schema.fromType<CreateProduct>()

// Parse a request body
let json = """
{
  "Name": "Standing Desk",
  "Category": "furniture",
  "Dimensions": { "Width": 120.0, "Height": 75.0, "Depth": 60.0, "Unit": "cm" },
  "Images": [
    { "Url": "https://cdn.example.com/desk-1.jpg", "Alt": "Front view" },
    { "Url": "https://cdn.example.com/desk-2.jpg" }
  ],
  "Variants": [
    { "Sku": "DESK-BLK-S", "Color": "black", "Size": "small", "Price": 399.99, "Stock": 12 },
    { "Sku": "DESK-WHT-L", "Color": "white", "Size": "large", "Price": 499.99, "Stock": 5 }
  ],
  "Draft": true
}"""

match Schema.parseString productSchema json with
| Ok product ->
    printfn $"{product.Name} — {product.Variants.Length} variants, {product.Images.Length} images"
    for v in product.Variants do
        printfn $"  {v.Sku}: ${v.Price} ({v.Stock} in stock)"
| Error errors ->
    printfn "Validation errors:"
    for e in errors do printfn $"  {e}"
```

If you need validation rules on top of the type structure, use the `schema { }` CE instead — see [Adding Validation Rules](/docs/flame-rules).

### Type mapping

| F# Type | Behavior |
|---------|----------|
| `string` | Required. Rejects empty strings. |
| `int`, `float`, `bool` | Required. |
| `DateTime`, `DateTimeOffset` | Required. Parses ISO 8601. |
| `string option`, `int option`, etc. | Optional. Defaults to `None`. |
| `string list`, `int list`, etc. | Required typed list. |
| `Record list` | Required. Each item parsed recursively. |
| Nested record | Required nested object, parsed recursively. |
| `Record option` | Optional nested object. |

`fromType` works with both named records and anonymous records:

```fsharp
let schema = Schema.fromType<{| Name: string; Score: float option |}>()
```

Results are cached per type — reflection only runs on the first call.
