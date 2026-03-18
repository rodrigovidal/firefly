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
