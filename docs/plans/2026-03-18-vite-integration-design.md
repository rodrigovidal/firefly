# Fire Vite Integration — Design

Light integration with Vite for client JS bundling, CSS processing, and React HMR. Fire provides a middleware for dev proxying and helpers for manifest-based asset resolution in production. Users run Vite separately — Fire does not depend on Node.js.

## Architecture

```
Development:
  Browser → Fire (port 3000)
    ├── HTML responses (Fire handlers)
    ├── /@vite/*, /src/* → proxy to Vite dev server (port 5173)
    └── Static files (Fire)

  Browser → Vite (port 5173, direct)
    └── WebSocket HMR connection

Production:
  Browser → Fire (port 3000)
    ├── HTML responses with hashed asset URLs from manifest
    └── /assets/* → static files (Vite build output in wwwroot/)
```

## 1. Vite Module

```fsharp
// src/Fire/Vite.fs

module Vite =
    val dev : unit -> Middleware
    val dev : port:int -> Middleware
    val script : entry:string -> Node
    val styles : entry:string -> Node
    val reactRefresh : unit -> Node
```

### Vite.dev() — Dev Proxy Middleware

Proxies specific requests to Vite's dev server (`http://localhost:5173` by default):

```fsharp
let private shouldProxy (path: string) =
    path.StartsWith("/@vite/") ||
    path.StartsWith("/@react-refresh") ||
    path.StartsWith("/src/") ||
    path.StartsWith("/node_modules/.vite/") ||
    path.EndsWith(".tsx") ||
    path.EndsWith(".ts") ||
    path.EndsWith(".jsx") ||
    path.EndsWith(".css") && path.Contains("?")
```

Behavior:
- Forwards matching requests to Vite, copies response back
- If Vite is unreachable, returns 502 with: `"Vite dev server not running. Start it with 'npm run dev'."`
- In production, the middleware is a no-op (calls `next` immediately)
- Uses a single shared `HttpClient`
- No WebSocket proxying — Vite's HMR WebSocket connects directly from browser to `localhost:5173`

### Vite.script — Script Tag Helper

```fsharp
val script : entry:string -> Node
```

- **Dev:** `<script type="module" src="http://localhost:5173/{entry}"></script>`
- **Prod:** reads `.vite/manifest.json`, returns `<script type="module" src="/{file}"></script>` with hashed filename

### Vite.styles — Stylesheet Helper

```fsharp
val styles : entry:string -> Node
```

- **Dev:** `Empty` (Vite injects CSS via HMR, no `<link>` tags needed)
- **Prod:** reads manifest, returns `Fragment` of `<link rel="stylesheet" href="/{css}">` for each CSS file associated with the entry

### Vite.reactRefresh — React Refresh Preamble

```fsharp
val reactRefresh : unit -> Node
```

- **Dev:** injects the `@react-refresh` preamble script Vite needs before any React code
- **Prod:** `Empty`

## 2. Manifest Resolution

Vite 5+ outputs manifest to `{outDir}/.vite/manifest.json`:

```json
{
  "src/MyApp/Assets/js/main.tsx": {
    "file": "assets/main-BRBfp3Xq.js",
    "css": ["assets/main-DiwrgTda.css"]
  }
}
```

Internal types:

```fsharp
type private ManifestEntry = {
    File: string
    Css: string list
}
```

Behavior:
- Manifest is read once at startup and cached in a `Map<string, ManifestEntry>`
- Path convention: `wwwroot/.vite/manifest.json` (Vite's default outDir is `wwwroot`)
- Dev vs prod detected via `ASPNETCORE_ENVIRONMENT` / `FIRE_ENVIRONMENT` (same as rest of Fire)

Error handling:
- Manifest missing in production → throw at startup: `"Vite manifest not found at {path}. Run 'npm run build' first."`
- Entry missing in manifest → throw: `"Entry '{entry}' not found in Vite manifest. Check your vite.config.ts input."`
- In development → manifest is never read

## 3. Scaffold Template

`fire new` generates a Vite-ready project:

```
MyApp/
  src/MyApp/
    Assets/
      js/
        main.tsx              # Client entry point
        components/           # React components for hydration
      css/
        app.css               # Global styles
    wwwroot/                  # Vite build output (gitignored)
  package.json                # Vite + React deps
  vite.config.ts              # Pre-configured for Fire
```

### vite.config.ts

```typescript
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  build: {
    outDir: 'src/MyApp/wwwroot',
    manifest: true,
    rollupOptions: {
      input: 'src/MyApp/Assets/js/main.tsx',
    },
  },
  server: {
    strictPort: true,
  },
})
```

### Layout usage

```fsharp
module RootLayout =
    let render (title: string) (content: string) =
        let head = Render.toHtml (Fragment [
            Vite.reactRefresh ()
            Vite.styles "src/MyApp/Assets/js/main.tsx"
        ])
        let scripts = Render.toHtml (Vite.script "src/MyApp/Assets/js/main.tsx")
        $"""<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <title>{title}</title>
  {head}
</head>
<body>
  {content}
  {scripts}
</body>
</html>"""
```

### Dev workflow

Two terminals (or `concurrently` in a single npm script):
- Terminal 1: `fire dev` (dotnet watch)
- Terminal 2: `npm run dev` (Vite dev server)

## File Structure

```
src/Fire/
  Vite.fs       # Vite module: dev proxy, script, styles, reactRefresh, manifest
```

## Scope

**In scope:** Dev proxy middleware, manifest-based asset resolution, script/styles/reactRefresh helpers, scaffold template with Vite config.

**Out of scope:** Automatic Vite process management (users run Vite themselves), SSR of React components, CSS Modules integration, image optimization.
