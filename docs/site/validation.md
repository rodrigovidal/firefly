# Validation

Fire integrates with [Flame](https://github.com/example/flame), a schema validation library for F#. Flame provides type-safe parsing, validation rules, and JSON Schema generation.

## Defining Schemas

Use the `schema` computation expression to define schemas:

```fsharp
open Flame

type CreateUser = { Name: string; Email: string; Age: int }

let createUserSchema = schema<CreateUser> {
    required "name"  Schema.string [ Rules.minLength 1; Rules.maxLength 100; Rules.trim ]
    required "email" Schema.string [ Rules.email ]
    required "age"   Schema.int   [ Rules.min 0; Rules.max 150 ]
}
```

## Field Types

Flame supports these built-in field parsers:

| Parser | F# Type | JSON Type |
|--------|---------|-----------|
| `Schema.string` | `string` | string |
| `Schema.int` | `int` | number/string |
| `Schema.float` | `float` | number/string |
| `Schema.bool` | `bool` | true/false/string |
| `Schema.dateTime` | `DateTime` | string |
| `Schema.dateTimeOffset` | `DateTimeOffset` | string |
| `Schema.list Schema.string` | `string list` | array |
| `Schema.nullable Schema.string` | `string option` | string/null |

## Required vs Optional Fields

```fsharp
type Profile = { Name: string; Bio: string option }

let profileSchema = schema<Profile> {
    required "name" Schema.string [ Rules.nonempty ]
    optional "bio"  Schema.string []           // defaults to None if missing
}
```

Optional fields with defaults:

```fsharp
type SearchParams = { Query: string; Page: int; Limit: int }

let searchSchema = schema<SearchParams> {
    required "query" Schema.string []
    withDefault "page"  Schema.int [] 1
    withDefault "limit" Schema.int [] 20
}
```

## Validation Rules

Flame includes a comprehensive set of typed rules:

### String Rules

| Rule | Description |
|------|-------------|
| `Rules.minLength n` | Minimum string length |
| `Rules.maxLength n` | Maximum string length |
| `Rules.length n` | Exact string length |
| `Rules.nonempty` | Must not be empty |
| `Rules.pattern "regex"` | Must match regex pattern |
| `Rules.email` | Must be a valid email |
| `Rules.url` | Must start with http:// or https:// |
| `Rules.uuid` | Must be a valid UUID |
| `Rules.ip` | Must be a valid IP address |
| `Rules.ipv4` | Must be a valid IPv4 address |
| `Rules.ipv6` | Must be a valid IPv6 address |
| `Rules.datetime` | Must be a valid date/time string |
| `Rules.oneOf ["a"; "b"]` | Must be one of the listed values |
| `Rules.startsWith "prefix"` | Must start with prefix |
| `Rules.endsWith "suffix"` | Must end with suffix |
| `Rules.includes "sub"` | Must contain substring |

### Transform Rules

| Rule | Description |
|------|-------------|
| `Rules.trim` | Trim whitespace (applied before other rules) |
| `Rules.lowercase` | Convert to lowercase |
| `Rules.uppercase` | Convert to uppercase |

### Number Rules

| Rule | Description |
|------|-------------|
| `Rules.min n` | Minimum value (inclusive) |
| `Rules.max n` | Maximum value (inclusive) |
| `Rules.gt n` | Greater than (exclusive) |
| `Rules.lt n` | Less than (exclusive) |
| `Rules.positive` | Must be > 0 |
| `Rules.negative` | Must be < 0 |
| `Rules.nonnegative` | Must be >= 0 |
| `Rules.nonpositive` | Must be <= 0 |
| `Rules.multipleOf n` | Must be a multiple of n |
| `Rules.integer` | Float must be a whole number |

### Array Rules

| Rule | Description |
|------|-------------|
| `Rules.minItems n` | Minimum array length |
| `Rules.maxItems n` | Maximum array length |
| `Rules.nonEmpty` | Must have at least one item |

## Nested Schemas

Compose schemas for nested objects:

```fsharp
type Address = { Street: string; City: string; Zip: string }
type CreateOrder = { Customer: string; Address: Address }

let addressSchema = schema<Address> {
    required "street" Schema.string [ Rules.nonempty ]
    required "city"   Schema.string [ Rules.nonempty ]
    required "zip"    Schema.string [ Rules.pattern "^\\d{5}$" ]
}

let orderSchema = schema<CreateOrder> {
    required "customer" Schema.string [ Rules.nonempty ]
    required "address"  (Schema.nest addressSchema) []
}
```

Errors from nested schemas use dotted paths: `"address.zip: must match pattern"`.

## Using Schemas in Fire

### Manual Parsing

Parse the request body with `Schema.parse` (auto-detects JSON vs form data):

```fsharp
let createUser (req: Request) = task {
    match! Schema.parse createUserSchema req with
    | Ok user ->
        // user is a typed CreateUser record
        return Response.json user |> Response.status 201
    | Error errors ->
        // errors is string list
        return Response.json {| errors = errors |} |> Response.status 400
}
```

### Validated Handler

Use `Schema.validated` to wrap a handler with automatic parsing and error responses:

```fsharp
let createUser =
    Schema.validated createUserSchema (fun user -> task {
        // `user` is already parsed and validated
        return Response.json user |> Response.status 201
    })

Route.post "/users" createUser
```

On validation failure, responds with 400 and `{ "errors": ["name: must be at least 1 character", ...] }`.

### Parsing Specific Sources

```fsharp
// JSON body only (via PipeReader — zero-alloc buffer path)
Schema.parseRequest schema req

// Form data only
Schema.parseFormRequest schema req

// Route parameters
Schema.parseParams schema req

// Query string
Schema.parseQuery schema req
```

## Simple Validators

For cases where you do not need full schema parsing, use `Validate`:

```fsharp
let validateUser =
    Validate.combine [
        Validate.required "name" (fun u -> u.Name)
        Validate.minLength "name" 2 (fun u -> u.Name)
        Validate.maxLength "name" 100 (fun u -> u.Name)
        Validate.pattern "email" @"^[^@]+@[^@]+\.[^@]+$" (fun u -> u.Email)
    ]

let handler (req: Request) = task {
    let! user = req.Json<CreateUser>()
    match validateUser user with
    | Ok _ -> return Response.json user |> Response.status 201
    | Error errors -> return Response.json {| errors = errors |} |> Response.status 400
}
```
