using System.Globalization;
using StockTrader.Application.Execution;

namespace StockTrader.Application.Trading;

public sealed record TradeRecommendationActivity(
    long Id,
    long? SourceSignalId,
    string Symbol,
    PatternType PatternType,
    string? CustomPatternName,
    decimal EntryPrice,
    decimal StopLossPrice,
    decimal TargetPrice,
    decimal PositionSize,
    int ShareQuantity,
    decimal Expectancy,
    bool WasExecuted,
    OrderMode Mode,
    DateTime GeneratedAt,
    DateTime? EntryRequestedAt,
    int? EntryAccountId,
    bool HasEntryOrderId,
    string? EntryExecutionNote);

public sealed record CompletedTradeActivity(
    long Id,
    string Symbol,
    PatternType PatternType,
    string? CustomPatternName,
    decimal EntryPrice,
    decimal ExitPrice,
    int Quantity,
    decimal PnL,
    decimal PnLPercent,
    string ExitReason,
    DateTime EntryTime,
    DateTime ExitTime);

public sealed record TradeHistorySlice(
    int TotalCount,
    IReadOnlyList<CompletedTradeActivity> Trades);

public interface ITradeActivityStore
{
    Task<IReadOnlyList<TradeRecommendationActivity>> GetRecommendationsAsync(
        int count,
        CancellationToken ct = default);

    Task<TradeHistorySlice> GetHistoryAsync(
        PatternType? patternType,
        DateTime? from,
        DateTime? to,
        int skip,
        int take,
        CancellationToken ct = default);
}

public sealed record TradeRecommendationView(
    long Id,
    long? SourceSignalId,
    string Symbol,
    string Pattern,
    string PatternName,
    decimal EntryPrice,
    decimal StopLossPrice,
    decimal TargetPrice,
    decimal PositionSize,
    int ShareQuantity,
    decimal Expectancy,
    decimal RiskRewardRatio,
    decimal StopLossPercent,
    bool WasExecuted,
    string EntryStatus,
    int? AccountId,
    bool HasBrokerOrderId,
    long PendingSeconds,
    string? Note,
    string Mode,
    string ModeName,
    DateTime GeneratedAt,
    DateTime? EntryRequestedAt);

public sealed record TradeRecommendationPage(
    int Count,
    IReadOnlyList<TradeRecommendationView> Recommendations);

public sealed record TradeHistoryView(
    long Id,
    string Symbol,
    string Pattern,
    string PatternName,
    decimal EntryPrice,
    decimal ExitPrice,
    int Quantity,
    decimal PnL,
    decimal PnLPercent,
    bool IsWin,
    string ExitReason,
    DateTime EntryTime,
    DateTime ExitTime,
    int HoldingDays);

public sealed record TradeHistoryPage(
    int TotalCount,
    int Skip,
    int Take,
    IReadOnlyList<TradeHistoryView> Trades);

public sealed record TradeHistoryQuery(
    string? Pattern,
    string? From,
    string? To,
    int? Skip,
    int? Take);

public sealed record TradeActivityQueryOutcome<T>(T? Value, IReadOnlyList<string> Errors)
    where T : class
{
    public bool Succeeded => Value is not null && Errors.Count == 0;
}

public interface ITradeActivityQuery
{
    Task<TradeActivityQueryOutcome<TradeRecommendationPage>> GetRecommendationsAsync(
        int? count,
        CancellationToken ct = default);

    Task<TradeActivityQueryOutcome<TradeHistoryPage>> GetHistoryAsync(
        TradeHistoryQuery query,
        CancellationToken ct = default);
}

