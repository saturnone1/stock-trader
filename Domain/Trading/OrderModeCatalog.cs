namespace StockTrader.Domain.Trading;

public sealed record OrderModeDescriptor(
    OrderMode Value,
    string Code,
    string DisplayName,
    string Description);

public static class OrderModeCatalog
{
    public static IReadOnlyList<OrderModeDescriptor> All { get; } =
    [
        new(
            OrderMode.AlertOnly,
            nameof(OrderMode.AlertOnly),
            "알림만 받기",
            "매매 신호를 알리되 주문은 직접 실행합니다."),
        new(
            OrderMode.AutoOrder,
            nameof(OrderMode.AutoOrder),
            "자동 주문",
            "조건을 통과한 신호를 연결된 증권사 계좌에 주문합니다.")
    ];

    public static OrderModeDescriptor Get(OrderMode value) =>
        All.Single(item => item.Value == value);

    public static bool Contains(OrderMode value) =>
        All.Any(item => item.Value == value);
}
