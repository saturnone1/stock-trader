using System.Text.Json;
using StockTrader.ServiceContracts.TradingCore;

if (args.Length != 2 || args[0] is not ("cutover" or "rollback"))
    throw new ArgumentException("usage: plan-builder cutover|rollback OUTPUT_PATH");

var direction = args[0] == "cutover"
    ? AuthorityTransitionDirections.Cutover
    : AuthorityTransitionDirections.Rollback;
var transitionId = Optional("STOCKTRADER_TRANSITION_ID") ?? Guid.NewGuid().ToString();
if (!Guid.TryParse(transitionId, out _))
    throw new ArgumentException("STOCKTRADER_TRANSITION_ID must be a UUID");
var sourceMode = direction == AuthorityTransitionDirections.Cutover
    ? TradingAuthorityMode.Shadow : TradingAuthorityMode.Remote;
var targetMode = direction == AuthorityTransitionDirections.Cutover
    ? TradingAuthorityMode.Remote : TradingAuthorityMode.Shadow;
var started = DateTime.UtcNow;
var sourceGeneration = Long("STOCKTRADER_SOURCE_GENERATION");
var releaseTag = Required("STOCKTRADER_RELEASE_TAG");
var deployment = new TradingCoreDeploymentTarget(
    "stocktrader", "stocktrader-api", "stocktrader-trading-core",
    "api", "trading-core",
    targetMode == TradingAuthorityMode.Remote
        ? $"localhost/stock-trader/api-remote:architecture-{releaseTag}"
        : $"localhost/stock-trader/api-local:architecture-{releaseTag}",
    targetMode == TradingAuthorityMode.Remote
        ? $"localhost/stock-trader/trading-core:architecture-{releaseTag}"
        : $"localhost/stock-trader/trading-core-shadow:architecture-{releaseTag}",
    Optional("STOCKTRADER_BROKER_SECRET_NAME") ?? "stocktrader-alpaca");
var rollback = direction == AuthorityTransitionDirections.Rollback
    ? new TradingCoreRollbackTarget(
        $"stocktrader-edge-rollback-import-{transitionId}",
        "/state/rollback-import-receipt.json")
    : null;
var compatibility = new FinancialTransferCompatibility(
    CanonicalFinancialTransferVersions.Current,
    Required("STOCKTRADER_SOURCE_SCHEMA_VERSION"),
    Required("STOCKTRADER_TARGET_SCHEMA_VERSION"),
    Required("STOCKTRADER_ENGINE_SEMANTICS_VERSION"),
    Required("STOCKTRADER_STRATEGY_ARTIFACT_VERSION"),
    Required("STOCKTRADER_PATTERN_CATALOG_VERSION"),
    Required("STOCKTRADER_CALENDAR_VERSION"),
    Required("STOCKTRADER_MARKET_DATA_CONTRACT_VERSION"));
var candidate = new TradingCoreTransitionPlanV1(
    TradingCoreCoordinatorVersions.Current, "", transitionId, direction,
    sourceMode, targetMode, sourceGeneration,
    Long("STOCKTRADER_ACCOUNT_GENERATION"), started, started.AddMinutes(30),
    new Uri("https://stocktrader-api:3543"),
    new Uri("https://stocktrader-trading-core:9443"),
    "/state/financial-transfer.json",
    Optional("STOCKTRADER_EXPECTED_TRANSFER_HASH"),
    compatibility,
    Optional("STOCKTRADER_EQUITY_BASIS") ?? "BrokerTotalEquity",
    deployment,
    rollback,
    Required("STOCKTRADER_ACCEPTED_ISOLATED_MANIFEST_ID"),
    Required("STOCKTRADER_ACCEPTED_SHADOW_MANIFEST_ID"));
var plan = candidate with { PlanHash = TradingCoreCoordinatorIdentity.Plan(candidate) };
if (TradingCoreCoordinatorPolicy.Error(plan) is { } error)
    throw new InvalidDataException(error);
var output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
await File.WriteAllTextAsync(output, JsonSerializer.Serialize(plan,
    new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
Console.WriteLine($"{transitionId} {plan.PlanHash} {output}");

static string Required(string name) => Optional(name)
    ?? throw new InvalidOperationException($"Missing required setting: {name}");
static string? Optional(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
    ? value : null;
static long Long(string name) => long.TryParse(Required(name), out var value) && value > 0
    ? value : throw new InvalidOperationException($"{name} must be a positive integer");
