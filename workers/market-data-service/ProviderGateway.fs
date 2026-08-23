namespace StockTrader.MarketDataService

open System
open System.Collections.Generic
open System.Globalization
open System.Net.Http
open System.Net.Http.Headers
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open StockTrader.Domain.MarketData
open StockTrader.Engine.MarketData
open StockTrader.ServiceContracts.MarketData

type ProviderResult = { Bars: MarketDataBar array; Complete: bool }

module ProviderJson =
    let decimalAt (element: JsonElement) (name: string) =
        match element.TryGetProperty(name) with
        | true, value when value.ValueKind = JsonValueKind.Number -> value.GetDecimal()
        | true, value when value.ValueKind = JsonValueKind.String ->
            match Decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture) with
            | true, parsed -> parsed
            | _ -> invalidOp $"Provider field {name} was not a decimal"
        | _ -> invalidOp $"Provider field {name} was missing or not numeric"

    let decimalOrZero (element: JsonElement) (name: string) =
        try decimalAt element name with :? InvalidOperationException -> 0m

    let int64At (element: JsonElement) (name: string) =
        match element.TryGetProperty(name) with
        | true, value when value.ValueKind = JsonValueKind.Number -> value.GetInt64()
        | true, value when value.ValueKind = JsonValueKind.String ->
            match Int64.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture) with
            | true, parsed -> parsed
            | _ -> invalidOp $"Provider field {name} was not an integer"
        | _ -> invalidOp $"Provider field {name} was missing or not an integer"

    let int64OrZero (element: JsonElement) (name: string) =
        try int64At element name with :? InvalidOperationException -> 0L

    let stringAt (element: JsonElement) (name: string) =
        match element.TryGetProperty(name) with
        | true, value -> value.GetString() |> Option.ofObj |> Option.defaultValue ""
        | _ -> ""

