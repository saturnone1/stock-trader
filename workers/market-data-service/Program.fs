namespace StockTrader.MarketDataService

open System.Net.Http
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Server.Kestrel.Https
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting

module Program =
    [<EntryPoint>]
    let main args =
        let settings = ServiceSettings.load()
        match ServiceSettings.validate settings with
        | first :: rest -> invalidOp (System.String.Join("; ", first :: rest))
        | [] -> ()

        let builder = WebApplication.CreateBuilder(args)
        builder.WebHost.ConfigureKestrel(fun options ->
            options.ConfigureHttpsDefaults(fun https ->
                https.ClientCertificateMode <- ClientCertificateMode.AllowCertificate
                https.ClientCertificateValidation <- fun _ _ _ -> true)) |> ignore
        builder.Services.AddSingleton(settings) |> ignore
        builder.Services.AddSingleton(BarStore(settings.DatabasePath)) |> ignore
        builder.Services.AddSingleton<HttpClient>() |> ignore
        builder.Services.AddSingleton<ProviderGateway>() |> ignore
        builder.Services.AddSingleton<SubscriptionState>() |> ignore
        builder.Services.AddSingleton<ServiceTelemetry>() |> ignore
        builder.Services.AddHostedService<AlpacaStreamingWorker>() |> ignore
        let app = builder.Build()
        let store = app.Services.GetRequiredService<BarStore>()
        store.InitializeAsync(System.Threading.CancellationToken.None).GetAwaiter().GetResult()
        HttpHost.map app |> ignore
        app.Run()
        0
