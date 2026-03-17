namespace Fire

open System
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection

/// Auto-injects services from DI into handler parameters.
/// Use Inject.services for service-only handlers.
/// Use Inject.handle for handlers that also need the Request.
type Inject =

    // --- Service-only handlers (no Request access) ---

    static member services (fn: Func<'S1, Task<Response>>) : Handler =
        fun req ->
            let s1 = req.Raw.RequestServices.GetRequiredService<'S1>()
            fn.Invoke(s1)

    static member services (fn: Func<'S1, 'S2, Task<Response>>) : Handler =
        fun req ->
            let sp = req.Raw.RequestServices
            fn.Invoke(sp.GetRequiredService<'S1>(), sp.GetRequiredService<'S2>())

    static member services (fn: Func<'S1, 'S2, 'S3, Task<Response>>) : Handler =
        fun req ->
            let sp = req.Raw.RequestServices
            fn.Invoke(sp.GetRequiredService<'S1>(), sp.GetRequiredService<'S2>(), sp.GetRequiredService<'S3>())

    static member services (fn: Func<'S1, 'S2, 'S3, 'S4, Task<Response>>) : Handler =
        fun req ->
            let sp = req.Raw.RequestServices
            fn.Invoke(sp.GetRequiredService<'S1>(), sp.GetRequiredService<'S2>(), sp.GetRequiredService<'S3>(), sp.GetRequiredService<'S4>())

    // --- Service + Request handlers ---

    static member handle (fn: Func<'S1, Request, Task<Response>>) : Handler =
        fun req ->
            let s1 = req.Raw.RequestServices.GetRequiredService<'S1>()
            fn.Invoke(s1, req)

    static member handle (fn: Func<'S1, 'S2, Request, Task<Response>>) : Handler =
        fun req ->
            let sp = req.Raw.RequestServices
            fn.Invoke(sp.GetRequiredService<'S1>(), sp.GetRequiredService<'S2>(), req)

    static member handle (fn: Func<'S1, 'S2, 'S3, Request, Task<Response>>) : Handler =
        fun req ->
            let sp = req.Raw.RequestServices
            fn.Invoke(sp.GetRequiredService<'S1>(), sp.GetRequiredService<'S2>(), sp.GetRequiredService<'S3>(), req)

    static member handle (fn: Func<'S1, 'S2, 'S3, 'S4, Request, Task<Response>>) : Handler =
        fun req ->
            let sp = req.Raw.RequestServices
            fn.Invoke(sp.GetRequiredService<'S1>(), sp.GetRequiredService<'S2>(), sp.GetRequiredService<'S3>(), sp.GetRequiredService<'S4>(), req)
