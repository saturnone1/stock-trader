namespace StockTrader.TradingCoreAcceptance

open System
open System.Collections.Generic
open System.Globalization
open System.Net.Http
open System.Net.Http.Json
open System.Security.Cryptography.X509Certificates
open System.Threading
open System.Threading.Tasks
open StockTrader.ServiceContracts.TradingCore
open StockTrader.TradingCore.Broker

module private BrokerMapping =
    let decimalValue (value: string) = Decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture)
    let optionalDecimal (value: string) = if String.IsNullOrWhiteSpace value then Nullable() else Nullable(decimalValue value)
    let order (value: ScriptedBrokerOrder) =
        BrokerOrderEvidence(value.OrderId, value.ClientOrderId, value.Symbol, value.Side,
            value.Quantity, value.FilledQuantity, optionalDecimal value.OrderPrice,
            optionalDecimal value.AverageFillPrice, value.Status, value.OrderType,
            value.SubmittedAtUtc, value.FilledAtUtc)
    let position (value: ScriptedBrokerPosition) =
        BrokerPositionEvidence(value.Symbol, value.Quantity,
            decimalValue value.AverageEntryPrice, decimalValue value.CurrentPrice)
    let account (value: ScriptedBrokerAccount) =
        BrokerAccountEvidence(value.AccountId, decimalValue value.TotalEquity,
            decimalValue value.PreviousDayEquity, decimalValue value.Cash,
            decimalValue value.BuyingPower, value.IsTradingBlocked, value.ObservedAtUtc)

module private BrokerHttp =
    let post<'TRequest,'TResponse> (client: HttpClient) (path: string)
        (request: 'TRequest) (ct: CancellationToken) = task {
        use! response = client.PostAsJsonAsync<'TRequest>(path, request, ct)
        response.EnsureSuccessStatusCode() |> ignore
        let! value = response.Content.ReadFromJsonAsync<'TResponse>(cancellationToken = ct)
        return value
    }

type ScriptedBrokerClient(client: HttpClient) =
    interface ITradingBroker with
        member _.SubmitEntryAsync(request, ct) = task {
            let! value = BrokerHttp.post<BrokerEntryOrderRequest,ScriptedBrokerOrder> client "/broker/submit-entry" request ct
            return BrokerMapping.order value }
        member _.IncreasePositionAsync(request, ct) = task {
            let! value = BrokerHttp.post<BrokerPositionOrderRequest,ScriptedBrokerOrder> client "/broker/increase-position" request ct
            return BrokerMapping.order value }
        member _.ClosePositionAsync(request, ct) = task {
            let! value = BrokerHttp.post<BrokerPositionOrderRequest,ScriptedBrokerOrder> client "/broker/close-position" request ct
            return BrokerMapping.order value }
        member _.CancelOrderAsync(orderId, ct) =
            BrokerHttp.post<obj,bool> client "/broker/cancel-order" {| orderId = orderId |} ct
        member _.GetPositionsAsync(ct) = task {
            let! values = client.GetFromJsonAsync<ScriptedBrokerPosition array>("/broker/positions", ct)
            return values |> Array.map BrokerMapping.position :> IReadOnlyList<_> }
        member _.GetAccountAsync(ct) = task {
            let! value = client.GetFromJsonAsync<ScriptedBrokerAccount>("/broker/account", ct)
            return BrokerMapping.account value }
        member _.GetOrdersAsync(fromUtc, toUtc, ct) = task {
            let fromValue = Uri.EscapeDataString(fromUtc.ToString("O"))
            let toValue = Uri.EscapeDataString(toUtc.ToString("O"))
            let path = $"/broker/orders?fromUtc={fromValue}&toUtc={toValue}"
            let! values = client.GetFromJsonAsync<ScriptedBrokerOrder array>(path, ct)
            return values |> Array.map BrokerMapping.order :> IReadOnlyList<_> }
        member _.Dispose() = ()

type ScriptedBrokerFactory(config: AcceptanceConfig) =
    let handler = new HttpClientHandler()
    let certificate = X509Certificate2.CreateFromPemFile(
        config.BrokerClientCertificatePath, config.BrokerClientKeyPath)
    do handler.ClientCertificates.Add certificate |> ignore
    do handler.ServerCertificateCustomValidationCallback <- fun _ certificate _ _ ->
        if isNull certificate then false else
        use root = X509Certificate2.CreateFromPemFile(config.BrokerServerCaPath)
        use chain = new X509Chain()
        chain.ChainPolicy.TrustMode <- X509ChainTrustMode.CustomRootTrust
        chain.ChainPolicy.CustomTrustStore.Add root |> ignore
        chain.ChainPolicy.RevocationMode <- X509RevocationMode.NoCheck
        let nameMatches = certificate.GetNameInfo(X509NameType.DnsName, false) = config.BrokerServerCommonName
        nameMatches && chain.Build certificate
    let client = new HttpClient(handler, true)
    do client.BaseAddress <- config.BrokerEndpoint
    interface ITradingBrokerFactory with
        member _.Create(_) = new ScriptedBrokerClient(client) :> ITradingBroker
