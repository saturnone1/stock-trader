using StockTrader.Data.Repositories;
using StockTrader.Services.Account;
using StockTrader.Services.Order;

namespace StockTrader.Api;

public static class OrderEndpoints
{
    public static RouteGroupBuilder MapOrderApi(this RouteGroupBuilder api)
    {
        var orders = api.MapGroup("/orders").RequireAuthorization();
        orders.MapPost("/execute-signal", ExecuteSignalAsync).RequireRateLimiting("api");
        orders.MapPost("/close-position", ClosePositionAsync).RequireRateLimiting("api");
        return api;
    }

    private static async Task<IResult> ExecuteSignalAsync(HttpContext context, IOrderService orders)
    {
        if (!context.User.Identity?.IsAuthenticated ?? true)
            return Results.Unauthorized();
        try
        {
            using var body = await System.Text.Json.JsonDocument.ParseAsync(context.Request.Body);
            var signalId = body.RootElement.GetProperty("signalId").GetInt64();
            var (success, message) = await orders.PlaceManualOrderAsync(signalId, context.RequestAborted);
            return success ? Results.Ok(new { message }) : Results.BadRequest(new { error = message });
        }
        catch
        {
            return Results.BadRequest(new { error = "Invalid JSON body. Provide 'signalId' (integer)." });
        }
    }

    private static async Task<IResult> ClosePositionAsync(
        HttpContext context,
        IAccountManager accounts,
        ITradeRepository trades,
        ILivePositionExitCoordinator exits)
    {
        if (!context.User.Identity?.IsAuthenticated ?? true)
            return Results.Unauthorized();

        string symbol;
        try
        {
            using var body = await System.Text.Json.JsonDocument.ParseAsync(context.Request.Body);
            symbol = body.RootElement.GetProperty("symbol").GetString() ?? "";
            if (string.IsNullOrWhiteSpace(symbol))
                return Results.BadRequest(new { error = "'symbol' must not be empty." });
        }
        catch
        {
            return Results.BadRequest(new { error = "Invalid JSON body. Provide 'symbol' (string)." });
        }

        var broker = await accounts.GetActiveBrokerServiceAsync(context.RequestAborted);
        if (broker == null)
            return Results.BadRequest(new { error = "활성 브로커 계좌가 없습니다. 계좌 관리에서 계좌를 설정하세요." });

        var positions = (await trades.GetOpenPositionsAsync(context.RequestAborted))
            .Where(item => item.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (positions.Length == 0)
            return Results.BadRequest(new { error = $"{symbol}의 관리 중인 오픈 포지션을 찾을 수 없습니다." });
        if (positions.Length > 1)
            return Results.BadRequest(new { error = $"{symbol} 포지션이 여러 계좌에 있어 계좌별 청산 기능이 필요합니다." });

        var submission = await exits.SubmitAsync(
            positions[0], "사용자 수동 청산", broker, context.RequestAborted);
        return submission.Status is LiveExitSubmissionStatus.Accepted or LiveExitSubmissionStatus.AlreadyPending
            ? Results.Ok(new { message = $"{symbol} 청산 주문 접수됨" })
            : Results.BadRequest(new { error = $"{symbol} 청산 실패. 브로커 연결 상태 또는 보유 포지션을 확인하세요." });
    }
}
