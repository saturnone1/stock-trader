namespace StockTrader.Application.Accounts;

public static class TradingAccountPolicy
{
    public const int MaxAccountNameLength = 100;
    public const int MaxCredentialLength = 200;
    public const int MaxEnvironmentLength = 50;
    public const int MaxNotesLength = 500;

    public static TradingAccountValidationResult Validate(ManagedTradingAccount account)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(account.AccountName))
            errors.Add("AccountName is required.");
        else if (account.AccountName.Trim().Length > MaxAccountNameLength)
            errors.Add($"AccountName must be {MaxAccountNameLength} characters or fewer.");

        if (!BrokerCatalog.IsDefined(account.BrokerType))
            errors.Add("Invalid BrokerType value.");
        else
        {
            var broker = BrokerCatalog.Get(account.BrokerType);
            if (!broker.Environments.Contains(account.Environment, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add(
                    $"Environment must be one of: {string.Join(", ", broker.Environments)}.");
            }

            if (broker.RequiresAccountCredentials
                && (string.IsNullOrWhiteSpace(account.ApiKey)
                    || string.IsNullOrWhiteSpace(account.ApiSecret)))
            {
                errors.Add($"{broker.DisplayName} API Key and Secret are required.");
            }

            if (account.IsEnabled && !broker.IsImplemented)
                errors.Add($"{broker.DisplayName} live broker integration is not available yet.");
        }

        if (account.IsActive && !account.IsEnabled)
            errors.Add("A disabled account cannot be active.");

        if ((account.ApiKey ?? string.Empty).Length > MaxCredentialLength
            || (account.ApiSecret ?? string.Empty).Length > MaxCredentialLength)
        {
            errors.Add($"API credentials must be {MaxCredentialLength} characters or fewer.");
        }
        if ((account.Environment ?? string.Empty).Length > MaxEnvironmentLength)
            errors.Add($"Environment must be {MaxEnvironmentLength} characters or fewer.");
        if ((account.Notes ?? string.Empty).Length > MaxNotesLength)
            errors.Add($"Notes must be {MaxNotesLength} characters or fewer.");

        return errors.Count == 0
            ? TradingAccountValidationResult.Success
            : new TradingAccountValidationResult(false, errors);
    }

    public static string MaskApiKey(string key)
    {
        if (string.IsNullOrEmpty(key))
            return string.Empty;
        if (key.Length <= 4)
            return "****";
        return key[..4] + new string('*', Math.Min(key.Length - 4, 8));
    }
}
