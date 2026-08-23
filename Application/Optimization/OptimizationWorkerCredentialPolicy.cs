using System.Security.Cryptography;
using System.Text;

namespace StockTrader.Application.Optimization;

public static class OptimizationWorkerCredentialPolicy
{
    public static bool IsAuthorized(
        bool enabled,
        string expectedSecret,
        string presentedSecret,
        string workerId)
    {
        if (!enabled
            || string.IsNullOrWhiteSpace(workerId)
            || expectedSecret.Length == 0
            || expectedSecret.Length != presentedSecret.Length)
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expectedSecret),
            Encoding.UTF8.GetBytes(presentedSecret));
    }
}
