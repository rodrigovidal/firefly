module Fire.Tests.SmokeTests

open System.Net
open System.Net.Http
open System.Text
open Xunit
open FsUnit.Xunit
open Fire

[<Fact>]
let ``Full API smoke test`` () = task {
    let withCors : Middleware = fun next req -> task {
        let! response = next req
        return response |> Response.header "Access-Control-Allow-Origin" "*"
    }

    let routes =
        Route.start
        |> Route.get "/" (fun _ -> task { return Response.text "Fire" })
        |> Route.group "/api" (fun api ->
            api
            |> Route.middleware withCors
            |> Route.get "/health" (fun _ -> task { return Response.ok })
            |> Route.group "/users" (fun users ->
                users
                |> Route.get "/:id" (fun (req: Request) -> task {
                    let id = req.Params.["id"]
                    return Response.json {| id = id |}
                })
            )
        )

    let config =
        App.defaults
        |> App.port 0
        |> App.notFound (fun req -> task {
            return Response.json {| error = "not found" |} |> Response.status 404
        })

    let! (port, stop) = App.runTest routes config System.Threading.CancellationToken.None
    use client = new HttpClient()
    let base' = $"http://127.0.0.1:{port}"

    // GET /
    let! r1 = client.GetAsync($"{base'}/")
    let! b1 = r1.Content.ReadAsStringAsync()
    r1.StatusCode |> should equal HttpStatusCode.OK
    b1 |> should equal "Fire"

    // GET /api/health (has CORS header)
    let! r2 = client.GetAsync($"{base'}/api/health")
    r2.StatusCode |> should equal HttpStatusCode.OK
    r2.Headers.GetValues("Access-Control-Allow-Origin") |> Seq.head |> should equal "*"

    // GET /api/users/42 (route param + CORS) — using and! for concurrent awaiting
    let! r3 = client.GetAsync($"{base'}/api/users/42")
    and! r4nf = client.GetAsync($"{base'}/nope")
    let! b3 = r3.Content.ReadAsStringAsync()
    r3.StatusCode |> should equal HttpStatusCode.OK
    b3 |> should haveSubstring "42"
    r3.Headers.GetValues("Access-Control-Allow-Origin") |> Seq.head |> should equal "*"

    // GET /nope (custom 404) — already fetched concurrently via and! above
    let! b4 = r4nf.Content.ReadAsStringAsync()
    r4nf.StatusCode |> should equal HttpStatusCode.NotFound
    b4 |> should haveSubstring "not found"

    do! stop()
}
