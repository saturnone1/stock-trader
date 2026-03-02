using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Backtest;

namespace StockTrader.Components.Pages;

public partial class BacktestResults
{
    private bool _showHelp;
    private string _symbols = "AAPL,MSFT,GOOGL,AMZN,TSLA,NVDA,META,SPY,QQQ,TQQQ";
    private DateTime? _from = new DateTime(2020, 1, 1);
    private DateTime? _to = DateTime.Today;
    private decimal _capital = 100_000m;
    private TimeFrame _timeFrame = TimeFrame.Daily;
    private DataSource? _dataSource = null;
    private SlippageModel _slippageModel = SlippageModel.Adaptive;
    private decimal _slippagePercent = 0.05m;
    private decimal _commissionPerTrade = 1.00m;
    private bool _enableWalkForward;
    private int _wfInSampleMonths = 12;
    private int _wfOutOfSampleMonths = 3;
    private bool _enableMonteCarlo;
    private int _mcSimulations = 1000;
    private HashSet<PatternType> _selectedPatterns = new()
    {
        PatternType.Breakout, PatternType.TrendPullback,
        PatternType.VolatilityExpansion, PatternType.MomentumReversal,
        PatternType.MeanReversionChannel, PatternType.Rsi2Bollinger,
        PatternType.Tqqq200Sma
    };
    private BacktestResult? _result;
    private bool _isRunning;
    private string? _errorMessage;
    private MudBlazor.MudTable<TradeRecord>? _tradeTable;

    // ── 차트 다운샘플링 (렉 방지) ──
    private List<EquityPoint> _chartData = new();
    private const int MaxChartPoints = 300;

    private static string FormatPrice(decimal price)
        => price >= 1000 ? $"${price:N0}" : $"${price:N2}";

    /// <summary>자본 대비 수익률 계산 (0 나누기 방지)</summary>
    private decimal SafeReturnPercent(decimal equity)
        => _capital > 0 ? equity / _capital - 1 : 0;

    // ── 리스크 관리 파라미터 ──
    private decimal _riskPerTradePercent = 0.01m;
    private decimal _riskDailyLossLimitPercent = 0.03m;
    private int _riskMaxTotalPositions = 7;
    private int _riskMaxPositionsPerSector = 2;

    private const decimal DefaultRiskPerTrade = 0.01m;
    private const decimal DefaultDailyLossLimit = 0.03m;
    private const int DefaultMaxTotalPositions = 7;
    private const int DefaultMaxPositionsPerSector = 2;

    private void ResetRiskDefaults()
    {
        _riskPerTradePercent = DefaultRiskPerTrade;
        _riskDailyLossLimitPercent = DefaultDailyLossLimit;
        _riskMaxTotalPositions = DefaultMaxTotalPositions;
        _riskMaxPositionsPerSector = DefaultMaxPositionsPerSector;
    }

    // ── 패턴 파라미터 뷰모델 ──
    private PatternParamViewModel _p = new();

    private void ResetPatternDefaults()
    {
        _p = new PatternParamViewModel();
    }

    private ApexCharts.ApexChartOptions<EquityPoint> _equityChartOptions = new()
    {
        Chart = new ApexCharts.Chart { Background = "transparent", ForeColor = "#9E9E9E" },
        Theme = new ApexCharts.Theme { Mode = ApexCharts.Mode.Dark },
        Stroke = new ApexCharts.Stroke { Curve = ApexCharts.Curve.Smooth, Width = 2 },
        Fill = new ApexCharts.Fill { Type = new List<ApexCharts.FillType> { ApexCharts.FillType.Gradient }, Opacity = new List<double> { 0.3 } },
        Yaxis = [new ApexCharts.YAxis { Labels = new ApexCharts.YAxisLabels { Formatter = @"function(v) { return '$' + v.toFixed(0) }" } }],
        Tooltip = new ApexCharts.Tooltip { Y = new ApexCharts.TooltipY { Formatter = @"function(v) { return '$' + v.toFixed(2) }" } }
    };

    private void TogglePattern(PatternType pattern, bool selected)
    {
        if (selected) _selectedPatterns.Add(pattern);
        else _selectedPatterns.Remove(pattern);
    }

    private static string GetTimeFrameLabel(TimeFrame tf) => tf switch
    {
        TimeFrame.OneMinute     => "1분봉",
        TimeFrame.FiveMinute    => "5분봉",
        TimeFrame.FifteenMinute => "15분봉",
        TimeFrame.Daily         => "일봉",
        TimeFrame.Weekly        => "주봉",
        _                       => tf.ToString()
    };

