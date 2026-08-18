using StockTrader.Domain.Strategies;

namespace StockTrader.Application.Reporting;

public sealed record DailyReportTradeSnapshot(
    string Symbol,
    decimal EntryPrice,
    int Quantity,
    decimal PnL,
    DateTime ExitTime);

public sealed record DailyReportSignalSnapshot(
    string Symbol,
    PatternType PatternType,
    decimal EntryPrice,
    DateTime GeneratedAt);

public sealed record DailyReportActivitySnapshot(
    IReadOnlyList<DailyReportTradeSnapshot> Trades,
    IReadOnlyList<DailyReportSignalSnapshot> Signals);

public sealed record DailyReportData(
    DateOnly ReportDate,
    int TotalSignals,
    int ExecutedTrades,
    decimal DailyPnl,
    decimal DailyPnlPercent,
    IReadOnlyList<string> TopSignals,
    IReadOnlyList<string> ExecutedSymbols,
    string MarketRegimeSummary);

public interface IDailyReportActivityStore
{
    Task<DailyReportActivitySnapshot> ReadAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct = default);
}

public interface IActiveAccountEquityReader
{
    Task<decimal?> GetAsync(CancellationToken ct = default);
}

public interface IDailyReportPublisher
{
    Task PublishAsync(
        DailyReportData report,
        CancellationToken ct = default);
}

public interface IDailyReportScheduleQuery
{
    Task<TimeOnly?> GetKoreanReportTimeAsync(CancellationToken ct = default);
}

public interface IDailyReportGenerator
{
    Task<DailyReportData> GenerateAndPublishAsync(
        TimeZoneInfo marketTimeZone,
        CancellationToken ct = default);
}
