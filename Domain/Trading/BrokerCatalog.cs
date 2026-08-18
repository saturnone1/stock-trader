namespace StockTrader.Domain.Trading;

/// <summary>저장소와 API에서 유지되는 안정적인 주문 브로커 식별자입니다.</summary>
public enum BrokerType
{
    Alpaca = 0,
    KoreaInvestment = 10,
    Kiwoom = 11,
    LsSecurities = 12
}

public sealed record BrokerCapabilities(
    bool CanReadAccount,
    bool CanReadPositions,
    bool CanReadOrderHistory,
    bool CanSubmitProtectedEntry,
    bool CanScaleIn,
    bool CanCloseFullPosition,
    bool CanClosePartialPosition,
    bool CanCancelOrder)
{
    public static BrokerCapabilities Full { get; } = new(true, true, true, true, true, true, true, true);
    public static BrokerCapabilities LsSecurities { get; } = new(true, true, true, false, true, true, true, true);
    public static BrokerCapabilities None { get; } = new(false, false, false, false, false, false, false, false);

    public bool IsImplemented => CanReadAccount || CanReadPositions || CanReadOrderHistory
        || CanSubmitProtectedEntry || CanScaleIn || CanCloseFullPosition
        || CanClosePartialPosition || CanCancelOrder;
}

public sealed record BrokerDescriptor(
    BrokerType Type,
    string Code,
    string DisplayName,
    string Market,
    IReadOnlyList<string> Environments,
    string DefaultEnvironment,
    bool RequiresAccountCredentials,
    BrokerCapabilities Capabilities)
{
    public bool IsImplemented => Capabilities.IsImplemented;
}

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
            BrokerCapabilities.Full),
        new(
            BrokerType.KoreaInvestment,
            nameof(BrokerType.KoreaInvestment),
            "한국투자증권",
            "국내 주식",
            ["Virtual", "Real"],
            "Virtual",
            RequiresAccountCredentials: false,
            BrokerCapabilities.None),
        new(
            BrokerType.Kiwoom,
            nameof(BrokerType.Kiwoom),
            "키움증권",
            "국내 주식",
            ["Real"],
            "Real",
            RequiresAccountCredentials: false,
            BrokerCapabilities.None),
        new(
            BrokerType.LsSecurities,
            nameof(BrokerType.LsSecurities),
            "LS증권",
            "국내 주식",
            ["Virtual", "Real"],
            "Virtual",
            RequiresAccountCredentials: false,
            BrokerCapabilities.LsSecurities)
    ];

    public static BrokerDescriptor Get(BrokerType type) =>
        All.FirstOrDefault(item => item.Type == type)
        ?? throw new ArgumentOutOfRangeException(
            nameof(type), type, $"Unsupported broker type: {type}");

    public static bool IsDefined(BrokerType type) =>
        All.Any(item => item.Type == type);

    public static bool CanMonitorPositions(BrokerType type)
    {
        var capabilities = Get(type).Capabilities;
        return capabilities.CanReadAccount && capabilities.CanReadPositions;
    }
}
