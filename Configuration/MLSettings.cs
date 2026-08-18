namespace StockTrader.Configuration;

public class MLSettings
{
    /// <summary>ML 모델 저장 폴더 (실행 파일 기준 상대 경로)</summary>
    public string ModelDirectory { get; set; } = string.Empty;

    /// <summary>시장 레짐 분류기 모델 파일명</summary>
    public string RegimeModelFileName { get; set; } = string.Empty;

    /// <summary>시그널 스코어링 모델 파일명</summary>
    public string SignalScorerModelFileName { get; set; } = string.Empty;

    /// <summary>모델 학습을 위한 최소 샘플 수</summary>
    public int MinTrainingSamples { get; set; }

    /// <summary>K-Means 클러스터 수 (시장 레짐 수)</summary>
    public int RegimeClusterCount { get; set; }

    /// <summary>레짐 분류기 학습용 공급자 기준 종목 히스토리 일수</summary>
    public int RegimeTrainingDays { get; set; }

    /// <summary>ML 스코어를 기존 Confidence와 혼합할 가중치 (0=기존만, 1=ML만)</summary>
    public double MlScoreBlendWeight { get; set; }

    /// <summary>ML 기능 활성화 여부</summary>
    public bool EnableMlScoring { get; set; }

    /// <summary>모델 자동 재학습 주기 (시간)</summary>
    public int AutoRetrainIntervalHours { get; set; }

    /// <summary>미국 동부시간 기준 자동 재학습 허용 시작 시각 (HH:mm)</summary>
    public string AutoRetrainAfterEt { get; set; } = string.Empty;

    /// <summary>냉각에 들어가기 전 연속 실패 횟수</summary>
    public int AutoRetrainMaxConsecutiveFailures { get; set; }

    /// <summary>연속 실패 후 냉각 시간(분)</summary>
    public int AutoRetrainCooldownMinutes { get; set; }

    /// <summary>한 재학습 실행의 최대 재시도 횟수</summary>
    public int AutoRetrainMaxRetries { get; set; }
}
