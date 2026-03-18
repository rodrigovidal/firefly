module Fire.Tests.IdempotentTests

open System
open System.Threading.Tasks
open Xunit
open FsUnit.Xunit
open Fire

let findHeader (name: string) (headers: (string * string) list) =
    headers |> List.tryFind (fun (k, _) -> k = name) |> Option.map snd

let mutable callCount = 0

let makeRoutes () =
    callCount <- 0
    Route.start
    |> Route.post "/pay" (fun _ -> task {
        callCount <- callCount + 1
        return Response.json {| id = callCount |}
    })
    |> Route.get "/status" (fun _ -> task {
        return Response.text "ok"
    })

[<Fact>]
let ``POST with Idempotency-Key caches response`` () = task {
    let store = Idempotent.inMemory()
    let routes = makeRoutes()
    let config = App.defaults |> App.middleware (Idempotent.middleware store (TimeSpan.FromMinutes 5.0))
    let client =
        TestClient.createWith routes config
        |> TestClient.withHeader "Idempotency-Key" "key-1"
    let! r1 = client |> TestClient.post "/pay" "{}"
    let! r2 = client |> TestClient.post "/pay" "{}"
    r1.Body |> should equal r2.Body
    callCount |> should equal 1
}

[<Fact>]
let ``POST replaying cached response returns same body and status`` () = task {
    let store = Idempotent.inMemory()
    let routes =
        Route.start
        |> Route.post "/pay" (fun _ -> task {
            return Response.json {| ok = true |} |> Response.status 201
        })
    let config = App.defaults |> App.middleware (Idempotent.middleware store (TimeSpan.FromMinutes 5.0))
    let client =
        TestClient.createWith routes config
        |> TestClient.withHeader "Idempotency-Key" "key-2"
    let! r1 = client |> TestClient.post "/pay" "{}"
    let! r2 = client |> TestClient.post "/pay" "{}"
    r2.Status |> should equal 201
    r2.Body |> should equal r1.Body
}

[<Fact>]
let ``replayed response includes Idempotency-Replayed header`` () = task {
    let store = Idempotent.inMemory()
    let routes =
        Route.start
        |> Route.post "/pay" (fun _ -> task { return Response.text "done" })
    let config = App.defaults |> App.middleware (Idempotent.middleware store (TimeSpan.FromMinutes 5.0))
    let client =
        TestClient.createWith routes config
        |> TestClient.withHeader "Idempotency-Key" "key-3"
    let! _ = client |> TestClient.post "/pay" "{}"
    let! r2 = client |> TestClient.post "/pay" "{}"
    r2.Headers |> findHeader "Idempotency-Replayed" |> should equal (Some "true")
}

[<Fact>]
let ``POST without Idempotency-Key passes through`` () = task {
    callCount <- 0
    let store = Idempotent.inMemory()
    let routes =
        Route.start
        |> Route.post "/pay" (fun _ -> task {
            callCount <- callCount + 1
            return Response.text $"call-{callCount}"
        })
    let config = App.defaults |> App.middleware (Idempotent.middleware store (TimeSpan.FromMinutes 5.0))
    let client = TestClient.createWith routes config
    let! r1 = client |> TestClient.post "/pay" "{}"
    let! r2 = client |> TestClient.post "/pay" "{}"
    callCount |> should equal 2
    r1.Body |> should not' (equal r2.Body)
}

[<Fact>]
let ``GET requests pass through without caching`` () = task {
    let store = Idempotent.inMemory()
    let routes = makeRoutes()
    let config = App.defaults |> App.middleware (Idempotent.middleware store (TimeSpan.FromMinutes 5.0))
    let client =
        TestClient.createWith routes config
        |> TestClient.withHeader "Idempotency-Key" "key-4"
    let! r = client |> TestClient.get "/status"
    r.Status |> should equal 200
    r.Body |> should equal "ok"
    r.Headers |> findHeader "Idempotency-Replayed" |> should equal None
}

[<Fact>]
let ``different keys return different responses`` () = task {
    callCount <- 0
    let store = Idempotent.inMemory()
    let routes =
        Route.start
        |> Route.post "/pay" (fun _ -> task {
            callCount <- callCount + 1
            return Response.text $"call-{callCount}"
        })
    let config = App.defaults |> App.middleware (Idempotent.middleware store (TimeSpan.FromMinutes 5.0))
    let client1 =
        TestClient.createWith routes config
        |> TestClient.withHeader "Idempotency-Key" "key-a"
    let client2 =
        TestClient.createWith routes config
        |> TestClient.withHeader "Idempotency-Key" "key-b"
    let! r1 = client1 |> TestClient.post "/pay" "{}"
    let! r2 = client2 |> TestClient.post "/pay" "{}"
    callCount |> should equal 2
    r1.Body |> should not' (equal r2.Body)
}

[<Fact>]
let ``custom store is called for get and set`` () = task {
    let mutable getCalled = false
    let mutable setCalled = false
    let customStore =
        { new IdempotencyStore with
            member _.TryGet(_key) = task {
                getCalled <- true
                return None
            }
            member _.Set(_key, _response, _ttl) = task {
                setCalled <- true
            }
        }
    let routes =
        Route.start
        |> Route.post "/pay" (fun _ -> task { return Response.text "ok" })
    let config = App.defaults |> App.middleware (Idempotent.middleware customStore (TimeSpan.FromMinutes 1.0))
    let client =
        TestClient.createWith routes config
        |> TestClient.withHeader "Idempotency-Key" "custom-key"
    let! _ = client |> TestClient.post "/pay" "{}"
    getCalled |> should equal true
    setCalled |> should equal true
}

[<Fact>]
let ``expired entry is evicted and handler re-invoked`` () = task {
    callCount <- 0
    let store = Idempotent.inMemory()
    let routes =
        Route.start
        |> Route.post "/pay" (fun _ -> task {
            callCount <- callCount + 1
            return Response.text $"call-{callCount}"
        })
    let config = App.defaults |> App.middleware (Idempotent.middleware store (TimeSpan.FromMilliseconds 50.0))
    let client =
        TestClient.createWith routes config
        |> TestClient.withHeader "Idempotency-Key" "ttl-key"
    let! r1 = client |> TestClient.post "/pay" "{}"
    r1.Body |> should equal "call-1"
    // Wait for TTL to expire
    do! Task.Delay(100)
    let! r2 = client |> TestClient.post "/pay" "{}"
    r2.Body |> should equal "call-2"
    callCount |> should equal 2
}
