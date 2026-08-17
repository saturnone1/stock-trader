namespace StockTrader.Application.Strategies;

/// <summary>
/// 저장된 사용자 전략을 검증된 실행 모델로만 노출한다.
/// 손상되거나 실행 정책과 맞지 않는 정의는 호출 경로로 유출하지 않는다.
/// </summary>
public interface ICompiledStrategyRepository
{
    Task<IReadOnlyList<CompiledStrategy>> ListAsync(
        bool activeOnly = false,
        bool liveOnly = false,
        CancellationToken ct = default);

    Task<IReadOnlyDictionary<string, CompiledStrategy>> GetByNamesAsync(
        IEnumerable<string> names,
        CancellationToken ct = default);
}
