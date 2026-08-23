using System.Globalization;
using Microsoft.EntityFrameworkCore;
using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Data.Repositories;

public class OhlcvRepository : IOhlcvRepository
{
    private readonly AppDbContext _db;

    public OhlcvRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<OhlcvBar>> GetBarsAsync(string symbol, TimeFrame timeFrame,
        DateTime from, DateTime to, CancellationToken ct = default)
    {
        return await _db.OhlcvBars
            .AsNoTracking()
            .Where(b => b.Symbol == symbol && b.TimeFrame == timeFrame
                && b.Timestamp >= from && b.Timestamp <= to)
            .OrderBy(b => b.Timestamp)
            .ToListAsync(ct);
    }

    public async Task<OhlcvBar?> GetLatestBarAsync(string symbol, TimeFrame timeFrame,
        CancellationToken ct = default)
    {
        return await _db.OhlcvBars
            .AsNoTracking()
            .Where(b => b.Symbol == symbol && b.TimeFrame == timeFrame)
            .OrderByDescending(b => b.Timestamp)
            .FirstOrDefaultAsync(ct);
    }

    public async Task AddBarsAsync(IEnumerable<OhlcvBar> bars, CancellationToken ct = default)
    {
        var barList = bars.ToList();
        if (barList.Count == 0) return;

        // A provider may first expose an in-progress bar and later return its completed values.
        // The unique bar identity therefore upserts market fields instead of freezing the first sample.
        var conn = _db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);

        using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "INSERT INTO OhlcvBars " +
                "(Symbol, TimeFrame, Timestamp, Open, High, Low, Close, Volume, Vwap) " +
                "VALUES (@sym, @tf, @ts, @open, @high, @low, @close, @vol, @vwap) " +
                "ON CONFLICT(Symbol, TimeFrame, Timestamp) DO UPDATE SET " +
                "Open = excluded.Open, High = excluded.High, Low = excluded.Low, " +
                "Close = excluded.Close, Volume = excluded.Volume, Vwap = excluded.Vwap";

            // Pre-create parameters once; rebind values per row
            var pSym   = cmd.CreateParameter(); pSym.ParameterName   = "@sym";   cmd.Parameters.Add(pSym);
            var pTf    = cmd.CreateParameter(); pTf.ParameterName    = "@tf";    cmd.Parameters.Add(pTf);
            var pTs    = cmd.CreateParameter(); pTs.ParameterName    = "@ts";    cmd.Parameters.Add(pTs);
            var pOpen  = cmd.CreateParameter(); pOpen.ParameterName  = "@open";  cmd.Parameters.Add(pOpen);
            var pHigh  = cmd.CreateParameter(); pHigh.ParameterName  = "@high";  cmd.Parameters.Add(pHigh);
            var pLow   = cmd.CreateParameter(); pLow.ParameterName   = "@low";   cmd.Parameters.Add(pLow);
            var pClose = cmd.CreateParameter(); pClose.ParameterName = "@close"; cmd.Parameters.Add(pClose);
            var pVol   = cmd.CreateParameter(); pVol.ParameterName   = "@vol";   cmd.Parameters.Add(pVol);
            var pVwap  = cmd.CreateParameter(); pVwap.ParameterName  = "@vwap";  cmd.Parameters.Add(pVwap);

            foreach (var bar in barList)
            {
                pSym.Value   = bar.Symbol;
                pTf.Value    = (int)bar.TimeFrame;
                pTs.Value    = bar.Timestamp.ToString("O", CultureInfo.InvariantCulture); // ISO 8601 — SQLite stores as TEXT
                // Bind decimals as their contract text. Binding System.Decimal directly makes
                // Microsoft.Data.Sqlite add a representation-only `.0` to integral values,
                // which breaks evidence hashes after a rollback projection.
                pOpen.Value  = bar.Open.ToString("G29", CultureInfo.InvariantCulture);
                pHigh.Value  = bar.High.ToString("G29", CultureInfo.InvariantCulture);
                pLow.Value   = bar.Low.ToString("G29", CultureInfo.InvariantCulture);
                pClose.Value = bar.Close.ToString("G29", CultureInfo.InvariantCulture);
                pVol.Value   = bar.Volume;
                pVwap.Value  = bar.Vwap.HasValue
                    ? bar.Vwap.Value.ToString("G29", CultureInfo.InvariantCulture)
                    : DBNull.Value;

                await cmd.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<DateTime?> GetLastTimestampAsync(string symbol, TimeFrame timeFrame,
        CancellationToken ct = default)
    {
        return await _db.OhlcvBars
            .AsNoTracking()
            .Where(b => b.Symbol == symbol && b.TimeFrame == timeFrame)
            .MaxAsync(b => (DateTime?)b.Timestamp, ct);
    }
}
