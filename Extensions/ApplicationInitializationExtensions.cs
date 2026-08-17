using Microsoft.EntityFrameworkCore;
using StockTrader.BackgroundServices;
using StockTrader.Data;
using StockTrader.Data.Migrations;
using StockTrader.Data.Repositories;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Account;

namespace StockTrader.Extensions;

public static class ApplicationInitializationExtensions
{
    public static async Task<bool> MigrateDatabaseOnlyAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        await services.GetRequiredService<DatabaseSchemaMigrator>().MigrateAsync();
        var status = await services.GetRequiredService<DatabaseMigrationStatusProvider>()
            .GetAsync(CancellationToken.None);
        return status.IsSynchronized;
    }

    public static async Task<bool> VerifyDatabaseMigrationsAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var status = await scope.ServiceProvider
            .GetRequiredService<DatabaseMigrationStatusProvider>()
            .GetAsync(CancellationToken.None);
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(ApplicationInitializationExtensions));
        if (status.IsSynchronized)
        {
            logger.LogInformation(
                "Database migration verification passed at {MigrationId}",
                status.Current);
            return true;
        }

        logger.LogError(
            "Database migration verification failed: current={Current}, latest={Latest}, pending={PendingCount}",
            status.Current,
            status.Latest,
            status.PendingCount);
        return false;
    }

    public static async Task InitializeStockTraderAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        var logger = services.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(ApplicationInitializationExtensions));

        await services.GetRequiredService<DatabaseSchemaMigrator>().MigrateAsync();
        await RecoverOptimizationJobsAsync(services, logger);
        await SeedDefaultAlpacaAccountAsync(app.Configuration, services, logger);

        var financialPipeline = services.GetRequiredService<FinancialSnapshotIngestionService>();
        Directory.CreateDirectory(financialPipeline.GetResolvedImportDirectory());
    }

    private static async Task RecoverOptimizationJobsAsync(
        IServiceProvider services,
        ILogger logger)
    {
        try
        {
            await services.GetRequiredService<IOptimizationRepository>().ResetRunningJobsAsync();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "OptimizationJob 스타트업 복구 중 오류 (무시하고 계속)");
        }
    }

    private static async Task SeedDefaultAlpacaAccountAsync(
        IConfiguration configuration,
        IServiceProvider services,
        ILogger logger)
    {
        try
        {
            if (await services.GetRequiredService<AppDbContext>().TradingAccounts.CountAsync() != 0)
                return;

            var alpaca = configuration.GetSection("Alpaca");
            var apiKey = alpaca["ApiKey"] ?? "";
            if (string.IsNullOrWhiteSpace(apiKey)
                || apiKey.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase))
                return;

            var isPaper = alpaca.GetValue("IsPaper", true);
            await services.GetRequiredService<IAccountManager>().AddAccountAsync(new TradingAccount
            {
                AccountName = isPaper ? "Alpaca Paper Trading" : "Alpaca Live Trading",
                BrokerType = BrokerType.Alpaca,
                ApiKey = apiKey,
                ApiSecret = alpaca["ApiSecret"] ?? "",
                Environment = isPaper ? "Paper" : "Live",
                IsActive = true,
                IsEnabled = true,
                Notes = "appsettings.json에서 자동 생성된 기본 계좌",
            });
            logger.LogInformation("Default Alpaca account seeded from appsettings.json");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "기본 계좌 시드 중 오류 발생 (무시하고 계속)");
        }
    }
}
