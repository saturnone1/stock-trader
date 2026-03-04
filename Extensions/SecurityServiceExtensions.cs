using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using StockTrader.Configuration;
using StockTrader.Services.Auth;

namespace StockTrader.Extensions;

public static class SecurityServiceExtensions
{
    /// <summary>
    /// Registers all security-related services: cookie auth, crypto, audit, rate limiting.
    /// Call this from Program.cs after AddStockTraderServices.
    /// </summary>
    public static IServiceCollection AddSecurityServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind security settings
        services.Configure<SecuritySettings>(configuration.GetSection("Security"));

        // HttpContextAccessor (needed by AuditService to read client IP)
        services.AddHttpContextAccessor();

        // DataProtection: 쿠키 암호화 키를 /data에 영구 저장.
        // 컨테이너 재시작해도 기존 로그인 세션이 유지된다.
        var keyPath = configuration["ConnectionStrings:DefaultConnection"] ?? "";
        var dataDir = keyPath.Contains("/data/")
            ? "/data/keys"
            : Path.Combine(AppContext.BaseDirectory, "keys");
        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(dataDir))
            .SetApplicationName("StockTrader");

        // Cookie Authentication
        var sessionMinutes = configuration
            .GetSection("Security")
            .GetValue<int>("SessionTimeoutMinutes", 1440);

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, opts =>
            {
                opts.LoginPath         = "/login";
                opts.LogoutPath        = "/logout";
                opts.AccessDeniedPath  = "/access-denied";
                opts.ExpireTimeSpan    = TimeSpan.FromMinutes(sessionMinutes);
                opts.SlidingExpiration = true;

                opts.Cookie.HttpOnly  = true;
                opts.Cookie.SameSite  = SameSiteMode.Strict;
                opts.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                opts.Cookie.Name      = "StockTrader.Auth";
            });

        services.AddAuthorization();

        // Auth + Audit + Crypto services
        services.AddSingleton<ICryptoService, AesCryptoService>();
        services.AddSingleton<IAuditService, AuditService>();
        services.AddScoped<IAuthService, AuthService>();

        // Rate limiting
        services.AddRateLimiter(opts =>
        {
            opts.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Login endpoint: 10 requests per minute per IP
            opts.AddFixedWindowLimiter("login", policy =>
            {
                policy.Window               = TimeSpan.FromMinutes(1);
                policy.PermitLimit          = 10;
                policy.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                policy.QueueLimit           = 0;
            });

            // General API endpoints: 60 requests per minute per IP
            opts.AddFixedWindowLimiter("api", policy =>
            {
                policy.Window               = TimeSpan.FromMinutes(1);
                policy.PermitLimit          = 60;
                policy.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                policy.QueueLimit           = 2;
            });
        });

        return services;
    }
}
