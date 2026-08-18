using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Data.Repositories;

public class SettingsRepository : ISettingsRepository
{
    private const string CacheKey = "UserSettings";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(2);

    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly TimeProvider _timeProvider;

    public SettingsRepository(AppDbContext db, IMemoryCache cache, TimeProvider timeProvider)
    {
        _db = db;
        _cache = cache;
        _timeProvider = timeProvider;
    }

    public async Task<UserSettings> GetAsync(CancellationToken ct = default)
    {
        if (_cache.TryGetValue(CacheKey, out UserSettings? cached) && cached != null)
            return UserSettingsCopy.Create(cached);

        var settings = await _db.UserSettings
            .AsNoTracking()
            .OrderBy(item => item.Id)
            .FirstOrDefaultAsync(ct);
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
                LastModified = _timeProvider.GetUtcNow().UtcDateTime
            };
            _db.UserSettings.Add(settings);
            await _db.SaveChangesAsync(ct);
            _db.Entry(settings).State = EntityState.Detached;
        }

        _cache.Set(CacheKey, UserSettingsCopy.Create(settings), CacheTtl);
        return UserSettingsCopy.Create(settings);
    }

    public async Task SaveAsync(UserSettings settings, CancellationToken ct = default)
    {
        _db.UserSettings.Update(settings);
        await _db.SaveChangesAsync(ct);
        _db.Entry(settings).State = EntityState.Detached;

        _cache.Set(CacheKey, UserSettingsCopy.Create(settings), CacheTtl);
    }
}
