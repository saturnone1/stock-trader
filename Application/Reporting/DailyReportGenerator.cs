namespace StockTrader.Application.Reporting;

public sealed class DailyReportGenerator(
    IDailyReportActivityStore activityStore,
    IActiveAccountEquityReader equityReader,
    IDailyReportPublisher publisher,
    TimeProvider timeProvider) : IDailyReportGenerator
{
    public async Task<DailyReportData> GenerateAndPublishAsync(
        TimeZoneInfo marketTimeZone,
        CancellationToken ct = default)
    {
        var observation = timeProvider.GetUtcNow();
        var window = DailyReportPolicy.ResolveMarketDay(observation, marketTimeZone);
        var activityTask = activityStore.ReadAsync(window.FromUtc, window.ToUtc, ct);
        var equityTask = equityReader.GetAsync(ct);
        await Task.WhenAll(activityTask, equityTask);

        var report = DailyReportPolicy.Create(
            window.ReportDate,
            await activityTask,
            await equityTask);
        await publisher.PublishAsync(report, ct);
        return report;
    }
}
