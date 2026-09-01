using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Options;
using StockTrader.Application.TradingCore;
using StockTrader.Configuration;
using StockTrader.ServiceContracts.TradingCore;
using StockTrader.Services.TradingCore;

namespace StockTrader.Api;

public static class EdgeTransitionControlEndpoints
{
    public static IEndpointRouteBuilder MapEdgeTransitionControlApi(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/internal/v2/edge-authority")
            .ExcludeFromDescription();
        group.MapGet("/capability", async (HttpContext context,
            EdgeAuthorityCapabilityAttestor attestor,
            IOptions<EdgeTransitionControlOptions> options) =>
            await IsCoordinatorAsync(context, options.Value)
                ? Results.Ok(attestor.Attest())
                : Results.Unauthorized());
        group.MapPost("/fence", async (HttpContext context,
            EdgeAuthorityFenceRequest request, IEdgeFinancialAuthorityControl control,
            IOptions<EdgeTransitionControlOptions> options, CancellationToken ct) =>
            await InvokeAsync(context, options.Value, () =>
            {
                EnsureValid(request);
                return control.FenceAsync(request.TransitionId, request.AuthorityGeneration, ct);
            }));
        group.MapPost("/barrier", async (HttpContext context,
            EdgeAuthorityFenceRequest request, IEdgeFinancialAuthorityControl control,
            IOptions<EdgeTransitionControlOptions> options, CancellationToken ct) =>
            await InvokeAsync(context, options.Value, () =>
            {
                EnsureValid(request);
                return control.EnterPositionBarrierAsync(
                    request.TransitionId, request.AuthorityGeneration, ct);
            }));
        group.MapGet("/{transitionId}/drain", async (HttpContext context,
            string transitionId, IEdgeFinancialAuthorityControl control,
            IOptions<EdgeTransitionControlOptions> options, CancellationToken ct) =>
            await InvokeAsync(context, options.Value,
                () => control.ReadDrainInventoryAsync(transitionId, ct)));
        group.MapPost("/financial-transfers/export", async (HttpContext context,
            CanonicalFinancialExportRequest request,
            IEdgeCanonicalFinancialTransferService transfers,
            IOptions<EdgeTransitionControlOptions> options,
            CancellationToken ct) =>
            await InvokeAsync(context, options.Value,
                () => transfers.ExportAsync(request, ct)));
        group.MapPost("/mirror", async (HttpContext context,
            EdgeAuthorityMirrorRequest request, IEdgeFinancialAuthorityControl control,
            IOptions<EdgeTransitionControlOptions> options, CancellationToken ct) =>
            await InvokeAsync(context, options.Value, async () =>
            {
                if (TradingControlCompatibilityPolicy.Error(request) is { } error)
                    throw new ArgumentException(error, nameof(request));
                await control.MirrorAuthorityAsync(request.TransitionId,
                    request.AuthorityGeneration, request.Mode, request.Owner,
                    request.AuthorityReceiptHash, ct);
                return Results.NoContent();
            }, value => value));
        group.MapPost("/release", async (HttpContext context,
            EdgeAuthorityFenceRequest request, IEdgeFinancialAuthorityControl control,
            IOptions<EdgeTransitionControlOptions> options, CancellationToken ct) =>
            await InvokeAsync(context, options.Value, () =>
            {
                EnsureValid(request);
                return control.ReleaseAsync(request.TransitionId, request.AuthorityGeneration, ct);
            }));
        return endpoints;
    }

    private static async Task<IResult> InvokeAsync<T>(HttpContext context,
        EdgeTransitionControlOptions options, Func<Task<T>> operation,
        Func<T, IResult>? result = null)
    {
        if (!await IsCoordinatorAsync(context, options))
            return Results.Unauthorized();
        try
        {
            var value = await operation();
            return result?.Invoke(value) ?? Results.Ok(value);
        }
        catch (ArgumentException error)
        {
            return Results.BadRequest(new { error = error.Message });
        }
        catch (InvalidOperationException error)
        {
            return Results.Conflict(new { error = error.Message });
        }
    }

    private static async Task<bool> IsCoordinatorAsync(
        HttpContext context, EdgeTransitionControlOptions options)
    {
        if (!options.Enabled || context.Connection.LocalPort != EdgeTransitionControlOptions.InternalPort
            || !context.Request.IsHttps)
            return false;
        var certificate = await context.Connection.GetClientCertificateAsync(
            context.RequestAborted);
        if (certificate is null)
            return false;
        using var root = X509Certificate2.CreateFromPemFile(
            options.ClientCertificateAuthorityPath);
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(root);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        var hasClientAuth = certificate.Extensions
            .OfType<X509EnhancedKeyUsageExtension>()
            .SelectMany(value => value.EnhancedKeyUsages.Cast<Oid>())
            .Any(value => value.Value == "1.3.6.1.5.5.7.3.2");
        var hasRole = certificate.Extensions
            .OfType<X509SubjectAlternativeNameExtension>()
            .SelectMany(value => value.EnumerateDnsNames())
            .Any(value => string.Equals(value, options.CoordinatorRoleDnsName,
                StringComparison.Ordinal));
        return hasClientAuth && hasRole && chain.Build(certificate);
    }

    private static void EnsureValid(EdgeAuthorityFenceRequest request)
    {
        if (TradingControlCompatibilityPolicy.Error(request) is { } error)
            throw new ArgumentException(error, nameof(request));
    }
}
