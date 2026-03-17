namespace Fire

open System
open Microsoft.Extensions.DependencyInjection

[<RequireQualifiedAccess>]
module DepsResolver =

    type Resolver = IServiceProvider -> obj

    let create (depsType: Type) : Resolver =
        let ctor = depsType.GetConstructors().[0]
        let paramTypes = ctor.GetParameters() |> Array.map (fun p -> p.ParameterType)
        fun (sp: IServiceProvider) ->
            let values = paramTypes |> Array.map (fun t -> sp.GetRequiredService(t))
            ctor.Invoke(values)
