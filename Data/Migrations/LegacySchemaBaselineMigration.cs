using StockTrader.Models;

namespace StockTrader.Data.Migrations;

/// <summary>
/// EnsureCreated 기반으로 운영되던 모든 과거 스키마 보정을 하나의 추적 가능한 기준선으로 이관한다.
/// 이후 변경은 이 파일을 수정하지 않고 새 IDatabaseMigration으로 추가한다.
/// </summary>
public sealed class LegacySchemaBaselineMigration : IDatabaseMigration
{
    public string Id => "202608180001_legacy_schema_baseline";
    public string Description => "기존 수동 스키마 보정과 일회성 데이터 정리를 버전 기록으로 이관";

    private readonly ILogger<LegacySchemaBaselineMigration> _logger;

    public LegacySchemaBaselineMigration(ILogger<LegacySchemaBaselineMigration> logger) => _logger = logger;

    public async Task ApplyAsync(SqliteMigrationContext db, CancellationToken ct)
    {
        await db.EnsureColumnsAsync("UserSettings", new Dictionary<string, string>
        {
            ["RiskPerTradePercent"] = "REAL NOT NULL DEFAULT 0.01",
            ["DailyLossLimitPercent"] = "REAL NOT NULL DEFAULT 0.03",
            ["MaxTotalPositions"] = "INTEGER NOT NULL DEFAULT 10",
            ["MaxPositionsPerSector"] = "INTEGER NOT NULL DEFAULT 2",
            ["MinExpectancy"] = "REAL NOT NULL DEFAULT 0.0",
            ["LiveParameterOverridesJson"] = "TEXT",
            ["EnableTelegram"] = "INTEGER",
            ["TelegramBotToken"] = "TEXT",
            ["TelegramChatId"] = "TEXT",
            ["EnableDiscord"] = "INTEGER",
            ["DiscordWebhookUrl"] = "TEXT",
            ["EnableEmail"] = "INTEGER",
            ["SmtpHost"] = "TEXT",
            ["SmtpPort"] = "INTEGER",
            ["SmtpUseSsl"] = "INTEGER",
            ["SmtpUsername"] = "TEXT",
            ["SmtpPassword"] = "TEXT",
            ["EmailFrom"] = "TEXT",
            ["EmailTo"] = "TEXT",
            ["DailyReportTimeKst"] = "TEXT",
            ["Tqqq200SmaAllowedSymbols"] = "TEXT"
        }, ct);

        await EnsureFinancialTablesAsync(db, ct);
        await EnsureAccountAndPositionSchemaAsync(db, ct);
        await EnsureSecurityAndProfileTablesAsync(db, ct);
        await EnsureCustomPatternSchemaAsync(db, ct);
        await EnsureOptimizationSchemaAsync(db, ct);
        await ApplyOneTimeDataRepairsAsync(db, ct);
    }

    private static async Task EnsureFinancialTablesAsync(SqliteMigrationContext db, CancellationToken ct)
    {
        await db.EnsureTableAsync("FinancialSnapshots", """
            CREATE TABLE FinancialSnapshots (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Symbol TEXT NOT NULL DEFAULT '', AsOfDate TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
                Source TEXT NOT NULL DEFAULT 'Manual', PeRatio REAL, PbRatio REAL, RoePercent REAL,
                OperatingMarginPercent REAL, RevenueCurrent REAL, RevenuePrevious REAL,
                OperatingIncomeCurrent REAL, OperatingIncomePrevious REAL, NetIncomeCurrent REAL,
                NetIncomePrevious REAL, Notes TEXT, CreatedAt TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
                UpdatedAt TEXT NOT NULL DEFAULT '0001-01-01 00:00:00')
            """, [
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_FinancialSnapshots_Symbol_AsOfDate ON FinancialSnapshots (Symbol, AsOfDate)",
            "CREATE INDEX IF NOT EXISTS IX_FinancialSnapshots_AsOfDate ON FinancialSnapshots (AsOfDate)",
            "CREATE INDEX IF NOT EXISTS IX_FinancialSnapshots_Symbol ON FinancialSnapshots (Symbol)"
        ], ct);

        await db.EnsureTableAsync("FinancialImportRuns", """
            CREATE TABLE FinancialImportRuns (
                Id INTEGER PRIMARY KEY AUTOINCREMENT, SourceType TEXT NOT NULL DEFAULT '',
                FilePath TEXT NOT NULL DEFAULT '', Fingerprint TEXT NOT NULL DEFAULT '',
                Status TEXT NOT NULL DEFAULT 'Pending', ImportedCount INTEGER NOT NULL DEFAULT 0,
                SkippedCount INTEGER NOT NULL DEFAULT 0, ErrorMessage TEXT,
                StartedAt TEXT NOT NULL DEFAULT '0001-01-01 00:00:00', CompletedAt TEXT)
            """, [
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_FinancialImportRuns_FilePath_Fingerprint ON FinancialImportRuns (FilePath, Fingerprint)",
            "CREATE INDEX IF NOT EXISTS IX_FinancialImportRuns_StartedAt ON FinancialImportRuns (StartedAt)",
            "CREATE INDEX IF NOT EXISTS IX_FinancialImportRuns_Status ON FinancialImportRuns (Status)"
        ], ct);
    }

