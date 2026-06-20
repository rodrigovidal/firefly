---
title: "Contacts App"
description: "A server-rendered CRUD contacts app showing Firefly's HTML view engine, schema-validated forms, and a JSON API."
group: "Guides"
order: 5
---

# Contacts App

The contacts app is a small CRUD application that stores contacts in an in-memory list and renders every page server-side with Firefly's HTML view engine. It exercises the full request lifecycle: typed routes with integer captures, schema-validated HTML form submissions with redirects and error re-rendering, and a parallel JSON API. The same `contactSchema` powers both the browser forms and (via a `fromType` variant) the API.

## What you'll learn

- Building pages with the `Html.*` element DSL, `Fragment`, `Text`, and `Empty`
- Composing reusable components (`contactCard`, `formField`) and a string layout via `View.withLayout`
- Rendering views with `View.page` / `View.render`
- Defining schemas two ways: `Schema.fromType<'T>()` and the `schema { ... }` builder with validators (`Schema.email`, `Schema.trim`, `Schema.maxLength`, …)
- Parsing requests with `Schema.parseRequest`, reading raw form data with `req.Form()`
- Typed routing with `Route.start`, `Route.get`/`Route.post`, and `%i` path captures
- Redirects (`Response.redirect`), JSON responses (`Response.json`), and status codes (`Response.status`)
- App startup with `App.defaults`, `App.port`, `App.middleware Log.toConsole`, and `App.run`

## Types and schemas

A `Contact` is the stored record; `ContactInput` is the JSON input shape where `Phone` is optional. Two schemas are defined — one auto-generated from the input type, one hand-built with validation rules for the HTML forms.

```fsharp
open System
open Flame
open Firefly

type Contact =
    { Id: int; Name: string; Email: string; Phone: string; CreatedAt: DateTime }

type ContactInput = { Name: string; Email: string; Phone: string option }

// Auto-generates a schema from the record type (Phone becomes optional)
let contactApiSchema = Schema.fromType<ContactInput>()

// Manual schema: HTML forms need validation rules
let contactSchema = schema {
    let! name  = Schema.required "name"  Schema.string [ Schema.nonempty; Schema.maxLength 100; Schema.trim ]
    let! email = Schema.required "email" Schema.string [ Schema.email; Schema.trim; Schema.lowercase ]
    let! phone = Schema.optional "phone" Schema.string "" [ Schema.maxLength 20; Schema.trim ]
    return {| Name = name; Email = email; Phone = phone |}
}
```

## Views and components

Pages are built from the `Html.*` DSL. `Fragment` groups siblings, `Text` emits escaped text, and `Empty` renders nothing — handy for conditional fields. Components are just functions returning nodes.

```fsharp
module Components =
    let contactCard (contact: Contact) =
        Html.div ([ Class "card" ], [
            Html.h3 [ Html.a ([ Href $"/contacts/{contact.Id}" ], [ Text contact.Name ]) ]
            Html.p [ Text contact.Email ]
            if contact.Phone <> "" then
                Html.p [ Html.small [ Text contact.Phone ] ]
        ])

    let formField (label': string) (name': string) (type': string) (value': string) (error: string option) =
        Fragment [
            Html.label [ Text label' ]
            Html.input [ Type type'; Name name'; Value value'; Placeholder label' ]
            match error with
            | Some msg -> Html.p ([ Class "error" ], [ Text msg ])
            | None -> Empty
        ]
```

A view is created with `View.page`, wrapped in a string-based layout with `View.withLayout`, and turned into a `Response` with `View.render`. The form view reuses `formField` and pre-fills values and per-field errors from `Map`s.

