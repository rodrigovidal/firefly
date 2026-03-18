# Fire Client-Side Navigation — Design

Intercept link clicks, fetch full HTML, swap the body, re-hydrate React islands. Zero server changes — purely a client-side library addition to `@fire/fire-react`.

## How It Works

1. **First page load** — server returns full HTML (unchanged)
2. **Subsequent navigations** — client intercepts link clicks, fetches full HTML via `fetch()`, parses with `DOMParser`, swaps `document.body.innerHTML`, updates title, pushes history state
3. **React islands** — `fire:navigate` event triggers re-hydration of new component markers

## API

```typescript
import { hydrateFireApp, enableNavigation } from '@fire/fire-react'
import { Counter } from './components/Counter'

hydrateFireApp({ Counter })
enableNavigation()
```

One line enables client-side navigation for all internal links. No `<Link>` component, no F# changes.

## enableNavigation()

Intercepts clicks on internal `<a>` elements. Skips:
- External links (`http://...`)
- Hash-only links (`#section`)
- Links with `target` attribute
- Links with `download` attribute
- Ctrl/Meta/Shift+click (new tab)
- Links with `data-fire-reload` attribute (opt-out)

Dispatches events:
- `fire:navigate-start` — before fetch begins (for loading indicators)
- `fire:navigate` — after body swap completes (for cleanup, re-hydration)

Manages browser history via `pushState` and `popstate` listener.

Scrolls to top after navigation, or to hash target if URL contains `#`.

## navigate(url)

```typescript
async function navigate(url: string, pushState = true) {
    window.dispatchEvent(new Event('fire:navigate-start'))

    const response = await fetch(url, {
        headers: { 'X-Fire-Navigation': 'true' }
    })
    const html = await response.text()
    const doc = new DOMParser().parseFromString(html, 'text/html')

    document.body.innerHTML = doc.body.innerHTML
    document.title = doc.title

    if (pushState) history.pushState(null, '', url)

    // Scroll behavior
    if (location.hash) {
        document.querySelector(location.hash)?.scrollIntoView()
    } else {
        window.scrollTo(0, 0)
    }

    window.dispatchEvent(new Event('fire:navigate'))
}
```

Sends `X-Fire-Navigation: true` header — not used by the server today, but allows future optimization (partial responses).

## Updated hydrateFireApp

```typescript
export function hydrateFireApp(components: ComponentMap) {
    hydrate(components)
    window.addEventListener('fire:navigate', () => hydrate(components))
}

function hydrate(components: ComponentMap) {
    const queryClient = new QueryClient()
    const dehydratedState = (window as any).__FIRE_QUERY_STATE__
    const markers = document.querySelectorAll('[data-fire-component]')

    markers.forEach(marker => {
        if (marker.hasAttribute('data-fire-hydrated')) return

        const name = marker.getAttribute('data-fire-component')!
        const props = JSON.parse(marker.getAttribute('data-fire-props') || '{}')
        const Component = components[name]
        if (!Component) {
            console.warn(`Fire: unknown component "${name}"`)
            return
        }

        marker.setAttribute('data-fire-hydrated', 'true')
        hydrateRoot(marker, ...)
    })
}
```

`data-fire-hydrated` prevents double-hydration. QueryClient is re-created per navigation for fresh dehydrated state.

## Opt-out

```html
<a href="/logout" data-fire-reload>Logout</a>
```

`data-fire-reload` forces full page reload.

## Loading Indicator

```typescript
enableNavigation()

document.addEventListener('fire:navigate-start', () => {
    document.body.classList.add('loading')
})
document.addEventListener('fire:navigate', () => {
    document.body.classList.remove('loading')
})
```

## Files Changed

```
packages/fire-react/src/index.tsx    — add enableNavigation(), update hydrateFireApp()
```

No F# changes. No server changes.

## Scope

**In scope:** Link interception, HTML fetch + body swap, history management, React re-hydration, scroll reset, opt-out, navigation events.

**Out of scope:** Form submission interception (future), prefetching on hover (future), view transitions API (future), partial HTML responses (future server optimization).
