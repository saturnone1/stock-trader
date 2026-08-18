using StockTrader.Application.Accounts;
using StockTrader.Services.Broker;

namespace StockTrader.Services.Account;

public interface IAccountBrokerServiceFactory
{
    IBrokerService Create(ManagedTradingAccount account);
}
