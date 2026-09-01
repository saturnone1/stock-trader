namespace StockTrader.TradingCoreBrokerEmulator

open System
open System.IO
open System.Security.Cryptography.X509Certificates
open System.Text.Json
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Server.Kestrel.Https
open Microsoft.AspNetCore.Server.Kestrel.Core
open Microsoft.Extensions.DependencyInjection
open StockTrader.ServiceContracts
open StockTrader.ServiceContracts.TradingCore
open StockTrader.TradingCore.Broker

type CancelRequest = { orderId: string }

module HttpHost =
    let private required name =
        match Environment.GetEnvironmentVariable name with
        | null | "" -> failwith $"Missing required setting: {name}"
        | value -> value

    let run (args: string array) =
        let data = match Environment.GetEnvironmentVariable "STOCKTRADER_BROKER_EMULATOR_DATA" with null | "" -> "/data" | value -> value
        let json = JsonSerializerOptions(JsonSerializerDefaults.Web)
        let store = EmulatorStore(Path.Combine(data, "broker-emulator.db"), json)
        let server = X509Certificate2.CreateFromPemFile(required "STOCKTRADER_BROKER_EMULATOR_SERVER_CERT_PATH", required "STOCKTRADER_BROKER_EMULATOR_SERVER_KEY_PATH")
        let root = X509Certificate2.CreateFromPem(File.ReadAllText(required "STOCKTRADER_BROKER_EMULATOR_CLIENT_CA_PATH"))
        let roles = [ required "STOCKTRADER_ACCEPTANCE_DRIVER_ROLE_DNS"; required "STOCKTRADER_ACCEPTANCE_CORE_ROLE_DNS" ]
        let role (certificate: X509Certificate2) =
            use chain = new X509Chain()
            chain.ChainPolicy.TrustMode <- X509ChainTrustMode.CustomRootTrust
            chain.ChainPolicy.CustomTrustStore.Add root |> ignore
            chain.ChainPolicy.RevocationMode <- X509RevocationMode.NoCheck
            let names = certificate.Extensions |> Seq.cast<X509Extension> |> Seq.choose (function | :? X509SubjectAlternativeNameExtension as san -> Some san | _ -> None) |> Seq.collect (fun san -> san.EnumerateDnsNames()) |> Seq.toArray
            if not (chain.Build certificate) then None
            else roles |> List.tryFind (fun expected -> names |> Array.contains expected)
        let trusted certificate = role certificate |> Option.isSome
        let builder = WebApplication.CreateBuilder(args)
        builder.WebHost.ConfigureKestrel(fun options ->
            options.ListenAnyIP(8080) |> ignore
            options.ListenAnyIP(10443, Action<ListenOptions>(fun listen ->
                listen.UseHttps(Action<HttpsConnectionAdapterOptions>(fun https ->
                    https.ServerCertificate <- server
                    https.ClientCertificateMode <- ClientCertificateMode.RequireCertificate
                    https.ClientCertificateValidation <- fun certificate _ _ -> trusted certificate)) |> ignore)) |> ignore) |> ignore
        builder.Services.AddSingleton<EmulatorStore>(store) |> ignore
        let app = builder.Build()
        let guarded expected (ctx: HttpContext) =
            ctx.Connection.LocalPort = 10443
            && not (isNull ctx.Connection.ClientCertificate)
            && role ctx.Connection.ClientCertificate = Some expected
        let driver ctx = guarded roles[0] ctx
        let core ctx = guarded roles[1] ctx
        app.MapGet("/health/live", Func<IResult>(fun () -> Results.Ok {| status = "live" |})) |> ignore
        app.MapPost("/control/plan", Func<HttpContext,ScriptedBrokerPlan,IResult>(fun ctx plan -> if not (driver ctx) then Results.Unauthorized() else try Results.Ok {| applied = store.LoadPlan plan |} with | :? ArgumentException as e -> Results.BadRequest {| error = e.Message |} | :? InvalidOperationException as e -> Results.Conflict {| error = e.Message |})) |> ignore
        app.MapPost("/control/barriers", Func<HttpContext,ScriptedBrokerBarrierRequest,IResult>(fun ctx request -> if driver ctx then Results.Ok {| advanced = store.AdvanceBarrier request.Name |} else Results.Unauthorized())) |> ignore
        app.MapGet("/control/journal", Func<HttpContext,IResult>(fun ctx -> if driver ctx then Results.Ok(store.Journal()) else Results.Unauthorized())) |> ignore
        app.MapGet("/control/state", Func<HttpContext,IResult>(fun ctx -> if driver ctx then Results.Ok(store.TerminalState()) else Results.Unauthorized())) |> ignore
        let execute operation clientId request = store.Execute(operation, clientId, CanonicalJsonHash.Compute request)
        app.MapPost("/broker/submit-entry", Func<HttpContext,BrokerEntryOrderRequest,IResult>(fun ctx request -> if core ctx then Results.Ok(execute ScriptedBrokerOperations.SubmitEntry request.ClientOrderId request) else Results.Unauthorized())) |> ignore
        app.MapPost("/broker/increase-position", Func<HttpContext,BrokerPositionOrderRequest,IResult>(fun ctx request -> if core ctx then Results.Ok(execute ScriptedBrokerOperations.IncreasePosition request.ClientOrderId request) else Results.Unauthorized())) |> ignore
        app.MapPost("/broker/close-position", Func<HttpContext,BrokerPositionOrderRequest,IResult>(fun ctx request -> if core ctx then Results.Ok(execute ScriptedBrokerOperations.ClosePosition request.ClientOrderId request) else Results.Unauthorized())) |> ignore
        app.MapPost("/broker/cancel-order", Func<HttpContext,CancelRequest,IResult>(fun ctx request -> if core ctx then execute ScriptedBrokerOperations.CancelOrder request.orderId request |> ignore; Results.Ok true else Results.Unauthorized())) |> ignore
        app.MapGet("/broker/account", Func<HttpContext,IResult>(fun ctx -> if core ctx then Results.Ok(store.Account()) else Results.Unauthorized())) |> ignore
        app.MapGet("/broker/positions", Func<HttpContext,IResult>(fun ctx -> if core ctx then Results.Ok(store.Positions()) else Results.Unauthorized())) |> ignore
        app.MapGet("/broker/orders", Func<HttpContext,DateTime,DateTime,IResult>(fun ctx fromUtc toUtc -> if core ctx then Results.Ok(store.Orders(fromUtc, toUtc)) else Results.Unauthorized())) |> ignore
        app.Run()
        0
