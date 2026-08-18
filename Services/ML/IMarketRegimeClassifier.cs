using StockTrader.Models;

namespace StockTrader.Services.ML;

public interface IMarketRegimeClassifier
{
    /// <summary>
    /// 주어진 공급자 기준 종목 바 데이터로 현재 시장 레짐을 분류합니다.
    /// 모델·manifest가 없거나 데이터가 부족하면 공통 장기추세 레짐을 반환합니다.
    /// </summary>
    Task<MarketRegime> ClassifyAsync(OhlcvBar[] benchmarkBars, CancellationToken ct = default);

    /// <summary>
    /// 공급자 기준 종목의 완료된 과거 일봉으로 K-Means 모델을 학습합니다.
    /// </summary>
    Task<bool> TrainAsync(OhlcvBar[] benchmarkBars, CancellationToken ct = default);

    /// <summary>현재 모델이 로드되어 있는지 여부</summary>
    bool IsModelLoaded { get; }

    /// <summary>모델이 학습된 시각 (UTC)</summary>
    DateTime? TrainedAt { get; }

    /// <summary>학습에 사용된 샘플 수</summary>
    int TrainingSamples { get; }

    /// <summary>클러스터별 레짐 레이블 맵</summary>
    IReadOnlyDictionary<uint, string> ClusterLabels { get; }
}
