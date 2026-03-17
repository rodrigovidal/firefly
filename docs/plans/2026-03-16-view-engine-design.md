# Fire View Engine Design

Server-side HTML rendering with React client component hydration and TanStack Query integration. F# DSL renders components to HTML on the server. Client components are standard React/TypeScript, hydrated via a small npm package.

## Architecture

```
Server (F#)                          Client (React/TypeScript)
─────────────                        ────────────────────────
Html.div [                           import { hydrateFireApp } from '@fire/react'
    Html.h1 [ Text "Hello" ]         import { LikeButton } from './components'
    Component.client "LikeButton"
        {| userId = 1 |}            hydrateFireApp({ LikeButton })
]
    │                                    │
    ▼                                    ▼
Render.toHtml                        Finds data-fire-component markers
    │                                Mounts React components
    ▼                                Hydrates TanStack Query cache
<html>
  <body>
    <div id="app">
      <h1>Hello</h1>
      <div data-fire-component="LikeButton"
           data-fire-props='{"userId":1}'></div>
    </div>
    <script>window.__FIRE_QUERY_STATE__=[...]</script>
    <script src="/static/client.js"></script>
  </body>
</html>
```

## 1. Core Types

### Node

```fsharp
type Node =
    | Element of tag: string * attrs: Attr list * children: Node list
    | Text of string                                // HTML-encoded
    | Raw of string                                 // trusted raw HTML
    | ClientComponent of name: string * props: obj  // hydration marker
    | Fragment of Node list
    | Empty
```

### Attr

```fsharp
type Attr =
    | Class of string
    | Id of string
    | Href of string
    | Src of string
    | Type of string
    | Name of string
    | Value of string
    | Placeholder of string
    | Style of string
    | Data of string * string       // data-x="y"
    | Custom of string * string     // any attribute
```

### Html (static class with overloads)

```fsharp
type Html =
    static member div (children: Node list) : Node
    static member div (attrs: Attr list, children: Node list) : Node
    static member h1 (children: Node list) : Node
    static member h1 (attrs: Attr list, children: Node list) : Node
    static member p (children: Node list) : Node
    static member p (attrs: Attr list, children: Node list) : Node
    // ... all standard HTML elements: a, span, section, nav, header, footer,
    //     form, input, button, img, ul, ol, li, table, tr, td, th,
    //     main, article, aside, label, select, option, textarea, etc.
```

Two overloads per element:
- `Html.div [ children ]` — no attributes (common case)
- `Html.div ([ attrs ], [ children ])` — with attributes

### Component

```fsharp
module Component =
    let client (name: string) (props: 'T) : Node =
        ClientComponent (name, props :> obj)
```

### Usage

```fsharp
// No attributes
Html.div [
    Html.h1 [ Text "Hello" ]
    Html.p [ Text "world" ]
]

// With attributes
Html.div ([ Attr.Class "container"; Attr.Id "main" ], [
    Html.h1 ([ Attr.Class "title" ], [ Text "Hello" ])
    Html.p [ Text "world" ]
])

// Client component
Html.div [
    Html.h1 [ Text user.Name ]
    Component.client "LikeButton" {| userId = user.Id |}
]
```

File: `src/Fire/View/Node.fs`

## 2. Rendering

```fsharp
[<RequireQualifiedAccess>]
module Render =
    let rec toHtml (node: Node) : string
```

Behavior:
- `Element` — renders open tag with attributes, recursively renders children, closes tag. Void elements (`br`, `hr`, `img`, `input`, `meta`, `link`) self-close without children.
- `Text` — HTML-encodes via `System.Web.HttpUtility.HtmlEncode` to prevent XSS.
- `Raw` — passes through unescaped. For trusted HTML only.
- `ClientComponent` — renders `<div data-fire-component="Name" data-fire-props="..."></div>`. Props are JSON-serialized and HTML-attribute-encoded.
- `Fragment` — concatenates children.
- `Empty` — empty string.

File: `src/Fire/View/Render.fs`

## 3. Query Prefetching & Dehydration

```fsharp
type QueryCache() =
    member _.Add(key: string, data: obj) : unit
    member _.Entries : QueryEntry list
    member _.DehydrateScript() : Node

module Query =
    let prefetch (key: string) (fetch: unit -> Task<'T>) (cache: QueryCache) : Task<'T>
```

`QueryCache` is created per-request. `prefetch` executes the fetch function, stores the result keyed by query key. `DehydrateScript` renders a `<script>` tag with `window.__FIRE_QUERY_STATE__` containing all prefetched data in TanStack Query's expected format:

