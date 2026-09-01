namespace StockTrader.ServiceContracts.TradingCore;

public static class TradingCoreCoordinatorVersions
{
    public const int Current = 1;
}

public sealed record TradingCoreDeploymentTarget(
    string Namespace,
    string EdgeDeployment,
    string TradingCoreDeployment,
    string EdgeContainer,
    string TradingCoreContainer,
    string EdgeImage,
    string TradingCoreImage,
    string BrokerSecretName);

public sealed record TradingCoreRollbackTarget(
    string ImportJobName,
    string ImportReceiptPath);

public sealed record TradingCoreTransitionPlanV1(
    int ContractVersion,
    string PlanHash,
    string TransitionId,
    string Direction,
    TradingAuthorityMode SourceMode,
    TradingAuthorityMode TargetMode,
    long SourceGeneration,
    long AccountGeneration,
    DateTime StartedAtUtc,
    DateTime ExpiresAtUtc,
    Uri EdgeControlEndpoint,
    Uri TradingCoreControlEndpoint,
    string SealedTransferPath,
    string? ExpectedTransferHash,
    FinancialTransferCompatibility TransferCompatibility,
    string EquityBasis,
    TradingCoreDeploymentTarget Deployments,
    TradingCoreRollbackTarget? Rollback,
    string AcceptedIsolatedManifestId,
    string AcceptedShadowManifestId);

public sealed record TradingCoreCoordinatorState(
    int ContractVersion,
    string PlanHash,
    string TransitionId,
    string LastCompletedStep,
    string? LastOperationId,
    string? LastReceiptHash,
    DateTime UpdatedAtUtc);

public static class TradingCoreCoordinatorIdentity
{
    public static string Plan(TradingCoreTransitionPlanV1 plan) =>
        CanonicalJsonHash.Compute(plan, nameof(TradingCoreTransitionPlanV1.PlanHash));
}

public static class TradingCoreCoordinatorPolicy
{
    public static string? Error(TradingCoreTransitionPlanV1 plan)
    {
        if (plan.ContractVersion != TradingCoreCoordinatorVersions.Current)
            return "unsupported-contract";
        if (!Guid.TryParse(plan.TransitionId, out _)
            || !AuthorityTransitionDirections.All.Contains(plan.Direction)
            || plan.SourceGeneration < 1 || plan.AccountGeneration < 1
            || plan.StartedAtUtc.Kind != DateTimeKind.Utc
            || plan.ExpiresAtUtc <= plan.StartedAtUtc
            || plan.EdgeControlEndpoint.Scheme != Uri.UriSchemeHttps
            || plan.TradingCoreControlEndpoint.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrWhiteSpace(plan.SealedTransferPath)
            || plan.TransferCompatibility is null
            || string.IsNullOrWhiteSpace(plan.EquityBasis)
            || string.IsNullOrWhiteSpace(plan.Deployments.Namespace)
            || string.IsNullOrWhiteSpace(plan.Deployments.BrokerSecretName)
            || string.IsNullOrWhiteSpace(plan.AcceptedIsolatedManifestId)
            || string.IsNullOrWhiteSpace(plan.AcceptedShadowManifestId)
            || plan.PlanHash != TradingCoreCoordinatorIdentity.Plan(plan))
            return "invalid-transition-plan";
        var legal = plan.Direction switch
        {
            AuthorityTransitionDirections.Cutover =>
                plan.SourceMode == TradingAuthorityMode.Shadow
                && plan.TargetMode == TradingAuthorityMode.Remote,
            AuthorityTransitionDirections.Rollback =>
                plan.SourceMode == TradingAuthorityMode.Remote
                && plan.TargetMode == TradingAuthorityMode.Shadow
                && plan.Rollback is not null
                && !string.IsNullOrWhiteSpace(plan.Rollback.ImportJobName)
                && Path.IsPathRooted(plan.Rollback.ImportReceiptPath),
            _ => false,
        };
        return legal ? null : "illegal-authority-transition";
    }
}
