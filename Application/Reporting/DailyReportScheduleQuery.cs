using StockTrader.Application.Settings;

namespace StockTrader.Application.Reporting;

public sealed class DailyReportScheduleQuery(ISettingsManagementStore settings)
    : IDailyReportScheduleQuery
{
    public async Task<TimeOnly?> GetKoreanReportTimeAsync(
        CancellationToken ct = default)
    {
        var value = (await settings.GetAsync(ct)).DailyReportTimeKst;
        return TimeOnly.TryParseExact(value, "HH:mm", out var parsed)
            ? parsed
            : null;
    }
}
