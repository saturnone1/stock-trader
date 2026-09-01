namespace StockTrader.TradingCoreCutoverCoordinator

open System
open System.IO
open System.Net.Http
open System.Net.Http.Headers
open System.Net.Http.Json
open System.Security.Cryptography
open System.Security.Cryptography.X509Certificates
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open StockTrader.ServiceContracts
open StockTrader.ServiceContracts.TradingCore

module Coordinator =
    let private required name =
        match Environment.GetEnvironmentVariable name with
        | null | "" -> failwith $"Missing required setting: {name}"
        | value -> value

    let private operationId transition step =
        let hash = SHA256.HashData(Encoding.UTF8.GetBytes(transition + ":" + step))
        Guid(ReadOnlySpan<byte>(hash, 0, 16)).ToString()

    let private operation (plan: TradingCoreTransitionPlanV1) (step: string) (ordinal: int) =
        TradingControlOperation(TradingControlContractVersions.Current,
            operationId plan.TransitionId step, "", plan.TransitionId, null,
            plan.StartedAtUtc.AddSeconds(float ordinal))

    let private client () =
        let handler = new HttpClientHandler()
        handler.ClientCertificates.Add(X509Certificate2.CreateFromPemFile(
            required "STOCKTRADER_COORDINATOR_CLIENT_CERT_PATH",
            required "STOCKTRADER_COORDINATOR_CLIENT_KEY_PATH")) |> ignore
        handler.ServerCertificateCustomValidationCallback <- fun _ certificate _ _ ->
            if isNull certificate then false else
            use root = X509Certificate2.CreateFromPemFile(required "STOCKTRADER_COORDINATOR_SERVER_CA_PATH")
            use chain = new X509Chain()
            chain.ChainPolicy.TrustMode <- X509ChainTrustMode.CustomRootTrust
            chain.ChainPolicy.CustomTrustStore.Add root |> ignore
            chain.ChainPolicy.RevocationMode <- X509RevocationMode.NoCheck
            chain.Build certificate
        new HttpClient(handler, true)

    let private get<'T> (client: HttpClient) (uri: Uri) (ct: CancellationToken) = task {
        let! response = client.GetAsync(uri, ct)
        response.EnsureSuccessStatusCode() |> ignore
        return! response.Content.ReadFromJsonAsync<'T>(cancellationToken = ct)
    }

    let private post<'TRequest,'TResponse> (client: HttpClient) (uri: Uri)
        (request: 'TRequest) (ct: CancellationToken) = task {
        let! response = client.PostAsJsonAsync<'TRequest>(uri, request, ct)
        response.EnsureSuccessStatusCode() |> ignore
        return! response.Content.ReadFromJsonAsync<'TResponse>(cancellationToken = ct)
    }

    let private postNoContent<'TRequest> (client: HttpClient) (uri: Uri)
        (request: 'TRequest) (ct: CancellationToken) = task {
        let! response = client.PostAsJsonAsync<'TRequest>(uri, request, ct)
        response.EnsureSuccessStatusCode() |> ignore
    }

    let private uri (root: Uri) (path: string) = Uri(root, path)

    let private createRequest (plan: TradingCoreTransitionPlanV1) =
        let op = operation plan "create" 0
        let candidate = AuthorityTransitionRequest(op, plan.TransitionId, plan.Direction,
            plan.SourceMode, plan.TargetMode, plan.SourceGeneration, plan.AccountGeneration,
            plan.StartedAtUtc, plan.ExpiresAtUtc)
        let sealedOp = TradingControlOperation(op.ContractVersion, op.OperationId,
            TradingControlIdentity.Transition(candidate), op.CorrelationId,
            op.CausationId, op.ObservedAtUtc)
        AuthorityTransitionRequest(sealedOp, candidate.TransitionId, candidate.Direction,
            candidate.SourceMode, candidate.TargetMode, candidate.SourceGeneration,
            candidate.AccountGeneration, candidate.StartedAtUtc, candidate.ExpiresAtUtc)

    let private fenceRequest (plan: TradingCoreTransitionPlanV1) (step: string) (ordinal: int) =
        let op = operation plan step ordinal
        let candidate = EdgeAuthorityFenceRequest(op, plan.TransitionId, plan.SourceGeneration)
        let hash = CanonicalJsonHash.Compute(candidate, "payloadHash")
        let sealedOp = TradingControlOperation(op.ContractVersion, op.OperationId, hash,
            op.CorrelationId, op.CausationId, op.ObservedAtUtc)
        EdgeAuthorityFenceRequest(sealedOp, candidate.TransitionId, candidate.AuthorityGeneration)

    let private stepRequest (plan: TradingCoreTransitionPlanV1) (step: string)
        (expected: string) (ordinal: int) (sourceFence: AuthorityFenceReceipt)
        (targetFence: AuthorityFenceReceipt) (drain: AuthorityDrainInventory)
        (reconciliation: AuthorityReconciliationEvidence)
        (sourceCapability: AuthorityCapabilityReceipt)
        (targetCapability: AuthorityCapabilityReceipt) =
        let op = operation plan step ordinal
        let candidate = AuthorityTransitionStepRequest(op, plan.TransitionId, step, expected,
            sourceFence, targetFence, drain, reconciliation, sourceCapability,
            targetCapability, Array.empty)
        let sealedOp = TradingControlOperation(op.ContractVersion, op.OperationId,
            TradingControlIdentity.Step(candidate), op.CorrelationId,
            op.CausationId, op.ObservedAtUtc)
        AuthorityTransitionStepRequest(sealedOp, candidate.TransitionId, candidate.Step,
            candidate.ExpectedPhase, candidate.SourceFence, candidate.TargetFence,
            candidate.DrainInventory, candidate.Reconciliation,
            candidate.SourceCapability, candidate.TargetCapability,
            candidate.EvidenceReferences)

    let private exportRequest (plan: TradingCoreTransitionPlanV1) =
        let op = operation plan "financial-export" 5
        let transferId = operationId plan.TransitionId "financial-transfer"
        let candidate = CanonicalFinancialExportRequest(op, transferId,
            plan.TransitionId, plan.Direction, plan.SourceMode,
            plan.SourceGeneration, plan.SourceGeneration + 1L,
            plan.TransferCompatibility, plan.EquityBasis)
        let hash = CanonicalJsonHash.Compute(candidate, "payloadHash")
        CanonicalFinancialExportRequest(
            TradingControlOperation(op.ContractVersion, op.OperationId, hash,
                op.CorrelationId, op.CausationId, op.ObservedAtUtc),
            candidate.TransferId, candidate.TransitionId, candidate.Direction,
            candidate.SourceMode, candidate.SourceGeneration,
            candidate.ReservedTargetGeneration, candidate.Compatibility,
            candidate.EquityBasis)

    let private mirrorRequest (plan: TradingCoreTransitionPlanV1) step ordinal
        generation mode owner receiptHash =
        let op = operation plan step ordinal
        let candidate = EdgeAuthorityMirrorRequest(op, plan.TransitionId,
            generation, mode, owner, receiptHash)
        let sealedOp = TradingControlOperation(op.ContractVersion, op.OperationId,
            TradingControlIdentity.EdgeMirror(candidate), op.CorrelationId,
            op.CausationId, op.ObservedAtUtc)
        EdgeAuthorityMirrorRequest(sealedOp, candidate.TransitionId,
            candidate.AuthorityGeneration, candidate.Mode, candidate.Owner,
            candidate.AuthorityReceiptHash)

    let private patchDeployment (target: TradingCoreDeploymentTarget) (name: string)
        (container: string) (image: string) (environment: obj array)
        (replicas: Nullable<int>)
        (ct: CancellationToken) = task {
        let token = File.ReadAllText("/var/run/secrets/kubernetes.io/serviceaccount/token")
        let host = required "KUBERNETES_SERVICE_HOST"
        let port = required "KUBERNETES_SERVICE_PORT_HTTPS"
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
        use client = new HttpClient(handler, true)
        client.DefaultRequestHeaders.Authorization <- AuthenticationHeaderValue("Bearer", token)
        let endpoint = $"https://{host}:{port}/apis/apps/v1/namespaces/{target.Namespace}/deployments/{name}"
        let payload =
            if replicas.HasValue then
                JsonSerializer.Serialize {| spec = {| replicas = replicas.Value; template = {| spec = {| containers = [| {| name = container; image = image; env = environment |} |] |} |} |} |}
            else
                JsonSerializer.Serialize {| spec = {| template = {| spec = {| containers = [| {| name = container; image = image; env = environment |} |] |} |} |} |}
        use content = new StringContent(payload, Encoding.UTF8, "application/strategic-merge-patch+json")
        use request = new HttpRequestMessage(HttpMethod.Patch, endpoint, Content = content)
        use! response = client.SendAsync(request, ct)
        response.EnsureSuccessStatusCode() |> ignore
        use! patched = JsonDocument.ParseAsync(response.Content.ReadAsStream(ct), cancellationToken = ct)
        let generation = patched.RootElement.GetProperty("metadata").GetProperty("generation").GetInt64()
        let deadline = DateTime.UtcNow.AddMinutes 3.0
        let mutable ready = false
        while not ready && DateTime.UtcNow < deadline do
            use! currentResponse = client.GetAsync(endpoint, ct)
            currentResponse.EnsureSuccessStatusCode() |> ignore
            use! current = JsonDocument.ParseAsync(currentResponse.Content.ReadAsStream(ct), cancellationToken = ct)
            let root = current.RootElement
            let desired =
                match root.GetProperty("spec").TryGetProperty("replicas") with
                | true, value -> value.GetInt32()
                | _ -> 1
            let status = root.GetProperty("status")
            let intProperty (name: string) =
                match status.TryGetProperty(name) with
                | true, value -> value.GetInt32()
                | _ -> 0
            let observed =
                match status.TryGetProperty("observedGeneration") with
                | true, value -> value.GetInt64()
                | _ -> 0L
            ready <- observed >= generation
                && intProperty "updatedReplicas" = desired
                && intProperty "availableReplicas" = desired
            if not ready then do! Task.Delay(TimeSpan.FromSeconds 1.0, ct)
        if not ready then invalidOp $"deployment-rollout-timeout:{name}"
    }

    let private envValue name value : obj = box {| name = name; value = value |}
    let private envSecret name secret keyName : obj = box {| name = name; valueFrom = {| secretKeyRef = {| name = secret; key = keyName |} |} |}
    let private envDelete name : obj =
        let value = Collections.Generic.Dictionary<string,obj>(StringComparer.Ordinal)
        value["name"] <- name
        value["$patch"] <- "delete"
        box value

    let private edgeEnvironment (plan: TradingCoreTransitionPlanV1) =
        if plan.TargetMode = TradingAuthorityMode.Remote then
            [| envValue "TradingCoreTransport__Mode" "Remote"
               envValue "AuthorityCapabilityAttestation__RuntimeProfile" "api-remote"
               envValue "AuthorityCapabilityAttestation__HasBrokerEgress" "false"
               envDelete "ALPACA__APIKEY"
               envDelete "ALPACA__APISECRET" |]
        else
            [| envValue "TradingCoreTransport__Mode" (plan.TargetMode.ToString())
               envValue "AuthorityCapabilityAttestation__RuntimeProfile" "api-local"
               envValue "AuthorityCapabilityAttestation__HasBrokerEgress" "true"
               envSecret "ALPACA__APIKEY" plan.Deployments.BrokerSecretName "api-key"
               envSecret "ALPACA__APISECRET" plan.Deployments.BrokerSecretName "api-secret" |]

    let private coreEnvironment (plan: TradingCoreTransitionPlanV1) =
        let enabled = plan.TargetMode = TradingAuthorityMode.Remote
        [| envValue "STOCKTRADER_TRADING_CORE_MODE" (plan.TargetMode.ToString())
           envValue "STOCKTRADER_TRADING_CORE_RUNTIME_PROFILE"
               (if enabled then "trading-core-remote" else "trading-core-shadow")
           envValue "STOCKTRADER_BROKER_CAPABILITY_ENABLED"
               (if enabled then "true" else "false")
           envValue "STOCKTRADER_BROKER_EGRESS_ENABLED"
               (if enabled then "true" else "false") |]

    let private runRollbackImporterJob (plan: TradingCoreTransitionPlanV1)
        (ct: CancellationToken) = task {
        let target = plan.Rollback
        if isNull target then invalidOp "rollback-target-missing"
        let token = File.ReadAllText("/var/run/secrets/kubernetes.io/serviceaccount/token")
        let host = required "KUBERNETES_SERVICE_HOST"
        let port = required "KUBERNETES_SERVICE_PORT_HTTPS"
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
        let endpoint = $"https://{host}:{port}/apis/batch/v1/namespaces/{plan.Deployments.Namespace}/jobs/{target.ImportJobName}"
        use content = new StringContent("{\"spec\":{\"suspend\":false}}", Encoding.UTF8,
                                        "application/strategic-merge-patch+json")
        use request = new HttpRequestMessage(HttpMethod.Patch, endpoint, Content = content)
        use! response = kube.SendAsync(request, ct)
        response.EnsureSuccessStatusCode() |> ignore
        let deadline = DateTime.UtcNow.AddMinutes 5.0
        let mutable complete = false
        while not complete && DateTime.UtcNow < deadline do
            use! currentResponse = kube.GetAsync(endpoint, ct)
            currentResponse.EnsureSuccessStatusCode() |> ignore
            use! current = JsonDocument.ParseAsync(
                currentResponse.Content.ReadAsStream(ct), cancellationToken = ct)
            match current.RootElement.TryGetProperty("status") with
            | true, status ->
                match status.TryGetProperty("conditions") with
                | true, conditions ->
                    for condition in conditions.EnumerateArray() do
                        let kind = condition.GetProperty("type").GetString()
                        let truth = condition.GetProperty("status").GetString() = "True"
                        if truth && kind = "Failed" then
                            invalidOp "rollback-importer-job-failed"
                        if truth && kind = "Complete" then complete <- true
                | _ -> ()
            | _ -> ()
            if not complete then do! Task.Delay(TimeSpan.FromSeconds 1.0, ct)
        if not complete then invalidOp "rollback-importer-job-timeout"
        let json = JsonSerializerOptions(JsonSerializerDefaults.Web)
        let receipt = JsonSerializer.Deserialize<CanonicalFinancialImportReceipt>(
            File.ReadAllText target.ImportReceiptPath, json)
        if isNull receipt then invalidOp "empty-financial-import-receipt"
        return receipt
    }

    let private writeState (path: string) (plan: TradingCoreTransitionPlanV1)
        (step: string) (receipt: string) =
        let state = TradingCoreCoordinatorState(TradingCoreCoordinatorVersions.Current,
            plan.PlanHash, plan.TransitionId, step, null, receipt, DateTime.UtcNow)
        let temporary = path + ".tmp"
        File.WriteAllText(temporary, JsonSerializer.Serialize state)
        File.Move(temporary, path, true)

    let private waitForCoreDrain (http: HttpClient) (endpoint: Uri)
        (ct: CancellationToken) = task {
        let deadline = DateTime.UtcNow.AddMinutes 3.0
        let mutable result: AuthorityDrainInventory = null
        while isNull result && DateTime.UtcNow < deadline do
            let! candidate = get<AuthorityDrainInventory> http endpoint ct
            if candidate.UnresolvedIntentCount = 0
                && candidate.UnresolvedBrokerEffectCount = 0
                && candidate.UnprocessedBrokerFillCount = 0
                && candidate.EnabledConsumerLag = 0L then
                result <- candidate
            else
                do! Task.Delay(TimeSpan.FromSeconds 1.0, ct)
        if isNull result then invalidOp "trading-core-drain-timeout"
        return result
    }

    let private runRollback (plan: TradingCoreTransitionPlanV1)
        (statePath: string) (json: JsonSerializerOptions) (ct: CancellationToken) = task {
        use http = client ()
        let core path = uri plan.TradingCoreControlEndpoint path
        let edge path = uri plan.EdgeControlEndpoint path
        let! _ = post<AuthorityTransitionRequest,AuthorityTransitionReceipt> http
                     (core "/v2/authority/transitions") (createRequest plan) ct
        let! sourceFence = get<AuthorityFenceReceipt> http (core "/v2/authority/fence") ct
        let! targetFence = post<EdgeAuthorityFenceRequest,AuthorityFenceReceipt> http
                               (edge "/internal/v2/edge-authority/fence")
                               (fenceRequest plan "edge-rollback-fence" 1) ct
        let! quiesced = post<AuthorityTransitionStepRequest,AuthorityTransitionReceipt> http
                            (core $"/v2/authority/transitions/{plan.TransitionId}/steps")
                            (stepRequest plan AuthorityTransitionOperations.Quiesce
                                AuthorityTransitionPhases.Requested 2 sourceFence targetFence
                                null null null null) ct
        writeState statePath plan quiesced.Phase quiesced.PayloadHash
        let! drain = waitForCoreDrain http
                         (core $"/v2/authority/transitions/{plan.TransitionId}/drain") ct
        let! drained = post<AuthorityTransitionStepRequest,AuthorityTransitionReceipt> http
                           (core $"/v2/authority/transitions/{plan.TransitionId}/steps")
                           (stepRequest plan AuthorityTransitionOperations.Drain
                               AuthorityTransitionPhases.Quiescing 3 sourceFence targetFence
                               drain null null null) ct
        writeState statePath plan drained.Phase drained.PayloadHash
        let! transfer = post<CanonicalFinancialExportRequest,CanonicalFinancialTransferV2> http
                            (core "/v2/financial-transfers/export") (exportRequest plan) ct
        match Option.ofObj (CanonicalFinancialTransferPolicy.Error transfer) with
        | Some error -> invalidOp error | None -> ()
        if not (String.IsNullOrWhiteSpace plan.ExpectedTransferHash)
            && transfer.TransferHash <> plan.ExpectedTransferHash then
            invalidOp "snapshot-hash-mismatch"
        let temporaryTransfer = plan.SealedTransferPath + ".tmp"
        File.WriteAllText(temporaryTransfer, JsonSerializer.Serialize(transfer, json))
        File.Move(temporaryTransfer, plan.SealedTransferPath, true)
        do! patchDeployment plan.Deployments plan.Deployments.EdgeDeployment
                plan.Deployments.EdgeContainer plan.Deployments.EdgeImage
                (edgeEnvironment plan) (Nullable 0) ct
        let! edgeReceipt = runRollbackImporterJob plan ct
        let! recorded = post<CanonicalFinancialImportReceipt,CanonicalFinancialImportReceipt> http
                            (core "/v2/financial-transfers/external-import-receipts") edgeReceipt ct
        let evidence = AuthorityReconciliationEvidence(transfer.TransferHash,
            CanonicalJsonHash.Compute transfer.BrokerEvidence, transfer.CapturedAtUtc,
            0, transfer.TransferId, recorded.TransferHash)
        let! reconciled = post<AuthorityTransitionStepRequest,AuthorityTransitionReceipt> http
                              (core $"/v2/authority/transitions/{plan.TransitionId}/steps")
                              (stepRequest plan AuthorityTransitionOperations.Reconcile
                                  AuthorityTransitionPhases.Draining 4 sourceFence targetFence
                                  drain evidence null null) ct
        writeState statePath plan reconciled.Phase reconciled.PayloadHash
        let! committed = post<AuthorityTransitionStepRequest,AuthorityTransitionReceipt> http
                            (core $"/v2/authority/transitions/{plan.TransitionId}/steps")
                            (stepRequest plan AuthorityTransitionOperations.Commit
                                AuthorityTransitionPhases.Reconciled 5 sourceFence targetFence
                                drain evidence null null) ct
        writeState statePath plan committed.Phase committed.PayloadHash
        do! patchDeployment plan.Deployments plan.Deployments.TradingCoreDeployment
                plan.Deployments.TradingCoreContainer plan.Deployments.TradingCoreImage
                (coreEnvironment plan) (Nullable()) ct
        do! patchDeployment plan.Deployments plan.Deployments.EdgeDeployment
                plan.Deployments.EdgeContainer plan.Deployments.EdgeImage
                (edgeEnvironment plan) (Nullable 1) ct
        let! sourceCapability = get<AuthorityCapabilityReceipt> http
                                    (core "/v2/authority/capability") ct
        let! targetCapability = get<AuthorityCapabilityReceipt> http
                                    (edge "/internal/v2/edge-authority/capability") ct
        let! verified = post<AuthorityTransitionStepRequest,AuthorityTransitionReceipt> http
                           (core $"/v2/authority/transitions/{plan.TransitionId}/steps")
                           (stepRequest plan AuthorityTransitionOperations.CompleteVerification
                               AuthorityTransitionPhases.Verifying 6 sourceFence targetFence
                               drain evidence sourceCapability targetCapability) ct
        writeState statePath plan verified.Phase verified.PayloadHash
        let mirror = mirrorRequest plan "edge-rollback-mirror" 7
                         committed.EffectiveGeneration (plan.TargetMode.ToString())
                         AuthorityOwners.Edge committed.PayloadHash
        do! postNoContent<EdgeAuthorityMirrorRequest> http
                (edge "/internal/v2/edge-authority/mirror") mirror ct
        let! released = post<AuthorityTransitionStepRequest,AuthorityTransitionReceipt> http
                            (core $"/v2/authority/transitions/{plan.TransitionId}/steps")
                            (stepRequest plan AuthorityTransitionOperations.Release
                                AuthorityTransitionPhases.ReadyToRelease 8 sourceFence targetFence
                                drain evidence sourceCapability targetCapability) ct
        let! _ = post<EdgeAuthorityFenceRequest,AuthorityFenceReceipt> http
                     (edge "/internal/v2/edge-authority/release")
                     (fenceRequest plan "edge-rollback-release" 9) ct
        writeState statePath plan released.Phase released.PayloadHash
        return 0
    }

    let private runCutover (plan: TradingCoreTransitionPlanV1)
        (statePath: string) (json: JsonSerializerOptions) (ct: CancellationToken) = task {
        use http = client ()
        let core path = uri plan.TradingCoreControlEndpoint path
        let edge path = uri plan.EdgeControlEndpoint path
        let! _ = post<AuthorityTransitionRequest,AuthorityTransitionReceipt> http
                     (core "/v2/authority/transitions") (createRequest plan) ct
        let! sourceFence = post<EdgeAuthorityFenceRequest,AuthorityFenceReceipt> http
                               (edge "/internal/v2/edge-authority/fence") (fenceRequest plan "edge-fence" 1) ct
        let! targetFence = get<AuthorityFenceReceipt> http (core "/v2/authority/fence") ct
        let! quiesced = post<AuthorityTransitionStepRequest,AuthorityTransitionReceipt> http
                            (core $"/v2/authority/transitions/{plan.TransitionId}/steps")
                            (stepRequest plan AuthorityTransitionOperations.Quiesce AuthorityTransitionPhases.Requested 2 sourceFence targetFence null null null null) ct
        writeState statePath plan quiesced.Phase quiesced.PayloadHash
        let! sourceBarrier = post<EdgeAuthorityFenceRequest,AuthorityFenceReceipt> http
                                (edge "/internal/v2/edge-authority/barrier") (fenceRequest plan "edge-barrier" 3) ct
        let! drain = get<AuthorityDrainInventory> http
                         (edge $"/internal/v2/edge-authority/{plan.TransitionId}/drain") ct
        let! drained = post<AuthorityTransitionStepRequest,AuthorityTransitionReceipt> http
                           (core $"/v2/authority/transitions/{plan.TransitionId}/steps")
                           (stepRequest plan AuthorityTransitionOperations.Drain AuthorityTransitionPhases.Quiescing 4 sourceBarrier targetFence drain null null null) ct
        writeState statePath plan drained.Phase drained.PayloadHash
        let! transfer = post<CanonicalFinancialExportRequest,CanonicalFinancialTransferV2> http
                            (edge "/internal/v2/edge-authority/financial-transfers/export")
                            (exportRequest plan) ct
        match Option.ofObj (CanonicalFinancialTransferPolicy.Error transfer) with
        | Some error -> invalidOp error | None -> ()
        if not (String.IsNullOrWhiteSpace plan.ExpectedTransferHash)
            && transfer.TransferHash <> plan.ExpectedTransferHash then
            invalidOp "snapshot-hash-mismatch"
        let temporaryTransfer = plan.SealedTransferPath + ".tmp"
        File.WriteAllText(temporaryTransfer, JsonSerializer.Serialize(transfer, json))
        File.Move(temporaryTransfer, plan.SealedTransferPath, true)
        let! imported = post<CanonicalFinancialTransferV2,CanonicalFinancialImportReceipt> http
                            (core "/v2/financial-transfers/import") transfer ct
        let evidence = AuthorityReconciliationEvidence(transfer.TransferHash,
            CanonicalJsonHash.Compute transfer.BrokerEvidence, transfer.CapturedAtUtc,
            0, transfer.TransferId, imported.TransferHash)
        let! reconciled = post<AuthorityTransitionStepRequest,AuthorityTransitionReceipt> http
                              (core $"/v2/authority/transitions/{plan.TransitionId}/steps")
                              (stepRequest plan AuthorityTransitionOperations.Reconcile AuthorityTransitionPhases.Draining 5 sourceBarrier targetFence drain evidence null null) ct
        writeState statePath plan reconciled.Phase reconciled.PayloadHash
        let! committed = post<AuthorityTransitionStepRequest,AuthorityTransitionReceipt> http
                            (core $"/v2/authority/transitions/{plan.TransitionId}/steps")
                            (stepRequest plan AuthorityTransitionOperations.Commit AuthorityTransitionPhases.Reconciled 6 sourceBarrier targetFence drain evidence null null) ct
        writeState statePath plan committed.Phase committed.PayloadHash
        do! patchDeployment plan.Deployments plan.Deployments.EdgeDeployment
                plan.Deployments.EdgeContainer plan.Deployments.EdgeImage
                (edgeEnvironment plan) (Nullable()) ct
        do! patchDeployment plan.Deployments plan.Deployments.TradingCoreDeployment
                plan.Deployments.TradingCoreContainer plan.Deployments.TradingCoreImage
                (coreEnvironment plan) (Nullable()) ct
        let! sourceCapability = get<AuthorityCapabilityReceipt> http
                                    (edge "/internal/v2/edge-authority/capability") ct
        let! targetCapability = get<AuthorityCapabilityReceipt> http
                                    (core "/v2/authority/capability") ct
        let! verified = post<AuthorityTransitionStepRequest,AuthorityTransitionReceipt> http
                           (core $"/v2/authority/transitions/{plan.TransitionId}/steps")
                           (stepRequest plan AuthorityTransitionOperations.CompleteVerification AuthorityTransitionPhases.Verifying 7 sourceBarrier targetFence drain evidence sourceCapability targetCapability) ct
        writeState statePath plan verified.Phase verified.PayloadHash
        let mirror = mirrorRequest plan "edge-mirror" 8
                         committed.EffectiveGeneration (plan.TargetMode.ToString())
                         AuthorityOwners.TradingCore committed.PayloadHash
        do! postNoContent<EdgeAuthorityMirrorRequest> http
                (edge "/internal/v2/edge-authority/mirror") mirror ct
        let! released = post<AuthorityTransitionStepRequest,AuthorityTransitionReceipt> http
                            (core $"/v2/authority/transitions/{plan.TransitionId}/steps")
                            (stepRequest plan AuthorityTransitionOperations.Release AuthorityTransitionPhases.ReadyToRelease 9 sourceBarrier targetFence drain evidence sourceCapability targetCapability) ct
        writeState statePath plan released.Phase released.PayloadHash
        return 0
    }

    let run (ct: CancellationToken) = task {
        let planPath = required "STOCKTRADER_TRANSITION_PLAN_PATH"
        let statePath = required "STOCKTRADER_COORDINATOR_STATE_PATH"
        let json = JsonSerializerOptions(JsonSerializerDefaults.Web)
        let plan = JsonSerializer.Deserialize<TradingCoreTransitionPlanV1>(File.ReadAllText planPath, json)
        if isNull plan then invalidOp "empty-transition-plan"
        match Option.ofObj (TradingCoreCoordinatorPolicy.Error plan) with
        | Some error -> invalidArg "plan" error | None -> ()
        if plan.Direction = AuthorityTransitionDirections.Rollback then
            return! runRollback plan statePath json ct
        else
            return! runCutover plan statePath json ct
    }
