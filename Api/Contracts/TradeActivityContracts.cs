using StockTrader.Application.Trading;

namespace StockTrader.Api.Contracts;

public sealed record TradeActivityErrorResponse(IReadOnlyList<string> Errors);

public sealed record TradeRecommendationResponse(
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
    DateTime? EntryRequestedAt)
{
    public static TradeRecommendationResponse Create(TradeRecommendationView value) => new(
        value.Id,
        value.SourceSignalId,
        value.Symbol,
        value.Pattern,
        value.PatternName,
        value.EntryPrice,
        value.StopLossPrice,
        value.TargetPrice,
        value.PositionSize,
        value.ShareQuantity,
        value.Expectancy,
        value.RiskRewardRatio,
        value.StopLossPercent,
        value.WasExecuted,
        value.EntryStatus,
        value.AccountId,
        value.HasBrokerOrderId,
        value.PendingSeconds,
        value.Note,
        value.Mode,
        value.ModeName,
        value.GeneratedAt,
        value.EntryRequestedAt);
}

public sealed record TradeRecommendationListResponse(
    int Count,
    IReadOnlyList<TradeRecommendationResponse> Recommendations)
{
    public static TradeRecommendationListResponse Create(TradeRecommendationPage value) => new(
        value.Count,
        value.Recommendations.Select(TradeRecommendationResponse.Create).ToArray());
}

public sealed record TradeHistoryItemResponse(
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
    int HoldingDays)
{
    public static TradeHistoryItemResponse Create(TradeHistoryView value) => new(
        value.Id,
        value.Symbol,
        value.Pattern,
        value.PatternName,
        value.EntryPrice,
        value.ExitPrice,
        value.Quantity,
        value.PnL,
        value.PnLPercent,
        value.IsWin,
        value.ExitReason,
        value.EntryTime,
        value.ExitTime,
        value.HoldingDays);
}

public sealed record TradeHistoryResponse(
    int TotalCount,
    int Skip,
    int Take,
    IReadOnlyList<TradeHistoryItemResponse> Trades)
{
    public static TradeHistoryResponse Create(TradeHistoryPage value) => new(
        value.TotalCount,
        value.Skip,
        value.Take,
        value.Trades.Select(TradeHistoryItemResponse.Create).ToArray());
}
