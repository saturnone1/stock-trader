namespace StockTrader.Domain.MarketData;

/// <summary>하루의 거래 상태.</summary>
public enum TradingDayStatus
{
    /// <summary>정규장 전일 운영.</summary>
    Open,

    /// <summary>주말 휴장.</summary>
    Weekend,

    /// <summary>거래소 공휴일 휴장.</summary>
    Holiday,

    /// <summary>조기마감. 정규 종료시각보다 일찍 마감한다.</summary>
    EarlyClose
}

/// <summary>특정 날짜의 거래 상태와, 조기마감이면 그날의 실제 마감 시각.</summary>
public sealed record TradingDayEvidence(
    DateOnly Date,
    TradingDayStatus Status,
    TimeSpan? EarlyCloseTime = null)
{
    public bool IsTradingDay => Status is TradingDayStatus.Open or TradingDayStatus.EarlyClose;
}

/// <summary>
/// 거래소별 휴장일·조기마감 근거의 단일 원천.
///
/// 이전에는 "주중이면 거래일"이라는 주말 전용 규칙이 유일한 판정이었다. 그 규칙은 추수감사절,
/// 성탄절, 광복절 같은 휴장일에 거래일이 존재한다고 잘못 답하고, 조기마감일의 오후 시간대를
/// 정규장으로 취급했다. 캘린더가 다루지 않는 기간에 대해서는 거래일 여부를 추측하지 않고
/// 명시적으로 실패한다.
/// </summary>
public static class ExchangeCalendarCatalog
{
    // 미국 정규 휴장일 (NYSE/NASDAQ). 관측 규칙이 적용된 실제 휴장 날짜.
    private static readonly IReadOnlySet<DateOnly> UnitedStatesHolidays = new HashSet<DateOnly>
    {
        // 2024
        new(2024, 1, 1), new(2024, 1, 15), new(2024, 2, 19), new(2024, 3, 29),
        new(2024, 5, 27), new(2024, 6, 19), new(2024, 7, 4), new(2024, 9, 2),
        new(2024, 11, 28), new(2024, 12, 25),
        // 2025
        new(2025, 1, 1), new(2025, 1, 9), new(2025, 1, 20), new(2025, 2, 17),
        new(2025, 4, 18), new(2025, 5, 26), new(2025, 6, 19), new(2025, 7, 4),
        new(2025, 9, 1), new(2025, 11, 27), new(2025, 12, 25),
        // 2026
        new(2026, 1, 1), new(2026, 1, 19), new(2026, 2, 16), new(2026, 4, 3),
        new(2026, 5, 25), new(2026, 6, 19), new(2026, 7, 3), new(2026, 9, 7),
        new(2026, 11, 26), new(2026, 12, 25),
        // 2027
        new(2027, 1, 1), new(2027, 1, 18), new(2027, 2, 15), new(2027, 3, 26),
        new(2027, 5, 31), new(2027, 6, 18), new(2027, 7, 5), new(2027, 9, 6),
        new(2027, 11, 25), new(2027, 12, 24)
    };

    // 미국 조기마감일 (13:00 ET). 독립기념일 전일, 추수감사절 다음날, 성탄 전야.
    private static readonly IReadOnlyDictionary<DateOnly, TimeSpan> UnitedStatesEarlyCloses =
        new Dictionary<DateOnly, TimeSpan>
        {
            [new(2024, 7, 3)] = new(13, 0, 0),
            [new(2024, 11, 29)] = new(13, 0, 0),
            [new(2024, 12, 24)] = new(13, 0, 0),
            [new(2025, 7, 3)] = new(13, 0, 0),
            [new(2025, 11, 28)] = new(13, 0, 0),
            [new(2025, 12, 24)] = new(13, 0, 0),
            [new(2026, 11, 27)] = new(13, 0, 0),
            [new(2026, 12, 24)] = new(13, 0, 0),
            [new(2027, 11, 26)] = new(13, 0, 0)
        };

