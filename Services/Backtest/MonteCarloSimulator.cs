using StockTrader.Models;

namespace StockTrader.Services.Backtest;

/// <summary>
/// 몬테카를로 시뮬레이션: 거래 PnL 순서를 무작위로 섞어 최종 자산,
/// 최대 낙폭, 손실 확률 등의 분포를 추정합니다.
/// </summary>
internal static class MonteCarloSimulator
{
    public static MonteCarloResult Run(
        List<TradeRecord> trades, decimal initialCapital, int simulations)
    {
        var tradePnls = new decimal[trades.Count];
        for (int k = 0; k < trades.Count; k++) tradePnls[k] = trades[k].PnL;

        var finalEquities = new decimal[simulations];
        var maxDrawdowns = new decimal[simulations];

        Parallel.For(0, simulations, i =>
        {
            var shuffled = ShuffleArray(tradePnls, i);
            var equity = initialCapital;
            var peak = equity;
            var maxDd = 0m;

            foreach (var pnl in shuffled)
            {
                equity += pnl;
                if (equity > peak) peak = equity;
                var dd = peak > 0 ? (peak - equity) / peak : 0;
                if (dd > maxDd) maxDd = dd;
            }

            finalEquities[i] = equity;
            maxDrawdowns[i] = maxDd;
        });

        Array.Sort(finalEquities);
        Array.Sort(maxDrawdowns);

        // 손실 = 원금 미만인 시뮬레이션 수 (손익분기점 == 원금은 손실 아님)
        // BinarySearch로 initialCapital 미만의 첫 번째 인덱스보다 큰 위치를 구한다.
        // finalEquities는 오름차순 정렬됨.
        int bsResult = Array.BinarySearch(finalEquities, initialCapital);
        // exact match가 있으면 같은 값이 여러 개일 수 있으므로 가장 왼쪽 인덱스로 이동
        int firstNotLoss = bsResult >= 0 ? bsResult : ~bsResult;
        while (firstNotLoss > 0 && finalEquities[firstNotLoss - 1] >= initialCapital)
            firstNotLoss--;
        // firstNotLoss = initialCapital과 같거나 큰 값이 처음 나타나는 인덱스
        // = initialCapital 미만인 원소의 수 = 손실 시뮬레이션 수
        var lossCount = firstNotLoss;

        return new MonteCarloResult
        {
            Simulations = simulations,
            MedianFinalEquity = Percentile(finalEquities, 50),
            MeanFinalEquity = finalEquities.Average(),
            Percentile5Equity = Percentile(finalEquities, 5),
            Percentile25Equity = Percentile(finalEquities, 25),
            Percentile75Equity = Percentile(finalEquities, 75),
            Percentile95Equity = Percentile(finalEquities, 95),
            MedianMaxDrawdown = Percentile(maxDrawdowns, 50),
            WorstCaseMaxDrawdown = Percentile(maxDrawdowns, 95),
            ProbabilityOfLoss = (decimal)lossCount / simulations,
            EquityDistribution = finalEquities.ToList()
        };
    }

    private static decimal[] ShuffleArray(decimal[] source, int seed)
    {
        var rng = new Random(seed);
        var arr = (decimal[])source.Clone();
        for (int i = arr.Length - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (arr[i], arr[j]) = (arr[j], arr[i]);
        }
        return arr;
    }

    private static decimal Percentile(decimal[] sorted, int percentile)
    {
        if (sorted.Length == 0) return 0;
        var index = (int)Math.Ceiling(percentile / 100.0 * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }
}
