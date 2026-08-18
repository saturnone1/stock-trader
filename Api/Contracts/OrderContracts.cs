namespace StockTrader.Api.Contracts;

public sealed record ExecuteSignalRequest(long SignalId);
public sealed record OrderMessageResponse(string Message);
public sealed record OrderErrorResponse(string Error);
