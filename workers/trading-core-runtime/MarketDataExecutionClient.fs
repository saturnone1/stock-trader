namespace StockTrader.TradingCoreService

open System
open System.IO
open System.Net.Http
open System.Net.Http.Json
open System.Security.Cryptography.X509Certificates
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open StockTrader.ServiceContracts.MarketData

type MarketDataExecutionClient(config: ServiceConfig, json: JsonSerializerOptions) =
    let handler = new HttpClientHandler()
    let clientCertificate = X509Certificate2.CreateFromPemFile(
        config.MarketDataClientCertificatePath, config.MarketDataClientKeyPath)
    let serverAuthority = X509Certificate2.CreateFromPem(
        File.ReadAllText config.MarketDataServerCaPath)
    do
        handler.ClientCertificates.Add clientCertificate |> ignore
        handler.ServerCertificateCustomValidationCallback <- fun _ certificate _ errors ->
            if isNull certificate || errors.HasFlag(Net.Security.SslPolicyErrors.RemoteCertificateNameMismatch) then false
            elif not (String.Equals(
                    certificate.GetNameInfo(X509NameType.DnsName, false),
                    config.MarketDataServerCommonName, StringComparison.Ordinal)) then false
            else
                use chain = new X509Chain()
                chain.ChainPolicy.TrustMode <- X509ChainTrustMode.CustomRootTrust
                chain.ChainPolicy.CustomTrustStore.Add serverAuthority |> ignore
                chain.ChainPolicy.RevocationMode <- X509RevocationMode.NoCheck
                chain.ChainPolicy.VerificationFlags <- X509VerificationFlags.NoFlag
                chain.Build certificate
    let http = new HttpClient(handler, true)
    do
        http.BaseAddress <- config.MarketDataEndpoint
        http.Timeout <- TimeSpan.FromSeconds 10.0

    let validateRange (response: MarketDataExecutionWindowResponse) =
        let evidence = response.Evidence
        let contentHash = MarketDataContractHash.Content response.Bars
        let evidenceId = MarketDataContractHash.Evidence(
            evidence.Provider, evidence.Symbol, evidence.TimeFrame, evidence.AdjustmentMode,
            evidence.CalendarVersion, evidence.Revision, contentHash)
        if evidence.ContractVersion <> MarketDataContractVersions.Current
           || not (String.Equals(contentHash, evidence.ContentHash, StringComparison.Ordinal))
           || not (String.Equals(evidenceId, evidence.EvidenceId, StringComparison.Ordinal)) then
            invalidOp "market-data-execution-evidence-hash-mismatch"
        response

    member _.VerifyAsync(evidence: MarketDataEvidenceContract, ct: CancellationToken) = task {
        let request = MarketDataEvidenceVerificationRequest(MarketDataContractVersions.Current, evidence)
        use! response = http.PostAsJsonAsync(
            "/v1/execution-evidence/verify", request, json, ct)
        response.EnsureSuccessStatusCode() |> ignore
        let! value = response.Content.ReadFromJsonAsync<MarketDataEvidenceVerificationResponse>(json, ct)
        return value |> Option.ofObj |> Option.defaultWith (fun () ->
            invalidOp "empty-market-data-evidence-verification-response")
    }

    member _.LatestCompletedAsync(request: MarketDataExecutionWindowRequest, ct: CancellationToken) = task {
        use! httpResponse = http.PostAsJsonAsync(
            "/v1/execution-evidence/latest-completed", request, json, ct)
        httpResponse.EnsureSuccessStatusCode() |> ignore
        let! response = httpResponse.Content.ReadFromJsonAsync<MarketDataExecutionWindowResponse>(json, ct)
        return response
            |> Option.ofObj
            |> Option.map validateRange
            |> Option.defaultWith (fun () -> invalidOp "empty-market-data-execution-response")
    }

    interface IDisposable with
        member _.Dispose() = http.Dispose()
