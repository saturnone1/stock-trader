using StockTrader.Models;

namespace StockTrader.Services.LiveParameter;

public interface ILiveParameterService
{
    /// <summary>
    /// 백테스트에서 최적화된 파라미터를 실거래에 적용합니다.
    /// 1) PatternParameterOverrides → DB(UserSettings.LiveParameterOverridesJson)에 저장
    /// 2) 패턴 감지 파라미터 → appsettings.json Patterns 섹션에 반영 (IOptionsSnapshot 핫리로드)
    /// 3) 리스크 파라미터 → DB(UserSettings) + appsettings.json Trading 섹션에 반영
    /// </summary>
    Task ApplyToLiveAsync(
        PatternParameterOverrides overrides,
        List<PatternType> enabledPatterns,
        decimal riskPerTradePercent,
        decimal dailyLossLimitPercent,
        int maxTotalPositions,
        int maxPositionsPerSector,
        CancellationToken ct = default);

    /// <summary>
    /// DB에 저장된 실거래 파라미터 오버라이드를 로드합니다.
    /// 저장된 값이 없으면 null 반환.
    /// </summary>
    Task<PatternParameterOverrides?> GetLiveOverridesAsync(CancellationToken ct = default);
}
