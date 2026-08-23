namespace StockTrader.MlTrainingService

open System
open System.Security.Cryptography.X509Certificates
open System.Text.Json
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Server.Kestrel.Https
open Microsoft.AspNetCore.Server.Kestrel.Core
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open StockTrader.ServiceContracts.MachineLearning

module HttpHost =
    let run (args: string array) =
        let config = Configuration.load()
        let json = JsonSerializerOptions(JsonSerializerDefaults.Web)
        let serverCertificate = X509Certificate2.CreateFromPemFile(
            config.ServerCertificatePath, config.ServerCertificateKeyPath)
        let builder = WebApplication.CreateBuilder(args)
        builder.WebHost.ConfigureKestrel(fun options ->
            options.ListenAnyIP(8080) |> ignore
            options.ListenAnyIP(8443, Action<ListenOptions>(fun listen ->
                listen.UseHttps(Action<HttpsConnectionAdapterOptions>(fun https ->
                        https.ServerCertificate <- serverCertificate
                        https.ClientCertificateMode <- ClientCertificateMode.RequireCertificate
                        https.ClientCertificateValidation <- fun certificate _ _ ->
                            use chain = new X509Chain()
                            chain.ChainPolicy.TrustMode <- X509ChainTrustMode.CustomRootTrust
                            chain.ChainPolicy.CustomTrustStore.Add(X509Certificate2.CreateFromPemFile(config.ClientCaPath)) |> ignore
                            chain.ChainPolicy.RevocationMode <- X509RevocationMode.NoCheck
                            chain.Build certificate)) |> ignore)) |> ignore) |> ignore
        builder.Services.AddSingleton config |> ignore
        builder.Services.AddSingleton json |> ignore
        builder.Services.AddSingleton<JobStore>(fun _ -> JobStore(config.DatabasePath, json)) |> ignore
        builder.Services.AddSingleton<ArtifactStore>(fun _ -> ArtifactStore(config.ArtifactDirectory, json)) |> ignore
        builder.Services.AddHostedService<TrainingWorker>() |> ignore
        let app = builder.Build()
        let authorized (ctx: HttpContext) =
            ctx.Connection.LocalPort = 8443
            && ctx.Request.Headers["X-StockTrader-Worker-Secret"].ToString() = config.SharedSecret
            && not (isNull ctx.Connection.ClientCertificate)
        app.MapGet("/health/live", Func<IResult>(fun () -> Results.Ok {| status="live" |})) |> ignore
        app.MapGet("/health/ready", Func<JobStore,IResult>(fun store -> Results.Ok(store.Status()))) |> ignore
        app.MapGet("/metrics", Func<JobStore,string>(fun store ->
            let status = store.Status()
            $"stocktrader_ml_training_pending {status.PendingJobs}\nstocktrader_ml_training_running {status.RunningJobs}\nstocktrader_ml_training_publication_revision {status.PublicationRevision}\n")) |> ignore
        app.MapGet("/v1/status", Func<HttpContext,JobStore,IResult>(fun ctx store ->
            if authorized ctx then Results.Ok(store.Status()) else Results.Unauthorized())) |> ignore
        app.MapPost("/v1/training/jobs", Func<HttpContext,JobStore,MlTrainingJobRequest,IResult>(fun ctx store request ->
            if not (authorized ctx) then Results.Unauthorized()
            else
                try Results.Accepted($"/v1/training/jobs/{request.JobId}", store.Accept request)
                with :? ArgumentException as error -> Results.BadRequest {| error=error.Message |})) |> ignore
        app.MapGet("/v1/training/jobs/{id}", Func<HttpContext,JobStore,string,IResult>(fun ctx store id ->
            if not (authorized ctx) then Results.Unauthorized()
            else match store.Get id with Some value -> Results.Ok value | None -> Results.NotFound())) |> ignore
        app.MapPost("/v1/training/jobs/{id}/cancel", Func<HttpContext,JobStore,string,IResult>(fun ctx store id ->
            if not (authorized ctx) then Results.Unauthorized()
            elif store.Cancel id then Results.Accepted() else Results.NotFound())) |> ignore
        app.Run()
        0
