using StockTrader.Domain.Strategies;

namespace StockTrader.Application.Strategies;

/// <summary>
/// 저장 방식과 무관하게 미리보기·백테스트·최적화·실시간 실행이 공유하는 전략 문서다.
/// DB 전용 비교 키와 감사 시각은 포함하지 않으며, 저장 전략을 가리킬 때만 선택적 ID를 사용한다.
/// </summary>
public sealed class StrategyDocument
{
    public int? StoredStrategyId { get; set; }
    public int DocumentVersion { get; set; } = StrategyDocumentVersions.Current;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string EntryRulesJson { get; set; } = StrategyDocumentDefaults.EmptyListJson;
    public string EntryLogic { get; set; } = StrategyDocumentDefaults.AndLogic;
    public bool RequireBullRegime { get; set; }
    public decimal AtrStopMultiplier { get; set; } = StrategyDocumentDefaults.AtrStopMultiplier;
    public decimal AtrTargetMultiplier { get; set; } = StrategyDocumentDefaults.AtrTargetMultiplier;
    public int MaxHoldingBars { get; set; } = StrategyDocumentDefaults.MaxHoldingBars;
    public decimal TrailingAtr { get; set; }
    public decimal PartialProfitR { get; set; }
    public bool UseWeightTiers { get; set; }
    public string WeightTiersJson { get; set; } = StrategyDocumentDefaults.EmptyListJson;
    public decimal DefaultAllocationPercent { get; set; } = StrategyDocumentDefaults.DefaultAllocationPercent;
    public string ExitRulesJson { get; set; } = StrategyDocumentDefaults.EmptyListJson;
    public string ExitRulesLogic { get; set; } = StrategyDocumentDefaults.OrLogic;
    public string ExitGroupsJson { get; set; } = StrategyDocumentDefaults.EmptyListJson;
    public string ExitGroupsLogic { get; set; } = StrategyDocumentDefaults.OrLogic;
    public string ScalingRulesJson { get; set; } = StrategyDocumentDefaults.EmptyListJson;
    public string TimeFilterJson { get; set; } = StrategyDocumentDefaults.EmptyObjectJson;
    public string CircuitBreakerJson { get; set; } = StrategyDocumentDefaults.EmptyObjectJson;
    public string ReentryJson { get; set; } = StrategyDocumentDefaults.EmptyObjectJson;
    public string PortfolioRulesJson { get; set; } = StrategyDocumentDefaults.EmptyObjectJson;
    public string EntryGroupsJson { get; set; } = StrategyDocumentDefaults.EmptyListJson;
    public string EntryGroupsLogic { get; set; } = StrategyDocumentDefaults.AndLogic;
    public string DynamicExitJson { get; set; } = StrategyDocumentDefaults.EmptyObjectJson;
    public string EntryMode { get; set; } = StrategyCatalog.CurrentCloseEntryMode;
    public TimeFrame TimeFrame { get; set; } = TimeFrame.Daily;
    public string SizingMode { get; set; } = StrategyCatalog.FixedRiskSizingMode;
    public bool IsActive { get; set; } = StrategyDocumentDefaults.IsActive;
    public bool EnableLiveTrading { get; set; }

    /// <summary>JSON 설정은 불변 문자열이므로 최적화·편집 경계에서 안전한 얕은 복사입니다.</summary>
    public StrategyDocument Copy() => (StrategyDocument)MemberwiseClone();
}
