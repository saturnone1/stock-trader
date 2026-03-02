using StockTrader.Models.Enums;

namespace StockTrader.Models;

/// <summary>
/// 패턴 검증 상태
/// </summary>
public enum PatternStatus
{
    /// <summary>검증 완료 (백테스트 최적화됨)</summary>
    Verified,
    /// <summary>미튜닝 (데이터 부족 등으로 파라미터 미최적화)</summary>
    Untuned,
    /// <summary>성능 불량 (승률/수익률 기준 미달)</summary>
    Poor
}

/// <summary>
/// 패턴 타입별 메타데이터 (한국어 이름, 트레이딩 스타일, 설명 등)
/// </summary>
public record PatternMetadata(
    PatternType PatternType,
    string KoreanName,
    string EnglishName,
    TradingStyle Style,
    string StyleLabel,
    string HoldingPeriod,
    string Description,
    string Indicators,
    string ColorHex,
    PatternStatus Status = PatternStatus.Verified,
    string? StatusNote = null
);

public static class PatternMetadataMap
{
    private static readonly Dictionary<PatternType, PatternMetadata> _map = new()
    {
        [PatternType.GapUpPullback] = new(
            PatternType.GapUpPullback,
            "갭업 풀백", "Gap-Up Pullback",
            TradingStyle.DayTrading, "단타",
            "1~3일",
            "전일 종가 대비 2%+ 갭업 후 되돌림 매수. 거래량 50만주 이상 필터.",
            "Gap%, Volume, Candle",
            "#FF9800", // Orange
            PatternStatus.Untuned, "분봉 데이터 부족으로 미튜닝"
        ),
        [PatternType.Breakout] = new(
            PatternType.Breakout,
            "브레이크아웃", "Breakout",
            TradingStyle.Swing, "스윙",
            "3~10일",
            "52주 최고가를 거래량 1.5배+ 동반 돌파 시 매수. ATR 기반 손절/목표.",
            "52주 고가, Volume, ATR",
            "#4CAF50" // Green
        ),
        [PatternType.VwapReversion] = new(
            PatternType.VwapReversion,
            "VWAP 회귀", "VWAP Reversion",
            TradingStyle.DayTrading, "단타",
            "수시간~1일",
            "가격이 VWAP 대비 2%+ 하방 이탈 후 반등 시 매수. 목표가는 VWAP 회귀.",
            "VWAP, Candle",
            "#FF9800",
            PatternStatus.Untuned, "분봉 데이터 부족으로 미튜닝"
        ),
        [PatternType.RsiMeanReversion] = new(
            PatternType.RsiMeanReversion,
            "RSI 평균회귀", "RSI Mean Reversion",
            TradingStyle.Swing, "스윙",
            "2~5일",
            "RSI 30 이하 과매도 + 거래량 1.2배 이상 증가 시 매수. 과매도 반등 역추세.",
            "RSI(14), ATR, Volume",
            "#4CAF50"
        ),
        [PatternType.TrendPullback] = new(
            PatternType.TrendPullback,
            "추세 풀백", "Trend Pullback",
            TradingStyle.Swing, "스윙",
            "3~7일",
            "SMA(20) 우상향 중 가격이 SMA 아래로 0~2% 되돌림 시 매수. 눌림목 매수.",
            "SMA(20), ATR",
            "#4CAF50"
        ),
        [PatternType.OpeningRangeBreakout] = new(
            PatternType.OpeningRangeBreakout,
            "ORB (시초가 돌파)", "Opening Range Breakout",
            TradingStyle.Scalping, "스캘핑",
            "수시간",
            "장 시작 15분 가격 범위 설정 후 상단 돌파 시 매수. 인트라데이 전략.",
            "시초가 Range(15분)",
            "#F44336" // Red
        ),
        [PatternType.VolumeSpikeContinuation] = new(
            PatternType.VolumeSpikeContinuation,
            "거래량 스파이크", "Volume Spike Continuation",
            TradingStyle.DayTrading, "단타",
            "1~3일",
            "거래량 20일 평균 2배+ 급증 + 3봉 연속 양봉 시 매수. 매수세 지속 추종.",
            "Volume(20MA), ATR, Candle",
            "#FF9800",
            PatternStatus.Untuned, "분봉 데이터 부족으로 미튜닝"
        ),
        [PatternType.EarningsDrift] = new(
            PatternType.EarningsDrift,
            "실적 드리프트", "Post-Earnings Drift",
            TradingStyle.Swing, "스윙",
            "5일+",
            "실적 서프라이즈 5%+ 시 해당 방향 5일간 가격 드리프트 추종. (미구현)",
            "Earnings Surprise",
            "#4CAF50"
        ),
        [PatternType.IndexRegimeFilter] = new(
            PatternType.IndexRegimeFilter,
            "지수 레짐 필터", "Index Regime Filter",
            TradingStyle.Filter, "필터",
            "-",
            "SPY가 200일 이평선 위일 때만 매매 허용하는 전역 필터. 독립 시그널 없음.",
            "SPY, SMA(200)",
            "#9E9E9E" // Gray
        ),
        [PatternType.VolatilityExpansion] = new(
            PatternType.VolatilityExpansion,
            "변동성 확대", "Volatility Expansion",
            TradingStyle.Swing, "스윙",
            "1~5일",
            "종가가 볼린저밴드 상단 돌파 시 매수. 손절은 SMA(20), 목표는 돌파 거리만큼.",
            "Bollinger(20, 2σ), ATR",
            "#4CAF50"
        ),
        [PatternType.MomentumReversal] = new(
            PatternType.MomentumReversal,
            "모멘텀 반전", "Momentum Reversal",
            TradingStyle.Swing, "스윙",
            "3~10일",
            "이중 모드: EMA 크로스오버+MACD 모멘텀 / RSI 과매도+MACD 반전 리버설.",
            "EMA(12/26), MACD, RSI(14)",
            "#4CAF50"
        ),
        [PatternType.MultiTimeframeTrend] = new(
            PatternType.MultiTimeframeTrend,
            "다중시간 추세", "Multi-Timeframe Trend",
            TradingStyle.Position, "포지션",
            "5~15일",
            "장기 SMA(50) 상승추세 + 단기 SMA(20) 근접 눌림목 + 양봉 반등 시 매수.",
            "SMA(50), SMA(20), ATR",
            "#2196F3", // Blue
            PatternStatus.Poor, "승률 18~20% — 사용 비권장"
        ),
        [PatternType.MeanReversionChannel] = new(
            PatternType.MeanReversionChannel,
            "평균회귀 채널", "Mean Reversion Channel",
            TradingStyle.Swing, "스윙",
            "2~7일",
            "켈트너 채널 하단 이탈 + RSI 35 이하 시 매수. 목표가는 채널 중간선 회귀.",
            "Keltner(EMA20, ATR10), RSI(14)",
            "#4CAF50"
        ),
        [PatternType.Rsi2Bollinger] = new(
            PatternType.Rsi2Bollinger,
            "RSI(2) 볼린저", "RSI(2) + Bollinger Band",
            TradingStyle.Swing, "스윙",
            "2~5일",
            "RSI(2) 극단 과매도 + 볼린저밴드 하단 이탈 + 200SMA 상승추세. 평균회귀 매수.",
            "RSI(2), BB(20,2σ), SMA(200), ATR",
            "#E91E63" // Pink
        ),
        [PatternType.VolatilityBreakout] = new(
            PatternType.VolatilityBreakout,
            "변동성 돌파", "Volatility Breakout",
            TradingStyle.DayTrading, "단타",
            "1~2일",
            "래리 윌리엄스 전략. 시가 + 전일레인지×K 돌파 시 매수. 거래량 1.2배+ 확인.",
            "PrevRange, K=0.6, Volume, ATR",
            "#FF5722", // Deep Orange
            PatternStatus.Poor, "승률 34~45% — 사용 비권장"
        ),
        [PatternType.Tqqq200Sma] = new(
            PatternType.Tqqq200Sma,
            "TQQQ 200일선", "TQQQ 200-SMA Rotation",
            TradingStyle.Position, "포지션",
            "수주~수개월",
            "TQQQ SMA(200) 크로스오버 추세추종. 돌파 진입(이틀 확인) + 밴드 눌림목 매수. " +
            "EMA(50) 골든크로스 필터로 휩쏘 감소. ATR 적응형 손절 + 트레일링 스탑.",
            "SMA(200), EMA(50), ATR, Volume",
            "#00BCD4" // Cyan
        ),
    };

