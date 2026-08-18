using StockTrader.Application.MachineLearning;
using StockTrader.Models;

namespace StockTrader.Services.ML;

public interface ISignalScorer
{
    /// <summary>
    /// 진입 시점 피처를 만들고, 검증된 ML 모델이 있으면 신뢰도를 보정합니다.
    /// 모델이 없어도 향후 인과적 학습을 위한 피처를 반환하며 신뢰도는 그대로 유지합니다.
    /// </summary>
    /// <param name="signal">평가할 패턴 시그널</param>
    /// <param name="bars">최근 OHLCV 데이터</param>
    /// <param name="regime">현재 시장 레짐</param>
    /// <param name="historicalWinRate">해당 패턴의 역사적 승률 (없으면 0.5)</param>
    /// <returns>보정된 신뢰도와 영속화할 진입 시점 피처</returns>
    Task<SignalScoringResult> EvaluateAsync(
        PatternSignal signal,
        OhlcvBar[] bars,
        MarketRegime regime,
        decimal historicalWinRate = 0.5m,
        CancellationToken ct = default);

    /// <summary>
    /// 진입 시점에 영속화한 피처와 이후 확정된 포지션 손익으로 모델을 학습합니다.
    /// </summary>
    Task<bool> TrainAsync(
        IReadOnlyList<SignalScoringTrainingSample> samples,
        CancellationToken ct = default);

    bool IsModelLoaded { get; }
    DateTime? TrainedAt { get; }
    int TrainingSamples { get; }
    double LastAccuracy { get; }
    double LastAuc { get; }
    List<FeatureImportance> FeatureImportances { get; }
}
