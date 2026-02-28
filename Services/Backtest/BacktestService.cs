using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.DataFeed;
using StockTrader.Services.Indicators;
using StockTrader.Services.Patterns;

namespace StockTrader.Services.Backtest;

public class BacktestService : IBacktestService
{
    private readonly IDataFeedServiceFactory _dataFeedFactory;
    private readonly IEnumerable<IPatternDetector> _detectors;
    private readonly IIndicatorService _indicators;
    private readonly TradingSettings _tradingSettings;
    private readonly PatternSettings _basePatternSettings;
    private readonly ILogger<BacktestService> _logger;

    private const int MinWarmupBars = 50;

    public BacktestService(
        IDataFeedServiceFactory dataFeedFactory,
        IEnumerable<IPatternDetector> detectors,
        IIndicatorService indicators,
        IOptions<TradingSettings> tradingSettings,
        IOptions<PatternSettings> patternSettings,
        ILogger<BacktestService> logger)
    {
        _dataFeedFactory = dataFeedFactory;
        _detectors = detectors;
        _indicators = indicators;
        _tradingSettings = tradingSettings.Value;
        _basePatternSettings = patternSettings.Value;
        _logger = logger;
    }

    /// <summary>
    /// 파라미터 오버라이드를 적용한 PatternSettings 복사본을 생성합니다.
    /// 오버라이드가 null이면 기본 설정을 그대로 반환합니다.
    /// </summary>
    private PatternSettings ApplyOverrides(PatternParameterOverrides? overrides)
    {
        if (overrides == null) return _basePatternSettings;

        return new PatternSettings
        {
            EnabledPatterns = _basePatternSettings.EnabledPatterns,
            GapUpPullback = new GapUpPullbackConfig
            {
                MinGapPercent      = overrides.GapUp_MinGapPercent      ?? _basePatternSettings.GapUpPullback.MinGapPercent,
                MaxPullbackPercent = overrides.GapUp_MaxPullbackPercent ?? _basePatternSettings.GapUpPullback.MaxPullbackPercent,
                MinVolume          = overrides.GapUp_MinVolume          ?? _basePatternSettings.GapUpPullback.MinVolume
            },
            Breakout = new BreakoutConfig
            {
                LookbackDays          = overrides.Breakout_LookbackDays          ?? _basePatternSettings.Breakout.LookbackDays,
                MinVolumeMultiplier   = overrides.Breakout_MinVolumeMultiplier   ?? _basePatternSettings.Breakout.MinVolumeMultiplier,
                BreakoutMarginPercent = overrides.Breakout_BreakoutMarginPercent ?? _basePatternSettings.Breakout.BreakoutMarginPercent,
                AtrStopMultiplier     = overrides.Breakout_AtrStopMultiplier     ?? _basePatternSettings.Breakout.AtrStopMultiplier,
                AtrTargetMultiplier   = overrides.Breakout_AtrTargetMultiplier   ?? _basePatternSettings.Breakout.AtrTargetMultiplier
            },
            VwapReversion = new VwapReversionConfig
            {
                MaxDeviationPercent    = overrides.Vwap_MaxDeviationPercent    ?? _basePatternSettings.VwapReversion.MaxDeviationPercent,
                MinBouncePercent       = overrides.Vwap_MinBouncePercent       ?? _basePatternSettings.VwapReversion.MinBouncePercent,
                MinBounceFromLowPercent = overrides.Vwap_MinBounceFromLowPercent ?? _basePatternSettings.VwapReversion.MinBounceFromLowPercent
            },
            RsiMeanReversion = new RsiMeanReversionConfig
            {
                OversoldThreshold           = overrides.Rsi_OversoldThreshold           ?? _basePatternSettings.RsiMeanReversion.OversoldThreshold,
                Period                      = overrides.Rsi_Period                       ?? _basePatternSettings.RsiMeanReversion.Period,
                MinVolumeIncreaseMultiplier = overrides.Rsi_MinVolumeIncreaseMultiplier ?? _basePatternSettings.RsiMeanReversion.MinVolumeIncreaseMultiplier,
                AtrStopMultiplier           = overrides.Rsi_AtrStopMultiplier           ?? _basePatternSettings.RsiMeanReversion.AtrStopMultiplier,
                AtrTargetMultiplier         = overrides.Rsi_AtrTargetMultiplier         ?? _basePatternSettings.RsiMeanReversion.AtrTargetMultiplier
            },
            TrendPullback = new TrendPullbackConfig
            {
                MaPeriod              = overrides.Trend_MaPeriod              ?? _basePatternSettings.TrendPullback.MaPeriod,
                MaxPullbackFromMa     = overrides.Trend_MaxPullbackFromMa     ?? _basePatternSettings.TrendPullback.MaxPullbackFromMa,
                TrendConfirmationDays = overrides.Trend_TrendConfirmationDays ?? _basePatternSettings.TrendPullback.TrendConfirmationDays,
                AtrStopMultiplier     = overrides.Trend_AtrStopMultiplier     ?? _basePatternSettings.TrendPullback.AtrStopMultiplier,
                AtrTargetMultiplier   = overrides.Trend_AtrTargetMultiplier   ?? _basePatternSettings.TrendPullback.AtrTargetMultiplier
            },
            OpeningRangeBreakout = new OrbConfig
            {
                RangeMinutes    = overrides.Orb_RangeMinutes    ?? _basePatternSettings.OpeningRangeBreakout.RangeMinutes,
                MinRangePercent = overrides.Orb_MinRangePercent ?? _basePatternSettings.OpeningRangeBreakout.MinRangePercent
            },
            VolumeSpikeContinuation = new VolumeSpikeConfig
            {
                VolumeMultiplier  = overrides.VolSpike_VolumeMultiplier  ?? _basePatternSettings.VolumeSpikeContinuation.VolumeMultiplier,
                ContinuationBars  = overrides.VolSpike_ContinuationBars  ?? _basePatternSettings.VolumeSpikeContinuation.ContinuationBars,
                VolumeAvgPeriod   = overrides.VolSpike_VolumeAvgPeriod   ?? _basePatternSettings.VolumeSpikeContinuation.VolumeAvgPeriod,
                AtrStopMultiplier = overrides.VolSpike_AtrStopMultiplier ?? _basePatternSettings.VolumeSpikeContinuation.AtrStopMultiplier,
                AtrTargetMultiplier = overrides.VolSpike_AtrTargetMultiplier ?? _basePatternSettings.VolumeSpikeContinuation.AtrTargetMultiplier
            },
            EarningsDrift = new EarningsDriftConfig
            {
                DriftDays          = overrides.Earnings_DriftDays          ?? _basePatternSettings.EarningsDrift.DriftDays,
                MinSurprisePercent = overrides.Earnings_MinSurprisePercent ?? _basePatternSettings.EarningsDrift.MinSurprisePercent
            },
            IndexRegimeFilter = new IndexRegimeConfig
            {
                MaPeriod    = overrides.Regime_MaPeriod    ?? _basePatternSettings.IndexRegimeFilter.MaPeriod,
                IndexSymbol = overrides.Regime_IndexSymbol ?? _basePatternSettings.IndexRegimeFilter.IndexSymbol
            },
            VolatilityExpansion = new VolatilityExpansionConfig
            {
                BollingerPeriod    = overrides.Vola_BollingerPeriod    ?? _basePatternSettings.VolatilityExpansion.BollingerPeriod,
                StdDevMultiplier   = overrides.Vola_StdDevMultiplier   ?? _basePatternSettings.VolatilityExpansion.StdDevMultiplier,
                AtrStopMultiplier  = overrides.Vola_AtrStopMultiplier  ?? _basePatternSettings.VolatilityExpansion.AtrStopMultiplier,
                AtrTargetMultiplier = overrides.Vola_AtrTargetMultiplier ?? _basePatternSettings.VolatilityExpansion.AtrTargetMultiplier
            },
            MomentumReversal = new MomentumReversalConfig
            {
                FastEmaPeriod      = overrides.Mom_FastEmaPeriod      ?? _basePatternSettings.MomentumReversal.FastEmaPeriod,
                SlowEmaPeriod      = overrides.Mom_SlowEmaPeriod      ?? _basePatternSettings.MomentumReversal.SlowEmaPeriod,
                MacdSignalPeriod   = overrides.Mom_MacdSignalPeriod   ?? _basePatternSettings.MomentumReversal.MacdSignalPeriod,
                RsiPeriod          = overrides.Mom_RsiPeriod          ?? _basePatternSettings.MomentumReversal.RsiPeriod,
                RsiOversold        = overrides.Mom_RsiOversold        ?? _basePatternSettings.MomentumReversal.RsiOversold,
                RsiOverbought      = overrides.Mom_RsiOverbought      ?? _basePatternSettings.MomentumReversal.RsiOverbought,
                RsiMomentumMin     = overrides.Mom_RsiMomentumMin     ?? _basePatternSettings.MomentumReversal.RsiMomentumMin,
                AtrStopMultiplier  = overrides.Mom_AtrStopMultiplier  ?? _basePatternSettings.MomentumReversal.AtrStopMultiplier,
                AtrTargetMultiplier = overrides.Mom_AtrTargetMultiplier ?? _basePatternSettings.MomentumReversal.AtrTargetMultiplier
            },
            MultiTimeframeTrend = new MultiTimeframeTrendConfig
            {
                LongTrendMaPeriod       = overrides.Mtf_LongTrendMaPeriod       ?? _basePatternSettings.MultiTimeframeTrend.LongTrendMaPeriod,
                ShortEntryMaPeriod      = overrides.Mtf_ShortEntryMaPeriod      ?? _basePatternSettings.MultiTimeframeTrend.ShortEntryMaPeriod,
                MaxPullbackPercent      = overrides.Mtf_MaxPullbackPercent      ?? _basePatternSettings.MultiTimeframeTrend.MaxPullbackPercent,
                TrendConfirmationBars   = overrides.Mtf_TrendConfirmationBars   ?? _basePatternSettings.MultiTimeframeTrend.TrendConfirmationBars,
                MaxDistanceAboveShortMa = overrides.Mtf_MaxDistanceAboveShortMa ?? _basePatternSettings.MultiTimeframeTrend.MaxDistanceAboveShortMa,
                AtrStopMultiplier       = overrides.Mtf_AtrStopMultiplier       ?? _basePatternSettings.MultiTimeframeTrend.AtrStopMultiplier,
                AtrTargetMultiplier     = overrides.Mtf_AtrTargetMultiplier     ?? _basePatternSettings.MultiTimeframeTrend.AtrTargetMultiplier
            },
            MeanReversionChannel = new MeanReversionChannelConfig
            {
                EmaPeriod              = overrides.Chan_EmaPeriod              ?? _basePatternSettings.MeanReversionChannel.EmaPeriod,
                AtrPeriod              = overrides.Chan_AtrPeriod              ?? _basePatternSettings.MeanReversionChannel.AtrPeriod,
                AtrMultiplier          = overrides.Chan_AtrMultiplier          ?? _basePatternSettings.MeanReversionChannel.AtrMultiplier,
                RsiPeriod              = overrides.Chan_RsiPeriod              ?? _basePatternSettings.MeanReversionChannel.RsiPeriod,
                RsiOversold            = overrides.Chan_RsiOversold            ?? _basePatternSettings.MeanReversionChannel.RsiOversold,
                RecentLowLookbackBars  = overrides.Chan_RecentLowLookbackBars ?? _basePatternSettings.MeanReversionChannel.RecentLowLookbackBars
            },
            Rsi2Bollinger = new Rsi2BollingerConfig
            {
                RsiPeriod        = overrides.Rsi2Bb_RsiPeriod        ?? _basePatternSettings.Rsi2Bollinger.RsiPeriod,
                RsiThreshold     = overrides.Rsi2Bb_RsiThreshold     ?? _basePatternSettings.Rsi2Bollinger.RsiThreshold,
                BollingerPeriod  = overrides.Rsi2Bb_BollingerPeriod  ?? _basePatternSettings.Rsi2Bollinger.BollingerPeriod,
                BollingerStdDev  = overrides.Rsi2Bb_BollingerStdDev  ?? _basePatternSettings.Rsi2Bollinger.BollingerStdDev,
                LongTrendMaPeriod = overrides.Rsi2Bb_LongTrendMaPeriod ?? _basePatternSettings.Rsi2Bollinger.LongTrendMaPeriod,
                AtrStopMultiplier = overrides.Rsi2Bb_AtrStopMultiplier ?? _basePatternSettings.Rsi2Bollinger.AtrStopMultiplier
            },
            VolatilityBreakout = new VolatilityBreakoutConfig
            {
                BreakoutFactor      = overrides.VolBrk_BreakoutFactor      ?? _basePatternSettings.VolatilityBreakout.BreakoutFactor,
                MinVolumeMultiplier = overrides.VolBrk_MinVolumeMultiplier ?? _basePatternSettings.VolatilityBreakout.MinVolumeMultiplier,
                VolumeAvgPeriod     = overrides.VolBrk_VolumeAvgPeriod     ?? _basePatternSettings.VolatilityBreakout.VolumeAvgPeriod,
                AtrStopMultiplier   = overrides.VolBrk_AtrStopMultiplier   ?? _basePatternSettings.VolatilityBreakout.AtrStopMultiplier,
                AtrTargetMultiplier = overrides.VolBrk_AtrTargetMultiplier ?? _basePatternSettings.VolatilityBreakout.AtrTargetMultiplier
            },
            Tqqq200Sma = new Tqqq200SmaConfig
            {
                SmaPeriod            = overrides.Tqqq_SmaPeriod            ?? _basePatternSettings.Tqqq200Sma.SmaPeriod,
                OverheatPercent      = overrides.Tqqq_OverheatPercent      ?? _basePatternSettings.Tqqq200Sma.OverheatPercent,
                ConfirmationDays     = overrides.Tqqq_ConfirmationDays     ?? _basePatternSettings.Tqqq200Sma.ConfirmationDays,
                ShortTrendEmaPeriod  = overrides.Tqqq_ShortTrendEmaPeriod  ?? _basePatternSettings.Tqqq200Sma.ShortTrendEmaPeriod,
                VolumeAvgPeriod      = overrides.Tqqq_VolumeAvgPeriod      ?? _basePatternSettings.Tqqq200Sma.VolumeAvgPeriod,
                MinVolumeRatio       = overrides.Tqqq_MinVolumeRatio       ?? _basePatternSettings.Tqqq200Sma.MinVolumeRatio,
                AtrStopMultiplier    = overrides.Tqqq_AtrStopMultiplier    ?? _basePatternSettings.Tqqq200Sma.AtrStopMultiplier,
                AtrTargetMultiplier  = overrides.Tqqq_AtrTargetMultiplier  ?? _basePatternSettings.Tqqq200Sma.AtrTargetMultiplier
            }
        };
    }

