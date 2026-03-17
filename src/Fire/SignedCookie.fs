namespace Fire

open System
open System.Security.Cryptography
open System.Text

[<RequireQualifiedAccess>]
module SignedCookie =

    /// Sign a cookie value with HMAC-SHA256
    let sign (secret: string) (value: string) : string =
        use hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret))
        let signature = hmac.ComputeHash(Encoding.UTF8.GetBytes(value))
        let sig64 = Convert.ToBase64String(signature)
        $"{value}.{sig64}"

    /// Verify and extract the original value from a signed cookie
    let verify (secret: string) (signedValue: string) : string option =
        let idx = signedValue.LastIndexOf('.')
        if idx <= 0 then None
        else
            let value = signedValue.Substring(0, idx)
            let expected = sign secret value
            if expected = signedValue then Some value
            else None

    /// Set a signed cookie on a response
    let set (secret: string) (name: string) (value: string) (opts: CookieOptions) (response: Response) : Response =
        let signed = sign secret value
        Cookie.set name signed opts response

    /// Read and verify a signed cookie from a request
    let get (secret: string) (name: string) (req: Request) : string option =
        match req.Cookie name with
        | Some signedValue -> verify secret signedValue
        | None -> None
