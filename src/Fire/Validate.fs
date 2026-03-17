namespace Fire

open System.Text.RegularExpressions

type Validator<'T> = 'T -> Result<'T, string list>

[<RequireQualifiedAccess>]
module Validate =

    // --- Record-level validators (for body validation) ---

    let required (field: string) (getter: 'T -> string) : Validator<'T> =
        fun value ->
            if System.String.IsNullOrWhiteSpace(getter value) then Error [$"{field} is required"]
            else Ok value

    let minLength (field: string) (len: int) (getter: 'T -> string) : Validator<'T> =
        fun value ->
            if (getter value).Length < len then Error [$"{field} must be at least {len} characters"]
            else Ok value

    let maxLength (field: string) (len: int) (getter: 'T -> string) : Validator<'T> =
        fun value ->
            if (getter value).Length > len then Error [$"{field} must be at most {len} characters"]
            else Ok value

    let pattern (field: string) (regex: string) (getter: 'T -> string) : Validator<'T> =
        fun value ->
            if Regex.IsMatch(getter value, regex) then Ok value
            else Error [$"{field} has invalid format"]

    let combine (validators: Validator<'T> list) : Validator<'T> =
        fun value ->
            let errors = validators |> List.collect (fun v -> match v value with Error errs -> errs | Ok _ -> [])
            if errors.IsEmpty then Ok value else Error errors

    let body<'T> (validator: Validator<'T>) (handler: 'T -> System.Threading.Tasks.Task<Response>) : Handler =
        fun req -> task {
            let! value = req.Json<'T>()
            match validator value with
            | Ok validated -> return! handler validated
            | Error errors -> return Response.json {| errors = errors |} |> Response.status 400
        }

    // --- String-level rules (for query/params/headers) ---

    type Rule = string -> string option -> string list

    let isRequired : Rule = fun field value ->
        match value with None | Some "" -> [$"{field} is required"] | _ -> []

    let isInt : Rule = fun field value ->
        match value with
        | None -> []
        | Some v -> match System.Int32.TryParse(v) with true, _ -> [] | false, _ -> [$"{field} must be an integer"]

    let isMinLength (len: int) : Rule = fun field value ->
        match value with None -> [] | Some v when v.Length < len -> [$"{field} must be at least {len} characters"] | _ -> []

    let isMaxLength (len: int) : Rule = fun field value ->
        match value with None -> [] | Some v when v.Length > len -> [$"{field} must be at most {len} characters"] | _ -> []

    // --- Source-specific handlers ---

    let query (rules: (string * Rule) list) (handler: Request -> System.Threading.Tasks.Task<Response>) : Handler =
        fun req -> task {
            let errors = rules |> List.collect (fun (field, rule) -> rule field (req.QueryParam field))
            if errors.IsEmpty then return! handler req
            else return Response.json {| errors = errors |} |> Response.status 400
        }

    let param (rules: (string * Rule) list) (handler: Request -> System.Threading.Tasks.Task<Response>) : Handler =
        fun req -> task {
            let errors = rules |> List.collect (fun (field, rule) ->
                let value = match req.Params.TryGetValue(field) with true, v -> Some v | false, _ -> None
                rule field value)
            if errors.IsEmpty then return! handler req
            else return Response.json {| errors = errors |} |> Response.status 400
        }

    let headerValues (rules: (string * Rule) list) (handler: Request -> System.Threading.Tasks.Task<Response>) : Handler =
        fun req -> task {
            let errors = rules |> List.collect (fun (field, rule) -> rule field (req.Header field))
            if errors.IsEmpty then return! handler req
            else return Response.json {| errors = errors |} |> Response.status 400
        }
