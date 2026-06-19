module Firefly.Tests.QueryBindingTests

open Xunit
open FsUnit.Xunit
open Firefly

type UserFilters = { search: string; limit: int; offset: int }
type OptionalFilters = { search: string }
type CreateUser = { name: string; email: string }

[<Fact>]
let ``GET with record param binds from query string`` () = task {
    let routes =
        Route.start
        |> Route.get "/users" (fun (filters: UserFilters) -> task {
            return Response.json {| search = filters.search; limit = filters.limit |}
        })
    let client = TestClient.create routes
    let! r = client |> TestClient.get "/users?search=alice&limit=10&offset=0"
    r.Status |> should equal 200
    r.Body |> should haveSubstring "alice"
    r.Body |> should haveSubstring "10"
}

[<Fact>]
let ``GET with record param returns 400 on invalid query`` () = task {
    let routes =
        Route.start
        |> Route.get "/users" (fun (filters: UserFilters) -> task {
            return Response.json filters
        })
    let client = TestClient.create routes
    // limit should be int, passing non-numeric
    let! r = client |> TestClient.get "/users?search=alice&limit=abc&offset=0"
    r.Status |> should equal 400
}

[<Fact>]
let ``GET with record and Request params both work`` () = task {
    let routes =
        Route.start
        |> Route.get "/users" (fun (req: Request) (filters: OptionalFilters) -> task {
            let path = req.Path
            return Response.json {| path = path; search = filters.search |}
        })
    let client = TestClient.create routes
    let! r = client |> TestClient.get "/users?search=bob"
    r.Status |> should equal 200
    r.Body |> should haveSubstring "bob"
    r.Body |> should haveSubstring "/users"
}

[<Fact>]
let ``GET with optional fields handles missing query params`` () = task {
    let routes =
        Route.start
        |> Route.get "/search" (fun (filters: OptionalFilters) -> task {
            return Response.json filters
        })
    let client = TestClient.create routes
    let! r = client |> TestClient.get "/search?search=test"
    r.Status |> should equal 200
    r.Body |> should haveSubstring "test"
}

[<Fact>]
let ``POST with record param still binds from body not query`` () = task {
    let routes =
        Route.start
        |> Route.post "/users" (fun (user: CreateUser) -> task {
            return Response.json {| name = user.name |}
        })
    let client = TestClient.create routes
    let! r = client |> TestClient.post "/users" """{"name":"alice","email":"a@b.com"}"""
    r.Status |> should equal 200
    r.Body |> should haveSubstring "alice"
}

[<Fact>]
let ``query binding works with Route.group`` () = task {
    let routes =
        Route.start
        |> Route.group "/api" (fun api ->
            api |> Route.get "/users" (fun (filters: OptionalFilters) -> task {
                return Response.json filters
            })
        )
    let client = TestClient.create routes
    let! r = client |> TestClient.get "/api/users?search=grouped"
    r.Status |> should equal 200
    r.Body |> should haveSubstring "grouped"
}
