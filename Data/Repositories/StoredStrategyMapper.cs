using StockTrader.Application.Strategies;
using StockTrader.Models;

namespace StockTrader.Data.Repositories;

/// <summary>EF strategy rows are translated only inside the persistence adapter.</summary>
internal static class StoredStrategyMapper
{
    public static StoredStrategy ToStoredStrategy(this CustomPatternDefinition value) => new(
        value.Id,
        new StrategyDocument
        {
            StoredStrategyId = value.Id > 0 ? value.Id : null,
            DocumentVersion = value.DocumentVersion,
            Name = value.Name,
            Description = value.Description,
            EntryRulesJson = value.EntryRulesJson,
            EntryLogic = value.EntryLogic,
            RequireBullRegime = value.RequireBullRegime,
            AtrStopMultiplier = value.AtrStopMultiplier,
            AtrTargetMultiplier = value.AtrTargetMultiplier,
            MaxHoldingBars = value.MaxHoldingBars,
            TrailingAtr = value.TrailingAtr,
            PartialProfitR = value.PartialProfitR,
            UseWeightTiers = value.UseWeightTiers,
            WeightTiersJson = value.WeightTiersJson,
            DefaultAllocationPercent = value.DefaultAllocationPercent,
            ExitRulesJson = value.ExitRulesJson,
            ExitRulesLogic = value.ExitRulesLogic,
            ExitGroupsJson = value.ExitGroupsJson,
            ExitGroupsLogic = value.ExitGroupsLogic,
            ScalingRulesJson = value.ScalingRulesJson,
            TimeFilterJson = value.TimeFilterJson,
            CircuitBreakerJson = value.CircuitBreakerJson,
            ReentryJson = value.ReentryJson,
            PortfolioRulesJson = value.PortfolioRulesJson,
            EntryGroupsJson = value.EntryGroupsJson,
            EntryGroupsLogic = value.EntryGroupsLogic,
            DynamicExitJson = value.DynamicExitJson,
            EntryMode = value.EntryMode,
            TimeFrame = value.TimeFrame,
            SizingMode = value.SizingMode,
            IsActive = value.IsActive,
            EnableLiveTrading = value.EnableLiveTrading,
        },
        value.CreatedAt,
        value.UpdatedAt);

    public static CustomPatternDefinition ToEntity(this StoredStrategy value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var document = value.Document;
        return new CustomPatternDefinition
        {
            Id = value.Id,
            DocumentVersion = document.DocumentVersion,
            Name = document.Name,
            Description = document.Description,
            EntryRulesJson = document.EntryRulesJson,
            EntryLogic = document.EntryLogic,
            RequireBullRegime = document.RequireBullRegime,
            AtrStopMultiplier = document.AtrStopMultiplier,
            AtrTargetMultiplier = document.AtrTargetMultiplier,
            MaxHoldingBars = document.MaxHoldingBars,
            TrailingAtr = document.TrailingAtr,
            PartialProfitR = document.PartialProfitR,
            UseWeightTiers = document.UseWeightTiers,
            WeightTiersJson = document.WeightTiersJson,
            DefaultAllocationPercent = document.DefaultAllocationPercent,
            ExitRulesJson = document.ExitRulesJson,
            ExitRulesLogic = document.ExitRulesLogic,
            ExitGroupsJson = document.ExitGroupsJson,
            ExitGroupsLogic = document.ExitGroupsLogic,
            ScalingRulesJson = document.ScalingRulesJson,
            TimeFilterJson = document.TimeFilterJson,
            CircuitBreakerJson = document.CircuitBreakerJson,
            ReentryJson = document.ReentryJson,
            PortfolioRulesJson = document.PortfolioRulesJson,
            EntryGroupsJson = document.EntryGroupsJson,
            EntryGroupsLogic = document.EntryGroupsLogic,
            DynamicExitJson = document.DynamicExitJson,
            EntryMode = document.EntryMode,
            TimeFrame = document.TimeFrame,
            SizingMode = document.SizingMode,
            IsActive = document.IsActive,
            EnableLiveTrading = document.EnableLiveTrading,
            CreatedAt = value.CreatedAt,
            UpdatedAt = value.UpdatedAt,
        };
    }
}
