namespace StockTrader.MlTrainingService

open System
open System.Diagnostics
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open StockTrader.MlTrainingCompute
open StockTrader.ServiceContracts.MachineLearning

type TrainingWorker(store: JobStore, artifacts: ArtifactStore, config: ServiceConfig,
                    logger: ILogger<TrainingWorker>) =
    inherit BackgroundService()

    override _.ExecuteAsync(stoppingToken: CancellationToken) = task {
        for result in store.PublishedResults() do
            artifacts.Publish(result.JobId, result)
        while not stoppingToken.IsCancellationRequested do
            match store.Claim() with
            | None -> do! Task.Delay(config.PollMilliseconds, stoppingToken)
            | Some request ->
                let timer = Stopwatch.StartNew()
                try
                    let! compute = Task.Run((fun () -> MlTrainingComputeFacade.Train(request, stoppingToken)), stoppingToken)
                    if store.ShouldPublish(request.JobId) then
                        match Option.ofObj compute.RegimeArtifact with
                        | Some artifact -> artifacts.Publish(request.JobId, artifact)
                        | None -> ()
                        match Option.ofObj compute.SignalArtifact with
                        | Some artifact -> artifacts.Publish(request.JobId, artifact)
                        | None -> ()
                    let result = store.Complete(request, compute, timer.ElapsedMilliseconds)
                    logger.LogInformation("ML training job {JobId} finished as {Status}", request.JobId, result.Status)
                with
                | :? OperationCanceledException when stoppingToken.IsCancellationRequested -> ()
                | error ->
                    store.Fail(request.JobId, error.Message)
                    logger.LogError(error, "ML training job {JobId} failed", request.JobId)
    }
