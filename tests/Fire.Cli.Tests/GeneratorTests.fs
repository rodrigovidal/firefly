module Fire.Cli.Tests.GeneratorTests

open System
open System.IO
open Xunit
open FsUnit.Xunit
open Fire.Cli.Generator

// --- parseFields ---

[<Fact>]
let ``parseFields parses name:type pairs`` () =
    let fields = parseFields [ "name:string"; "age:int" ]
    fields |> should haveLength 2
    fields.[0].Name |> should equal "name"
    fields.[0].Type |> should equal "string"
    fields.[1].Name |> should equal "age"
    fields.[1].Type |> should equal "int"

[<Fact>]
let ``parseFields throws on invalid format`` () =
    (fun () -> parseFields [ "badformat" ] |> ignore)
    |> should throw typeof<Exception>

// --- singular ---

[<Fact>]
let ``singular removes trailing s`` () =
    singular "Users" |> should equal "User"

[<Fact>]
let ``singular handles -ies`` () =
    singular "Categories" |> should equal "Category"

[<Fact>]
let ``singular handles -ses`` () =
    singular "Addresses" |> should equal "Address"

[<Fact>]
let ``singular handles -xes`` () =
    singular "Boxes" |> should equal "Box"

[<Fact>]
let ``singular handles -ches`` () =
    singular "Batches" |> should equal "Batch"

[<Fact>]
let ``singular handles -shes`` () =
    singular "Dishes" |> should equal "Dish"

[<Fact>]
let ``singular passes through non-plural`` () =
    singular "Data" |> should equal "Data"

[<Fact>]
let ``singular handles short names`` () =
    singular "A" |> should equal "A"

// --- capitalize ---

[<Fact>]
let ``capitalize uppercases first letter`` () =
    capitalize "users" |> should equal "Users"

[<Fact>]
let ``capitalize handles already capitalized`` () =
    capitalize "Users" |> should equal "Users"

[<Fact>]
let ``capitalize handles empty string`` () =
    capitalize "" |> should equal ""

// --- generate html: output patterns ---

let private htmlOpts = {
    Kind = "html"
    Resource = "Users"
    Fields = [ { Name = "name"; Type = "string" }; { Name = "email"; Type = "string" } ]
    ProjectDir = Path.Combine(Path.GetTempPath(), $"fire-gen-test-{Guid.NewGuid()}")
    Namespace = "TestApp"
}

let private ensureClean (opts: GeneratorOptions) =
    if Directory.Exists(opts.ProjectDir) then
        Directory.Delete(opts.ProjectDir, true)
    Directory.CreateDirectory(opts.ProjectDir) |> ignore
    opts

[<Fact>]
let ``generate html creates Domain file`` () =
    let opts = ensureClean htmlOpts
    generate opts
    let path = Path.Combine(opts.ProjectDir, "Domain", "User.fs")
    File.Exists(path) |> should be True
    let content = File.ReadAllText(path)
    content |> should haveSubstring "type User ="
    content |> should haveSubstring "Id: Guid"
    content |> should haveSubstring "Name: string"
    content |> should haveSubstring "Email: string"
    content |> should haveSubstring "IUserRepository"
    content |> should haveSubstring "InMemoryUserRepository"
    content |> should haveSubstring "Guid.CreateVersion7()"
    Directory.Delete(opts.ProjectDir, true)

[<Fact>]
let ``generate html creates Controller file with Schema.parse`` () =
    let opts = ensureClean htmlOpts
    generate opts
    let path = Path.Combine(opts.ProjectDir, "Controllers", "UserController.fs")
    File.Exists(path) |> should be True
    let content = File.ReadAllText(path)
    content |> should haveSubstring "Schema.parse"
    content |> should not' (haveSubstring "Schema.parseRequest")
    content |> should haveSubstring "UserController"
    content |> should haveSubstring "let list"
    content |> should haveSubstring "let get"
    content |> should haveSubstring "let newForm"
    content |> should haveSubstring "let create"
    content |> should haveSubstring "let editForm"
    content |> should haveSubstring "let update"
    content |> should haveSubstring "let delete"
    content |> should haveSubstring "cursor"
    Directory.Delete(opts.ProjectDir, true)

