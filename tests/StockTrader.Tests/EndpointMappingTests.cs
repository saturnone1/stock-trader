using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using StockTrader.Api;
using StockTrader.Extensions;

namespace StockTrader.Tests;

public class EndpointMappingTests
{
    [Fact]
    public async Task MapStockTraderApi_RegistersExtractedRoutesExactlyOnce()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddMemoryCache();
        builder.Services.AddStockTraderServices(builder.Configuration);
        builder.Services.AddSecurityServices(builder.Configuration);
        await using var app = builder.Build();
        app.MapStockTraderApi();

        var routes = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .ToArray();
        var expected = new[]
        {
            "/api/health",
            "/api/auth/login",
            "/api/auth/logout",
            "/api/auth/me",
            "/api/auth/bootstrap",
            "/api/auth/register",
            "/api/auth/change-password",
            "/api/orders/execute-signal",
            "/api/orders/close-position",
            "/api/backtest",
            "/api/backtest/apply-live",
        };

        foreach (var route in expected)
            routes.Count(item => item == route).Should().Be(1, $"{route} 계약은 한 번만 등록돼야 합니다");
    }
}
