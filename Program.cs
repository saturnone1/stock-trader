using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MudBlazor.Services;
using Serilog;
using StockTrader.Components;
using StockTrader.Configuration;
using StockTrader.Data;
using StockTrader.Extensions;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Auth;
using StockTrader.Services.Backtest;
using StockTrader.Services.Order;

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

// Add security services (cookie auth, crypto, rate limiting)
builder.Services.AddSecurityServices(builder.Configuration);

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

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// Only redirect to HTTPS in local development (not Docker).
if (app.Environment.IsDevelopment() && !app.Configuration.GetValue<bool>("DOTNET_RUNNING_IN_CONTAINER"))
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

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
            kv => new { kv.Value.SampleSize, WinRate = kv.Value.WinRate.ToString("P1"), kv.Value.AvgWinPercent, kv.Value.AvgLossPercent }),
        Trades = result.Trades.Select(t => new
        {
            t.Symbol, Pattern = t.PatternType.ToString(),
            EntryTime = t.EntryTime.ToString("yyyy-MM-dd"), ExitTime = t.ExitTime.ToString("yyyy-MM-dd"),
            t.EntryPrice, t.ExitPrice, ReturnPct = ((t.ExitPrice - t.EntryPrice) / t.EntryPrice).ToString("P2"),
            t.ExitReason
        })
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
