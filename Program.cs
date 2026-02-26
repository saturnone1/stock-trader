using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using StockTrader.Components;
using StockTrader.Data;
using StockTrader.Extensions;

var builder = WebApplication.CreateBuilder(args);

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

        await conn.CloseAsync();
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogWarning(ex, "UserSettings 스키마 마이그레이션 중 오류 발생 (무시하고 계속)");
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

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// Only redirect to HTTPS in development where the dev certificate is available.
// In production (published exe), we typically run on plain HTTP on localhost,
// so redirecting would cause a connection failure and blank/broken UI.
if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
