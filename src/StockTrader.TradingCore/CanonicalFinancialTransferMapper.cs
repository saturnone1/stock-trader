using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using StockTrader.ServiceContracts.TradingCore;

namespace StockTrader.TradingCore.Execution;

public static class CanonicalFinancialTransferMapper
{
    private static readonly JsonSerializerOptions ExactJson = CreateOptions();

    public static CanonicalFinancialTransferV2 Create(
        string transferId,
        string transitionId,
        string direction,
        TradingAuthorityMode sourceMode,
        long reservedTargetGeneration,
        FinancialTransferCompatibility compatibility,
        TradingAccountConfigurationSet accountConfiguration,
        TradingStateSnapshot snapshot,
        IReadOnlyList<FinancialExecutionIdentity> executionIdentities,
        IReadOnlyList<FinancialBrokerEvidence> brokerEvidence,
        FinancialActivityContinuity activity,
        string equityBasis)
    {
        if (TradingCoreCompatibilityPolicy.Error(snapshot) is { } snapshotError)
            throw new ArgumentException(snapshotError, nameof(snapshot));
        if (TradingCoreCompatibilityPolicy.Error(accountConfiguration) is { } accountError)
            throw new ArgumentException(accountError, nameof(accountConfiguration));
        var accounts = accountConfiguration.Accounts
            .OrderBy(value => value.AccountId, StringComparer.Ordinal)
            .Select(value => new FinancialAccountReference(
                value.AccountId, value.BrokerCode, value.Environment,
                value.IsEnabled, value.IsActive, accountConfiguration.Generation,
                accountConfiguration.ConfigurationHash))
            .ToArray();
        var recommendations = snapshot.Recommendations
            .OrderBy(value => value.RecommendationId, StringComparer.Ordinal)
            .Select(Recommendation).ToArray();
        var positions = snapshot.Positions
            .OrderBy(value => value.PositionId, StringComparer.Ordinal)
            .Select(Position).ToArray();
        var trades = snapshot.Trades
            .OrderBy(value => value.TradeId, StringComparer.Ordinal)
            .Select(Trade).ToArray();
        executionIdentities = executionIdentities
            .OrderBy(value => value.CommandId, StringComparer.Ordinal).ToArray();
        brokerEvidence = brokerEvidence
            .OrderBy(value => value.AccountId, StringComparer.Ordinal)
            .ThenBy(value => value.ClientOrderId, StringComparer.Ordinal)
            .ThenBy(value => value.BrokerOrderId, StringComparer.Ordinal)
            .ToArray();
        var risk = Risk(
            DateOnly.FromDateTime(snapshot.CapturedAtUtc),
            equityBasis,
            snapshot.Risk,
            accountConfiguration.Generation);
        var sections = new[]
        {
            Section("accounts", accounts),
            Section("recommendations", recommendations),
            Section("positions", positions),
            Section("realizedTrades", trades),
            Section("executionIdentities", executionIdentities),
            Section("brokerEvidence", brokerEvidence),
            new FinancialTransferSection("risk", 1, risk.StateHash),
            new FinancialTransferSection("activity", 1, activity.ContinuityHash),
        };
        var transfer = new CanonicalFinancialTransferV2(
            CanonicalFinancialTransferVersions.Current,
            transferId,
            string.Empty,
            transitionId,
            direction,
            AuthorityOwners.ForMode(sourceMode),
            sourceMode,
            snapshot.SourceGeneration,
            reservedTargetGeneration,
            Utc(snapshot.CapturedAtUtc),
            compatibility,
            accounts,
            recommendations,
            positions,
            trades,
            executionIdentities,
            brokerEvidence,
            risk,
            activity,
            sections);
        transfer = transfer with
        {
            TransferHash = CanonicalFinancialTransferIdentity.Transfer(transfer)
        };
        if (CanonicalFinancialTransferPolicy.Error(transfer) is { } transferError)
            throw new InvalidDataException(transferError);
        return transfer;
    }

    public static FinancialActivityContinuity Activity(
        IReadOnlyDictionary<string, long> aggregateVersions,
        long journalHighWatermark,
        IReadOnlyList<FinancialConsumerCursor> consumerCursors)
    {
        var value = new FinancialActivityContinuity(
            new SortedDictionary<string, long>(
                aggregateVersions.ToDictionary(item => item.Key, item => item.Value),
                StringComparer.Ordinal),
            journalHighWatermark,
            consumerCursors.OrderBy(item => item.ConsumerId, StringComparer.Ordinal).ToArray(),
            string.Empty);
        return value with
        {
            ContinuityHash = CanonicalFinancialTransferIdentity.Activity(value)
        };
    }

