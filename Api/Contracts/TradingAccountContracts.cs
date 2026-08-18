using StockTrader.Application.Accounts;

namespace StockTrader.Api.Contracts;

public sealed record BrokerAccountOptionResponse(
    BrokerType Type,
    string Code,
    string DisplayName,
    string Market,
    IReadOnlyList<string> Environments,
    string DefaultEnvironment,
    bool RequiresAccountCredentials,
    bool IsImplemented,
    BrokerCapabilities Capabilities)
{
    public static BrokerAccountOptionResponse Create(BrokerDescriptor value) => new(
        value.Type,
        value.Code,
        value.DisplayName,
        value.Market,
        value.Environments,
        value.DefaultEnvironment,
        value.RequiresAccountCredentials,
        value.IsImplemented,
        value.Capabilities);
}

public sealed record TradingAccountMetadataResponse(
    IReadOnlyList<BrokerAccountOptionResponse> Brokers)
{
    public static TradingAccountMetadataResponse Create() => new(
        BrokerCatalog.All.Select(BrokerAccountOptionResponse.Create).ToArray());
}

public sealed record TradingAccountResponse(
    int Id,
    string AccountName,
    string BrokerType,
    string ApiKey,
    string Environment,
    bool IsActive,
    bool IsEnabled,
    string Notes,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? LastConnectedAt)
{
    public static TradingAccountResponse Create(ManagedTradingAccount value) => new(
        value.Id,
        value.AccountName,
        value.BrokerType.ToString(),
        TradingAccountPolicy.MaskApiKey(value.ApiKey),
        value.Environment,
        value.IsActive,
        value.IsEnabled,
        value.Notes,
        value.CreatedAt,
        value.UpdatedAt,
        value.LastConnectedAt);
}

public sealed record TradingAccountListResponse(
    int Count,
    IReadOnlyList<TradingAccountResponse> Accounts);

public sealed record TradingAccountCreateRequest
{
    public string AccountName { get; init; } = string.Empty;
    public BrokerType BrokerType { get; init; } = BrokerType.Alpaca;
    public string ApiKey { get; init; } = string.Empty;
    public string ApiSecret { get; init; } = string.Empty;
    public string Environment { get; init; } = "Paper";
    public bool IsActive { get; init; }
    public bool IsEnabled { get; init; } = true;
    public string Notes { get; init; } = string.Empty;

    public ManagedTradingAccount ToManaged() => new()
    {
        AccountName = AccountName ?? string.Empty,
        BrokerType = BrokerType,
        ApiKey = ApiKey ?? string.Empty,
        ApiSecret = ApiSecret ?? string.Empty,
        Environment = Environment ?? string.Empty,
        IsActive = IsActive,
        IsEnabled = IsEnabled,
        Notes = Notes ?? string.Empty
    };
}

public sealed record TradingAccountUpdateRequest
{
    public string AccountName { get; init; } = string.Empty;
    public BrokerType BrokerType { get; init; } = BrokerType.Alpaca;
    public string? ApiKey { get; init; }
    public string? ApiSecret { get; init; }
    public string Environment { get; init; } = "Paper";
    public bool IsActive { get; init; }
    public bool IsEnabled { get; init; } = true;
    public string Notes { get; init; } = string.Empty;

    public ManagedTradingAccount ApplyTo(ManagedTradingAccount existing) => existing with
    {
        AccountName = AccountName ?? string.Empty,
        BrokerType = BrokerType,
        ApiKey = string.IsNullOrWhiteSpace(ApiKey) ? existing.ApiKey : ApiKey,
        ApiSecret = string.IsNullOrWhiteSpace(ApiSecret) ? existing.ApiSecret : ApiSecret,
        Environment = Environment ?? string.Empty,
        IsActive = IsActive,
        IsEnabled = IsEnabled,
        Notes = Notes ?? string.Empty
    };
}

public sealed record AccountConnectionStatusResponse(
    int AccountId,
    bool IsConnected,
    string StatusMessage,
    decimal TotalEquity,
    decimal Cash,
    decimal BuyingPower,
    int OpenPositionCount,
    DateTime CheckedAt)
{
    public static AccountConnectionStatusResponse Create(AccountConnectionStatus value) => new(
        value.AccountId,
        value.IsConnected,
        value.StatusMessage,
        value.TotalEquity,
        value.Cash,
        value.BuyingPower,
        value.OpenPositionCount,
        value.CheckedAt);
}

public sealed record TradingAccountMessageResponse(string Message);
public sealed record TradingAccountErrorResponse(IReadOnlyList<string> Errors);
