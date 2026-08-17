using StockTrader.Domain.Strategies;
using StockTrader.Models;

namespace StockTrader.Application.Strategies;

/// <summary>
/// 저장 전략 문서의 버전 호환성 경계다. 버전이 없던 기존 API 문서는 읽되,
/// 저장할 때는 현재 버전을 명시하고 알 수 없는 미래 문서는 추측해서 실행하지 않는다.
/// </summary>
public static class StrategyDocumentVersionPolicy
{
    public static string? Validate(int version) => version switch
    {
        StrategyDocumentVersions.LegacyUnversioned or StrategyDocumentVersions.Current => null,
        < StrategyDocumentVersions.LegacyUnversioned => "전략 문서 버전은 0 이상이어야 합니다.",
        _ => $"이 프로그램이 지원하지 않는 전략 문서 버전입니다. 지원 버전: {StrategyDocumentVersions.Current}, 입력 버전: {version}"
    };

    public static void StampCurrent(CustomPatternDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        definition.DocumentVersion = StrategyDocumentVersions.Current;
    }
}
