module Firefly.Cli.Tests.DevManifestTests

open System
open System.IO
open Xunit
open FsUnit.Xunit
open Firefly.Cli

let private newTempDir () =
    let dir = Path.Combine(Path.GetTempPath(), "firefly-devmanifest-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(dir) |> ignore
    dir

[<Fact>]
let ``parse reads generator specs`` () =
    let json =
        """{ "generators": [
              { "kind": "schema", "name": "User", "fields": ["name:string", "age:int"] },
              { "kind": "controller", "name": "Health" }
        ] }"""
    let m = DevManifest.parse json
    m.generators |> should haveLength 2
    m.generators.[0].kind |> should equal "schema"
    m.generators.[0].name |> should equal "User"
    m.generators.[0].fields |> should equal [ "name:string"; "age:int" ]
    m.generators.[1].kind |> should equal "controller"
    m.generators.[1].name |> should equal "Health"

[<Fact>]
let ``runGenerators runs the declared schema generator`` () =
    let dir = newTempDir ()
    File.WriteAllText(
        Path.Combine(dir, "firefly.json"),
        """{ "generators": [ { "kind": "schema", "name": "User", "fields": ["name:string"] } ] }""")
    let n = DevManifest.runGenerators dir
    n |> should equal 1
    let schemaFile = Path.Combine(dir, "Schemas", "UserSchema.fs")
    File.Exists(schemaFile) |> should be True
    File.ReadAllText(schemaFile) |> should haveSubstring "UserInput"

[<Fact>]
let ``runGenerators returns 0 when no manifest is present`` () =
    let dir = newTempDir ()
    DevManifest.runGenerators dir |> should equal 0
