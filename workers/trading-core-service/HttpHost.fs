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

module HttpHost =
    let run (args: string array) =
        let config = Configuration.load()
        let json = JsonSerializerOptions(JsonSerializerDefaults.Web)
        let serverCertificate = X509Certificate2.CreateFromPemFile(
            config.ServerCertificatePath, config.ServerCertificateKeyPath)
        let clientAuthority = X509Certificate2.CreateFromPem(File.ReadAllText config.ClientCaPath)
        let trustedClient (certificate: X509Certificate2) =
            use chain = new X509Chain()
            chain.ChainPolicy.TrustMode <- X509ChainTrustMode.CustomRootTrust
            chain.ChainPolicy.CustomTrustStore.Add(clientAuthority) |> ignore
            chain.ChainPolicy.RevocationMode <- X509RevocationMode.NoCheck
            chain.ChainPolicy.VerificationFlags <- X509VerificationFlags.NoFlag
            let clientEku =
                certificate.Extensions
                |> Seq.cast<X509Extension>
                |> Seq.tryPick (function
                    | :? X509EnhancedKeyUsageExtension as eku -> Some eku
                    | _ -> None)
                |> Option.exists (fun eku ->
                    eku.EnhancedKeyUsages
                    |> Seq.cast<Oid>
                    |> Seq.exists (fun oid -> oid.Value = "1.3.6.1.5.5.7.3.2"))
            let roleMatches =
                certificate.Extensions
                |> Seq.cast<X509Extension>
                |> Seq.tryPick (function
                    | :? X509SubjectAlternativeNameExtension as san -> Some san
                    | _ -> None)
                |> Option.exists (fun san ->
                    san.EnumerateDnsNames()
                    |> Seq.exists (fun name ->
                        String.Equals(name, config.ClientRoleDnsName, StringComparison.Ordinal)))
            clientEku && roleMatches && chain.Build certificate
        let builder = WebApplication.CreateBuilder(args)
        builder.WebHost.ConfigureKestrel(fun options ->
            options.ListenAnyIP(8080) |> ignore
            options.ListenAnyIP(9443, Action<ListenOptions>(fun listen ->
                listen.UseHttps(Action<HttpsConnectionAdapterOptions>(fun https ->
                    https.ServerCertificate <- serverCertificate
                    https.ClientCertificateMode <- ClientCertificateMode.RequireCertificate
                    https.ClientCertificateValidation <- fun certificate _ _ ->
                        trustedClient certificate)) |> ignore)) |> ignore) |> ignore
        builder.Services.AddSingleton config |> ignore
        builder.Services.AddSingleton json |> ignore
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System) |> ignore
        builder.Services.AddSingleton<ITradingBrokerFactory, AlpacaTradingBrokerFactory>() |> ignore
        RuntimeComposition.add builder.Services
        let app = builder.Build()
        let authorized (ctx: HttpContext) =
            ctx.Connection.LocalPort = 9443
            && not (isNull ctx.Connection.ClientCertificate)
        RuntimeHttpEndpoints.map app authorized
        app.Run()
        0