    private static async Task EnsureAccountAndPositionSchemaAsync(SqliteMigrationContext db, CancellationToken ct)
    {
        await db.EnsureTableAsync("TradingAccounts", """
            CREATE TABLE TradingAccounts (
                Id INTEGER PRIMARY KEY AUTOINCREMENT, AccountName TEXT NOT NULL DEFAULT '',
                BrokerType INTEGER NOT NULL DEFAULT 0, ApiKey TEXT NOT NULL DEFAULT '',
                ApiSecret TEXT NOT NULL DEFAULT '', Environment TEXT NOT NULL DEFAULT 'Paper',
                IsActive INTEGER NOT NULL DEFAULT 0, IsEnabled INTEGER NOT NULL DEFAULT 1,
                Notes TEXT NOT NULL DEFAULT '', CreatedAt TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
                UpdatedAt TEXT NOT NULL DEFAULT '0001-01-01 00:00:00', LastConnectedAt TEXT)
            """, [
            "CREATE INDEX IF NOT EXISTS IX_TradingAccounts_BrokerType ON TradingAccounts (BrokerType)",
            "CREATE INDEX IF NOT EXISTS IX_TradingAccounts_IsActive ON TradingAccounts (IsActive)"
        ], ct);
        await db.EnsureColumnsAsync("TradingAccounts", new Dictionary<string, string>
        {
            ["Notes"] = "TEXT NOT NULL DEFAULT ''",
            ["LastConnectedAt"] = "TEXT"
        }, ct);
        await db.EnsureColumnsAsync("Positions", new Dictionary<string, string>
        {
            ["HighSinceEntry"] = "REAL NOT NULL DEFAULT 0",
            ["EntryAtr"] = "REAL NOT NULL DEFAULT 0",
            ["AccountId"] = "INTEGER NOT NULL DEFAULT 0"
        }, ct);
    }

    private static async Task EnsureSecurityAndProfileTablesAsync(SqliteMigrationContext db, CancellationToken ct)
    {
        await db.EnsureTableAsync("AppUsers", """
            CREATE TABLE AppUsers (
                Id INTEGER PRIMARY KEY AUTOINCREMENT, Username TEXT NOT NULL DEFAULT '',
                PasswordHash TEXT NOT NULL DEFAULT '', Salt TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL DEFAULT '0001-01-01 00:00:00', LastLoginAt TEXT,
                IsActive INTEGER NOT NULL DEFAULT 1, FailedLoginAttempts INTEGER NOT NULL DEFAULT 0,
                LockedUntil TEXT)
            """, [
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_AppUsers_Username ON AppUsers (Username)",
            "CREATE INDEX IF NOT EXISTS IX_AppUsers_IsActive ON AppUsers (IsActive)"
        ], ct);
        await db.EnsureTableAsync("AuditLogs", """
            CREATE TABLE AuditLogs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT, UserId INTEGER, Action TEXT NOT NULL DEFAULT '',
                Details TEXT NOT NULL DEFAULT '', IpAddress TEXT NOT NULL DEFAULT '',
                Timestamp TEXT NOT NULL DEFAULT '0001-01-01 00:00:00')
            """, [
            "CREATE INDEX IF NOT EXISTS IX_AuditLogs_Timestamp ON AuditLogs (Timestamp)",
            "CREATE INDEX IF NOT EXISTS IX_AuditLogs_UserId ON AuditLogs (UserId)",
            "CREATE INDEX IF NOT EXISTS IX_AuditLogs_Action ON AuditLogs (Action)"
        ], ct);
        await db.EnsureTableAsync("SymbolProfiles", """
            CREATE TABLE SymbolProfiles (
                Id INTEGER PRIMARY KEY AUTOINCREMENT, Symbol TEXT NOT NULL DEFAULT '',
                Name TEXT NOT NULL DEFAULT '기본', IsActive INTEGER NOT NULL DEFAULT 0,
                EnabledPatterns TEXT NOT NULL DEFAULT '[]', ParameterOverridesJson TEXT,
                WeightStrategyJson TEXT, RiskPerTradePercent REAL NOT NULL DEFAULT 0.01,
                MaxTotalPositions INTEGER NOT NULL DEFAULT 7, BacktestReturnPct REAL,
                BacktestWinRate REAL, BacktestMaxDrawdown REAL, BacktestSharpe REAL,
                BacktestTrades INTEGER, BacktestFrom TEXT, BacktestTo TEXT,
                CreatedAt TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
                UpdatedAt TEXT NOT NULL DEFAULT '0001-01-01 00:00:00')
            """, [
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_SymbolProfiles_Symbol_Name ON SymbolProfiles (Symbol, Name)",
            "CREATE INDEX IF NOT EXISTS IX_SymbolProfiles_Symbol_IsActive ON SymbolProfiles (Symbol, IsActive)"
        ], ct);
    }

