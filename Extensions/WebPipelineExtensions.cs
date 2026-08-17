using Microsoft.AspNetCore.Diagnostics;
using Serilog;
using StockTrader.Services.Auth;

namespace StockTrader.Extensions;

public static class WebPipelineExtensions
{
    public static WebApplication UseStockTraderPipeline(this WebApplication app)
    {
        app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
        {
            var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
            if (exception is not null)
            {
                context.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger(typeof(WebPipelineExtensions))
                    .LogError(exception, "Unhandled exception occurred");
            }

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new
            {
                type = "about:blank",
                title = "An unexpected server error occurred.",
                status = StatusCodes.Status500InternalServerError,
                traceId = context.TraceIdentifier
            });
        }));

        app.UseSecurityHeaders();
        app.UseRateLimiter();
        app.UseSerilogRequestLogging(options =>
            options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000}ms");
        app.UseCors("DesktopUi");

        if (app.Environment.IsDevelopment()
            && !app.Configuration.GetValue<bool>("DOTNET_RUNNING_IN_CONTAINER"))
            app.UseHttpsRedirection();

        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }
}