type YahooProvider(settings: ServiceSettings, http: HttpClient) =
    let gate = new SemaphoreSlim(3, 3)

    let interval frame =
        match frame with
        | TimeFrame.OneMinute -> "1m" | TimeFrame.FiveMinute -> "5m"
        | TimeFrame.FifteenMinute -> "15m" | TimeFrame.Weekly -> "1wk" | _ -> "1d"

    let fetch (path: string) (ct: CancellationToken) = task {
        do! gate.WaitAsync(ct)
        try
            use request = new HttpRequestMessage(HttpMethod.Get, settings.YahooBaseUrl.TrimEnd('/') + path)
            request.Headers.UserAgent.ParseAdd(settings.YahooUserAgent)
            use! response = http.SendAsync(request, ct)
            response.EnsureSuccessStatusCode() |> ignore
            let! body = response.Content.ReadAsStringAsync(ct)
            try do! Task.Delay(settings.YahooDelayMs, ct) with :? OperationCanceledException -> ()
            return body
        finally
            gate.Release() |> ignore
    }

    let parse (symbol: string) (frame: TimeFrame) (json: string) =
        use document = JsonDocument.Parse(json)
        let mutable chart, results = Unchecked.defaultof<JsonElement>, Unchecked.defaultof<JsonElement>
        if not (document.RootElement.TryGetProperty("chart", &chart))
           || not (chart.TryGetProperty("result", &results))
           || results.ValueKind <> JsonValueKind.Array || results.GetArrayLength() = 0 then [||]
        else
            let result = results[0]
            let mutable timestamps, indicators, quotes = Unchecked.defaultof<JsonElement>, Unchecked.defaultof<JsonElement>, Unchecked.defaultof<JsonElement>
            if not (result.TryGetProperty("timestamp", &timestamps))
               || not (result.TryGetProperty("indicators", &indicators))
               || not (indicators.TryGetProperty("quote", &quotes))
               || quotes.GetArrayLength() = 0 then [||]
            else
                let quote = quotes[0]
                let mutable opens, highs, lows, closes, volumes = Unchecked.defaultof<JsonElement>, Unchecked.defaultof<JsonElement>, Unchecked.defaultof<JsonElement>, Unchecked.defaultof<JsonElement>, Unchecked.defaultof<JsonElement>
                quote.TryGetProperty("open", &opens) |> ignore; quote.TryGetProperty("high", &highs) |> ignore
                quote.TryGetProperty("low", &lows) |> ignore; quote.TryGetProperty("close", &closes) |> ignore
                quote.TryGetProperty("volume", &volumes) |> ignore
                let mutable adjRoot, adjValues = Unchecked.defaultof<JsonElement>, Unchecked.defaultof<JsonElement>
                let hasAdjusted = indicators.TryGetProperty("adjclose", &adjRoot) && adjRoot.GetArrayLength() > 0 && adjRoot[0].TryGetProperty("adjclose", &adjValues)
                [| for index in 0 .. timestamps.GetArrayLength() - 1 do
                    if opens[index].ValueKind = JsonValueKind.Number && highs[index].ValueKind = JsonValueKind.Number
                       && lows[index].ValueKind = JsonValueKind.Number && closes[index].ValueKind = JsonValueKind.Number then
                        let rawClose = closes[index].GetDecimal()
                        let adjusted = if hasAdjusted && adjValues[index].ValueKind = JsonValueKind.Number then adjValues[index].GetDecimal() else rawClose
                        let factor = if rawClose > 0m && adjusted > 0m then adjusted / rawClose else 1m
                        let volume = if volumes[index].ValueKind = JsonValueKind.Number then volumes[index].GetInt64() else 0L
                        yield MarketDataBar(symbol, frame.ToString(), DateTimeOffset.FromUnixTimeSeconds(timestamps[index].GetInt64()).UtcDateTime,
                            opens[index].GetDecimal() * factor, highs[index].GetDecimal() * factor,
                            lows[index].GetDecimal() * factor, adjusted, volume, Nullable()) |]

    member _.HistoricalAsync(symbol: string, frame: TimeFrame, fromUtc: DateTime, toUtc: DateTime, ct: CancellationToken) = task {
        let maxDays = DataProviderCatalog.MaximumLookbackDays(DataSource.Yahoo, frame)
        let actualFrom = if maxDays.HasValue then max fromUtc (toUtc.AddDays(-float maxDays.Value)) else fromUtc
        let fromUnix, toUnix = DateTimeOffset(actualFrom).ToUnixTimeSeconds(), DateTimeOffset(toUtc).ToUnixTimeSeconds()
        let! json = fetch $"/v8/finance/chart/{Uri.EscapeDataString(symbol)}?period1={fromUnix}&period2={toUnix}&interval={interval frame}" ct
        return { Bars = parse symbol frame json; Complete = actualFrom = fromUtc }
    }

    member this.LatestAsync(symbol: string, frame: TimeFrame, ct: CancellationToken) = task {
        let! json = fetch $"/v8/finance/chart/{Uri.EscapeDataString(symbol)}?range=5d&interval={interval frame}" ct
        let bars = parse symbol frame json
        return if bars.Length = 0 then None else Some bars[bars.Length - 1]
    }

    member _.PriceAsync(symbol: string, ct: CancellationToken) = task {
        let! json = fetch $"/v8/finance/chart/{Uri.EscapeDataString(symbol)}?range=1d&interval=1m" ct
        use document = JsonDocument.Parse(json)
        let result = document.RootElement.GetProperty("chart").GetProperty("result")[0]
        return result.GetProperty("meta").GetProperty("regularMarketPrice").GetDecimal()
    }

