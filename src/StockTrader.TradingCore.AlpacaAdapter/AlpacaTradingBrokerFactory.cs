namespace StockTrader.TradingCore.Broker;

public sealed class AlpacaTradingBrokerFactory(TimeProvider clock) : ITradingBrokerFactory
{
    public ITradingBroker Create(TradingBrokerConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (!string.Equals(connection.BrokerCode, "Alpaca", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException($"Unsupported trading broker '{connection.BrokerCode}'.");

        var isPaper = !string.Equals(
            connection.Environment,
            "Live",
            StringComparison.OrdinalIgnoreCase);
        return new AlpacaTradingBroker(
            connection.ApiKey,
            connection.ApiSecret,
            isPaper,
            clock);
    }
}
