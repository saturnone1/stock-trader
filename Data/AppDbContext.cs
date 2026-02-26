using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Data;

public class AppDbContext : DbContext
{
    public DbSet<OhlcvBar> OhlcvBars => Set<OhlcvBar>();
    public DbSet<PatternSignal> PatternSignals => Set<PatternSignal>();
    public DbSet<PatternStats> PatternStats => Set<PatternStats>();
    public DbSet<TradeRecommendation> TradeRecommendations => Set<TradeRecommendation>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<TradeRecord> TradeRecords => Set<TradeRecord>();
    public DbSet<Ticker> Tickers => Set<Ticker>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OhlcvBar>(entity =>
        {
            entity.HasIndex(b => new { b.Symbol, b.TimeFrame, b.Timestamp }).IsUnique();
            entity.Ignore(b => b.Range);
            entity.Ignore(b => b.IsBullish);
            entity.Ignore(b => b.Body);
        });

        modelBuilder.Entity<PatternSignal>(entity =>
        {
            entity.HasIndex(s => new { s.Symbol, s.PatternType, s.DetectedAt });
        });

        modelBuilder.Entity<PatternStats>(entity =>
        {
            entity.HasIndex(s => new { s.PatternType, s.Symbol }).IsUnique();
            entity.Ignore(s => s.Expectancy);
            entity.Ignore(s => s.ProfitFactor);
        });

        modelBuilder.Entity<TradeRecommendation>(entity =>
        {
            entity.HasIndex(r => r.GeneratedAt);
            entity.Ignore(r => r.StopLossPercent);
            entity.Ignore(r => r.RiskRewardRatio);
        });

        modelBuilder.Entity<Position>(entity =>
        {
            entity.HasIndex(p => p.ClosedAt);
            entity.Ignore(p => p.IsOpen);
            entity.Ignore(p => p.UnrealizedPnL);
            entity.Ignore(p => p.RealizedPnL);
        });

        modelBuilder.Entity<TradeRecord>(entity =>
        {
            entity.HasIndex(t => new { t.PatternType, t.EntryTime });
            entity.Ignore(t => t.IsWin);
        });

        modelBuilder.Entity<Ticker>(entity =>
        {
            entity.HasKey(t => t.Symbol);
            entity.HasIndex(t => t.Sector);
        });

        modelBuilder.Entity<UserSettings>(entity =>
        {
            entity.Property(u => u.EnabledPatterns)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<List<PatternType>>(v, (JsonSerializerOptions?)null)!
                );

            entity.Property(u => u.WatchlistSymbols)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null)!
                );
        });
    }
}
