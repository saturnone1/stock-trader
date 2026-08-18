using Microsoft.EntityFrameworkCore;
using StockTrader.Configuration;
using StockTrader.Data;
using StockTrader.Data.Repositories;
using StockTrader.Services.Analysis;
using StockTrader.Services.Account;
using StockTrader.Services.Backtest;
using StockTrader.Services.Indicators;
using StockTrader.Services.Market;
using StockTrader.Services.ML;
using StockTrader.Services.Order;
using StockTrader.Services.Risk;
using StockTrader.Application.Settings;
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
using StockTrader.Application.Reporting;
using StockTrader.Application.Dashboard;
using StockTrader.Application.Accounts;
using StockTrader.Application.Trading;
using StockTrader.Services.Dashboard;

namespace StockTrader.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddStockTraderServices(this IServiceCollection services,
        IConfiguration configuration,
        bool includeHostedServices = true)
    {
        // Configuration binding
        services.Configure<AlpacaSettings>(configuration.GetSection("Alpaca"));
        services.AddOptions<StreamingSettings>()
            .Bind(configuration.GetSection("Streaming"))
            .Validate(settings => settings.MaxReconnectAttempts >= 0,
                "Streaming MaxReconnectAttempts cannot be negative")
            .Validate(settings => settings.InitialReconnectDelaySeconds > 0,
                "Streaming InitialReconnectDelaySeconds must be positive")
            .Validate(settings => settings.MaxReconnectDelaySeconds
                    >= settings.InitialReconnectDelaySeconds,
                "Streaming MaxReconnectDelaySeconds must be at least the initial delay")
            .Validate(settings => settings.StatusStalenessSeconds > 0,
                "Streaming StatusStalenessSeconds must be positive")
            .Validate(settings => settings.BarFlushIntervalSeconds > 0,
                "Streaming BarFlushIntervalSeconds must be positive")
            .Validate(settings => settings.WatchlistSyncIntervalSeconds > 0,
                "Streaming WatchlistSyncIntervalSeconds must be positive")
            .Validate(settings => settings.BufferCapacity > 0,
                "Streaming BufferCapacity must be positive")
            .ValidateOnStart();
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
            .Validate(
                settings => settings.EntryReconciliationIntervalSeconds
                    is >= TradingSettings.MinimumEntryReconciliationIntervalSeconds
                    and <= TradingSettings.MaximumEntryReconciliationIntervalSeconds,
                $"EntryReconciliationIntervalSeconds must be between "
                + $"{TradingSettings.MinimumEntryReconciliationIntervalSeconds} and "
                + $"{TradingSettings.MaximumEntryReconciliationIntervalSeconds}")
            .Validate(settings => settings.EntryReconciliationBatchSize > 0, "EntryReconciliationBatchSize must be positive")
            .Validate(settings => settings.PatternScanMaxRetries > 0,
                "PatternScanMaxRetries must be positive")
            .Validate(settings => settings.PatternScanMaxConsecutiveFailures > 0,
                "PatternScanMaxConsecutiveFailures must be positive")
            .Validate(settings => settings.PatternScanCooldownSeconds > 0,
                "PatternScanCooldownSeconds must be positive")
            .Validate(settings => settings.PositionMonitoringIntervalSeconds > 0,
                "PositionMonitoringIntervalSeconds must be positive")
            .Validate(settings => settings.PositionOrderResolutionMaxAttempts > 0,
                "PositionOrderResolutionMaxAttempts must be positive")
            .Validate(settings => settings.PositionOrderResolutionDelayMilliseconds > 0,
                "PositionOrderResolutionDelayMilliseconds must be positive")
            .Validate(settings => TimeSpan.TryParse(settings.MarketOpenET, out _), "MarketOpenET must be a valid time")
            .Validate(settings => TimeSpan.TryParse(settings.MarketCloseET, out _), "MarketCloseET must be a valid time")
            .ValidateOnStart();
        services.Configure<PatternSettings>(configuration.GetSection("Patterns"));
        services.AddOptions<NotificationSettings>()
            .Bind(configuration.GetSection("Notification"))
            .Validate(
                settings => TimeOnly.TryParseExact(settings.DailyReportTime, "HH:mm", out _),
                "DailyReportTime must use HH:mm")
            .Validate(settings => settings.MaxRetryAttempts > 0, "MaxRetryAttempts must be positive")
            .Validate(settings => settings.RetryDelaySeconds > 0, "RetryDelaySeconds must be positive")
            .Validate(
                settings => settings.DailyReportRetryDelayMinutes > 0,
                "DailyReportRetryDelayMinutes must be positive")
            .ValidateOnStart();
        services.AddOptions<MLSettings>()
            .Bind(configuration.GetSection("ML"))
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.ModelDirectory),
                "ModelDirectory is required")
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.RegimeModelFileName),
                "RegimeModelFileName is required")
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.SignalScorerModelFileName),
                "SignalScorerModelFileName is required")
            .Validate(settings => settings.MinTrainingSamples > 0,
                "MinTrainingSamples must be positive")
            .Validate(settings => settings.RegimeClusterCount >= 2,
                "RegimeClusterCount must be at least 2")
            .Validate(settings => settings.RegimeTrainingDays > 0,
                "RegimeTrainingDays must be positive")
            .Validate(settings => settings.MlScoreBlendWeight is >= 0 and <= 1,
                "MlScoreBlendWeight must be in [0, 1]")
            .Validate(settings => settings.AutoRetrainIntervalHours > 0,
                "AutoRetrainIntervalHours must be positive")
            .Validate(settings => TimeOnly.TryParseExact(
                    settings.AutoRetrainAfterEt,
                    "HH:mm",
                    out _),
                "AutoRetrainAfterEt must use HH:mm")
            .Validate(settings => settings.AutoRetrainMaxConsecutiveFailures > 0,
                "AutoRetrainMaxConsecutiveFailures must be positive")
            .Validate(settings => settings.AutoRetrainCooldownMinutes > 0,
                "AutoRetrainCooldownMinutes must be positive")
            .Validate(settings => settings.AutoRetrainMaxRetries > 0,
                "AutoRetrainMaxRetries must be positive")
            .ValidateOnStart();
        services.AddOptions<PatternStatisticsSettings>()
            .Bind(configuration.GetSection("PatternStatistics"))
            .Validate(settings => settings.CacheMinutes > 0,
                "PatternStatistics CacheMinutes must be positive")
            .ValidateOnStart();
        services.AddOptions<LsSecuritiesSettings>()
            .Bind(configuration.GetSection("LsSecurities"))
            .Validate(settings => Uri.TryCreate(
                    settings.BaseUrl, UriKind.Absolute, out var uri)
                    && uri.Scheme == Uri.UriSchemeHttps,
                "LsSecurities BaseUrl must be an HTTPS URI")
            .Validate(settings => Uri.TryCreate(
                    settings.PaperBaseUrl, UriKind.Absolute, out var uri)
                    && uri.Scheme == Uri.UriSchemeHttps,
                "LsSecurities PaperBaseUrl must be an HTTPS URI")
            .Validate(settings => Uri.TryCreate(
                    settings.WebSocketUrl, UriKind.Absolute, out var uri)
                    && uri.Scheme == "wss",
                "LsSecurities WebSocketUrl must be a WSS URI")
            .Validate(settings => Uri.TryCreate(
                    settings.WebSocketPaperUrl, UriKind.Absolute, out var uri)
                    && uri.Scheme == "wss",
                "LsSecurities WebSocketPaperUrl must be a WSS URI")
            .Validate(settings => settings.TokenExpirySafetyMinutes >= 0,
                "LsSecurities TokenExpirySafetyMinutes cannot be negative")
            .ValidateOnStart();
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
        services.AddOptions<SignalLifecycleOptions>()
            .Bind(configuration.GetSection(SignalLifecycleOptions.SectionName))
            .Validate(
                settings => settings.ActionableLifetimeHours > 0
                    && settings.ActionableLifetimeHours
                        <= SignalFreshnessPolicy.MaximumConfigurableLifetime.TotalHours,
                $"ActionableLifetimeHours must be in (0, "
                + $"{SignalFreshnessPolicy.MaximumConfigurableLifetime.TotalHours:F0}]")
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
        services.AddSingleton(serviceProvider =>
        {
            var settings = serviceProvider
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<SignalLifecycleOptions>>()
                .Value;
            return new SignalFreshnessPolicy(
                TimeSpan.FromHours(settings.ActionableLifetimeHours));
        });

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
        services.AddScoped<ILiveSignalProcessor, LiveSignalProcessor>();
        services.AddSingleton<IRiskManagementService, MultiAccountRiskService>();
        services.AddScoped<IRiskOverviewQuery, RiskOverviewQuery>();
        services.AddScoped<IPortfolioPerformanceQuery, PortfolioPerformanceQuery>();
        services.AddScoped<IOpenPositionQuery, OpenPositionQuery>();
        services.AddScoped<ITradeActivityQuery, TradeActivityQueryService>();
        services.AddScoped<IDailyReportScheduleQuery, DailyReportScheduleQuery>();
        services.AddScoped<IDailyReportGenerator, DailyReportGenerator>();
        services.AddSingleton<IActiveAccountEquityReader, ActiveAccountEquityReader>();
        services.AddSingleton<IActiveBrokerAccountQuery, ActiveBrokerAccountQuery>();
        services.AddScoped<IDashboardQuery, DashboardQuery>();
        services.AddScoped<ManualOrderWorkflow>();
        services.AddSingleton<ManualSignalEntryPolicy>();
        services.AddScoped<ILiveEntryExecutionCoordinator, LiveEntryExecutionCoordinator>();
        services.AddScoped<ILiveEntryReconciliationCycle, LiveEntryReconciliationCycle>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<ILivePositionExecutionCoordinator, LivePositionExecutionCoordinator>();
        services.AddScoped<ILiveOrderManagement, LiveOrderManagement>();
        services.AddScoped<LivePositionExecutionEvaluator>();
        services.AddScoped<ILivePositionExecutionEvaluator>(sp =>
            sp.GetRequiredService<LivePositionExecutionEvaluator>());
        services.AddScoped<ILivePositionMonitoringCycle, LivePositionMonitoringCycle>();
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
