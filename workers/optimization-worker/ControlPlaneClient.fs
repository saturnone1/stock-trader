module StockTrader.OptimizationWorker.ControlPlaneClient

open System
open System.Net
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading
open StockTrader.ServiceContracts.Optimization

type Client(http: HttpClient, baseUrl: string, workerId: string, secret: string) =
    let json = JsonSerializerOptions(JsonSerializerDefaults.Web)
    let endpoint = baseUrl.TrimEnd('/') + "/api/internal/optimization-worker"

    let request path payload =
        let message = new HttpRequestMessage(HttpMethod.Post, endpoint + path)
        message.Headers.Add(OptimizationWorkerHttpHeaders.WorkerId, workerId)
        message.Headers.Add(OptimizationWorkerHttpHeaders.Secret, secret)
        payload |> Option.iter (fun value ->
            message.Content <- new StringContent(value, Encoding.UTF8, "application/json"))
        message

    let send (message: HttpRequestMessage) (ct: CancellationToken) = task {
        use timeout = CancellationTokenSource.CreateLinkedTokenSource(ct)
        timeout.CancelAfter(TimeSpan.FromSeconds(30.0))
        return! http.SendAsync(message, timeout.Token)
    }

    member _.ClaimAsync(ct: CancellationToken) = task {
        use message = request "/leases/claim" None
        use! response = send message ct
        if response.StatusCode = HttpStatusCode.NoContent then return Ok None
        elif not response.IsSuccessStatusCode then return Error $"http-{int response.StatusCode}"
        else
            let! body = response.Content.ReadAsStringAsync(ct)
            return
                match JsonSerializer.Deserialize<OptimizationWorkLease>(body, json) |> Option.ofObj with
                | Some lease -> Ok (Some lease)
                | None -> Error "empty-lease"
    }

    member _.PostAsync(path: string, payload: obj, ct: CancellationToken) = task {
        use message = request path (Some (JsonSerializer.Serialize(payload, json)))
        use! response = send message ct
        let! body = response.Content.ReadAsStringAsync(ct)
        if not response.IsSuccessStatusCode then return Error $"http-{int response.StatusCode}"
        elif String.IsNullOrWhiteSpace(body) then return Error "empty-response"
        else return Ok body
    }

    member _.Json = json
