using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MudBlazor.Services;
using Serilog;
using StockTrader.Api;
using StockTrader.Components;
using StockTrader.Configuration;
using StockTrader.Data;
using StockTrader.Extensions;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Auth;
using StockTrader.Services.Backtest;
using StockTrader.Services.LiveParameter;
using StockTrader.Services.Order;
using StockTrader.BackgroundServices;

// Serilog bootstrap logger — captures startup errors before host is built
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{

var builder = WebApplication.CreateBuilder(args);

// 로컬 전용 앱이므로 Production에서도 User Secrets를 읽는다.
// (클라우드 배포 시에는 환경 변수나 Vault로 대체)
if (!builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>(optional: true);
}

// Serilog: replace default logging with structured logging from appsettings
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services));

// Add Blazor services
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();

// In-memory cache (used by StockAnalysisService to avoid redundant API calls)
builder.Services.AddMemoryCache();

// Minimal API JSON: enum을 문자열 이름으로 직렬화/역직렬화
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});

// Add StockTrader services (DI, DB, background services, etc.)
builder.Services.AddStockTraderServices(builder.Configuration);

// Add security services (cookie auth, crypto, rate limiting)
builder.Services.AddSecurityServices(builder.Configuration);
builder.Services.AddCors(options =>
{
    options.AddPolicy("DesktopUi", policy =>
    {
        policy.WithOrigins(
                "http://stock-desktop.taewon",
                "https://stock-desktop.taewon",
                "http://localhost:5173",
                "http://localhost:8000",
                "http://127.0.0.1:5173",
                "http://127.0.0.1:8000")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// Auth HttpClient — Blazor Server 컴포넌트에서 자체 API(/api/auth/*)를 호출할 때 사용
// Kestrel은 0.0.0.0:5239로 바인딩되므로, 자체 호출에는 localhost 사용
builder.Services.AddHttpClient("Auth", client =>
{
    client.BaseAddress = new Uri("http://localhost:5239");
    client.Timeout = TimeSpan.FromSeconds(10);
});

var app = builder.Build();

// Ensure DB is created and apply lightweight schema migrations
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var financialPipeline = scope.ServiceProvider.GetRequiredService<FinancialSnapshotIngestionService>();
    await db.Database.EnsureCreatedAsync();

    // UserSettings 테이블에 리스크 관련 컬럼이 없으면 추가 (기존 DB 호환)
    // EnsureCreatedAsync는 기존 DB 스키마를 변경하지 않으므로 수동 ALTER 필요
    try
    {
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();

        // 컬럼 목록 조회
        cmd.CommandText = "PRAGMA table_info(UserSettings)";
        var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                existingColumns.Add(reader.GetString(1)); // column name
        }

        // 누락된 컬럼 추가 (리스크 관리 + 알림 설정)
        var alterStatements = new Dictionary<string, string>
        {
            ["RiskPerTradePercent"]   = "ALTER TABLE UserSettings ADD COLUMN RiskPerTradePercent REAL NOT NULL DEFAULT 0.01",
            ["DailyLossLimitPercent"] = "ALTER TABLE UserSettings ADD COLUMN DailyLossLimitPercent REAL NOT NULL DEFAULT 0.03",
            ["MaxTotalPositions"]     = "ALTER TABLE UserSettings ADD COLUMN MaxTotalPositions INTEGER NOT NULL DEFAULT 10",
            ["MaxPositionsPerSector"] = "ALTER TABLE UserSettings ADD COLUMN MaxPositionsPerSector INTEGER NOT NULL DEFAULT 2",
            ["MinExpectancy"]         = "ALTER TABLE UserSettings ADD COLUMN MinExpectancy REAL NOT NULL DEFAULT 0.0",
            ["LiveParameterOverridesJson"] = "ALTER TABLE UserSettings ADD COLUMN LiveParameterOverridesJson TEXT",
            // 알림 채널 설정 (nullable — null이면 appsettings.json fallback)
            ["EnableTelegram"]     = "ALTER TABLE UserSettings ADD COLUMN EnableTelegram INTEGER",
            ["TelegramBotToken"]   = "ALTER TABLE UserSettings ADD COLUMN TelegramBotToken TEXT",
            ["TelegramChatId"]     = "ALTER TABLE UserSettings ADD COLUMN TelegramChatId TEXT",
            ["EnableDiscord"]      = "ALTER TABLE UserSettings ADD COLUMN EnableDiscord INTEGER",
            ["DiscordWebhookUrl"]  = "ALTER TABLE UserSettings ADD COLUMN DiscordWebhookUrl TEXT",
            ["EnableEmail"]        = "ALTER TABLE UserSettings ADD COLUMN EnableEmail INTEGER",
            ["SmtpHost"]           = "ALTER TABLE UserSettings ADD COLUMN SmtpHost TEXT",
            ["SmtpPort"]           = "ALTER TABLE UserSettings ADD COLUMN SmtpPort INTEGER",
            ["SmtpUseSsl"]         = "ALTER TABLE UserSettings ADD COLUMN SmtpUseSsl INTEGER",
            ["SmtpUsername"]       = "ALTER TABLE UserSettings ADD COLUMN SmtpUsername TEXT",
            ["SmtpPassword"]       = "ALTER TABLE UserSettings ADD COLUMN SmtpPassword TEXT",
            ["EmailFrom"]          = "ALTER TABLE UserSettings ADD COLUMN EmailFrom TEXT",
            ["EmailTo"]            = "ALTER TABLE UserSettings ADD COLUMN EmailTo TEXT",
            ["DailyReportTimeKst"] = "ALTER TABLE UserSettings ADD COLUMN DailyReportTimeKst TEXT",
            // 패턴별 종목 설정
            ["Tqqq200SmaAllowedSymbols"] = "ALTER TABLE UserSettings ADD COLUMN Tqqq200SmaAllowedSymbols TEXT",
        };

        foreach (var (col, sql) in alterStatements)
        {
            if (!existingColumns.Contains(col))
            {
                cmd.CommandText = sql;
                await cmd.ExecuteNonQueryAsync();
            }
        }

        // FinancialSnapshots 테이블 생성 (없는 경우)
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='FinancialSnapshots'";
        if (await cmd.ExecuteScalarAsync() == null)
        {
            cmd.CommandText = @"
                CREATE TABLE FinancialSnapshots (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Symbol TEXT NOT NULL DEFAULT '',
                    AsOfDate TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
                    Source TEXT NOT NULL DEFAULT 'Manual',
                    PeRatio REAL,
                    PbRatio REAL,
                    RoePercent REAL,
                    OperatingMarginPercent REAL,
                    RevenueCurrent REAL,
                    RevenuePrevious REAL,
                    OperatingIncomeCurrent REAL,
                    OperatingIncomePrevious REAL,
                    NetIncomeCurrent REAL,
                    NetIncomePrevious REAL,
                    Notes TEXT,
                    CreatedAt TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
                    UpdatedAt TEXT NOT NULL DEFAULT '0001-01-01 00:00:00'
                )";
            await cmd.ExecuteNonQueryAsync();

            cmd.CommandText = "CREATE UNIQUE INDEX IX_FinancialSnapshots_Symbol_AsOfDate ON FinancialSnapshots (Symbol, AsOfDate)";
            await cmd.ExecuteNonQueryAsync();
            cmd.CommandText = "CREATE INDEX IX_FinancialSnapshots_AsOfDate ON FinancialSnapshots (AsOfDate)";
            await cmd.ExecuteNonQueryAsync();
            cmd.CommandText = "CREATE INDEX IX_FinancialSnapshots_Symbol ON FinancialSnapshots (Symbol)";
            await cmd.ExecuteNonQueryAsync();
        }

        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='FinancialImportRuns'";
        if (await cmd.ExecuteScalarAsync() == null)
        {
            cmd.CommandText = @"
                CREATE TABLE FinancialImportRuns (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    SourceType TEXT NOT NULL DEFAULT '',
                    FilePath TEXT NOT NULL DEFAULT '',
                    Fingerprint TEXT NOT NULL DEFAULT '',
                    Status TEXT NOT NULL DEFAULT 'Pending',
                    ImportedCount INTEGER NOT NULL DEFAULT 0,
                    SkippedCount INTEGER NOT NULL DEFAULT 0,
                    ErrorMessage TEXT,
                    StartedAt TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
                    CompletedAt TEXT
                )";
            await cmd.ExecuteNonQueryAsync();
            cmd.CommandText = "CREATE UNIQUE INDEX IX_FinancialImportRuns_FilePath_Fingerprint ON FinancialImportRuns (FilePath, Fingerprint)";
            await cmd.ExecuteNonQueryAsync();
            cmd.CommandText = "CREATE INDEX IX_FinancialImportRuns_StartedAt ON FinancialImportRuns (StartedAt)";
            await cmd.ExecuteNonQueryAsync();
            cmd.CommandText = "CREATE INDEX IX_FinancialImportRuns_Status ON FinancialImportRuns (Status)";
            await cmd.ExecuteNonQueryAsync();
        }

        // TradingAccounts 테이블 마이그레이션
        // EnsureCreatedAsync는 이미 존재하는 테이블은 건드리지 않으므로 신규 컬럼은 수동 ALTER 필요
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='TradingAccounts'";
        var tableExists = await cmd.ExecuteScalarAsync() != null;

        if (!tableExists)
        {
            // 기존 DB에 TradingAccounts 테이블이 없으면 생성
            cmd.CommandText = @"
                CREATE TABLE TradingAccounts (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    AccountName TEXT NOT NULL DEFAULT '',
                    BrokerType INTEGER NOT NULL DEFAULT 0,
                    ApiKey TEXT NOT NULL DEFAULT '',
                    ApiSecret TEXT NOT NULL DEFAULT '',
                    Environment TEXT NOT NULL DEFAULT 'Paper',
                    IsActive INTEGER NOT NULL DEFAULT 0,
                    IsEnabled INTEGER NOT NULL DEFAULT 1,
                    Notes TEXT NOT NULL DEFAULT '',
                    CreatedAt TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
                    UpdatedAt TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
                    LastConnectedAt TEXT
                )";
            await cmd.ExecuteNonQueryAsync();

            cmd.CommandText = "CREATE INDEX IX_TradingAccounts_BrokerType ON TradingAccounts (BrokerType)";
            await cmd.ExecuteNonQueryAsync();
            cmd.CommandText = "CREATE INDEX IX_TradingAccounts_IsActive ON TradingAccounts (IsActive)";
            await cmd.ExecuteNonQueryAsync();
        }
        else
        {
            // 기존 TradingAccounts 테이블에 신규 컬럼이 있으면 추가 (하위 호환성)
            cmd.CommandText = "PRAGMA table_info(TradingAccounts)";
            var accountColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var reader2 = await cmd.ExecuteReaderAsync())
            {
                while (await reader2.ReadAsync())
                    accountColumns.Add(reader2.GetString(1));
            }

            var accountAlterStatements = new Dictionary<string, string>
            {
                ["Notes"] = "ALTER TABLE TradingAccounts ADD COLUMN Notes TEXT NOT NULL DEFAULT ''",
                ["LastConnectedAt"] = "ALTER TABLE TradingAccounts ADD COLUMN LastConnectedAt TEXT",
            };

            foreach (var (col, sql) in accountAlterStatements)
            {
                if (!accountColumns.Contains(col))
                {
                    cmd.CommandText = sql;
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        // Positions 테이블에 Exit Management용 신규 컬럼 추가
        cmd.CommandText = "PRAGMA table_info(Positions)";
        var positionColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var reader3 = await cmd.ExecuteReaderAsync())
        {
            while (await reader3.ReadAsync())
                positionColumns.Add(reader3.GetString(1));
        }

        var positionAlterStatements = new Dictionary<string, string>
        {
            ["HighSinceEntry"] = "ALTER TABLE Positions ADD COLUMN HighSinceEntry REAL NOT NULL DEFAULT 0",
            ["EntryAtr"]       = "ALTER TABLE Positions ADD COLUMN EntryAtr REAL NOT NULL DEFAULT 0",
            // AccountId: 계좌별 포지션 격리용. 레거시 행은 0(미지정)으로 채워짐.
            ["AccountId"]      = "ALTER TABLE Positions ADD COLUMN AccountId INTEGER NOT NULL DEFAULT 0",
        };

        foreach (var (col, sql) in positionAlterStatements)
        {
            if (!positionColumns.Contains(col))
            {
                cmd.CommandText = sql;
                await cmd.ExecuteNonQueryAsync();
            }
        }

        // AppUsers 테이블 생성 (없는 경우)
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='AppUsers'";
        if (await cmd.ExecuteScalarAsync() == null)
        {
            cmd.CommandText = @"
                CREATE TABLE AppUsers (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Username TEXT NOT NULL DEFAULT '',
                    PasswordHash TEXT NOT NULL DEFAULT '',
                    Salt TEXT NOT NULL DEFAULT '',
                    CreatedAt TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
                    LastLoginAt TEXT,
                    IsActive INTEGER NOT NULL DEFAULT 1,
                    FailedLoginAttempts INTEGER NOT NULL DEFAULT 0,
                    LockedUntil TEXT
                )";
            await cmd.ExecuteNonQueryAsync();
            cmd.CommandText = "CREATE UNIQUE INDEX IX_AppUsers_Username ON AppUsers (Username)";
            await cmd.ExecuteNonQueryAsync();
            cmd.CommandText = "CREATE INDEX IX_AppUsers_IsActive ON AppUsers (IsActive)";
            await cmd.ExecuteNonQueryAsync();
        }

        // AuditLogs 테이블 생성 (없는 경우)
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='AuditLogs'";
        if (await cmd.ExecuteScalarAsync() == null)
        {
            cmd.CommandText = @"
                CREATE TABLE AuditLogs (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    UserId INTEGER,
                    Action TEXT NOT NULL DEFAULT '',
                    Details TEXT NOT NULL DEFAULT '',
                    IpAddress TEXT NOT NULL DEFAULT '',
                    Timestamp TEXT NOT NULL DEFAULT '0001-01-01 00:00:00'
                )";
            await cmd.ExecuteNonQueryAsync();
            cmd.CommandText = "CREATE INDEX IX_AuditLogs_Timestamp ON AuditLogs (Timestamp)";
            await cmd.ExecuteNonQueryAsync();
            cmd.CommandText = "CREATE INDEX IX_AuditLogs_UserId ON AuditLogs (UserId)";
            await cmd.ExecuteNonQueryAsync();
            cmd.CommandText = "CREATE INDEX IX_AuditLogs_Action ON AuditLogs (Action)";
            await cmd.ExecuteNonQueryAsync();
        }

        // SymbolProfiles 테이블 생성 (없는 경우) — 종목별 트레이딩 프로파일
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='SymbolProfiles'";
        if (await cmd.ExecuteScalarAsync() == null)
        {
            cmd.CommandText = @"
                CREATE TABLE SymbolProfiles (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Symbol TEXT NOT NULL DEFAULT '',
                    Name TEXT NOT NULL DEFAULT '기본',
                    IsActive INTEGER NOT NULL DEFAULT 0,
                    EnabledPatterns TEXT NOT NULL DEFAULT '[]',
                    ParameterOverridesJson TEXT,
                    WeightStrategyJson TEXT,
                    RiskPerTradePercent REAL NOT NULL DEFAULT 0.01,
                    MaxTotalPositions INTEGER NOT NULL DEFAULT 7,
                    BacktestReturnPct REAL,
                    BacktestWinRate REAL,
                    BacktestMaxDrawdown REAL,
                    BacktestSharpe REAL,
                    BacktestTrades INTEGER,
                    BacktestFrom TEXT,
                    BacktestTo TEXT,
                    CreatedAt TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
                    UpdatedAt TEXT NOT NULL DEFAULT '0001-01-01 00:00:00'
                )";
            await cmd.ExecuteNonQueryAsync();
            cmd.CommandText = "CREATE UNIQUE INDEX IX_SymbolProfiles_Symbol_Name ON SymbolProfiles (Symbol, Name)";
            await cmd.ExecuteNonQueryAsync();
            cmd.CommandText = "CREATE INDEX IX_SymbolProfiles_Symbol_IsActive ON SymbolProfiles (Symbol, IsActive)";
            await cmd.ExecuteNonQueryAsync();
        }

        // CustomPatterns 테이블
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='CustomPatterns'";
        if (await cmd.ExecuteScalarAsync() == null)
        {
            cmd.CommandText = @"
                CREATE TABLE CustomPatterns (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL DEFAULT '',
                    Description TEXT,
                    EntryRulesJson TEXT NOT NULL DEFAULT '[]',
                    EntryLogic TEXT NOT NULL DEFAULT 'AND',
                    RequireBullRegime INTEGER NOT NULL DEFAULT 0,
                    AtrStopMultiplier REAL NOT NULL DEFAULT 2.0,
                    AtrTargetMultiplier REAL NOT NULL DEFAULT 3.0,
                    MaxHoldingBars INTEGER NOT NULL DEFAULT 10,
                    TrailingAtr REAL NOT NULL DEFAULT 0,
                    PartialProfitR REAL NOT NULL DEFAULT 0,
                    TimeFrame INTEGER NOT NULL DEFAULT 3,
                    IsActive INTEGER NOT NULL DEFAULT 1,
                    CreatedAt TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
                    UpdatedAt TEXT NOT NULL DEFAULT '0001-01-01 00:00:00'
                )";
            await cmd.ExecuteNonQueryAsync();
            cmd.CommandText = "CREATE UNIQUE INDEX IX_CustomPatterns_Name ON CustomPatterns (Name)";
            await cmd.ExecuteNonQueryAsync();
        }

        // CustomPatterns 비중 단계 컬럼 추가 (기존 테이블 마이그레이션)
        cmd.CommandText = "PRAGMA table_info(CustomPatterns)";
        var existingCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                existingCols.Add(reader.GetString(1));
        }
        if (!existingCols.Contains("UseWeightTiers"))
        {
            cmd.CommandText = "ALTER TABLE CustomPatterns ADD COLUMN UseWeightTiers INTEGER NOT NULL DEFAULT 0";
            await cmd.ExecuteNonQueryAsync();
        }
        if (!existingCols.Contains("WeightTiersJson"))
        {
            cmd.CommandText = "ALTER TABLE CustomPatterns ADD COLUMN WeightTiersJson TEXT NOT NULL DEFAULT '[]'";
            await cmd.ExecuteNonQueryAsync();
        }
        if (!existingCols.Contains("DefaultAllocationPercent"))
        {
            cmd.CommandText = "ALTER TABLE CustomPatterns ADD COLUMN DefaultAllocationPercent REAL NOT NULL DEFAULT 100";
            await cmd.ExecuteNonQueryAsync();
        }
        // 고급 설정 컬럼 추가
        foreach (var (col, def) in new[] {
            ("ExitRulesJson", "'[]'"), ("ExitRulesLogic", "'OR'"),
            ("ExitGroupsJson", "'[]'"), ("ExitGroupsLogic", "'OR'"),
            ("ScalingRulesJson", "'[]'"), ("TimeFilterJson", "'{}'"),
            ("CircuitBreakerJson", "'{}'"), ("ReentryJson", "'{}'"), ("PortfolioRulesJson", "'{}'"),
            ("EntryGroupsJson", "'[]'"), ("EntryGroupsLogic", "'AND'"), ("DynamicExitJson", "'{}'"),
            ("EntryMode", "'CurrentClose'"), ("SizingMode", "'FixedRisk'") })
        {
            if (!existingCols.Contains(col))
            {
                cmd.CommandText = $"ALTER TABLE CustomPatterns ADD COLUMN {col} TEXT NOT NULL DEFAULT {def}";
                await cmd.ExecuteNonQueryAsync();
            }
        }
        if (!existingCols.Contains("EnableLiveTrading"))
        {
            cmd.CommandText = "ALTER TABLE CustomPatterns ADD COLUMN EnableLiveTrading INTEGER NOT NULL DEFAULT 0";
            await cmd.ExecuteNonQueryAsync();
        }
        if (!existingCols.Contains("TimeFrame"))
        {
            // TimeFrame.Daily = 3. 기존 전략은 기존 실행 방식과 같은 일봉으로 이관한다.
            cmd.CommandText = "ALTER TABLE CustomPatterns ADD COLUMN TimeFrame INTEGER NOT NULL DEFAULT 3";
            await cmd.ExecuteNonQueryAsync();
        }

        // 사용자 전략 이름을 신호 → 추천 → 포지션 → 거래까지 보존
        foreach (var table in new[] { "PatternSignals", "TradeRecommendations", "Positions", "TradeRecords" })
        {
            cmd.CommandText = $"PRAGMA table_info({table})";
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync()) columns.Add(reader.GetString(1));
            }
            if (!columns.Contains("CustomPatternName"))
            {
                cmd.CommandText = $"ALTER TABLE {table} ADD COLUMN CustomPatternName TEXT";
                await cmd.ExecuteNonQueryAsync();
            }
        }

        // OptimizationJobs 테이블 생성 (없는 경우)
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='OptimizationJobs'";
        if (await cmd.ExecuteScalarAsync() == null)
        {
            cmd.CommandText = @"
                CREATE TABLE OptimizationJobs (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL DEFAULT '',
                    Status INTEGER NOT NULL DEFAULT 0,
                    Priority INTEGER NOT NULL DEFAULT 0,
                    RequestJson TEXT NOT NULL DEFAULT '',
                    TotalCombinations INTEGER NOT NULL DEFAULT 0,
                    TestedCombinations INTEGER NOT NULL DEFAULT 0,
                    CurrentChunkIndex INTEGER NOT NULL DEFAULT 0,
                    ChunkSize INTEGER NOT NULL DEFAULT 200,
                    CreatedAt TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
                    StartedAt TEXT,
                    CompletedAt TEXT,
                    LastProgressAt TEXT,
                    MaxDurationHours REAL,
                    MaxTestedCombinations INTEGER,
                    RankBy TEXT NOT NULL DEFAULT 'sortinoRatio',
                    TopResultsToKeep INTEGER NOT NULL DEFAULT 50,
                    ContinuousMode INTEGER NOT NULL DEFAULT 0,
                    AutoApplyBestResult INTEGER NOT NULL DEFAULT 0,
                    AutoApplyMinTrades INTEGER NOT NULL DEFAULT 10,
                    AppliedResultCount INTEGER NOT NULL DEFAULT 0,
                    LastAutoAppliedAt TEXT,
                    LastAutoAppliedResultId INTEGER,
                    LastAutoApplyMessage TEXT,
                    ErrorMessage TEXT
                )";
            await cmd.ExecuteNonQueryAsync();
            cmd.CommandText = "CREATE INDEX IX_OptimizationJobs_Status_Priority ON OptimizationJobs (Status, Priority)";
            await cmd.ExecuteNonQueryAsync();
        }

        cmd.CommandText = "PRAGMA table_info(OptimizationJobs)";
        var optimizationJobColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var optimizationJobReader = await cmd.ExecuteReaderAsync())
        {
            while (await optimizationJobReader.ReadAsync())
                optimizationJobColumns.Add(optimizationJobReader.GetString(1));
        }

        var optimizationJobAlterStatements = new Dictionary<string, string>
        {
            ["ContinuousMode"] = "ALTER TABLE OptimizationJobs ADD COLUMN ContinuousMode INTEGER NOT NULL DEFAULT 0",
            ["AutoApplyBestResult"] = "ALTER TABLE OptimizationJobs ADD COLUMN AutoApplyBestResult INTEGER NOT NULL DEFAULT 0",
            ["AutoApplyMinTrades"] = "ALTER TABLE OptimizationJobs ADD COLUMN AutoApplyMinTrades INTEGER NOT NULL DEFAULT 10",
            ["AppliedResultCount"] = "ALTER TABLE OptimizationJobs ADD COLUMN AppliedResultCount INTEGER NOT NULL DEFAULT 0",
            ["LastAutoAppliedAt"] = "ALTER TABLE OptimizationJobs ADD COLUMN LastAutoAppliedAt TEXT",
            ["LastAutoAppliedResultId"] = "ALTER TABLE OptimizationJobs ADD COLUMN LastAutoAppliedResultId INTEGER",
            ["LastAutoApplyMessage"] = "ALTER TABLE OptimizationJobs ADD COLUMN LastAutoApplyMessage TEXT"
        };

        foreach (var (col, sql) in optimizationJobAlterStatements)
        {
            if (!optimizationJobColumns.Contains(col))
            {
                cmd.CommandText = sql;
                await cmd.ExecuteNonQueryAsync();
            }
        }

        // OptimizationResults 테이블 생성 (없는 경우)
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='OptimizationResults'";
        if (await cmd.ExecuteScalarAsync() == null)
        {
            cmd.CommandText = @"
                CREATE TABLE OptimizationResults (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    JobId INTEGER NOT NULL,
                    Rank INTEGER NOT NULL DEFAULT 0,
                    ParamsJson TEXT NOT NULL DEFAULT '',
                    TotalReturn REAL NOT NULL DEFAULT 0,
                    SortinoRatio REAL NOT NULL DEFAULT 0,
                    SharpeRatio REAL NOT NULL DEFAULT 0,
                    MaxDrawdown REAL NOT NULL DEFAULT 0,
                    WinRate REAL NOT NULL DEFAULT 0,
                    TotalTrades INTEGER NOT NULL DEFAULT 0,
                    ProfitFactor REAL NOT NULL DEFAULT 0,
                    CalmarRatio REAL NOT NULL DEFAULT 0,
                    AnnualizedReturn REAL NOT NULL DEFAULT 0,
                    OosTotalReturn REAL,
                    OosSortinoRatio REAL,
                    OosSharpeRatio REAL,
                    OosMaxDrawdown REAL,
                    OosWinRate REAL,
                    OosTotalTrades INTEGER,
                    OosProfitFactor REAL,
                    OosCalmarRatio REAL,
                    OosAnnualizedReturn REAL,
                    TestedAtCombination INTEGER NOT NULL DEFAULT 0,
                    DiscoveredAt TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
                    FOREIGN KEY (JobId) REFERENCES OptimizationJobs(Id) ON DELETE CASCADE
                )";
            await cmd.ExecuteNonQueryAsync();
            cmd.CommandText = "CREATE INDEX IX_OptimizationResults_JobId_Rank ON OptimizationResults (JobId, Rank)";
            await cmd.ExecuteNonQueryAsync();
        }

        // TQQQ 200SMA 스테일 시그널 정리: 비-TQQQ 종목의 시그널 비활성화
        // AllowedSymbols 기본값은 ["TQQQ"]이므로, 이전에 잘못 생성된 비-TQQQ 시그널을 정리
        cmd.CommandText = "UPDATE PatternSignals SET IsActive = 0 WHERE PatternType = 16 AND Symbol != 'TQQQ' AND IsActive = 1";
        var deactivated = await cmd.ExecuteNonQueryAsync();
        if (deactivated > 0)
        {
            var logger2 = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
            logger2.LogInformation("비-TQQQ Tqqq200Sma 시그널 {Count}건 비활성화", deactivated);
        }

        // ── 저품질 시그널/추천 정리 (SignalService 6단계 필터 소급 적용) ──
        // MinConfidence=0.3 기준 미달 시그널 비활성화
        cmd.CommandText = "UPDATE PatternSignals SET IsActive = 0 WHERE Confidence < 0.3 AND IsActive = 1";
        var lowConf = await cmd.ExecuteNonQueryAsync();
        // 가격 유효성 위반 시그널 비활성화 (SL >= Entry 또는 Target <= Entry)
        cmd.CommandText = "UPDATE PatternSignals SET IsActive = 0 WHERE (StopLossPrice >= EntryPrice OR TargetPrice <= EntryPrice) AND IsActive = 1";
        var badPrice = await cmd.ExecuteNonQueryAsync();
        // 미체결 추천 전체 제거 — 6단계 필터 소급 적용 위해 전부 삭제 후 재생성
        // (WasExecuted=1인 실제 체결 추천만 보존)
        cmd.CommandText = "DELETE FROM TradeRecommendations WHERE WasExecuted = 0";
        var badRecs = await cmd.ExecuteNonQueryAsync();
        if (lowConf + badPrice + badRecs > 0)
        {
            var logger3 = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
            logger3.LogInformation(
                "저품질 데이터 정리: 시그널 비활성화 {LowConf}건(저신뢰) + {BadPrice}건(가격위반), 추천 삭제 {BadRecs}건",
                lowConf, badPrice, badRecs);
        }

    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogWarning(ex, "스키마 마이그레이션 중 오류 발생 (무시하고 계속)");
    }

    // OptimizationJob 스타트업 복구: 비정상 종료로 Running 상태 남은 작업 → Pending 복귀
    try
    {
        var optRepo = scope.ServiceProvider.GetRequiredService<StockTrader.Data.Repositories.IOptimizationRepository>();
        await optRepo.ResetRunningJobsAsync();
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogWarning(ex, "OptimizationJob 스타트업 복구 중 오류 (무시하고 계속)");
    }

    // appsettings.json의 기존 Alpaca 설정으로 기본 계좌 시드 (계좌가 0개일 때만)
    try
    {
        var seedDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var accountCount = await seedDb.TradingAccounts.CountAsync();

        if (accountCount == 0)
        {
            var alpacaConfig = app.Configuration.GetSection("Alpaca");
            var apiKey = alpacaConfig["ApiKey"] ?? "";
            var apiSecret = alpacaConfig["ApiSecret"] ?? "";
            var isPaper = alpacaConfig.GetValue<bool>("IsPaper", true);

            // 플레이스홀더 키는 시드하지 않음
            if (!string.IsNullOrWhiteSpace(apiKey)
                && !apiKey.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase))
            {
                var accountManager = scope.ServiceProvider.GetRequiredService<StockTrader.Services.Account.IAccountManager>();
                await accountManager.AddAccountAsync(new StockTrader.Models.TradingAccount
                {
                    AccountName = isPaper ? "Alpaca Paper Trading" : "Alpaca Live Trading",
                    BrokerType = StockTrader.Models.Enums.BrokerType.Alpaca,
                    ApiKey = apiKey,
                    ApiSecret = apiSecret,
                    Environment = isPaper ? "Paper" : "Live",
                    IsActive = true,
                    IsEnabled = true,
                    Notes = "appsettings.json에서 자동 생성된 기본 계좌"
                });

                var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
                logger.LogInformation("Default Alpaca account seeded from appsettings.json");
            }
        }
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogWarning(ex, "기본 계좌 시드 중 오류 발생 (무시하고 계속)");
    }

    Directory.CreateDirectory(financialPipeline.GetResolvedImportDirectory());
}

