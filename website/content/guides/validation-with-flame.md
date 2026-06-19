---
title: "Validation with Flame"
description: "Use Flame schemas to validate and parse request bodies inside a Firefly app."
group: "Guides"
order: 7
---

# Validation with Flame

[Flame](/docs/flame) is the schema-validation library in the Firefly stack. It parses, validates, and transforms JSON in a single zero-allocation pass — and Firefly wires it straight into request handling. You define a schema once, then validate request bodies, query strings, and forms with one call.

This guide shows Flame on its own, then Flame inside a Firefly handler.

## What you'll learn

- Generating a schema from a record type with `Schema.fromType`
- Adding validation rules with the `schema { }` computation expression
- Validating a Firefly request body with `Schema.parseRequest`
- Returning structured `422` errors when validation fails

## Flame on its own

The repo ships a runnable demo. It defines types, builds schemas, parses JSON and query strings, runs cross-field checks, transforms into domain value objects, and generates JSON Schema:

```bash
dotnet run --project examples/quickstart   # in the flame repo
```

A schema can be generated directly from a record type — option fields become optional, nested records and typed lists recurse:

```fsharp
open Flame

type Address = { Street: string; City: string; Zip: string }

type CreateUser =
    { Name: string
      Email: string
      Age: int
      Address: Address
      Bio: string option }

let userSchema = Schema.fromType<CreateUser>()

match Schema.parseString userSchema requestJson with
| Ok user -> printfn $"Hello, {user.Name} from {user.Address.City}"
| Error errors -> errors |> List.iter (printfn "  %s")
```

Flame never short-circuits — every validation error is collected and reported at once, with dotted paths like `address.zip is required`.

## Adding rules

When you need more than type-level checks — length limits, formats, transforms — use the `schema { }` computation expression. Each `let!` binds a field name, a type parser, and a list of rules; transforms (like `trim`) run before validators:

```fsharp
open Flame

let signupSchema =
    schema {
        let! name  = Schema.required "name"  Schema.string [ Schema.nonempty; Schema.maxLength 100; Schema.trim ]
        let! email = Schema.required "email" Schema.string [ Schema.email; Schema.trim; Schema.lowercase ]
        let! age   = Schema.optional "age"   Schema.int 0  [ Schema.min 0; Schema.max 150 ]
        return {| Name = name; Email = email; Age = age |}
    }
```

See [Validation Rules](/docs/flame-rules) for the full set of 30+ validators.

## Validating a Firefly request

Inside a Firefly app you `open Flame` for the schema and `open Firefly` for routing and responses. Build the schema once at module scope, then validate the request body in the handler with `Schema.parseRequest`:

```fsharp
open Flame
open Firefly

type CreatePostInput =
    { Title: string
      Body: string
      Tags: string list }

let createPostSchema = Schema.fromType<CreatePostInput>()

let createPost (req: Request) =
    task {
        match! Schema.parseRequest createPostSchema req with
        | Ok input ->
            // input is a fully-typed, validated CreatePostInput
            let post = {| id = 1; title = input.Title; tags = input.Tags |}
            return Response.json post |> Response.status 201
        | Error errors ->
            // every validation error, collected
            return Response.json {| errors = errors |} |> Response.status 422
    }

let routes =
    Route.start
    |> Route.post "/posts" createPost
```

`Schema.parseRequest` reads the request body straight from Kestrel's pipe and runs the Flame schema over it — no intermediate `JsonDocument`. On success you get a typed value; on failure you get a `string list` of errors to shape into a response. This is exactly the pattern used in the [blog-api](/guides/blog-api) and [todo-api](/guides/todo-api) examples.

## Trying it

```bash
# valid
curl -s -X POST localhost:5000/posts \
  -H 'content-type: application/json' \
  -d '{"Title":"Hello","Body":"First post","Tags":["intro"]}'

# invalid — missing/empty fields come back as 422 with all errors
curl -s -X POST localhost:5000/posts \
  -H 'content-type: application/json' \
  -d '{"Title":"","Tags":[]}'
```

## Where to go next

- [Flame](/docs/flame) — overview, type mapping, and quick start
- [Validation Rules](/docs/flame-rules) — the full validator catalog
- [Parsing & Errors](/docs/flame-parsing) — parse sources and error formats
- [Advanced](/docs/flame-advanced) — JSON Schema, validating existing values, validate-and-transform

## Source

- Standalone demo: `examples/quickstart/` in the [flame repo](https://github.com/rodrigovidal/flame)
- In-app usage: `examples/blog-api/` and `examples/todo-api/` in the Firefly repo
