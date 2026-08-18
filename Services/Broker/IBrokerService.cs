using StockTrader.Application.Accounts;
using StockTrader.Models;

namespace StockTrader.Services.Broker;

/// <summary>
/// 브로커 독립적인 주문 실행 인터페이스 (Ports &amp; Adapters 패턴의 Port).
///
/// 설계 원칙:
/// - 모든 메서드는 도메인 모델(BrokerAccount, BrokerOrder, Position)을 반환
/// - 브로커별 SDK 타입이 이 인터페이스 밖으로 노출되어서는 안 됨
/// - 실패 시 예외를 throw하지 않고 null/false/빈 컬렉션 반환 (예외는 구현체에서 로깅 후 변환)
/// </summary>
public interface IBrokerService
{
    /// <summary>기능 지원 여부를 중앙 카탈로그에서 해석하기 위한 안정적인 브로커 식별자.</summary>
    BrokerType BrokerType { get; }

    // ── 주문 실행 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 진입 주문을 제출한다. 익절/손절 보호 주문의 동시 제출 여부는
    /// 중앙 브로커 카탈로그의 기능 지원 정보에 따른다.
    /// </summary>
    /// <param name="recommendation">매매 추천 정보 (종목, 수량, 진입/목표/손절가)</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>브로커가 접수한 주문 증거. 제출 거부 또는 미구현이면 null.</returns>
    Task<BrokerOrder?> SubmitEntryOrderAsync(
        TradeRecommendation recommendation,
        CancellationToken ct = default);

    /// <summary>기존 롱 포지션에 지정 수량을 시장가로 추가하고 추적 가능한 주문을 반환한다.</summary>
    Task<BrokerOrder?> IncreasePositionAsync(
        string symbol,
        int quantity,
        CancellationToken ct = default);

    /// <summary>
    /// 특정 주문을 취소한다.
    /// </summary>
    /// <param name="orderId">브로커가 부여한 주문 ID</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>취소 성공 여부</returns>
    Task<bool> CancelOrderAsync(string orderId, CancellationToken ct = default);

    /// <summary>
    /// 보유 중인 포지션을 시장가로 청산한다.
    /// 연결된 브라켓 주문(손절/익절 대기 주문)도 자동 취소된다.
    /// </summary>
    /// <param name="symbol">종목 코드</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>접수된 청산 주문. 제출 실패 시 null.</returns>
    Task<BrokerOrder?> ClosePositionAsync(string symbol, CancellationToken ct = default);

    /// <summary>지정 수량만 시장가로 청산한다.</summary>
    Task<BrokerOrder?> ClosePositionAsync(string symbol, int quantity, CancellationToken ct = default);

    // ── 포지션 조회 ────────────────────────────────────────────────────────

    /// <summary>
    /// 현재 보유 중인 종목의 브로커 스냅샷을 반환한다. 실제 개설 시각과 전략
    /// 실행 상태는 브로커 잔고 응답에서 추측하지 않는다.
    /// </summary>
    /// <param name="ct">취소 토큰</param>
    /// <returns>포지션 목록. 오류 시 빈 컬렉션 반환</returns>
    Task<IReadOnlyList<BrokerPositionSnapshot>> GetPositionsAsync(
        CancellationToken ct = default);

    // ── 계좌 정보 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 계좌 잔고 및 상태를 조회한다.
    /// </summary>
    /// <param name="ct">취소 토큰</param>
    /// <returns>계좌 정보. 조회 실패 시 null</returns>
    Task<BrokerAccount?> GetAccountAsync(CancellationToken ct = default);

    // ── 주문 내역 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 지정 기간의 주문 내역을 조회한다.
    /// </summary>
    /// <param name="from">조회 시작 시각 (UTC)</param>
    /// <param name="to">조회 종료 시각 (UTC)</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>주문 목록. 오류 시 빈 컬렉션 반환</returns>
    Task<List<BrokerOrder>> GetOrderHistoryAsync(DateTime from, DateTime to,
        CancellationToken ct = default);
}