public sealed class TradeActivityQueryService(
    ITradeActivityStore store,
    TimeProvider timeProvider) : ITradeActivityQuery
{
    public async Task<TradeActivityQueryOutcome<TradeRecommendationPage>> GetRecommendationsAsync(
        int? count,
        CancellationToken ct = default)
    {
        var requestedCount = count ?? TradeActivityQueryPolicy.DefaultRecommendationCount;
        var errors = TradeActivityQueryPolicy.ValidateRecommendationCount(requestedCount);
        if (errors.Count > 0)
            return new(null, errors);

        var observedAt = timeProvider.GetUtcNow().UtcDateTime;
        var rows = await store.GetRecommendationsAsync(requestedCount, ct);
        var recommendations = rows.Select(row => Project(row, observedAt)).ToArray();
        return new(new(recommendations.Length, recommendations), []);
    }

    public async Task<TradeActivityQueryOutcome<TradeHistoryPage>> GetHistoryAsync(
        TradeHistoryQuery query,
        CancellationToken ct = default)
    {
        var skip = query.Skip ?? 0;
        var take = query.Take ?? TradeActivityQueryPolicy.DefaultHistoryPageSize;
        var errors = new List<string>();
        var pattern = TradeActivityQueryPolicy.ParsePattern(query.Pattern, errors);
        var from = TradeActivityQueryPolicy.ParseUtc(query.From, "시작일", errors);
        var to = TradeActivityQueryPolicy.ParseUtc(query.To, "종료일", errors);
        errors.AddRange(TradeActivityQueryPolicy.ValidateHistory(
            pattern, from, to, skip, take));
        if (errors.Count > 0)
            return new(null, errors);

        var slice = await store.GetHistoryAsync(
            pattern, from, to, skip, take, ct);
        var trades = slice.Trades.Select(Project).ToArray();
        return new(new(slice.TotalCount, skip, take, trades), []);
    }

    private static TradeRecommendationView Project(
        TradeRecommendationActivity row,
        DateTime observedAt)
    {
        var status = LiveEntryOrderStatusPolicy.Evaluate(new LiveEntryOrderStatusInput(
            row.WasExecuted,
            row.EntryRequestedAt,
            row.EntryAccountId,
            row.HasEntryOrderId,
            row.EntryExecutionNote), observedAt);
        var stopDistance = Math.Abs(row.EntryPrice - row.StopLossPrice);
        return new(
            row.Id,
            row.SourceSignalId,
            row.Symbol,
            row.PatternType.ToString(),
            PatternCatalog.DisplayName(row.PatternType, row.CustomPatternName),
            row.EntryPrice,
            row.StopLossPrice,
            row.TargetPrice,
            row.PositionSize,
            row.ShareQuantity,
            row.Expectancy,
            RiskRewardRatioPolicy.CalculateWithAbsoluteStopDistance(
                row.EntryPrice, row.StopLossPrice, row.TargetPrice),
            row.EntryPrice == 0 ? 0 : stopDistance / row.EntryPrice,
            row.WasExecuted,
            status.State.ToString(),
            status.AccountId,
            status.HasBrokerOrderId,
            status.PendingSeconds,
            status.Note,
            row.Mode.ToString(),
            OrderModeCatalog.Get(row.Mode).DisplayName,
            row.GeneratedAt,
            status.RequestedAt);
    }

    private static TradeHistoryView Project(CompletedTradeActivity row) => new(
        row.Id,
        row.Symbol,
        row.PatternType.ToString(),
        PatternCatalog.DisplayName(row.PatternType, row.CustomPatternName),
        row.EntryPrice,
        row.ExitPrice,
        row.Quantity,
        row.PnL,
        row.PnLPercent,
        row.PnL > 0,
        row.ExitReason,
        row.EntryTime,
        row.ExitTime,
        Math.Max(0, (row.ExitTime - row.EntryTime).Days));
}

public static class TradeActivityQueryPolicy
{
    public const int DefaultRecommendationCount = 50;
    public const int DefaultHistoryPageSize = 50;
    public const int MaximumPageSize = 500;

    public static IReadOnlyList<string> ValidateRecommendationCount(int count) =>
        count is < 1 or > MaximumPageSize
            ? [$"추천 조회 건수는 1 이상 {MaximumPageSize} 이하여야 합니다."]
            : [];

    public static PatternType? ParsePattern(string? value, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (Enum.TryParse<PatternType>(value.Trim(), ignoreCase: true, out var pattern)
            && PatternCatalog.TryGet(pattern, out _))
            return pattern;
        errors.Add($"알 수 없는 전략 코드({value.Trim()})입니다.");
        return null;
    }

    public static DateTime? ParseUtc(
        string? value,
        string label,
        ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (DateTimeOffset.TryParse(
                value.Trim(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces
                    | DateTimeStyles.AssumeUniversal
                    | DateTimeStyles.AdjustToUniversal,
                out var timestamp))
            return timestamp.UtcDateTime;
        errors.Add($"거래 이력 {label} 형식이 올바르지 않습니다.");
        return null;
    }

    public static IReadOnlyList<string> ValidateHistory(
        PatternType? pattern,
        DateTime? from,
        DateTime? to,
        int skip,
        int take)
    {
        var errors = new List<string>();
        if (pattern.HasValue && !PatternCatalog.TryGet(pattern.Value, out _))
            errors.Add($"알 수 없는 전략 코드({(int)pattern.Value})입니다.");
        if (skip < 0)
            errors.Add("건너뛸 거래 수는 0 이상이어야 합니다.");
        if (take is < 1 or > MaximumPageSize)
            errors.Add($"거래 이력 조회 건수는 1 이상 {MaximumPageSize} 이하여야 합니다.");
        if (from.HasValue && to.HasValue && from.Value > to.Value)
            errors.Add("거래 이력 시작일은 종료일보다 늦을 수 없습니다.");
        return errors;
    }
}
