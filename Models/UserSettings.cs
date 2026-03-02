using StockTrader.Models.Enums;

namespace StockTrader.Models;

public class UserSettings
{
    public long Id { get; set; }
    public OrderMode OrderMode { get; set; } = OrderMode.AlertOnly;
    public DataSource PreferredDataSource { get; set; } = DataSource.Alpaca;
    public List<PatternType> EnabledPatterns { get; set; } = new();
    public List<string> WatchlistSymbols { get; set; } = new();
    public bool SoundAlerts { get; set; } = true;
    public DateTime LastModified { get; set; }

    // ── 리스크 관리 설정 ──────────────────────────────────────────────
    /// <summary>기본 계좌 규모 (포지션 크기 계산 기준)</summary>
    public decimal AccountSize { get; set; } = 100_000m;

    /// <summary>1회 매매당 리스크 비율 (0.01 = 1%)</summary>
    public decimal RiskPerTradePercent { get; set; } = 0.01m;

    /// <summary>일일 최대 손실 한도 비율 (0.03 = 3%)</summary>
    public decimal DailyLossLimitPercent { get; set; } = 0.03m;

    /// <summary>전체 최대 동시 포지션 수</summary>
    public int MaxTotalPositions { get; set; } = 7;

    /// <summary>섹터당 최대 포지션 수</summary>
    public int MaxPositionsPerSector { get; set; } = 2;

    /// <summary>최소 기대 수익 (0 = 제한 없음)</summary>
    public decimal MinExpectancy { get; set; } = 0m;

    // ── 실거래 파라미터 오버라이드 ─────────────────────────────────────
    /// <summary>
    /// 백테스트에서 최적화된 파라미터를 실거래에 적용할 때 저장하는 JSON.
    /// PatternParameterOverrides를 직렬화한 값으로, 패턴 감지 + 청산 전략 파라미터 포함.
    /// null이면 appsettings.json 기본값 사용.
    /// </summary>
    public string? LiveParameterOverridesJson { get; set; }
}