    private static async Task EnsureCustomPatternSchemaAsync(SqliteMigrationContext db, CancellationToken ct)
    {
        await db.EnsureTableAsync("CustomPatterns", """
            CREATE TABLE CustomPatterns (
                Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL DEFAULT '', Description TEXT,
                EntryRulesJson TEXT NOT NULL DEFAULT '[]', EntryLogic TEXT NOT NULL DEFAULT 'AND',
                RequireBullRegime INTEGER NOT NULL DEFAULT 0, AtrStopMultiplier REAL NOT NULL DEFAULT 2.0,
                AtrTargetMultiplier REAL NOT NULL DEFAULT 3.0, MaxHoldingBars INTEGER NOT NULL DEFAULT 10,
                TrailingAtr REAL NOT NULL DEFAULT 0, PartialProfitR REAL NOT NULL DEFAULT 0,
                TimeFrame INTEGER NOT NULL DEFAULT 3, IsActive INTEGER NOT NULL DEFAULT 1,
                CreatedAt TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
                UpdatedAt TEXT NOT NULL DEFAULT '0001-01-01 00:00:00')
            """, [
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_CustomPatterns_Name ON CustomPatterns (Name)",
            "CREATE INDEX IF NOT EXISTS IX_CustomPatterns_IsActive ON CustomPatterns (IsActive)"
        ], ct);
        await db.EnsureColumnsAsync("CustomPatterns", new Dictionary<string, string>
        {
            ["UseWeightTiers"] = "INTEGER NOT NULL DEFAULT 0",
            ["WeightTiersJson"] = "TEXT NOT NULL DEFAULT '[]'",
            ["DefaultAllocationPercent"] = "REAL NOT NULL DEFAULT 100",
            ["ExitRulesJson"] = "TEXT NOT NULL DEFAULT '[]'",
            ["ExitRulesLogic"] = "TEXT NOT NULL DEFAULT 'OR'",
            ["ExitGroupsJson"] = "TEXT NOT NULL DEFAULT '[]'",
            ["ExitGroupsLogic"] = "TEXT NOT NULL DEFAULT 'OR'",
            ["ScalingRulesJson"] = "TEXT NOT NULL DEFAULT '[]'",
            ["TimeFilterJson"] = "TEXT NOT NULL DEFAULT '{}'",
            ["CircuitBreakerJson"] = "TEXT NOT NULL DEFAULT '{}'",
            ["ReentryJson"] = "TEXT NOT NULL DEFAULT '{}'",
            ["PortfolioRulesJson"] = "TEXT NOT NULL DEFAULT '{}'",
            ["EntryGroupsJson"] = "TEXT NOT NULL DEFAULT '[]'",
            ["EntryGroupsLogic"] = "TEXT NOT NULL DEFAULT 'AND'",
            ["DynamicExitJson"] = "TEXT NOT NULL DEFAULT '{}'",
            ["EntryMode"] = "TEXT NOT NULL DEFAULT 'CurrentClose'",
            ["SizingMode"] = "TEXT NOT NULL DEFAULT 'FixedRisk'",
            ["EnableLiveTrading"] = "INTEGER NOT NULL DEFAULT 0",
            ["TimeFrame"] = "INTEGER NOT NULL DEFAULT 3"
        }, ct);

        foreach (var table in new[] { "PatternSignals", "TradeRecommendations", "Positions", "TradeRecords" })
            await db.EnsureColumnsAsync(table, new Dictionary<string, string> { ["CustomPatternName"] = "TEXT" }, ct);
    }

