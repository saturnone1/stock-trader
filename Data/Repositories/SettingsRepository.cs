using Microsoft.EntityFrameworkCore;
using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Data.Repositories;

public class SettingsRepository : ISettingsRepository
{
    private readonly AppDbContext _db;

    public SettingsRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<UserSettings> GetAsync(CancellationToken ct = default)
    {
        var settings = await _db.UserSettings.FirstOrDefaultAsync(ct);
        if (settings == null)
        {
            settings = new UserSettings
            {
                OrderMode = OrderMode.AlertOnly,
                PreferredDataSource = DataSource.Alpaca,
                EnabledPatterns = new List<PatternType>
                {
                    PatternType.GapUpPullback,
                    PatternType.Breakout,
                    PatternType.VwapReversion
                },
                WatchlistSymbols = new List<string> { "AAPL", "MSFT", "GOOGL", "AMZN", "TSLA", "SPY" },
                AccountSize = 100_000m,
                SoundAlerts = true,
                LastModified = DateTime.UtcNow
            };
            _db.UserSettings.Add(settings);
            await _db.SaveChangesAsync(ct);
        }
        return settings;
    }

    public async Task SaveAsync(UserSettings settings, CancellationToken ct = default)
    {
        settings.LastModified = DateTime.UtcNow;
        _db.UserSettings.Update(settings);
        await _db.SaveChangesAsync(ct);
    }
}
