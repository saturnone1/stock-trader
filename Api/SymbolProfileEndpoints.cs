using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StockTrader.Data;
using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Api;

public static class SymbolProfileEndpoints
{
    public static RouteGroupBuilder MapSymbolProfileApi(this RouteGroupBuilder group)
    {
        // GET /api/symbol-profiles — 전체 프로파일 목록
        group.MapGet("/symbol-profiles", async (AppDbContext db, CancellationToken ct) =>
        {
            var profiles = await db.SymbolProfiles
                .OrderBy(p => p.Symbol).ThenByDescending(p => p.IsActive).ThenBy(p => p.Name)
                .ToListAsync(ct);

            return Results.Ok(profiles.Select(MapToDto));
        }).RequireAuthorization();

        // GET /api/symbol-profiles/{symbol} — 특정 종목의 프로파일 목록
        group.MapGet("/symbol-profiles/{symbol}", async (
            string symbol, AppDbContext db, CancellationToken ct) =>
        {
            var profiles = await db.SymbolProfiles
                .Where(p => p.Symbol == symbol.ToUpperInvariant())
                .OrderByDescending(p => p.IsActive).ThenBy(p => p.Name)
                .ToListAsync(ct);

            return Results.Ok(profiles.Select(MapToDto));
        }).RequireAuthorization();

        // POST /api/symbol-profiles — 프로파일 생성/업데이트
        group.MapPost("/symbol-profiles", async (
            HttpContext ctx, AppDbContext db, CancellationToken ct) =>
        {
            SymbolProfileDto? dto;
            try
            {
                dto = await ctx.Request.ReadFromJsonAsync<SymbolProfileDto>(ct);
                if (dto == null) return Results.BadRequest(new { error = "Request body is null." });
            }
            catch
            {
                return Results.BadRequest(new { error = "Invalid JSON body." });
            }

            if (string.IsNullOrWhiteSpace(dto.Symbol))
                return Results.BadRequest(new { error = "Symbol is required." });

            var symbol = dto.Symbol.Trim().ToUpperInvariant();
            var name = string.IsNullOrWhiteSpace(dto.Name) ? "기본" : dto.Name.Trim();

            // 기존 프로파일 찾기 (symbol + name 조합)
            var existing = await db.SymbolProfiles
                .FirstOrDefaultAsync(p => p.Symbol == symbol && p.Name == name, ct);

            if (existing != null)
            {
                // 업데이트
                existing.EnabledPatterns = dto.EnabledPatterns?.Select(p => Enum.Parse<PatternType>(p)).ToList()
                    ?? existing.EnabledPatterns;
                existing.ParameterOverridesJson = dto.ParameterOverridesJson ?? existing.ParameterOverridesJson;
                existing.WeightStrategyJson = dto.WeightStrategyJson ?? existing.WeightStrategyJson;
                existing.RiskPerTradePercent = dto.RiskPerTradePercent ?? existing.RiskPerTradePercent;
                existing.MaxTotalPositions = dto.MaxTotalPositions ?? existing.MaxTotalPositions;
                existing.BacktestReturnPct = dto.BacktestReturnPct ?? existing.BacktestReturnPct;
                existing.BacktestWinRate = dto.BacktestWinRate ?? existing.BacktestWinRate;
                existing.BacktestMaxDrawdown = dto.BacktestMaxDrawdown ?? existing.BacktestMaxDrawdown;
                existing.BacktestSharpe = dto.BacktestSharpe ?? existing.BacktestSharpe;
                existing.BacktestTrades = dto.BacktestTrades ?? existing.BacktestTrades;
                existing.BacktestFrom = dto.BacktestFrom ?? existing.BacktestFrom;
                existing.BacktestTo = dto.BacktestTo ?? existing.BacktestTo;
                existing.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                return Results.Ok(MapToDto(existing));
            }
            else
            {
                // 신규 생성
                var profile = new SymbolProfile
                {
                    Symbol = symbol,
                    Name = name,
                    EnabledPatterns = dto.EnabledPatterns?.Select(p => Enum.Parse<PatternType>(p)).ToList()
                        ?? new List<PatternType>(),
                    ParameterOverridesJson = dto.ParameterOverridesJson,
                    WeightStrategyJson = dto.WeightStrategyJson,
                    RiskPerTradePercent = dto.RiskPerTradePercent ?? 0.01m,
                    MaxTotalPositions = dto.MaxTotalPositions ?? 7,
                    BacktestReturnPct = dto.BacktestReturnPct,
                    BacktestWinRate = dto.BacktestWinRate,
                    BacktestMaxDrawdown = dto.BacktestMaxDrawdown,
                    BacktestSharpe = dto.BacktestSharpe,
                    BacktestTrades = dto.BacktestTrades,
                    BacktestFrom = dto.BacktestFrom,
                    BacktestTo = dto.BacktestTo,
                };
                db.SymbolProfiles.Add(profile);
                await db.SaveChangesAsync(ct);
                return Results.Created($"/api/symbol-profiles/{profile.Id}", MapToDto(profile));
            }
        }).RequireAuthorization();

        // POST /api/symbol-profiles/{id}/activate — 프로파일 활성화 (같은 종목의 다른 프로파일은 비활성)
        group.MapPost("/symbol-profiles/{id}/activate", async (
            long id, AppDbContext db, CancellationToken ct) =>
        {
            var profile = await db.SymbolProfiles.FindAsync(new object[] { id }, ct);
            if (profile == null) return Results.NotFound();

            // 같은 종목의 모든 프로파일 비활성화
            var sameSymbol = await db.SymbolProfiles
                .Where(p => p.Symbol == profile.Symbol && p.IsActive)
                .ToListAsync(ct);
            foreach (var p in sameSymbol) p.IsActive = false;

            profile.IsActive = true;
            profile.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { message = $"{profile.Symbol} - {profile.Name} 프로파일이 활성화되었습니다." });
        }).RequireAuthorization();