    public static PatternMetadata Get(PatternType type)
        => _map.TryGetValue(type, out var meta) ? meta : new(
            type, type.ToString(), type.ToString(),
            TradingStyle.Swing, "스윙", "-", "", "", "#9E9E9E");

    public static IReadOnlyDictionary<PatternType, PatternMetadata> All => _map;

    public static string GetKoreanName(PatternType type) => Get(type).KoreanName;
    public static string GetStyleLabel(PatternType type) => Get(type).StyleLabel;
    public static string GetColorHex(PatternType type) => Get(type).ColorHex;
    public static string GetDescription(PatternType type) => Get(type).Description;

    public static MudBlazor.Color GetMudColor(TradingStyle style) => style switch
    {
        TradingStyle.Scalping => MudBlazor.Color.Error,
        TradingStyle.DayTrading => MudBlazor.Color.Warning,
        TradingStyle.Swing => MudBlazor.Color.Success,
        TradingStyle.Position => MudBlazor.Color.Info,
        TradingStyle.Filter => MudBlazor.Color.Default,
        _ => MudBlazor.Color.Default
    };

    public static MudBlazor.Color GetMudColor(PatternType type)
        => GetMudColor(Get(type).Style);

    public static PatternStatus GetStatus(PatternType type) => Get(type).Status;
    public static string? GetStatusNote(PatternType type) => Get(type).StatusNote;

    public static string GetStatusLabel(PatternStatus status) => status switch
    {
        PatternStatus.Untuned => "미튜닝",
        PatternStatus.Poor => "성능불량",
        _ => ""
    };

    public static MudBlazor.Color GetStatusColor(PatternStatus status) => status switch
    {
        PatternStatus.Untuned => MudBlazor.Color.Warning,
        PatternStatus.Poor => MudBlazor.Color.Error,
        _ => MudBlazor.Color.Default
    };

    public static string GetStatusIcon(PatternStatus status) => status switch
    {
        PatternStatus.Untuned => MudBlazor.Icons.Material.Filled.Science,
        PatternStatus.Poor => MudBlazor.Icons.Material.Filled.Warning,
        _ => ""
    };
}
