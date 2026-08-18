using Microsoft.EntityFrameworkCore;
using StockTrader.Application.Execution;
using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Data.Repositories;

/// <summary>SQLite에서 포지션 실행의 비교 후 갱신과 체결 원장 기록을 원자적으로 수행합니다.</summary>
public sealed class LivePositionExecutionStore(IDbContextFactory<AppDbContext> dbFactory)
    : ILivePositionExecutionStore
{
    public async Task<bool> TryClaimAsync(
        PositionExecutionClaim claim,
        CancellationToken ct = default)
    {
        if (!IsValid(claim))
            return false;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var updated = await db.Positions
            .Where(position => position.Id == claim.PositionId
                && position.ClosedAt == null
                && position.ExecutionRequestedAt == null
                && position.Quantity == claim.ExpectedPositionQuantity)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(position => position.ExecutionRequestedAt, claim.RequestedAt)
                .SetProperty(position => position.ExecutionRequestReason, claim.Reason)
                .SetProperty(position => position.ExecutionRequestQuantity, claim.Quantity)
                .SetProperty(position => position.ExecutionRequestMarksPartialProfit, claim.MarksPartialProfit)
                .SetProperty(position => position.ExecutionRequestKind, claim.Kind)
                .SetProperty(position => position.ExecutionRequestRuleIndex, claim.ScalingRuleIndex)
                .SetProperty(position => position.ExecutionOrderId, (string?)null), ct);
        return updated == 1;
    }

    public async Task<bool> SetOrderEvidenceAsync(
        long positionId,
        DateTime requestedAt,
        string? orderId,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var updated = await db.Positions
            .Where(position => position.Id == positionId
                && position.ClosedAt == null
                && position.ExecutionRequestedAt == requestedAt)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(position => position.ExecutionOrderId, orderId), ct);
        return updated == 1;
    }

    public async Task<bool> ReleaseClaimAsync(
        long positionId,
        DateTime requestedAt,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var updated = await db.Positions
            .Where(position => position.Id == positionId
                && position.ClosedAt == null
                && position.ExecutionRequestedAt == requestedAt)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(position => position.ExecutionRequestedAt, (DateTime?)null)
                .SetProperty(position => position.ExecutionRequestReason, (string?)null)
                .SetProperty(position => position.ExecutionRequestQuantity, (int?)null)
                .SetProperty(position => position.ExecutionRequestMarksPartialProfit, false)
                .SetProperty(position => position.ExecutionRequestKind, (PositionExecutionKind?)null)
                .SetProperty(position => position.ExecutionRequestRuleIndex, (int?)null)
                .SetProperty(position => position.ExecutionOrderId, (string?)null), ct);
        return updated == 1;
    }

    public async Task<bool> CommitFillAsync(
        PositionExecutionFill fill,
        PositionExecutionTrade? trade,
        CancellationToken ct = default)
    {
        if (!IsValid(fill, trade))
            return false;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var query = db.Positions
                .Where(stored => stored.Id == fill.PositionId
                    && stored.ClosedAt == null
                    && stored.ExecutionRequestedAt == fill.RequestedAt
                    && stored.ExecutionRequestQuantity == fill.FilledQuantity
                    && stored.ExecutionRequestKind == fill.Kind
                    && stored.ExecutionRequestRuleIndex == fill.ScalingRuleIndex
                    && stored.ExecutionRequestMarksPartialProfit == fill.MarksPartialProfit
                    && stored.Quantity == fill.ExpectedPositionQuantity);

            var updated = fill.Kind switch
            {
                PositionExecutionKind.FullExit => await ApplyFullExitAsync(query, fill, ct),
                PositionExecutionKind.ScaleIn => await ApplyScaleInAsync(query, fill, ct),
                _ => await ApplyPartialExitAsync(query, fill, ct),
            };
            if (updated != 1)
            {
                await transaction.RollbackAsync(ct);
                return false;
            }

            if (fill.Kind is PositionExecutionKind.ScaleIn or PositionExecutionKind.ScaleOut)
                await IncrementScalingCounterAsync(db, fill, ct);
            if (trade is not null)
                db.TradeRecords.Add(ToEntity(trade));
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return true;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static bool IsValid(PositionExecutionClaim claim) =>
        claim.PositionId > 0
        && claim.ExpectedPositionQuantity > 0
        && claim.Quantity > 0
        && Enum.IsDefined(claim.Kind)
        && (claim.Kind != PositionExecutionKind.FullExit
            || claim.Quantity == claim.ExpectedPositionQuantity)
        && (claim.Kind is not (PositionExecutionKind.PartialProfit or PositionExecutionKind.ScaleOut)
            || claim.Quantity < claim.ExpectedPositionQuantity)
        && (claim.Kind is PositionExecutionKind.ScaleIn or PositionExecutionKind.ScaleOut)
            == (claim.ScalingRuleIndex is >= 0)
        && (!claim.MarksPartialProfit || claim.Kind == PositionExecutionKind.PartialProfit)
        && !string.IsNullOrWhiteSpace(claim.Reason);

    private static bool IsValid(PositionExecutionFill fill, PositionExecutionTrade? trade) =>
        fill.PositionId > 0
        && fill.ExpectedPositionQuantity > 0
        && fill.FilledQuantity > 0
        && fill.FillPrice > 0
        && Enum.IsDefined(fill.Kind)
        && (fill.Kind != PositionExecutionKind.FullExit
            || fill.FilledQuantity == fill.ExpectedPositionQuantity)
        && (fill.Kind is not (PositionExecutionKind.PartialProfit or PositionExecutionKind.ScaleOut)
            || fill.FilledQuantity < fill.ExpectedPositionQuantity)
        && (fill.Kind is PositionExecutionKind.ScaleIn or PositionExecutionKind.ScaleOut)
            == (fill.ScalingRuleIndex is >= 0)
        && (!fill.MarksPartialProfit || fill.Kind == PositionExecutionKind.PartialProfit)
        && (fill.Kind == PositionExecutionKind.ScaleIn) == (trade is null);

    private static Task<int> ApplyFullExitAsync(
        IQueryable<Position> query,
        PositionExecutionFill fill,
        CancellationToken ct) => query.ExecuteUpdateAsync(setters => setters
            .SetProperty(stored => stored.ClosedAt, fill.FilledAt)
            .SetProperty(stored => stored.ExitPrice, fill.FillPrice)
            .SetProperty(stored => stored.ExecutionOrderId, fill.OrderId), ct);

    private static Task<int> ApplyScaleInAsync(
        IQueryable<Position> query,
        PositionExecutionFill fill,
        CancellationToken ct)
    {
        var nextQuantity = fill.ExpectedPositionQuantity + fill.FilledQuantity;
        return query.ExecuteUpdateAsync(setters => setters
            .SetProperty(stored => stored.EntryPrice,
                stored => (stored.EntryPrice * fill.ExpectedPositionQuantity
                    + fill.FillPrice * fill.FilledQuantity) / nextQuantity)
            .SetProperty(stored => stored.Quantity, nextQuantity)
            .SetProperty(stored => stored.CurrentPrice, fill.FillPrice)
            .SetProperty(stored => stored.ExecutionRequestedAt, (DateTime?)null)
            .SetProperty(stored => stored.ExecutionRequestReason, (string?)null)
            .SetProperty(stored => stored.ExecutionRequestQuantity, (int?)null)
            .SetProperty(stored => stored.ExecutionRequestMarksPartialProfit, false)
            .SetProperty(stored => stored.ExecutionRequestKind, (PositionExecutionKind?)null)
            .SetProperty(stored => stored.ExecutionRequestRuleIndex, (int?)null)
            .SetProperty(stored => stored.ExecutionOrderId, (string?)null), ct);
    }

    private static Task<int> ApplyPartialExitAsync(
        IQueryable<Position> query,
        PositionExecutionFill fill,
        CancellationToken ct) => query.ExecuteUpdateAsync(setters => setters
            .SetProperty(stored => stored.Quantity,
                fill.ExpectedPositionQuantity - fill.FilledQuantity)
            .SetProperty(stored => stored.CurrentPrice, fill.FillPrice)
            .SetProperty(stored => stored.PartialProfitTaken,
                stored => stored.PartialProfitTaken || fill.MarksPartialProfit)
            .SetProperty(stored => stored.StopLossPrice,
                stored => fill.MarksPartialProfit && stored.StopLossPrice < stored.EntryPrice
                    ? stored.EntryPrice
                    : stored.StopLossPrice)
            .SetProperty(stored => stored.BreakevenApplied,
                stored => stored.BreakevenApplied || fill.MarksPartialProfit)
            .SetProperty(stored => stored.ExecutionRequestedAt, (DateTime?)null)
            .SetProperty(stored => stored.ExecutionRequestReason, (string?)null)
            .SetProperty(stored => stored.ExecutionRequestQuantity, (int?)null)
            .SetProperty(stored => stored.ExecutionRequestMarksPartialProfit, false)
            .SetProperty(stored => stored.ExecutionRequestKind, (PositionExecutionKind?)null)
            .SetProperty(stored => stored.ExecutionRequestRuleIndex, (int?)null)
            .SetProperty(stored => stored.ExecutionOrderId, (string?)null), ct);

    private static async Task IncrementScalingCounterAsync(
        AppDbContext db,
        PositionExecutionFill fill,
        CancellationToken ct)
    {
        var ruleIndex = fill.ScalingRuleIndex!.Value;
        var counter = await db.PositionScalingExecutions.SingleOrDefaultAsync(item =>
            item.PositionId == fill.PositionId && item.RuleIndex == ruleIndex, ct);
        if (counter is null)
        {
            db.PositionScalingExecutions.Add(new PositionScalingExecution
            {
                PositionId = fill.PositionId,
                RuleIndex = ruleIndex,
                ExecutionCount = 1,
            });
        }
        else
        {
            counter.ExecutionCount++;
        }
    }

    private static TradeRecord ToEntity(PositionExecutionTrade trade) => new()
    {
        SourceSignalId = trade.SourceSignalId,
        Symbol = trade.Symbol,
        PatternType = trade.PatternType,
        CustomPatternName = trade.CustomPatternName,
        EntryPrice = trade.EntryPrice,
        ExitPrice = trade.ExitPrice,
        Quantity = trade.Quantity,
        EntryTime = trade.EntryTime,
        ExitTime = trade.ExitTime,
        PnL = trade.PnL,
        PnLPercent = trade.PnLPercent,
        ExitReason = trade.ExitReason,
    };
}