    /// <summary>
    /// 선택된 패턴 타입에 해당하는 디텍터 목록을 반환합니다.
    /// 파라미터 오버라이드가 있으면 오버라이드된 설정으로 새 디텍터 인스턴스를 생성합니다.
    /// </summary>
    private List<IPatternDetector> BuildDetectors(List<PatternType> patterns, PatternParameterOverrides? overrides)
    {
        if (overrides == null)
            return _detectors.Where(d => patterns.Contains(d.PatternType)).ToList();

        var opts = new OptionsWrapper<PatternSettings>(ApplyOverrides(overrides));
        var allDetectors = new List<IPatternDetector>
        {
            new GapUpPullbackDetector(_indicators, opts),
            new BreakoutDetector(_indicators, opts),
            new VwapReversionDetector(_indicators, opts),
            new RsiMeanReversionDetector(_indicators, opts),
            new TrendPullbackDetector(_indicators, opts),
            new OrbDetector(_indicators, opts),
            new VolumeSpikeContinuationDetector(_indicators, opts),
            new EarningsDriftDetector(_indicators, opts),
            new IndexRegimeFilterDetector(_indicators, opts),
            new VolatilityExpansionDetector(_indicators, opts),
            new MomentumReversalDetector(_indicators, opts),
            new MultiTimeframeTrendDetector(_indicators, opts),
            new MeanReversionChannelDetector(_indicators, opts),
            new Rsi2BollingerDetector(_indicators, opts),
            new VolatilityBreakoutDetector(_indicators, opts),
            new Tqqq200SmaDetector(_indicators, opts)
        };
        return allDetectors.Where(d => patterns.Contains(d.PatternType)).ToList();
    }

