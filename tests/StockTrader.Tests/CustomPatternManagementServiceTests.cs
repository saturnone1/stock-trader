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
        input.StoredStrategyId = 999;

        var created = await service.CreateAsync(input);
        var duplicate = await service.CreateAsync(ValidPattern("반등 전략".ToUpperInvariant()));

        created.Kind.Should().Be(CustomPatternOperationKind.Success);
        created.Strategy!.Id.Should().Be(1);
        created.Strategy.Document.StoredStrategyId.Should().Be(1);
        created.Strategy.Document.Name.Should().Be("반등 전략");
        created.Strategy.Document.DocumentVersion.Should().Be(StrategyDocumentVersions.Current);
        created.Strategy.CreatedAt.Should().Be(Now.UtcDateTime);
        created.Strategy.UpdatedAt.Should().Be(Now.UtcDateTime);
        duplicate.Kind.Should().Be(CustomPatternOperationKind.Conflict);
        store.AddCount.Should().Be(1);
    }

    [Fact]
    public async Task CreateTranslatesDatabaseUniquenessRaceToConflict()
    {
        var store = new MemoryStore { NextWriteResult = CustomPatternWriteResult.NameConflict };
        var service = new CustomPatternManagementService(store, new FixedClock(Now));

        var result = await service.CreateAsync(ValidPattern("동시 저장 전략"));

        result.Kind.Should().Be(CustomPatternOperationKind.Conflict);
        result.Error.Should().Contain("같은 이름");
        store.AddCount.Should().Be(1);
    }

    [Fact]
    public async Task UpdatePreservesServerOwnedIdentityAndCreationTime()
    {
        var createdAt = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var existing = new StoredStrategy(7, ValidPattern("기존 전략"), createdAt, createdAt);
        var store = new MemoryStore(existing);
        var service = new CustomPatternManagementService(store, new FixedClock(Now));
        var replacement = ValidPattern("수정 전략");
        replacement.StoredStrategyId = 1234;

        var result = await service.UpdateAsync(7, replacement);

        result.Kind.Should().Be(CustomPatternOperationKind.Success);
        result.Strategy!.Id.Should().Be(7);
        result.Strategy.Document.StoredStrategyId.Should().Be(7);
        result.Strategy.CreatedAt.Should().Be(createdAt);
        result.Strategy.UpdatedAt.Should().Be(Now.UtcDateTime);
        store.UpdateCount.Should().Be(1);
        store.Stored(7)!.Document.Name.Should().Be("수정 전략");
    }

    [Fact]
    public async Task UpdateTranslatesDeleteRaceToNotFound()
    {
        var existing = new StoredStrategy(7, ValidPattern("삭제 경쟁"), Now.UtcDateTime, Now.UtcDateTime);
        var store = new MemoryStore(existing) { NextWriteResult = CustomPatternWriteResult.NotFound };
        var service = new CustomPatternManagementService(store, new FixedClock(Now));

        var result = await service.UpdateAsync(7, ValidPattern("수정 시도"));

        result.Kind.Should().Be(CustomPatternOperationKind.NotFound);
        store.Stored(7)!.Document.Name.Should().Be("삭제 경쟁");
    }

    [Fact]
    public async Task ApplyBacktestRejectsInvalidParametersBeforePersistence()
    {
        var existing = new StoredStrategy(3, ValidPattern("안전 전략"), Now.UtcDateTime, Now.UtcDateTime);
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
        store.Stored(3)!.Document.AtrStopMultiplier.Should().Be(StrategyDocumentDefaults.AtrStopMultiplier);

        var valid = await service.ApplyBacktestAsync(3, new BacktestStrategyParameterUpdate(
            AtrStopMultiplier: 1.25m,
            AtrTargetMultiplier: 4m,
            MaxHoldingBars: 22,
            TrailingAtr: 0.75m,
            PartialProfitR: 1.5m));

        valid.Kind.Should().Be(CustomPatternOperationKind.Success);
        store.UpdateCount.Should().Be(1);
        store.Stored(3)!.Document.AtrStopMultiplier.Should().Be(1.25m);
        store.Stored(3)!.UpdatedAt.Should().Be(Now.UtcDateTime);
    }

    private static StrategyDocument ValidPattern(string name) => new()
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
        private readonly Dictionary<int, StoredStrategy> _items = new();
        private int _nextId = 1;

        public MemoryStore(params StoredStrategy[] definitions)
        {
            foreach (var definition in definitions)
            {
                _items[definition.Id] = Clone(definition);
                _nextId = Math.Max(_nextId, definition.Id + 1);
            }
        }

        public int AddCount { get; private set; }
        public int UpdateCount { get; private set; }
        public CustomPatternWriteResult NextWriteResult { get; set; } = CustomPatternWriteResult.Saved;
        public StoredStrategy? Stored(int id) =>
            _items.TryGetValue(id, out var value) ? Clone(value) : null;

        public Task<IReadOnlyList<StoredStrategy>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<StoredStrategy>>(_items.Values.Select(Clone).ToArray());

        public Task<StoredStrategy?> FindAsync(int id, CancellationToken ct = default) =>
            Task.FromResult(Stored(id));

        public Task<StoredStrategy?> FindByNameAsync(string name, CancellationToken ct = default) =>
            Task.FromResult(_items.Values
                .FirstOrDefault(value => StoredStrategyName.Normalize(value.Document.Name) == name) is { } value
                    ? Clone(value)
                    : null);

        public Task<bool> NameExistsAsync(
            string normalizedName,
            int? excludingId = null,
            CancellationToken ct = default) => Task.FromResult(_items.Values.Any(value =>
                value.Id != excludingId && StoredStrategyName.Normalize(value.Document.Name) == normalizedName));

        public Task<CustomPatternStoreWriteOutcome> AddAsync(
            StoredStrategy strategy,
            CancellationToken ct = default)
        {
            var result = NextWriteResult;
            NextWriteResult = CustomPatternWriteResult.Saved;
            var id = _nextId++;
            var saved = strategy with
            {
                Id = id,
                Document = strategy.Document.Copy()
            };
            saved.Document.StoredStrategyId = id;
            AddCount++;
            if (result == CustomPatternWriteResult.Saved)
            {
                _items[id] = Clone(saved);
                return Task.FromResult(CustomPatternStoreWriteOutcome.Saved(Clone(saved)));
            }
            return Task.FromResult(Failed(result));
        }

        public Task<CustomPatternStoreWriteOutcome> UpdateAsync(
            StoredStrategy strategy,
            CancellationToken ct = default)
        {
            var result = NextWriteResult;
            NextWriteResult = CustomPatternWriteResult.Saved;
            UpdateCount++;
            if (result == CustomPatternWriteResult.Saved)
            {
                _items[strategy.Id] = Clone(strategy);
                return Task.FromResult(CustomPatternStoreWriteOutcome.Saved(Clone(strategy)));
            }
            return Task.FromResult(Failed(result));
        }

        public Task<bool> DeleteAsync(int id, CancellationToken ct = default) =>
            Task.FromResult(_items.Remove(id));

        private static StoredStrategy Clone(StoredStrategy value) =>
            value with { Document = value.Document.Copy() };

        private static CustomPatternStoreWriteOutcome Failed(CustomPatternWriteResult result) =>
            result == CustomPatternWriteResult.NotFound
                ? CustomPatternStoreWriteOutcome.NotFound()
                : CustomPatternStoreWriteOutcome.NameConflict();
    }
}