    // 한국거래소 휴장일. 대체공휴일과 임시공휴일을 반영한 실제 휴장 날짜.
    private static readonly IReadOnlySet<DateOnly> KoreaHolidays = new HashSet<DateOnly>
    {
        // 2024
        new(2024, 1, 1), new(2024, 2, 9), new(2024, 2, 12), new(2024, 3, 1),
        new(2024, 4, 10), new(2024, 5, 1), new(2024, 5, 6), new(2024, 5, 15),
        new(2024, 6, 6), new(2024, 8, 15), new(2024, 9, 16), new(2024, 9, 17),
        new(2024, 9, 18), new(2024, 10, 1), new(2024, 10, 3), new(2024, 10, 9),
        new(2024, 12, 25), new(2024, 12, 31),
        // 2025
        new(2025, 1, 1), new(2025, 1, 28), new(2025, 1, 29), new(2025, 1, 30),
        new(2025, 3, 3), new(2025, 5, 1), new(2025, 5, 5), new(2025, 5, 6),
        new(2025, 6, 3), new(2025, 6, 6), new(2025, 8, 15), new(2025, 10, 3),
        new(2025, 10, 6), new(2025, 10, 7), new(2025, 10, 8), new(2025, 10, 9),
        new(2025, 12, 25), new(2025, 12, 31),
        // 2026
        new(2026, 1, 1), new(2026, 2, 16), new(2026, 2, 17), new(2026, 2, 18),
        new(2026, 3, 2), new(2026, 5, 1), new(2026, 5, 5), new(2026, 5, 25),
        new(2026, 6, 3), new(2026, 8, 17), new(2026, 9, 24), new(2026, 9, 25),
        new(2026, 10, 5), new(2026, 10, 9), new(2026, 12, 25), new(2026, 12, 31),
        // 2027
        new(2027, 1, 1), new(2027, 2, 8), new(2027, 2, 9), new(2027, 3, 1),
        new(2027, 5, 5), new(2027, 5, 13), new(2027, 6, 7), new(2027, 8, 16),
        new(2027, 9, 14), new(2027, 9, 15), new(2027, 9, 16), new(2027, 10, 4),
        new(2027, 12, 25), new(2027, 12, 31)
    };

    /// <summary>캘린더가 실제 근거를 보유한 기간. 이 밖은 알 수 없는 것으로 취급한다.</summary>
    private static readonly DateOnly CoverageStart = new(2024, 1, 1);
    private static readonly DateOnly CoverageEnd = new(2027, 12, 31);

    public static string Version => MarketCalendarVersion.Current;

    /// <summary>해당 날짜가 캘린더 근거 보유 범위 안에 있는지 여부.</summary>
    public static bool CoversDate(DateOnly date) => date >= CoverageStart && date <= CoverageEnd;

    /// <summary>
    /// 해당 거래일의 상태를 반환한다.
    /// 캘린더가 다루지 않는 날짜는 추측하지 않고 <see cref="MarketCalendarCoverageException"/> 로 실패한다.
    /// </summary>
    public static TradingDayEvidence GetTradingDay(MarketRegion market, DateOnly date)
    {
        if (!CoversDate(date))
            throw new MarketCalendarCoverageException(market, date, CoverageStart, CoverageEnd);

        if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return new TradingDayEvidence(date, TradingDayStatus.Weekend);

        var holidays = HolidaysFor(market);
        if (holidays.Contains(date))
            return new TradingDayEvidence(date, TradingDayStatus.Holiday);

        if (EarlyClosesFor(market).TryGetValue(date, out var earlyClose))
            return new TradingDayEvidence(date, TradingDayStatus.EarlyClose, earlyClose);

        return new TradingDayEvidence(date, TradingDayStatus.Open);
    }

    /// <summary>해당 거래일의 실제 마감 시각. 조기마감이면 그 시각, 아니면 정규 마감.</summary>
    public static TimeSpan ResolveCloseTime(MarketRegion market, DateOnly date)
    {
        var evidence = GetTradingDay(market, date);
        return evidence.EarlyCloseTime ?? MarketRegionCatalog.Get(market).RegularClose;
    }

    private static IReadOnlySet<DateOnly> HolidaysFor(MarketRegion market) => market switch
    {
        MarketRegion.UnitedStates => UnitedStatesHolidays,
        MarketRegion.Korea => KoreaHolidays,
        _ => throw new ArgumentOutOfRangeException(
            nameof(market), market, "휴장일 근거가 없는 시장입니다.")
    };

    private static IReadOnlyDictionary<DateOnly, TimeSpan> EarlyClosesFor(MarketRegion market) =>
        market switch
        {
            MarketRegion.UnitedStates => UnitedStatesEarlyCloses,
            // 한국거래소는 정기 조기마감일을 운영하지 않는다.
            MarketRegion.Korea => new Dictionary<DateOnly, TimeSpan>(),
            _ => throw new ArgumentOutOfRangeException(
                nameof(market), market, "조기마감 근거가 없는 시장입니다.")
        };
}

/// <summary>캘린더가 근거를 보유하지 않은 기간에 거래일 판정을 요청했을 때 발생한다.</summary>
public sealed class MarketCalendarCoverageException : InvalidOperationException
{
    public MarketCalendarCoverageException(
        MarketRegion market, DateOnly requested, DateOnly coverageStart, DateOnly coverageEnd)
        : base($"{market} 시장 거래소 캘린더가 {requested:yyyy-MM-dd} 를 다루지 않습니다. " +
               $"보유 범위: {coverageStart:yyyy-MM-dd} ~ {coverageEnd:yyyy-MM-dd}. " +
               "휴장일 근거 없이 거래일 여부를 추측하지 않습니다.")
    {
        Market = market;
        RequestedDate = requested;
        CoverageStart = coverageStart;
        CoverageEnd = coverageEnd;
    }

    public MarketRegion Market { get; }
    public DateOnly RequestedDate { get; }
    public DateOnly CoverageStart { get; }
    public DateOnly CoverageEnd { get; }
}
