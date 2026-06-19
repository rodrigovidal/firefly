module Fire.Tests.Tier1SmokeTests

open System.Net
open System.Net.Http
open System.Text
open System.Threading
open Xunit
open FsUnit.Xunit
open Firefly

[<Fact>]
let ``Tier 1 integration smoke test`` () = task {
    let routes =
        Route.start
        |> Route.get "/" (fun _ -> task { return Response.text "Fire" })
        |> Route.get "/search" (fun (req: Request) -> task {
            let q = req.QueryParam "q" |> Option.defaultValue "none"
            return Response.text q
        })
        |> Route.post "/echo" (fun (req: Request) -> task {
            let! body = req.Text()
            return Response.text body
        })
        |> Route.get "/static/*path" (fun (req: Request) -> task {
            return Response.text req.Params.["path"]
        })
        |> Route.get "/cookie" (fun _ -> task {
            return
                Response.ok
                |> Response.cookie "simple" "val"
                |> Cookie.set "secure" "tok" (
                    Cookie.defaults |> Cookie.httpOnly |> Cookie.secure |> Cookie.path "/"
                )
        })

    let config =
        App.defaults
        |> App.port 0
        |> App.middleware Cors.allowAll

    let! (port, stop) = App.runTest routes config CancellationToken.None
    use client = new HttpClient()
    let base' = $"http://127.0.0.1:{port}"

    // QueryParam
    let! r1 = client.GetAsync($"{base'}/search?q=fire")
    let! b1 = r1.Content.ReadAsStringAsync()
    b1 |> should equal "fire"

    // Text body
    let! r2 = client.PostAsync($"{base'}/echo", new StringContent("hello", Encoding.UTF8))
    let! b2 = r2.Content.ReadAsStringAsync()
    b2 |> should equal "hello"

    // Wildcard route
    let! r3 = client.GetAsync($"{base'}/static/css/app.css")
    let! b3 = r3.Content.ReadAsStringAsync()
    b3 |> should equal "css/app.css"

    // Cookies
    let! r4 = client.GetAsync($"{base'}/cookie")
    let cookies = r4.Headers.GetValues("Set-Cookie") |> Seq.toList
    cookies |> List.length |> should equal 2

    // CORS on all responses
    r1.Headers.GetValues("Access-Control-Allow-Origin") |> Seq.head |> should equal "*"

    // CORS preflight
    let preflight = new HttpRequestMessage(HttpMethod.Options, $"{base'}/anything")
    preflight.Headers.Add("Origin", "http://example.com")
    preflight.Headers.Add("Access-Control-Request-Method", "GET")
    let! r5 = client.SendAsync(preflight)
    r5.StatusCode |> should equal HttpStatusCode.NoContent

    do! stop()
}
