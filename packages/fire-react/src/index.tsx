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

// --- Navigation ---

const prefetchCache = new Set<string>()

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

function shouldIntercept(element: Element, e: MouseEvent): boolean {
    if (e.metaKey || e.ctrlKey || e.shiftKey) return false
    if (element.hasAttribute('target')) return false
    if (element.hasAttribute('download')) return false
    if (element.hasAttribute('data-fire-reload')) return false
    return true
}

function isInternalHref(href: string): boolean {
    return !href.startsWith('http') && !href.startsWith('#') && !href.startsWith('mailto:')
}

export function enableNavigation() {
    // Intercept link clicks
    document.addEventListener('click', (e) => {
        const link = (e.target as Element).closest('a[href]') as HTMLAnchorElement | null
        if (!link) return

        const href = link.getAttribute('href')!
        if (!isInternalHref(href)) return
        if (!shouldIntercept(link, e)) return

        e.preventDefault()
        navigate(href)
    })

    // Intercept form submissions
    document.addEventListener('submit', (e) => {
        const form = e.target as HTMLFormElement
        if (form.hasAttribute('data-fire-reload')) return

        const method = (form.method || 'GET').toUpperCase()
        const action = form.action || location.href

        // Only intercept same-origin forms
        try {
            const url = new URL(action, location.origin)
            if (url.origin !== location.origin) return
        } catch {
            return
        }

        e.preventDefault()

        if (method === 'GET') {
            const params = new URLSearchParams(new FormData(form) as any)
            const url = new URL(action, location.origin)
            url.search = params.toString()
            navigate(url.pathname + url.search)
        } else {
            // POST/PUT/PATCH/DELETE — submit via fetch, navigate to redirect
            window.dispatchEvent(new Event('fire:navigate-start'))
            fetch(action, {
                method,
                body: new FormData(form),
                headers: { 'X-Fire-Navigation': 'true' },
                redirect: 'follow',
            }).then(async (response) => {
                const html = await response.text()
                const doc = new DOMParser().parseFromString(html, 'text/html')
                document.body.innerHTML = doc.body.innerHTML
                document.title = doc.title
                history.pushState(null, '', response.url)
                window.scrollTo(0, 0)
                window.dispatchEvent(new Event('fire:navigate'))
            })
        }
    })

    // Browser back/forward
    window.addEventListener('popstate', () => {
        navigate(location.pathname + location.search, false)
    })

    // Prefetch on hover
    document.addEventListener('mouseover', (e) => {
        const link = (e.target as Element).closest('a[href]') as HTMLAnchorElement | null
        if (!link) return

        const href = link.getAttribute('href')!
        if (!isInternalHref(href)) return
        if (link.hasAttribute('data-fire-reload')) return
        if (prefetchCache.has(href)) return

        prefetchCache.add(href)
        const prefetchLink = document.createElement('link')
        prefetchLink.rel = 'prefetch'
        prefetchLink.href = href
        document.head.appendChild(prefetchLink)
    })
}
