using Microsoft.EntityFrameworkCore;
using StockTrader.Configuration;
using StockTrader.Data;
using StockTrader.Data.Repositories;
using StockTrader.Services.Analysis;
using StockTrader.Services.Backtest;
using StockTrader.Services.Indicators;
using StockTrader.Services.Market;
using StockTrader.Services.ML;
using StockTrader.Services.Order;
using StockTrader.Services.Risk;
using StockTrader.Services.LiveParameter;
using StockTrader.Services.Signal;
using StockTrader.Services.Statistics;
using StockTrader.Services.Financial;
using StockTrader.Services.StrategyPreview;
using StockTrader.Application.StrategyPreview;
using StockTrader.Application.Execution;
using StockTrader.Application.Strategies;
using StockTrader.Application.Optimization;
using StockTrader.Application.SymbolProfiles;
using StockTrader.Application.Research;
using StockTrader.Application.Risk;
using StockTrader.Application.Portfolio;
using StockTrader.Services.Portfolio;
using StockTrader.Application.Statistics;
using StockTrader.Application.Signals;

namespace StockTrader.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddStockTraderServices(this IServiceCollection services,
        IConfiguration configuration,
        bool includeHostedServices = true)
    {
        // Configuration binding
        services.Configure<AlpacaSettings>(configuration.GetSection("Alpaca"));
        services.Configure<YahooFinanceSettings>(configuration.GetSection("YahooFinance"));
        services.AddOptions<TradingSettings>()
            .Bind(configuration.GetSection("Trading"))
            .Validate(settings => settings.DefaultAccountSize > 0, "DefaultAccountSize must be positive")
            .Validate(settings => settings.RiskPerTradePercent is > 0 and <= 1, "RiskPerTradePercent must be in (0, 1]")
            .Validate(settings => settings.DailyLossLimitPercent is > 0 and <= 1, "DailyLossLimitPercent must be in (0, 1]")
            .Validate(settings => settings.MaxPositionsPerSector > 0, "MaxPositionsPerSector must be positive")
            .Validate(settings => settings.MaxTotalPositions > 0, "MaxTotalPositions must be positive")
            .Validate(settings => settings.MinConfidence is >= 0 and <= 1, "MinConfidence must be in [0, 1]")
            .Validate(settings => settings.DataFetchIntervalSeconds > 0, "DataFetchIntervalSeconds must be positive")
            .Validate(settings => settings.RiskCheckIntervalSeconds > 0, "RiskCheckIntervalSeconds must be positive")
            .Validate(settings => settings.RiskMonitorMaxConsecutiveFailures > 0, "RiskMonitorMaxConsecutiveFailures must be positive")
            .Validate(settings => settings.RiskMonitorCooldownSeconds > 0, "RiskMonitorCooldownSeconds must be positive")
            .Validate(settings => settings.RiskHaltAlertIntervalMinutes > 0, "RiskHaltAlertIntervalMinutes must be positive")
            .Validate(settings => settings.EntryReconciliationIntervalSeconds > 0, "EntryReconciliationIntervalSeconds must be positive")
            .Validate(settings => settings.EntryReconciliationBatchSize > 0, "EntryReconciliationBatchSize must be positive")
            .Validate(settings => TimeSpan.TryParse(settings.MarketOpenET, out _), "MarketOpenET must be a valid time")
            .Validate(settings => TimeSpan.TryParse(settings.MarketCloseET, out _), "MarketCloseET must be a valid time")
            .ValidateOnStart();
        services.Configure<PatternSettings>(configuration.GetSection("Patterns"));
        services.Configure<NotificationSettings>(configuration.GetSection("Notification"));
        services.Configure<MLSettings>(configuration.GetSection("ML"));
        services.Configure<LsSecuritiesSettings>(configuration.GetSection("LsSecurities"));
        services.Configure<FinancialDataPipelineSettings>(configuration.GetSection("FinancialDataPipeline"));
        services.AddOptions<StockAnalysisSettings>()
            .Bind(configuration.GetSection("StockAnalysis"))
            .Validate(settings => settings.MaxParallelAnalyses > 0, "MaxParallelAnalyses must be positive")
            .Validate(settings => settings.AnalysisCacheSeconds > 0, "AnalysisCacheSeconds must be positive")
            .Validate(settings => settings.RegimeCacheMinutes > 0, "RegimeCacheMinutes must be positive")
            .Validate(settings => settings.StatisticsCacheMinutes > 0, "StatisticsCacheMinutes must be positive")
            .Validate(settings => settings.HistoryLookbackDays > 0, "HistoryLookbackDays must be positive")
            .Validate(settings => settings.MinimumHistoryBars > 0, "MinimumHistoryBars must be positive")
            .Validate(settings => settings.RegimeLookbackDays > 0, "RegimeLookbackDays must be positive")
            .Validate(
                settings => settings.MinimumRegimeBars >= StockIndicatorSnapshotFactory.LongTrendPeriod,
                $"MinimumRegimeBars must be at least {StockIndicatorSnapshotFactory.LongTrendPeriod}")
            .ValidateOnStart();

        // Database
        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseSqlite(configuration.GetConnectionString("DefaultConnection")));

        // Domain services
        services.AddDataServices(configuration);
        services.AddBrokerServices();
        services.AddPatternServices();
        services.AddNotificationServices();
        services.AddBackgroundServices(includeHostedServices);

        services.AddSingleton(TimeProvider.System);

        // Market Calendar (stateless - singleton)
        services.AddSingleton<IMarketCalendar, MarketCalendar>();

        // Indicators (stateless - singleton)
        services.AddSingleton<IIndicatorService, IndicatorService>();
        services.AddSingleton<StockIndicatorSnapshotFactory>();

        // ML Services
        services.AddSingleton<IMarketRegimeClassifier, MarketRegimeClassifier>();
        services.AddSingleton<ISignalScorer, SignalScorer>();
        services.AddSingleton<IMLModelTrainingService, MLModelTrainingService>();

        // Business Services
        services.AddScoped<IStatisticsService, StatisticsService>();
        services.AddScoped<IPatternStatisticsQuery, PatternStatisticsQuery>();
        services.AddScoped<ISignalListQuery, SignalListQuery>();
        services.AddScoped<ISignalService, SignalService>();
        services.AddSingleton<IRiskManagementService, MultiAccountRiskService>();
        services.AddScoped<IRiskOverviewQuery, RiskOverviewQuery>();
        services.AddScoped<IPortfolioPerformanceQuery, PortfolioPerformanceQuery>();
        services.AddScoped<IOpenPositionQuery, OpenPositionQuery>();
        services.AddScoped<ManualOrderWorkflow>();
        services.AddScoped<ILiveEntryExecutionCoordinator, LiveEntryExecutionCoordinator>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<ILivePositionExecutionCoordinator, LivePositionExecutionCoordinator>();
        services.AddScoped<ILiveOrderManagement, LiveOrderManagement>();
        services.AddScoped<LivePositionExecutionEvaluator>();
        services.AddScoped<CustomPatternManagementService>();
        services.AddScoped<ISymbolProfileStore, SymbolProfileStore>();
        services.AddScoped<SymbolProfileManagementService>();
        services.AddSingleton<ResearchUniverseQueryService>();
        services.AddSingleton<FinancialFactorQueryService>();
        services.AddSingleton<FinancialSnapshotImportService>();
        services.AddSingleton<FinancialSnapshotFileParser>();
        services.AddScoped<BacktestDataPreparer>();
        services.AddScoped<BacktestSignalEntryProcessor>();
        services.AddScoped<BacktestSimulationEngine>();
        services.AddScoped<BacktestPreparedSimulationRunner>();
        services.AddScoped<WalkForwardAnalysisRunner>();
        services.AddScoped<IOptimizationCandidateEvaluator, OptimizationCandidateEvaluator>();
        services.AddScoped<BacktestRegimeMapBuilder>();
        services.AddScoped<IOptimizationEvaluationContextPreparer, OptimizationEvaluationContextPreparer>();
        services.AddScoped<BacktestOptimizationService>();
        services.AddScoped<BacktestService>();
        services.AddScoped<IBacktestService>(sp => sp.GetRequiredService<BacktestService>());
        services.AddSingleton<PatternPreviewSimulationEngine>();
        services.AddScoped<IPatternPreviewService, PatternPreviewService>();
        services.AddScoped<ILiveParameterService, LiveParameterService>();
        services.AddScoped<IStockAnalysisService, StockAnalysisService>();

        return services;
    }
}
