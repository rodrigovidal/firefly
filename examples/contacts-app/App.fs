module ContactsApp.App

open System
open System.Collections.Generic
open Flame
open Fire

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

type Contact =
    { Id: int
      Name: string
      Email: string
      Phone: string
      CreatedAt: DateTime }

// Input type for JSON API — Phone is optional (omit or null → None)
type ContactInput = { Name: string; Email: string; Phone: string option }

// fromType: auto-generates schema from record type (Phone becomes optional)
let contactApiSchema = Schema.fromType<ContactInput>()

// Manual schema: HTML forms need validation rules (email, trim, maxLength, etc.)
let contactSchema = schema {
    let! name = Schema.required "name" Schema.string [ Schema.nonempty; Schema.maxLength 100; Schema.trim ]
    let! email = Schema.required "email" Schema.string [ Schema.email; Schema.trim; Schema.lowercase ]
    let! phone = Schema.optional "phone" Schema.string "" [ Schema.maxLength 20; Schema.trim ]
    return {| Name = name; Email = email; Phone = phone |}
}

// ---------------------------------------------------------------------------
// Layout
// ---------------------------------------------------------------------------

module Layout =
    let main (title: string) (content: string) =
        $"""<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>{System.Net.WebUtility.HtmlEncode title} — Contacts</title>
  <style>
    *, *::before, *::after {{ box-sizing: border-box; }}
    body {{ font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
           max-width: 720px; margin: 0 auto; padding: 2rem 1rem; color: #1a1a2e; background: #f8f9fa; }}
    h1 {{ font-size: 1.5rem; margin-bottom: 0.25rem; }}
    a {{ color: #2563eb; text-decoration: none; }}
    a:hover {{ text-decoration: underline; }}
    nav {{ display: flex; gap: 1rem; padding: 0.75rem 0; border-bottom: 1px solid #e5e7eb; margin-bottom: 1.5rem; }}
    .card {{ background: #fff; border: 1px solid #e5e7eb; border-radius: 8px; padding: 1rem; margin-bottom: 0.75rem; }}
    .card h3 {{ margin: 0 0 0.25rem; font-size: 1rem; }}
    .card p {{ margin: 0.125rem 0; color: #4b5563; font-size: 0.875rem; }}
    .badge {{ display: inline-block; background: #dbeafe; color: #1d4ed8; padding: 0.125rem 0.5rem;
              border-radius: 4px; font-size: 0.75rem; }}
    form {{ display: flex; flex-direction: column; gap: 0.75rem; max-width: 400px; }}
    label {{ font-weight: 600; font-size: 0.875rem; }}
    input {{ padding: 0.5rem; border: 1px solid #d1d5db; border-radius: 6px; font-size: 0.875rem; }}
    button {{ padding: 0.5rem 1rem; background: #2563eb; color: #fff; border: none;
             border-radius: 6px; cursor: pointer; font-size: 0.875rem; }}
    button:hover {{ background: #1d4ed8; }}
    .error {{ color: #dc2626; font-size: 0.8rem; margin-top: 0.25rem; }}
    .empty {{ color: #6b7280; font-style: italic; }}
    .actions {{ display: flex; gap: 0.5rem; margin-top: 0.5rem; }}
    .btn-danger {{ background: #dc2626; }}
    .btn-danger:hover {{ background: #b91c1c; }}
  </style>
</head>
<body>
  <nav>
    <a href="/"><strong>Contacts</strong></a>
    <a href="/contacts/new">New Contact</a>
  </nav>
  {content}
</body>
</html>"""

// ---------------------------------------------------------------------------
// Components
// ---------------------------------------------------------------------------

