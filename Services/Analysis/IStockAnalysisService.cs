using StockTrader.Models;

namespace StockTrader.Services.Analysis;

public interface IStockAnalysisService
{
    Task<StockAnalysis> AnalyzeAsync(string symbol, CancellationToken ct = default);

    /// <summary>
    /// 관심종목 전체를 병렬로 분석합니다.
    /// 결과는 상승 확률 내림차순으로 정렬되어 반환됩니다.
    /// </summary>
    Task<List<StockAnalysis>> AnalyzeWatchlistAsync(CancellationToken ct = default);

    /// <summary>
    /// 관심종목을 병렬로 분석하되, 종목 하나가 완료될 때마다 콜백을 호출합니다.
    /// 점진적 렌더링(progressive rendering)에 사용합니다.
    /// </summary>
    Task<List<StockAnalysis>> AnalyzeWatchlistProgressiveAsync(
        Func<StockAnalysis, Task> onItemCompleted,
        CancellationToken ct = default);
}
