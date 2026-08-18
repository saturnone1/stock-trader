using StockTrader.Models;

namespace StockTrader.Application.Trading;

public sealed record LiveDailyScanContext(string RegimeBenchmarkSymbol);

/// <summary>현재 공급자 선택과 저장된 일봉 조회를 실시간 스캔 유스케이스에 제공합니다.</summary>
public interface ILiveDailyScanData
{
    Task<LiveDailyScanContext> ResolveContextAsync(CancellationToken ct = default);

    Task<IReadOnlyList<OhlcvBar>> LoadBarsAsync(
        string symbol,
        DateTime from,
        DateTime to,
        CancellationToken ct = default);
}

/// <summary>공통 컴파일 전략과 내장 감지기로 한 종목의 신호를 평가합니다.</summary>
public interface ILivePatternDetection
{
    Task<List<PatternSignal>> ScanSymbolAsync(
        string symbol,
        OhlcvBar[] bars,
        MarketRegime regime,
        CancellationToken ct = default);
}

/// <summary>기준 종목의 완료 일봉에서 실시간 스캔용 시장 국면을 계산합니다.</summary>
public interface ILiveMarketRegimeEvaluator
{
    MarketRegime Evaluate(IReadOnlyList<OhlcvBar> bars, DateTime observedAt);
}

/// <summary>감지된 신호의 저장, 추천 평가, 주문 모드 적용을 한 경계에서 처리합니다.</summary>
public interface ILiveSignalProcessor
{
    Task ProcessAsync(
        IReadOnlyList<PatternSignal> signals,
        CancellationToken ct = default);
}

/// <summary>한 종목의 완료 일봉 스캔을 한 번 실행합니다.</summary>
public interface ILivePatternScanCycle
{
    Task RunAsync(string symbol, CancellationToken ct = default);
}
