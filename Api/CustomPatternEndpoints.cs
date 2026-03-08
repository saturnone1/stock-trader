using Microsoft.EntityFrameworkCore;
using StockTrader.Data;
using StockTrader.Models;

namespace StockTrader.Api;

public record BacktestApplyRequest(
    decimal? AtrStopMultiplier = null,
    decimal? AtrTargetMultiplier = null,
    int? MaxHoldingBars = null,
    decimal? TrailingAtr = null,
    decimal? PartialProfitR = null
);

public static class CustomPatternEndpoints
{
    public static RouteGroupBuilder MapCustomPatternApi(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/custom-patterns").RequireAuthorization();

        group.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
            await db.CustomPatterns.AsNoTracking()
                .OrderByDescending(p => p.UpdatedAt)
                .ToListAsync(ct));

        group.MapGet("/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var pattern = await db.CustomPatterns.FindAsync([id], ct);
            return pattern is null ? Results.NotFound() : Results.Ok(pattern);
        });

        group.MapPost("/", async (CustomPatternDefinition input, AppDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(input.Name))
                return Results.BadRequest(new { error = "패턴 이름을 입력하세요." });

            var existing = await db.CustomPatterns
                .FirstOrDefaultAsync(p => p.Name == input.Name, ct);

            if (existing != null)
            {
                existing.Description = input.Description;
                existing.EntryRulesJson = input.EntryRulesJson;
                existing.EntryLogic = input.EntryLogic;
                existing.RequireBullRegime = input.RequireBullRegime;
                existing.AtrStopMultiplier = input.AtrStopMultiplier;
                existing.AtrTargetMultiplier = input.AtrTargetMultiplier;
                existing.MaxHoldingBars = input.MaxHoldingBars;
                existing.TrailingAtr = input.TrailingAtr;
                existing.PartialProfitR = input.PartialProfitR;
                existing.UseWeightTiers = input.UseWeightTiers;
                existing.WeightTiersJson = input.WeightTiersJson;
                existing.DefaultAllocationPercent = input.DefaultAllocationPercent;
                existing.ExitRulesJson = input.ExitRulesJson;
                existing.ExitRulesLogic = input.ExitRulesLogic;
                existing.ScalingRulesJson = input.ScalingRulesJson;
                existing.TimeFilterJson = input.TimeFilterJson;
                existing.CircuitBreakerJson = input.CircuitBreakerJson;
                existing.ReentryJson = input.ReentryJson;
                existing.PortfolioRulesJson = input.PortfolioRulesJson;
                existing.EntryGroupsJson = input.EntryGroupsJson;
                existing.EntryGroupsLogic = input.EntryGroupsLogic;
                existing.DynamicExitJson = input.DynamicExitJson;
                existing.EntryMode = input.EntryMode;
                existing.SizingMode = input.SizingMode;
                existing.IsActive = input.IsActive;
                existing.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                return Results.Ok(existing);
            }

            input.Id = 0;
            input.CreatedAt = DateTime.UtcNow;
            input.UpdatedAt = DateTime.UtcNow;
            db.CustomPatterns.Add(input);
            await db.SaveChangesAsync(ct);
            return Results.Ok(input);
        });

        group.MapDelete("/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var pattern = await db.CustomPatterns.FindAsync([id], ct);
            if (pattern is null) return Results.NotFound();
            db.CustomPatterns.Remove(pattern);
            await db.SaveChangesAsync(ct);
            return Results.Ok();
        });

        group.MapPost("/{id:int}/apply-backtest", async (int id, BacktestApplyRequest req, AppDbContext db, CancellationToken ct) =>
        {
            var pattern = await db.CustomPatterns.FindAsync([id], ct);
            if (pattern is null) return Results.NotFound();
            pattern.UpdatedAt = DateTime.UtcNow;
            // req에서 최적 파라미터 반영 (AtrStop, AtrTarget 등)
            if (req.AtrStopMultiplier.HasValue) pattern.AtrStopMultiplier = req.AtrStopMultiplier.Value;
            if (req.AtrTargetMultiplier.HasValue) pattern.AtrTargetMultiplier = req.AtrTargetMultiplier.Value;
            if (req.MaxHoldingBars.HasValue) pattern.MaxHoldingBars = req.MaxHoldingBars.Value;
            if (req.TrailingAtr.HasValue) pattern.TrailingAtr = req.TrailingAtr.Value;
            if (req.PartialProfitR.HasValue) pattern.PartialProfitR = req.PartialProfitR.Value;
            await db.SaveChangesAsync(ct);
            return Results.Ok(pattern);
        });

        return api;
    }
}
