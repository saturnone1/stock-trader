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
using StockTrader.Data.Repositories;
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

// Apply ordered, transactional database migrations before any background service uses the schema.
using (var scope = app.Services.CreateScope())
{
    var financialPipeline = scope.ServiceProvider.GetRequiredService<FinancialSnapshotIngestionService>();
    var migrations = scope.ServiceProvider.GetRequiredService<StockTrader.Data.Migrations.DatabaseMigrationRunner>();
    await migrations.MigrateAsync();

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

app.MapPost("/api/orders/close-position", async (
    HttpContext ctx,
    StockTrader.Services.Account.IAccountManager accountManager,
    ITradeRepository trades,
    StockTrader.Services.Order.ILivePositionExitCoordinator exitCoordinator) =>
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

    var positions = await trades.GetOpenPositionsAsync(ctx.RequestAborted);
    var matchingPositions = positions.Where(item =>
        item.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase)).ToArray();
    if (matchingPositions.Length == 0)
        return Results.BadRequest(new { error = $"{symbol}의 관리 중인 오픈 포지션을 찾을 수 없습니다." });
    if (matchingPositions.Length > 1)
        return Results.BadRequest(new { error = $"{symbol} 포지션이 여러 계좌에 있어 계좌별 청산 기능이 필요합니다." });

    var submission = await exitCoordinator.SubmitAsync(
        matchingPositions[0], "사용자 수동 청산", broker, ctx.RequestAborted);
    return submission.Status is StockTrader.Services.Order.LiveExitSubmissionStatus.Accepted
        or StockTrader.Services.Order.LiveExitSubmissionStatus.AlreadyPending
        ? Results.Ok(new { message = $"{symbol} 청산 주문 접수됨" })
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