    private static async Task EnsureOptimizationSchemaAsync(SqliteMigrationContext db, CancellationToken ct)
    {
        await db.EnsureTableAsync("OptimizationJobs", """
            CREATE TABLE OptimizationJobs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL DEFAULT '',
                Status INTEGER NOT NULL DEFAULT 0, Priority INTEGER NOT NULL DEFAULT 0,
                RequestJson TEXT NOT NULL DEFAULT '', TotalCombinations INTEGER NOT NULL DEFAULT 0,
                TestedCombinations INTEGER NOT NULL DEFAULT 0, CurrentChunkIndex INTEGER NOT NULL DEFAULT 0,
                ChunkSize INTEGER NOT NULL DEFAULT 200, CreatedAt TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
                StartedAt TEXT, CompletedAt TEXT, LastProgressAt TEXT, MaxDurationHours REAL,
                MaxTestedCombinations INTEGER, RankBy TEXT NOT NULL DEFAULT 'sortinoRatio',
                TopResultsToKeep INTEGER NOT NULL DEFAULT 50, ContinuousMode INTEGER NOT NULL DEFAULT 0,
                AutoApplyBestResult INTEGER NOT NULL DEFAULT 0, AutoApplyMinTrades INTEGER NOT NULL DEFAULT 10,
                AppliedResultCount INTEGER NOT NULL DEFAULT 0, LastAutoAppliedAt TEXT,
                LastAutoAppliedResultId INTEGER, LastAutoApplyMessage TEXT, ErrorMessage TEXT)
            """, [
            "CREATE INDEX IF NOT EXISTS IX_OptimizationJobs_Status_Priority ON OptimizationJobs (Status, Priority)"
        ], ct);
        await db.EnsureColumnsAsync("OptimizationJobs", new Dictionary<string, string>
        {
            ["ContinuousMode"] = "INTEGER NOT NULL DEFAULT 0",
            ["AutoApplyBestResult"] = "INTEGER NOT NULL DEFAULT 0",
            ["AutoApplyMinTrades"] = "INTEGER NOT NULL DEFAULT 10",
            ["AppliedResultCount"] = "INTEGER NOT NULL DEFAULT 0",
            ["LastAutoAppliedAt"] = "TEXT",
            ["LastAutoAppliedResultId"] = "INTEGER",
            ["LastAutoApplyMessage"] = "TEXT"
        }, ct);
        await db.EnsureTableAsync("OptimizationResults", """
            CREATE TABLE OptimizationResults (
                Id INTEGER PRIMARY KEY AUTOINCREMENT, JobId INTEGER NOT NULL, Rank INTEGER NOT NULL DEFAULT 0,
                ParamsJson TEXT NOT NULL DEFAULT '', TotalReturn REAL NOT NULL DEFAULT 0,
                SortinoRatio REAL NOT NULL DEFAULT 0, SharpeRatio REAL NOT NULL DEFAULT 0,
                MaxDrawdown REAL NOT NULL DEFAULT 0, WinRate REAL NOT NULL DEFAULT 0,
                TotalTrades INTEGER NOT NULL DEFAULT 0, ProfitFactor REAL NOT NULL DEFAULT 0,
                CalmarRatio REAL NOT NULL DEFAULT 0, AnnualizedReturn REAL NOT NULL DEFAULT 0,
                OosTotalReturn REAL, OosSortinoRatio REAL, OosSharpeRatio REAL, OosMaxDrawdown REAL,
                OosWinRate REAL, OosTotalTrades INTEGER, OosProfitFactor REAL, OosCalmarRatio REAL,
                OosAnnualizedReturn REAL, TestedAtCombination INTEGER NOT NULL DEFAULT 0,
                DiscoveredAt TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
                FOREIGN KEY (JobId) REFERENCES OptimizationJobs(Id) ON DELETE CASCADE)
            """, [
            "CREATE INDEX IF NOT EXISTS IX_OptimizationResults_JobId_Rank ON OptimizationResults (JobId, Rank)"
        ], ct);
    }

    private async Task ApplyOneTimeDataRepairsAsync(SqliteMigrationContext db, CancellationToken ct)
    {
        var deactivated = 0;
        var lowConfidence = 0;
        var invalidPrices = 0;
        var pendingRecommendations = 0;
        if (await db.TableExistsAsync("PatternSignals", ct))
        {
            deactivated = await db.ExecuteAsync(
                $"UPDATE PatternSignals SET IsActive = 0 WHERE PatternType = {(int)PatternType.Tqqq200Sma} AND Symbol != 'TQQQ' AND IsActive = 1", ct);
            lowConfidence = await db.ExecuteAsync(
                "UPDATE PatternSignals SET IsActive = 0 WHERE Confidence < 0.3 AND IsActive = 1", ct);
            invalidPrices = await db.ExecuteAsync(
                "UPDATE PatternSignals SET IsActive = 0 WHERE (StopLossPrice >= EntryPrice OR TargetPrice <= EntryPrice) AND IsActive = 1", ct);
        }
        if (await db.TableExistsAsync("TradeRecommendations", ct))
            pendingRecommendations = await db.ExecuteAsync("DELETE FROM TradeRecommendations WHERE WasExecuted = 0", ct);

        _logger.LogInformation(
            "Legacy data repair: TQQQ scope={TqqqScope}, low confidence={LowConfidence}, invalid price={InvalidPrice}, pending recommendations={PendingRecommendations}",
            deactivated, lowConfidence, invalidPrices, pendingRecommendations);
    }
}
