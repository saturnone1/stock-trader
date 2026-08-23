namespace StockTrader.ServiceContracts.MarketData;

/// <summary>
/// Compares the financial meaning of two bar sets after a storage round trip.
/// Decimal scale is representation metadata: 306 and 306.0 are the same price.
/// Transport evidence hashes are still validated before this policy is used.
/// </summary>
public static class MarketDataContractParity
{
    public static bool ContentEquals(
        IEnumerable<MarketDataBar> expected,
        IEnumerable<MarketDataBar> actual)
    {
        var left = expected.OrderBy(item => MarketDataContractHash.Utc(item.TimestampUtc)).ToArray();
        var right = actual.OrderBy(item => MarketDataContractHash.Utc(item.TimestampUtc)).ToArray();
        if (left.Length != right.Length)
            return false;

        return left.Zip(right).All(pair => BarEquals(pair.First, pair.Second));
    }

    public static bool BarEquals(MarketDataBar expected, MarketDataBar actual) =>
        string.Equals(expected.Symbol, actual.Symbol, StringComparison.Ordinal)
        && string.Equals(expected.TimeFrame, actual.TimeFrame, StringComparison.Ordinal)
        && MarketDataContractHash.Utc(expected.TimestampUtc)
            == MarketDataContractHash.Utc(actual.TimestampUtc)
        && expected.Open == actual.Open
        && expected.High == actual.High
        && expected.Low == actual.Low
        && expected.Close == actual.Close
        && expected.Volume == actual.Volume
        && expected.Vwap == actual.Vwap;
}