// Configure the HTTP request pipeline.
// Both development and production branches register a logging exception handler so that
// unhandled exceptions are always recorded in structured logs regardless of environment.
if (app.Environment.IsDevelopment())
{
    // In development, log the full exception details to the console.
    app.UseExceptionHandler(errorApp =>
    {
        errorApp.Run(async context =>
        {
            var logger = context.RequestServices
                .GetRequiredService<ILogger<Program>>();
            var exceptionFeature = context.Features
                .Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();

            if (exceptionFeature != null)
            {
                logger.LogError(exceptionFeature.Error,
                    "Unhandled exception occurred (development)");
            }

            context.Response.StatusCode = 500;
            await context.Response.WriteAsync("An error occurred. Please try again later.");
        });
    });
}
else
{
    // In production, log then delegate to the Blazor error page.
    app.UseExceptionHandler(errorApp =>
    {
        errorApp.Run(async context =>
        {
            var logger = context.RequestServices
                .GetRequiredService<ILogger<Program>>();
            var exceptionFeature = context.Features
                .Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();

            if (exceptionFeature != null)
            {
                logger.LogError(exceptionFeature.Error,
                    "Unhandled exception occurred");
            }

            // Redirect to the Blazor error boundary page.
            context.Response.Redirect("/Error");
        });
    });

    // Note: UseHsts() is intentionally omitted here.
    // This app runs as a local exe on HTTP, so HSTS headers are not needed
    // and would cause browser issues if the user ever switches to HTTP.
}

