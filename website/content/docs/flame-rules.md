---
title: "Validation Rules"
description: "The schema { } CE: required vs optional, type-safe rules, chaining, and 30+ validators."
group: "Flame"
order: 2
---

# Validation Rules

Beyond type-level checks, Flame's `schema { }` computation expression layers on length limits, format validators, and transforms.

## Adding Validation Rules

When you need more than type-level validation — length limits, format checks, transforms — use the `schema { }` computation expression:

```fsharp
let createUserSchema = schema {
    let! name  = Schema.required "name"  Schema.string [ Schema.nonempty; Schema.maxLength 100; Schema.trim ]
    let! email = Schema.required "email" Schema.string [ Schema.email; Schema.trim; Schema.lowercase ]
    let! age   = Schema.optional "age"   Schema.int 0  [ Schema.min 0; Schema.max 150 ]
    return {| Name = name; Email = email; Age = age |}
}
```

Each `let!` binds a field with a name, a type parser, and a list of rules. The `return` assembles the typed result.

### Required vs optional

```fsharp
// Required: must be present and non-null
let! title = Schema.required "title" Schema.string [ Schema.nonempty; Schema.maxLength 200 ]

// Optional: can be omitted or null, falls back to default. Rules apply when present.
let! priority = Schema.optional "priority" Schema.string "medium" [ Schema.oneOf ["low"; "medium"; "high"] ]
```

### Type-safe rules

Rules are generic — `Rule<string>`, `Rule<float>`, `Rule<'T list>`. The compiler rejects mismatched rules:

```fsharp
// Compiles — email is Rule<string>, field is string:
Schema.required "email" Schema.string [ Schema.email; Schema.trim ]

// Compile error — email is Rule<string>, field is int:
// Schema.required "age" Schema.int [ Schema.email ]
```

### Rule chaining

Rules are applied left to right. Each rule receives the output of the previous rule. Transforms should come before validators:

```fsharp
// 1. trim whitespace  2. check not empty  3. lowercase
let! email = Schema.required "email" Schema.string [ Schema.trim; Schema.nonempty; Schema.lowercase ]
```

If a rule fails, the error is collected but subsequent rules still run. Flame never short-circuits — all errors are reported.

### String rules

```fsharp
Schema.minLength 3              // at least 3 characters
Schema.maxLength 200            // at most 200 characters
Schema.length 5                 // exactly 5 characters
Schema.nonempty                 // must not be empty
Schema.pattern @"^\d{5}$"      // must match regex
Schema.email                    // must contain @ and .
Schema.url                      // must start with http:// or https://
Schema.uuid                     // must be a valid UUID/GUID
Schema.ip                       // must be a valid IP address (v4 or v6)
Schema.ipv4                     // must be a valid IPv4 address
Schema.ipv6                     // must be a valid IPv6 address
Schema.datetime                 // must be a valid ISO 8601 date/time string
Schema.startsWith "https"       // must start with prefix
Schema.endsWith ".com"          // must end with suffix
Schema.includes "@"             // must contain substring
Schema.oneOf ["a"; "b"; "c"]   // must be one of the listed values
```

### String transforms

Transforms modify the value. They always succeed and pass the transformed value to the next rule.

```fsharp
Schema.trim                     // remove leading/trailing whitespace
Schema.lowercase                // convert to lowercase (invariant)
Schema.uppercase                // convert to uppercase (invariant)
```

### Number rules

```fsharp
Schema.min 0.0                  // >= 0 (inclusive)
Schema.max 100.0                // <= 100 (inclusive)
Schema.gt 0.0                   // > 0 (exclusive)
Schema.lt 100.0                 // < 100 (exclusive)
Schema.positive                 // > 0
Schema.negative                 // < 0
Schema.nonnegative              // >= 0
Schema.nonpositive              // <= 0
Schema.integer                     // must be a whole number (e.g. 5.0 ok, 5.5 fails)
Schema.multipleOf 0.5           // must be divisible by 0.5
```

### Array rules

Applied to the parsed list after all items have been individually validated:

```fsharp
Schema.minItems 1               // at least 1 item
Schema.maxItems 10              // at most 10 items
Schema.nonEmpty                 // must have at least 1 item
```
