using StockTrader.Api.Contracts;
using StockTrader.Application.Execution;
using StockTrader.Services.Order;
namespace StockTrader.Api;

public static class OrderEndpoints
{
    public static RouteGroupBuilder MapOrderApi(this RouteGroupBuilder api)
    {
        var orders = api.MapGroup("/orders").RequireAuthorization();
        orders.MapPost("/execute-signal", ExecuteSignalAsync)
            .Accepts<ExecuteSignalRequest>("application/json")
            .Produces<OrderMessageResponse>()
            .Produces<OrderErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<OrderErrorResponse>(StatusCodes.Status500InternalServerError)
            .RequireRateLimiting("api");
        orders.MapPost("/close-position", ClosePositionAsync)
            .Accepts<PositionSymbolRequest>("application/json")
            .Produces<LiveOrderResponse>()
            .Produces<LiveOrderResponse>(StatusCodes.Status202Accepted)
            .Produces<LiveOrderErrorResponse>(StatusCodes.Status400BadRequest)
            .RequireRateLimiting("api");
        orders.MapPost("/reconcile-position-order", ReconcilePositionOrderAsync)
            .Accepts<PositionSymbolRequest>("application/json")
            .Produces<LiveOrderResponse>()
            .Produces<LiveOrderErrorResponse>(StatusCodes.Status400BadRequest)
            .RequireRateLimiting("api");
        orders.MapPost("/reconcile-entry-order", ReconcileEntryOrderAsync)
            .Accepts<EntryRecommendationRequest>("application/json")
            .Produces<LiveOrderResponse>()
            .Produces<LiveOrderErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<LiveOrderErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<LiveOrderErrorResponse>(StatusCodes.Status409Conflict)
            .RequireRateLimiting("api");
        return api;
    }

    private static async Task<IResult> ExecuteSignalAsync(
        ExecuteSignalRequest request,
        HttpContext context,
        IOrderService orders,
        ILoggerFactory loggerFactory)
    {
        if (request.SignalId <= 0)
            return Results.BadRequest(new OrderErrorResponse("'signalId' must be a positive integer."));

        try
        {
            var (success, message) = await orders.PlaceManualOrderAsync(
                request.SignalId, context.RequestAborted);
            return success
                ? Results.Ok(new OrderMessageResponse(message))
                : Results.BadRequest(new OrderErrorResponse(message));
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            loggerFactory.CreateLogger("ManualOrderEndpoint").LogError(
                exception, "Manual order processing failed for signal {SignalId}", request.SignalId);
            return Results.Json(
                new OrderErrorResponse("수동 주문 처리 중 오류가 발생했습니다. 브로커 주문 내역을 확인하세요."),
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> ClosePositionAsync(
        PositionSymbolRequest request,
        HttpContext context,
        ILiveOrderManagement orders) => Map(await orders.ClosePositionAsync(
            request.Symbol ?? string.Empty, context.RequestAborted));

    private static async Task<IResult> ReconcilePositionOrderAsync(
        PositionSymbolRequest request,
        HttpContext context,
        ILiveOrderManagement orders) => Map(await orders.ReconcilePositionAsync(
            request.Symbol ?? string.Empty, context.RequestAborted));

    private static async Task<IResult> ReconcileEntryOrderAsync(
        EntryRecommendationRequest request,
        HttpContext context,
        ILiveOrderManagement orders) => Map(await orders.ReconcileEntryAsync(
            request.RecommendationId, context.RequestAborted));

    private static IResult Map(LiveOrderManagementResult result)
    {
        if (!result.IsSuccess)
        {
            var error = new LiveOrderErrorResponse(result.Error ?? "주문 처리에 실패했습니다.", result.Status);
            return result.Failure switch
            {
                LiveOrderManagementFailure.NotFound => Results.NotFound(error),
                LiveOrderManagementFailure.Conflict => Results.Conflict(error),
                _ => Results.BadRequest(error),
            };
        }

        var response = new LiveOrderResponse(
            result.Status ?? "Unknown",
            result.Message ?? string.Empty,
            result.RequestedAt?.ToString("o"),
            result.BrokerStatus,
            result.FillPrice,
            result.FilledQuantity,
            result.BrokerOrderIdPersisted);
        return result.Accepted ? Results.Accepted(value: response) : Results.Ok(response);
    }
}