// Security headers (before any response-generating middleware)
app.UseSecurityHeaders();

app.UseRateLimiter();

app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000}ms";
});

app.UseCors("DesktopUi");

// StatusCodePages 제거 — API 전용 서버로 404를 그대로 반환

// Only redirect to HTTPS in local development (not Docker).
if (app.Environment.IsDevelopment() && !app.Configuration.GetValue<bool>("DOTNET_RUNNING_IN_CONTAINER"))
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
 
// ── StockTrader REST API (Dashboard, Portfolio, Signals, Trades, Risk, Settings, Accounts, ML, Analysis) ──
app.MapStockTraderApi();

app.MapGet("/api/health", (IOptions<AlpacaSettings> alpaca) =>
    Results.Ok(new
    {
        status = "ok",
        service = "stocktrader-api",
        alpacaConfigured = alpaca.Value.HasConfiguredCredentials,
        timestamp = DateTimeOffset.UtcNow
    }));

// ── Auth API ──────────────────────────────────────────────────────────────────

app.MapPost("/api/auth/login", async (HttpContext ctx, IAuthService auth, IAuditService audit) =>
{
    string username, password;
    try
    {
        using var body   = await System.Text.Json.JsonDocument.ParseAsync(ctx.Request.Body);
        username = body.RootElement.GetProperty("username").GetString() ?? "";
        password = body.RootElement.GetProperty("password").GetString() ?? "";
    }
    catch
    {
        return Results.BadRequest(new { error = "Invalid JSON body. Provide 'username' and 'password'." });
    }

    var result = await auth.LoginAsync(username, password);
    if (!result.Success || result.Principal == null)
        return Results.Json(new { error = result.ErrorMessage }, statusCode: 401);

    await ctx.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        result.Principal,
        new AuthenticationProperties { IsPersistent = true });

    return Results.Ok(new { message = "로그인 성공", username });
}).RequireRateLimiting("login");

