using StockTrader.Models;

namespace StockTrader.Services.ML;

public interface ISignalScorer
{
    /// <summary>
    /// ML 모델을 사용해 시그널의 신뢰도 점수를 보정합니다.
    /// 모델이 없으면 원래 signal.Confidence를 그대로 반환합니다 (graceful degradation).
    /// </summary>
    /// <param name="signal">평가할 패턴 시그널</param>
    /// <param name="bars">최근 OHLCV 데이터</param>
    /// <param name="regime">현재 시장 레짐</param>
    /// <param name="historicalWinRate">해당 패턴의 역사적 승률 (없으면 0.5)</param>
    /// <returns>보정된 신뢰도 점수 (0~1)</returns>
    Task<decimal> ScoreAsync(
        PatternSignal signal,
        OhlcvBar[] bars,
        MarketRegime regime,
        decimal historicalWinRate = 0.5m,
        CancellationToken ct = default);

    /// <summary>
    /// 백테스트 거래 내역으로 모델을 학습합니다.
    /// </summary>
    Task<bool> TrainAsync(IReadOnlyList<TradeRecord> trades, CancellationToken ct = default);

    bool IsModelLoaded { get; }
    DateTime? TrainedAt { get; }
    int TrainingSamples { get; }
    double LastAccuracy { get; }
    double LastAuc { get; }
    List<FeatureImportance> FeatureImportances { get; }
}
