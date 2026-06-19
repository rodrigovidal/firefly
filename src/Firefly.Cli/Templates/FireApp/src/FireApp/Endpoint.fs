namespace FireApp

open System
open Firefly

module Endpoint =

    let private environmentName () =
        Environment.GetEnvironmentVariable("FIRE_ENVIRONMENT")
        |> Option.ofObj
        |> Option.defaultWith (fun () ->
            Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            |> Option.ofObj
            |> Option.defaultWith (fun () ->
                Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                |> Option.ofObj
                |> Option.defaultValue "Development"))

    let private isDevelopment () =
        environmentName().Equals("Development", StringComparison.OrdinalIgnoreCase)

    let config =
        let baseConfig =
            Firefly.App.defaults
            |> Firefly.App.port 3000
            |> Firefly.App.middleware RequestId.middleware
            |> Firefly.App.middleware CorrelationId.middleware
            |> Firefly.App.middleware Log.toConsole

        if isDevelopment () then
            baseConfig |> Firefly.App.onError DevErrorPage.handler
        else
            baseConfig