app.MapPost("/api/auth/logout", async (HttpContext ctx, IAuditService audit) =>
{
    var userId = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    if (int.TryParse(userId, out var uid))
        await audit.LogAsync(uid, "LOGOUT", "User signed out");
    return Results.Ok(new { message = "로그아웃 완료" });
});

app.MapGet("/api/auth/me", (HttpContext ctx) =>
{
    if (!ctx.User.Identity?.IsAuthenticated ?? true)
        return Results.Unauthorized();

    var userId = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
    var username = ctx.User.Identity?.Name ?? ctx.User.FindFirstValue(ClaimTypes.Name) ?? "";

    return Results.Ok(new
    {
        userId,
        username,
        authenticated = true
    });
});

app.MapGet("/api/auth/bootstrap", async (IAuthService auth,
    IOptionsMonitor<SecuritySettings> securityOptions) =>
{
    var hasUsers = await auth.HasAnyUserAsync();
    return Results.Ok(new
    {
        hasUsers,
        allowRegistration = !hasUsers || securityOptions.CurrentValue.AllowRegistration
    });
});

app.MapPost("/api/auth/register", async (HttpContext ctx, IAuthService auth,
    IOptionsMonitor<SecuritySettings> securityOptions) =>
{
    string username, password;
    try
    {
        using var body = await System.Text.Json.JsonDocument.ParseAsync(ctx.Request.Body);
        username = body.RootElement.GetProperty("username").GetString() ?? "";
        password = body.RootElement.GetProperty("password").GetString() ?? "";
    }
    catch
    {
        return Results.BadRequest(new { error = "Invalid JSON body." });
    }

    bool wasFirstUser = !await auth.HasAnyUserAsync();

    var result = await auth.RegisterAsync(username, password);
    if (!result.Success)
        return Results.BadRequest(new { error = result.ErrorMessage });

    // Auto-disable registration after the first user is created to prevent
    // unauthorized accounts from being added to a single-user system.
    if (wasFirstUser)
    {
        securityOptions.CurrentValue.AllowRegistration = false;
        var logger = ctx.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogInformation(
            "First user '{Username}' registered. AllowRegistration set to false.", username);
    }

    return Results.Ok(new { message = "사용자 등록 완료", userId = result.UserId });
}).RequireRateLimiting("login");

