open System.Threading
open Fire
open UrlShortener

let (routes, config) = App.create()

printfn "Fire URL Shortener running on http://localhost:3000"

App.run routes config CancellationToken.None
|> fun t -> t.GetAwaiter().GetResult()
