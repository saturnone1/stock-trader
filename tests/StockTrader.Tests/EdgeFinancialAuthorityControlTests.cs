using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using StockTrader.Application.TradingCore;
using StockTrader.Data;
using StockTrader.Data.Repositories;
using StockTrader.ServiceContracts.TradingCore;

namespace StockTrader.Tests;

public sealed class EdgeFinancialAuthorityControlTests
{
    [Fact]
    public async Task FenceBlocksCommandsAndBarrierWaitsForActivePositionCycle()
    {
        var factory = await CreateFactoryAsync();
        var control = new EdgeFinancialAuthorityControl(factory, TimeProvider.System);
        var transitionId = Guid.NewGuid().ToString();
        var activeCycle = await control.TryEnterPositionCycleAsync();
        activeCycle.Should().NotBeNull();

        var fence = await control.FenceAsync(transitionId, 7);
        fence.FenceHash.Should().Be(TradingControlIdentity.Fence(fence));
        var gate = new EdgeFinancialCommandGate(factory);
        var rejected = () => gate.EnsureOpenAsync(FinancialCommandClasses.NewEntry);
        await rejected.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{transitionId}*");

        var barrier = control.EnterPositionBarrierAsync(transitionId, 7);
        await Task.Delay(20);
        barrier.IsCompleted.Should().BeFalse();
        await activeCycle!.DisposeAsync();
        var receipt = await barrier;
        receipt.PositionCycle.Should().Be("AtBarrier");
        (await control.TryEnterPositionCycleAsync()).Should().BeNull();
    }

    [Fact]
    public async Task ReleaseRequiresMirroredHigherEdgeOwnedGeneration()
    {
        var factory = await CreateFactoryAsync();
        var control = new EdgeFinancialAuthorityControl(factory, TimeProvider.System);
        var transitionId = Guid.NewGuid().ToString();
        await control.FenceAsync(transitionId, 3);

        var missingMirror = () => control.ReleaseAsync(transitionId, 3);
        await missingMirror.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("edge-higher-local-authority-not-mirrored");

        await control.MirrorAuthorityAsync(
            transitionId, 4, TradingAuthorityMode.Shadow.ToString(), AuthorityOwners.Edge,
            "canonical-authority-receipt");
        var released = await control.ReleaseAsync(transitionId, 3);
        released.NewEntryAcceptance.Should().Be(AuthorityCommandAcceptanceStates.Open);
        released.FenceHash.Should().Be(TradingControlIdentity.Fence(released));
    }

    private static async Task<IDbContextFactory<AppDbContext>> CreateFactoryAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var factory = new TestDbContextFactory(options);
        await using var db = factory.CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        return factory;
    }

    private sealed class TestDbContextFactory(DbContextOptions<AppDbContext> options)
        : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }
}
