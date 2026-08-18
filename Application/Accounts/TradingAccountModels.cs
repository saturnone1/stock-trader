namespace StockTrader.Application.Accounts;

public sealed record ManagedTradingAccount
{
    public int Id { get; init; }
    public string AccountName { get; init; } = string.Empty;
    public BrokerType BrokerType { get; init; } = BrokerType.Alpaca;
    public string ApiKey { get; init; } = string.Empty;
    public string ApiSecret { get; init; } = string.Empty;
    public string Environment { get; init; } = "Paper";
    public bool IsActive { get; init; }
    public bool IsEnabled { get; init; } = true;
    public string Notes { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public DateTime? LastConnectedAt { get; init; }
}

public sealed record TradingAccountDeletion(
    bool Deleted,
    bool DeletedWasActive,
    int? ActivatedAccountId);

public sealed record AccountConnectionStatus
{
    public int AccountId { get; init; }
    public bool IsConnected { get; init; }
    public string StatusMessage { get; init; } = string.Empty;
    public decimal TotalEquity { get; init; }
    public decimal Cash { get; init; }
    public decimal BuyingPower { get; init; }
    public int OpenPositionCount { get; init; }
    public DateTime CheckedAt { get; init; }
}

public sealed record TradingAccountValidationResult(
    bool Succeeded,
    IReadOnlyList<string> Errors)
{
    public static TradingAccountValidationResult Success { get; } = new(true, []);
}
