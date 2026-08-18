namespace StockTrader.Configuration;

public class MLSettings
{
    /// <summary>ML 모델 저장 폴더 (실행 파일 기준 상대 경로)</summary>
    public string ModelDirectory { get; set; } = "ml_models";

    /// <summary>시장 레짐 분류기 모델 파일명</summary>
    public string RegimeModelFileName { get; set; } = "regime_classifier.zip";

    /// <summary>시그널 스코어링 모델 파일명</summary>
    public string SignalScorerModelFileName { get; set; } = "signal_scorer.zip";

    /// <summary>모델 학습을 위한 최소 샘플 수</summary>
    public int MinTrainingSamples { get; set; } = 50;

    /// <summary>K-Means 클러스터 수 (시장 레짐 수)</summary>
    public int RegimeClusterCount { get; set; } = 4;

    /// <summary>레짐 분류기 학습용 공급자 기준 종목 히스토리 일수</summary>
    public int RegimeTrainingDays { get; set; } = 365;

    /// <summary>ML 스코어를 기존 Confidence와 혼합할 가중치 (0=기존만, 1=ML만)</summary>
    public double MlScoreBlendWeight { get; set; } = 0.5;

    /// <summary>ML 기능 활성화 여부</summary>
    public bool EnableMlScoring { get; set; } = true;

    /// <summary>모델 자동 재학습 주기 (시간)</summary>
    public int AutoRetrainIntervalHours { get; set; } = 24;
}
