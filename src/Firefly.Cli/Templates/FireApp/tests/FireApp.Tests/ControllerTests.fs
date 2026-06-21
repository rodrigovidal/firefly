module FireApp.Tests.ControllerTests

open Xunit
open FsUnit.Xunit
open FireApp.Tests

[<Fact>]
let ``home page responds with HTML`` () = task {
    let! response = Fixtures.client |> FireApp.Testing.get "/"
    response.Status |> should equal 200
    response.Headers |> should contain ("Content-Type", "text/html; charset=utf-8")
}

[<Fact>]
let ``health endpoint returns ok`` () = task {
    let! response = Fixtures.client |> FireApp.Testing.get "/health"
    response.Status |> should equal 200
    response.Body |> should haveSubstring "\"status\":\"ok\""
}