type AlpacaProvider(settings: ServiceSettings, http: HttpClient) =
    let ensureConfigured () =
        if String.IsNullOrWhiteSpace(settings.AlpacaKey) || String.IsNullOrWhiteSpace(settings.AlpacaSecret) then
            invalidOp "Alpaca market-data credentials are not configured in the Market Data service"

    let timeframe frame =
        match frame with
        | TimeFrame.OneMinute -> "1Min" | TimeFrame.FiveMinute -> "5Min"
        | TimeFrame.FifteenMinute -> "15Min" | TimeFrame.Weekly -> "1Week" | _ -> "1Day"

    let request (path: string) (ct: CancellationToken) = task {
        ensureConfigured()
        use message = new HttpRequestMessage(HttpMethod.Get, settings.AlpacaDataBaseUrl.TrimEnd('/') + path)
        message.Headers.Add("APCA-API-KEY-ID", settings.AlpacaKey)
        message.Headers.Add("APCA-API-SECRET-KEY", settings.AlpacaSecret)
        use! response = http.SendAsync(message, ct)
        response.EnsureSuccessStatusCode() |> ignore
        return! response.Content.ReadAsStringAsync(ct)
    }

    let bar (symbol: string) (frame: TimeFrame) (value: JsonElement) =
        MarketDataBar(symbol, frame.ToString(), DateTime.Parse(ProviderJson.stringAt value "t", null, DateTimeStyles.RoundtripKind).ToUniversalTime(),
            ProviderJson.decimalAt value "o", ProviderJson.decimalAt value "h", ProviderJson.decimalAt value "l",
            ProviderJson.decimalAt value "c", ProviderJson.int64At value "v",
            let v = ProviderJson.decimalOrZero value "vw" in if v = 0m then Nullable() else Nullable(v))

    member _.HistoricalAsync(symbol: string, frame: TimeFrame, fromUtc: DateTime, toUtc: DateTime, ct: CancellationToken) = task {
        let bars = ResizeArray<MarketDataBar>()
        let mutable token = ""
        let mutable more = true
        while more do
            let tokenPart = if String.IsNullOrEmpty(token) then "" else "&page_token=" + Uri.EscapeDataString(token)
            let encodedSymbol = Uri.EscapeDataString(symbol)
            let encodedFrom = Uri.EscapeDataString(fromUtc.ToString("O"))
            let encodedTo = Uri.EscapeDataString(toUtc.ToString("O"))
            let path = $"/v2/stocks/{encodedSymbol}/bars?timeframe={timeframe frame}&start={encodedFrom}&end={encodedTo}&adjustment=all&feed={settings.AlpacaFeed}&limit=10000{tokenPart}"
            let! json = request path ct
            use document = JsonDocument.Parse(json)
            match document.RootElement.TryGetProperty("bars") with
            | true, values -> for value in values.EnumerateArray() do bars.Add(bar symbol frame value)
            | _ -> ()
            token <- ProviderJson.stringAt document.RootElement "next_page_token"
            more <- not (String.IsNullOrEmpty(token))
        return { Bars = bars.ToArray(); Complete = true }
    }

    member _.LatestAsync(symbol: string, frame: TimeFrame, ct: CancellationToken) = task {
        let! json = request $"/v2/stocks/{Uri.EscapeDataString(symbol)}/bars/latest?feed={settings.AlpacaFeed}" ct
        use document = JsonDocument.Parse(json)
        return match document.RootElement.TryGetProperty("bar") with true, value -> Some(bar symbol frame value) | _ -> None
    }

    member _.PriceAsync(symbol: string, ct: CancellationToken) = task {
        let! json = request $"/v2/stocks/{Uri.EscapeDataString(symbol)}/trades/latest?feed={settings.AlpacaFeed}" ct
        use document = JsonDocument.Parse(json)
        return ProviderJson.decimalAt (document.RootElement.GetProperty("trade")) "p"
    }

