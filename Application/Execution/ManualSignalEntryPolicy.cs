using StockTrader.Application.Signals;

namespace StockTrader.Application.Execution;

public sealed record ManualSignalEntryCandidate(
    string Symbol,
    DateTime DetectedAtUtc,
    decimal EntryPrice,
    decimal StopLossPrice,
    decimal TargetPrice);

public sealed record ManualRecommendationEntryCandidate(
    string Symbol,
    int ShareQuantity,
    decimal EntryPrice,
    decimal StopLossPrice,
    decimal TargetPrice);

public sealed record ManualEntryValidationDecision(
    bool IsAllowed,
    string Code,
    string? Message)
{
    public static ManualEntryValidationDecision Allow() => new(true, "Allowed", null);

    public static ManualEntryValidationDecision Reject(string code, string message) =>
        new(false, code, message);
}

/// <summary>
/// Pure validation policy for operator-requested entries. It runs before sizing or broker access
/// and delegates all observation-time semantics to the shared signal freshness policy.
/// </summary>
public sealed class ManualSignalEntryPolicy(SignalFreshnessPolicy freshness)
{
    public ManualEntryValidationDecision EvaluateSignal(
        ManualSignalEntryCandidate signal,
        DateTime observedAtUtc,
        bool isMarketOpen,
        DateTime marketLocalNow)
    {
        if (!isMarketOpen)
        {
            return ManualEntryValidationDecision.Reject(
                "MarketClosed",
                $"장외 시간입니다 (ET {marketLocalNow:HH:mm}, {marketLocalNow.DayOfWeek}). "
                + "정규장(09:30–16:00 ET) 중에 다시 시도하세요.");
        }

        var status = freshness.Evaluate(signal.DetectedAtUtc, observedAtUtc);
        if (status == SignalFreshnessStatus.FutureDated)
        {
            return ManualEntryValidationDecision.Reject(
                "FutureDated",
                $"{signal.Symbol} 시그널의 생성 시각이 현재보다 미래입니다. "
                + "서버 시간과 데이터 시각을 확인하세요.");
        }

        if (status == SignalFreshnessStatus.Expired)
        {
            var age = observedAtUtc - signal.DetectedAtUtc;
            return ManualEntryValidationDecision.Reject(
                "Expired",
                $"{signal.Symbol} 시그널이 {age.TotalHours:F0}시간 전 생성됨. "
                + $"{freshness.ActionableLifetime.TotalHours:0.##}시간 초과 시그널은 "
                + "주문할 수 없습니다.");
        }

        if (signal.StopLossPrice >= signal.EntryPrice)
        {
            return ManualEntryValidationDecision.Reject(
                "InvalidStop",
                $"{signal.Symbol} 손절가({signal.StopLossPrice:F2})가 "
                + $"진입가({signal.EntryPrice:F2}) 이상입니다. 시그널이 유효하지 않습니다.");
        }

        if (signal.TargetPrice <= signal.EntryPrice)
        {
            return ManualEntryValidationDecision.Reject(
                "InvalidTarget",
                $"{signal.Symbol} 목표가({signal.TargetPrice:F2})가 "
                + $"진입가({signal.EntryPrice:F2}) 이하입니다. 시그널이 유효하지 않습니다.");
        }

        return ManualEntryValidationDecision.Allow();
    }

    public static ManualEntryValidationDecision EvaluateRecommendation(
        ManualRecommendationEntryCandidate recommendation)
    {
        if (recommendation.ShareQuantity <= 0)
        {
            return ManualEntryValidationDecision.Reject(
                "ZeroQuantity",
                $"{recommendation.Symbol}: 계산된 주문 수량이 0입니다. 계좌 잔고를 확인하세요.");
        }

        if (recommendation.StopLossPrice >= recommendation.EntryPrice
            || recommendation.TargetPrice <= recommendation.EntryPrice)
        {
            return ManualEntryValidationDecision.Reject(
                "InvalidRecommendationPrices",
                $"{recommendation.Symbol}: 추천 재계산 후 가격 구조가 유효하지 않습니다.");
        }

        return ManualEntryValidationDecision.Allow();
    }
}
