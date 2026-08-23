using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using StockTrader.Application.Authentication;
using StockTrader.Configuration;
using StockTrader.Data.Repositories;
using StockTrader.Services.Auth;
using StockTrader.Api;

namespace StockTrader.Extensions;

public static class SecurityServiceExtensions
{
    /// <summary>
    /// Registers all security-related services: cookie auth, crypto, audit, rate limiting.
    /// Call this from Program.cs after AddStockTraderServices.
    /// </summary>
    public static IServiceCollection AddSecurityServices(
        this IServiceCollection services,
        IConfiguration configuration,
        bool persistDataProtectionKeys = true)
    {
        // Bind security settings
        services.AddOptions<SecuritySettings>()
            .Bind(configuration.GetSection("Security"))
            .Validate(
                settings => settings.SessionTimeoutMinutes > 0,
                "SessionTimeoutMinutes must be positive")
            .Validate(
                settings => settings.MaxFailedLoginAttempts > 0,
                "MaxFailedLoginAttempts must be positive")
            .Validate(
                settings => settings.LockoutMinutes > 0,
                "LockoutMinutes must be positive")
            .ValidateOnStart();
        services.AddOptions<OptimizationWorkerTransportOptions>()
            .Bind(configuration.GetSection(OptimizationWorkerTransportOptions.SectionName))
            .Validate(settings => settings.IsValid(),
                "Optimization worker transport requires a 32+ character secret when enabled "
                + "and a lease duration between 30 and 1800 seconds")
            .ValidateOnStart();

        // HttpContextAccessor (needed by AuditService to read client IP)
        services.AddHttpContextAccessor();

        // DataProtection: 쿠키 암호화 키를 /data에 영구 저장.
        // 컨테이너 재시작해도 기존 로그인 세션이 유지된다.
        var dataProtection = services.AddDataProtection()
            .SetApplicationName("StockTrader");
        if (persistDataProtectionKeys)
        {
            var keyPath = configuration["ConnectionStrings:DefaultConnection"] ?? "";
            var dataDir = keyPath.Contains("/data/")
                ? "/data/keys"
                : Path.Combine(AppContext.BaseDirectory, "keys");
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(dataDir));
        }
        else
        {
            dataProtection.UseEphemeralDataProtectionProvider();
        }

        // Cookie Authentication
        var sessionMinutes = configuration
            .GetSection("Security")
            .GetValue<int>("SessionTimeoutMinutes");

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

                // API 요청에는 302 리다이렉트 대신 401 반환
                opts.Events = new CookieAuthenticationEvents
                {
                    OnRedirectToLogin = context =>
                    {
                        if (context.Request.Path.StartsWithSegments("/api"))
                        {
                            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                            return Task.CompletedTask;
                        }
                        context.Response.Redirect(context.RedirectUri);
                        return Task.CompletedTask;
                    }
                };
            })
            .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions,
                OptimizationWorkerAuthenticationHandler>(
                OptimizationWorkerAuthenticationDefaults.Scheme,
                _ => { });

        services.AddAuthorization(options => options.AddPolicy(
            OptimizationWorkerAuthenticationDefaults.Policy,
            policy => policy
                .AddAuthenticationSchemes(OptimizationWorkerAuthenticationDefaults.Scheme)
                .RequireClaim("service", "optimization-worker")));

        // Auth + Audit + Crypto services
        services.AddSingleton<ICryptoService, AesCryptoService>();
        services.AddSingleton<ISecurityAuditStore, SecurityAuditStore>();
        services.AddSingleton<ISecurityAuditSink, AuditService>();
        services.AddSingleton(serviceProvider =>
        {
            var settings = serviceProvider
                .GetRequiredService<IOptions<SecuritySettings>>()
                .Value;
            return new AuthenticationPolicy(
                settings.MaxFailedLoginAttempts,
                TimeSpan.FromMinutes(settings.LockoutMinutes),
                settings.AllowRegistration);
        });
        services.AddScoped<IAuthenticationUserStore, AuthenticationUserStore>();
        services.AddScoped<IUserAuthenticationService, AuthenticationService>();

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

            opts.AddFixedWindowLimiter("optimization-worker", policy =>
            {
                policy.Window = TimeSpan.FromMinutes(1);
                policy.PermitLimit = 120;
                policy.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                policy.QueueLimit = 0;
            });
        });

        return services;
    }
}