    private static int GetMaxRecommendedDays(TimeFrame tf) => tf switch
    {
        TimeFrame.OneMinute     => 5,
        TimeFrame.FiveMinute    => 30,
        TimeFrame.FifteenMinute => 60,
        _                       => 90
    };

    private int GetSelectedDays()
        => (int)((_to ?? DateTime.Today) - (_from ?? DateTime.Today.AddYears(-1))).TotalDays;

    private bool IsIntraDayPeriodOverLimit()
        => GetSelectedDays() > GetMaxRecommendedDays(_timeFrame);

    private string GetTimeFrameHelperText() => _timeFrame switch
    {
        TimeFrame.OneMinute     => "스캘핑 전략 (ORB) 에 최적",
        TimeFrame.FiveMinute    => "단타 전략 (GapUp, VWAP, VolSpike) 에 최적",
        TimeFrame.FifteenMinute => "단타/스윙 혼합 전략에 적합",
        TimeFrame.Daily         => "스윙/포지션 전략 (Breakout, RSI, Trend 등) 에 최적",
        TimeFrame.Weekly        => "장기 포지션 전략에 적합",
        _                       => ""
    };

    private static string GetTimeFramePatternHint(TimeFrame tf) => tf switch
    {
        TimeFrame.OneMinute =>
            "1분봉 권장 패턴: ORB (시초가 돌파). 스윙/포지션 패턴은 분봉에서 신호가 부정확할 수 있습니다.",
        TimeFrame.FiveMinute =>
            "5분봉 권장 패턴: 갭업 풀백, VWAP 회귀, 거래량 스파이크. ORB 도 활용 가능합니다.",
        TimeFrame.FifteenMinute =>
            "15분봉 권장 패턴: 갭업 풀백, VWAP 회귀, 거래량 스파이크, 브레이크아웃.",
        TimeFrame.Daily =>
            "일봉 권장 패턴: 브레이크아웃, RSI 평균회귀, 추세 풀백, 변동성 확대, 모멘텀 반전, 다중시간 추세.",
        TimeFrame.Weekly =>
            "주봉 권장 패턴: 브레이크아웃, 추세 풀백, 다중시간 추세.",
        _ => ""
    };

    /// <summary>EquityCurve를 maxPoints 이하로 다운샘플링 (처음/끝 보존, 균등 간격 추출)</summary>
    private static List<EquityPoint> DownsampleEquityCurve(List<EquityPoint> source, int maxPoints)
    {
        if (source.Count <= maxPoints) return source;

        var result = new List<EquityPoint>(maxPoints);
        result.Add(source[0]);

        var step = (double)(source.Count - 1) / (maxPoints - 1);
        for (int i = 1; i < maxPoints - 1; i++)
        {
            var idx = (int)Math.Round(i * step);
            result.Add(source[idx]);
        }

        result.Add(source[^1]);
        return result;
    }

    private async Task RunBacktestAsync()
    {
        _isRunning = true;
        _result = null;
        StateHasChanged();

        try
        {
            var request = new BacktestRequest
            {
                Symbols = _symbols.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList(),
                Patterns = _selectedPatterns.ToList(),
                From = _from ?? DateTime.Today.AddYears(-5),
                To = _to ?? DateTime.Today,
                InitialCapital = _capital,
                TimeFrame = _timeFrame,
                SlippagePercent = _slippagePercent,
                SlippageModel = _slippageModel,
                CommissionPerTrade = _commissionPerTrade,
                EnableWalkForward = _enableWalkForward,
                WalkForwardInSampleMonths = _wfInSampleMonths,
                WalkForwardOutOfSampleMonths = _wfOutOfSampleMonths,
                EnableMonteCarlo = _enableMonteCarlo,
                MonteCarloSimulations = _mcSimulations,
                RiskPerTradePercent = _riskPerTradePercent,
                DailyLossLimitPercent = _riskDailyLossLimitPercent,
                MaxTotalPositions = _riskMaxTotalPositions,
                MaxPositionsPerSector = _riskMaxPositionsPerSector,
                DataSource = _dataSource,
                ParameterOverrides = _p.ToOverrides()
            };
            _result = await BacktestService.RunAsync(request);
            _chartData = DownsampleEquityCurve(_result.EquityCurve, MaxChartPoints);
        }
        catch (OperationCanceledException)
        {
            _errorMessage = "백테스트가 취소되었습니다.";
        }
        catch (Exception ex)
        {
            _errorMessage = $"백테스트 실패: {ex.Message}";
        }
        finally
        {
            _isRunning = false;
        }
    }
}
