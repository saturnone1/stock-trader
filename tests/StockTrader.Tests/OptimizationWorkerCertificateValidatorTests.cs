using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StockTrader.Application.Optimization;
using StockTrader.Configuration;

namespace StockTrader.Tests;

public sealed class OptimizationWorkerCertificateValidatorTests
{
    [Fact]
    public void IsTrusted_RequiresConfiguredCaClientEkuAndExactWorkloadName()
    {
        using var caKey = RSA.Create(2048);
        var caRequest = new CertificateRequest(
            "CN=worker-test-ca", caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        caRequest.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        using var authority = caRequest.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        var path = Path.Combine(Path.GetTempPath(), $"worker-ca-{Guid.NewGuid():N}.crt");
        File.WriteAllText(path, authority.ExportCertificatePem());
        try
        {
            var validator = new OptimizationWorkerCertificateValidator(
                Options.Create(new OptimizationWorkerTransportOptions
                {
                    ClientCertificateAuthorityPath = path,
                    ClientCertificateCommonName = "stocktrader-optimization-worker"
                }),
                NullLogger<OptimizationWorkerCertificateValidator>.Instance);

            using var trusted = CreateLeaf(
                authority, caKey, "stocktrader-optimization-worker", clientAuthentication: true);
            using var wrongName = CreateLeaf(
                authority, caKey, "another-worker", clientAuthentication: true);
            using var serverOnly = CreateLeaf(
                authority, caKey, "stocktrader-optimization-worker", clientAuthentication: false);

            validator.IsTrusted(trusted).Should().BeTrue();
            validator.IsTrusted(wrongName).Should().BeFalse();
            validator.IsTrusted(serverOnly).Should().BeFalse();
            validator.IsTrusted(null).Should().BeFalse();
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static X509Certificate2 CreateLeaf(
        X509Certificate2 authority,
        RSA authorityKey,
        string commonName,
        bool clientAuthentication)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={commonName}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new Oid(clientAuthentication
                ? "1.3.6.1.5.5.7.3.2"
                : "1.3.6.1.5.5.7.3.1")], true));
        var serial = RandomNumberGenerator.GetBytes(16);
        using var signed = request.Create(
            authority.SubjectName,
            X509SignatureGenerator.CreateForRSA(authorityKey, RSASignaturePadding.Pkcs1),
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddHours(1),
            serial);
        return signed.CopyWithPrivateKey(key);
    }
}
