namespace StockTrader.MarketDataService

open System
open System.Net
open System.Security.Cryptography
open System.Security.Cryptography.X509Certificates
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open StockTrader.ServiceContracts.MarketData

type ServiceTelemetry() =
    let mutable requests, failures, reads, writes, providerCalls = 0L, 0L, 0L, 0L, 0L
    member _.Request() = Interlocked.Increment(&requests) |> ignore
    member _.Failure() = Interlocked.Increment(&failures) |> ignore
    member _.Read() = Interlocked.Increment(&reads) |> ignore
    member _.Write() = Interlocked.Increment(&writes) |> ignore
    member _.ProviderCall() = Interlocked.Increment(&providerCalls) |> ignore
    member _.Render(ready, connected, bars, revision) = String.Join("\n", [|
        $"stocktrader_market_data_ready {if ready then 1 else 0}"
        $"stocktrader_market_data_stream_connected {if connected then 1 else 0}"
        $"stocktrader_market_data_requests_total {Interlocked.Read(&requests)}"
        $"stocktrader_market_data_failures_total {Interlocked.Read(&failures)}"
        $"stocktrader_market_data_reads_total {Interlocked.Read(&reads)}"
        $"stocktrader_market_data_writes_total {Interlocked.Read(&writes)}"
        $"stocktrader_market_data_provider_calls_total {Interlocked.Read(&providerCalls)}"
        $"stocktrader_market_data_stored_bars {bars}"
        $"stocktrader_market_data_revision {revision}"
        $"stocktrader_market_data_contract_version {MarketDataContractVersions.Current}"
        ""
    |])

