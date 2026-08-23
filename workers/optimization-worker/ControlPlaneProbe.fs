module StockTrader.OptimizationWorker.ControlPlaneProbe

open System
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open StockTrader.ServiceContracts.Optimization

type ProbeSnapshot =
    { Configured: bool
      Connected: bool
      Attempts: int64
      Successes: int64
      LastError: string }

type ProbeState() =
    let mutable configured = 0
    let mutable connected = 0
    let mutable attempts = 0L
    let mutable successes = 0L
    let mutable lastError = "not-configured"

    member _.Configure() = Interlocked.Exchange(&configured, 1) |> ignore
    member _.Attempt() = Interlocked.Increment(&attempts) |> ignore
    member _.Succeed() =
        Interlocked.Exchange(&connected, 1) |> ignore
        Interlocked.Increment(&successes) |> ignore
        Volatile.Write(&lastError, "")
    member _.Fail(error: string) =
        Interlocked.Exchange(&connected, 0) |> ignore
        Volatile.Write(&lastError, error)
    member _.Snapshot() =
        { Configured = Volatile.Read(&configured) = 1
          Connected = Volatile.Read(&connected) = 1
          Attempts = Interlocked.Read(&attempts)
          Successes = Interlocked.Read(&successes)
          LastError = Volatile.Read(&lastError) }

type Worker(
    client: HttpClient,
    configuration: IConfiguration,
    state: ProbeState,
    logger: ILogger<Worker>) =
    inherit BackgroundService()

    override _.ExecuteAsync(ct: CancellationToken) = task {
        let baseUrl = configuration["STOCKTRADER_CONTROL_API_URL"]
        let workerId = configuration["STOCKTRADER_WORKER_ID"]
        let secret = configuration["STOCKTRADER_WORKER_SECRET"]
        if String.IsNullOrWhiteSpace(baseUrl)
           || String.IsNullOrWhiteSpace(workerId)
           || String.IsNullOrWhiteSpace(secret) then
            logger.LogInformation("Control API probe is not configured; shadow host remains isolated")
        else
            state.Configure()
            let endpoint =
                (baseUrl |> Option.ofObj |> Option.defaultValue "").TrimEnd('/')
                + "/api/internal/optimization-worker/status"
            while not ct.IsCancellationRequested do
                state.Attempt()
                try
                    use request = new HttpRequestMessage(HttpMethod.Get, endpoint)
                    request.Headers.Add(OptimizationWorkerHttpHeaders.WorkerId, workerId)
                    request.Headers.Add(OptimizationWorkerHttpHeaders.Secret, secret)
                    use attemptTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct)
                    attemptTimeout.CancelAfter(TimeSpan.FromSeconds(5.0))
                    use! response = client.SendAsync(request, attemptTimeout.Token)
                    if response.IsSuccessStatusCode then
                        state.Succeed()
                        logger.LogDebug("Control API shadow handshake succeeded")
                    else
                        let failure = $"http-{int response.StatusCode}"
                        state.Fail(failure)
                        logger.LogWarning("Control API shadow handshake failed: {Failure}", failure)
                with
                | :? OperationCanceledException when ct.IsCancellationRequested -> ()
                | error ->
                    let failure = error.GetType().Name
                    state.Fail(failure)
                    logger.LogWarning("Control API shadow handshake failed: {Failure}", failure)
                if not ct.IsCancellationRequested then
                    do! Task.Delay(30_000, ct)
    }
