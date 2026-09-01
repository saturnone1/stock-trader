using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.ServiceContracts;
using StockTrader.ServiceContracts.TradingCore;

namespace StockTrader.Services.TradingCore;

public sealed class EdgeAuthorityCapabilityAttestor(
    IOptions<AuthorityCapabilityAttestationOptions> options,
    IOptions<TradingCoreTransportOptions> tradingCore,
    TimeProvider clock)
{
    public AuthorityCapabilityReceipt Attest()
    {
        var configured = options.Value;
        var files = Directory.GetFiles(AppContext.BaseDirectory, "*.dll")
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .Select(path => new
            {
                Name = Path.GetFileName(path),
                Hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
            }).ToArray();
        var isRemote = string.Equals(tradingCore.Value.Mode, "Remote", StringComparison.Ordinal);
        var hasAdapter = files.Any(value =>
            value.Name is "StockTrader.TradingCore.AlpacaAdapter.dll");
        var hasSecret = Environment.GetEnvironmentVariable("ALPACA__APIKEY") is { Length: > 0 }
            || Environment.GetEnvironmentVariable("ALPACA__APISECRET") is { Length: > 0 };
        var receipt = new AuthorityCapabilityReceipt(
            AuthorityOwners.Edge,
            configured.RuntimeProfile,
            configured.ImageDigest,
            CanonicalJsonHash.Compute(files),
            configured.ServiceInventoryHash,
            configured.SecretReferenceHash,
            configured.NetworkPolicyHash,
            !isRemote,
            hasAdapter,
            hasSecret,
            configured.HasBrokerEgress,
            clock.GetUtcNow().UtcDateTime,
            string.Empty);
        return receipt with { ReceiptHash = TradingControlIdentity.Capability(receipt) };
    }
}
