# Exchange holiday and early-close correction

Trading-day decisions now use exchange holiday and early-close evidence instead of a weekend-only
rule. Previously any weekday counted as a full regular session, so the market was reported open on
Thanksgiving, Christmas, Good Friday, Korean substitute holidays, and every other weekday closure.
The afternoon of a 13:00 ET early-close day was likewise treated as regular session time.

This intentionally changes behavior at those dates. Live order placement, manual entry, intraday
ingestion, and position monitoring correctly refuse to act on a closed session where they previously
proceeded. `MarketCalendarTests` and `ExchangeCalendarCatalogTests` characterize each corrected case,
including the accepted morning and rejected afternoon of the same early-close day.

The calendar carries evidence for 2024 through 2027 and states its version. A date outside that range
is not guessed: the live market gate logs an error and reports the market closed, and the analytical
`GetTradingDay` surface raises `MarketCalendarCoverageException`. Extending the calendar also advances
`MarketCalendarVersion`, which is recorded in backtest result metadata.

Backtest results additionally now state the conditions that produced them — provider, market region
and time zone, time frame, price adjustment mode, session scope, calendar version, and warmup
requirements. This makes the previously invisible LS Securities split explicit: its daily and weekly
bars are split/dividend adjusted while its intraday bars are unadjusted. Stored results and the
desktop contract expose this evidence; no simulation arithmetic changed as a result of adding it.

The LS Securities daily-bar fallback that aggregated 60-minute bars when the t8410 request returned
nothing has been removed. The minute TR carries no adjustment field, so that fallback produced
unadjusted daily bars, and the shared bar identity `(Symbol, TimeFrame, Timestamp)` has no provider
or adjustment column to distinguish them from the adjusted series. A price series with split and
dividend discontinuities could therefore be stored as though it were adjusted, silently corrupting
every result computed from it afterwards. A failed t8410 request now logs an error and returns no
bars rather than substituting a series whose adjustment cannot be guaranteed. Callers already treat
an empty result as insufficient data. An architecture test prevents the fallback from returning.
