using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using Serilog;
using StockTrader.Components;
using StockTrader.Data;
using StockTrader.Extensions;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Backtest;

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

// Add StockTrader services (DI, DB, background services, etc.)
builder.Services.AddStockTraderServices(builder.Configuration);

var app = builder.Build();

// Ensure DB is created and apply lightweight schema migrations
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
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

        // 누락된 리스크 컬럼 추가
        var alterStatements = new Dictionary<string, string>
        {
            ["RiskPerTradePercent"]   = "ALTER TABLE UserSettings ADD COLUMN RiskPerTradePercent REAL NOT NULL DEFAULT 0.01",
            ["DailyLossLimitPercent"] = "ALTER TABLE UserSettings ADD COLUMN DailyLossLimitPercent REAL NOT NULL DEFAULT 0.03",
            ["MaxTotalPositions"]     = "ALTER TABLE UserSettings ADD COLUMN MaxTotalPositions INTEGER NOT NULL DEFAULT 10",
            ["MaxPositionsPerSector"] = "ALTER TABLE UserSettings ADD COLUMN MaxPositionsPerSector INTEGER NOT NULL DEFAULT 2",
            ["MinExpectancy"]         = "ALTER TABLE UserSettings ADD COLUMN MinExpectancy REAL NOT NULL DEFAULT 0.0",
        };

        foreach (var (col, sql) in alterStatements)
        {
            if (!existingColumns.Contains(col))
            {
                cmd.CommandText = sql;
                await cmd.ExecuteNonQueryAsync();
            }
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

    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogWarning(ex, "스키마 마이그레이션 중 오류 발생 (무시하고 계속)");
    }

    // appsettings.json의 기존 Alpaca 설정으로 기본 계좌 시드 (계좌가 0개일 때만)
    try
    {
        var seedDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var accountCount = seedDb.TradingAccounts.Count();

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

app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000}ms";
});

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// Only redirect to HTTPS in development where the dev certificate is available.
// In production (published exe), we typically run on plain HTTP on localhost,
// so redirecting would cause a connection failure and blank/broken UI.
if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAntiforgery();

// ── Backtest API (CLI에서 수익률 비교용) ──
app.MapPost("/api/backtest", async (BacktestRequest request, IBacktestService svc, CancellationToken ct) =>
{
    var result = await svc.RunAsync(request, ct);
    return Results.Ok(new
    {
        result.TotalTrades,
        TotalReturn = result.TotalReturnPercent.ToString("P2"),
        result.MaxDrawdown,
        result.SharpeRatio,
        result.OverallWinRate,
        PerPattern = result.PerPatternStats.ToDictionary(
            kv => kv.Key.ToString(),
            kv => new { kv.Value.SampleSize, WinRate = kv.Value.WinRate.ToString("P1"), kv.Value.AvgWinPercent, kv.Value.AvgLossPercent })
    });
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

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
