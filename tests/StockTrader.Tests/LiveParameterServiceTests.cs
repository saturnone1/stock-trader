using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using StockTrader.Application.Settings;
using StockTrader.Models;

namespace StockTrader.Tests;

public sealed class LiveParameterServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 18, 13, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetUsesTheSettingsStoreAsTheOnlyLiveConfigurationSource()
    {
        var store = new MemoryStore(Current() with
        {
            EnabledPatterns = [PatternType.Breakout, PatternType.OpeningRangeBreakout],
            LiveParameterOverridesJson = JsonSerializer.Serialize(
                new PatternParameterOverrides { Breakout_LookbackDays = 41 })
        });
        var service = CreateService(store);

        var snapshot = await service.GetAsync();

        snapshot.EnabledPatterns.Should().Equal(PatternType.Breakout);
        snapshot.Overrides!.Breakout_LookbackDays.Should().Be(41);
    }

    [Fact]
    public async Task ApplyPersistsValidatedSettingsWithoutMutatingApplicationFiles()
    {
        var store = new MemoryStore(Current());
        var service = CreateService(store);
        var command = ValidCommand() with
        {
            Overrides = new PatternParameterOverrides { Breakout_LookbackDays = 33 },
            EnabledPatterns = [PatternType.Breakout, PatternType.Breakout]
        };

        var outcome = await service.ApplyAsync(command);

        outcome.Succeeded.Should().BeTrue();
        store.SaveCount.Should().Be(1);
        store.Value.EnabledPatterns.Should().Equal(PatternType.Breakout);
        store.Value.RiskPerTradePercent.Should().Be(0.02m);
        store.Value.LastModified.Should().Be(Now.UtcDateTime);
        JsonSerializer.Deserialize<PatternParameterOverrides>(
            store.Value.LiveParameterOverridesJson!)!.Breakout_LookbackDays.Should().Be(33);
    }

    [Theory]
    [InlineData(PatternType.OpeningRangeBreakout)]
    [InlineData(PatternType.EarningsDrift)]
    public async Task ApplyRejectsUnavailableBuiltInPatternsBeforePersistence(PatternType patternType)
    {
        var store = new MemoryStore(Current());
        var service = CreateService(store);

        var outcome = await service.ApplyAsync(ValidCommand() with
        {
            EnabledPatterns = [patternType]
        });

        outcome.Succeeded.Should().BeFalse();
        outcome.Errors.Should().ContainSingle(error => error.Contains("실행할 수 없는"));
        store.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task ApplyRejectsInvalidRiskLimitsBeforePersistence()
    {
        var store = new MemoryStore(Current());
        var service = CreateService(store);

        var outcome = await service.ApplyAsync(ValidCommand() with
        {
            RiskPerTradePercent = 0m,
            DailyLossLimitPercent = 2m,
            MaxTotalPositions = 2,
            MaxPositionsPerSector = 3
        });

        outcome.Succeeded.Should().BeFalse();
        outcome.Errors.Should().HaveCount(3);
        store.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task GetFailsClosedWhenStoredOverrideJsonIsMalformed()
    {
        var store = new MemoryStore(Current() with { LiveParameterOverridesJson = "{" });

        var snapshot = await CreateService(store).GetAsync();

        snapshot.Overrides.Should().BeNull();
    }

    private static LiveParameterService CreateService(MemoryStore store) =>
        new(store, new FixedTimeProvider(Now), NullLogger<LiveParameterService>.Instance);

    private static LiveParameterApplyCommand ValidCommand() => new(
        new PatternParameterOverrides(),
        [PatternType.Breakout],
        0.02m,
        0.04m,
        6,
        2);

    private static ManagedSettings Current() => new()
    {
        Id = 1,
        EnabledPatterns = [PatternType.Breakout],
        RiskPerTradePercent = 0.01m,
        DailyLossLimitPercent = 0.03m,
        MaxTotalPositions = 7,
        MaxPositionsPerSector = 2
    };

    private sealed class MemoryStore(ManagedSettings value) : ISettingsManagementStore
    {
        public ManagedSettings Value { get; private set; } = value;
        public int SaveCount { get; private set; }

        public Task<ManagedSettings> GetAsync(CancellationToken ct = default) =>
            Task.FromResult(Value);

        public Task SaveAsync(ManagedSettings settings, CancellationToken ct = default)
        {
            Value = settings;
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
