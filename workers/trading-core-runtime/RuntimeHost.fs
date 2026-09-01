namespace StockTrader.TradingCoreService

open System
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open StockTrader.ServiceContracts.TradingCore

module RuntimeComposition =
    let add (services: IServiceCollection) =
        services.AddSingleton<SecretStore>() |> ignore
        services.AddSingleton<MarketDataExecutionClient>() |> ignore
        services.AddSingleton<TradingCoreStore>() |> ignore
        services.AddHostedService<BrokerWorker>() |> ignore
        services.AddHostedService<PositionProtectionWorker>() |> ignore

module RuntimeHttpEndpoints =
    let map (app: WebApplication) (authorized: HttpContext -> bool) =
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
        app.MapPost("/v1/shadow/entries", Func<HttpContext,TradingCoreStore,TradingShadowEntryObservation,IResult>(fun ctx store observation ->
            if not (authorized ctx) then Results.Unauthorized()
            else
                try Results.Ok(store.CompareShadowEntry observation)
                with
                | :? ArgumentException as error -> Results.BadRequest {| error = error.Message |}
                | :? InvalidOperationException as error -> Results.Conflict {| error = error.Message |})) |> ignore
        app.MapGet("/v1/shadow/summary", Func<HttpContext,TradingCoreStore,IResult>(fun ctx store ->
            if authorized ctx then Results.Ok(store.ShadowSummary()) else Results.Unauthorized())) |> ignore
        app.MapPost("/v1/shadow/positions", Func<HttpContext,TradingCoreStore,TradingShadowPositionObservation,IResult>(fun ctx store observation ->
            if not (authorized ctx) then Results.Unauthorized()
            else
                try Results.Ok(store.CompareShadowPosition observation)
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
