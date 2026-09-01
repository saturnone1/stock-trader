namespace StockTrader.TradingCoreAcceptance

open System
open System.IO
open System.Security.Cryptography
open System.Security.Cryptography.X509Certificates
open System.Text.Json
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Server.Kestrel.Https
open Microsoft.AspNetCore.Server.Kestrel.Core
open Microsoft.Extensions.DependencyInjection
open StockTrader.ServiceContracts.TradingCore
open StockTrader.TradingCore.Broker
open StockTrader.TradingCoreService

module HttpHost =
    let run (args: string array) =
        let config = Configuration.load()
        let json = JsonSerializerOptions(JsonSerializerDefaults.Web)
        let server = X509Certificate2.CreateFromPemFile(
            config.Runtime.ServerCertificatePath, config.Runtime.ServerCertificateKeyPath)
        let root = X509Certificate2.CreateFromPem(File.ReadAllText config.Runtime.ClientCaPath)
        let trusted (certificate: X509Certificate2) =
            use chain = new X509Chain()
            chain.ChainPolicy.TrustMode <- X509ChainTrustMode.CustomRootTrust
            chain.ChainPolicy.CustomTrustStore.Add root |> ignore
            chain.ChainPolicy.RevocationMode <- X509RevocationMode.NoCheck
            let role =
                certificate.Extensions
                |> Seq.cast<X509Extension>
                |> Seq.tryPick (function
                    | :? X509SubjectAlternativeNameExtension as san -> Some san
                    | _ -> None)
                |> Option.exists (fun san ->
                    san.EnumerateDnsNames()
                    |> Seq.exists (fun name -> name = config.Runtime.ClientRoleDnsName))
            role && chain.Build certificate
        let builder = WebApplication.CreateBuilder(args)
        builder.WebHost.ConfigureKestrel(fun options ->
            options.ListenAnyIP(8080) |> ignore
            for port in [ 9443; 9543 ] do
                options.ListenAnyIP(port, Action<ListenOptions>(fun listen ->
                    listen.UseHttps(Action<HttpsConnectionAdapterOptions>(fun https ->
                        https.ServerCertificate <- server
                        https.ClientCertificateMode <- ClientCertificateMode.RequireCertificate
                        https.ClientCertificateValidation <- fun certificate _ _ -> trusted certificate)) |> ignore)) |> ignore) |> ignore
        builder.Services.AddSingleton<ServiceConfig>(config.Runtime) |> ignore
        builder.Services.AddSingleton<AcceptanceConfig>(config) |> ignore
        builder.Services.AddSingleton<JsonSerializerOptions>(json) |> ignore
        let clock = ControlledTimeProvider(config.ClockPath, DateTime(2024, 1, 2, 15, 0, 0, DateTimeKind.Utc))
        builder.Services.AddSingleton<ControlledTimeProvider>(clock) |> ignore
        builder.Services.AddSingleton<TimeProvider>(clock) |> ignore
        builder.Services.AddSingleton<ITradingBrokerFactory, ScriptedBrokerFactory>() |> ignore
        RuntimeComposition.add builder.Services
        let app = builder.Build()
        let standard (ctx: HttpContext) =
            ctx.Connection.LocalPort = 9443 && not (isNull ctx.Connection.ClientCertificate)
        RuntimeHttpEndpoints.map app standard
        app.MapGet("/internal/acceptance/time", Func<HttpContext,IResult>(fun ctx ->
            if ctx.Connection.LocalPort = 9543 && not (isNull ctx.Connection.ClientCertificate) then Results.Ok(clock.View())
            else Results.Unauthorized())) |> ignore
        app.MapPost("/internal/acceptance/time", Func<HttpContext,AcceptanceTimeAdvanceRequest,IResult>(fun ctx request ->
            if ctx.Connection.LocalPort <> 9543 || isNull ctx.Connection.ClientCertificate then Results.Unauthorized()
            else
                try Results.Ok(clock.Advance request)
                with
                | :? ArgumentException as error -> Results.BadRequest {| error = error.Message |}
                | :? InvalidOperationException as error -> Results.Conflict {| error = error.Message |})) |> ignore
        app.Run()
        0
