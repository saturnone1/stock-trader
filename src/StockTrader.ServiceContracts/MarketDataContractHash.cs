using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace StockTrader.ServiceContracts.MarketData;

public static class MarketDataContractHash
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public static string Bar(MarketDataBar bar) => Sha256(string.Join('|',
        bar.Symbol,
        bar.TimeFrame,
        Utc(bar.TimestampUtc).ToString("O", Invariant),
        bar.Open.ToString("G29", Invariant),
        bar.High.ToString("G29", Invariant),
        bar.Low.ToString("G29", Invariant),
        bar.Close.ToString("G29", Invariant),
        bar.Volume.ToString(Invariant),
        bar.Vwap?.ToString("G29", Invariant) ?? string.Empty));

    public static string Content(IEnumerable<MarketDataBar> bars) => Sha256(string.Join('\n',
        bars.OrderBy(bar => Utc(bar.TimestampUtc)).Select(Bar)));

    public static string Evidence(
        string provider,
        string symbol,
        string timeFrame,
        string adjustmentMode,
        string calendarVersion,
        long revision,
        string contentHash) => Sha256(string.Join('|',
            MarketDataContractVersions.Current,
            provider,
            symbol,
            timeFrame,
            adjustmentMode,
            calendarVersion,
            revision,
            contentHash));

    public static DateTime Utc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
