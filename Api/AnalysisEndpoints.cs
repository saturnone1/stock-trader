using StockTrader.Services.Analysis;

namespace StockTrader.Api;

public static class AnalysisEndpoints
{
    public static RouteGroupBuilder MapAnalysisApi(this RouteGroupBuilder group)
    {
        // GET /api/analysis/{symbol}
        group.MapGet("/analysis/{symbol}", async (
            string symbol,
            IStockAnalysisService analysisService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return Results.BadRequest(new { error = "Symbol is required." });

            symbol = symbol.ToUpperInvariant();

            try
            {
                var analysis = await analysisService.AnalyzeAsync(symbol, ct);

                return Results.Ok(new
                {
                    analysis.Symbol,
                    analysis.CurrentPrice,
                    Grade               = analysis.Grade.ToString(),
                    analysis.UpsideProbability,
                    analysis.ExpectedReturnPercent,
                    analysis.ExpectedHoldingDays,
                    analysis.DownsideRiskPercent,
                    analysis.RecommendedStopLoss,
                    analysis.RecommendedTarget,
                    analysis.ConfidenceScore,
                    analysis.ATR,
                    Indicators = new
                    {
                        analysis.Indicators.RSI,
                        analysis.Indicators.SMA20,
                        analysis.Indicators.SMA50,
                        analysis.Indicators.SMA200,
                        analysis.Indicators.MACD,
                        analysis.Indicators.MACDSignal,
                        analysis.Indicators.BollingerUpper,
                        analysis.Indicators.BollingerMiddle,
                        analysis.Indicators.BollingerLower,
                        analysis.Indicators.VWAP,
                        analysis.Indicators.BullishIndicatorCount,
                        analysis.Indicators.TotalIndicatorCount
                    },
                    ActivePatterns = analysis.ActivePatterns.Select(p => new
                    {
                        Pattern                 = p.PatternType.ToString(),
                        p.Confidence,
                        p.HistoricalWinRate,
                        p.HistoricalAvgReturn
                    }),
                    AnalyzedAt = analysis.AnalyzedAt.ToString("o")
                });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = $"Analysis failed for {symbol}: {ex.Message}" });
            }
        }).RequireAuthorization();

        return group;
    }
}
