using StockTrader.Application.Backtesting;
using StockTrader.Application.MarketData;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Configuration;

namespace StockTrader.Application.Optimization;

/// <summary>한 최적화 요청에 대해 이미 준비된 시장 데이터와 실행 조건을 묶습니다.</summary>
public sealed record OptimizationEvaluationContext(
    OptimizeRequest Request,
    IReadOnlyDictionary<TimeFrame, IReadOnlyDictionary<string, PreparedSymbolData>> DataByTimeFrame,
    IReadOnlyDictionary<string, PreparedSymbolData> DefaultData,
    Dictionary<DateOnly, MarketRegime> Regimes,
    OptimizationRiskParameters Risk,
    IReadOnlyDictionary<TimeFrame, MarketDataEvidence> EvidenceByTimeFrame,
    MarketDataEvidence DefaultEvidence)
{
    public PatternSettings PatternSettings { get; init; } = new();

    /// <summary>
    /// 해당 타임프레임의 데이터 근거. 같은 공급자라도 타임프레임에 따라 조정 모드가
    /// 다를 수 있으므로(LS증권 분봉), 실행에 사용한 데이터와 짝이 맞는 근거를 반환한다.
    /// </summary>
    public MarketDataEvidence EvidenceFor(TimeFrame timeFrame) =>
        EvidenceByTimeFrame.TryGetValue(timeFrame, out var evidence) ? evidence : DefaultEvidence;
}

public sealed record OptimizationRiskParameters(
    decimal RiskPerTradePercent,
    decimal DailyLossLimitPercent,
    int MaxTotalPositions,
    int MaxPositionsPerSector);

/// <summary>
/// 동기 API와 백그라운드 작업이 공유하는 최적화 후보 실행 포트입니다.
/// 호출자는 탐색과 저장을 조정하고, 구현은 전략 변형과 준비 데이터 시뮬레이션을 소유합니다.
/// </summary>
public interface IOptimizationCandidateEvaluator
{
    Task<List<OptimizeResultItem>> EvaluateBatchAsync(
        OptimizationEvaluationContext context,
        IReadOnlyList<OptimizeParamSnapshot> combinations,
        DateTime from,
        DateTime to,
        string failureMessage,
        CancellationToken ct);

    Task<BacktestResult?> RunAsync(
        OptimizationEvaluationContext context,
        OptimizeParamSnapshot combination,
        DateTime from,
        DateTime to,
        string failureMessage,
        CancellationToken ct);
}