app.MapPost("/api/auth/change-password", async (HttpContext ctx, IAuthService auth) =>
{
    if (!ctx.User.Identity?.IsAuthenticated ?? true)
        return Results.Unauthorized();

    var userIdStr = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!int.TryParse(userIdStr, out var userId))
        return Results.Unauthorized();

    string oldPassword, newPassword;
    try
    {
        using var body = await System.Text.Json.JsonDocument.ParseAsync(ctx.Request.Body);
        oldPassword = body.RootElement.GetProperty("currentPassword").GetString() ?? "";
        newPassword = body.RootElement.GetProperty("newPassword").GetString() ?? "";
    }
    catch
    {
        return Results.BadRequest(new { error = "Invalid JSON body." });
    }

    var (success, error) = await auth.ChangePasswordAsync(userId, oldPassword, newPassword);
    if (!success)
        return Results.BadRequest(new { error });

    return Results.Ok(new { message = "비밀번호가 변경되었습니다." });
}).RequireRateLimiting("api").RequireAuthorization();

// ── Manual Trading API ────────────────────────────────────────────────────────

app.MapPost("/api/orders/execute-signal", async (HttpContext ctx, IOrderService orderService) =>
{
    if (!ctx.User.Identity?.IsAuthenticated ?? true)
        return Results.Unauthorized();

    long signalId;
    try
    {
        using var body = await System.Text.Json.JsonDocument.ParseAsync(ctx.Request.Body);
        signalId = body.RootElement.GetProperty("signalId").GetInt64();
    }
    catch
    {
        return Results.BadRequest(new { error = "Invalid JSON body. Provide 'signalId' (integer)." });
    }

    var (success, message) = await orderService.PlaceManualOrderAsync(signalId, ctx.RequestAborted);
    return success
        ? Results.Ok(new { message })
        : Results.BadRequest(new { error = message });
}).RequireRateLimiting("api").RequireAuthorization();

