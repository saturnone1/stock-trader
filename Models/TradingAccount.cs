using System.ComponentModel.DataAnnotations;
namespace StockTrader.Models;

/// <summary>
/// DB에 영속화되는 거래 계좌 엔티티.
///
/// 멀티 브로커 지원을 위한 계좌 정보를 관리한다.
/// API 키/시크릿은 로컬 SQLite에 평문 저장 (로컬 전용 앱 전제).
/// 프로덕션 환경에서는 DPAPI 또는 별도 Vault 사용 권장.
/// </summary>
public class TradingAccount
{
    [Key]
    public int Id { get; set; }

    /// <summary>사용자가 지정한 계좌 별칭 (예: "Alpaca Paper #1")</summary>
    [Required]
    [MaxLength(100)]
    public string AccountName { get; set; } = string.Empty;

    /// <summary>연결된 브로커 종류</summary>
    public BrokerType BrokerType { get; set; } = BrokerType.Alpaca;

    /// <summary>브로커 API 키 (Alpaca: key_id)</summary>
    [MaxLength(200)]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>브로커 API 시크릿 (Alpaca: secret_key)</summary>
    [MaxLength(200)]
    public string ApiSecret { get; set; } = string.Empty;

    /// <summary>
    /// 브로커별 추가 옵션.
    /// Alpaca: "Paper" | "Live"
    /// KoreaInvestment: "Real" | "Virtual"
    /// </summary>
    [MaxLength(50)]
    public string Environment { get; set; } = "Paper";

    /// <summary>이 계좌가 현재 활성(사용 중)인지 여부. 오직 1개만 true여야 한다.</summary>
    public bool IsActive { get; set; }

    /// <summary>계좌 활성화/비활성화 (소프트 삭제가 아닌 임시 정지용)</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>계좌 메모 (선택 사항)</summary>
    [MaxLength(500)]
    public string Notes { get; set; } = string.Empty;

    /// <summary>계좌 생성 시각 (UTC)</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>마지막 수정 시각 (UTC)</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>마지막 연결 확인 시각 (UTC, null이면 미확인)</summary>
    public DateTime? LastConnectedAt { get; set; }
}