        // POST /api/symbol-profiles/{id}/deactivate — 프로파일 비활성화
        group.MapPost("/symbol-profiles/{id}/deactivate", async (
            long id, AppDbContext db, CancellationToken ct) =>
        {
            var profile = await db.SymbolProfiles.FindAsync(new object[] { id }, ct);
            if (profile == null) return Results.NotFound();

            profile.IsActive = false;
            profile.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { message = $"{profile.Symbol} - {profile.Name} 프로파일이 비활성화되었습니다." });
        }).RequireAuthorization();

        // DELETE /api/symbol-profiles/{id}
        group.MapDelete("/symbol-profiles/{id}", async (
            long id, AppDbContext db, CancellationToken ct) =>
        {
            var profile = await db.SymbolProfiles.FindAsync(new object[] { id }, ct);
            if (profile == null) return Results.NotFound();

            db.SymbolProfiles.Remove(profile);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { message = "삭제되었습니다." });
        }).RequireAuthorization();

        return group;
    }

    private static object MapToDto(SymbolProfile p) => new
    {
        p.Id, p.Symbol, p.Name, p.IsActive,
        EnabledPatterns = p.EnabledPatterns.Select(pt => pt.ToString()),
        p.ParameterOverridesJson,
        p.WeightStrategyJson,
        p.RiskPerTradePercent, p.MaxTotalPositions,
        p.BacktestReturnPct, p.BacktestWinRate, p.BacktestMaxDrawdown,
        p.BacktestSharpe, p.BacktestTrades,
        BacktestFrom = p.BacktestFrom?.ToString("yyyy-MM-dd"),
        BacktestTo = p.BacktestTo?.ToString("yyyy-MM-dd"),
        CreatedAt = p.CreatedAt.ToString("o"),
        UpdatedAt = p.UpdatedAt.ToString("o"),
    };
}

public class SymbolProfileDto
{
    public string? Symbol { get; set; }
    public string? Name { get; set; }
    public List<string>? EnabledPatterns { get; set; }
    public string? ParameterOverridesJson { get; set; }
    public string? WeightStrategyJson { get; set; }
    public decimal? RiskPerTradePercent { get; set; }
    public int? MaxTotalPositions { get; set; }
    public decimal? BacktestReturnPct { get; set; }
    public decimal? BacktestWinRate { get; set; }
    public decimal? BacktestMaxDrawdown { get; set; }
    public decimal? BacktestSharpe { get; set; }
    public int? BacktestTrades { get; set; }
    public DateTime? BacktestFrom { get; set; }
    public DateTime? BacktestTo { get; set; }
}
