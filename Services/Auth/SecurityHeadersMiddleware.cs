namespace StockTrader.Services.Auth;

/// <summary>
/// Adds defensive HTTP security headers to every response.
/// The API serves JSON only; the Svelte application owns its document policy.
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

        // The API does not serve executable documents or browser assets.
        headers["Content-Security-Policy"] =
            "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

        await _next(context);
    }
}

/// <summary>Extension method for convenient middleware registration.</summary>
public static class SecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
        => app.UseMiddleware<SecurityHeadersMiddleware>();
}