[<Fact>]
let ``generate html controller uses tryGet not FormValue`` () =
    let opts = ensureClean htmlOpts
    generate opts
    let content = File.ReadAllText(Path.Combine(opts.ProjectDir, "Controllers", "UserController.fs"))
    content |> should haveSubstring "tryGet"
    content |> should not' (haveSubstring "FormValue")
    Directory.Delete(opts.ProjectDir, true)

[<Fact>]
let ``generate html creates View file with Html DSL`` () =
    let opts = ensureClean htmlOpts
    generate opts
    let path = Path.Combine(opts.ProjectDir, "Views", "UserView.fs")
    File.Exists(path) |> should be True
    let content = File.ReadAllText(path)
    content |> should haveSubstring "Html.div"
    content |> should haveSubstring "Html.h1"
    content |> should haveSubstring "View.page"
    content |> should haveSubstring "View.render"
    content |> should haveSubstring "pagination"
    content |> should haveSubstring "nextCursor"
    Directory.Delete(opts.ProjectDir, true)

// --- generate json: output patterns ---

let private jsonOpts = {
    Kind = "json"
    Resource = "Posts"
    Fields = [ { Name = "title"; Type = "string" }; { Name = "body"; Type = "string" } ]
    ProjectDir = Path.Combine(Path.GetTempPath(), $"fire-gen-test-{Guid.NewGuid()}")
    Namespace = "TestApp"
}

[<Fact>]
let ``generate json creates Domain file`` () =
    let opts = ensureClean jsonOpts
    generate opts
    let path = Path.Combine(opts.ProjectDir, "Domain", "Post.fs")
    File.Exists(path) |> should be True
    let content = File.ReadAllText(path)
    content |> should haveSubstring "type Post ="
    content |> should haveSubstring "IPostRepository"
    content |> should haveSubstring "InMemoryPostRepository"
    Directory.Delete(opts.ProjectDir, true)

[<Fact>]
let ``generate json creates Api file with Schema.parseRequest`` () =
    let opts = ensureClean jsonOpts
    generate opts
    let path = Path.Combine(opts.ProjectDir, "Api", "PostApi.fs")
    File.Exists(path) |> should be True
    let content = File.ReadAllText(path)
    content |> should haveSubstring "Schema.parseRequest"
    content |> should haveSubstring "Response.json"
    content |> should haveSubstring "let list"
    content |> should haveSubstring "let get"
    content |> should haveSubstring "let create"
    content |> should haveSubstring "let update"
    content |> should haveSubstring "let delete"
    content |> should haveSubstring "cursor"
    Directory.Delete(opts.ProjectDir, true)

[<Fact>]
let ``generate json does not create Views or Controllers`` () =
    let opts = ensureClean jsonOpts
    generate opts
    Directory.Exists(Path.Combine(opts.ProjectDir, "Views")) |> should be False
    Directory.Exists(Path.Combine(opts.ProjectDir, "Controllers")) |> should be False
    Directory.Delete(opts.ProjectDir, true)

// --- field types ---

[<Fact>]
let ``generate html with bool field uses checkbox`` () =
    let opts = ensureClean { htmlOpts with
                                Fields = [ { Name = "active"; Type = "bool" } ]
                                ProjectDir = Path.Combine(Path.GetTempPath(), $"fire-gen-test-{Guid.NewGuid()}") }
    generate opts
    let content = File.ReadAllText(Path.Combine(opts.ProjectDir, "Views", "UserView.fs"))
    content |> should haveSubstring "checkbox"
    content |> should haveSubstring "Checked"
    Directory.Delete(opts.ProjectDir, true)

[<Fact>]
let ``generate html with int field uses number input`` () =
    let opts = ensureClean { htmlOpts with
                                Fields = [ { Name = "age"; Type = "int" } ]
                                ProjectDir = Path.Combine(Path.GetTempPath(), $"fire-gen-test-{Guid.NewGuid()}") }
    generate opts
    let content = File.ReadAllText(Path.Combine(opts.ProjectDir, "Views", "UserView.fs"))
    content |> should haveSubstring "number"
    Directory.Delete(opts.ProjectDir, true)

[<Fact>]
let ``generate with unknown type throws`` () =
    let opts = { htmlOpts with
                    Fields = [ { Name = "x"; Type = "datetime" } ]
                    ProjectDir = Path.Combine(Path.GetTempPath(), $"fire-gen-test-{Guid.NewGuid()}") }
    (fun () -> generate opts)
    |> should throw typeof<Exception>
