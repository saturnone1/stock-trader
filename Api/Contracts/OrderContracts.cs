using System.Text.Json.Serialization;

namespace StockTrader.Api.Contracts;

public sealed record ExecuteSignalRequest(long SignalId);
public sealed record EntryRecommendationRequest(long RecommendationId);
public sealed record PositionSymbolRequest(string? Symbol);
public sealed record OrderMessageResponse(string Message);
public sealed record OrderErrorResponse(string Error);
public sealed record LiveOrderResponse(
    string Status,
    string Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RequestedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? BrokerStatus,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? FillPrice,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? FilledQuantity,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? BrokerOrderIdPersisted);
public sealed record LiveOrderErrorResponse(
    string Error,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Status = null);
