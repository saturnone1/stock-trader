using StockTrader.Models;

namespace StockTrader.Data.Repositories;

public interface ISettingsRepository
{
    Task<UserSettings> GetAsync(CancellationToken ct = default);
    Task SaveAsync(UserSettings settings, CancellationToken ct = default);
}
