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

    public SettingsRepository(AppDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<UserSettings> GetAsync(CancellationToken ct = default)
    {
        if (_cache.TryGetValue(CacheKey, out UserSettings? cached) && cached != null)
            return cached;

        var settings = await _db.UserSettings
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
                LastModified = DateTime.UtcNow
            };
            _db.UserSettings.Add(settings);
            await _db.SaveChangesAsync(ct);
        }

        _cache.Set(CacheKey, settings, CacheTtl);
        return settings;
    }

    public async Task SaveAsync(UserSettings settings, CancellationToken ct = default)
    {
        settings.LastModified = DateTime.UtcNow;
        _db.UserSettings.Update(settings);
        await _db.SaveChangesAsync(ct);

        // 저장 후 캐시 무효화 — 다음 GetAsync에서 DB에서 최신 값을 로드한다
        _cache.Remove(CacheKey);
    }
}
