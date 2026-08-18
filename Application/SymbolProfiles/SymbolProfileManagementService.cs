using System.Text.Json;
using StockTrader.Domain.MarketData;
using StockTrader.Domain.Strategies;

namespace StockTrader.Application.SymbolProfiles;

public sealed class SymbolProfileManagementService(
    ISymbolProfileStore store,
    TimeProvider timeProvider)
{
    public Task<IReadOnlyList<ManagedSymbolProfile>> ListAsync(
        string? symbol = null,
        CancellationToken ct = default) =>
        store.ListAsync(
            string.IsNullOrWhiteSpace(symbol) ? null : MarketSymbolPolicy.Normalize(symbol),
            ct);

    public Task<ManagedSymbolProfile?> GetActiveAsync(
        string symbol,
        CancellationToken ct = default) =>
        store.GetActiveAsync(MarketSymbolPolicy.Normalize(symbol), ct);

    public async Task<SymbolProfileUpsertOutcome> UpsertAsync(
        SymbolProfileUpsertCommand command,
        CancellationToken ct = default)
    {
        var symbol = MarketSymbolPolicy.Normalize(command.Symbol);
        var name = string.IsNullOrWhiteSpace(command.Name)
            ? SymbolProfilePolicy.DefaultName
            : command.Name.Trim();
        var errors = Validate(command, symbol, name);
        if (errors.Count > 0)
            return new(null, false, errors);

        var current = await store.GetBySymbolAndNameAsync(symbol, name, ct);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var profile = new ManagedSymbolProfile
        {
            Id = current?.Id ?? 0,
            Symbol = symbol,
            Name = name,
            IsActive = current?.IsActive ?? false,
            EnabledPatterns = command.EnabledPatterns is null
                ? current?.EnabledPatterns ?? []
                : command.EnabledPatterns.Distinct().ToArray(),
            ParameterOverridesJson = command.ParameterOverridesJson ?? current?.ParameterOverridesJson,
            WeightStrategyJson = command.WeightStrategyJson ?? current?.WeightStrategyJson,
            RiskPerTradePercent = command.RiskPerTradePercent
                ?? current?.RiskPerTradePercent
                ?? SymbolProfilePolicy.DefaultRiskPerTradePercent,
            MaxTotalPositions = command.MaxTotalPositions
                ?? current?.MaxTotalPositions
                ?? SymbolProfilePolicy.DefaultMaximumPositions,
            BacktestReturnPct = command.BacktestReturnPct ?? current?.BacktestReturnPct,
            BacktestWinRate = command.BacktestWinRate ?? current?.BacktestWinRate,
            BacktestMaxDrawdown = command.BacktestMaxDrawdown ?? current?.BacktestMaxDrawdown,
            BacktestSharpe = command.BacktestSharpe ?? current?.BacktestSharpe,
            BacktestTrades = command.BacktestTrades ?? current?.BacktestTrades,
            BacktestFrom = command.BacktestFrom ?? current?.BacktestFrom,
            BacktestTo = command.BacktestTo ?? current?.BacktestTo,
            CreatedAt = current?.CreatedAt ?? now,
            UpdatedAt = now
        };

        return new(await store.SaveAsync(profile, ct), current is null, []);
    }

    public Task<ManagedSymbolProfile?> ActivateAsync(long id, CancellationToken ct = default) =>
        store.SetActiveAsync(id, true, timeProvider.GetUtcNow().UtcDateTime, ct);

    public Task<ManagedSymbolProfile?> DeactivateAsync(long id, CancellationToken ct = default) =>
        store.SetActiveAsync(id, false, timeProvider.GetUtcNow().UtcDateTime, ct);

    public Task<bool> DeleteAsync(long id, CancellationToken ct = default) =>
        store.DeleteAsync(id, ct);

    private static List<string> Validate(
        SymbolProfileUpsertCommand command,
        string symbol,
        string name)
    {
        var errors = new List<string>();
        if (!MarketSymbolPolicy.IsValid(symbol))
            errors.Add("종목 코드는 영문자, 숫자, 점(.)과 하이픈(-)만 사용할 수 있습니다.");
        if (name.Length > SymbolProfilePolicy.MaximumNameLength)
            errors.Add($"프로파일 이름은 {SymbolProfilePolicy.MaximumNameLength}자 이하여야 합니다.");

        if (command.EnabledPatterns?.Any(pattern => !PatternCatalog.IsOperationalBuiltIn(pattern)) == true)
            errors.Add("종목 프로파일에는 지원되는 내장 전략만 배정할 수 있습니다.");
        if (command.RiskPerTradePercent is <= 0 or > 1)
            errors.Add("거래당 손실 허용률은 0보다 크고 1 이하여야 합니다.");
        if (command.MaxTotalPositions is <= 0)
            errors.Add("전체 최대 보유 종목 수는 1개 이상이어야 합니다.");
        if (command.BacktestTrades is < 0)
            errors.Add("백테스트 거래 수는 0 이상이어야 합니다.");
        if (command.BacktestFrom.HasValue
            && command.BacktestTo.HasValue
            && command.BacktestFrom.Value > command.BacktestTo.Value)
            errors.Add("백테스트 시작일은 종료일보다 늦을 수 없습니다.");
        ValidateJson(command.ParameterOverridesJson, "전략 파라미터", errors);
        ValidateJson(command.WeightStrategyJson, "비중 관리 설정", errors);
        return errors;
    }

    private static void ValidateJson(string? json, string label, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(json))
            return;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                errors.Add($"{label}은 JSON 객체여야 합니다.");
        }
        catch (JsonException)
        {
            errors.Add($"{label}의 JSON 형식이 올바르지 않습니다.");
        }
    }
}
