namespace StockTrader.ServiceContracts.MarketData;

public static class MarketDataContractVersions
{
    public const int Current = 1;
}

public sealed record MarketDataBar(
    string Symbol,
    string TimeFrame,
    DateTime TimestampUtc,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume,
    decimal? Vwap);

public sealed record MarketDataEvidenceContract(
    int ContractVersion,
    string EvidenceId,
    string Provider,
    string Symbol,
    string TimeFrame,
    string AdjustmentMode,
    string Market,
    string CalendarVersion,
    DateTime RequestedFromUtc,
    DateTime RequestedToUtc,
    DateTime? FirstBarUtc,
    DateTime? LastBarUtc,
    long Revision,
    bool IsComplete,
    string ContentHash);

public sealed record MarketDataRangeRequest(
    int ContractVersion,
    string Provider,
    string Symbol,
    string TimeFrame,
    string AdjustmentMode,
    string Market,
    string CalendarVersion,
    DateTime FromUtc,
    DateTime ToUtc);

public sealed record MarketDataRangeResponse(
    MarketDataEvidenceContract Evidence,
    IReadOnlyList<MarketDataBar> Bars);

public static class MarketDataExecutionEvidenceLimits
{
    // Bounds one financial evaluation request and prevents the execution identity from becoming a
    // general historical-data reader. Strategy warmup above this limit is not live-compatible.
    public const int MaximumBars = 2048;
    public static int RequiredDailyLookbackCalendarDays(int requiredBars)
    {
        if (requiredBars < 1 || requiredBars > MaximumBars)
            throw new ArgumentOutOfRangeException(nameof(requiredBars));
        const int calendarDaysPerTradingWeek = 7;
        const int tradingDaysPerWeek = 5;
        const int holidayAndFeedBufferDays = 30;
        return checked((int)Math.Ceiling(
            requiredBars * (decimal)calendarDaysPerTradingWeek / tradingDaysPerWeek)
            + holidayAndFeedBufferDays);
    }
}

public sealed record MarketDataEvidenceVerificationRequest(
    int ContractVersion,
    MarketDataEvidenceContract Evidence);

public sealed record MarketDataEvidenceVerificationResponse(
    int ContractVersion,
    string EvidenceId,
    bool Matches,
    long CurrentRevision,
    string CurrentContentHash,
    string? RejectionReason);

public sealed record MarketDataExecutionWindowRequest(
    int ContractVersion,
    string Provider,
    string Symbol,
    string TimeFrame,
    string AdjustmentMode,
    string Market,
    string CalendarVersion,
    DateTime NotBeforeUtc,
    DateTime CompletedThroughUtc,
    int RequiredBars,
    DateOnly ExpectedLastSessionDate,
    long AfterRevision = 0,
    DateTime? EvaluatedThroughUtc = null);

public sealed record MarketDataExecutionWindowResponse(
    MarketDataEvidenceContract Evidence,
    IReadOnlyList<MarketDataBar> Bars,
    bool PriorEvaluatedRangeCorrected);

public sealed record MarketDataUpsertRequest(
    int ContractVersion,
    string RequestId,
    string Provider,
    string AdjustmentMode,
    string Market,
    string CalendarVersion,
    DateTime? RequestedFromUtc,
    DateTime? RequestedToUtc,
    bool IsComplete,
    IReadOnlyList<MarketDataBar> Bars);

public sealed record MarketDataUpsertResponse(
    string RequestId,
    int Inserted,
    int Unchanged,
    int Corrected,
    long Revision,
    bool AlreadyApplied);

public sealed record MarketDataProviderRequest(
    int ContractVersion,
    string Provider,
    string Symbol,
    string TimeFrame,
    DateTime FromUtc,
    DateTime ToUtc,
    bool Persist);

public sealed record MarketDataIntradayRequest(
    int ContractVersion,
    string Provider,
    string Symbol,
    DateOnly SessionDate,
    bool Persist);

public sealed record MarketDataPriceRequest(
    int ContractVersion,
    string Provider,
    string Symbol);

public sealed record MarketDataPriceResponse(
    string Provider,
    string Symbol,
    decimal Price,
    DateTime ObservedAtUtc);

public sealed record MarketDataSubscriptionRequest(
    int ContractVersion,
    string Provider,
    IReadOnlyList<string> Symbols);

public sealed record MarketDataSubscriptionResponse(
    string Provider,
    IReadOnlyList<string> Symbols,
    long Generation,
    bool StreamingConnected);

public sealed record MarketDataCorrection(
    long Revision,
    string Provider,
    string Symbol,
    string TimeFrame,
    string AdjustmentMode,
    DateTime TimestampUtc,
    string PreviousHash,
    string CurrentHash,
    DateTime OccurredAtUtc);

public sealed record MarketDataCorrectionPage(
    long LatestRevision,
    IReadOnlyList<MarketDataCorrection> Corrections);

public sealed record MarketDataStoredSeries(
    string Provider,
    string Symbol,
    string TimeFrame,
    string AdjustmentMode,
    DateTime FirstBarUtc,
    DateTime LastBarUtc,
    long BarCount,
    long Revision);

public sealed record MarketDataStoredSeriesResponse(
    IReadOnlyList<MarketDataStoredSeries> Series);

public sealed record MarketDataServiceStatus(
    int ContractVersion,
    string Mode,
    bool Ready,
    bool DatabaseReady,
    long LatestRevision,
    long StoredBars,
    string? LastError);
