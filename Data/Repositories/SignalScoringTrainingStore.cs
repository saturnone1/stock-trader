using Microsoft.EntityFrameworkCore;
using StockTrader.Application.MachineLearning;

namespace StockTrader.Data.Repositories;

/// <summary>
/// 실현 거래를 원래 신호의 진입 시점 피처에 연결합니다. 부분청산은 신호별로 합산하여
/// 하나의 진입 판단이 학습 데이터에서 여러 번 가중되지 않도록 합니다.
/// </summary>
public sealed class SignalScoringTrainingStore(IDbContextFactory<AppDbContext> dbFactory)
    : ISignalScoringTrainingStore
{
    public async Task<IReadOnlyList<SignalScoringTrainingSample>> GetRecentAsync(
        int limit,
        CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 50_000);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var outcomes = db.TradeRecords
            .AsNoTracking()
            .Where(trade => trade.SourceSignalId != null)
            .GroupBy(trade => trade.SourceSignalId!.Value)
            .Select(group => new
            {
                SourceSignalId = group.Key,
                RealizedPnl = group.Sum(trade => trade.PnL),
            });

        var rows = await (
                from outcome in outcomes
                join signal in db.PatternSignals.AsNoTracking()
                    on outcome.SourceSignalId equals signal.Id
                where signal.SignalBarAt != null
                    && signal.ScoringFeatureVersion
                        == SignalScoringFeatureSchema.CurrentVersion
                    && signal.ScoringRsi != null
                    && signal.ScoringBollingerPosition != null
                    && signal.ScoringVolumeRatio != null
                    && signal.ScoringMarketRegimeCode != null
                    && signal.ScoringAtrPercent != null
                    && signal.ScoringHistoricalWinRate != null
                    && signal.ScoringRiskRewardRatio != null
                    && signal.ScoringPriceVsLongMovingAverage != null
                    && signal.ScoringLongTrendHistoryAvailable != null
                orderby signal.SignalBarAt descending, signal.Id descending
                select new
                {
                    signal.Id,
                    SignalBarAt = signal.SignalBarAt!.Value,
                    signal.PatternType,
                    Version = signal.ScoringFeatureVersion!.Value,
                    Rsi = signal.ScoringRsi!.Value,
                    BollingerPosition = signal.ScoringBollingerPosition!.Value,
                    VolumeRatio = signal.ScoringVolumeRatio!.Value,
                    MarketRegimeCode = signal.ScoringMarketRegimeCode!.Value,
                    AtrPercent = signal.ScoringAtrPercent!.Value,
                    HistoricalWinRate = signal.ScoringHistoricalWinRate!.Value,
                    RiskRewardRatio = signal.ScoringRiskRewardRatio!.Value,
                    PriceVsLongMovingAverage = signal.ScoringPriceVsLongMovingAverage!.Value,
                    LongTrendHistoryAvailable = signal.ScoringLongTrendHistoryAvailable!.Value,
                    outcome.RealizedPnl,
                })
            .Take(limit)
            .ToListAsync(ct);

        return rows
            .OrderBy(row => row.SignalBarAt)
            .ThenBy(row => row.Id)
            .Select(row => new SignalScoringTrainingSample(
                row.Id,
                row.SignalBarAt,
                new SignalScoringFeatures(
                    row.Version,
                    (float)row.PatternType,
                    row.Rsi,
                    row.BollingerPosition,
                    row.VolumeRatio,
                    row.MarketRegimeCode,
                    row.AtrPercent,
                    row.HistoricalWinRate,
                    row.RiskRewardRatio,
                    row.PriceVsLongMovingAverage,
                    row.LongTrendHistoryAvailable),
                row.RealizedPnl > 0))
            .ToArray();
    }
}
