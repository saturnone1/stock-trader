using Microsoft.ML.Data;

namespace StockTrader.Services.ML;

// ──────────────────────────────────────────────────────────────────────────────
// 시장 레짐 분류기 데이터 모델
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// K-Means 클러스터링 입력 피처.
/// ML.NET은 float[] 컬럼으로 피처 벡터를 받습니다.
/// </summary>
public class RegimeFeatureInput
{
    /// <summary>SPY 5일 수익률</summary>
    [LoadColumn(0)]
    public float Return5Day { get; set; }

    /// <summary>SPY 10일 수익률</summary>
    [LoadColumn(1)]
    public float Return10Day { get; set; }

    /// <summary>SPY 20일 수익률</summary>
    [LoadColumn(2)]
    public float Return20Day { get; set; }

    /// <summary>VIX 수준 (proxy: SPY 5일 변동성)</summary>
    [LoadColumn(3)]
    public float VolatilityLevel { get; set; }

    /// <summary>거래량 변화율 (5일 평균 대비 현재)</summary>
    [LoadColumn(4)]
    public float VolumeChangeRate { get; set; }

    /// <summary>20일 이동평균 기울기 (%)</summary>
    [LoadColumn(5)]
    public float MaSlopePercent { get; set; }

    /// <summary>RSI (14일)</summary>
    [LoadColumn(6)]
    public float Rsi { get; set; }
}

/// <summary>K-Means 클러스터링 출력 (클러스터 ID)</summary>
public class RegimeClusterOutput
{
    /// <summary>클러스터 번호 (0-based)</summary>
    [ColumnName("PredictedLabel")]
    public uint ClusterId { get; set; }

    /// <summary>각 클러스터까지의 거리</summary>
    [ColumnName("Score")]
    public float[]? Distances { get; set; }
}

// ──────────────────────────────────────────────────────────────────────────────
// 시그널 스코어링 데이터 모델
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Binary Classification 입력 피처.
/// 학습 시에는 Label이 필요하고, 예측 시에는 무시됩니다.
/// </summary>
public class SignalScorerInput
{
    /// <summary>성공 여부 레이블 (1=승리, 0=패배)</summary>
    [LoadColumn(0), ColumnName("Label")]
    public bool Label { get; set; }

    /// <summary>패턴 타입 (숫자로 인코딩)</summary>
    [LoadColumn(1)]
    public float PatternTypeCode { get; set; }

    /// <summary>RSI 값</summary>
    [LoadColumn(2)]
    public float Rsi { get; set; }

    /// <summary>볼린저밴드 내 위치 (0=하단, 1=상단)</summary>
    [LoadColumn(3)]
    public float BollingerPosition { get; set; }

    /// <summary>거래량 비율 (현재 / 20일 평균)</summary>
    [LoadColumn(4)]
    public float VolumeRatio { get; set; }

    /// <summary>시장 레짐 코드 (0=강세, 1=약세, 2=횡보, 3=고변동)</summary>
    [LoadColumn(5)]
    public float MarketRegimeCode { get; set; }

    /// <summary>ATR 기반 변동성 (%)</summary>
    [LoadColumn(6)]
    public float AtrPercent { get; set; }

    /// <summary>해당 패턴의 역사적 승률 (0~1)</summary>
    [LoadColumn(7)]
    public float HistoricalWinRate { get; set; }

    /// <summary>스톱-목표 비율 (R:R)</summary>
    [LoadColumn(8)]
    public float RiskRewardRatio { get; set; }

    /// <summary>가격이 200MA 대비 위치 (%)</summary>
    [LoadColumn(9)]
    public float PriceVs200Ma { get; set; }
}

/// <summary>Binary Classification 예측 출력</summary>
public class SignalScorerOutput
{
    /// <summary>예측 레이블 (true=성공 예측)</summary>
    [ColumnName("PredictedLabel")]
    public bool PredictedLabel { get; set; }

    /// <summary>성공 확률 (0~1) — ML 신뢰도 점수로 사용</summary>
    [ColumnName("Probability")]
    public float Probability { get; set; }

    /// <summary>원시 스코어</summary>
    [ColumnName("Score")]
    public float Score { get; set; }
}

// ──────────────────────────────────────────────────────────────────────────────
// ML 모델 상태 / 결과 공유 모델
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>ML 모델 현재 상태 (UI 표시용)</summary>
public class MlModelStatus
{
    public bool IsRegimeModelLoaded { get; set; }
    public bool IsSignalScorerLoaded { get; set; }

    public DateTime? RegimeModelTrainedAt { get; set; }
    public DateTime? SignalScorerTrainedAt { get; set; }

    public double RegimeModelAccuracy { get; set; }
    public double SignalScorerAccuracy { get; set; }

    public int RegimeTrainingSamples { get; set; }
    public int SignalScorerTrainingSamples { get; set; }

    public string CurrentRegimeLabel { get; set; } = "알 수 없음";
    public int CurrentRegimeClusterId { get; set; } = -1;

    public List<FeatureImportance> SignalScorerFeatureImportances { get; set; } = new();

    public bool IsTraining { get; set; }
    public string TrainingStatus { get; set; } = string.Empty;
}

/// <summary>피처 중요도 항목 (UI 표시용)</summary>
public class FeatureImportance
{
    public string FeatureName { get; set; } = string.Empty;
    public double Importance { get; set; }
}

/// <summary>ML 학습 결과 요약</summary>
public class MlTrainingResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;

    public int RegimeSamples { get; set; }
    public int SignalSamples { get; set; }

    public double SignalScorerAccuracy { get; set; }
    public double SignalScorerAuc { get; set; }
    public double SignalScorerF1Score { get; set; }

    public TimeSpan TrainingDuration { get; set; }
}
