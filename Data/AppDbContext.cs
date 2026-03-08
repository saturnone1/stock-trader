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
    public DbSet<TradingAccount> TradingAccounts => Set<TradingAccount>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<SymbolProfile> SymbolProfiles => Set<SymbolProfile>();
    public DbSet<CustomPatternDefinition> CustomPatterns => Set<CustomPatternDefinition>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OhlcvBar>(entity =>
        {
            entity.HasIndex(b => new { b.Symbol, b.TimeFrame, b.Timestamp }).IsUnique();
            // TimeFrame별 전체 쿼리 가속 (예: 모든 Daily 바 일괄 조회)
            entity.HasIndex(b => new { b.TimeFrame, b.Symbol, b.Timestamp });
            entity.Ignore(b => b.Range);
            entity.Ignore(b => b.IsBullish);
            entity.Ignore(b => b.Body);
        });

        modelBuilder.Entity<PatternSignal>(entity =>
        {
            entity.HasIndex(s => new { s.Symbol, s.PatternType, s.DetectedAt }).IsUnique();
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
            entity.HasIndex(p => p.Symbol);
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

        modelBuilder.Entity<TradingAccount>(entity =>
        {
            entity.HasIndex(a => a.BrokerType);
            entity.HasIndex(a => a.IsActive);
            // AccountName을 고유하게 강제하지 않음 — 같은 브로커에 여러 계좌 허용
        });

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.HasIndex(u => u.Username).IsUnique();
            entity.HasIndex(u => u.IsActive);
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasIndex(l => l.Timestamp);
            entity.HasIndex(l => l.UserId);
            entity.HasIndex(l => l.Action);
        });

        modelBuilder.Entity<SymbolProfile>(entity =>
        {
            entity.HasIndex(p => new { p.Symbol, p.Name }).IsUnique();
            entity.HasIndex(p => new { p.Symbol, p.IsActive });
            entity.Property(p => p.EnabledPatterns)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<List<PatternType>>(v, (JsonSerializerOptions?)null)!
                );
        });

        modelBuilder.Entity<CustomPatternDefinition>(entity =>
        {
            entity.HasIndex(p => p.Name).IsUnique();
            entity.HasIndex(p => p.IsActive);
        });
    }
}
