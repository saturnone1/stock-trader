using Microsoft.EntityFrameworkCore;
using StockTrader.Api.Contracts;
using StockTrader.Application.Strategies;
using StockTrader.Data;
using StockTrader.Services.Patterns;

namespace StockTrader.Api;

public static class CustomPatternEndpoints
{
    public static RouteGroupBuilder MapCustomPatternApi(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/custom-patterns").RequireAuthorization();

        group.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
            (await db.CustomPatterns.AsNoTracking()
                .OrderByDescending(p => p.UpdatedAt)
                .ToListAsync(ct)).Select(value => value.ToResponse()).ToArray());

        group.MapGet("/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var pattern = await db.CustomPatterns.FindAsync([id], ct);
            return pattern is null ? Results.NotFound() : Results.Ok(pattern.ToResponse());
        });

        group.MapPost("/", async (CustomPatternWriteRequest request, AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var input = request.ToDefinition();
            var validationErrors = CustomPatternValidator.Validate(input);
            if (validationErrors.Count > 0)
                return Results.BadRequest(new { error = validationErrors[0], errors = validationErrors });

            var normalizedName = input.Name.Trim().ToLower();
            if (await db.CustomPatterns.AnyAsync(p => p.Name.ToLower() == normalizedName, ct))
                return Results.Conflict(new { error = "같은 이름의 전략이 이미 있습니다. 다른 이름을 사용하세요." });

            input.Name = input.Name.Trim();
            input.Id = 0;
            StrategyDocumentVersionPolicy.StampCurrent(input);
            input.CreatedAt = clock.GetUtcNow().UtcDateTime;
            input.UpdatedAt = input.CreatedAt;
            db.CustomPatterns.Add(input);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/custom-patterns/{input.Id}", input.ToResponse());
        });

        group.MapPut("/{id:int}", async (int id, CustomPatternWriteRequest request, AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var input = request.ToDefinition();
            var validationErrors = CustomPatternValidator.Validate(input);
            if (validationErrors.Count > 0)
                return Results.BadRequest(new { error = validationErrors[0], errors = validationErrors });

            var existing = await db.CustomPatterns.FindAsync([id], ct);
            if (existing is null) return Results.NotFound(new { error = "수정할 전략을 찾을 수 없습니다." });

            var normalizedName = input.Name.Trim().ToLower();
            if (await db.CustomPatterns.AnyAsync(p => p.Id != id && p.Name.ToLower() == normalizedName, ct))
                return Results.Conflict(new { error = "같은 이름의 전략이 이미 있습니다. 다른 이름을 사용하세요." });

            request.ApplyTo(existing);
            StrategyDocumentVersionPolicy.StampCurrent(existing);
            existing.Name = input.Name.Trim();
            existing.UpdatedAt = clock.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(ct);
            return Results.Ok(existing.ToResponse());
        });

        group.MapDelete("/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var pattern = await db.CustomPatterns.FindAsync([id], ct);
            if (pattern is null) return Results.NotFound();
            db.CustomPatterns.Remove(pattern);
            await db.SaveChangesAsync(ct);
            return Results.Ok();
        });

        group.MapPost("/{id:int}/apply-backtest", async (int id, BacktestApplyRequest req, AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var pattern = await db.CustomPatterns.FindAsync([id], ct);
            if (pattern is null) return Results.NotFound();
            StrategyDocumentVersionPolicy.StampCurrent(pattern);
            pattern.UpdatedAt = clock.GetUtcNow().UtcDateTime;
            // req에서 최적 파라미터 반영 (AtrStop, AtrTarget 등)
            if (req.AtrStopMultiplier.HasValue) pattern.AtrStopMultiplier = req.AtrStopMultiplier.Value;
            if (req.AtrTargetMultiplier.HasValue) pattern.AtrTargetMultiplier = req.AtrTargetMultiplier.Value;
            if (req.MaxHoldingBars.HasValue) pattern.MaxHoldingBars = req.MaxHoldingBars.Value;
            if (req.TrailingAtr.HasValue) pattern.TrailingAtr = req.TrailingAtr.Value;
            if (req.PartialProfitR.HasValue) pattern.PartialProfitR = req.PartialProfitR.Value;
            await db.SaveChangesAsync(ct);
            return Results.Ok(pattern.ToResponse());
        });

        return api;
    }
}
