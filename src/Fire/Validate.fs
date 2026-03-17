namespace Fire

open System.Text.RegularExpressions

type Validator<'T> = 'T -> Result<'T, string list>

[<RequireQualifiedAccess>]
module Validate =

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
