using StockTrader.Application.StrategyPreview;
using StockTrader.Domain.MarketData;
using StockTrader.Domain.Strategies;
using StockTrader.Models.Enums;

namespace StockTrader.Api.Contracts;

public sealed record IndicatorParameterMetadataResponse(
    string Key,
    string DisplayName,
    decimal DefaultValue,
    decimal Step,
    bool MustBePositive);

public sealed record IndicatorMetadataResponse(
    string Code,
    string DisplayName,
    string Category,
    string DefaultOperator,
    decimal DefaultThreshold,
    string? ValueGuide,
    IReadOnlyList<IndicatorParameterMetadataResponse> Parameters);

public sealed record PreviewTimeFrameMetadataResponse(
    int DefaultLookbackDays,
    int MaximumRangeDays,
    IReadOnlyList<int> SuggestedRangeDays);

public sealed record TimeFrameMetadataResponse(
    TimeFrame Value,
    string DisplayName,
    bool IsIntraday,
    decimal AnnualizationPeriods,
    PreviewTimeFrameMetadataResponse Preview);

public sealed record DataProviderMetadataResponse(
    DataSource Value,
    string DisplayName,
    string Market,
    IReadOnlyList<TimeFrame> SupportedTimeFrames,
    IReadOnlyDictionary<TimeFrame, int> MaximumLookbackDays);

public sealed record StrategyBuilderMetadataResponse(
    int SchemaVersion,
    IReadOnlyList<IndicatorMetadataResponse> Indicators,
    IReadOnlyList<TimeFrameMetadataResponse> TimeFrames,
    IReadOnlyList<DataProviderMetadataResponse> DataProviders,
    IReadOnlyList<string> RuleOperators)
{
    public static StrategyBuilderMetadataResponse Create() => new(
        SchemaVersion: 1,
        Indicators: IndicatorCatalog.All.Select(item => new IndicatorMetadataResponse(
            item.Code,
            item.DisplayName,
            item.Category,
            item.DefaultOperator,
            item.DefaultThreshold,
            item.ValueGuide,
            item.Parameters.Select(parameter => new IndicatorParameterMetadataResponse(
                parameter.Key,
                parameter.DisplayName,
                parameter.DefaultValue,
                parameter.Step,
                parameter.MustBePositive)).ToArray())).ToArray(),
        TimeFrames: TimeFrameCatalog.All.Select(item =>
        {
            var preview = PreviewTimeFramePolicy.Get(item.Value);
            return new TimeFrameMetadataResponse(
                item.Value,
                item.DisplayName,
                item.IsIntraday,
                item.AnnualizationPeriods,
                new PreviewTimeFrameMetadataResponse(
                    preview.DefaultLookbackDays,
                    (int)preview.MaximumRange.TotalDays,
                    preview.SuggestedRangeDays));
        }).ToArray(),
        DataProviders: DataProviderCatalog.Implemented.Select(item => new DataProviderMetadataResponse(
            item.Value,
            item.DisplayName,
            item.Market,
            item.SupportedTimeFrames,
            item.MaximumLookbackDays)).ToArray(),
        RuleOperators: RuleOperatorCatalog.All);
}
