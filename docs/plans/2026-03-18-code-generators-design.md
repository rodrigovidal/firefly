# Fire Code Generators — Design

CLI commands to scaffold controllers, views, API modules, and repository interfaces. Generated code uses the view engine DSL, Flame validation, auto DI, and cursor pagination.

## Commands

### fire gen html

```
fire gen html <Resource> field:type [field:type ...]
```

Generates a full HTML CRUD set with in-memory repository:

- `Domain/<Resource>.fs` — record type, repository interface, in-memory implementation
- `Controllers/<Resource>Controller.fs` — list, get, newForm, create, editForm, update, delete
- `Views/<Resource>View.fs` — list (with cursor pagination), show, form views using Html DSL

Prints grouped routes to add to Router.fs.

### fire gen json

```
fire gen json <Resource> field:type [field:type ...]
```

Generates a JSON API module with in-memory repository:

- `Domain/<Resource>.fs` — record type, repository interface, in-memory implementation
- `Api/<Resource>Api.fs` — list, get, create, update, delete with Flame schema validation

Prints grouped routes to add to Router.fs.

## Field Types

| Syntax | F# type | Flame |
|--------|---------|-------|
| `name:string` | `string` | `Schema.string` |
| `age:int` | `int` | `Schema.int` |
| `price:float` | `float` | `Schema.float` |
| `active:bool` | `bool` | `Schema.bool` |

## Generated Types

For `fire gen html Users name:string email:string`:

```fsharp
// Domain/User.fs

type User = {
    Id: Guid
    Name: string
    Email: string
}

type IUserRepository =
    abstract List : cursor: Guid option -> limit: int -> Task<{| Items: User list; NextCursor: Guid option |}>
    abstract Get : id: Guid -> Task<User option>
    abstract Create : input: {| Name: string; Email: string |} -> Task<User>
    abstract Update : id: Guid -> input: {| Name: string; Email: string |} -> Task<User option>
    abstract Delete : id: Guid -> Task<bool>

type InMemoryUserRepository() =
    let users = System.Collections.Generic.List<User>()

    interface IUserRepository with
        member _.List cursor limit = task {
            let filtered =
                match cursor with
                | Some c -> users |> Seq.filter (fun u -> u.Id > c)
                | None -> users |> Seq.cast
            let items = filtered |> Seq.truncate (limit + 1) |> Seq.toList
            let hasMore = items.Length > limit
            let page = items |> List.truncate limit
            let nextCursor = if hasMore then Some (page |> List.last).Id else None
            return {| Items = page; NextCursor = nextCursor |}
        }

        member _.Get id = task {
            return users |> Seq.tryFind (fun u -> u.Id = id)
        }

        member _.Create input = task {
            let user = { Id = Guid.CreateVersion7(); Name = input.Name; Email = input.Email }
            users.Add(user)
            return user
        }

        member _.Update id input = task {
            match users |> Seq.tryFindIndex (fun u -> u.Id = id) with
            | Some idx ->
                users.[idx] <- { users.[idx] with Name = input.Name; Email = input.Email }
                return Some users.[idx]
            | None -> return None
        }

        member _.Delete id = task {
            match users |> Seq.tryFindIndex (fun u -> u.Id = id) with
            | Some idx -> users.RemoveAt(idx); return true
            | None -> return false
        }
```

IDs are GUID v7 (time-ordered via `Guid.CreateVersion7()`), so cursor pagination works directly on Id.

## Generated Controller (HTML)

```fsharp
// Controllers/UserController.fs

module UserController =
    let list (repo: IUserRepository) (req: Request) = task {
        let limit = req.QueryParam "limit" |> Option.map int |> Option.defaultValue 20
        let cursor = req.QueryParam "cursor" |> Option.bind (fun s ->
            match Guid.TryParse(s) with true, g -> Some g | _ -> None)
        let! result = repo.List cursor limit
        return UserView.list result.Items result.NextCursor limit
    }

    let get (id: string) (repo: IUserRepository) (_req: Request) = task {
        match Guid.TryParse(id) with
        | true, guid ->
            match! repo.Get guid with
            | Some user -> return UserView.show user
            | None -> return Response.notFound
        | _ -> return Response.notFound
    }

    let newForm (_req: Request) = task {
        return UserView.form "New User" "/users" Map.empty Map.empty
    }

    let create (repo: IUserRepository) (req: Request) = task {
        match! Schema.parseRequest userSchema req with
        | Ok input ->
            let! user = repo.Create input
            return Response.ok |> Response.redirect $"/users/{user.Id}" 303
        | Error errors -> // re-render form with errors
            ...
    }

    // ... editForm, update, delete
```

## Generated API (JSON)

```fsharp
// Api/PostApi.fs

module PostApi =
    let list (repo: IPostRepository) (req: Request) = task {
        let limit = req.QueryParam "limit" |> Option.map int |> Option.defaultValue 20
        let cursor = req.QueryParam "cursor" |> Option.bind (fun s ->
            match Guid.TryParse(s) with true, g -> Some g | _ -> None)
        let! result = repo.List cursor limit
        return Response.json result
    }

    let get (id: string) (repo: IPostRepository) (_req: Request) = task {
        match Guid.TryParse(id) with
        | true, guid ->
            match! repo.Get guid with
            | Some post -> return Response.json post
            | None -> return Response.notFound
        | _ -> return Response.notFound
    }

    let create (repo: IPostRepository) (req: Request) = task {
        match! Schema.parseRequest postSchema req with
        | Ok input ->
            let! post = repo.Create input
            return Response.json post |> Response.status 201
        | Error errors ->
            return Response.json {| errors = errors |} |> Response.status 400
    }

    // ... update, delete
```

## Printed Routes

### HTML

```
Add these routes to Router.fs:

    |> Route.group "/users" (fun users ->
        users
        |> Route.get "" UserController.list
        |> Route.get "/new" UserController.newForm
        |> Route.post "" UserController.create
        |> Route.get "/%s" UserController.get
        |> Route.get "/%s/edit" UserController.editForm
        |> Route.post "/%s/edit" UserController.update
        |> Route.post "/%s/delete" UserController.delete)
```

### JSON

```
Add these routes to Router.fs:

    |> Route.group "/posts" (fun posts ->
        posts
        |> Route.get "" PostApi.list
        |> Route.post "" PostApi.create
        |> Route.get "/%s" PostApi.get
        |> Route.put "/%s" PostApi.update
        |> Route.delete "/%s" PostApi.delete)
```

## DI Registration

Printed instruction:

```
Register the repository in Endpoint.fs:

    |> Fire.App.di (fun services ->
        services.AddSingleton<IUserRepository, InMemoryUserRepository>() |> ignore)
```

## fsproj Update

Printed instruction:

```
Add to your .fsproj compile list (before Router.fs):

    <Compile Include="Domain\User.fs" />
    <Compile Include="Controllers\UserController.fs" />
    <Compile Include="Views\UserView.fs" />
```

## File Structure

Changes only to `src/Fire.Cli/Program.fs` — all generation is string templates in the CLI.

## Scope

**In scope:** `fire gen html`, `fire gen json`, field type parsing, Domain/Controller/View/Api generation, route printing, DI instructions.

**Out of scope:** Auto-modifying Router.fs or fsproj, database migrations, test generation.
