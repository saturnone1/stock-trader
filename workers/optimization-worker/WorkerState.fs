module StockTrader.OptimizationWorker.WorkerState

open System.Threading

type ProbeSnapshot =
    { Configured: bool
      Connected: bool
      Attempts: int64
      Successes: int64
      Leases: int64
      Heartbeats: int64
      Results: int64
      LastError: string }

type ProbeState() =
    let mutable configured = 0
    let mutable connected = 0
    let mutable attempts = 0L
    let mutable successes = 0L
    let mutable leases = 0L
    let mutable heartbeats = 0L
    let mutable results = 0L
    let mutable lastError = "not-configured"

    member _.Configure() = Interlocked.Exchange(&configured, 1) |> ignore
    member _.Attempt() = Interlocked.Increment(&attempts) |> ignore
    member _.Succeed() =
        Interlocked.Exchange(&connected, 1) |> ignore
        Interlocked.Increment(&successes) |> ignore
        Volatile.Write(&lastError, "")
    member _.Lease() = Interlocked.Increment(&leases) |> ignore
    member _.Heartbeat() = Interlocked.Increment(&heartbeats) |> ignore
    member _.Result() = Interlocked.Increment(&results) |> ignore
    member _.Fail(error: string) =
        Interlocked.Exchange(&connected, 0) |> ignore
        Volatile.Write(&lastError, error)
    member _.Snapshot() =
        { Configured = Volatile.Read(&configured) = 1
          Connected = Volatile.Read(&connected) = 1
          Attempts = Interlocked.Read(&attempts)
          Successes = Interlocked.Read(&successes)
          Leases = Interlocked.Read(&leases)
          Heartbeats = Interlocked.Read(&heartbeats)
          Results = Interlocked.Read(&results)
          LastError = Volatile.Read(&lastError) }
