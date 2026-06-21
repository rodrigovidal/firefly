open System.Threading
open Firefly
open ContactsApp

let (routes, config) = App.create ()

let config' =
    config
    |> App.dashboard "/dashboard"
    |> App.middleware Log.toConsole

printfn "Contacts app running on http://localhost:3000"
printfn "  GET  /dashboard            - live metrics dashboard"
printfn "  GET  /                     - list contacts"
printfn "  GET  /contacts/new         - new contact form"
printfn "  POST /contacts             - create contact"
printfn "  GET  /contacts/:id         - show contact"
printfn "  GET  /contacts/:id/edit    - edit contact form"
printfn "  POST /contacts/:id/edit    - update contact"
printfn "  POST /contacts/:id/delete  - delete contact"

App.run routes config' CancellationToken.None
|> fun t -> t.GetAwaiter().GetResult()
