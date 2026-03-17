module FireApp.Tests.IntegrationTests

open Xunit
open FsUnit.Xunit
open FireApp

[<Fact>]
let ``health endpoint returns json`` () = task {
    let! client = FireApp.Testing.startClient ()
    let! response = client |> FireApp.Testing.get "/health"
    response.Status |> should equal 200
    response.Body |> should haveSubstring "\"status\":\"ok\""
    do! FireApp.Testing.stopClient client
}
