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
        IEnumerable<MarketDataBar> actual) =>
        DescribeDifference(expected, actual) is null;

    public static string? DescribeDifference(
        IEnumerable<MarketDataBar> expected,
        IEnumerable<MarketDataBar> actual)
    {
        var left = expected.OrderBy(item => MarketDataContractHash.Utc(item.TimestampUtc)).ToArray();
        var right = actual.OrderBy(item => MarketDataContractHash.Utc(item.TimestampUtc)).ToArray();
        if (left.Length != right.Length)
            return $"bar count expected {left.Length}, projected {right.Length}";

        for (var index = 0; index < left.Length; index++)
        {
            if (BarEquals(left[index], right[index]))
                continue;

            var expectedBar = left[index];
            var actualBar = right[index];
            return $"bar {index} expected "
                   + $"{Identity(expectedBar)} {Values(expectedBar)}, projected "
                   + $"{Identity(actualBar)} {Values(actualBar)}";
        }

        return null;
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

    private static string Identity(MarketDataBar bar) =>
        $"{bar.Symbol}/{bar.TimeFrame}/{MarketDataContractHash.Utc(bar.TimestampUtc):O}";

    private static string Values(MarketDataBar bar) =>
        $"O={bar.Open:G29},H={bar.High:G29},L={bar.Low:G29},C={bar.Close:G29},"
        + $"V={bar.Volume},VWAP={bar.Vwap?.ToString("G29") ?? "null"}";
}
