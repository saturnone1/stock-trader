namespace StockTrader.Models;

public enum OptimizationWorkerLeaseStatus
{
    Pending,
    Leased,
    Completed,
    Cancelled
}

/// <summary>
/// Strategy Research가 단독 소유하는 원격 계산 임대 기록입니다. Worker는 이 테이블이나
/// 애플리케이션 데이터베이스에 직접 접근하지 않고 인증된 계약 API만 사용합니다.
/// </summary>
public sealed class OptimizationWorkerLeaseRecord
{
    public string LeaseId { get; set; } = string.Empty;
    public int JobId { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public string InputHash { get; set; } = string.Empty;
    public string InputJson { get; set; } = string.Empty;
    public OptimizationWorkerLeaseStatus Status { get; set; }
    public string? WorkerId { get; set; }
    public long LeaseGeneration { get; set; }
    public long CancellationGeneration { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LeasedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastHeartbeatAt { get; set; }
    public long TestedCombinations { get; set; }
    public string? SubmissionId { get; set; }
    public string? ResultHash { get; set; }
    public string? ResultJson { get; set; }
    public DateTime? CompletedAt { get; set; }
}