    public async Task<BacktestResult> RunAsync(BacktestRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("백테스트 시작: {Symbols} ({From:d} ~ {To:d}) [타임프레임: {TimeFrame}]",
            string.Join(", ", request.Symbols), request.From, request.To, request.TimeFrame);

        var dataFeed = await _dataFeedFactory.GetServiceAsync(ct);
        var regimeByDate = await BuildRegimeMapAsync(dataFeed, request.From, request.To, ct);
        if (regimeByDate == null) return new BacktestResult();

        var activeDetectors = BuildDetectors(request.Patterns, request.ParameterOverrides);

        if (activeDetectors.Count == 0)
        {
            _logger.LogWarning("선택된 패턴이 없습니다");
            return new BacktestResult();
        }

        // request에 지정된 리스크 파라미터를 우선 사용하고, 없으면 appsettings.json 기본값 사용
        var riskParams = new RiskParams(
            RiskPerTradePercent: request.RiskPerTradePercent ?? _tradingSettings.RiskPerTradePercent,
            DailyLossLimitPercent: request.DailyLossLimitPercent ?? _tradingSettings.DailyLossLimitPercent,
            MaxTotalPositions: request.MaxTotalPositions ?? _tradingSettings.MaxTotalPositions,
            MaxPositionsPerSector: request.MaxPositionsPerSector ?? _tradingSettings.MaxPositionsPerSector
        );

        var exitParams = new ExitParams(
            EnableTrailingStop:        request.EnableTrailingStop,
            TrailingStopAtrMultiplier: request.TrailingStopAtrMultiplier,
            MaxHoldingBars:            request.MaxHoldingBars,
            EnablePartialProfit:       request.EnablePartialProfit,
            PartialProfitRMultiple:    request.PartialProfitRMultiple);

        // Run core backtest
        var result = await RunCoreAsync(
            request.Symbols, dataFeed, activeDetectors, regimeByDate,
            request.From, request.To, request.InitialCapital,
            request.SlippagePercent, request.CommissionPerTrade,
            request.TimeFrame, riskParams, exitParams, ct);

        result.UsedTimeFrame = request.TimeFrame;

        // Walk-Forward analysis
        if (request.EnableWalkForward)
        {
            result.WalkForward = await RunWalkForwardAsync(
                request, dataFeed, activeDetectors, regimeByDate, riskParams, ct);
        }

        // Monte Carlo simulation
        if (request.EnableMonteCarlo && result.Trades.Count >= 2)
        {
            result.MonteCarlo = RunMonteCarlo(
                result.Trades, request.InitialCapital, request.MonteCarloSimulations);
        }

        _logger.LogInformation(
            "백테스트 완료: {Trades}건 거래, 수익률 {Return:P2}, 최대 낙폭 {Drawdown:P2}, 샤프 비율 {Sharpe:F2}",
            result.TotalTrades, result.TotalReturnPercent, result.MaxDrawdown, result.SharpeRatio);

        return result;
    }

