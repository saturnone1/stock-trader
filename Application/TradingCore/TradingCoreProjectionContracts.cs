using StockTrader.ServiceContracts.TradingCore;

namespace StockTrader.Application.TradingCore;

public interface ITradingCoreProjectionSource
{
    Task<TradingStateSnapshot> CaptureAsync(CancellationToken ct = default);
}

public interface ITradingCoreAccountConfigurationSource
{
    Task<TradingAccountConfigurationSet> CaptureAsync(CancellationToken ct = default);
}

public interface ITradingAccountIdentitySource
{
    Task<string?> GetActiveAccountIdAsync(CancellationToken ct = default);
}

public interface ITradingCoreControlPlane
{
    Task<bool> PublishProjectionAsync(
        TradingStateSnapshot snapshot,
        CancellationToken ct = default);

    Task<bool> PublishAccountConfigurationAsync(
        TradingAccountConfigurationSet configuration,
        CancellationToken ct = default);

    Task<TradingCoreStatus> GetStatusAsync(CancellationToken ct = default);

    Task<TradingCorePortfolioView> GetPortfolioAsync(CancellationToken ct = default);

    Task<TradingCommandReceipt> SubmitEntryAsync(
        TradingEntryIntent intent,
        CancellationToken ct = default);

    Task<TradingCommandReceipt> SubmitPositionAsync(
        TradingPositionCommand command,
        CancellationToken ct = default);

    Task<TradingCommandStatusView?> GetCommandAsync(
        string commandId,
        CancellationToken ct = default);
}
