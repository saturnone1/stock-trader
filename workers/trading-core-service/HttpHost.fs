namespace StockTrader.TradingCoreService

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
open StockTrader.ServiceContracts.TradingCore

module HttpHost =
    let run (args: string array) =
        let config = Configuration.load()
        let json = JsonSerializerOptions(JsonSerializerDefaults.Web)
        let serverCertificate = X509Certificate2.CreateFromPemFile(
            config.ServerCertificatePath, config.ServerCertificateKeyPath)
        let clientAuthority = X509Certificate2.CreateFromPem(File.ReadAllText config.ClientCaPath)
        let builder = WebApplication.CreateBuilder(args)
        builder.WebHost.ConfigureKestrel(fun options ->
            options.ListenAnyIP(8080) |> ignore
            options.ListenAnyIP(9443, Action<ListenOptions>(fun listen ->
                listen.UseHttps(Action<HttpsConnectionAdapterOptions>(fun https ->
                    https.ServerCertificate <- serverCertificate
                    https.ClientCertificateMode <- ClientCertificateMode.RequireCertificate
                    https.ClientCertificateValidation <- fun certificate _ _ ->
                        use chain = new X509Chain()
                        chain.ChainPolicy.TrustMode <- X509ChainTrustMode.CustomRootTrust
                        chain.ChainPolicy.CustomTrustStore.Add(clientAuthority) |> ignore
                        chain.ChainPolicy.RevocationMode <- X509RevocationMode.NoCheck
                        chain.Build certificate)) |> ignore)) |> ignore) |> ignore
        builder.Services.AddSingleton config |> ignore
        builder.Services.AddSingleton json |> ignore
        builder.Services.AddSingleton<SecretStore>() |> ignore
        builder.Services.AddSingleton<TradingCoreStore>() |> ignore
        builder.Services.AddHostedService<BrokerWorker>() |> ignore
        let app = builder.Build()
        let authorized (ctx: HttpContext) =
            ctx.Connection.LocalPort = 9443
            && ctx.Request.Headers["X-StockTrader-Trading-Core-Secret"].ToString() = config.SharedSecret
            && not (isNull ctx.Connection.ClientCertificate)
        app.MapGet("/health/live", Func<IResult>(fun () -> Results.Ok {| status = "live" |})) |> ignore
        app.MapGet("/health/ready", Func<TradingCoreStore,IResult>(fun store -> Results.Ok(store.Status()))) |> ignore
        app.MapGet("/metrics", Func<TradingCoreStore,string>(fun store ->
            let status = store.Status()
            $"stocktrader_trading_core_authority_generation {status.AuthorityGeneration}\nstocktrader_trading_core_inbox_total {status.InboxCount}\nstocktrader_trading_core_outbox_pending {status.OutboxPendingCount}\n")) |> ignore
        app.MapGet("/v1/status", Func<HttpContext,TradingCoreStore,IResult>(fun ctx store ->
            if authorized ctx then Results.Ok(store.Status()) else Results.Unauthorized())) |> ignore
        app.MapGet("/v1/portfolio", Func<HttpContext,TradingCoreStore,IResult>(fun ctx store ->
            if authorized ctx then Results.Ok(store.Portfolio()) else Results.Unauthorized())) |> ignore
        app.MapPost("/v1/projections", Func<HttpContext,TradingCoreStore,TradingStateSnapshot,IResult>(fun ctx store snapshot ->
            if not (authorized ctx) then Results.Unauthorized()
            else
                try Results.Ok {| snapshotId = snapshot.SnapshotId; alreadyApplied = store.Import snapshot |}
                with
                | :? ArgumentException as error -> Results.BadRequest {| error = error.Message |}
                | :? InvalidOperationException as error -> Results.Conflict {| error = error.Message |})) |> ignore
        app.MapPost("/v1/account-configurations", Func<HttpContext,TradingCoreStore,TradingAccountConfigurationSet,IResult>(fun ctx store configuration ->
            if not (authorized ctx) then Results.Unauthorized()
            else
                try Results.Ok(store.ApplyAccountConfiguration configuration)
                with
                | :? ArgumentException as error -> Results.BadRequest {| error = error.Message |}
                | :? InvalidOperationException as error -> Results.Conflict {| error = error.Message |})) |> ignore
        app.MapPost("/v1/authority", Func<HttpContext,TradingCoreStore,TradingAuthorityContract,IResult>(fun ctx store authority ->
            if not (authorized ctx) then Results.Unauthorized()
            else
                try store.Activate authority; Results.Ok(store.Status())
                with
                | :? ArgumentException as error -> Results.BadRequest {| error = error.Message |}
                | :? InvalidOperationException as error -> Results.Conflict {| error = error.Message |})) |> ignore
        app.MapPost("/v1/commands/entries", Func<HttpContext,TradingCoreStore,TradingEntryIntent,IResult>(fun ctx store intent ->
            if not (authorized ctx) then Results.Unauthorized()
            else
                try Results.Accepted($"/v1/commands/{intent.Envelope.CommandId}", store.AcceptEntry intent)
                with
                | :? ArgumentException as error -> Results.BadRequest {| error = error.Message |}
                | :? InvalidOperationException as error -> Results.Conflict {| error = error.Message |})) |> ignore
        app.MapPost("/v1/recommendations", Func<HttpContext,TradingCoreStore,TradingRecommendationObservation,IResult>(fun ctx store observation ->
            if not (authorized ctx) then Results.Unauthorized()
            else
                try Results.Ok(store.RecordRecommendation observation)
                with
                | :? ArgumentException as error -> Results.BadRequest {| error = error.Message |}
                | :? InvalidOperationException as error -> Results.Conflict {| error = error.Message |})) |> ignore
        app.MapPost("/v1/commands/positions", Func<HttpContext,TradingCoreStore,TradingPositionCommand,IResult>(fun ctx store command ->
            if not (authorized ctx) then Results.Unauthorized()
            else
                try Results.Accepted($"/v1/commands/{command.Envelope.CommandId}", store.AcceptPosition command)
                with
                | :? ArgumentException as error -> Results.BadRequest {| error = error.Message |}
                | :? InvalidOperationException as error -> Results.Conflict {| error = error.Message |})) |> ignore
        app.MapPost("/v1/positions/state", Func<HttpContext,TradingCoreStore,TradingPositionPolicyStateUpdate,IResult>(fun ctx store update ->
            if not (authorized ctx) then Results.Unauthorized()
            else
                try Results.Ok(store.ApplyPositionState update)
                with
                | :? ArgumentException as error -> Results.BadRequest {| error = error.Message |}
                | :? InvalidOperationException as error -> Results.Conflict {| error = error.Message |})) |> ignore
        app.MapGet("/v1/commands/{commandId}", Func<HttpContext,TradingCoreStore,string,IResult>(fun ctx store commandId ->
            if not (authorized ctx) then Results.Unauthorized()
            else
                match store.CommandStatus commandId with
                | Some status -> Results.Ok status
                | None -> Results.NotFound())) |> ignore
        app.MapGet("/v1/commands/positions/{positionId}/latest", Func<HttpContext,TradingCoreStore,string,IResult>(fun ctx store positionId ->
            if not (authorized ctx) then Results.Unauthorized()
            else
                match store.LatestPositionCommand positionId with
                | Some status -> Results.Ok status
                | None -> Results.NotFound())) |> ignore
        app.MapGet("/v1/commands/entries/by-signal/{sourceSignalId}/latest", Func<HttpContext,TradingCoreStore,string,IResult>(fun ctx store sourceSignalId ->
            if not (authorized ctx) then Results.Unauthorized()
            else
                match store.LatestEntryCommand sourceSignalId with
                | Some status -> Results.Ok status
                | None -> Results.NotFound())) |> ignore
        app.Run()
        0
