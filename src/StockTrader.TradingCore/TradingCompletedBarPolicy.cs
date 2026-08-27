using StockTrader.Domain.MarketData;

namespace StockTrader.TradingCore.Execution;

/// <summary>Resolves a fail-closed upper bound that never includes the active market daily bar.</summary>
public sealed record TradingCompletedBarWindow(
    DateTime CompletedThroughUtc,
    DateOnly ExpectedLastSessionDate);

public static class TradingCompletedBarPolicy
{
    public static TradingCompletedBarWindow Resolve(DateTime observedAtUtc, string provider)
    {
        var utc = observedAtUtc.Kind == DateTimeKind.Utc
            ? observedAtUtc
            : observedAtUtc.ToUniversalTime();
        if (!Enum.TryParse<DataSource>(provider, true, out var source))
            throw new ArgumentException("Unsupported market-data provider.", nameof(provider));
        var market = DataProviderCatalog.Get(source).MarketRegion;
        var descriptor = MarketRegionCatalog.Get(market);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(descriptor.TimeZoneId);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
        var day = ExchangeCalendarCatalog.GetTradingDay(
            market, DateOnly.FromDateTime(local));
        var close = day.EarlyCloseTime ?? descriptor.RegularClose;
        var candidate = day.IsTradingDay && local.TimeOfDay >= close
            ? DateOnly.FromDateTime(local)
            : DateOnly.FromDateTime(local).AddDays(-1);
        while (!ExchangeCalendarCatalog.GetTradingDay(
                   market, candidate).IsTradingDay)
            candidate = candidate.AddDays(-1);
        var nextLocalMidnight = DateTime.SpecifyKind(
            candidate.ToDateTime(TimeOnly.MinValue).AddDays(1), DateTimeKind.Unspecified);
        return new(
            TimeZoneInfo.ConvertTimeToUtc(nextLocalMidnight, zone).AddTicks(-1),
            candidate);
    }
}
