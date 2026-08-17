namespace StockTrader.Domain.Strategies;

/// <summary>영속 전략 문서 형식의 버전 식별자. 호환 정책과 저장소가 공유하는 단일 소유자다.</summary>
public static class StrategyDocumentVersions
{
    public const int LegacyUnversioned = 0;
    public const int Current = 1;
}
