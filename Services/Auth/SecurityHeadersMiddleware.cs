namespace StockTrader.Services.Auth;

/// <summary>
/// Adds defensive HTTP security headers to every response.
/// Should be registered early in the pipeline, before static files.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;

        // Prevent MIME-type sniffing
        headers["X-Content-Type-Options"] = "nosniff";

        // Deny all framing (clickjacking protection)
        headers["X-Frame-Options"] = "DENY";

        // No referrer sent cross-origin
        headers["Referrer-Policy"] = "no-referrer";

        // Restrict powerful browser features
        headers["Permissions-Policy"] =
            "camera=(), microphone=(), geolocation=(), payment=(), usb=()";

        // Content Security Policy — tuned for Blazor Server (requires inline scripts + SignalR WebSocket)
        // 'unsafe-inline' on script-src is required by Blazor Server's blazor.server.js bootstrap
        headers["Content-Security-Policy"] =
            "default-src 'self'; " +
            "script-src 'self' 'unsafe-inline' 'unsafe-eval'; " +
            "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
            "font-src 'self' https://fonts.gstatic.com data:; " +
            "img-src 'self' data: https:; " +
            "connect-src 'self' wss: ws:; " +
            "frame-ancestors 'none';";

        // X-XSS-Protection is deprecated in modern browsers but harmless for older ones
        headers["X-XSS-Protection"] = "1; mode=block";

        await _next(context);
    }
}

/// <summary>Extension method for convenient middleware registration.</summary>
public static class SecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
        => app.UseMiddleware<SecurityHeadersMiddleware>();
}
