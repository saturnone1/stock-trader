using Microsoft.EntityFrameworkCore;
using StockTrader.Data;
using StockTrader.Models;
using StockTrader.Services.Patterns;

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
            var validationErrors = CustomPatternValidator.Validate(input);
            if (validationErrors.Count > 0)
                return Results.BadRequest(new { error = validationErrors[0], errors = validationErrors });

            var normalizedName = input.Name.Trim().ToLower();
            if (await db.CustomPatterns.AnyAsync(p => p.Name.ToLower() == normalizedName, ct))
                return Results.Conflict(new { error = "같은 이름의 전략이 이미 있습니다. 다른 이름을 사용하세요." });

            input.Name = input.Name.Trim();
            input.Id = 0;
            input.CreatedAt = DateTime.UtcNow;
            input.UpdatedAt = DateTime.UtcNow;
            db.CustomPatterns.Add(input);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/custom-patterns/{input.Id}", input);
        });

        group.MapPut("/{id:int}", async (int id, CustomPatternDefinition input, AppDbContext db, CancellationToken ct) =>
        {
            var validationErrors = CustomPatternValidator.Validate(input);
            if (validationErrors.Count > 0)
                return Results.BadRequest(new { error = validationErrors[0], errors = validationErrors });

            var existing = await db.CustomPatterns.FindAsync([id], ct);
            if (existing is null) return Results.NotFound(new { error = "수정할 전략을 찾을 수 없습니다." });

            var normalizedName = input.Name.Trim().ToLower();
            if (await db.CustomPatterns.AnyAsync(p => p.Id != id && p.Name.ToLower() == normalizedName, ct))
                return Results.Conflict(new { error = "같은 이름의 전략이 이미 있습니다. 다른 이름을 사용하세요." });

            CopyEditableFields(existing, input);
            existing.Name = input.Name.Trim();
            existing.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(existing);
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

    private static void CopyEditableFields(CustomPatternDefinition target, CustomPatternDefinition source)
    {
        target.Description = source.Description;
        target.EntryRulesJson = source.EntryRulesJson;
        target.EntryLogic = source.EntryLogic;
        target.RequireBullRegime = source.RequireBullRegime;
        target.AtrStopMultiplier = source.AtrStopMultiplier;
        target.AtrTargetMultiplier = source.AtrTargetMultiplier;
        target.MaxHoldingBars = source.MaxHoldingBars;
        target.TrailingAtr = source.TrailingAtr;
        target.PartialProfitR = source.PartialProfitR;
        target.UseWeightTiers = source.UseWeightTiers;
        target.WeightTiersJson = source.WeightTiersJson;
        target.DefaultAllocationPercent = source.DefaultAllocationPercent;
        target.ExitRulesJson = source.ExitRulesJson;
        target.ExitRulesLogic = source.ExitRulesLogic;
        target.ExitGroupsJson = source.ExitGroupsJson;
        target.ExitGroupsLogic = source.ExitGroupsLogic;
        target.ScalingRulesJson = source.ScalingRulesJson;
        target.TimeFilterJson = source.TimeFilterJson;
        target.CircuitBreakerJson = source.CircuitBreakerJson;
        target.ReentryJson = source.ReentryJson;
        target.PortfolioRulesJson = source.PortfolioRulesJson;
        target.EntryGroupsJson = source.EntryGroupsJson;
        target.EntryGroupsLogic = source.EntryGroupsLogic;
        target.DynamicExitJson = source.DynamicExitJson;
        target.EntryMode = source.EntryMode;
        target.SizingMode = source.SizingMode;
        target.IsActive = source.IsActive;
        target.EnableLiveTrading = source.EnableLiveTrading;
    }
}
