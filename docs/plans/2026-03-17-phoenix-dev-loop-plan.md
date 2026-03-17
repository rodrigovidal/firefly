# Phoenix-Style Dev Loop Plan

**Goal:** Make Fire feel opinionated and productive in the same way Phoenix does: one golden path, fast development feedback, generators, and a named application structure that users can learn once and reuse everywhere.

**Product Direction:** Fire should stop acting like "just a minimal HTTP toolkit" when used for full applications. For app development, it should provide a first-party project shape, a first-party CLI, and first-party development ergonomics.

## Principles

1. **Conventions over configuration**
Users should not have to decide where controllers, views, templates, components, assets, and tests live.

2. **One golden path**
The default scaffold should represent the recommended Fire architecture, not one example among many.

3. **Fast local feedback**
The development loop should include rich error pages, file watching, code reload, and browser live reload.

4. **Generators, not boilerplate**
Developers should create features through commands that preserve naming, file placement, and framework conventions.

5. **Server-first**
The view layer should work well without requiring a separate SPA. Client hydration should be optional and additive.

## Milestone Scope

This milestone should be treated as one cohesive product surface, even if it lands in multiple PRs:

- opinionated project scaffold
- framework CLI and generators
- dev error page
- hot reload / watch mode
- live reload for browser-facing assets and templates
- router conventions: scopes and pipelines
- named project structure and test conventions

## Opinionated Project Structure

The default generated app should look like this:

```text
my_app/
  Fire.sln
  src/MyApp/
    MyApp.fsproj
    App.fs
    Router.fs
    Endpoint.fs
    Config/
      Dev.fs
      Prod.fs
    Controllers/
      PageController.fs
    Views/
      PageView.fs
    Components/
      CoreComponents.fs
    Layouts/
      RootLayout.fs
      AppLayout.fs
    Domain/
      Accounts.fs
      Blog.fs
    Static/
      favicon.ico
      robots.txt
    Assets/
      css/app.css
      js/app.js
  tests/MyApp.Tests/
    MyApp.Tests.fsproj
    Fixtures.fs
    ControllerTests.fs
    IntegrationTests.fs
```

### File Responsibilities

- `App.fs` wires the application together and is the main bootstrapping entry point.
- `Endpoint.fs` owns middleware, dev tools, static files, error pages, and HTTP runtime configuration.
- `Router.fs` declares scopes, pipelines, and route groups.
- `Controllers/` handles request orchestration and returns rendered views or JSON.
- `Views/` render server HTML.
- `Components/` holds reusable UI building blocks.
- `Layouts/` owns shared shells and page wrappers.
- `Config/` separates development and production behavior with strongly named modules.
- `Static/` contains files served as-is.
- `Assets/` contains source CSS/JS that participate in the dev loop.
- `tests/Fixtures.fs` provides reusable setup helpers and seeded test data.

## Router Conventions

Phoenix-like DX requires a router that teaches developers how to organize an app.

### Required Concepts

- `scope` for URL and namespace grouping
- `pipeline` for middleware stacks
- `pipeThrough` for applying named pipelines to groups
- `resources` or generator-produced CRUD route groups

### Example Shape

```fsharp
let browser =
    Pipeline.create "browser"
    |> Pipeline.accepts ["text/html"]
    |> Pipeline.fetchSession
    |> Pipeline.putRootLayout RootLayout.render
    |> Pipeline.protectFromForgery

let api =
    Pipeline.create "api"
    |> Pipeline.accepts ["application/json"]

let routes =
    Router.create()
    |> Router.scope "/" (fun scope ->
        scope
        |> Router.pipeThrough [browser]
        |> Router.get "/" PageController.home
    )
    |> Router.scope "/api" (fun scope ->
        scope
        |> Router.pipeThrough [api]
        |> Router.resources "/posts" PostApiController.routes
    )
```

The framework does not need every Phoenix router feature on day one, but it does need the naming model and the grouping primitives.

## CLI and Generators

The CLI should be a first-class part of the framework, not an afterthought.

### Commands for the First Release

- `fire new MyApp`
- `fire new MyApp --api`
- `fire gen html Blog Post posts title:string body:text published:bool`
- `fire gen json Blog Post posts title:string body:text`
- `fire gen controller Page home about`
- `fire test`
- `fire dev`

### Generator Output Rules

- create files in canonical locations only
- preserve naming conventions automatically
- update `Router.fs` when generation implies new routes
- update tests when generation implies new features
- avoid interactive prompts unless required

The CLI should reduce decisions, not create them.

## Dev Loop Design

### `fire dev`

`fire dev` should be the single entry point for local development. It should:

- start the app in development mode
- watch source, templates, static assets, and generated files
- reload server code on change
- trigger browser live reload when relevant files change
- surface compilation errors and request errors clearly

### Hot Reload / Watch Rules

- F# source changes should restart or hot-reload the server automatically
- view/template changes should refresh the browser without a manual restart
- asset changes should rebuild or copy changed files and refresh the browser
- router changes should be treated as first-class watched files

### Dev Error Page

The development error page should include:

- exception type and message
- filtered stack trace with local source emphasis
- request method, path, headers, and route params
- rendered view/controller context when available
- quick navigation to the failing file and line

In production, this must collapse to a safe 500 page or JSON response.

## Testing Conventions

Phoenix-like confidence comes from first-party testing defaults.

### Required Defaults

- app scaffold includes a test project by default
- `Fixtures.fs` provides test helpers and shared bootstrapping
- route/controller tests are easy in-process
- integration tests are easy against a real server
- generated features include generated tests

### Initial Test Categories

- controller tests
- router tests
- view rendering tests
- integration tests

## Delivery Order

### Phase 1: Foundation

- define the opinionated project structure
- ship `dotnet new fire`
- ship `fire new`
- create a generated sample app that uses the canonical structure

### Phase 2: Dev Loop

- ship `fire dev`
- implement file watching
- implement server reload
- implement browser live reload
- implement the development error page

### Phase 3: Framework Conventions

- add router scopes and pipelines
- add the first-party view/layout/component conventions
- add generator support for HTML and JSON features

### Phase 4: Testing and Polish

- add generated tests and fixtures
- improve diagnostics
- improve template output quality
- document the golden path end-to-end

## Acceptance Criteria

- a new developer can create an app with one command and run it with one command
- generated apps share the same structure and naming model
- route, controller, view, and asset changes are reflected without manual restarts
- development failures produce rich diagnostics
- documentation teaches one recommended way to organize an app
- generated features include tests and router updates automatically

## Non-Goals for This Milestone

- LiveView-equivalent real-time UI
- a large plugin ecosystem
- multiple competing project templates
- broad configurability of directory layout

The point of this milestone is to make Fire feel decisive.
