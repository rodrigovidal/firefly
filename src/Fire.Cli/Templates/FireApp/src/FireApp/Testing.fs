namespace FireApp

open Fire

module Testing =

    let createClient () =
        TestClient.createWith Router.routes Endpoint.config

    let startClient () =
        TestClient.start Router.routes Endpoint.config

    let get path client =
        client |> TestClient.get path

    let stopClient client =
        TestClient.stop client
