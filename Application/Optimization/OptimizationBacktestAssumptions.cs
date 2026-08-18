using StockTrader.Domain.Backtesting;

namespace StockTrader.Application.Optimization;

/// <summary>
/// 동기·백그라운드 최적화가 후보를 같은 비용 조건에서 비교하기 위한 중앙 체결 가정입니다.
/// 사용자 실행 백테스트의 명시적 비용 입력에는 적용하지 않습니다.
/// </summary>
public static class OptimizationBacktestAssumptions
{
    public const decimal SlippagePercent = 0.05m;
    public const decimal CommissionPerTrade = 1.00m;
    public const SlippageModel CostModel = BacktestExecutionCatalog.DefaultSlippageModel;
}
