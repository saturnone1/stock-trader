using StockTrader.Models;

namespace StockTrader.Services.ML;

public interface IMarketRegimeClassifier
{
    /// <summary>
    /// 주어진 SPY 바 데이터로 현재 시장 레짐을 분류합니다.
    /// 모델이 없거나 데이터 부족 시 기존 MarketRegime을 그대로 반환합니다 (graceful degradation).
    /// </summary>
    Task<MarketRegime> ClassifyAsync(OhlcvBar[] spyBars, CancellationToken ct = default);

    /// <summary>
    /// SPY 히스토리 데이터로 K-Means 클러스터링 모델을 학습합니다.
    /// </summary>
    Task<bool> TrainAsync(OhlcvBar[] spyBars, CancellationToken ct = default);

    /// <summary>현재 모델이 로드되어 있는지 여부</summary>
    bool IsModelLoaded { get; }

    /// <summary>모델이 학습된 시각 (UTC)</summary>
    DateTime? TrainedAt { get; }

    /// <summary>학습에 사용된 샘플 수</summary>
    int TrainingSamples { get; }

    /// <summary>클러스터별 레짐 레이블 맵</summary>
    IReadOnlyDictionary<uint, string> ClusterLabels { get; }
}
