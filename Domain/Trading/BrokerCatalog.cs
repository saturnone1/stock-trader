namespace StockTrader.Domain.Trading;

/// <summary>저장소와 API에서 유지되는 안정적인 주문 브로커 식별자입니다.</summary>
public enum BrokerType
{
    Alpaca = 0,
    KoreaInvestment = 10,
    Kiwoom = 11,
    LsSecurities = 12
}

public sealed record BrokerDescriptor(
    BrokerType Type,
    string Code,
    string DisplayName,
    string Market,
    IReadOnlyList<string> Environments,
    string DefaultEnvironment,
    bool RequiresAccountCredentials,
    bool IsImplemented);

/// <summary>계좌 화면과 런타임 브로커 생성이 공유하는 단일 브로커 카탈로그입니다.</summary>
public static class BrokerCatalog
{
    public static IReadOnlyList<BrokerDescriptor> All { get; } =
    [
        new(
            BrokerType.Alpaca,
            nameof(BrokerType.Alpaca),
            "Alpaca",
            "미국 주식",
            ["Paper", "Live"],
            "Paper",
            RequiresAccountCredentials: true,
            IsImplemented: true),
        new(
            BrokerType.KoreaInvestment,
            nameof(BrokerType.KoreaInvestment),
            "한국투자증권",
            "국내 주식",
            ["Virtual", "Real"],
            "Virtual",
            RequiresAccountCredentials: false,
            IsImplemented: false),
        new(
            BrokerType.Kiwoom,
            nameof(BrokerType.Kiwoom),
            "키움증권",
            "국내 주식",
            ["Real"],
            "Real",
            RequiresAccountCredentials: false,
            IsImplemented: false),
        new(
            BrokerType.LsSecurities,
            nameof(BrokerType.LsSecurities),
            "LS증권",
            "국내 주식",
            ["Virtual", "Real"],
            "Virtual",
            RequiresAccountCredentials: false,
            IsImplemented: true)
    ];

    public static BrokerDescriptor Get(BrokerType type) =>
        All.FirstOrDefault(item => item.Type == type)
        ?? throw new ArgumentOutOfRangeException(
            nameof(type), type, $"Unsupported broker type: {type}");

    public static bool IsDefined(BrokerType type) =>
        All.Any(item => item.Type == type);
}