app.MapPost("/api/orders/close-position", async (HttpContext ctx, StockTrader.Services.Account.IAccountManager accountManager) =>
{
    if (!ctx.User.Identity?.IsAuthenticated ?? true)
        return Results.Unauthorized();

    string symbol;
    try
    {
        using var body = await System.Text.Json.JsonDocument.ParseAsync(ctx.Request.Body);
        symbol = body.RootElement.GetProperty("symbol").GetString() ?? "";
        if (string.IsNullOrWhiteSpace(symbol))
            return Results.BadRequest(new { error = "'symbol' must not be empty." });
    }
    catch
    {
        return Results.BadRequest(new { error = "Invalid JSON body. Provide 'symbol' (string)." });
    }

    var broker = await accountManager.GetActiveBrokerServiceAsync(ctx.RequestAborted);
    if (broker == null)
        return Results.BadRequest(new { error = "활성 브로커 계좌가 없습니다. 계좌 관리에서 계좌를 설정하세요." });

    var success = await broker.ClosePositionAsync(symbol, ctx.RequestAborted);
    return success
        ? Results.Ok(new { message = $"{symbol} 청산 완료" })
        : Results.BadRequest(new { error = $"{symbol} 청산 실패. 브로커 연결 상태 또는 보유 포지션을 확인하세요." });
}).RequireRateLimiting("api").RequireAuthorization();

