namespace Fire

[<RequireQualifiedAccess>]
module Db =

    open System.Data
    open Microsoft.Extensions.DependencyInjection

    /// Middleware that opens a fresh IDbConnection per request and disposes it after.
    /// Register the connection factory via App.services [ Service.transientFactory (fun _ -> createConn()) ]
    /// Handlers receive IDbConnection via auto-DI.
    let connection : Middleware =
        fun next req -> task {
            let conn = req.Raw.RequestServices.GetRequiredService<IDbConnection>()
            try
                if conn.State = ConnectionState.Closed then
                    conn.Open()
                return! next req
            finally
                conn.Dispose()
        }

    /// Middleware that wraps each request in a database transaction.
    /// Commits on success (2xx/3xx), rolls back on error or 4xx/5xx.
    /// The handler can access the transaction via req.Raw.RequestServices if needed.
    let transaction : Middleware =
        fun next req -> task {
            let conn = req.Raw.RequestServices.GetRequiredService<IDbConnection>()
            if conn.State = ConnectionState.Closed then
                conn.Open()
            let txn = conn.BeginTransaction()
            try
                let! response = next req
                if response.Status >= 200 && response.Status < 400 then
                    txn.Commit()
                else
                    txn.Rollback()
                return response
            with ex ->
                txn.Rollback()
                return raise ex
        }

    /// Convenience: combines connection + transaction in one middleware.
    let transactional : Middleware =
        fun next req -> task {
            let conn = req.Raw.RequestServices.GetRequiredService<IDbConnection>()
            try
                if conn.State = ConnectionState.Closed then
                    conn.Open()
                let txn = conn.BeginTransaction()
                try
                    let! response = next req
                    if response.Status >= 200 && response.Status < 400 then
                        txn.Commit()
                    else
                        txn.Rollback()
                    return response
                with ex ->
                    txn.Rollback()
                    return raise ex
            finally
                conn.Dispose()
        }
