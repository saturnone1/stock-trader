using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockTrader.Data.EfMigrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppUsers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Username = table.Column<string>(type: "TEXT", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    Salt = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    FailedLoginAttempts = table.Column<int>(type: "INTEGER", nullable: false),
                    LockedUntil = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: true),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    Details = table.Column<string>(type: "TEXT", nullable: false),
                    IpAddress = table.Column<string>(type: "TEXT", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomPatterns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    EntryRulesJson = table.Column<string>(type: "TEXT", nullable: false),
                    EntryLogic = table.Column<string>(type: "TEXT", nullable: false),
                    RequireBullRegime = table.Column<bool>(type: "INTEGER", nullable: false),
                    AtrStopMultiplier = table.Column<decimal>(type: "TEXT", nullable: false),
                    AtrTargetMultiplier = table.Column<decimal>(type: "TEXT", nullable: false),
                    MaxHoldingBars = table.Column<int>(type: "INTEGER", nullable: false),
                    TrailingAtr = table.Column<decimal>(type: "TEXT", nullable: false),
                    PartialProfitR = table.Column<decimal>(type: "TEXT", nullable: false),
                    UseWeightTiers = table.Column<bool>(type: "INTEGER", nullable: false),
                    WeightTiersJson = table.Column<string>(type: "TEXT", nullable: false),
                    DefaultAllocationPercent = table.Column<decimal>(type: "TEXT", nullable: false),
                    ExitRulesJson = table.Column<string>(type: "TEXT", nullable: false),
                    ExitRulesLogic = table.Column<string>(type: "TEXT", nullable: false),
                    ExitGroupsJson = table.Column<string>(type: "TEXT", nullable: false),
                    ExitGroupsLogic = table.Column<string>(type: "TEXT", nullable: false),
                    ScalingRulesJson = table.Column<string>(type: "TEXT", nullable: false),
                    TimeFilterJson = table.Column<string>(type: "TEXT", nullable: false),
                    CircuitBreakerJson = table.Column<string>(type: "TEXT", nullable: false),
                    ReentryJson = table.Column<string>(type: "TEXT", nullable: false),
                    PortfolioRulesJson = table.Column<string>(type: "TEXT", nullable: false),
                    EntryGroupsJson = table.Column<string>(type: "TEXT", nullable: false),
                    EntryGroupsLogic = table.Column<string>(type: "TEXT", nullable: false),
                    DynamicExitJson = table.Column<string>(type: "TEXT", nullable: false),
                    EntryMode = table.Column<string>(type: "TEXT", nullable: false),
                    TimeFrame = table.Column<int>(type: "INTEGER", nullable: false),
                    SizingMode = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    EnableLiveTrading = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomPatterns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FinancialImportRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceType = table.Column<string>(type: "TEXT", nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", nullable: false),
                    Fingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    ImportedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SkippedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialImportRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FinancialSnapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Symbol = table.Column<string>(type: "TEXT", nullable: false),
                    AsOfDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    PeRatio = table.Column<decimal>(type: "TEXT", nullable: true),
                    PbRatio = table.Column<decimal>(type: "TEXT", nullable: true),
                    RoePercent = table.Column<decimal>(type: "TEXT", nullable: true),
                    OperatingMarginPercent = table.Column<decimal>(type: "TEXT", nullable: true),
                    RevenueCurrent = table.Column<decimal>(type: "TEXT", nullable: true),
                    RevenuePrevious = table.Column<decimal>(type: "TEXT", nullable: true),
                    OperatingIncomeCurrent = table.Column<decimal>(type: "TEXT", nullable: true),
                    OperatingIncomePrevious = table.Column<decimal>(type: "TEXT", nullable: true),
                    NetIncomeCurrent = table.Column<decimal>(type: "TEXT", nullable: true),
                    NetIncomePrevious = table.Column<decimal>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OhlcvBars",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Symbol = table.Column<string>(type: "TEXT", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TimeFrame = table.Column<int>(type: "INTEGER", nullable: false),
                    Open = table.Column<decimal>(type: "TEXT", nullable: false),
                    High = table.Column<decimal>(type: "TEXT", nullable: false),
                    Low = table.Column<decimal>(type: "TEXT", nullable: false),
                    Close = table.Column<decimal>(type: "TEXT", nullable: false),
                    Volume = table.Column<long>(type: "INTEGER", nullable: false),
                    Vwap = table.Column<decimal>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OhlcvBars", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OptimizationJobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    RequestJson = table.Column<string>(type: "TEXT", nullable: false),
                    TotalCombinations = table.Column<long>(type: "INTEGER", nullable: false),
                    TestedCombinations = table.Column<long>(type: "INTEGER", nullable: false),
                    CurrentChunkIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    ChunkSize = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastProgressAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    MaxDurationHours = table.Column<decimal>(type: "TEXT", nullable: true),
                    MaxTestedCombinations = table.Column<long>(type: "INTEGER", nullable: true),
                    RankBy = table.Column<string>(type: "TEXT", nullable: false),
                    TopResultsToKeep = table.Column<int>(type: "INTEGER", nullable: false),
                    ContinuousMode = table.Column<bool>(type: "INTEGER", nullable: false),
                    AutoApplyBestResult = table.Column<bool>(type: "INTEGER", nullable: false),
                    AutoApplyMinTrades = table.Column<int>(type: "INTEGER", nullable: false),
                    AppliedResultCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastAutoAppliedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastAutoAppliedResultId = table.Column<int>(type: "INTEGER", nullable: true),
                    LastAutoApplyMessage = table.Column<string>(type: "TEXT", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OptimizationJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PatternSignals",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Symbol = table.Column<string>(type: "TEXT", nullable: false),
                    PatternType = table.Column<int>(type: "INTEGER", nullable: false),
                    CustomPatternName = table.Column<string>(type: "TEXT", nullable: true),
                    DetectedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EntryPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                    StopLossPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                    TargetPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                    Confidence = table.Column<decimal>(type: "TEXT", nullable: false),
                    Details = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatternSignals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PatternStats",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PatternType = table.Column<int>(type: "INTEGER", nullable: false),
                    Symbol = table.Column<string>(type: "TEXT", nullable: true),
                    SampleSize = table.Column<int>(type: "INTEGER", nullable: false),
                    WinRate = table.Column<decimal>(type: "TEXT", nullable: false),
                    AvgWinPercent = table.Column<decimal>(type: "TEXT", nullable: false),
                    AvgLossPercent = table.Column<decimal>(type: "TEXT", nullable: false),
                    MaxDrawdownPercent = table.Column<decimal>(type: "TEXT", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatternStats", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Positions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AccountId = table.Column<int>(type: "INTEGER", nullable: false),
                    Symbol = table.Column<string>(type: "TEXT", nullable: false),
                    Sector = table.Column<string>(type: "TEXT", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    EntryPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                    CurrentPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                    StopLossPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                    TargetPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                    PatternType = table.Column<int>(type: "INTEGER", nullable: false),
                    CustomPatternName = table.Column<string>(type: "TEXT", nullable: true),
                    OpenedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ExitPrice = table.Column<decimal>(type: "TEXT", nullable: true),
                    HighSinceEntry = table.Column<decimal>(type: "TEXT", nullable: false),
                    EntryAtr = table.Column<decimal>(type: "TEXT", nullable: false),
                    InitialRiskDistance = table.Column<decimal>(type: "TEXT", nullable: false),
                    BreakevenApplied = table.Column<bool>(type: "INTEGER", nullable: false),
                    TrailingStopActivated = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExitRequestedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ExitRequestReason = table.Column<string>(type: "TEXT", nullable: true),
                    ExitOrderId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Positions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SymbolProfiles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Symbol = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    EnabledPatterns = table.Column<string>(type: "TEXT", nullable: false),
                    ParameterOverridesJson = table.Column<string>(type: "TEXT", nullable: true),
                    WeightStrategyJson = table.Column<string>(type: "TEXT", nullable: true),
                    RiskPerTradePercent = table.Column<decimal>(type: "TEXT", nullable: false),
                    MaxTotalPositions = table.Column<int>(type: "INTEGER", nullable: false),
                    BacktestReturnPct = table.Column<decimal>(type: "TEXT", nullable: true),
                    BacktestWinRate = table.Column<decimal>(type: "TEXT", nullable: true),
                    BacktestMaxDrawdown = table.Column<decimal>(type: "TEXT", nullable: true),
                    BacktestSharpe = table.Column<decimal>(type: "TEXT", nullable: true),
                    BacktestTrades = table.Column<int>(type: "INTEGER", nullable: true),
                    BacktestFrom = table.Column<DateTime>(type: "TEXT", nullable: true),
                    BacktestTo = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SymbolProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tickers",
                columns: table => new
                {
                    Symbol = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Sector = table.Column<string>(type: "TEXT", nullable: false),
                    Industry = table.Column<string>(type: "TEXT", nullable: false),
                    MarketCap = table.Column<decimal>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tickers", x => x.Symbol);
                });

            migrationBuilder.CreateTable(
                name: "TradeRecommendations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Symbol = table.Column<string>(type: "TEXT", nullable: false),
                    PatternType = table.Column<int>(type: "INTEGER", nullable: false),
                    CustomPatternName = table.Column<string>(type: "TEXT", nullable: true),
                    GeneratedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EntryPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                    StopLossPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                    TargetPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                    PositionSize = table.Column<decimal>(type: "TEXT", nullable: false),
                    ShareQuantity = table.Column<int>(type: "INTEGER", nullable: false),
                    Expectancy = table.Column<decimal>(type: "TEXT", nullable: false),
                    WasExecuted = table.Column<bool>(type: "INTEGER", nullable: false),
                    Mode = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeRecommendations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TradeRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Symbol = table.Column<string>(type: "TEXT", nullable: false),
                    PatternType = table.Column<int>(type: "INTEGER", nullable: false),
                    CustomPatternName = table.Column<string>(type: "TEXT", nullable: true),
                    EntryPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                    ExitPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    EntryTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExitTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PnL = table.Column<decimal>(type: "TEXT", nullable: false),
                    PnLPercent = table.Column<decimal>(type: "TEXT", nullable: false),
                    ExitReason = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TradingAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AccountName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    BrokerType = table.Column<int>(type: "INTEGER", nullable: false),
                    ApiKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ApiSecret = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Environment = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastConnectedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradingAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserSettings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OrderMode = table.Column<int>(type: "INTEGER", nullable: false),
                    PreferredDataSource = table.Column<int>(type: "INTEGER", nullable: false),
                    EnabledPatterns = table.Column<string>(type: "TEXT", nullable: false),
                    WatchlistSymbols = table.Column<string>(type: "TEXT", nullable: false),
                    SoundAlerts = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastModified = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AccountSize = table.Column<decimal>(type: "TEXT", nullable: false),
                    RiskPerTradePercent = table.Column<decimal>(type: "TEXT", nullable: false),
                    DailyLossLimitPercent = table.Column<decimal>(type: "TEXT", nullable: false),
                    MaxTotalPositions = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxPositionsPerSector = table.Column<int>(type: "INTEGER", nullable: false),
                    MinExpectancy = table.Column<decimal>(type: "TEXT", nullable: false),
                    LiveParameterOverridesJson = table.Column<string>(type: "TEXT", nullable: true),
                    EnableTelegram = table.Column<bool>(type: "INTEGER", nullable: true),
                    TelegramBotToken = table.Column<string>(type: "TEXT", nullable: true),
                    TelegramChatId = table.Column<string>(type: "TEXT", nullable: true),
                    EnableDiscord = table.Column<bool>(type: "INTEGER", nullable: true),
                    DiscordWebhookUrl = table.Column<string>(type: "TEXT", nullable: true),
                    EnableEmail = table.Column<bool>(type: "INTEGER", nullable: true),
                    SmtpHost = table.Column<string>(type: "TEXT", nullable: true),
                    SmtpPort = table.Column<int>(type: "INTEGER", nullable: true),
                    SmtpUseSsl = table.Column<bool>(type: "INTEGER", nullable: true),
                    SmtpUsername = table.Column<string>(type: "TEXT", nullable: true),
                    SmtpPassword = table.Column<string>(type: "TEXT", nullable: true),
                    EmailFrom = table.Column<string>(type: "TEXT", nullable: true),
                    EmailTo = table.Column<string>(type: "TEXT", nullable: true),
                    DailyReportTimeKst = table.Column<string>(type: "TEXT", nullable: true),
                    Tqqq200SmaAllowedSymbols = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OptimizationResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    JobId = table.Column<int>(type: "INTEGER", nullable: false),
                    Rank = table.Column<int>(type: "INTEGER", nullable: false),
                    ParamsJson = table.Column<string>(type: "TEXT", nullable: false),
                    TotalReturn = table.Column<decimal>(type: "TEXT", nullable: false),
                    SortinoRatio = table.Column<decimal>(type: "TEXT", nullable: false),
                    SharpeRatio = table.Column<decimal>(type: "TEXT", nullable: false),
                    MaxDrawdown = table.Column<decimal>(type: "TEXT", nullable: false),
                    WinRate = table.Column<decimal>(type: "TEXT", nullable: false),
                    TotalTrades = table.Column<int>(type: "INTEGER", nullable: false),
                    ProfitFactor = table.Column<decimal>(type: "TEXT", nullable: false),
                    CalmarRatio = table.Column<decimal>(type: "TEXT", nullable: false),
                    AnnualizedReturn = table.Column<decimal>(type: "TEXT", nullable: false),
                    OosTotalReturn = table.Column<decimal>(type: "TEXT", nullable: true),
                    OosSortinoRatio = table.Column<decimal>(type: "TEXT", nullable: true),
                    OosSharpeRatio = table.Column<decimal>(type: "TEXT", nullable: true),
                    OosMaxDrawdown = table.Column<decimal>(type: "TEXT", nullable: true),
                    OosWinRate = table.Column<decimal>(type: "TEXT", nullable: true),
                    OosTotalTrades = table.Column<int>(type: "INTEGER", nullable: true),
                    OosProfitFactor = table.Column<decimal>(type: "TEXT", nullable: true),
                    OosCalmarRatio = table.Column<decimal>(type: "TEXT", nullable: true),
                    OosAnnualizedReturn = table.Column<decimal>(type: "TEXT", nullable: true),
                    TestedAtCombination = table.Column<long>(type: "INTEGER", nullable: false),
                    DiscoveredAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OptimizationResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OptimizationResults_OptimizationJobs_JobId",
                        column: x => x.JobId,
                        principalTable: "OptimizationJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_IsActive",
                table: "AppUsers",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_Username",
                table: "AppUsers",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Action",
                table: "AuditLogs",
                column: "Action");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Timestamp",
                table: "AuditLogs",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserId",
                table: "AuditLogs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomPatterns_IsActive",
                table: "CustomPatterns",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_CustomPatterns_Name",
                table: "CustomPatterns",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialImportRuns_FilePath_Fingerprint",
                table: "FinancialImportRuns",
                columns: new[] { "FilePath", "Fingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialImportRuns_StartedAt",
                table: "FinancialImportRuns",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialImportRuns_Status",
                table: "FinancialImportRuns",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialSnapshots_AsOfDate",
                table: "FinancialSnapshots",
                column: "AsOfDate");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialSnapshots_Symbol",
                table: "FinancialSnapshots",
                column: "Symbol");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialSnapshots_Symbol_AsOfDate",
                table: "FinancialSnapshots",
                columns: new[] { "Symbol", "AsOfDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OhlcvBars_Symbol_TimeFrame_Timestamp",
                table: "OhlcvBars",
                columns: new[] { "Symbol", "TimeFrame", "Timestamp" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OhlcvBars_TimeFrame_Symbol_Timestamp",
                table: "OhlcvBars",
                columns: new[] { "TimeFrame", "Symbol", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_OptimizationJobs_Status_Priority",
                table: "OptimizationJobs",
                columns: new[] { "Status", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_OptimizationResults_JobId_Rank",
                table: "OptimizationResults",
                columns: new[] { "JobId", "Rank" });

            migrationBuilder.CreateIndex(
                name: "IX_PatternSignals_Symbol_PatternType_DetectedAt",
                table: "PatternSignals",
                columns: new[] { "Symbol", "PatternType", "DetectedAt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PatternStats_PatternType_Symbol",
                table: "PatternStats",
                columns: new[] { "PatternType", "Symbol" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Positions_ClosedAt",
                table: "Positions",
                column: "ClosedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Positions_Symbol",
                table: "Positions",
                column: "Symbol");

            migrationBuilder.CreateIndex(
                name: "IX_SymbolProfiles_Symbol_IsActive",
                table: "SymbolProfiles",
                columns: new[] { "Symbol", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_SymbolProfiles_Symbol_Name",
                table: "SymbolProfiles",
                columns: new[] { "Symbol", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tickers_Sector",
                table: "Tickers",
                column: "Sector");

            migrationBuilder.CreateIndex(
                name: "IX_TradeRecommendations_GeneratedAt",
                table: "TradeRecommendations",
                column: "GeneratedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TradeRecords_PatternType_EntryTime",
                table: "TradeRecords",
                columns: new[] { "PatternType", "EntryTime" });

            migrationBuilder.CreateIndex(
                name: "IX_TradingAccounts_BrokerType",
                table: "TradingAccounts",
                column: "BrokerType");

            migrationBuilder.CreateIndex(
                name: "IX_TradingAccounts_IsActive",
                table: "TradingAccounts",
                column: "IsActive");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppUsers");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "CustomPatterns");

            migrationBuilder.DropTable(
                name: "FinancialImportRuns");

            migrationBuilder.DropTable(
                name: "FinancialSnapshots");

            migrationBuilder.DropTable(
                name: "OhlcvBars");

            migrationBuilder.DropTable(
                name: "OptimizationResults");

            migrationBuilder.DropTable(
                name: "PatternSignals");

            migrationBuilder.DropTable(
                name: "PatternStats");

            migrationBuilder.DropTable(
                name: "Positions");

            migrationBuilder.DropTable(
                name: "SymbolProfiles");

            migrationBuilder.DropTable(
                name: "Tickers");

            migrationBuilder.DropTable(
                name: "TradeRecommendations");

            migrationBuilder.DropTable(
                name: "TradeRecords");

            migrationBuilder.DropTable(
                name: "TradingAccounts");

            migrationBuilder.DropTable(
                name: "UserSettings");

            migrationBuilder.DropTable(
                name: "OptimizationJobs");
        }
    }
}