// ── Backtest API ──
app.MapPost("/api/backtest", async (BacktestRequest request, IBacktestService svc, CancellationToken ct) =>
{
    if (string.Equals(request.BacktestMode, "weight", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new { error = "weight 백테스트 모드는 제거되었습니다. 패턴 백테스트 또는 패턴 빌더를 사용하세요." });
    }

    var result = await svc.RunAsync(request, ct);

    // 에퀴티 커브 다운샘플링 (300포인트 이하)
    var equityCurve = result.EquityCurve;
    if (equityCurve.Count > 300)
    {
        var sampled = new List<EquityPoint>(300);
        sampled.Add(equityCurve[0]);
        var step = (double)(equityCurve.Count - 1) / 299;
        for (int i = 1; i < 299; i++)
            sampled.Add(equityCurve[(int)Math.Round(i * step)]);
        sampled.Add(equityCurve[^1]);
        equityCurve = sampled;
    }

    return Results.Ok(new
    {
        result.TotalTrades,
        TotalReturn = result.TotalReturnPercent,
        result.MaxDrawdown,
        result.SharpeRatio,
        result.OverallWinRate,
        result.TotalSlippageCost,
        result.TotalCommissionCost,
        result.ErrorMessage,
        result.Warnings,
        result.WeightStrategyApplied,
        result.WeightReducedTrades,
        UsedTimeFrame = result.UsedTimeFrame.ToString(),
        ActualDataFrom = result.ActualDataFrom?.ToString("yyyy-MM-dd"),
        PerPattern = result.PerPatternStats.ToDictionary(
            kv => kv.Key.ToString(),
            kv => new { kv.Value.SampleSize, kv.Value.WinRate, kv.Value.AvgWinPercent, kv.Value.AvgLossPercent, kv.Value.Expectancy, kv.Value.ProfitFactor }),
        PerStrategy = result.PerStrategyStats.ToDictionary(
            kv => kv.Key,
            kv => new { kv.Value.SampleSize, kv.Value.WinRate, kv.Value.AvgWinPercent, kv.Value.AvgLossPercent, kv.Value.Expectancy, kv.Value.ProfitFactor }),
        PerSymbol = result.PerSymbolStats.Select(s => new
        {
            s.Symbol, s.TradeCount, s.WinRate, s.TotalPnL, s.AvgPnLPercent
        }),
        EquityCurve = equityCurve.Select(e => new { Date = e.Date.ToString("yyyy-MM-dd"), e.Equity }),
        Trades = result.Trades.Select(t => new
        {
            t.Symbol, Pattern = t.PatternType.ToString(), t.CustomPatternName,
            EntryTime = t.EntryTime.ToString("yyyy-MM-dd"), ExitTime = t.ExitTime.ToString("yyyy-MM-dd"),
            t.EntryPrice, t.ExitPrice, ReturnPct = t.EntryPrice > 0 ? (t.ExitPrice - t.EntryPrice) / t.EntryPrice : 0m,
            t.ExitReason
        }),
        WalkForward = result.WalkForward != null ? new
        {
            result.WalkForward.AggregateOosReturnPercent,
            result.WalkForward.AggregateOosMaxDrawdown,
            result.WalkForward.AggregateOosWinRate,
            result.WalkForward.AggregateOosSharpe,
            result.WalkForward.WalkForwardEfficiency,
            Windows = result.WalkForward.Windows.Select(w => new
            {
                IsFrom = w.InSampleFrom.ToString("yyyy-MM-dd"), IsTo = w.InSampleTo.ToString("yyyy-MM-dd"),
                OosFrom = w.OutOfSampleFrom.ToString("yyyy-MM-dd"), OosTo = w.OutOfSampleTo.ToString("yyyy-MM-dd"),
                w.InSampleTrades, w.InSampleReturnPercent,
                w.OutOfSampleTrades, w.OutOfSampleReturnPercent, w.OutOfSampleMaxDrawdown, w.Efficiency
            })
        } : null,
        MonteCarlo = result.MonteCarlo != null ? new
        {
            result.MonteCarlo.Simulations,
            result.MonteCarlo.MedianFinalEquity,
            result.MonteCarlo.MeanFinalEquity,
            result.MonteCarlo.Percentile5Equity,
            result.MonteCarlo.Percentile25Equity,
            result.MonteCarlo.Percentile75Equity,
            result.MonteCarlo.Percentile95Equity,
            result.MonteCarlo.MedianMaxDrawdown,
            result.MonteCarlo.WorstCaseMaxDrawdown,
            result.MonteCarlo.ProbabilityOfLoss,
        } : null,
    });
}).RequireAuthorization();

