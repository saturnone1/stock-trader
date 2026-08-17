using System.Text.Json.Serialization;
using MudBlazor.Services;
using Serilog;
using StockTrader.Api;
using StockTrader.Components;
using StockTrader.Extensions;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    if (!builder.Environment.IsDevelopment())
        builder.Configuration.AddUserSecrets<Program>(optional: true);

    builder.Host.UseSerilog((context, services, logging) => logging
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services));

    builder.Services.AddRazorComponents().AddInteractiveServerComponents();
    builder.Services.AddMudServices();
    builder.Services.AddMemoryCache();
    builder.Services.ConfigureHttpJsonOptions(options =>
        options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
    builder.Services.AddStockTraderServices(builder.Configuration);
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
    builder.Services.AddHttpClient("Auth", client =>
    {
        client.BaseAddress = new Uri("http://localhost:5239");
        client.Timeout = TimeSpan.FromSeconds(10);
    });

    var app = builder.Build();
    if (args.Contains("--verify-ef-baseline", StringComparer.Ordinal))
    {
        Environment.ExitCode = await app.VerifyEfBaselineCompatibilityAsync() ? 0 : 2;
        return;
    }
    await app.InitializeStockTraderAsync();
    app.UseStockTraderPipeline();
    app.MapStockTraderApi();
    app.MapStaticAssets();
    app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
    app.Run();
}
catch (Exception exception)
{
    Log.Fatal(exception, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
