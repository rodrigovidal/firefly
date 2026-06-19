namespace Firefly

open System
open System.Collections.Generic
open System.Text
open Microsoft.IdentityModel.JsonWebTokens
open Microsoft.IdentityModel.Tokens

type JwtConfig = {
    SigningKey: string
    EncryptionKey: string option
    Issuer: string option
    Audience: string option
}

[<RequireQualifiedAccess>]
module Jwt =

    let private claimsKey = "fire.jwt.claims"

    let defaults (signingKey: string) : JwtConfig =
        { SigningKey = signingKey; EncryptionKey = None; Issuer = None; Audience = None }

    let encryptionKey key (config: JwtConfig) = { config with EncryptionKey = Some key }
    let issuer iss (config: JwtConfig) = { config with Issuer = Some iss }
    let audience aud (config: JwtConfig) = { config with Audience = Some aud }

    let validate (config: JwtConfig) : Middleware =
        let handler = JsonWebTokenHandler()
        let signingKey = SymmetricSecurityKey(Encoding.UTF8.GetBytes(config.SigningKey))
        let validationParams = TokenValidationParameters(
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidateIssuer = config.Issuer.IsSome,
            ValidateAudience = config.Audience.IsSome,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1.0)
        )
        match config.Issuer with Some iss -> validationParams.ValidIssuer <- iss | None -> ()
        match config.Audience with Some aud -> validationParams.ValidAudience <- aud | None -> ()
        match config.EncryptionKey with
        | Some ek -> validationParams.TokenDecryptionKey <- SymmetricSecurityKey(Encoding.UTF8.GetBytes(ek))
        | None -> ()

        fun next req ->
            let authHeader = req.Header "Authorization"
            match authHeader with
            | Some h when h.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ->
                let token = h.Substring(7).Trim()
                task {
                    let! result = handler.ValidateTokenAsync(token, validationParams)
                    if result.IsValid then
                        let claims = Dictionary<string, string>()
                        for claim in result.Claims do
                            claims.[claim.Key] <-
                                match claim.Value with
                                | :? string as s -> s
                                | v -> string v
                        req.Raw.Items.[claimsKey] <- claims :> IReadOnlyDictionary<string, string>
                        return! next req
                    else
                        return Response.json {| error = "invalid token" |} |> Response.status 401
                }
            | _ ->
                task {
                    return Response.json {| error = "missing or invalid authorization header" |} |> Response.status 401
                }

    let claims (req: Request) : IReadOnlyDictionary<string, string> option =
        match req.Raw.Items.TryGetValue(claimsKey) with
        | true, value -> Some (value :?> IReadOnlyDictionary<string, string>)
        | false, _ -> None
