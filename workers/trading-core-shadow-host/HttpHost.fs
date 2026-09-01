namespace StockTrader.TradingCoreService

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

type BrokerCapabilityAbsentFactory() =
    interface ITradingBrokerFactory with
        member _.Create _ = invalidOp "broker-capability-absent"

module ShadowHttpHost =
    let run (args: string array) =
        let config = Configuration.load()
        if config.InitialMode = TradingAuthorityMode.Remote then
            invalidOp "brokerless-host-cannot-start-remote"
        let json = JsonSerializerOptions(JsonSerializerDefaults.Web)
        let server = X509Certificate2.CreateFromPemFile(
            config.ServerCertificatePath, config.ServerCertificateKeyPath)
        let root = X509Certificate2.CreateFromPem(File.ReadAllText config.ClientCaPath)
        let trusted (certificate: X509Certificate2) =
            use chain = new X509Chain()
            chain.ChainPolicy.TrustMode <- X509ChainTrustMode.CustomRootTrust
            chain.ChainPolicy.CustomTrustStore.Add root |> ignore
            chain.ChainPolicy.RevocationMode <- X509RevocationMode.NoCheck
            chain.Build certificate
        let role (ctx: HttpContext) expected =
            not (isNull ctx.Connection.ClientCertificate)
            && ctx.Connection.ClientCertificate.Extensions
                |> Seq.cast<X509Extension>
                |> Seq.tryPick (function
                    | :? X509SubjectAlternativeNameExtension as san -> Some san
                    | _ -> None)
                |> Option.exists (fun san -> san.EnumerateDnsNames() |> Seq.exists (fun name ->
                    String.Equals(name, expected, StringComparison.Ordinal)))
        let builder = WebApplication.CreateBuilder(args)
        builder.WebHost.ConfigureKestrel(fun options ->
            options.ListenAnyIP(8080) |> ignore
            options.ListenAnyIP(9443, Action<ListenOptions>(fun listen ->
                listen.UseHttps(Action<HttpsConnectionAdapterOptions>(fun https ->
                    https.ServerCertificate <- server
                    https.ClientCertificateMode <- ClientCertificateMode.RequireCertificate
                    https.ClientCertificateValidation <- fun certificate _ _ -> trusted certificate)) |> ignore)) |> ignore) |> ignore
        builder.Services.AddSingleton config |> ignore
        builder.Services.AddSingleton json |> ignore
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System) |> ignore
        builder.Services.AddSingleton<ITradingBrokerFactory, BrokerCapabilityAbsentFactory>() |> ignore
        RuntimeComposition.add builder.Services
        let app = builder.Build()
        RuntimeHttpEndpoints.map app
            (fun ctx -> ctx.Connection.LocalPort = 9443 && role ctx config.ClientRoleDnsName)
            (fun ctx -> ctx.Connection.LocalPort = 9443 && role ctx config.CoordinatorRoleDnsName)
        app.Run()
        0
