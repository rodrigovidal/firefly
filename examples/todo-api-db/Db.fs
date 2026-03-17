module TodoApiDb.Db

open System.Data
open Dapper
open Microsoft.Data.Sqlite

[<CLIMutable>]
type Todo = {
    Id: int
    Title: string
    Completed: bool
    CreatedAt: string
}

let connect (connectionString: string) : IDbConnection =
    let conn = new SqliteConnection(connectionString)
    conn.Open()
    conn :> IDbConnection

let ensureTable (conn: IDbConnection) =
    conn.Execute("""
        CREATE TABLE IF NOT EXISTS Todos (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Title TEXT NOT NULL,
            Completed INTEGER NOT NULL DEFAULT 0,
            CreatedAt TEXT NOT NULL DEFAULT (datetime('now'))
        )
    """) |> ignore

let getAll (conn: IDbConnection) =
    conn.Query<Todo>("SELECT * FROM Todos ORDER BY Id") |> Seq.toList

let getById (conn: IDbConnection) (id: int) =
    conn.Query<Todo>("SELECT * FROM Todos WHERE Id = @Id", {| Id = id |}) |> Seq.tryHead

let create (conn: IDbConnection) (title: string) =
    let id = conn.ExecuteScalar<int64>(
        "INSERT INTO Todos (Title) VALUES (@Title); SELECT last_insert_rowid()",
        {| Title = title |})
    getById conn (int id) |> Option.get

let update (conn: IDbConnection) (id: int) (title: string) (completed: bool) =
    let rows = conn.Execute(
        "UPDATE Todos SET Title = @Title, Completed = @Completed WHERE Id = @Id",
        {| Id = id; Title = title; Completed = if completed then 1 else 0 |})
    if rows > 0 then getById conn id
    else None

let delete (conn: IDbConnection) (id: int) =
    conn.Execute("DELETE FROM Todos WHERE Id = @Id", {| Id = id |}) > 0
