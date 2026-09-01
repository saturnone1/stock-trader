using StockTrader.Application.TradingCore;
using StockTrader.Models;
using StockTrader.ServiceContracts;
using StockTrader.ServiceContracts.TradingCore;
using StockTrader.Services.Account;
using StockTrader.TradingCore.Execution;

namespace StockTrader.Services.TradingCore;

internal sealed class EdgeCanonicalFinancialTransferService(
    ITradingCoreProjectionSource projections,
    ITradingCoreAccountConfigurationSource accounts,
    IEdgeFinancialAuthorityControl authority,
    IAccountManager accountManager)
    : IEdgeCanonicalFinancialTransferService
{
    public async Task<CanonicalFinancialTransferV2> ExportAsync(
        CanonicalFinancialExportRequest request,
        CancellationToken ct = default)
    {
        if (CanonicalFinancialTransferPolicy.Error(request) is { } requestError)
            throw new ArgumentException(requestError, nameof(request));
        if (request.Direction != AuthorityTransitionDirections.Cutover
            || request.SourceMode != TradingAuthorityMode.Shadow)
            throw new ArgumentException("invalid-financial-export-request", nameof(request));

        var drain = await authority.ReadDrainInventoryAsync(request.TransitionId, ct);
        if (drain.UnresolvedIntentCount != 0
            || drain.UnresolvedBrokerEffectCount != 0
            || drain.UnprocessedBrokerFillCount != 0
            || drain.EnabledConsumerLag != 0)
            throw new InvalidOperationException("unresolved-financial-intent");

        var configuration = await accounts.CaptureAsync(ct);
        var brokerBefore = await CaptureBrokerAsync(configuration, ct);
        var captured = await projections.CaptureAsync(ct);
        captured = captured with
        {
            SourceGeneration = request.SourceGeneration,
            SnapshotId = string.Empty,
        };
        captured = captured with { SnapshotId = TradingCoreIdentity.Snapshot(captured) };
        var brokerAfter = await CaptureBrokerAsync(configuration, ct);
        if (brokerBefore.Hash != brokerAfter.Hash)
            throw new InvalidOperationException("broker-snapshot-changed-during-export");

        var identities = BuildExecutionIdentities(captured, brokerAfter);
        var brokerEvidence = BuildBrokerEvidence(captured, brokerAfter);

        // Edge has no independent activity consumer in Stage 5. Its durable order/fill
        // identities are embedded in the canonical recommendation/position rows; an empty
        // explicit list means no additional terminal identity ledger exists at this source.
        var activity = CanonicalFinancialTransferMapper.Activity(
            new SortedDictionary<string, long>(StringComparer.Ordinal),
            drain.ActivityJournalCount,
            Array.Empty<FinancialConsumerCursor>());
        return CanonicalFinancialTransferMapper.Create(
            request.TransferId,
            request.TransitionId,
            request.Direction,
            request.SourceMode,
            request.ReservedTargetGeneration,
            request.Compatibility,
            configuration,
            captured,
            identities,
            brokerEvidence,
            activity,
            request.EquityBasis);
    }

    private async Task<EdgeBrokerSnapshot> CaptureBrokerAsync(
        TradingAccountConfigurationSet configuration,
        CancellationToken ct)
    {
        var positions = new List<EdgeBrokerPosition>();
        var orders = new List<EdgeBrokerOrder>();
        var observedAtUtc = DateTime.UtcNow;
        foreach (var account in configuration.Accounts
                     .Where(value => value.IsEnabled)
                     .OrderBy(value => value.AccountId, StringComparer.Ordinal))
        {
            if (!int.TryParse(account.AccountId, out var accountId))
                throw new InvalidOperationException("edge-account-identity-incompatible");
            var context = await accountManager.GetBrokerContextForReconciliationAsync(accountId, ct)
                          ?? throw new InvalidOperationException("edge-broker-context-unavailable");
            var brokerPositions = await context.Broker.GetPositionsAsync(ct);
            positions.AddRange(brokerPositions.Select(value =>
                new EdgeBrokerPosition(account.AccountId, value)));
            var brokerOrders = await context.Broker.GetOrderHistoryAsync(
                observedAtUtc.AddYears(-10), observedAtUtc, ct);
            orders.AddRange(brokerOrders.Select(value => new EdgeBrokerOrder(account.AccountId, value)));
        }
        positions.Sort((left, right) => string.CompareOrdinal(
            $"{left.AccountId}|{left.Position.Symbol}", $"{right.AccountId}|{right.Position.Symbol}"));
        orders.Sort((left, right) => string.CompareOrdinal(
            $"{left.AccountId}|{left.Order.OrderId}", $"{right.AccountId}|{right.Order.OrderId}"));
        var canonical = new
        {
            Positions = positions.Select(value => new
            {
                value.AccountId,
                Symbol = value.Position.Symbol.ToUpperInvariant(),
                value.Position.Quantity,
                value.Position.AverageEntryPrice,
                value.Position.CurrentPrice,
            }),
            Orders = orders.Select(value => new
            {
                value.AccountId,
                value.Order.OrderId,
                Symbol = value.Order.Symbol.ToUpperInvariant(),
                Direction = value.Order.Direction.ToString(),
                value.Order.Quantity,
                value.Order.FilledQuantity,
                value.Order.OrderPrice,
                value.Order.AverageFillPrice,
                Status = value.Order.Status.ToString(),
                Type = value.Order.OrderType.ToString(),
                SubmittedAtUtc = Utc(value.Order.SubmittedAt),
                FilledAtUtc = value.Order.FilledAt is { } filled ? Utc(filled) : (DateTime?)null,
            }),
        };
        return new EdgeBrokerSnapshot(positions, orders, CanonicalJsonHash.Compute(canonical));
    }

    private static IReadOnlyList<FinancialExecutionIdentity> BuildExecutionIdentities(
        TradingStateSnapshot snapshot,
        EdgeBrokerSnapshot broker)
    {
        var values = new List<FinancialExecutionIdentity>();
        foreach (var recommendation in snapshot.Recommendations
                     .Where(value => !string.IsNullOrWhiteSpace(value.EntryOrderId)))
        {
            var match = broker.Orders.FirstOrDefault(value =>
                value.Order.OrderId == recommendation.EntryOrderId);
            var commandId = $"edge-entry-{recommendation.RecommendationId}";
            values.Add(new FinancialExecutionIdentity(
                recommendation.SourceSignalId,
                commandId,
                EdgeClientOrderId(commandId),
                recommendation.EntryOrderId!,
                match?.Order.Status.ToString() ?? "LegacyTerminalUnknown",
                CanonicalJsonHash.Compute(recommendation),
                match is null ? snapshot.CapturedAtUtc : Utc(match.Order.FilledAt ?? match.Order.SubmittedAt)));
        }
        foreach (var position in snapshot.Positions
                     .Where(value => !string.IsNullOrWhiteSpace(value.ExecutionOrderId)))
        {
            var match = broker.Orders.FirstOrDefault(value =>
                value.Order.OrderId == position.ExecutionOrderId);
            var commandId = $"edge-position-{position.PositionId}-{position.ExecutionOrderId}";
            values.Add(new FinancialExecutionIdentity(
                position.PositionId,
                commandId,
                EdgeClientOrderId(commandId),
                position.ExecutionOrderId!,
                match?.Order.Status.ToString() ?? "LegacyTerminalUnknown",
                CanonicalJsonHash.Compute(position),
                match is null ? snapshot.CapturedAtUtc : Utc(match.Order.FilledAt ?? match.Order.SubmittedAt)));
        }
        return values.OrderBy(value => value.CommandId, StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<FinancialBrokerEvidence> BuildBrokerEvidence(
        TradingStateSnapshot snapshot,
        EdgeBrokerSnapshot broker)
    {
        var evidence = new List<FinancialBrokerEvidence>();
        var canonical = snapshot.Positions
            .Where(value => value.ClosedAtUtc is null)
            .GroupBy(value => $"{value.AccountId}|{value.Symbol.ToUpperInvariant()}")
            .ToDictionary(group => group.Key, group => group.Sum(value => value.Quantity));
        var actual = broker.Positions
            .GroupBy(value => $"{value.AccountId}|{value.Position.Symbol.ToUpperInvariant()}")
            .ToDictionary(group => group.Key, group => group.Sum(value => value.Position.Quantity));
        foreach (var key in canonical.Keys.Union(actual.Keys).Order(StringComparer.Ordinal))
        {
            var separator = key.IndexOf('|');
            var accountId = key[..separator];
            var symbol = key[(separator + 1)..];
            var canonicalQuantity = canonical.GetValueOrDefault(key);
            var brokerQuantity = actual.GetValueOrDefault(key);
            var relatedOrder = broker.Orders
                .Where(value => value.AccountId == accountId
                                && value.Order.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(value => value.Order.FilledAt ?? value.Order.SubmittedAt)
                .FirstOrDefault();
            var brokerOrderId = relatedOrder?.Order.OrderId ?? $"legacy-position-{accountId}-{symbol}";
            var commandId = $"edge-evidence-{accountId}-{symbol}-{brokerOrderId}";
            var clientOrderId = EdgeClientOrderId(commandId);
            var side = relatedOrder?.Order.Direction.ToString() ?? "LegacyImported";
            var requested = relatedOrder?.Order.Quantity ?? brokerQuantity;
            var filled = relatedOrder?.Order.FilledQuantity ?? brokerQuantity;
            var status = relatedOrder?.Order.Status.ToString() ?? "LegacyImportedPosition";
            var observed = relatedOrder is null
                ? snapshot.CapturedAtUtc
                : Utc(relatedOrder.Order.FilledAt ?? relatedOrder.Order.SubmittedAt);
            var candidate = new FinancialBrokerEvidence(accountId, symbol,
                canonicalQuantity, brokerQuantity, clientOrderId, brokerOrderId,
                side, requested, filled, status, observed, string.Empty);
            evidence.Add(candidate with
            {
                EvidenceHash = CanonicalFinancialTransferIdentity.BrokerEvidence(candidate)
            });
        }
        return evidence.OrderBy(value => value.AccountId, StringComparer.Ordinal)
            .ThenBy(value => value.ClientOrderId, StringComparer.Ordinal)
            .ThenBy(value => value.BrokerOrderId, StringComparer.Ordinal)
            .ToArray();
    }

    private static string EdgeClientOrderId(string identity) =>
        "edge-" + CanonicalJsonHash.Compute(identity)[..32].ToLowerInvariant();

    private static DateTime Utc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    private sealed record EdgeBrokerPosition(string AccountId,
        StockTrader.Application.Accounts.BrokerPositionSnapshot Position);
    private sealed record EdgeBrokerOrder(string AccountId, BrokerOrder Order);
    private sealed record EdgeBrokerSnapshot(
        IReadOnlyList<EdgeBrokerPosition> Positions,
        IReadOnlyList<EdgeBrokerOrder> Orders,
        string Hash);
}