// ── 실거래 파라미터 적용 API ──
app.MapPost("/api/backtest/apply-live", async (
    ApplyLiveRequest req,
    ILiveParameterService liveSvc,
    CancellationToken ct) =>
{
    await liveSvc.ApplyToLiveAsync(
        req.ParameterOverrides ?? new PatternParameterOverrides(),
        req.EnabledPatterns?.Select(p => Enum.Parse<PatternType>(p)).ToList() ?? new(),
        req.RiskPerTradePercent ?? 0.01m,
        req.DailyLossLimitPercent ?? 0.03m,
        req.MaxTotalPositions ?? 7,
        req.MaxPositionsPerSector ?? 2,
        ct);
    return Results.Ok(new { message = "실거래 파라미터가 적용되었습니다." });
}).RequireAuthorization();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// 데스크톱 Svelte UI와 별개로 기존 Blazor 운영 UI도 유지한다.
// /api/* 는 REST API, 그 외 라우트는 기존 운영 화면으로 제공된다.

app.Run();

}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// ── 요청 DTO ──
public record ApplyLiveRequest(
    PatternParameterOverrides? ParameterOverrides,
    List<string>? EnabledPatterns,
    decimal? RiskPerTradePercent,
    decimal? DailyLossLimitPercent,
    int? MaxTotalPositions,
    int? MaxPositionsPerSector
);
