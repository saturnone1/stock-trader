namespace StockTrader.Api;

// ── Job 생성 요청 ──────────────────────────────────────────────────────────────

/// <summary>
/// 연속 파라미터 최적화 Job 생성 요청 DTO.
/// OptimizeRequest를 포함하며, Job 실행 제어 옵션을 추가로 정의합니다.
/// </summary>
public class CreateOptimizeJobRequest
{
    /// <summary>사용자가 지정하는 Job 이름 (식별용)</summary>
    public string Name { get; set; } = "";

    /// <summary>실행 우선순위 — 높을수록 먼저 실행</summary>
    public int Priority { get; set; }

    /// <summary>한 번에 처리할 조합 수 (기본 200)</summary>
    public int ChunkSize { get; set; } = 200;

    /// <summary>최대 실행 시간(시간 단위). null = 무제한</summary>
    public decimal? MaxDurationHours { get; set; }

    /// <summary>최대 테스트 조합 수. null = 무제한</summary>
    public long? MaxTestedCombinations { get; set; }

    /// <summary>보존할 상위 결과 수 (기본 50)</summary>
    public int TopResultsToKeep { get; set; } = 50;

    /// <summary>결과 정렬 기준: totalReturn, sortinoRatio, sharpeRatio, calmarRatio, profitFactor, winRate</summary>
    public string RankBy { get; set; } = "sortinoRatio";

    /// <summary>최적화 실행 파라미터 (기존 OptimizeRequest와 동일한 구조)</summary>
    public OptimizeRequest OptimizeRequest { get; set; } = new();
}

// ── Job 목록 응답 ─────────────────────────────────────────────────────────────

/// <summary>
/// Job 목록 조회 시 반환되는 요약 DTO.
/// 무거운 결과 데이터는 포함하지 않습니다.
/// </summary>
public class OptimizeJobSummary
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
    public long TotalCombinations { get; set; }
    public long TestedCombinations { get; set; }
    public decimal ProgressPercent { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
}

// ── Job 상세 응답 ─────────────────────────────────────────────────────────────

/// <summary>
/// Job 상세 조회 시 반환되는 전체 DTO.
/// 진행률, 예상 잔여 시간, 상위 3개 결과 미리보기를 포함합니다.
/// </summary>
public class OptimizeJobDetail
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
    public long TotalCombinations { get; set; }
    public long TestedCombinations { get; set; }
    public decimal ProgressPercent { get; set; }

    /// <summary>시작 이후 경과 시간(초). StartedAt이 null이면 null.</summary>
    public double? ElapsedSeconds { get; set; }

    /// <summary>예상 잔여 시간(초). 진행률이 0이거나 완료된 경우 null.</summary>
    public double? EstimatedRemainingSeconds { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? LastProgressAt { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>상위 3개 결과 미리보기. 결과가 없으면 null.</summary>
    public List<OptimizeResultItem>? TopResults { get; set; }
}
