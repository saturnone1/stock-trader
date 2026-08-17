using System.Text.Json.Serialization;
using System.Reflection;
using Serilog;
using StockTrader.Api;
using StockTrader.Extensions;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var isOpenApiGeneration =
        Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";
    var builder = WebApplication.CreateBuilder(args);
    if (!isOpenApiGeneration && !builder.Environment.IsDevelopment())
        builder.Configuration.AddUserSecrets<Program>(optional: true);

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
    builder.Services.AddSecurityServices(builder.Configuration);
    builder.Services.AddCors(options => options.AddPolicy("DesktopUi", policy => policy
        .WithOrigins(
            "http://stock-desktop.taewon",
            "https://stock-desktop.taewon",
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
