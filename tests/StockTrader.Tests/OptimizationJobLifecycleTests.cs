using FluentAssertions;
using Moq;
using StockTrader.Application.Optimization;
using StockTrader.Data.Repositories;
using StockTrader.Models;

namespace StockTrader.Tests;

public class OptimizationJobLifecycleTests
{
    [Fact]
    public async Task TryStartNextAsync_MarksRunningAndReturnsStorageIndependentTicket()
    {
        var stored = new OptimizationJob
        {
            Id = 8,
            Name = "lifecycle",
            Priority = 4,
            RequestJson = "{\"symbols\":[\"TQQQ\"]}",
            TotalCombinations = 900,
            TestedCombinations = 200,
            CurrentChunkIndex = 2,
            ChunkSize = 100,
            MaxDurationHours = 3m,
            MaxTestedCombinations = 700,
            RankBy = "calmarRatio",
            TopResultsToKeep = 30
        };
        var repository = new Mock<IOptimizationRepository>();
        repository.Setup(value => value.TryClaimNextPendingJobAsync(It.IsAny<DateTime>()))
            .Callback<DateTime>(claimedAt =>
            {
                stored.Status = OptimizationJobStatus.Running;
                stored.StartedAt ??= claimedAt;
            })
            .ReturnsAsync(stored);
        var lifecycle = new OptimizationJobLifecycle(repository.Object);
        var observedAt = new DateTime(2026, 8, 18, 2, 0, 0, DateTimeKind.Utc);

        var ticket = await lifecycle.TryStartNextAsync(observedAt);

        stored.Status.Should().Be(OptimizationJobStatus.Running);
        stored.StartedAt.Should().Be(observedAt);
        ticket.Should().NotBeNull();
        ticket!.Id.Should().Be(stored.Id);
        ticket.RequestJson.Should().Be(stored.RequestJson);
        ticket.TestedCombinations.Should().Be(200);
        ticket.CurrentChunkIndex.Should().Be(2);
        ticket.RankBy.Should().Be("calmarRatio");
        ticket.TopResultsToKeep.Should().Be(30);
        repository.Verify(value => value.TryClaimNextPendingJobAsync(observedAt), Times.Once);
    }

    [Theory]
    [InlineData(OptimizationJobExecutionDisposition.Completed, OptimizationJobStatus.Completed)]
    [InlineData(OptimizationJobExecutionDisposition.Cancelled, OptimizationJobStatus.Cancelled)]
    public async Task ApplyDispositionAsync_SetsTerminalStateAndClearsPreviousError(
        OptimizationJobExecutionDisposition disposition,
        OptimizationJobStatus expectedStatus)
    {
        var stored = new OptimizationJob
        {
            Id = 9,
            Status = OptimizationJobStatus.Running,
            ErrorMessage = "previous"
        };
        var repository = CreateStateRepository(stored);
        var lifecycle = new OptimizationJobLifecycle(repository.Object);
        var observedAt = new DateTime(2026, 8, 18, 3, 0, 0, DateTimeKind.Utc);

        await lifecycle.ApplyDispositionAsync(stored.Id, disposition, observedAt);

        stored.Status.Should().Be(expectedStatus);
        stored.CompletedAt.Should().Be(observedAt);
        stored.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task ShutdownAndFailureTransitionsPreserveTheirDistinctEvidence()
    {
        var stored = new OptimizationJob
        {
            Id = 10,
            Status = OptimizationJobStatus.Running,
            CompletedAt = DateTime.UtcNow,
            ErrorMessage = "old"
        };
        var repository = CreateStateRepository(stored);
        var lifecycle = new OptimizationJobLifecycle(repository.Object);

        await lifecycle.ReturnToPendingAsync(stored.Id);

        stored.Status.Should().Be(OptimizationJobStatus.Pending);
        stored.CompletedAt.Should().BeNull();
        stored.ErrorMessage.Should().BeNull();

        var failedAt = new DateTime(2026, 8, 18, 4, 0, 0, DateTimeKind.Utc);
        await lifecycle.MarkFailedAsync(stored.Id, failedAt, "data unavailable");

        stored.Status.Should().Be(OptimizationJobStatus.Failed);
        stored.CompletedAt.Should().Be(failedAt);
        stored.ErrorMessage.Should().Be("data unavailable");
    }

    private static Mock<IOptimizationRepository> CreateStateRepository(OptimizationJob stored)
    {
        var repository = new Mock<IOptimizationRepository>();
        repository.Setup(value => value.GetJobSummaryAsync(stored.Id)).ReturnsAsync(stored);
        repository.Setup(value => value.UpdateJobAsync(stored)).Returns(Task.CompletedTask);
        return repository;
    }
}
