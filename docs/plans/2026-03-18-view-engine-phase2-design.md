# Fire View Engine — Phase 2: React Hydration

Client component hydration and TanStack Query integration. Builds on the Phase 1 server-side DSL (Node, Render, View).

## 1. Component.client Helper

Added to `src/Fire/View/Node.fs` below the `Html` type. No changes to `Node` DU or `Render` — uses existing types.

```fsharp
module Component =
    let client (name: string) (props: 'T) : Node =
        let json = System.Text.Json.JsonSerializer.Serialize(props)
        Element("div", [ Data("fire-component", name); Data("fire-props", json) ], [])
```

Produces:

```html
<div data-fire-component="LikeButton" data-fire-props="{&quot;userId&quot;:1}"></div>
```

Props JSON is HTML-encoded by `Render.renderAttr` (already encodes Data values). Client JS parses it back with `JSON.parse`.

Usage:

```fsharp
Html.div [
    Html.h1 [ Text user.Name ]
    Component.client "LikeButton" {| userId = user.Id |}
]
```

## 2. QueryCache & Query.prefetch

```fsharp
// src/Fire/View/Query.fs

type QueryEntry = { Key: string; Data: obj }

type QueryCache() =
    let entries = System.Collections.Generic.List<QueryEntry>()

    member _.Add(key: string, data: obj) =
        entries.Add({ Key = key; Data = data })

    member _.Entries = entries |> Seq.toList

    member _.DehydrateScript() : Node =
        if entries.Count = 0 then Empty
        else
            let json =
                entries
                |> Seq.map (fun e ->
                    let data = System.Text.Json.JsonSerializer.Serialize(e.Data)
                    $"""{{ "queryKey": ["{e.Key}"], "state": {{ "data": {data} }} }}""")
                |> String.concat ","
            Raw $"""<script>window.__FIRE_QUERY_STATE__=[{json}]</script>"""

module Query =
    let prefetch (key: string) (fetch: unit -> Task<'T>) (cache: QueryCache) = task {
        let! result = fetch ()
        cache.Add(key, result :> obj)
        return result
    }
```

- `QueryCache` is created per-request, collects key/data pairs
- `Query.prefetch` executes the fetch, stores in cache, returns the result for server rendering
- `DehydrateScript()` returns `Empty` (no entries) or `Raw` with a `<script>` tag in TanStack Query's expected dehydration format
- `Raw` is safe because we control the JSON serialization

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

## 3. View.withQueryCache

New field on `ViewConfig`, new pipeline function, tweak to `render`.

```fsharp
type ViewConfig = {
    Title: string
    Content: Node
    Scripts: string list
    Styles: string list
    Head: Node list
    Layout: (string -> string -> string) option
    QueryCache: QueryCache option  // new
}

module View =
    let withQueryCache (cache: QueryCache) (config: ViewConfig) : ViewConfig =
        { config with QueryCache = Some cache }
```

In `View.render`, the default document inserts the dehydration script before user scripts:

```html
</head><body>
  {content}
  <script>window.__FIRE_QUERY_STATE__=[...]</script>
  <script src="/static/client.js"></script>
</body></html>
```

When a custom layout is used, the dehydration script is appended to the content string before passing to the layout function.

Usage:

```fsharp
Route.get "/users/%i" (fun userId req -> task {
    let cache = QueryCache()
    let! content = userPage userId cache
    return
        View.page "User Profile" content
        |> View.withQueryCache cache
        |> View.withScript "/static/client.js"
        |> View.render
})
```

## 4. @fire/fire-react npm Package

Small TypeScript package (~30 lines) at `packages/fire-react/`.

```
packages/fire-react/
├── package.json
├── tsconfig.json
└── src/
    └── index.tsx
```

**package.json:** `@fire/fire-react`, peer deps: `react`, `react-dom`, `@tanstack/react-query`.

**src/index.tsx:**

```tsx
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
        if (!Component) {
            console.warn(`Fire: unknown component "${name}"`)
            return
        }

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

Each component gets its own React root with shared QueryClient so they all share the hydrated cache.

User's client entry:

```tsx
import { hydrateFireApp } from '@fire/fire-react'
import { LikeButton } from './components/LikeButton'

hydrateFireApp({ LikeButton })
```

## End-to-End Flow

1. Handler creates `QueryCache()`, calls `Query.prefetch` to fetch data
2. Handler builds `Node` tree using `Html.*` and `Component.client`
3. Handler calls `View.page |> View.withQueryCache |> View.withScript |> View.render`
4. Fire renders full HTML with dehydrated query state and component markers
5. Browser loads HTML, shows server-rendered content immediately
6. Client JS loads, `hydrateFireApp()` finds `data-fire-component` markers
7. React mounts client components, TanStack Query hydrates from `__FIRE_QUERY_STATE__`
8. No re-fetch — data is already in the cache from SSR

## File Structure

### F# (modifications to src/Fire/)
```
src/Fire/View/
├── Node.fs       -- add Component module (no Node DU changes)
├── Render.fs     -- no changes
├── Query.fs      -- new: QueryCache, QueryEntry, Query.prefetch
└── View.fs       -- add QueryCache field, withQueryCache, update render
```

### TypeScript (new)
```
packages/fire-react/
├── package.json
├── tsconfig.json
└── src/
    └── index.tsx
```

## Scope

**In scope:** Component.client helper, QueryCache, Query.prefetch, DehydrateScript, View.withQueryCache, @fire/fire-react npm package.

**Out of scope:** Build tooling for client JS (Vite/esbuild config), SSR of React components on the server, streaming hydration.
