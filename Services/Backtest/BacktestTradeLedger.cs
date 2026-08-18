using StockTrader.Domain.Backtesting;
using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Services.Backtest;

/// <summary>
/// 백테스트 거래 목록과 비용 정산을 소유하고, 정산된 손익을 포트폴리오와 전략 런타임에
/// 정확히 한 번 반영합니다.
/// </summary>
internal sealed class BacktestTradeLedger
{
    private readonly BacktestPortfolioState _portfolio;
    private readonly BacktestStrategyRuntimeRegistry _runtimeRegistry;
    private readonly BacktestExecutionCostLedger _executionCosts;

    public List<TradeRecord> Trades { get; } = [];
    public int Count => Trades.Count;
    public decimal TotalSlippage => _executionCosts.TotalSlippage;
    public decimal TotalCommission => _executionCosts.TotalCommission;

    public BacktestTradeLedger(
        BacktestPortfolioState portfolio,
        BacktestStrategyRuntimeRegistry runtimeRegistry,
        SlippageModel slippageModel,
        decimal slippagePercent,
        decimal commissionPerTrade)
    {
        _portfolio = portfolio;
        _runtimeRegistry = runtimeRegistry;
        _executionCosts = new BacktestExecutionCostLedger(
            slippageModel, slippagePercent, commissionPerTrade);
    }

    public void SettleSince(int startIndex)
    {
        _executionCosts.ApplyNewTrades(Trades, startIndex, trade =>
        {
            _portfolio.ApplyRealizedTrade(trade);
            _runtimeRegistry.ApplyRealizedTrade(trade);
        });
    }
}