```json
[
    { "queryKey": ["user-1"], "state": { "data": { "name": "Alice" } } }
]
```

Usage:
```fsharp
let userPage userId (cache: QueryCache) = task {
    let! user = Query.prefetch $"user-{userId}" (fun () -> getUser userId) cache
    return Html.div [
        Html.h1 [ Text user.Name ]
        Component.client "UserProfile" {| userId = userId |}
    ]
}
```

File: `src/Fire/View/Query.fs`

## 4. View Module

```fsharp
type ViewConfig = {
    Title: string
    Content: Node
    Scripts: string list
    Styles: string list
    QueryCache: QueryCache option
    Head: Node list
}

module View =
    let page (title: string) (content: Node) : ViewConfig
    let withScript (src: string) (config: ViewConfig) : ViewConfig
    let withStyle (href: string) (config: ViewConfig) : ViewConfig
    let withQueryCache (cache: QueryCache) (config: ViewConfig) : ViewConfig
    let withHead (node: Node) (config: ViewConfig) : ViewConfig
    let render (config: ViewConfig) : Response
```

`View.render` produces a full HTML document:

```html
<!DOCTYPE html>
<html>
<head>
    <meta charset="utf-8">
    <title>{Title}</title>
    <link rel="stylesheet" href="{each style}">
    {Head nodes}
</head>
<body>
    <div id="app">{Content}</div>
    <script>window.__FIRE_QUERY_STATE__={dehydrated}</script>
    <script src="{each script}"></script>
</body>
</html>
```

Returns a standard `Response` with `Content-Type: text/html; charset=utf-8`.

Usage in Fire handler:
```fsharp
Route.get "/users/:id" (fun req -> task {
    let cache = QueryCache()
    let! content = userPage (int req.Params.["id"]) cache
    return
        View.page "User Profile" content
        |> View.withQueryCache cache
        |> View.withScript "/static/client.js"
        |> View.withStyle "/static/styles.css"
        |> View.render
})
```

File: `src/Fire/View/View.fs`

## 5. @fire/react — Client NPM Package

Small TypeScript package (~100 lines) that handles client-side hydration.

```typescript
import { hydrateRoot } from 'react-dom/client'
import { QueryClient, QueryClientProvider, HydrationBoundary } from '@tanstack/react-query'

type ComponentMap = Record<string, React.ComponentType<any>>

export function hydrateFireApp(components: ComponentMap) {
    const queryClient = new QueryClient()
    const dehydratedState = (window as any).__FIRE_QUERY_STATE__
    const markers = document.querySelectorAll('[data-fire-component]')

    markers.forEach(marker => {
        const name = marker.getAttribute('data-fire-component')!
        const props = JSON.parse(marker.getAttribute('data-fire-props') || '{}')
        const Component = components[name]
        if (!Component) return console.warn(`Fire: unknown component "${name}"`)

        hydrateRoot(marker,
            <QueryClientProvider client={queryClient}>
                <HydrationBoundary state={dehydratedState}>
                    <Component {...props} />
                </HydrationBoundary>
            </QueryClientProvider>
        )
    })
}
```

User's client entry:
```tsx
import { hydrateFireApp } from '@fire/react'
import { LikeButton } from './components/LikeButton'
import { UserProfile } from './components/UserProfile'

hydrateFireApp({ LikeButton, UserProfile })
```

Published as `@fire/react` on npm. Peer dependencies: `react`, `react-dom`, `@tanstack/react-query`.

## File Structure

### F# (in src/Fire/)
```
src/Fire/
├── View/
│   ├── Node.fs       -- Node, Attr, Html, Component
│   ├── Render.fs     -- Render.toHtml
│   ├── Query.fs      -- QueryCache, Query.prefetch
│   └── View.fs       -- ViewConfig, View.page/render
```

### TypeScript (separate package)
```
packages/fire-react/
├── package.json
├── tsconfig.json
├── src/
│   └── index.tsx
```

## End-to-End Flow

1. User defines a Fire route handler
2. Handler creates `QueryCache()`, calls `Query.prefetch` to fetch data
3. Handler builds `Node` tree using `Html.*` and `Component.client`
4. Handler calls `View.page |> View.withQueryCache |> View.withScript |> View.render`
5. Fire renders full HTML with dehydrated query state and component markers
6. Browser loads HTML, shows server-rendered content immediately
7. Client JS loads, `hydrateFireApp()` finds component markers
8. React mounts client components, TanStack Query hydrates from `__FIRE_QUERY_STATE__`
9. No re-fetch — data is already in the cache from SSR
