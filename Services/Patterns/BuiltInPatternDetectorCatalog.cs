using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Models;

namespace StockTrader.Services.Patterns;

public sealed record BuiltInPatternDetectorDescriptor(
    PatternType PatternType,
    Type ImplementationType);

/// <summary>내장 전략 코드와 감지기 구현의 단일 등록 원천입니다.</summary>
public static class BuiltInPatternDetectorCatalog
{
    public static IReadOnlyList<BuiltInPatternDetectorDescriptor> All { get; } =
    [
        D<GapUpPullbackDetector>(PatternType.GapUpPullback),
        D<BreakoutDetector>(PatternType.Breakout),
        D<VwapReversionDetector>(PatternType.VwapReversion),
        D<RsiMeanReversionDetector>(PatternType.RsiMeanReversion),
        D<TrendPullbackDetector>(PatternType.TrendPullback),
        D<VolumeSpikeContinuationDetector>(PatternType.VolumeSpikeContinuation),
        D<IndexRegimeFilterDetector>(PatternType.IndexRegimeFilter),
        D<VolatilityExpansionDetector>(PatternType.VolatilityExpansion),
        D<MomentumReversalDetector>(PatternType.MomentumReversal),
        D<MultiTimeframeTrendDetector>(PatternType.MultiTimeframeTrend),
        D<MeanReversionChannelDetector>(PatternType.MeanReversionChannel),
        D<Rsi2BollingerDetector>(PatternType.Rsi2Bollinger),
        D<VolatilityBreakoutDetector>(PatternType.VolatilityBreakout),
        D<Tqqq200SmaDetector>(PatternType.Tqqq200Sma),
        D<CumulativeRsi2Detector>(PatternType.CumulativeRsi2)
    ];

    public static BuiltInPatternDetectorDescriptor Get(PatternType patternType)
    {
        var detector = All.SingleOrDefault(item => item.PatternType == patternType);
        if (detector is not null)
            return detector;

        if (!PatternCatalog.TryGet(patternType, out var descriptor))
            throw new NotSupportedException($"알 수 없는 전략 코드({(int)patternType})입니다.");
        throw new NotSupportedException(
            descriptor.UnavailableReason
            ?? $"{descriptor.DisplayName} 내장 전략은 실행할 수 없습니다.");
    }

    private static BuiltInPatternDetectorDescriptor D<T>(PatternType patternType)
        where T : class, IPatternDetector => new(patternType, typeof(T));
}

public interface IBuiltInPatternDetectorFactory
{
    IReadOnlyList<IPatternDetector> CreateAll(PatternSettings settings);
    IPatternDetector Create(PatternType patternType, PatternSettings settings);
}

/// <summary>같은 카탈로그에서 기본 실행과 파라미터 오버라이드 감지기를 조립합니다.</summary>
public sealed class BuiltInPatternDetectorFactory(IServiceProvider services)
    : IBuiltInPatternDetectorFactory
{
    public IReadOnlyList<IPatternDetector> CreateAll(PatternSettings settings) =>
        BuiltInPatternDetectorCatalog.All
            .Select(descriptor => Create(descriptor, settings))
            .ToArray();

    public IPatternDetector Create(PatternType patternType, PatternSettings settings) =>
        Create(BuiltInPatternDetectorCatalog.Get(patternType), settings);

    private IPatternDetector Create(
        BuiltInPatternDetectorDescriptor descriptor,
        PatternSettings settings)
    {
        var options = new FixedOptionsSnapshot<PatternSettings>(settings);
        var detector = (IPatternDetector)ActivatorUtilities.CreateInstance(
            services, descriptor.ImplementationType, options);
        if (detector.PatternType != descriptor.PatternType)
            throw new InvalidOperationException(
                $"내장 전략 카탈로그 불일치: {descriptor.PatternType} -> {detector.PatternType}");
        return detector;
    }

    private sealed class FixedOptionsSnapshot<T>(T value) : IOptionsSnapshot<T>
        where T : class
    {
        public T Value { get; } = value;
        public T Get(string? name) => Value;
    }
}
