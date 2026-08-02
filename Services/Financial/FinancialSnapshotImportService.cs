using Microsoft.EntityFrameworkCore;
using StockTrader.Api;
using StockTrader.Data;
using StockTrader.Models;

namespace StockTrader.Services.Financial;

public class FinancialSnapshotImportService
{
    public async Task<FinancialImportSummary> UpsertAsync(
        AppDbContext db,
        IEnumerable<FinancialSnapshotImportDto> items,
        CancellationToken ct)
    {
        var normalized = items
            .Where(item => !string.IsNullOrWhiteSpace(item.Symbol))
            .Select(item => new FinancialSnapshot
            {
                Symbol = item.Symbol!.Trim().ToUpperInvariant(),
                AsOfDate = item.AsOfDate?.Date ?? DateTime.UtcNow.Date,
                Source = string.IsNullOrWhiteSpace(item.Source) ? "Manual" : item.Source!.Trim(),
                PeRatio = item.PeRatio,
                PbRatio = item.PbRatio,
                RoePercent = item.RoePercent,
                OperatingMarginPercent = item.OperatingMarginPercent,
                RevenueCurrent = item.RevenueCurrent,
                RevenuePrevious = item.RevenuePrevious,
                OperatingIncomeCurrent = item.OperatingIncomeCurrent,
                OperatingIncomePrevious = item.OperatingIncomePrevious,
                NetIncomeCurrent = item.NetIncomeCurrent,
                NetIncomePrevious = item.NetIncomePrevious,
                Notes = item.Notes?.Trim(),
                UpdatedAt = DateTime.UtcNow
            })
            .ToList();

        var summary = new FinancialImportSummary();

        foreach (var item in normalized)
        {
            var existing = await db.FinancialSnapshots
                .FirstOrDefaultAsync(x => x.Symbol == item.Symbol && x.AsOfDate == item.AsOfDate, ct);

            if (existing == null)
            {
                item.CreatedAt = DateTime.UtcNow;
                db.FinancialSnapshots.Add(item);
                summary.ImportedCount++;
            }
            else
            {
                existing.Source = item.Source;
                existing.PeRatio = item.PeRatio;
                existing.PbRatio = item.PbRatio;
                existing.RoePercent = item.RoePercent;
                existing.OperatingMarginPercent = item.OperatingMarginPercent;
                existing.RevenueCurrent = item.RevenueCurrent;
                existing.RevenuePrevious = item.RevenuePrevious;
                existing.OperatingIncomeCurrent = item.OperatingIncomeCurrent;
                existing.OperatingIncomePrevious = item.OperatingIncomePrevious;
                existing.NetIncomeCurrent = item.NetIncomeCurrent;
                existing.NetIncomePrevious = item.NetIncomePrevious;
                existing.Notes = item.Notes;
                existing.UpdatedAt = DateTime.UtcNow;
                summary.ImportedCount++;
            }
        }

        await db.SaveChangesAsync(ct);
        return summary;
    }
}

public class FinancialImportSummary
{
    public int ImportedCount { get; set; }
    public int SkippedCount { get; set; }
}
