using StockTrader.Configuration;
using StockTrader.Api.Contracts;
using StockTrader.Application.Settings;
using StockTrader.Models;
using StockTrader.Services.Backtest;

namespace StockTrader.Api;

public static class BacktestEndpoints
{
    public static RouteGroupBuilder MapBacktestApi(this RouteGroupBuilder api)
    {
        api.MapPost("/backtest", RunAsync)
            .Produces<BacktestResponse>()
            .Produces<BacktestErrorResponse>(StatusCodes.Status400BadRequest)
            .RequireAuthorization();
        api.MapPost("/backtest/apply-live", ApplyLiveAsync)
            .Produces<ApplyLiveResponse>()
            .Produces<SettingsErrorResponse>(StatusCodes.Status400BadRequest)
            .RequireAuthorization();
        return api;
    }

    private static async Task<IResult> RunAsync(
        BacktestRequest request,
        IBacktestService service,
        CancellationToken ct)
    {
        if (string.Equals(request.BacktestMode, "weight", StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest(new BacktestErrorResponse(
                "weight 백테스트 모드는 제거되었습니다. 패턴 백테스트 또는 패턴 빌더를 사용하세요."));

        var result = await service.RunAsync(request, ct);
        return Results.Ok(BacktestResponse.Create(result));
    }

    private static async Task<IResult> ApplyLiveAsync(
        ApplyLiveRequest request,
        ILiveParameterService liveParameters,
        CancellationToken ct)
    {
        var outcome = await liveParameters.ApplyAsync(new LiveParameterApplyCommand(
            request.ParameterOverrides ?? new PatternParameterOverrides(),
            request.EnabledPatterns,
            request.RiskPerTradePercent,
            request.DailyLossLimitPercent,
            request.MaxTotalPositions,
            request.MaxPositionsPerSector), ct);
        return outcome.Succeeded
            ? Results.Ok(new ApplyLiveResponse(
                "실거래 파라미터가 적용되었습니다.",
                outcome.Settings!.LastModified))
            : Results.BadRequest(new SettingsErrorResponse(outcome.Errors));
    }

}
