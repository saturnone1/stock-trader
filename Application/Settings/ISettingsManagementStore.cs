namespace StockTrader.Application.Settings;

public interface ISettingsManagementStore
{
    Task<ManagedSettings> GetAsync(CancellationToken ct = default);
    Task SaveAsync(ManagedSettings settings, CancellationToken ct = default);
}