```fsharp
module Views =
    let form (title: string) (action: string) (values: Map<string,string>) (errors: Map<string,string>) =
        View.page title (
            Fragment [
                Html.h1 [ Text title ]
                Html.form ([ Custom("method", "POST"); Custom("action", action) ], [
                    Components.formField "Name"  "name"  "text"
                        (values |> Map.tryFind "name"  |> Option.defaultValue "") (errors |> Map.tryFind "name")
                    Components.formField "Email" "email" "email"
                        (values |> Map.tryFind "email" |> Option.defaultValue "") (errors |> Map.tryFind "email")
                    Components.formField "Phone" "phone" "tel"
                        (values |> Map.tryFind "phone" |> Option.defaultValue "") (errors |> Map.tryFind "phone")
                    Html.button [ Text "Save" ]
                ])
            ]
        )
        |> View.withLayout Layout.main
        |> View.render
```

The layout itself is a plain interpolated HTML string (`Layout.main title content`) that wraps the rendered body with `<head>`, CSS, and a nav bar.

## Handlers

Handlers take a `Request` and return a `task`. The create handler parses the form against `contactSchema`: on success it redirects (303) to the new contact; on failure it re-reads the raw form with `req.Form()` and re-renders the form with the user's values and the validation errors.

```fsharp
let createContact (req: Request) = task {
    match! Schema.parseRequest contactSchema req with
    | Ok input ->
        let id = nextId
        nextId <- nextId + 1
        contacts.Add({ Id = id; Name = input.Name; Email = input.Email
                       Phone = input.Phone; CreatedAt = DateTime.UtcNow })
        return Response.ok |> Response.redirect $"/contacts/{id}" 303
    | Error errors ->
        let! form = req.Form()
        let tryGet key = match form.TryGetValue(key) with true, v -> v | _ -> ""
        let values = Map.ofList [ "name", tryGet "name"; "email", tryGet "email"; "phone", tryGet "phone" ]
        return Views.form "New Contact" "/contacts" values (parseErrorMap errors)
}
```

The JSON API reuses the same flow with `contactApiSchema`, returning `Response.json` with explicit status codes.

```fsharp
let apiCreateContact (req: Request) = task {
    match! Schema.parseRequest contactApiSchema req with
    | Ok input ->
        let id = nextId
        nextId <- nextId + 1
        let contact = { Id = id; Name = input.Name; Email = input.Email
                        Phone = input.Phone |> Option.defaultValue ""; CreatedAt = DateTime.UtcNow }
        contacts.Add(contact)
        return Response.json contact |> Response.status 201
    | Error errors ->
        return Response.json {| errors = errors |} |> Response.status 400
}
```

## Routes

Routes are built by piping onto `Route.start`. `%i` captures an integer path segment and passes it to the handler before the `Request`.

```fsharp
let routes =
    Route.start
    |> Route.get  "/"                    listContacts
    |> Route.get  "/contacts/new"        newContact
    |> Route.post "/contacts"            createContact
    |> Route.get  "/contacts/%i"         showContact
    |> Route.get  "/contacts/%i/edit"    editContact
    |> Route.post "/contacts/%i/edit"    updateContact
    |> Route.post "/contacts/%i/delete"  deleteContact
    |> Route.get  "/api/contacts"        apiListContacts
    |> Route.post "/api/contacts"        apiCreateContact
```

## App startup

`App.create` seeds three contacts and returns the routes plus a config. `Program.fs` adds console request logging and runs the app.

```fsharp
open System.Threading
open Firefly
open ContactsApp

let (routes, config) = App.create ()           // config = App.defaults |> App.port 3000
let config' = config |> App.middleware Log.toConsole

App.run routes config' CancellationToken.None
|> fun t -> t.GetAwaiter().GetResult()
```

## Running it

```bash
dotnet run --project examples/contacts-app
```

Then open http://localhost:3000 in a browser to list contacts, click **New Contact** to add one, and open a contact to edit or delete it.

The JSON API is also available:

```bash
# List all contacts
curl http://localhost:3000/api/contacts

# Create a contact (Phone is optional)
curl -X POST http://localhost:3000/api/contacts \
  -H "Content-Type: application/json" \
  -d '{"name":"Dave Lee","email":"dave@example.com"}'
```

## Source

See the full example under [`examples/contacts-app/`](https://github.com/firefly/firefly/tree/main/examples/contacts-app) — `App.fs` (types, views, handlers, routes) and `Program.fs` (startup).
