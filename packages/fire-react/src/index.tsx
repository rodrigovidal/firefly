import { hydrateRoot } from 'react-dom/client'
import { QueryClient, QueryClientProvider, HydrationBoundary } from '@tanstack/react-query'

type ComponentMap = Record<string, React.ComponentType<any>>

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
        hydrateRoot(marker,
            <QueryClientProvider client={queryClient}>
                <HydrationBoundary state={dehydratedState}>
                    <Component {...props} />
                </HydrationBoundary>
            </QueryClientProvider>
        )
    })
}

export function hydrateFireApp(components: ComponentMap) {
    hydrate(components)
    window.addEventListener('fire:navigate', () => hydrate(components))
}

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

    if (location.hash) {
        document.querySelector(location.hash)?.scrollIntoView()
    } else {
        window.scrollTo(0, 0)
    }

    window.dispatchEvent(new Event('fire:navigate'))
}

export function enableNavigation() {
    document.addEventListener('click', (e) => {
        const link = (e.target as Element).closest('a[href]') as HTMLAnchorElement | null
        if (!link) return

        const href = link.getAttribute('href')!

        // Skip: external, hash-only, new tab, download, opt-out
        if (href.startsWith('http') || href.startsWith('#')) return
        if (link.hasAttribute('target')) return
        if (link.hasAttribute('download')) return
        if (link.hasAttribute('data-fire-reload')) return
        if (e.metaKey || e.ctrlKey || e.shiftKey) return

        e.preventDefault()
        navigate(href)
    })

    window.addEventListener('popstate', () => {
        navigate(location.pathname + location.search, false)
    })
}
