namespace FireApp

open System
open Fire

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
            Fire.App.defaults
            |> Fire.App.port 3000
            |> Fire.App.middleware RequestId.middleware
            |> Fire.App.middleware CorrelationId.middleware
            |> Fire.App.middleware Log.toConsole

        if isDevelopment () then
            baseConfig |> Fire.App.onError DevErrorPage.handler
        else
            baseConfig