module Components =
    let contactCard (contact: Contact) =
        Html.div ([ Class "card" ], [
            Html.h3 [
                Html.a ([ Href $"/contacts/{contact.Id}" ], [ Text contact.Name ])
            ]
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

// ---------------------------------------------------------------------------
// Views
// ---------------------------------------------------------------------------

module Views =
    let index (contacts: Contact list) =
        let content =
            if contacts.IsEmpty then
                Html.p ([ Class "empty" ], [ Text "No contacts yet. Add one!" ])
            else
                Fragment [
                    Html.p [
                        Html.span ([ Class "badge" ], [ Text $"{contacts.Length} contact(s)" ])
                    ]
                    Fragment [
                        for c in contacts do
                            Components.contactCard c
                    ]
                ]
        View.page "All Contacts" (Fragment [
            Html.h1 [ Text "Contacts" ]
            content
        ])
        |> View.withLayout Layout.main
        |> View.render

    let show (contact: Contact) =
        View.page contact.Name (
            Fragment [
                Html.h1 [ Text contact.Name ]
                Html.div ([ Class "card" ], [
                    Html.p [ Html.strong [ Text "Email: " ]; Text contact.Email ]
                    if contact.Phone <> "" then
                        Html.p [ Html.strong [ Text "Phone: " ]; Text contact.Phone ]
                    Html.p [
                        Html.small [
                            let ts = contact.CreatedAt.ToString("yyyy-MM-dd HH:mm")
                            Text $"Added {ts}"
                        ]
                    ]
                ])
                Html.div ([ Class "actions" ], [
                    Html.a ([ Href $"/contacts/{contact.Id}/edit" ], [
                        Html.button [ Text "Edit" ]
                    ])
                    Html.form ([ Custom("method", "POST"); Custom("action", $"/contacts/{contact.Id}/delete") ], [
                        Html.button ([ Class "btn-danger" ], [ Text "Delete" ])
                    ])
                ])
            ]
        )
        |> View.withLayout Layout.main
        |> View.render

    let form (title: string) (action: string) (values: Map<string, string>) (errors: Map<string, string>) =
        View.page title (
            Fragment [
                Html.h1 [ Text title ]
                Html.form ([ Custom("method", "POST"); Custom("action", action) ], [
                    Components.formField "Name" "name" "text"
                        (values |> Map.tryFind "name" |> Option.defaultValue "")
                        (errors |> Map.tryFind "name")
                    Components.formField "Email" "email" "email"
                        (values |> Map.tryFind "email" |> Option.defaultValue "")
                        (errors |> Map.tryFind "email")
                    Components.formField "Phone" "phone" "tel"
                        (values |> Map.tryFind "phone" |> Option.defaultValue "")
                        (errors |> Map.tryFind "phone")
                    Html.button [ Text "Save" ]
                ])
            ]
        )
        |> View.withLayout Layout.main
        |> View.render

    let notFound =
        View.page "Not Found" (
            Fragment [
                Html.h1 [ Text "404 — Not Found" ]
                Html.p [ Html.a ([ Href "/" ], [ Text "Back to contacts" ]) ]
            ]
        )
        |> View.withLayout Layout.main
        |> View.render

// ---------------------------------------------------------------------------
// App factory
// ---------------------------------------------------------------------------

let create () =
    let contacts = ResizeArray<Contact>()
    let mutable nextId = 1

    // Seed data
    let now = DateTime.UtcNow
    contacts.AddRange([
        { Id = 1; Name = "Alice Johnson"; Email = "alice@example.com"; Phone = "+1 555-0101"; CreatedAt = now.AddDays(-3) }
        { Id = 2; Name = "Bob Smith"; Email = "bob@example.com"; Phone = ""; CreatedAt = now.AddDays(-1) }
        { Id = 3; Name = "Carol Williams"; Email = "carol@example.com"; Phone = "+1 555-0303"; CreatedAt = now }
    ])
    nextId <- 4

    // Handlers
    let listContacts (_req: Request) = task {
        return Views.index (contacts |> Seq.toList)
    }

    let showContact (id: int) (_req: Request) = task {
        match contacts |> Seq.tryFind (fun c -> c.Id = id) with
        | Some c -> return Views.show c
        | None -> return Views.notFound
    }

    let newContact (_req: Request) = task {
        return Views.form "New Contact" "/contacts" Map.empty Map.empty
    }

    let parseErrorMap (errors: string list) =
        errors |> List.choose (fun e ->
            match e.IndexOf(':') with
            | -1 -> None
            | i -> Some(e.Substring(0, i).Trim().ToLowerInvariant(), e.Substring(i + 1).Trim()))
        |> Map.ofList

    let createContact (req: Request) = task {
        match! Schema.parseRequest contactSchema req with
        | Ok input ->
            let id = nextId
            nextId <- nextId + 1
            contacts.Add(
                { Id = id; Name = input.Name; Email = input.Email
                  Phone = input.Phone; CreatedAt = DateTime.UtcNow })
            return Response.ok |> Response.redirect $"/contacts/{id}" 303
        | Error errors ->
            let! form = req.Form()
            let tryGet key = match form.TryGetValue(key) with true, v -> v | _ -> ""
            let values = Map.ofList [ "name", tryGet "name"; "email", tryGet "email"; "phone", tryGet "phone" ]
            return Views.form "New Contact" "/contacts" values (parseErrorMap errors)
    }

    let editContact (id: int) (_req: Request) = task {
        match contacts |> Seq.tryFind (fun c -> c.Id = id) with
        | Some c ->
            let values = Map.ofList [ "name", c.Name; "email", c.Email; "phone", c.Phone ]
            return Views.form $"Edit {c.Name}" $"/contacts/{id}/edit" values Map.empty
        | None -> return Views.notFound
    }

    let updateContact (id: int) (req: Request) = task {
        match contacts |> Seq.tryFindIndex (fun c -> c.Id = id) with
        | Some idx ->
            match! Schema.parseRequest contactSchema req with
            | Ok input ->
                contacts.[idx] <- { contacts.[idx] with Name = input.Name; Email = input.Email; Phone = input.Phone }
                return Response.ok |> Response.redirect $"/contacts/{id}" 303
            | Error errors ->
                let! form = req.Form()
                let tryGet key = match form.TryGetValue(key) with true, v -> v | _ -> ""
                let values = Map.ofList [ "name", tryGet "name"; "email", tryGet "email"; "phone", tryGet "phone" ]
                return Views.form "Edit Contact" $"/contacts/{id}/edit" values (parseErrorMap errors)
        | None -> return Views.notFound
    }

    let deleteContact (id: int) (_req: Request) = task {
        match contacts |> Seq.tryFindIndex (fun c -> c.Id = id) with
        | Some idx ->
            contacts.RemoveAt(idx)
            return Response.ok |> Response.redirect "/" 303
        | None -> return Views.notFound
    }

    // Demo: Component.client + QueryCache (Phase 2 view engine)
    let interactivePage (_req: Request) = task {
        let cache = QueryCache()
        let fetch () = task {
            return contacts |> Seq.tryHead |> Option.map (fun c -> {| name = c.Name; email = c.Email |})
        }
        let! featured = Query.prefetch "featured" fetch cache
        let content =
            Html.div [
                Html.h1 [ Text "Interactive Demo" ]
                Html.p [ Text "This page uses Component.client and QueryCache." ]
                match featured with
                | Some f ->
                    Html.div ([ Class "card" ], [
                        Html.p [ Html.strong [ Text "Featured: " ]; Text f.name ]
                        Component.client "ContactActions" {| email = f.email |}
                    ])
                | None ->
                    Html.p ([ Class "empty" ], [ Text "No contacts yet." ])
            ]
        return
            View.page "Interactive" content
            |> View.withQueryCache cache
            |> View.withScript "/static/client.js"
            |> View.withLayout Layout.main
            |> View.render
    }

    // JSON API using fromType schema (no validation rules, just type-safe parsing)
    let apiListContacts (_req: Request) = task {
        return Response.json (contacts |> Seq.toList)
    }

    let apiCreateContact (req: Request) = task {
        match! Schema.parseRequest contactApiSchema req with
        | Ok input ->
            let id = nextId
            nextId <- nextId + 1
            let contact =
                { Id = id; Name = input.Name; Email = input.Email
                  Phone = input.Phone |> Option.defaultValue ""; CreatedAt = DateTime.UtcNow }
            contacts.Add(contact)
            return Response.json contact |> Response.status 201
        | Error errors ->
            return Response.json {| errors = errors |} |> Response.status 400
    }

    let routes =
        Route.start
        |> Route.get "/" listContacts
        |> Route.get "/interactive" interactivePage
        |> Route.get "/contacts/new" newContact
        |> Route.post "/contacts" createContact
        |> Route.get "/contacts/%i" showContact
        |> Route.get "/contacts/%i/edit" editContact
        |> Route.post "/contacts/%i/edit" updateContact
        |> Route.post "/contacts/%i/delete" deleteContact
        // JSON API using fromType schema
        |> Route.get "/api/contacts" apiListContacts
        |> Route.post "/api/contacts" apiCreateContact

    let config =
        App.defaults
        |> App.port 3000

    (routes, config)
