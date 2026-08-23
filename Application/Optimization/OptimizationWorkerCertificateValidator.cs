using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Options;
using StockTrader.Configuration;

namespace StockTrader.Application.Optimization;

public interface IOptimizationWorkerCertificateValidator
{
    bool IsTrusted(X509Certificate2? certificate);
}

/// <summary>내부 CA와 Client Authentication EKU에 묶인 Worker 신원을 검증합니다.</summary>
public sealed class OptimizationWorkerCertificateValidator(
    IOptions<OptimizationWorkerTransportOptions> options,
    ILogger<OptimizationWorkerCertificateValidator> logger)
    : IOptimizationWorkerCertificateValidator
{
    private const string ClientAuthenticationOid = "1.3.6.1.5.5.7.3.2";

    public bool IsTrusted(X509Certificate2? certificate)
    {
        if (certificate is null) return false;
        var settings = options.Value;
        if (!string.Equals(
                certificate.GetNameInfo(X509NameType.SimpleName, false),
                settings.ClientCertificateCommonName,
                StringComparison.Ordinal))
            return false;

        try
        {
            var roots = new X509Certificate2Collection();
            roots.ImportFromPemFile(settings.ClientCertificateAuthorityPath);
            if (roots.Count == 0) return false;
            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.AddRange(roots);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
            chain.ChainPolicy.ApplicationPolicy.Add(new Oid(ClientAuthenticationOid));
            return chain.Build(certificate);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or System.Security.Cryptography.CryptographicException)
        {
            logger.LogWarning(error, "Optimization Worker client certificate validation failed");
            return false;
        }
    }
}
