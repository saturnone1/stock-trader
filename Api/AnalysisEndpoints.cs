using StockTrader.Api.Contracts;
using StockTrader.Domain.MarketData;
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
            symbol = MarketSymbolPolicy.Normalize(symbol);
            if (!MarketSymbolPolicy.IsValid(symbol))
                return Results.BadRequest(new StockAnalysisErrorResponse(
                    "올바른 종목 코드를 입력하세요."));

            try
            {
                var analysis = await analysisService.AnalyzeAsync(symbol, ct);

                return Results.Ok(StockAnalysisResponse.Create(analysis));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new StockAnalysisErrorResponse(
                    $"Analysis failed for {symbol}: {ex.Message}"));
            }
        })
            .Produces<StockAnalysisResponse>()
            .Produces<StockAnalysisErrorResponse>(StatusCodes.Status400BadRequest)
            .RequireAuthorization();

        return group;
    }
}