type LsProvider(settings: ServiceSettings, http: HttpClient) =
    let tokenGate = new SemaphoreSlim(1, 1)
    let rateGate = new SemaphoreSlim(1, 1)
    let mutable accessToken, expiresAt, lastChartAt = "", DateTime.MinValue, DateTime.MinValue

    let ensureConfigured () =
        if String.IsNullOrWhiteSpace(settings.LsAppKey) || String.IsNullOrWhiteSpace(settings.LsAppSecret) then
            invalidOp "LS market-data credentials are not configured in the Market Data service"

    let token (ct: CancellationToken) = task {
        ensureConfigured()
        if accessToken <> "" && DateTime.UtcNow < expiresAt then return accessToken
        else
            do! tokenGate.WaitAsync(ct)
            try
                if accessToken = "" || DateTime.UtcNow >= expiresAt then
                    use content = new FormUrlEncodedContent(dict [ "grant_type", "client_credentials"; "appkey", settings.LsAppKey; "appsecretkey", settings.LsAppSecret; "scope", "oob" ])
                    use! response = http.PostAsync(settings.LsBaseUrl.TrimEnd('/') + "/oauth2/token", content, ct)
                    response.EnsureSuccessStatusCode() |> ignore
                    let! json = response.Content.ReadAsStringAsync(ct)
                    use document = JsonDocument.Parse(json)
                    accessToken <- ProviderJson.stringAt document.RootElement "access_token"
                    expiresAt <- DateTime.UtcNow.AddHours(20)
                return accessToken
            finally tokenGate.Release() |> ignore
    }

    let sendChart (trCode: string) body (continuation: bool) (continuationKey: string) (ct: CancellationToken) = task {
        do! rateGate.WaitAsync(ct)
        try
            let delay = TimeSpan.FromSeconds(1.0) - (DateTime.UtcNow - lastChartAt)
            if delay > TimeSpan.Zero then do! Task.Delay(delay, ct)
            lastChartAt <- DateTime.UtcNow
        finally rateGate.Release() |> ignore
        let! bearer = token ct
        use request = new HttpRequestMessage(HttpMethod.Post, settings.LsBaseUrl.TrimEnd('/') + "/stock/chart")
        request.Headers.Authorization <- AuthenticationHeaderValue("Bearer", bearer)
        request.Headers.Add("tr_cd", trCode); request.Headers.Add("tr_cont", if continuation then "Y" else "N")
        if not (String.IsNullOrWhiteSpace(continuationKey)) then request.Headers.Add("tr_cont_key", continuationKey)
        request.Content <- new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        use! response = http.SendAsync(request, ct)
        response.EnsureSuccessStatusCode() |> ignore
        let! json = response.Content.ReadAsStringAsync(ct)
        let nextKey =
            match response.Headers.TryGetValues("tr_cont_key") with
            | true, values -> values |> Option.ofObj |> Option.bind Seq.tryHead |> Option.defaultValue ""
            | _ -> ""
        return json, nextKey
    }

    let parse (symbol: string) (frame: TimeFrame) (daily: bool) (value: JsonElement) =
        let timestamp =
            let date = ProviderJson.stringAt value "date"
            let time = if daily then "" else ProviderJson.stringAt value "time"
            DateTime.ParseExact(date + time, (if daily then "yyyyMMdd" else "yyyyMMddHHmmss"), CultureInfo.InvariantCulture, DateTimeStyles.None)
        let zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Seoul")
        MarketDataBar(symbol, frame.ToString(), TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(timestamp, DateTimeKind.Unspecified), zone),
            ProviderJson.decimalAt value "open", ProviderJson.decimalAt value "high", ProviderJson.decimalAt value "low",
            ProviderJson.decimalAt value "close", max (ProviderJson.int64OrZero value "jdiff_vol") (ProviderJson.int64OrZero value "volume"), Nullable())

    member _.HistoricalAsync(symbol: string, frame: TimeFrame, fromUtc: DateTime, toUtc: DateTime, ct: CancellationToken) = task {
        let daily = frame = TimeFrame.Daily || frame = TimeFrame.Weekly
        let trCode, root, body =
            if daily then
                "t8410", "t8410OutBlock1", box {| t8410InBlock = {| shcode=symbol; gubun=(if frame = TimeFrame.Weekly then "3" else "2"); qrycnt=500; sdate=fromUtc.ToString("yyyyMMdd"); edate=toUtc.ToString("yyyyMMdd"); cts_date=""; comp_yn="N"; sujung="Y" |} |}
            else
                let minutes = if frame = TimeFrame.FiveMinute then 5 elif frame = TimeFrame.FifteenMinute then 15 else 1
                "t8412", "t8412OutBlock1", box {| t8412InBlock = {| shcode=symbol; ncnt=minutes; qrycnt=500; sdate=fromUtc.ToString("yyyyMMdd"); edate=toUtc.ToString("yyyyMMdd"); stime="090000"; etime="153000"; comp_yn="N" |} |}
        let values = ResizeArray<MarketDataBar>()
        let mutable page, continuation, continuationKey, complete = 0, false, "", false
        while page < (if daily then 20 else 50) && not complete do
            let! json, nextKey = sendChart trCode body continuation continuationKey ct
            use document = JsonDocument.Parse(json)
            match document.RootElement.TryGetProperty(root) with
            | true, found -> for value in found.EnumerateArray() do values.Add(parse symbol frame daily value)
            | _ -> ()
            let headerName = if daily then "t8410OutBlock" else "t8412OutBlock"
            let mutable header, cursorDate, cursorTime = Unchecked.defaultof<JsonElement>, Unchecked.defaultof<JsonElement>, Unchecked.defaultof<JsonElement>
            let hasDate = document.RootElement.TryGetProperty(headerName, &header) && header.TryGetProperty("cts_date", &cursorDate) && not (String.IsNullOrWhiteSpace(cursorDate.GetString()))
            let hasTime = daily || (header.TryGetProperty("cts_time", &cursorTime) && not (String.IsNullOrWhiteSpace(cursorTime.GetString())))
            complete <- not (hasDate && hasTime && not (String.IsNullOrWhiteSpace(nextKey)))
            continuation <- not complete
            continuationKey <- nextKey
            page <- page + 1
        return { Bars = values |> Seq.distinctBy _.TimestampUtc |> Seq.sortBy _.TimestampUtc |> Seq.toArray; Complete = complete }
    }

    member this.LatestAsync(symbol: string, frame: TimeFrame, ct: CancellationToken) = task {
        let! result = this.HistoricalAsync(symbol, frame, DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, ct)
        return if result.Bars.Length = 0 then None else Some result.Bars[result.Bars.Length - 1]
    }

    member _.PriceAsync(symbol: string, ct: CancellationToken) = task {
        let! bearer = token ct
        use request = new HttpRequestMessage(HttpMethod.Post, settings.LsBaseUrl.TrimEnd('/') + "/stock/market-data")
        request.Headers.Authorization <- AuthenticationHeaderValue("Bearer", bearer)
        request.Headers.Add("tr_cd", "t1102"); request.Headers.Add("tr_cont", "N")
        request.Content <- new StringContent(JsonSerializer.Serialize({| t1102InBlock = {| shcode=symbol |} |}), Encoding.UTF8, "application/json")
        use! response = http.SendAsync(request, ct)
        response.EnsureSuccessStatusCode() |> ignore
        let! json = response.Content.ReadAsStringAsync(ct)
        use document = JsonDocument.Parse(json)
        return ProviderJson.decimalAt (document.RootElement.GetProperty("t1102OutBlock")) "price"
    }

