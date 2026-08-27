namespace StockTrader.MarketDataService

open System

type ServiceSettings = {
    DatabasePath: string
    ClientCaPath: string
    EdgeRoleDnsName: string
    TradingCoreRoleDnsName: string
    YahooBaseUrl: string
    YahooUserAgent: string
    YahooDelayMs: int
    AlpacaKey: string
    AlpacaSecret: string
    AlpacaFeed: string
    AlpacaDataBaseUrl: string
    AlpacaStreamUrl: string
    LsAppKey: string
    LsAppSecret: string
    LsBaseUrl: string
}

module ServiceSettings =
    let private value name fallback =
        match Environment.GetEnvironmentVariable(name) with
        | null | "" -> fallback
        | configured -> configured

    let private positiveInt name fallback =
        match Int32.TryParse(value name "") with
        | true, parsed when parsed > 0 -> parsed
        | _ -> fallback

    let load () = {
        DatabasePath = value "MARKET_DATA_DATABASE_PATH" "/data/marketdata.db"
        ClientCaPath = value "MARKET_DATA_CLIENT_CA_PATH" ""
        EdgeRoleDnsName = value "MARKET_DATA_EDGE_ROLE_DNS" "edge-market-data.stocktrader.internal"
        TradingCoreRoleDnsName = value "MARKET_DATA_TRADING_CORE_ROLE_DNS" "trading-core-evidence.stocktrader.internal"
        YahooBaseUrl = value "YAHOO_BASE_URL" "https://query1.finance.yahoo.com"
        YahooUserAgent = value "YAHOO_USER_AGENT" "StockTrader-MarketData/1.0"
        YahooDelayMs = positiveInt "YAHOO_RATE_LIMIT_DELAY_MS" 200
        AlpacaKey = value "ALPACA_API_KEY" ""
        AlpacaSecret = value "ALPACA_API_SECRET" ""
        AlpacaFeed = value "ALPACA_DATA_FEED" "iex"
        AlpacaDataBaseUrl = value "ALPACA_DATA_BASE_URL" "https://data.alpaca.markets"
        AlpacaStreamUrl = value "ALPACA_STREAM_URL" "wss://stream.data.alpaca.markets/v2/iex"
        LsAppKey = value "LS_APP_KEY" ""
        LsAppSecret = value "LS_APP_SECRET" ""
        LsBaseUrl = value "LS_BASE_URL" "https://openapi.ls-sec.co.kr:8080"
    }

    let private validAbsoluteUri scheme value =
        match Uri.TryCreate(value, UriKind.Absolute) with
        | true, uri -> uri |> Option.ofObj |> Option.exists (fun value -> value.Scheme = scheme)
        | _ -> false

    let validate settings =
        [ if String.IsNullOrWhiteSpace(settings.ClientCaPath) then
              "MARKET_DATA_CLIENT_CA_PATH is required"
          if String.IsNullOrWhiteSpace(settings.DatabasePath) then
              "MARKET_DATA_DATABASE_PATH is required"
          if not (validAbsoluteUri Uri.UriSchemeHttps settings.AlpacaDataBaseUrl) then
              "ALPACA_DATA_BASE_URL must be an absolute HTTPS URI"
          if not (validAbsoluteUri "wss" settings.AlpacaStreamUrl) then
              "ALPACA_STREAM_URL must be an absolute WSS URI" ]
