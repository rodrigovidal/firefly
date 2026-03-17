module FireApp.Tests.ControllerTests

open Xunit
open FsUnit.Xunit
open FireApp.Tests

[<Fact>]
let ``home page renders HTML`` () = task {
    let! response = Fixtures.client |> FireApp.Testing.get "/"
    response.Status |> should equal 200
    response.Headers |> should contain ("Content-Type", "text/html; charset=utf-8")
    response.Body |> should haveSubstring "Opinionated by default."
}
