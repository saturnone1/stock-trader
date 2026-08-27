using StockTrader.Application.Execution;
using StockTrader.Application.MarketData;
using StockTrader.Application.Strategies;
using StockTrader.Application.Trading;
using StockTrader.Domain.MarketData;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Signal;

namespace StockTrader.Services.Order;

internal sealed record TradingCoreManualEntryPreparationResult(
    TradeRecommendation? Recommendation,
    string Message)
{
    public bool Succeeded => Recommendation is not null;
}

/// <summary>
/// Revalidates an operator-selected signal and binds it to fresh, immutable completed-bar evidence.
/// It prepares an entry only; broker access and financial persistence remain in Trading Core.
/// </summary>
internal sealed class TradingCoreManualEntryPreparation(
    IManualOrderSignalStore signals,
    ISignalService recommendations,
    ILiveDailyScanData marketData,
    ManualSignalEntryPolicy policy,
    IMarketCalendar calendar,
    TimeProvider clock)
{
    public async Task<TradingCoreManualEntryPreparationResult> PrepareAsync(
        long signalId,
        CancellationToken ct = default)
    {
        var signal = await signals.LoadAsync(signalId, ct);
        if (signal is null)
            return Reject($"시그널 ID {signalId}을(를) 찾을 수 없습니다.");

        var now = clock.GetUtcNow().UtcDateTime;
        var decision = policy.EvaluateSignal(
            new ManualSignalEntryCandidate(
                signal.Symbol,
                signal.DetectedAt,
                signal.EntryPrice,
                signal.StopLossPrice,
                signal.TargetPrice),
            now,
            calendar.IsMarketOpen(MarketRegion.UnitedStates),
            calendar.GetLocalNow(MarketRegion.UnitedStates));
        if (!decision.IsAllowed)
            return Reject(decision.Message!);
        if (!signal.SignalBarAt.HasValue)
            return Reject("완료 봉 식별자가 없는 기존 시그널은 원격 수동 주문에 사용할 수 없습니다.");

        var barSet = await marketData.LoadBarsAsync(
            signal.Symbol,
            now.AddDays(-StrategyEvaluationPolicy.LiveDailySignalLookbackDays),
            now,
            ct);
        if (!EvidenceMatches(signal, barSet))
            return Reject("시그널의 완료 봉과 현재 시장 데이터 증거가 일치하지 않습니다. 새 시그널을 사용하세요.");

        var evaluated = await recommendations.EvaluateSignalsAsync([signal], ct);
        var recommendation = evaluated.SingleOrDefault();
        if (recommendation is null)
            return Reject("현재 전략·리스크 조건으로 다시 평가했을 때 주문 가능한 추천이 아닙니다.");

        recommendation.SourceSignalId = signal.Id;
        recommendation.Mode = OrderMode.AutoOrder;
        recommendation.MarketDataEvidence = barSet.Evidence;
        var recommendationDecision = ManualSignalEntryPolicy.EvaluateRecommendation(
            new ManualRecommendationEntryCandidate(
                recommendation.Symbol,
                recommendation.ShareQuantity,
                recommendation.EntryPrice,
                recommendation.StopLossPrice,
                recommendation.TargetPrice));
        return recommendationDecision.IsAllowed
            ? new(recommendation, string.Empty)
            : Reject(recommendationDecision.Message!);
    }

    private static bool EvidenceMatches(PatternSignal signal, LiveDailyBarSet bars)
    {
        var evidence = bars.Evidence;
        var signalBar = Utc(signal.SignalBarAt!.Value);
        return evidence.IsComplete
            && evidence.ContractVersion > 0
            && evidence.Symbol.Equals(signal.Symbol, StringComparison.OrdinalIgnoreCase)
            && evidence.TimeFrame.Equals(TimeFrame.Daily.ToString(), StringComparison.OrdinalIgnoreCase)
            && evidence.LastBarUtc.HasValue
            && Utc(evidence.LastBarUtc.Value) == signalBar
            && bars.Bars.Count >= StrategyEvaluationPolicy.LiveScannerMinimumBars;
    }

    private static DateTime Utc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    private static TradingCoreManualEntryPreparationResult Reject(string message) =>
        new(null, message);
}
