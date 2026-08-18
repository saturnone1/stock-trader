using StockTrader.Domain.Optimization;

namespace StockTrader.Models;

public enum OptimizationJobStatus
{
    Pending,    // 대기 중
    Running,    // 실행 중
    Paused,     // 일시정지
    Completed,  // 완료
    Cancelled,  // 취소됨
    Failed      // 실패
}

public class OptimizationJob
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public OptimizationJobStatus Status { get; set; } = OptimizationJobStatus.Pending;
    public int Priority { get; set; }

    /// <summary>작업 정의 — OptimizeRequest를 JSON으로 직렬화</summary>
    public string RequestJson { get; set; } = "";

    // 파라미터 공간
    public long TotalCombinations { get; set; }
    public long TestedCombinations { get; set; }
    public int CurrentChunkIndex { get; set; }

    // 청크 설정
    public int ChunkSize { get; set; } = 200;

    // 시간
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? LastProgressAt { get; set; }

    // 실행 제한
    public decimal? MaxDurationHours { get; set; }
    public long? MaxTestedCombinations { get; set; }

    // 결과 설정
    public string RankBy { get; set; } = OptimizationRankingCatalog.DefaultCode;
    public int TopResultsToKeep { get; set; } = 50;
    public bool ContinuousMode { get; set; }
    public bool AutoApplyBestResult { get; set; }
    public int AutoApplyMinTrades { get; set; } = 10;
    public int AppliedResultCount { get; set; }
    public DateTime? LastAutoAppliedAt { get; set; }
    public int? LastAutoAppliedResultId { get; set; }
    public string? LastAutoApplyMessage { get; set; }

    // 오류
    public string? ErrorMessage { get; set; }

    // Navigation
    public List<OptimizationResult> Results { get; set; } = new();
}