type ProviderGateway(settings: ServiceSettings, store: BarStore, http: HttpClient) =
    let yahoo, alpaca, ls = YahooProvider(settings, http), AlpacaProvider(settings, http), LsProvider(settings, http)

    let metadata provider frame =
        let descriptor = DataProviderCatalog.Get(provider)
        let market = MarketRegionCatalog.Get(descriptor.MarketRegion)
        PriceAdjustmentCatalog.Resolve(provider, frame).ToString(), descriptor.Market, MarketCalendarVersion.Current, market

    let persist (provider: DataSource) (frame: TimeFrame) (fromUtc: DateTime) (toUtc: DateTime) (complete: bool) (bars: MarketDataBar array) (ct: CancellationToken) = task {
        if bars.Length > 0 then
            let adjustment, market, calendar, _ = metadata provider frame
            let requestId = ContractPolicy.sha256 $"provider|{provider}|{frame}|{fromUtc:O}|{toUtc:O}|{ContractPolicy.contentHash bars}"
            let request = MarketDataUpsertRequest(MarketDataContractVersions.Current, requestId, provider.ToString(), adjustment, market, calendar, Nullable(fromUtc), Nullable(toUtc), complete, bars)
            let! _ = store.UpsertAsync(request, ct)
            ()
    }

    member _.HistoricalAsync(request: MarketDataProviderRequest, ct: CancellationToken) = task {
        ContractPolicy.validateVersion request.ContractVersion
        let provider, frame, symbol = ContractPolicy.normalizeProvider request.Provider, ContractPolicy.normalizeTimeFrame request.TimeFrame, ContractPolicy.normalizeSymbol request.Symbol
        let fromUtc, toUtc = ContractPolicy.ensureRange request.FromUtc request.ToUtc
        let! result = match provider with DataSource.Yahoo -> yahoo.HistoricalAsync(symbol, frame, fromUtc, toUtc, ct) | DataSource.Alpaca -> alpaca.HistoricalAsync(symbol, frame, fromUtc, toUtc, ct) | DataSource.LsSecurities -> ls.HistoricalAsync(symbol, frame, fromUtc, toUtc, ct) | _ -> invalidOp "Provider is not implemented"
        if request.Persist then do! persist provider frame fromUtc toUtc result.Complete result.Bars ct
        let adjustment, market, calendar, _ = metadata provider frame
        let range = MarketDataRangeRequest(MarketDataContractVersions.Current, provider.ToString(), symbol, frame.ToString(), adjustment, market, calendar, fromUtc, toUtc)
        if request.Persist then return! store.ReadRangeAsync(range, ct)
        else
            let hash = ContractPolicy.contentHash result.Bars
            let evidence = MarketDataEvidenceContract(MarketDataContractVersions.Current, ContractPolicy.evidenceId (provider.ToString()) symbol (frame.ToString()) adjustment calendar 0L hash, provider.ToString(), symbol, frame.ToString(), adjustment, market, calendar, fromUtc, toUtc, (if result.Bars.Length=0 then Nullable() else Nullable(result.Bars[0].TimestampUtc)), (if result.Bars.Length=0 then Nullable() else Nullable(result.Bars[result.Bars.Length-1].TimestampUtc)), 0L, result.Complete, hash)
            return MarketDataRangeResponse(evidence, result.Bars)
    }

    member this.LatestAsync(request: MarketDataProviderRequest, ct: CancellationToken) =
        this.HistoricalAsync(request, ct)

    member _.PriceAsync(request: MarketDataPriceRequest, ct: CancellationToken) = task {
        ContractPolicy.validateVersion request.ContractVersion
        let provider, symbol = ContractPolicy.normalizeProvider request.Provider, ContractPolicy.normalizeSymbol request.Symbol
        let! price = match provider with DataSource.Yahoo -> yahoo.PriceAsync(symbol, ct) | DataSource.Alpaca -> alpaca.PriceAsync(symbol, ct) | DataSource.LsSecurities -> ls.PriceAsync(symbol, ct) | _ -> invalidOp "Provider is not implemented"
        return MarketDataPriceResponse(provider.ToString(), symbol, price, DateTime.UtcNow)
    }

    member this.IntradayAsync(request: MarketDataIntradayRequest, ct: CancellationToken) = task {
        ContractPolicy.validateVersion request.ContractVersion
        let provider = ContractPolicy.normalizeProvider request.Provider
        let descriptor = DataProviderCatalog.Get(provider) |> fun item -> MarketRegionCatalog.Get(item.MarketRegion)
        let zone = TimeZoneInfo.FindSystemTimeZoneById(descriptor.TimeZoneId)
        let localStart = request.SessionDate.ToDateTime(TimeOnly.FromTimeSpan(descriptor.RegularOpen), DateTimeKind.Unspecified)
        let localEnd = request.SessionDate.ToDateTime(TimeOnly.FromTimeSpan(descriptor.RegularClose), DateTimeKind.Unspecified)
        let fromUtc, toUtc = TimeZoneInfo.ConvertTimeToUtc(localStart, zone), TimeZoneInfo.ConvertTimeToUtc(localEnd, zone)
        return! this.HistoricalAsync(MarketDataProviderRequest(request.ContractVersion, request.Provider, request.Symbol, TimeFrame.OneMinute.ToString(), fromUtc, toUtc, request.Persist), ct)
    }
