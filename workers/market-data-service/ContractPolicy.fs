namespace StockTrader.MarketDataService

open System
open System.Globalization
open StockTrader.Domain.MarketData
open StockTrader.ServiceContracts.MarketData

module ContractPolicy =
    let private invariant = CultureInfo.InvariantCulture

    let sha256 value = MarketDataContractHash.Sha256(value)

    let utc (value: DateTime) =
        match value.Kind with
        | DateTimeKind.Utc -> value
        | DateTimeKind.Local -> value.ToUniversalTime()
        | _ -> DateTime.SpecifyKind(value, DateTimeKind.Utc)

    let normalizeProvider (value: string) =
        match Enum.TryParse<DataSource>(value, true) with
        | true, provider when DataProviderCatalog.Get(provider).IsImplemented -> provider
        | _ -> invalidArg "provider" $"Unsupported market-data provider: {value}"

    let normalizeTimeFrame (value: string) =
        match Enum.TryParse<TimeFrame>(value, true) with
        | true, frame -> frame
        | _ -> invalidArg "timeFrame" $"Unsupported timeframe: {value}"

    let normalizeAdjustment provider frame (value: string) =
        let expected = PriceAdjustmentCatalog.Resolve(provider, frame)
        match Enum.TryParse<PriceAdjustmentMode>(value, true) with
        | true, parsed when parsed = expected -> expected
        | _ -> invalidArg "adjustmentMode" $"Expected {expected} for {provider}/{frame}"

    let normalizeSymbol value =
        let symbol = MarketSymbolPolicy.Normalize(value)
        if not (MarketSymbolPolicy.IsValid(symbol)) then
            invalidArg "symbol" $"Invalid market symbol: {value}"
        symbol

    let validateVersion version =
        if version <> MarketDataContractVersions.Current then
            invalidArg "contractVersion" $"Unsupported market-data contract version: {version}"

    let normalizeBar (bar: MarketDataBar) =
        let symbol = normalizeSymbol bar.Symbol
        let frame = normalizeTimeFrame bar.TimeFrame
        if bar.Open <= 0m || bar.High <= 0m || bar.Low <= 0m || bar.Close <= 0m then
            invalidArg "bars" "OHLC values must be positive"
        if bar.High < max bar.Open bar.Close || bar.Low > min bar.Open bar.Close || bar.High < bar.Low then
            invalidArg "bars" "OHLC ordering is invalid"
        if bar.Volume < 0L then invalidArg "bars" "Volume cannot be negative"
        if bar.Vwap.HasValue && bar.Vwap.Value <= 0m then
            invalidArg "bars" "VWAP must be positive when present"
        MarketDataBar(
            symbol, frame.ToString(), utc bar.TimestampUtc,
            bar.Open, bar.High, bar.Low, bar.Close, bar.Volume, bar.Vwap)

    let barHash (bar: MarketDataBar) = MarketDataContractHash.Bar(bar)

    let contentHash (bars: seq<MarketDataBar>) =
        MarketDataContractHash.Content(bars)

    let evidenceId provider symbol frame adjustment calendar revision hash =
        MarketDataContractHash.Evidence(provider, symbol, frame, adjustment, calendar, revision, hash)

    let ensureRange fromUtc toUtc =
        let fromValue, toValue = utc fromUtc, utc toUtc
        if fromValue > toValue then invalidArg "range" "FromUtc must not be after ToUtc"
        fromValue, toValue
