using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using StockTrader.Application.Optimization;
using StockTrader.Configuration;
using StockTrader.ServiceContracts.Optimization;

namespace StockTrader.Api;

public static class OptimizationWorkerAuthenticationDefaults
{
    public const string Scheme = "OptimizationWorker";
    public const string Policy = "OptimizationWorkerOnly";
}

public sealed class OptimizationWorkerAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> schemes,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<OptimizationWorkerTransportOptions> transport,
    IOptimizationWorkerCertificateValidator certificates)
    : AuthenticationHandler<AuthenticationSchemeOptions>(schemes, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (transport.Value.LeaseTransportEnabled)
        {
            var certificate = Request.IsHttps
                ? await Context.Connection.GetClientCertificateAsync(Context.RequestAborted)
                : null;
            if (!certificates.IsTrusted(certificate))
                return AuthenticateResult.Fail("invalid-worker-certificate");
        }

        var workerId = Request.Headers[OptimizationWorkerHttpHeaders.WorkerId]
            .ToString().Trim();
        var secret = Request.Headers[OptimizationWorkerHttpHeaders.Secret]
            .ToString();
        if (!OptimizationWorkerCredentialPolicy.IsAuthorized(
                transport.Value.Enabled,
                transport.Value.SharedSecret,
                secret,
                workerId))
        {
            return AuthenticateResult.Fail("invalid-worker-credential");
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, workerId),
                new Claim("service", "optimization-worker")
            ],
            OptimizationWorkerAuthenticationDefaults.Scheme);
        return AuthenticateResult.Success(
            new AuthenticationTicket(
                new ClaimsPrincipal(identity),
                OptimizationWorkerAuthenticationDefaults.Scheme));
    }
}
