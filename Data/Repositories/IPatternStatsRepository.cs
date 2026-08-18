using StockTrader.Models;

namespace StockTrader.Data.Repositories;

public interface IPatternStatsRepository
{
    Task<PatternStats?> GetAsync(PatternType patternType, string? symbol = null,
        CancellationToken ct = default);
    Task<List<PatternStats>> GetAllAsync(CancellationToken ct = default);
    /// <summary>
    /// statsList를 기준으로 DB를 동기화한다. statsList에 없는 기존 행은 삭제된다.
    /// </summary>
    Task SaveBatchAsync(IEnumerable<PatternStats> statsList, CancellationToken ct = default);

    /// <summary>
    /// activeKeys에 포함되지 않는 모든 PatternStats 행을 삭제한다.
    /// RefreshAllStatsAsync 완료 후 호출하여 더 이상 거래가 없는 조합의 stale 통계를 제거한다.
    /// </summary>
    Task DeleteStaleAsync(ISet<(PatternType PatternType, string? Symbol)> activeKeys,
        CancellationToken ct = default);
}
