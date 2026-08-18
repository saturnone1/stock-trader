using StockTrader.Application.Trading;
using StockTrader.Data.Repositories;
using StockTrader.Models;

namespace StockTrader.Api;

public static class SignalEndpoints
{
    public static RouteGroupBuilder MapSignalApi(this RouteGroupBuilder group)
    {
        // GET /api/signals?pattern=&search=&sort=latest&style=
        group.MapGet("/signals", async (
            IPatternSignalStore signalStore,
            IPatternStatsRepository statsRepo,
            string? pattern,
            string? search,
            string? sort,
            string? style,
            CancellationToken ct) =>
        {
            var signals = await signalStore.GetActiveSignalsAsync(ct);

            // 패턴 필터
            if (!string.IsNullOrWhiteSpace(pattern) &&
                Enum.TryParse<PatternType>(pattern, ignoreCase: true, out var patternEnum))
            {
                signals = signals.Where(s => s.PatternType == patternEnum).ToList();
            }

            // 종목 검색
            if (!string.IsNullOrWhiteSpace(search))
            {
                var searchUpper = search.ToUpperInvariant();
                signals = signals.Where(s => s.Symbol.Contains(searchUpper, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            // 스타일 필터 (Long/Short — PatternSignal에 Direction 필드가 없으므로 추후 확장용)
            // style 파라미터는 현재 예약 (향후 TradeDirection 연동 시 활성화)

            // 패턴 통계 조회 (R:R 정렬에 필요)
            var allStats = await statsRepo.GetAllAsync(ct);
            var statsLookup = allStats.ToDictionary(s => s.PatternType, s => s);

            // 정렬
            IEnumerable<PatternSignal> sorted = sort?.ToLowerInvariant() switch
            {
                "confidence" => signals.OrderByDescending(s => s.Confidence),
                "rr"         => signals.OrderByDescending(s =>
                    s.EntryPrice > 0 && s.StopLossPrice > 0 && s.StopLossPrice < s.EntryPrice
                        ? (s.TargetPrice - s.EntryPrice) / (s.EntryPrice - s.StopLossPrice)
                        : 0m),
                _            => signals.OrderByDescending(s => s.DetectedAt) // latest (default)
            };

            return Results.Ok(new
            {
                Count = signals.Count,
                Signals = sorted.Select(s =>
                {
                    var rr = s.EntryPrice > 0 && s.StopLossPrice > 0 && s.StopLossPrice < s.EntryPrice
                        ? (s.TargetPrice - s.EntryPrice) / (s.EntryPrice - s.StopLossPrice)
                        : 0m;

                    statsLookup.TryGetValue(s.PatternType, out var stat);

                    return new
                    {
                        s.Id,
                        s.Symbol,
                        Pattern       = s.PatternType.ToString(),
                        s.EntryPrice,
                        s.StopLossPrice,
                        s.TargetPrice,
                        s.Confidence,
                        RiskReward    = rr,
                        s.Details,
                        DetectedAt    = s.DetectedAt.ToString("o"),
                        PatternWinRate    = stat?.WinRate,
                        PatternExpectancy = stat?.Expectancy
                    };
                })
            });
        }).RequireAuthorization();

        return group;
    }
}
