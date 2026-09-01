namespace StockTrader.TradingCoreAcceptanceDriver

open System
open System.IO
open System.Net.Http
open System.Net.Http.Headers
open System.Net.Http.Json
open System.Security.Cryptography.X509Certificates
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open StockTrader.ServiceContracts
open StockTrader.ServiceContracts.MarketData
open StockTrader.ServiceContracts.TradingCore
open StockTrader.TradingCore.AcceptanceFixtures

module ScenarioDriver =
    let private required name =
        match Environment.GetEnvironmentVariable name with
        | null | "" -> failwith $"Missing required setting: {name}"
        | value -> value

    let private client endpoint expectedName =
        let handler = new HttpClientHandler()
        handler.ClientCertificates.Add(X509Certificate2.CreateFromPemFile(
            required "STOCKTRADER_ACCEPTANCE_DRIVER_CERT_PATH",
            required "STOCKTRADER_ACCEPTANCE_DRIVER_KEY_PATH")) |> ignore
        handler.ServerCertificateCustomValidationCallback <- fun _ certificate _ _ ->
            if isNull certificate then false else
            use root = X509Certificate2.CreateFromPemFile(
                required "STOCKTRADER_ACCEPTANCE_SERVER_CA_PATH")
            use chain = new X509Chain()
            chain.ChainPolicy.TrustMode <- X509ChainTrustMode.CustomRootTrust
            chain.ChainPolicy.CustomTrustStore.Add root |> ignore
            chain.ChainPolicy.RevocationMode <- X509RevocationMode.NoCheck
            certificate.GetNameInfo(X509NameType.DnsName, false) = expectedName
            && chain.Build certificate
        let value = new HttpClient(handler, true)
        value.BaseAddress <- Uri endpoint
        value

    let private hashBody (body: string) =
        if String.IsNullOrWhiteSpace body then CanonicalJsonHash.Compute("")
        else
            try
                use document = JsonDocument.Parse body
                CanonicalJsonHash.Compute document.RootElement
            with :? JsonException -> CanonicalJsonHash.Compute body

    let private resolvePointer (root: JsonElement) (pointer: string) =
        let mutable current = root
        for raw in pointer.Split('/', StringSplitOptions.RemoveEmptyEntries) do
            let segment = raw.Replace("~1", "/").Replace("~0", "~")
            if current.ValueKind = JsonValueKind.Object then
                match current.TryGetProperty segment with
                | true, value -> current <- value
                | _ -> invalidOp $"acceptance-assertion-path-missing:{pointer}"
            elif current.ValueKind = JsonValueKind.Array then
                match Int32.TryParse segment with
                | true, index when index >= 0 && index < current.GetArrayLength() ->
                    current <- current[index]
                | _ -> invalidOp $"acceptance-assertion-index-invalid:{pointer}"
            else invalidOp $"acceptance-assertion-path-invalid:{pointer}"
        current

    let private send (http: HttpClient) (operation: AcceptanceDriverOperation)
        (ct: CancellationToken) = task {
        let mutable attempt = 0
        let mutable complete = false
        let mutable responseHash = ""
        while not complete && attempt < operation.MaxAttempts do
            attempt <- attempt + 1
            try
                use request = new HttpRequestMessage(HttpMethod(operation.Method), operation.Path)
                if not (String.IsNullOrWhiteSpace operation.BodyJson) then
                    request.Content <- new StringContent(operation.BodyJson, Encoding.UTF8, "application/json")
                use! response = http.SendAsync(request, ct)
                let! body = response.Content.ReadAsStringAsync ct
                responseHash <- hashBody body
                complete <- int response.StatusCode = operation.ExpectedStatus
                    && (String.IsNullOrWhiteSpace operation.ExpectedResponseHash
                        || responseHash = operation.ExpectedResponseHash)
            with
            | :? HttpRequestException -> ()
            | :? IO.IOException -> ()
            if not complete && attempt < operation.MaxAttempts then
                do! Task.Delay(TimeSpan.FromMilliseconds 100.0, ct)
        if not complete then invalidOp $"acceptance-operation-mismatch:{operation.OperationId}"
        return responseHash
    }

    let private getStringWithRetry (http: HttpClient) (path: string) (ct: CancellationToken) = task {
        let mutable attempt = 0
        let mutable value: string = null
        while isNull value && attempt < 100 do
            attempt <- attempt + 1
            try
                use! response = http.GetAsync(path, ct)
                if response.IsSuccessStatusCode then
                    let! body = response.Content.ReadAsStringAsync ct
                    value <- body
            with
            | :? HttpRequestException -> ()
            | :? IO.IOException -> ()
            if isNull value then do! Task.Delay(TimeSpan.FromMilliseconds 100.0, ct)
        if isNull value then invalidOp $"acceptance-final-state-unavailable:{path}"
        return value
    }

    let private compileFixture (control: HttpClient) (json: JsonSerializerOptions)
        (ct: CancellationToken) =
        let definitionPath = required "STOCKTRADER_ACCEPTANCE_DEFINITION_PATH"
        let definition = JsonSerializer.Deserialize<AcceptanceScenarioDefinition>(
            File.ReadAllText definitionPath, json)
        match Option.ofObj (TradingCoreAcceptancePolicy.DefinitionError definition) with
        | Some error -> invalidOp error
        | None -> ()
        let mutable candidate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1.0))
        let mutable response: MarketDataExecutionWindowResponse = null
        let mutable attempts = 0
        while isNull response && attempts < 21 do
            if candidate.DayOfWeek <> DayOfWeek.Saturday
               && candidate.DayOfWeek <> DayOfWeek.Sunday then
                let request = AcceptanceScenarioCompiler.EvidenceRequest(definition, candidate)
                use result =
                    (control.PostAsJsonAsync(
                        "/internal/acceptance/market-data/latest-completed", request, json, ct))
                        .GetAwaiter().GetResult()
                if result.IsSuccessStatusCode then
                    let value =
                        (result.Content.ReadFromJsonAsync<MarketDataExecutionWindowResponse>(json, ct))
                            .GetAwaiter().GetResult()
                    if not (isNull value) && value.Evidence.IsComplete then response <- value
            attempts <- attempts + 1
            candidate <- candidate.AddDays(-1)
        if isNull response then invalidOp "acceptance-completed-market-data-evidence-unavailable"
        AcceptanceScenarioCompiler.Compile(definition, response)

    let private deleteCorePod (operation: AcceptanceDriverOperation)
        (ct: CancellationToken) = task {
        let namespaceName = required "STOCKTRADER_ACCEPTANCE_NAMESPACE"
        let selector = required "STOCKTRADER_ACCEPTANCE_CORE_POD_SELECTOR"
        let token = File.ReadAllText("/var/run/secrets/kubernetes.io/serviceaccount/token")
        let handler = new HttpClientHandler()
        handler.ServerCertificateCustomValidationCallback <- fun _ certificate _ _ ->
            if isNull certificate then false else
            use root = X509Certificate2.CreateFromPemFile(
                "/var/run/secrets/kubernetes.io/serviceaccount/ca.crt")
            use chain = new X509Chain()
            chain.ChainPolicy.TrustMode <- X509ChainTrustMode.CustomRootTrust
            chain.ChainPolicy.CustomTrustStore.Add root |> ignore
            chain.ChainPolicy.RevocationMode <- X509RevocationMode.NoCheck
            chain.Build certificate
        use kube = new HttpClient(handler, true)
        kube.DefaultRequestHeaders.Authorization <- AuthenticationHeaderValue("Bearer", token)
        let host = required "KUBERNETES_SERVICE_HOST"
        let port = required "KUBERNETES_SERVICE_PORT_HTTPS"
        let root = $"https://{host}:{port}/api/v1/namespaces/{namespaceName}/pods"
        use! listed = kube.GetAsync($"{root}?labelSelector={Uri.EscapeDataString selector}", ct)
        listed.EnsureSuccessStatusCode() |> ignore
        use! document = JsonDocument.ParseAsync(listed.Content.ReadAsStream(ct), cancellationToken = ct)
        let items = document.RootElement.GetProperty("items").EnumerateArray() |> Seq.toArray
        if items.Length <> 1 then invalidOp "acceptance-core-pod-cardinality"
        let metadata = items[0].GetProperty("metadata")
        let name = metadata.GetProperty("name").GetString()
        let uid = metadata.GetProperty("uid").GetString()
        let payload = JsonSerializer.Serialize {| apiVersion = "v1"; kind = "DeleteOptions"
                                                  preconditions = {| uid = uid |} |}
        use request = new HttpRequestMessage(HttpMethod.Delete, $"{root}/{name}")
        request.Content <- new StringContent(payload, Encoding.UTF8, "application/json")
        use! response = kube.SendAsync(request, ct)
        if int response.StatusCode <> operation.ExpectedStatus then
            invalidOp $"acceptance-pod-delete-mismatch:{int response.StatusCode}"
        return CanonicalJsonHash.Compute {| name = name; uid = uid |}
    }

    let run () =
        let fragmentPath = required "STOCKTRADER_ACCEPTANCE_RESULT_PATH"
        let json = JsonSerializerOptions(JsonSerializerDefaults.Web)
        json.WriteIndented <- true
        use core = client (required "STOCKTRADER_ACCEPTANCE_CORE_ENDPOINT")
                           (required "STOCKTRADER_ACCEPTANCE_CORE_SERVER_NAME")
        use control = client (required "STOCKTRADER_ACCEPTANCE_CONTROL_ENDPOINT")
                              (required "STOCKTRADER_ACCEPTANCE_CORE_SERVER_NAME")
        use broker = client (required "STOCKTRADER_ACCEPTANCE_BROKER_ENDPOINT")
                             (required "STOCKTRADER_ACCEPTANCE_BROKER_SERVER_NAME")
        use cancellation = new CancellationTokenSource(TimeSpan.FromMinutes 10.0)
        let fixture =
            match Environment.GetEnvironmentVariable "STOCKTRADER_ACCEPTANCE_FIXTURE_PATH" with
            | null | "" -> compileFixture control json cancellation.Token
            | fixturePath -> JsonSerializer.Deserialize<AcceptanceScenarioFixture>(
                                 File.ReadAllText fixturePath, json)
        match Option.ofObj (TradingCoreAcceptancePolicy.FixtureError fixture) with
        | Some error -> invalidOp error
        | None -> ()
        let fixtureArchive = Path.Combine(Path.GetDirectoryName fragmentPath, "..", "fixtures",
                                          fixture.BrokerPlan.ScenarioCode + ".fixture.json")
        Directory.CreateDirectory(Path.GetDirectoryName fixtureArchive) |> ignore
        File.WriteAllText(fixtureArchive, JsonSerializer.Serialize(fixture, json))
        let evidence = ResizeArray<string>()
        let mutable failure: string = null
        let started = DateTime.UtcNow
        try
            send control (AcceptanceDriverOperation("set-initial-time",
                AcceptanceDriverTargets.AcceptanceControl, "POST", "/internal/acceptance/time",
                JsonSerializer.Serialize(AcceptanceTimeAdvanceRequest(
                    fixture.BrokerPlan.ScenarioId, "set-initial-time-" + fixture.BrokerPlan.ScenarioId,
                    fixture.BrokerPlan.VirtualStartUtc, "fixture-compile"), json),
                200, null, 1)) cancellation.Token |> _.GetAwaiter().GetResult() |> ignore
            send broker (AcceptanceDriverOperation("load-plan", AcceptanceDriverTargets.BrokerControl,
                "POST", "/control/plan", JsonSerializer.Serialize(fixture.BrokerPlan, json),
                200, null, 1)) cancellation.Token |> _.GetAwaiter().GetResult() |> ignore
            send control (AcceptanceDriverOperation("bootstrap", AcceptanceDriverTargets.AcceptanceControl,
                "POST", "/internal/acceptance/bootstrap", JsonSerializer.Serialize(fixture.Bootstrap, json),
                200, null, 1)) cancellation.Token |> _.GetAwaiter().GetResult() |> ignore
            send control (AcceptanceDriverOperation("start", AcceptanceDriverTargets.AcceptanceControl,
                "POST", "/internal/acceptance/start",
                JsonSerializer.Serialize(AcceptanceScenarioStartRequest(
                    fixture.BrokerPlan.ScenarioId, "start-" + fixture.BrokerPlan.ScenarioId), json),
                200, null, 1)) cancellation.Token |> _.GetAwaiter().GetResult() |> ignore
            for operation in fixture.Operations do
                let hash =
                    if operation.Target = AcceptanceDriverTargets.DeleteTradingCorePod then
                        deleteCorePod operation cancellation.Token |> _.GetAwaiter().GetResult()
                    else
                        let target =
                            if operation.Target = AcceptanceDriverTargets.TradingCore then core
                            elif operation.Target = AcceptanceDriverTargets.AcceptanceControl then control
                            else broker
                        send target operation cancellation.Token |> _.GetAwaiter().GetResult()
                evidence.Add($"{operation.OperationId}:{hash}")
        with error -> failure <- error.Message
        let coreState = getStringWithRetry core "/v1/portfolio" cancellation.Token
                            |> _.GetAwaiter().GetResult()
        let brokerState = getStringWithRetry broker "/control/state" cancellation.Token
                              |> _.GetAwaiter().GetResult()
        use coreDocument = JsonDocument.Parse coreState
        use brokerDocument = JsonDocument.Parse brokerState
        let observations =
            fixture.Assertions
            |> Seq.sortBy _.Name
            |> Seq.map (fun assertion ->
                let root =
                    if assertion.Target = AcceptanceDriverTargets.TradingCore then
                        coreDocument.RootElement
                    else brokerDocument.RootElement
                let value = resolvePointer root assertion.JsonPointer
                AcceptanceAssertionObservation(assertion.Name, assertion.Target,
                    assertion.JsonPointer, CanonicalJsonHash.Compute value))
            |> Seq.toArray
        let actualStateHash = CanonicalJsonHash.Compute observations
        let expectedFailure = fixture.ExpectedStopReason
        let passed =
            (actualStateHash = fixture.ExpectedStateHash)
            && ((String.IsNullOrWhiteSpace expectedFailure && isNull failure)
                || (failure = expectedFailure))
        let result = AcceptanceScenarioResult(fixture.BrokerPlan.ScenarioId,
            fixture.BrokerPlan.ScenarioCode, fixture.FixtureHash,
            fixture.ExpectedStateHash, actualStateHash, evidence.ToArray(),
            started, DateTime.UtcNow, passed,
            if passed then null elif isNull failure then "state-hash-mismatch" else failure)
        Directory.CreateDirectory(Path.GetDirectoryName fragmentPath) |> ignore
        let temporary = fragmentPath + ".tmp"
        File.WriteAllText(temporary, JsonSerializer.Serialize(result, json))
        File.Move(temporary, fragmentPath, true)
        if passed then 0 else 2
