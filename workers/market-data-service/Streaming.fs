namespace StockTrader.MarketDataService

open System
open System.Collections.Generic
open System.Net.WebSockets
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open StockTrader.Domain.MarketData
open StockTrader.Engine.MarketData
open StockTrader.ServiceContracts.MarketData

module StreamingProtocol =
    let isAuthenticated (json: string) =
        try
            use document = JsonDocument.Parse(json)
            document.RootElement.ValueKind = JsonValueKind.Array
            && (document.RootElement.EnumerateArray()
                |> Seq.exists (fun value ->
                    String.Equals(
                        ProviderJson.stringAt value "T",
                        "success",
                        StringComparison.OrdinalIgnoreCase)
                    && String.Equals(
                        ProviderJson.stringAt value "msg",
                        "authenticated",
                        StringComparison.OrdinalIgnoreCase)))
        with :? JsonException -> false

type SubscriptionState() =
    let sync = obj()
    let mutable symbols: string array = [||]
    let mutable generation = 0L
    let mutable connected = false

    member _.Update(values: seq<string>) =
        let normalized = values |> Seq.map ContractPolicy.normalizeSymbol |> Seq.distinct |> Seq.sort |> Seq.toArray
        lock sync (fun () ->
            if normalized <> symbols then
                symbols <- normalized
                generation <- generation + 1L
            symbols, generation, connected)

    member _.Snapshot() = lock sync (fun () -> symbols, generation, connected)
    member _.SetConnected(value) = lock sync (fun () -> connected <- value)

type AlpacaStreamingWorker(
    settings: ServiceSettings,
    store: BarStore,
    subscriptions: SubscriptionState,
    logger: ILogger<AlpacaStreamingWorker>) =
    inherit BackgroundService()

    let receive (socket: ClientWebSocket) (ct: CancellationToken) = task {
        let buffer = Array.zeroCreate<byte> 131072
        let builder = StringBuilder()
        let mutable finished = false
        while not finished do
            let! result = socket.ReceiveAsync(ArraySegment<byte>(buffer), ct)
            if result.MessageType = WebSocketMessageType.Close then
                raise (WebSocketException("Alpaca streaming socket closed"))
            builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count)) |> ignore
            finished <- result.EndOfMessage
        return builder.ToString()
    }

    let send (socket: ClientWebSocket) (value: string) (ct: CancellationToken) = task {
        let bytes = Encoding.UTF8.GetBytes(value)
        do! socket.SendAsync(ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct)
    }

    let persist (bars: MarketDataBar array) (ct: CancellationToken) = task {
        if bars.Length > 0 then
            let provider, frame = DataSource.Alpaca, TimeFrame.OneMinute
            let descriptor = DataProviderCatalog.Get(provider)
            let request = MarketDataUpsertRequest(
                MarketDataContractVersions.Current,
                ContractPolicy.sha256 ("stream|" + ContractPolicy.contentHash bars),
                provider.ToString(), PriceAdjustmentCatalog.Resolve(provider, frame).ToString(),
                descriptor.Market, MarketCalendarVersion.Current, Nullable(), Nullable(), false, bars)
            let! _ = store.UpsertAsync(request, ct)
            ()
    }

    let parseBars (json: string) =
        use document = JsonDocument.Parse(json)
        if document.RootElement.ValueKind <> JsonValueKind.Array then [||]
        else
            [| for value in document.RootElement.EnumerateArray() do
                if ProviderJson.stringAt value "T" = "b" then
                    let symbol = ContractPolicy.normalizeSymbol (ProviderJson.stringAt value "S")
                    let vwap = ProviderJson.decimalOrZero value "vw"
                    yield MarketDataBar(symbol, TimeFrame.OneMinute.ToString(),
                        DateTime.Parse(ProviderJson.stringAt value "t", null, Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime(),
                        ProviderJson.decimalAt value "o", ProviderJson.decimalAt value "h",
                        ProviderJson.decimalAt value "l", ProviderJson.decimalAt value "c",
                        ProviderJson.int64At value "v", if vwap = 0m then Nullable() else Nullable(vwap)) |]

    member private _.RunSocketAsync(ct: CancellationToken) = task {
        use socket = new ClientWebSocket()
        do! socket.ConnectAsync(Uri(settings.AlpacaStreamUrl), ct)
        let! _ = receive socket ct
        do! send socket (JsonSerializer.Serialize({| action="auth"; key=settings.AlpacaKey; secret=settings.AlpacaSecret |})) ct
        let! authentication = receive socket ct
        if not (StreamingProtocol.isAuthenticated authentication) then
            invalidOp "Alpaca streaming authentication was rejected"
        subscriptions.SetConnected(true)
        let mutable appliedGeneration = -1L
        let mutable appliedSymbols: string array = [||]
        while socket.State = WebSocketState.Open && not ct.IsCancellationRequested do
            let symbols, generation, _ = subscriptions.Snapshot()
            if generation <> appliedGeneration then
                let removed = appliedSymbols |> Array.except symbols
                if removed.Length > 0 then
                    do! send socket (JsonSerializer.Serialize({| action="unsubscribe"; bars=removed |})) ct
                do! send socket (JsonSerializer.Serialize({| action="subscribe"; bars=symbols |})) ct
                appliedGeneration <- generation
                appliedSymbols <- symbols
            use receiveTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct)
            receiveTimeout.CancelAfter(TimeSpan.FromSeconds(5.0))
            try
                let! message = receive socket receiveTimeout.Token
                do! persist (parseBars message) ct
            with :? OperationCanceledException when not ct.IsCancellationRequested -> ()
    }

    override this.ExecuteAsync(ct) = task {
        if String.IsNullOrWhiteSpace(settings.AlpacaKey) || String.IsNullOrWhiteSpace(settings.AlpacaSecret) then
            logger.LogInformation("Alpaca streaming is disabled because Market Data credentials are absent")
        else
            while not ct.IsCancellationRequested do
                let symbols, _, _ = subscriptions.Snapshot()
                if symbols.Length = 0 then do! Task.Delay(TimeSpan.FromSeconds(5.0), ct)
                else
                    try
                        try
                            do! this.RunSocketAsync(ct)
                        with
                        | :? OperationCanceledException when ct.IsCancellationRequested -> ()
                        | error ->
                            logger.LogWarning(error, "Alpaca market-data stream disconnected; retrying")
                            try do! Task.Delay(TimeSpan.FromSeconds(5.0), ct) with :? OperationCanceledException -> ()
                    finally subscriptions.SetConnected(false)
    }