    public static CanonicalFinancialRow Recommendation(TradingRecommendationProjection value) =>
        Row(value.RecommendationId, value.SourceSignalId, value);

    public static CanonicalFinancialRow Position(TradingPositionProjection value) =>
        Row(value.PositionId, value.SourceSignalId, value);

    public static CanonicalFinancialRow Trade(TradingTradeProjection value) =>
        Row(value.TradeId, value.SourceSignalId, value);

    public static TradingRecommendationProjection ReadRecommendation(CanonicalFinancialRow row) =>
        Read<TradingRecommendationProjection>(row);

    public static TradingPositionProjection ReadPosition(CanonicalFinancialRow row) =>
        Read<TradingPositionProjection>(row);

    public static TradingTradeProjection ReadTrade(CanonicalFinancialRow row) =>
        Read<TradingTradeProjection>(row);

    public static TradingRiskProjection ReadRisk(FinancialRiskState risk) => new(
        Parse(risk.DailyPnl),
        Parse(risk.DailyPnlPercent),
        risk.OpenPositionCount,
        risk.IsTradingHalted,
        Utc(risk.ObservedAtUtc));

    public static IReadOnlyList<TradingAccountProjection> ReadAccounts(
        IReadOnlyList<FinancialAccountReference> accounts) => accounts
        .Select(value => new TradingAccountProjection(
            value.AccountId, value.BrokerCode, value.Environment,
            value.IsEnabled, value.IsActive, value.ConfigurationGeneration))
        .ToArray();

    public static TradingStateSnapshot Snapshot(CanonicalFinancialTransferV2 transfer)
    {
        var snapshot = new TradingStateSnapshot(
            TradingCoreContractVersions.Current,
            string.Empty,
            transfer.SourceGeneration,
            Utc(transfer.CapturedAtUtc),
            ReadAccounts(transfer.Accounts),
            transfer.Recommendations.Select(ReadRecommendation).ToArray(),
            transfer.Positions.Select(ReadPosition).ToArray(),
            transfer.RealizedTrades.Select(ReadTrade).ToArray(),
            ReadRisk(transfer.Risk));
        return snapshot with { SnapshotId = TradingCoreIdentity.Snapshot(snapshot) };
    }

    public static FinancialRiskState Risk(
        DateOnly tradingDay,
        string equityBasis,
        TradingRiskProjection risk,
        long accountGeneration)
    {
        var value = new FinancialRiskState(
            tradingDay,
            equityBasis,
            Exact(risk.DailyPnL),
            Exact(risk.DailyPnLPercent),
            risk.OpenPositionCount,
            risk.IsTradingHalted,
            Utc(risk.ObservedAtUtc),
            accountGeneration,
            string.Empty);
        return value with { StateHash = CanonicalFinancialTransferIdentity.Risk(value) };
    }

    private static CanonicalFinancialRow Row<T>(string identity, string? sourceIdentity, T value)
    {
        var payload = JsonSerializer.Serialize(value, ExactJson);
        return new CanonicalFinancialRow(
            identity,
            sourceIdentity,
            CanonicalFinancialTransferIdentity.Payload(payload),
            payload);
    }

    private static FinancialTransferSection Section<T>(string name, IReadOnlyList<T> rows) =>
        new(name, rows.Count, CanonicalFinancialTransferIdentity.Rows(rows));

    private static T Read<T>(CanonicalFinancialRow row)
    {
        if (row.PayloadHash != CanonicalFinancialTransferIdentity.Payload(row.PayloadJson))
            throw new InvalidDataException("canonical-financial-row-hash-mismatch");
        return JsonSerializer.Deserialize<T>(row.PayloadJson, ExactJson)
               ?? throw new InvalidDataException("empty-canonical-financial-row");
    }

    private static string Exact(decimal value) => value.ToString("G29", CultureInfo.InvariantCulture);

    private static decimal Parse(string value) =>
        decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);

    private static DateTime Utc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new DecimalStringConverter());
        return options;
    }

    private sealed class DecimalStringConverter : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
                throw new JsonException("Financial decimals must be invariant strings.");
            return Parse(reader.GetString()!);
        }

        public override void Write(Utf8JsonWriter writer, decimal value,
            JsonSerializerOptions options) => writer.WriteStringValue(Exact(value));
    }
}
