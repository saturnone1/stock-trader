namespace StockTrader.Api;

/// <summary>
/// StockTrader REST API 엔드포인트 전체를 /api 그룹에 등록하는 확장 메서드.
/// Program.cs에서 app.MapStockTraderApi() 한 줄로 모든 엔드포인트를 활성화한다.
/// </summary>
public static class ApiEndpointExtensions
{
    public static WebApplication MapStockTraderApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapHealthApi();
        api.MapAuthApi();
        api.MapOrderApi();
        api.MapBacktestApi();
        api.MapDashboardApi();
        api.MapPortfolioApi();
        api.MapSignalApi();
        api.MapTradeApi();
        api.MapRiskApi();
        api.MapSettingsApi();
        api.MapAccountApi();
        api.MapMlApi();
        api.MapAnalysisApi();
        api.MapPatternStatsApi();
        api.MapUniverseApi();
        api.MapFinancialFactorApi();
        api.MapSymbolProfileApi();
        api.MapCustomPatternApi();
        api.MapPatternPreviewApi();
        api.MapOptimizeApi();
        api.MapOptimizeJobApi();
        api.MapMetadataApi();
        api.MapOptimizationWorkerApi();

        return app;
    }
}
