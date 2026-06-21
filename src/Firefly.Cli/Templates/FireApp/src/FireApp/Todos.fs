namespace FireApp

open System.Collections.Generic
open Flame
open Firefly

/// Example domain: an in-memory todo list with a validated input schema.
/// Replace this with your own domain modules.
module Todos =

    type Todo = { Id: int; Title: string; Completed: bool }

    let private store = List<Todo>()
    let private gate = obj ()
    let mutable private nextId = 1

    let all () : Todo list =
        lock gate (fun () -> List.ofSeq store)

    let add (title: string) (completed: bool) : Todo =
        lock gate (fun () ->
            let todo = { Id = nextId; Title = title; Completed = completed }
            nextId <- nextId + 1
            store.Add todo
            todo)

    /// Input schema with validation rules (Flame). Used by Schema.parseRequest.
    let inputSchema =
        schema {
            let! title = Schema.required "title" Schema.string [ Schema.nonempty; Schema.maxLength 140; Schema.trim ]
            let! completed = Schema.optional "completed" Schema.bool false []
            return {| Title = title; Completed = completed |}
        }