    /// <summary>
    /// Core backtest logic extracted for reuse by walk-forward.
    /// riskParams가 null이면 appsettings.json 기본값을 사용합니다.
    /// </summary>
    internal async Task<BacktestResult> RunCoreAsync(
        List<string> symbols,
        IDataFeedService dataFeed,
        List<IPatternDetector> detectors,
        Dictionary<DateOnly, MarketRegime> regimeByDate,
        DateTime from, DateTime to,
        decimal initialCapital,
        decimal slippagePercent, decimal commissionPerTrade,
        TimeFrame timeFrame = TimeFrame.Daily,
        RiskParams? riskParams = null,
        ExitParams? exitParams = null,
        CancellationToken ct = default)
    {
        // riskParams가 없으면 appsettings.json 기본값으로 구성
        riskParams ??= new RiskParams(
            RiskPerTradePercent: _tradingSettings.RiskPerTradePercent,
            DailyLossLimitPercent: _tradingSettings.DailyLossLimitPercent,
            MaxTotalPositions: _tradingSettings.MaxTotalPositions,
            MaxPositionsPerSector: _tradingSettings.MaxPositionsPerSector
        );

        var allTrades = new List<TradeRecord>();
        var warnings = new List<string>();
        DateTime? actualDataFrom = null;

        foreach (var symbol in symbols)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var (trades, symWarning, symDataFrom) = await BacktestSymbolAsync(
                    symbol, dataFeed, detectors, regimeByDate,
                    from, to, initialCapital, slippagePercent, commissionPerTrade,
                    timeFrame, riskParams, exitParams, ct);
                allTrades.AddRange(trades);

                if (symWarning != null)
                    warnings.Add(symWarning);

                // 가장 이른 실제 데이터 시작일 추적 (클램핑이 발생한 경우)
                if (symDataFrom.HasValue)
                {
                    if (!actualDataFrom.HasValue || symDataFrom.Value < actualDataFrom.Value)
                        actualDataFrom = symDataFrom;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Symbol} 백테스트 중 오류 발생", symbol);
                warnings.Add($"{symbol}: 백테스트 중 오류 발생 — {ex.Message}");
            }
        }

        allTrades = allTrades.OrderBy(t => t.EntryTime).ToList();

        // Compute equity curve, drawdown, and cost totals
        var equity = initialCapital;
        var peakEquity = equity;
        var maxDrawdown = 0m;
        var totalSlippage = 0m;
        var totalCommission = 0m;
        var equityCurve = new List<EquityPoint> { new(from, initialCapital) };

        foreach (var trade in allTrades)
        {
            // Slippage cost: applies to both entry and exit (adverse direction)
            var slippageCost = (trade.EntryPrice + trade.ExitPrice) * (slippagePercent / 100m) * trade.Quantity;
            var tradePnl = trade.PnL - slippageCost - commissionPerTrade;

            // Update trade with adjusted PnL
            trade.PnL = tradePnl;
            trade.PnLPercent = trade.EntryPrice > 0
                ? tradePnl / (trade.EntryPrice * trade.Quantity)
                : 0;

            totalSlippage += slippageCost;
            totalCommission += commissionPerTrade;

            equity += tradePnl;
            if (equity > peakEquity) peakEquity = equity;
            var drawdown = peakEquity > 0 ? (peakEquity - equity) / peakEquity : 0;
            if (drawdown > maxDrawdown) maxDrawdown = drawdown;

            equityCurve.Add(new EquityPoint(trade.ExitTime, equity));
        }

        var perPatternStats = ComputePerPatternStats(allTrades);
        var totalReturn = equity - initialCapital;
        var totalReturnPct = initialCapital > 0 ? totalReturn / initialCapital : 0;
        var winCount = allTrades.Count(t => t.IsWin);
        var overallWinRate = allTrades.Count > 0 ? (decimal)winCount / allTrades.Count : 0;
        var sharpe = ComputeSharpeRatio(allTrades, timeFrame);

        return new BacktestResult
        {
            Trades = allTrades,
            TotalReturn = totalReturn,
            TotalReturnPercent = totalReturnPct,
            MaxDrawdown = maxDrawdown,
            SharpeRatio = sharpe,
            TotalTrades = allTrades.Count,
            OverallWinRate = overallWinRate,
            PerPatternStats = perPatternStats,
            EquityCurve = equityCurve,
            TotalSlippageCost = totalSlippage,
            TotalCommissionCost = totalCommission,
            Warnings = warnings,
            ActualDataFrom = actualDataFrom
        };
    }

    #region Walk-Forward Analysis

    private async Task<WalkForwardResult> RunWalkForwardAsync(
        BacktestRequest request,
        IDataFeedService dataFeed,
        List<IPatternDetector> detectors,
        Dictionary<DateOnly, MarketRegime> regimeByDate,
        RiskParams riskParams,
        CancellationToken ct)
    {
        _logger.LogInformation("Walk-Forward 분석 시작 (IS:{IS}개월, OOS:{OOS}개월)",
            request.WalkForwardInSampleMonths, request.WalkForwardOutOfSampleMonths);

        // Walk-forward reuses the same exit params as the parent request
        var wfExitParams = new ExitParams(
            EnableTrailingStop:        request.EnableTrailingStop,
            TrailingStopAtrMultiplier: request.TrailingStopAtrMultiplier,
            MaxHoldingBars:            request.MaxHoldingBars,
            EnablePartialProfit:       request.EnablePartialProfit,
            PartialProfitRMultiple:    request.PartialProfitRMultiple);

        var windows = new List<WalkForwardWindow>();
        var windowStart = request.From;
        var totalMonths = request.WalkForwardInSampleMonths + request.WalkForwardOutOfSampleMonths;

        while (windowStart.AddMonths(totalMonths) <= request.To)
        {
            ct.ThrowIfCancellationRequested();

            var isFrom = windowStart;
            var isTo = windowStart.AddMonths(request.WalkForwardInSampleMonths);
            var oosFrom = isTo;
            var oosTo = isTo.AddMonths(request.WalkForwardOutOfSampleMonths);

            // Clamp OOS end to request end
            if (oosTo > request.To) oosTo = request.To;

            // Run IS backtest (동일한 리스크 파라미터 전달)
            var isResult = await RunCoreAsync(
                request.Symbols, dataFeed, detectors, regimeByDate,
                isFrom, isTo, request.InitialCapital,
                request.SlippagePercent, request.CommissionPerTrade,
                request.TimeFrame, riskParams, wfExitParams, ct);

            // Run OOS backtest (동일한 리스크 파라미터 전달)
            var oosResult = await RunCoreAsync(
                request.Symbols, dataFeed, detectors, regimeByDate,
                oosFrom, oosTo, request.InitialCapital,
                request.SlippagePercent, request.CommissionPerTrade,
                request.TimeFrame, riskParams, wfExitParams, ct);

            var efficiency = isResult.TotalReturnPercent != 0
                ? oosResult.TotalReturnPercent / isResult.TotalReturnPercent
                : 0;

            windows.Add(new WalkForwardWindow
            {
                InSampleFrom = isFrom,
                InSampleTo = isTo,
                OutOfSampleFrom = oosFrom,
                OutOfSampleTo = oosTo,
                InSampleTrades = isResult.TotalTrades,
                InSampleReturn = isResult.TotalReturn,
                InSampleReturnPercent = isResult.TotalReturnPercent,
                OutOfSampleTrades = oosResult.TotalTrades,
                OutOfSampleReturn = oosResult.TotalReturn,
                OutOfSampleReturnPercent = oosResult.TotalReturnPercent,
                OutOfSampleMaxDrawdown = oosResult.MaxDrawdown,
                Efficiency = efficiency
            });

            // Slide forward by OOS window size
            windowStart = oosTo;
        }

        // Aggregate OOS stats
        var allOosTrades = windows.Sum(w => w.OutOfSampleTrades);
        var allOosReturn = windows.Sum(w => w.OutOfSampleReturn);
        var totalIsReturn = windows.Sum(w => w.InSampleReturnPercent);
        var totalOosReturn = windows.Sum(w => w.OutOfSampleReturnPercent);
        var avgOosReturnPct = windows.Count > 0
            ? windows.Average(w => w.OutOfSampleReturnPercent) : 0;
        var avgOosMaxDd = windows.Count > 0
            ? windows.Max(w => w.OutOfSampleMaxDrawdown) : 0;

        // Compute aggregate OOS win rate from window-level returns
        var oosWinWindows = windows.Count(w => w.OutOfSampleReturnPercent > 0);
        var oosWinRate = windows.Count > 0 ? (decimal)oosWinWindows / windows.Count : 0;

        var wfEfficiency = totalIsReturn != 0 ? totalOosReturn / totalIsReturn : 0;

        _logger.LogInformation(
            "Walk-Forward 완료: {Count}개 윈도우, OOS 평균 수익률 {Avg:P2}, WF 효율 {Eff:P2}",
            windows.Count, avgOosReturnPct, wfEfficiency);

        return new WalkForwardResult
        {
            Windows = windows,
            AggregateOosReturn = allOosReturn,
            AggregateOosReturnPercent = avgOosReturnPct,
            AggregateOosMaxDrawdown = avgOosMaxDd,
            AggregateOosWinRate = oosWinRate,
            AggregateOosSharpe = 0, // simplified — individual window sharpe is noisy
            WalkForwardEfficiency = wfEfficiency
        };
    }

    #endregion

    #region Monte Carlo Simulation

    internal static MonteCarloResult RunMonteCarlo(
        List<TradeRecord> trades, decimal initialCapital, int simulations)
    {
        var tradePnls = trades.Select(t => t.PnL).ToArray();
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

        var lossCount = finalEquities.Count(e => e < initialCapital);

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

    #endregion

    #region Regime Map

    internal async Task<Dictionary<DateOnly, MarketRegime>?> BuildRegimeMapAsync(
        IDataFeedService dataFeed, DateTime from, DateTime to, CancellationToken ct)
    {
        var spyFrom = from.AddDays(-400);
        List<OhlcvBar> spyBars;
        try
        {
            spyBars = await dataFeed.GetHistoricalBarsAsync("SPY", TimeFrame.Daily, spyFrom, to, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SPY 데이터 조회 실패");
            return null;
        }

        if (spyBars.Count < 200)
        {
            _logger.LogWarning("SPY 데이터 부족: {Count}개 (최소 200개 필요)", spyBars.Count);
            return null;
        }

        var spyCloses = spyBars.Select(b => b.Close).ToArray();
        var spy200Sma = _indicators.SMA(spyCloses, 200);
        var regimeByDate = new Dictionary<DateOnly, MarketRegime>();

        for (int i = 0; i < spyBars.Count; i++)
        {
            var date = DateOnly.FromDateTime(spyBars[i].Timestamp);
            regimeByDate[date] = new MarketRegime
            {
                SpyAbove200Ma = spy200Sma[i] > 0 && spyBars[i].Close > spy200Sma[i],
                SpyPrice = spyBars[i].Close,
                Spy200Ma = spy200Sma[i],
                RegimeLabel = spy200Sma[i] > 0 && spyBars[i].Close > spy200Sma[i] ? "강세" : "약세",
                AsOf = spyBars[i].Timestamp
            };
        }

        return regimeByDate;
    }

    #endregion

    #region Symbol-Level Backtest

    /// <returns>
    /// (trades, warningMessage, actualDataFrom)
    ///   - warningMessage: null이면 정상, 문자열이면 사용자에게 표시할 경고
    ///   - actualDataFrom: 분봉 기간 클램핑이 발생한 경우 실제 데이터 시작일, 없으면 null
    /// </returns>
    private async Task<(List<TradeRecord> trades, string? warning, DateTime? actualDataFrom)> BacktestSymbolAsync(
        string symbol,
        IDataFeedService dataFeed,
        List<IPatternDetector> detectors,
        Dictionary<DateOnly, MarketRegime> regimeByDate,
        DateTime from, DateTime to,
        decimal capital,
        decimal slippagePercent, decimal commissionPerTrade,
        TimeFrame timeFrame,
        RiskParams riskParams,
        ExitParams? exitParams,
        CancellationToken ct)
    {
        // 타임프레임별 워밍업 lookback: 일봉은 400일, 분봉은 API 기간 제한 내에서 조정
        var warmupDays = timeFrame switch
        {
            TimeFrame.OneMinute     => 2,   // 1m: 최대 7일 제한, 워밍업은 2일
            TimeFrame.FiveMinute    => 10,  // 5m: 최대 60일 제한
            TimeFrame.FifteenMinute => 15,  // 15m: 최대 60일 제한
            TimeFrame.Daily         => 400,
            TimeFrame.Weekly        => 400,
            _                       => 400
        };
        var fetchFrom = from.AddDays(-warmupDays);
        var bars = await dataFeed.GetHistoricalBarsAsync(symbol, timeFrame, fetchFrom, to, ct);

        if (bars.Count < MinWarmupBars)
        {
            string warning;
            if (timeFrame is TimeFrame.OneMinute or TimeFrame.FiveMinute or TimeFrame.FifteenMinute)
            {
                var limitDays = timeFrame == TimeFrame.OneMinute ? 7 : 60;
                warning = $"{symbol}: 분봉 데이터 부족 ({bars.Count}개). " +
                          $"Yahoo Finance {GetTimeFrameLabel(timeFrame)} 데이터는 최근 {limitDays}일만 제공됩니다. " +
                          $"시작일을 오늘 기준 {limitDays}일 이내로 설정하세요.";
            }
            else
            {
                warning = $"{symbol}: 데이터 부족 ({bars.Count}개, 최소 {MinWarmupBars}개 필요). " +
                          "기간을 늘리거나 다른 종목을 선택하세요.";
            }
            _logger.LogWarning("{Symbol}: 데이터 부족 ({Count}개, timeFrame={TimeFrame})", symbol, bars.Count, timeFrame);
            return ([], warning, null);
        }

        // 실제 첫 번째 바의 날짜를 기록 (요청 날짜와 다를 수 있음)
        var actualDataFrom = bars.Count > 0 ? (DateTime?)bars[0].Timestamp : null;

        // Pre-compute full ATR array for the symbol (period=14)
        // Used at entry to snapshot ATR and for trailing stop calculations
        var barsArray = bars.ToArray();
        var atrArray = _indicators.ATR(barsArray, 14);

        // Pre-compute SMA200 for regime-based strategies (Tqqq200Sma dynamic exit)
        var closesArray = barsArray.Select(b => b.Close).ToArray();
        var sma200Array = _indicators.SMA(closesArray, 200);

        var ep = exitParams ?? ExitParams.Default;

        var trades = new List<TradeRecord>();
        OpenPosition? openPosition = null;
        // request에서 전달된 리스크 파라미터 사용 (UI에서 설정한 값 우선)
        var riskPerTrade = riskParams.RiskPerTradePercent;
        var maxTotalPositions = riskParams.MaxTotalPositions;
        var maxPositionsPerSector = riskParams.MaxPositionsPerSector;

        for (int i = MinWarmupBars; i < bars.Count; i++)
        {
            var currentBar = bars[i];
            if (currentBar.Timestamp < from) continue;

            ct.ThrowIfCancellationRequested();

            var currentDate = DateOnly.FromDateTime(currentBar.Timestamp);
            var regime = GetRegimeForDate(currentDate, regimeByDate);

            // ── Exit logic for open position ──────────────────────────────
            if (openPosition != null)
            {
                var currentAtr = atrArray[i] > 0 ? atrArray[i] : openPosition.EntryAtr;
                var barsSinceEntry = i - openPosition.EntryBarIndex;
                var pep = PatternExitProfile.For(openPosition.PatternType);

                // 1. Update highest high since entry (for Chandelier trailing stop)
                if (currentBar.High > openPosition.HighestHighSinceEntry)
                    openPosition.HighestHighSinceEntry = currentBar.High;

                // 2. Breakeven stop: once price moves 1.5 ATR above entry, stop → entry
                if (!openPosition.BreakevenApplied && openPosition.EntryAtr > 0)
                {
                    var breakevenThreshold = openPosition.EntryPrice + openPosition.EntryAtr * 1.5m;
                    if (currentBar.Close >= breakevenThreshold)
                    {
                        openPosition.StopLoss = Math.Max(openPosition.StopLoss, openPosition.EntryPrice);
                        openPosition.BreakevenApplied = true;
                    }
                }

                // 3. Trailing stop (Chandelier): pattern-specific activation R & ATR multiplier
                if (pep.EnableTrailingStop)
                {
                    var activationTarget = openPosition.EntryPrice
                        + openPosition.RiskDistance * pep.TrailingActivationR;
                    if (!openPosition.TrailingStopActivated && currentBar.Close >= activationTarget)
                        openPosition.TrailingStopActivated = true;

                    if (openPosition.TrailingStopActivated && currentAtr > 0)
                    {
                        var chandelier = openPosition.HighestHighSinceEntry
                            - currentAtr * pep.TrailingStopAtrMultiplier;
                        if (chandelier > openPosition.StopLoss)
                            openPosition.StopLoss = chandelier;
                    }
                }

                // 4. Partial profit: pattern-specific R-multiple threshold
                if (pep.EnablePartialProfit && !openPosition.PartialProfitTaken)
                {
                    var partialProfitTarget = openPosition.EntryPrice
                        + openPosition.RiskDistance * pep.PartialProfitRMultiple;
                    if (currentBar.High >= partialProfitTarget && openPosition.Quantity >= 2)
                    {
                        var halfQty = openPosition.Quantity / 2;
                        var remainQty = openPosition.Quantity - halfQty;

                        trades.Add(CreateTradeRecordWithQty(
                            symbol, openPosition, partialProfitTarget,
                            currentBar.Timestamp, $"부분 익절({pep.PartialProfitRMultiple}R)", halfQty));

                        openPosition.PartialProfitTaken = true;
                        openPosition = new OpenPosition
                        {
                            PatternType              = openPosition.PatternType,
                            EntryPrice               = openPosition.EntryPrice,
                            OriginalStop             = openPosition.OriginalStop,
                            StopLoss                 = Math.Max(openPosition.StopLoss, openPosition.EntryPrice),
                            Target                   = openPosition.Target,
                            Quantity                 = remainQty,
                            EntryTime                = openPosition.EntryTime,
                            EntryBarIndex            = openPosition.EntryBarIndex,
                            EntryAtr                 = openPosition.EntryAtr,
                            HighestHighSinceEntry    = openPosition.HighestHighSinceEntry,
                            TrailingStopActivated    = openPosition.TrailingStopActivated,
                            BreakevenApplied         = true,
                            PartialProfitTaken       = true,
                            RiskDistance             = openPosition.RiskDistance
                        };
                    }
                }

                // 5. Regime-based dynamic exit (Tqqq200Sma: SMA200 trailing stop)
                if (openPosition.PatternType == PatternType.Tqqq200Sma
                    && sma200Array[i] > 0)
                {
                    var dynamicSmaStop = sma200Array[i] * 0.99m;
                    if (dynamicSmaStop > openPosition.StopLoss)
                        openPosition.StopLoss = dynamicSmaStop;
                }

                // 6. Check exits (stop, target, time-based) — pattern profile controls each
                decimal exitPrice = 0;
                string exitReason = "";

                if (currentBar.Low <= openPosition.StopLoss)
                {
                    exitPrice = openPosition.StopLoss;
                    exitReason = openPosition.PatternType == PatternType.Tqqq200Sma
                        ? "SMA200 이탈"
                        : openPosition.BreakevenApplied || openPosition.TrailingStopActivated
                            ? "트레일링 손절"
                            : "손절";
                }
                else if (pep.EnableTargetExit && currentBar.High >= openPosition.Target)
                {
                    exitPrice = openPosition.Target;
                    exitReason = "목표 도달";
                }
                else if (pep.EnableTimeExit && barsSinceEntry >= pep.MaxHoldingBars)
                {
                    exitPrice = currentBar.Close;
                    exitReason = $"시간 청산({pep.MaxHoldingBars}봉)";
                }

                if (exitPrice > 0)
                {
                    trades.Add(CreateTradeRecordWithQty(symbol, openPosition, exitPrice,
                        currentBar.Timestamp, exitReason, openPosition.Quantity));
                    openPosition = null;
                }
            }

            if (openPosition != null) continue;

            // Daily bars → 260 is enough for 200-SMA warmup.
            // Intraday bars need more: 5-min has 78 bars/day, so 260 bars = ~3.3 days.
            // Patterns like RSI(2)+Bollinger need 205+ bars and the SMA warmup needs actual
            // trading-day data, so we need a larger window for intraday.
            var maxWindow = timeFrame switch
            {
                TimeFrame.OneMinute     => 800,  // ~2 trading days
                TimeFrame.FiveMinute    => 800,  // ~10 trading days
                TimeFrame.FifteenMinute => 600,  // ~11 trading days
                _                       => 260
            };
            var windowSize = Math.Min(i + 1, maxWindow);
            var windowBars = bars.Skip(i + 1 - windowSize).Take(windowSize).ToArray();

            foreach (var detector in detectors)
            {
                try
                {
                    var signal = await detector.DetectAsync(symbol, windowBars, regime, ct);
                    if (signal == null) continue;
                    if (signal.EntryPrice <= 0 || signal.StopLossPrice <= 0) continue;

                    var stopDistance = Math.Abs(signal.EntryPrice - signal.StopLossPrice);
                    if (stopDistance <= 0) continue;

                    int quantity;

                    if (detector.PatternType == PatternType.Tqqq200Sma)
                    {
                        // 레짐 전략: 자본 전체 투입 (원본 전략의 "풀매수" 구현)
                        quantity = (int)(capital * 0.95m / signal.EntryPrice);
                        if (quantity <= 0) quantity = 1;
                    }
                    else
                    {
                        var riskAmount = capital * riskPerTrade;
                        quantity = (int)(riskAmount / stopDistance);
                        if (quantity <= 0) quantity = 1;

                        // MaxTotalPositions 기반 단일 포지션 최대 자본 비율 (1/N)
                        var maxPositionCapitalRatio = maxTotalPositions > 0
                            ? 1.0m / maxTotalPositions
                            : 0.10m;
                        var maxQty = (int)(capital * maxPositionCapitalRatio / signal.EntryPrice);
                        if (maxQty > 0) quantity = Math.Min(quantity, maxQty);
                    }

                    // Snapshot ATR at entry (fallback to stop distance if ATR not yet ready)
                    var entryAtr = atrArray[i] > 0 ? atrArray[i] : stopDistance;

                    openPosition = new OpenPosition
                    {
                        PatternType           = detector.PatternType,
                        EntryPrice            = signal.EntryPrice,
                        OriginalStop          = signal.StopLossPrice,
                        StopLoss              = signal.StopLossPrice,
                        Target                = signal.TargetPrice,
                        Quantity              = quantity,
                        EntryTime             = currentBar.Timestamp,
                        EntryBarIndex         = i,
                        EntryAtr              = entryAtr,
                        HighestHighSinceEntry = currentBar.High,
                        RiskDistance          = stopDistance
                    };

                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "{Symbol} 패턴 {Pattern} 감지 실패",
                        symbol, detector.PatternType);
                }
            }
        }

        // Close remaining position at last bar's close
        if (openPosition != null && bars.Count > 0)
        {
            var lastBar = bars[^1];
            trades.Add(CreateTradeRecordWithQty(symbol, openPosition, lastBar.Close,
                lastBar.Timestamp, "기간 종료", openPosition.Quantity));
        }

        _logger.LogInformation("{Symbol}: {Count}건 거래 완료", symbol, trades.Count);
        return (trades, null, actualDataFrom);
    }

    #endregion

    #region Helpers

    /// <summary>백테스트 실행 시 사용할 리스크 파라미터 묶음</summary>
    internal sealed record RiskParams(
        decimal RiskPerTradePercent,
        decimal DailyLossLimitPercent,
        int MaxTotalPositions,
        int MaxPositionsPerSector
    );

    /// <summary>
    /// Advanced exit strategy parameters passed through the backtest call chain.
    /// All fields mirror the corresponding BacktestRequest properties.
    /// </summary>
    internal sealed record ExitParams(
        bool EnableTrailingStop,
        decimal TrailingStopAtrMultiplier,
        int MaxHoldingBars,
        bool EnablePartialProfit,
        decimal PartialProfitRMultiple)
    {
        /// <summary>
        /// Conservative defaults used when no BacktestRequest is available
        /// (e.g. walk-forward sub-runs that don't carry exit params).
        /// </summary>
        public static readonly ExitParams Default = new(
            EnableTrailingStop:        true,
            TrailingStopAtrMultiplier: 2.5m,
            MaxHoldingBars:            20,
            EnablePartialProfit:       true,
            PartialProfitRMultiple:    2.0m);
    };

    /// <summary>
    /// Pattern-specific exit profile. Each pattern category has optimal holding period,
    /// trailing stop behavior, and partial profit settings tuned to its trading style.
    /// </summary>
    internal sealed record PatternExitProfile(
        int MaxHoldingBars,
        bool EnableTrailingStop,
        decimal TrailingStopAtrMultiplier,
        decimal TrailingActivationR,
        bool EnablePartialProfit,
        decimal PartialProfitRMultiple,
        bool EnableTargetExit,
        bool EnableTimeExit)
    {
        public static PatternExitProfile For(PatternType pt) => pt switch
        {
            // ── Day Trading (1~5봉): 빠른 청산, 트레일링 불필요 ──
            PatternType.GapUpPullback          => new( 3, false, 0m,   0m,   true,  2.0m, true,  true),
            PatternType.VwapReversion          => new( 3, false, 0m,   0m,   true,  1.5m, true,  true),
            PatternType.OpeningRangeBreakout   => new( 3, false, 0m,   0m,   true,  2.0m, true,  true),
            PatternType.VolumeSpikeContinuation=> new( 5, true,  1.5m, 1.0m, false, 0m,   true,  true),
            PatternType.VolatilityBreakout     => new( 5, true,  2.0m, 1.0m, false, 0m,   true,  true),

            // ── Mean Reversion (5~7봉): 평균 회귀, 트레일링 최소화 ──
            PatternType.RsiMeanReversion       => new( 5, false, 0m,   0m,   true,  1.5m, true,  true),
            PatternType.VolatilityExpansion    => new( 7, true,  2.0m, 1.5m, true,  2.0m, true,  true),
            PatternType.MeanReversionChannel   => new( 5, false, 0m,   0m,   true,  1.5m, true,  true),
            PatternType.Rsi2Bollinger          => new( 5, false, 0m,   0m,   true,  1.5m, true,  true),

            // ── Swing Trading (10~15봉): 중기 보유, 트레일링 적극 활용 ──
            PatternType.Breakout               => new(15, true,  2.5m, 1.5m, true,  2.5m, true,  true),
            PatternType.MomentumReversal       => new(10, true,  2.5m, 1.5m, true,  2.0m, true,  true),
            PatternType.IndexRegimeFilter      => new(15, true,  2.5m, 1.5m, true,  2.0m, true,  true),

            // ── Position/Trend (20~30봉): 장기 보유, 넓은 트레일링 ──
            PatternType.TrendPullback          => new(20, true,  3.0m, 2.0m, true,  3.0m, true,  true),
            PatternType.EarningsDrift          => new(20, true,  2.5m, 1.5m, true,  2.0m, true,  true),
            PatternType.MultiTimeframeTrend    => new(30, true,  3.0m, 2.0m, true,  3.0m, true,  true),

            // ── Regime (SMA200 이탈까지 무제한 보유) ──
            PatternType.Tqqq200Sma             => new(999, false, 0m,  0m,   false, 0m,   false, false),

            _ => new(20, true, 2.5m, 1.0m, true, 2.0m, true, true)
        };
    };

    private static string GetTimeFrameLabel(TimeFrame tf) => tf switch
    {
        TimeFrame.OneMinute     => "1분봉",
        TimeFrame.FiveMinute    => "5분봉",
        TimeFrame.FifteenMinute => "15분봉",
        TimeFrame.Daily         => "일봉",
        TimeFrame.Weekly        => "주봉",
        _                       => tf.ToString()
    };

    private static MarketRegime GetRegimeForDate(
        DateOnly date, Dictionary<DateOnly, MarketRegime> regimeByDate)
    {
        if (regimeByDate.TryGetValue(date, out var regime))
            return regime;

        var closest = regimeByDate
            .Where(kv => kv.Key <= date)
            .OrderByDescending(kv => kv.Key)
            .Select(kv => kv.Value)
            .FirstOrDefault();

        return closest ?? new MarketRegime
        {
            SpyAbove200Ma = true,
            RegimeLabel = "알 수 없음"
        };
    }

    /// <summary>
    /// Creates a TradeRecord for a given exit. The quantity parameter allows partial-profit
    /// trades to record only the closed portion rather than the full position size.
    /// </summary>
    private static TradeRecord CreateTradeRecordWithQty(
        string symbol, OpenPosition pos, decimal exitPrice,
        DateTime exitTime, string exitReason, int qty)
    {
        var pnl = (exitPrice - pos.EntryPrice) * qty;
        var pnlPct = pos.EntryPrice > 0
            ? (exitPrice - pos.EntryPrice) / pos.EntryPrice
            : 0;

        return new TradeRecord
        {
            Symbol = symbol,
            PatternType = pos.PatternType,
            EntryPrice = pos.EntryPrice,
            ExitPrice = exitPrice,
            Quantity = qty,
            EntryTime = pos.EntryTime,
            ExitTime = exitTime,
            PnL = pnl,
            PnLPercent = pnlPct,
            ExitReason = exitReason
        };
    }

    internal static Dictionary<PatternType, PatternStats> ComputePerPatternStats(
        List<TradeRecord> trades)
    {
        var stats = new Dictionary<PatternType, PatternStats>();

        foreach (var group in trades.GroupBy(t => t.PatternType))
        {
            var all = group.ToList();
            var wins = all.Where(t => t.IsWin).ToList();
            var losses = all.Where(t => !t.IsWin).ToList();

            stats[group.Key] = new PatternStats
            {
                PatternType = group.Key,
                SampleSize = all.Count,
                WinRate = all.Count > 0 ? (decimal)wins.Count / all.Count : 0,
                AvgWinPercent = wins.Count > 0 ? wins.Average(t => t.PnLPercent) : 0,
                AvgLossPercent = losses.Count > 0 ? Math.Abs(losses.Average(t => t.PnLPercent)) : 0,
                MaxDrawdownPercent = ComputeGroupDrawdown(all),
                LastUpdated = DateTime.UtcNow
            };
        }

        return stats;
    }

    private static decimal ComputeGroupDrawdown(List<TradeRecord> trades)
    {
        var cumPnl = 0m;
        var peak = 0m;
        var maxDd = 0m;

        foreach (var t in trades.OrderBy(t => t.EntryTime))
        {
            cumPnl += t.PnLPercent;
            if (cumPnl > peak) peak = cumPnl;
            var dd = peak - cumPnl;
            if (dd > maxDd) maxDd = dd;
        }

        return maxDd;
    }

    internal static decimal ComputeSharpeRatio(
        List<TradeRecord> trades,
        TimeFrame timeFrame = TimeFrame.Daily)
    {
        if (trades.Count < 2) return 0;

        var returns = trades.Select(t => t.PnLPercent).ToList();
        var avgReturn = returns.Average();
        var variance = returns.Average(r => (r - avgReturn) * (r - avgReturn));
        var stdDev = (decimal)Math.Sqrt((double)variance);

        if (stdDev <= 0) return 0;

        // 타임프레임별 연환산 계수: 일봉=252거래일, 분봉=분당 거래 수 * 일 * 연
        // 여기서는 거래 건수 기반으로 스케일링하므로 봉 수 기준 annualization factor 사용
        var annualizationFactor = timeFrame switch
        {
            TimeFrame.OneMinute    => 252.0 * 390.0,   // 390분/거래일
            TimeFrame.FiveMinute   => 252.0 * 78.0,    // 78개 5분봉/거래일
            TimeFrame.FifteenMinute => 252.0 * 26.0,   // 26개 15분봉/거래일
            TimeFrame.Daily        => 252.0,
            TimeFrame.Weekly       => 52.0,
            _                      => 252.0
        };

        return avgReturn / stdDev * (decimal)Math.Sqrt(annualizationFactor / Math.Max(1, trades.Count));
    }

    private sealed class OpenPosition
    {
        // ── Immutable entry data ──────────────────────────────────────
        public PatternType PatternType { get; init; }
        public decimal EntryPrice { get; init; }
        public decimal OriginalStop { get; init; }   // original stop — never moves down
        public decimal Target { get; init; }
        public int Quantity { get; init; }
        public DateTime EntryTime { get; init; }
        public int EntryBarIndex { get; init; }      // bars[] index at entry, for time-based exit
        public decimal EntryAtr { get; init; }       // ATR value at entry bar, for breakeven calculation

        // ── Mutable tracking state ────────────────────────────────────

        /// <summary>Effective stop (max of original, breakeven, trailing). Updated each bar.</summary>
        public decimal StopLoss { get; set; }

        /// <summary>Highest high since entry (used by Chandelier trailing stop).</summary>
        public decimal HighestHighSinceEntry { get; set; }

        /// <summary>Whether the trailing stop has been activated (requires 1R profit first).</summary>
        public bool TrailingStopActivated { get; set; }

        /// <summary>Whether the breakeven stop has been applied.</summary>
        public bool BreakevenApplied { get; set; }

        /// <summary>Whether the 50% partial profit has already been taken.</summary>
        public bool PartialProfitTaken { get; set; }

        /// <summary>Risk distance (|entry - originalStop|). Pre-computed for R-multiple checks.</summary>
        public decimal RiskDistance { get; init; }
    }

    #endregion
}
