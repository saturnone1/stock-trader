using System.Text.Json;
using FluentAssertions;
using StockTrader.Application.Strategies;
using StockTrader.Domain.Strategies;
using StockTrader.Models;

namespace StockTrader.Tests;

public class CustomPatternManagementServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 18, 3, 4, 5, TimeSpan.Zero);

    [Fact]
    public async Task CreateOwnsIdentityClockVersionValidationAndCaseInsensitiveUniqueness()
    {
        var store = new MemoryStore();
        var service = new CustomPatternManagementService(store, new FixedClock(Now));
        var input = ValidPattern("  반등 전략  ");
        input.DocumentVersion = StrategyDocumentVersions.LegacyUnversioned;
        input.Id = 999;
        input.CreatedAt = DateTime.UnixEpoch;
        input.UpdatedAt = DateTime.UnixEpoch;

        var created = await service.CreateAsync(input);
        var duplicate = await service.CreateAsync(ValidPattern("반등 전략".ToUpperInvariant()));

        created.Kind.Should().Be(CustomPatternOperationKind.Success);
        created.Definition!.Id.Should().Be(1);
        created.Definition.Name.Should().Be("반등 전략");
        created.Definition.DocumentVersion.Should().Be(StrategyDocumentVersions.Current);
        created.Definition.CreatedAt.Should().Be(Now.UtcDateTime);
        created.Definition.UpdatedAt.Should().Be(Now.UtcDateTime);
        duplicate.Kind.Should().Be(CustomPatternOperationKind.Conflict);
        store.AddCount.Should().Be(1);
    }

    [Fact]
    public async Task UpdatePreservesServerOwnedIdentityAndCreationTime()
    {
        var createdAt = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var existing = ValidPattern("기존 전략");
        existing.Id = 7;
        existing.CreatedAt = createdAt;
        var store = new MemoryStore(existing);
        var service = new CustomPatternManagementService(store, new FixedClock(Now));
        var replacement = ValidPattern("수정 전략");
        replacement.Id = 1234;
        replacement.CreatedAt = DateTime.UnixEpoch;

        var result = await service.UpdateAsync(7, replacement);

        result.Kind.Should().Be(CustomPatternOperationKind.Success);
        result.Definition!.Id.Should().Be(7);
        result.Definition.CreatedAt.Should().Be(createdAt);
        result.Definition.UpdatedAt.Should().Be(Now.UtcDateTime);
        store.UpdateCount.Should().Be(1);
        store.Stored(7)!.Name.Should().Be("수정 전략");
    }

    [Fact]
    public async Task ApplyBacktestRejectsInvalidParametersBeforePersistence()
    {
        var existing = ValidPattern("안전 전략");
        existing.Id = 3;
        var store = new MemoryStore(existing);
        var service = new CustomPatternManagementService(store, new FixedClock(Now));

        var invalid = await service.ApplyBacktestAsync(3, new BacktestStrategyParameterUpdate(
            AtrStopMultiplier: -1m,
            AtrTargetMultiplier: null,
            MaxHoldingBars: null,
            TrailingAtr: null,
            PartialProfitR: null));

        invalid.Kind.Should().Be(CustomPatternOperationKind.Invalid);
        invalid.Errors.Should().Contain(error => error.Contains("ATR 손절 배수"));
        store.UpdateCount.Should().Be(0);
        store.Stored(3)!.AtrStopMultiplier.Should().Be(StrategyDocumentDefaults.AtrStopMultiplier);

        var valid = await service.ApplyBacktestAsync(3, new BacktestStrategyParameterUpdate(
            AtrStopMultiplier: 1.25m,
            AtrTargetMultiplier: 4m,
            MaxHoldingBars: 22,
            TrailingAtr: 0.75m,
            PartialProfitR: 1.5m));

        valid.Kind.Should().Be(CustomPatternOperationKind.Success);
        store.UpdateCount.Should().Be(1);
        store.Stored(3)!.AtrStopMultiplier.Should().Be(1.25m);
        store.Stored(3)!.UpdatedAt.Should().Be(Now.UtcDateTime);
    }

    private static CustomPatternDefinition ValidPattern(string name) => new()
    {
        Name = name,
        EntryGroupsJson = JsonSerializer.Serialize(new[]
        {
            new ConditionGroup
            {
                Rules =
                [
                    new EntryRule
                    {
                        Indicator = "RSI",
                        Operator = "<=",
                        Value = 30m,
                        Params = new() { ["period"] = 14m }
                    }
                ]
            }
        })
    };

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class MemoryStore : ICustomPatternStore
    {
        private readonly Dictionary<int, CustomPatternDefinition> _items = new();
        private int _nextId = 1;

        public MemoryStore(params CustomPatternDefinition[] definitions)
        {
            foreach (var definition in definitions)
            {
                _items[definition.Id] = Clone(definition);
                _nextId = Math.Max(_nextId, definition.Id + 1);
            }
        }

        public int AddCount { get; private set; }
        public int UpdateCount { get; private set; }
        public CustomPatternDefinition? Stored(int id) =>
            _items.TryGetValue(id, out var value) ? Clone(value) : null;

        public Task<IReadOnlyList<CustomPatternDefinition>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CustomPatternDefinition>>(_items.Values.Select(Clone).ToArray());

        public Task<CustomPatternDefinition?> FindAsync(int id, CancellationToken ct = default) =>
            Task.FromResult(Stored(id));

        public Task<CustomPatternDefinition?> FindByNameAsync(string name, CancellationToken ct = default) =>
            Task.FromResult(_items.Values
                .FirstOrDefault(value => value.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } value
                    ? Clone(value)
                    : null);

        public Task<bool> NameExistsAsync(
            string normalizedName,
            int? excludingId = null,
            CancellationToken ct = default) => Task.FromResult(_items.Values.Any(value =>
                value.Id != excludingId && value.Name.ToLowerInvariant() == normalizedName));

        public Task AddAsync(CustomPatternDefinition definition, CancellationToken ct = default)
        {
            definition.Id = _nextId++;
            _items[definition.Id] = Clone(definition);
            AddCount++;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(CustomPatternDefinition definition, CancellationToken ct = default)
        {
            _items[definition.Id] = Clone(definition);
            UpdateCount++;
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(int id, CancellationToken ct = default) =>
            Task.FromResult(_items.Remove(id));

        private static CustomPatternDefinition Clone(CustomPatternDefinition value) =>
            JsonSerializer.Deserialize<CustomPatternDefinition>(JsonSerializer.Serialize(value))!;
    }
}
