namespace StockTrader.Application.Execution;

public enum LiveOrderManagementFailure
{
    None,
    InvalidRequest,
    NotFound,
    Conflict,
}

/// <summary>HTTP와 무관한 수동 주문 관리 유스케이스 결과.</summary>
public sealed record LiveOrderManagementResult(
    LiveOrderManagementFailure Failure,
    string? Status = null,
    string? Message = null,
    string? Error = null,
    bool Accepted = false,
    DateTime? RequestedAt = null,
    string? BrokerStatus = null,
    decimal? FillPrice = null,
    int? FilledQuantity = null,
    bool? BrokerOrderIdPersisted = null)
{
    public bool IsSuccess => Failure == LiveOrderManagementFailure.None;
}

public interface ILiveOrderManagement
{
    Task<LiveOrderManagementResult> ClosePositionAsync(
        string symbol,
        CancellationToken ct = default);

    Task<LiveOrderManagementResult> ReconcilePositionAsync(
        string symbol,
        CancellationToken ct = default);

    Task<LiveOrderManagementResult> ReconcileEntryAsync(
        long recommendationId,
        CancellationToken ct = default);
}
