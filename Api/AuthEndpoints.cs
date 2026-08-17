using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Services.Auth;

namespace StockTrader.Api;

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthApi(this RouteGroupBuilder api)
    {
        var authApi = api.MapGroup("/auth");

        authApi.MapPost("/login", LoginAsync).RequireRateLimiting("login");
        authApi.MapPost("/logout", LogoutAsync);
        authApi.MapGet("/me", CurrentUser);
        authApi.MapGet("/bootstrap", BootstrapAsync);
        authApi.MapPost("/register", RegisterAsync).RequireRateLimiting("login");
        authApi.MapPost("/change-password", ChangePasswordAsync)
            .RequireRateLimiting("api").RequireAuthorization();
        return api;
    }

    private static async Task<IResult> LoginAsync(HttpContext context, IAuthService auth)
    {
        var credentials = await ReadCredentialsAsync(context.Request);
        if (credentials is null)
            return Results.BadRequest(new { error = "Invalid JSON body. Provide 'username' and 'password'." });

        var result = await auth.LoginAsync(credentials.Value.Username, credentials.Value.Password);
        if (!result.Success || result.Principal == null)
            return Results.Json(new { error = result.ErrorMessage }, statusCode: 401);

        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            result.Principal, new AuthenticationProperties { IsPersistent = true });
        return Results.Ok(new { message = "로그인 성공", username = credentials.Value.Username });
    }

    private static async Task<IResult> LogoutAsync(HttpContext context, IAuditService audit)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (int.TryParse(userId, out var id))
            await audit.LogAsync(id, "LOGOUT", "User signed out");
        return Results.Ok(new { message = "로그아웃 완료" });
    }

    private static IResult CurrentUser(HttpContext context)
    {
        if (!context.User.Identity?.IsAuthenticated ?? true)
            return Results.Unauthorized();
        return Results.Ok(new
        {
            userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier),
            username = context.User.Identity?.Name ?? context.User.FindFirstValue(ClaimTypes.Name) ?? "",
            authenticated = true,
        });
    }

    private static async Task<IResult> BootstrapAsync(
        IAuthService auth,
        IOptionsMonitor<SecuritySettings> security)
    {
        var hasUsers = await auth.HasAnyUserAsync();
        return Results.Ok(new
        {
            hasUsers,
            allowRegistration = !hasUsers || security.CurrentValue.AllowRegistration,
        });
    }

    private static async Task<IResult> RegisterAsync(
        HttpContext context,
        IAuthService auth,
        IOptionsMonitor<SecuritySettings> security)
    {
        var credentials = await ReadCredentialsAsync(context.Request);
        if (credentials is null)
            return Results.BadRequest(new { error = "Invalid JSON body." });

        var wasFirstUser = !await auth.HasAnyUserAsync();
        var result = await auth.RegisterAsync(credentials.Value.Username, credentials.Value.Password);
        if (!result.Success)
            return Results.BadRequest(new { error = result.ErrorMessage });

        if (wasFirstUser)
        {
            security.CurrentValue.AllowRegistration = false;
            context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(AuthEndpoints))
                .LogInformation("First user '{Username}' registered. AllowRegistration set to false.",
                    credentials.Value.Username);
        }
        return Results.Ok(new { message = "사용자 등록 완료", userId = result.UserId });
    }

    private static async Task<IResult> ChangePasswordAsync(HttpContext context, IAuthService auth)
    {
        if (!context.User.Identity?.IsAuthenticated ?? true)
            return Results.Unauthorized();
        if (!int.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Results.Unauthorized();

        try
        {
            using var body = await System.Text.Json.JsonDocument.ParseAsync(context.Request.Body);
            var current = body.RootElement.GetProperty("currentPassword").GetString() ?? "";
            var replacement = body.RootElement.GetProperty("newPassword").GetString() ?? "";
            var (success, error) = await auth.ChangePasswordAsync(userId, current, replacement);
            return success
                ? Results.Ok(new { message = "비밀번호가 변경되었습니다." })
                : Results.BadRequest(new { error });
        }
        catch
        {
            return Results.BadRequest(new { error = "Invalid JSON body." });
        }
    }

    private static async Task<(string Username, string Password)?> ReadCredentialsAsync(HttpRequest request)
    {
        try
        {
            using var body = await System.Text.Json.JsonDocument.ParseAsync(request.Body);
            return (
                body.RootElement.GetProperty("username").GetString() ?? "",
                body.RootElement.GetProperty("password").GetString() ?? "");
        }
        catch
        {
            return null;
        }
    }
}