module HttpHost =
    let private clientRole (settings: ServiceSettings) (certificate: X509Certificate2 | null) =
        match certificate |> Option.ofObj with
        | None -> None
        | Some trustedCertificate ->
            try
                let roots = new X509Certificate2Collection()
                roots.ImportFromPemFile(settings.ClientCaPath)
                use chain = new X509Chain()
                chain.ChainPolicy.TrustMode <- X509ChainTrustMode.CustomRootTrust
                chain.ChainPolicy.CustomTrustStore.AddRange(roots)
                chain.ChainPolicy.RevocationMode <- X509RevocationMode.NoCheck
                chain.ChainPolicy.VerificationFlags <- X509VerificationFlags.NoFlag
                let clientEku = trustedCertificate.Extensions
                                |> Seq.cast<X509Extension>
                                |> Seq.tryPick (function :? X509EnhancedKeyUsageExtension as eku -> Some eku | _ -> None)
                                |> Option.exists (fun eku -> eku.EnhancedKeyUsages |> Seq.cast<Oid> |> Seq.exists (fun oid -> oid.Value = "1.3.6.1.5.5.7.3.2"))
                let names =
                    trustedCertificate.Extensions
                    |> Seq.cast<X509Extension>
                    |> Seq.tryPick (function
                        | :? X509SubjectAlternativeNameExtension as san -> Some(san.EnumerateDnsNames())
                        | _ -> None)
                    |> Option.map Seq.toArray
                    |> Option.defaultValue Array.empty
                if not clientEku || not (chain.Build(trustedCertificate)) then None
                elif names |> Array.exists (fun name -> String.Equals(name, settings.EdgeRoleDnsName, StringComparison.Ordinal)) then Some "edge"
                elif names |> Array.exists (fun name -> String.Equals(name, settings.TradingCoreRoleDnsName, StringComparison.Ordinal)) then Some "trading-core-evidence"
                elif names |> Array.exists (fun name -> String.Equals(name, settings.AcceptanceRoleDnsName, StringComparison.Ordinal)) then Some "acceptance-evidence"
                else None
            with _ -> None

    let private result (telemetry: ServiceTelemetry) (operation: unit -> Task<'a>) = task {
        telemetry.Request()
        try
            let! value = operation()
            return Results.Ok(value) :> IResult
        with
        | :? ArgumentException as error ->
            telemetry.Failure()
            return Results.BadRequest({| error = error.Message |}) :> IResult
        | :? InvalidOperationException as error ->
            telemetry.Failure()
            return Results.Problem(error.Message, statusCode = 503) :> IResult
        | error ->
            telemetry.Failure()
            return Results.Problem(error.Message, statusCode = 500) :> IResult
    }

    let map (app: WebApplication) =
        let settings = app.Services.GetRequiredService<ServiceSettings>()
        let store = app.Services.GetRequiredService<BarStore>()
        let providers = app.Services.GetRequiredService<ProviderGateway>()
        let subscriptions = app.Services.GetRequiredService<SubscriptionState>()
        let telemetry = app.Services.GetRequiredService<ServiceTelemetry>()

        app.Use(Func<HttpContext, RequestDelegate, Task>(fun (context: HttpContext) (next: RequestDelegate) -> task {
            if not (context.Request.Path.StartsWithSegments("/v1")) then do! next.Invoke(context)
            else
                let! certificate = context.Connection.GetClientCertificateAsync(context.RequestAborted)
                match clientRole settings certificate with
                | Some "edge" -> do! next.Invoke(context)
                | Some "trading-core-evidence" when context.Request.Path.StartsWithSegments("/v1/execution-evidence") ->
                    do! next.Invoke(context)
                | Some "acceptance-evidence" when context.Request.Path.StartsWithSegments("/v1/execution-evidence") ->
                    do! next.Invoke(context)
                | Some _ -> context.Response.StatusCode <- int HttpStatusCode.Forbidden
                | None -> context.Response.StatusCode <- int HttpStatusCode.Unauthorized
        })) |> ignore

        app.MapGet("/health/live", Func<IResult>(fun () -> Results.Ok({| status = "live" |}))) |> ignore
        app.MapGet("/health/ready", Func<CancellationToken, Task<IResult>>(fun ct -> task {
            try
                let! bars, revision = store.StatusAsync(ct)
                let _, _, connected = subscriptions.Snapshot()
                return Results.Ok(MarketDataServiceStatus(MarketDataContractVersions.Current, "Remote", true, true, revision, bars, null)) :> IResult
            with error -> return Results.Json(MarketDataServiceStatus(MarketDataContractVersions.Current, "Remote", false, false, 0L, 0L, error.GetType().Name), statusCode=503) :> IResult
        })) |> ignore
        app.MapGet("/metrics", Func<CancellationToken, Task<IResult>>(fun ct -> task {
            try
                let! bars, revision = store.StatusAsync(ct)
                let _, _, connected = subscriptions.Snapshot()
                return Results.Text(telemetry.Render(true, connected, bars, revision), "text/plain; version=0.0.4") :> IResult
            with _ -> return Results.Text(telemetry.Render(false, false, 0L, 0L), "text/plain; version=0.0.4", statusCode=503) :> IResult
        })) |> ignore

        app.MapPost("/v1/bars/upsert", Func<MarketDataUpsertRequest, CancellationToken, Task<IResult>>(fun request ct ->
            result telemetry (fun () -> task { telemetry.Write(); return! store.UpsertAsync(request, ct) }))) |> ignore
        app.MapPost("/v1/bars/range", Func<MarketDataRangeRequest, CancellationToken, Task<IResult>>(fun request ct ->
            result telemetry (fun () -> task { telemetry.Read(); return! store.ReadRangeAsync(request, ct) }))) |> ignore
        app.MapPost("/v1/bars/latest", Func<MarketDataRangeRequest, CancellationToken, Task<IResult>>(fun request ct ->
            result telemetry (fun () -> task {
                telemetry.Read()
                return! store.ReadRangeAsync(request, ct)
            }))) |> ignore
        app.MapPost("/v1/execution-evidence/verify", Func<MarketDataEvidenceVerificationRequest, CancellationToken, Task<IResult>>(fun request ct ->
            result telemetry (fun () -> task { telemetry.Read(); return! store.VerifyEvidenceAsync(request, ct) }))) |> ignore
        app.MapPost("/v1/execution-evidence/latest-completed", Func<MarketDataExecutionWindowRequest, CancellationToken, Task<IResult>>(fun request ct ->
            result telemetry (fun () -> task { telemetry.Read(); return! store.ReadExecutionWindowAsync(request, ct) }))) |> ignore
        app.MapGet("/v1/corrections", Func<int64, int, CancellationToken, Task<IResult>>(fun afterRevision limit ct ->
            result telemetry (fun () -> store.CorrectionsAsync(afterRevision, limit, ct)))) |> ignore
        app.MapGet("/v1/series", Func<CancellationToken, Task<IResult>>(fun ct ->
            result telemetry (fun () -> store.SeriesAsync(ct)))) |> ignore
        app.MapPost("/v1/provider/history", Func<MarketDataProviderRequest, CancellationToken, Task<IResult>>(fun request ct ->
            result telemetry (fun () -> task { telemetry.ProviderCall(); return! providers.HistoricalAsync(request, ct) }))) |> ignore
        app.MapPost("/v1/provider/latest", Func<MarketDataProviderRequest, CancellationToken, Task<IResult>>(fun request ct ->
            result telemetry (fun () -> task {
                telemetry.ProviderCall()
                return! providers.LatestAsync(request, ct)
            }))) |> ignore
        app.MapPost("/v1/provider/intraday", Func<MarketDataIntradayRequest, CancellationToken, Task<IResult>>(fun request ct ->
            result telemetry (fun () -> task { telemetry.ProviderCall(); return! providers.IntradayAsync(request, ct) }))) |> ignore
        app.MapPost("/v1/provider/price", Func<MarketDataPriceRequest, CancellationToken, Task<IResult>>(fun request ct ->
            result telemetry (fun () -> task { telemetry.ProviderCall(); return! providers.PriceAsync(request, ct) }))) |> ignore
        app.MapPut("/v1/subscriptions", Func<MarketDataSubscriptionRequest, Task<IResult>>(fun request -> task {
            return! result telemetry (fun () -> task {
                ContractPolicy.validateVersion request.ContractVersion
                if not (String.Equals(request.Provider, "Alpaca", StringComparison.OrdinalIgnoreCase)) then invalidArg "provider" "Only Alpaca streaming is implemented"
                let symbols, generation, connected = subscriptions.Update(request.Symbols)
                return MarketDataSubscriptionResponse("Alpaca", symbols, generation, connected)
            })
        })) |> ignore

        app
