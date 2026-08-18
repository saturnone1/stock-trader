using StockTrader.Application.Execution;
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
        orders.MapPost("/reconcile-position-exit", ReconcilePositionExitAsync)
            .RequireRateLimiting("api");
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
            var body = await context.Request.ReadFromJsonAsync<PositionSymbolRequest>(
                context.RequestAborted);
            symbol = body?.Symbol ?? "";
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
        return submission.Status switch
        {
            LiveExitSubmissionStatus.Accepted => Results.Accepted(value: new
            {
                status = submission.Status.ToString(),
                message = $"{symbol} 청산 주문이 브로커에 접수되었습니다.",
                requestedAt = submission.RequestedAt?.ToString("o"),
                brokerStatus = submission.Order?.Status.ToString(),
                brokerOrderIdPersisted = submission.BrokerOrderIdPersisted
            }),
            LiveExitSubmissionStatus.AlreadyPending => Results.Ok(new
            {
                status = submission.Status.ToString(),
                message = $"{symbol} 청산 주문의 확정 상태를 기다리고 있습니다.",
                requestedAt = submission.RequestedAt?.ToString("o")
            }),
            _ => Results.BadRequest(new
            {
                status = submission.Status.ToString(),
                error = $"{symbol} 청산 실패. 브로커 연결 상태 또는 보유 포지션을 확인하세요."
            })
        };
    }

    private static async Task<IResult> ReconcilePositionExitAsync(
        HttpContext context,
        IAccountManager accounts,
        ITradeRepository trades,
        ILivePositionExitCoordinator exits)
    {
        if (!context.User.Identity?.IsAuthenticated ?? true)
            return Results.Unauthorized();

        PositionSymbolRequest? body;
        try
        {
            body = await context.Request.ReadFromJsonAsync<PositionSymbolRequest>(
                context.RequestAborted);
        }
        catch
        {
            return Results.BadRequest(new { error = "Invalid JSON body. Provide 'symbol' (string)." });
        }

        var symbol = body?.Symbol?.Trim() ?? string.Empty;
        if (symbol.Length == 0)
            return Results.BadRequest(new { error = "'symbol' must not be empty." });

        var positions = (await trades.GetOpenPositionsAsync(context.RequestAborted))
            .Where(item => item.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (positions.Length != 1)
            return Results.BadRequest(new
            {
                error = positions.Length == 0
                    ? $"{symbol}의 관리 중인 오픈 포지션을 찾을 수 없습니다."
                    : $"{symbol} 포지션이 여러 계좌에 있어 계좌별 재조정 기능이 필요합니다."
            });

        var position = positions[0];
        if (!position.ExitRequestedAt.HasValue)
        {
            return Results.Ok(new
            {
                status = LiveExitReconciliationStatus.NotPending.ToString(),
                message = $"{symbol}에는 확인할 청산 주문이 없습니다."
            });
        }

        var broker = await accounts.GetActiveBrokerServiceAsync(context.RequestAborted);
        if (broker == null)
            return Results.BadRequest(new { error = "활성 브로커 계좌가 없습니다. 계좌 관리에서 계좌를 설정하세요." });

        var reconciliation = await exits.ReconcileAsync(
            position, broker, ct: context.RequestAborted);
        var message = reconciliation.Status switch
        {
            LiveExitReconciliationStatus.Completed => $"{symbol} 청산 체결을 확인하고 거래 기록을 완료했습니다.",
            LiveExitReconciliationStatus.ReleasedForRetry => $"{symbol} 청산 주문의 실패가 확인되어 다시 청산할 수 있습니다.",
            LiveExitReconciliationStatus.AwaitingBroker => $"{symbol} 청산 주문은 아직 브로커 확정 상태를 기다리고 있습니다.",
            LiveExitReconciliationStatus.ConcurrentChange => $"{symbol} 상태가 다른 작업에서 변경되어 최신 목록을 다시 불러옵니다.",
            LiveExitReconciliationStatus.BrokerFillMismatch =>
                $"{symbol} 청산 요청 수량과 브로커 체결 수량이 달라 자동 반영을 중단했습니다. 브로커 주문 내역을 확인하세요.",
            _ => $"{symbol}에는 확인할 청산 주문이 없습니다."
        };
        return Results.Ok(new
        {
            status = reconciliation.Status.ToString(),
            message,
            brokerStatus = reconciliation.Order?.Status.ToString(),
            fillPrice = reconciliation.Order?.AverageFillPrice,
            filledQuantity = reconciliation.FilledQuantity
        });
    }

    private sealed record PositionSymbolRequest(string? Symbol);
}
