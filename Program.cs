using System.Text.Json.Serialization;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Serilog;
using StockTrader.Api;
using StockTrader.Configuration;
using StockTrader.Extensions;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var isOpenApiGeneration =
        Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";
    var builder = WebApplication.CreateBuilder(args);
    if (!isOpenApiGeneration && !builder.Environment.IsDevelopment())
        builder.Configuration.AddUserSecrets<Program>(optional: true);

    var edgeControl = builder.Configuration
        .GetSection(EdgeTransitionControlOptions.SectionName)
        .Get<EdgeTransitionControlOptions>() ?? new EdgeTransitionControlOptions();
    if (!isOpenApiGeneration && edgeControl.Enabled)
    {
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenAnyIP(8080);
            options.ListenAnyIP(EdgeTransitionControlOptions.InternalPort, listen =>
            {
                var certificate = X509Certificate2.CreateFromPemFile(
                    edgeControl.ServerCertificatePath,
                    edgeControl.ServerCertificateKeyPath);
                listen.UseHttps(https =>
                {
                    https.ServerCertificate = certificate;
                    https.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                    // Full private-CA and SAN validation is performed by the internal endpoint.
                    https.ClientCertificateValidation = (_, _, _) => true;
                });
            });
        });
    }

    if (builder.Configuration.GetValue<bool>(
            $"{OptimizationWorkerTransportOptions.SectionName}:LeaseTransportEnabled"))
    {
        builder.WebHost.ConfigureKestrel(options => options.ConfigureHttpsDefaults(https =>
        {
            // The private worker CA is intentionally absent from the host trust store.
            // Accept it at the TLS boundary; the worker endpoints then enforce the
            // configured CA, clientAuth EKU, common name, and shared secret.
            https.ClientCertificateValidation = (_, _, _) => true;
        }));
    }

    builder.Host.UseSerilog((context, services, logging) => logging
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services));

    builder.Services.AddMemoryCache();
    builder.Services.ConfigureHttpJsonOptions(options =>
    {
        options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
        options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
    builder.Services.AddOpenApi("desktop");
    builder.Services.AddStockTraderServices(
        builder.Configuration,
        includeHostedServices: !isOpenApiGeneration);
    builder.Services.AddSecurityServices(
        builder.Configuration,
        persistDataProtectionKeys: !isOpenApiGeneration);
    builder.Services.AddCors(options => options.AddPolicy("DesktopUi", policy => policy
        .WithOrigins(
            "http://localhost:5173",
            "http://localhost:8000",
            "http://127.0.0.1:5173",
            "http://127.0.0.1:8000")
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));
    var app = builder.Build();
    if (args.Contains("--verify-database-migrations", StringComparer.Ordinal))
    {
        Environment.ExitCode = await app.VerifyDatabaseMigrationsAsync() ? 0 : 2;
        return;
    }
    if (args.Contains("--migrate-database", StringComparer.Ordinal))
    {
        Environment.ExitCode = await app.MigrateDatabaseOnlyAsync() ? 0 : 2;
        return;
    }
    if (args.Contains("--project-market-data-rollback", StringComparer.Ordinal))
    {
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider
            .GetRequiredService<StockTrader.Services.DataFeed.MarketDataRollbackProjector>()
            .ProjectAsync(CancellationToken.None);
        return;
    }
    if (!isOpenApiGeneration)
        await app.InitializeStockTraderAsync();
    app.UseStockTraderPipeline();
    app.MapStockTraderApi();
    if (app.Environment.IsDevelopment())
        app.MapOpenApi("/openapi/{documentName}.json");
    app.Run();
}
catch (Exception exception)
{
    Log.Fatal(exception, "Application terminated unexpectedly");
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}
