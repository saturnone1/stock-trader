using System.Security.Claims;

namespace StockTrader.Application.Authentication;

public sealed record LoginResult(
    bool Success,
    string? ErrorMessage,
    ClaimsPrincipal? Principal);

public sealed record RegisterResult(
    bool Success,
    string? ErrorMessage,
    int UserId = 0);

public interface IUserAuthenticationService
{
    Task<LoginResult> LoginAsync(
        string username,
        string password,
        CancellationToken ct = default);

    Task<RegisterResult> RegisterAsync(
        string username,
        string password,
        CancellationToken ct = default);

    Task<(bool Success, string? ErrorMessage)> ChangePasswordAsync(
        int userId,
        string oldPassword,
        string newPassword,
        CancellationToken ct = default);

    Task<bool> HasAnyUserAsync(CancellationToken ct = default);
}
