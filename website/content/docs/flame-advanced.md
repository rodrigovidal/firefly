---
title: "Advanced"
description: "JSON Schema generation, validating existing values, and validate-and-transform (DDD)."
group: "Flame"
order: 4
---

# Advanced

Generate JSON Schema, validate values you already hold, and validate-and-transform straight into your domain model.

## JSON Schema Generation

Generate standard JSON Schema from any Flame schema:

```fsharp
let jsonSchema = Schema.toJsonSchema createUserSchema
```

```json
{
  "type": "object",
  "properties": {
    "name": { "type": "string", "minLength": 1, "maxLength": 100 },
    "email": { "type": "string", "format": "email" },
    "age": { "type": "integer", "minimum": 0, "maximum": 150 }
  },
  "required": ["name", "email"]
}
```

All validators emit correct JSON Schema keywords: `minLength`, `maxLength`, `pattern`, `minimum`, `maximum`, `exclusiveMinimum`, `exclusiveMaximum`, `multipleOf`, `minItems`, `maxItems`, `format`, `enum`. Nested objects include `properties` and `required`. Arrays include `items`.

## Validating Existing Values

Use `validator { }` to validate an F# value that already exists — no JSON involved:

```fsharp
let userValidator = validator {
    validate "Name" (fun (u: User) -> u.Name) [ Schema.nonempty; Schema.maxLength 100 ]
    validate "Email" (fun u -> u.Email) [ Schema.email ]
    validate "Address.Zip" (fun u -> u.Address.Zip) [ Schema.pattern @"^\d{5}$" ]
}

match Validator.validate userValidator someUser with
| Ok user -> // valid
| Error errors -> // ["Name: must not be empty"; "Address.Zip: must match pattern ..."]
```

Uses the same rules as `schema { }` but works directly on typed values via lambdas. No JSON, no reflection at runtime.

`Validator.fromType` auto-generates a validator from a record type (required strings get `nonempty`, nested records recurse):

```fsharp
let userValidator = Validator.fromType<User>()
```

## Validate and Transform

Use `validated { }` to validate an input and produce a different output type in one step. Transforms (trim, lowercase) are applied to the output values:

```fsharp
type CreateUserInput = { Name: string; Email: string; Age: int }
type ValidUser = { Name: string; Email: string; Age: int }

let createUser = validated {
    let! name  = Validated.field "Name"  (fun (r: CreateUserInput) -> r.Name)  [ Schema.nonempty; Schema.trim ]
    let! email = Validated.field "Email" (fun r -> r.Email) [ Schema.email; Schema.trim; Schema.lowercase ]
    let! age   = Validated.field "Age"   (fun r -> r.Age)   [ Schema.min 0; Schema.max 150 ]
    return { ValidUser.Name = name; Email = email; Age = age }
}

match Validated.run createUser { Name = "  Alice  "; Email = "  ALICE@TEST.COM  "; Age = 30 } with
| Ok user -> // { Name = "Alice"; Email = "alice@test.com"; Age = 30 }
| Error errors -> // all validation errors collected
```

### Domain-Driven Design with value objects

The `validated { }` CE works naturally with single-case discriminated unions for domain modeling. Validation and domain construction happen in one step:

```fsharp
// Value objects
type Name = Name of string
type Email = Email of string
type Age = Age of int

// Domain model
type ValidUser = { Name: Name; Email: Email; Age: Age }

// Input DTO
type CreateUserRequest = { Name: string; Email: string; Age: int }

let createUser = validated {
    let! name  = Validated.field "Name"  (fun (r: CreateUserRequest) -> r.Name)  [ Schema.nonempty; Schema.maxLength 100; Schema.trim ]
    let! email = Validated.field "Email" (fun r -> r.Email) [ Schema.email; Schema.trim; Schema.lowercase ]
    let! age   = Validated.field "Age"   (fun r -> r.Age)   [ Schema.positive; Schema.max 150 ]
    return { Name = Name name; Email = Email email; Age = Age age }
}

// DTO in, validated domain model out:
match Validated.run createUser request with
| Ok user -> // user.Email = Email "alice@test.com"
| Error errors -> // ["Email: invalid email format"; "Age: must be positive"]
```

## Performance

Flame parses and validates in a single pass using `Utf8JsonReader` with `ArrayPool<FieldValue>` (a struct union — no heap boxing for primitives). Rules are generic (`Rule<'T>`) so the Validator and Validated paths have zero boxing. No intermediate `JsonDocument` on the buffer path.

Benchmarks on Apple M4 Pro, .NET 10:

| Scenario | Flame (buffer) | FluentValidation (STJ + validate) | Speedup | Memory |
|----------|---------------:|----------------------------------:|--------:|-------:|
| 3 fields + rules | 239 ns / 408 B | 266 ns / 832 B | 1.1x | 2x less |
| 10 fields + nested | 763 ns / 1,448 B | 1,098 ns / 2,856 B | 1.4x | 2x less |
| Nested objects | 370 ns / 824 B | 509 ns / 2,136 B | 1.4x | 2.6x less |
| uuid + ip + array validators | 516 ns / 944 B | 566 ns / 1,800 B | 1.1x | 1.9x less |

```bash
dotnet run --project benchmarks/Flame.Benchmarks -c Release
```
