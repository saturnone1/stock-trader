# Unified long-position execution session

Pattern preview and backtest now apply each bar through the same deterministic long-position
execution session. The session returns one ordered event stream and atomically updates current
quantity, weighted entry cost, realized profit and loss, protective-stop state, and per-rule scaling
execution counts.

Backtest custom exit and scaling conditions are resolved by one adapter for both ordinary held bars
and NextOpen entry bars. Previously a NextOpen fill evaluated its intrabar stop and target but did
not apply the custom close rule or scaling rule at that bar's close. Preview did apply those rules,
so the two paths could disagree on the first held bar.

Scale-in capacity now comes from a pure portfolio-cap policy instead of processor-local arithmetic.
Golden tests lock custom close and scale-out behavior on a NextOpen entry bar, fractional scaling,
daily/weekly/minute execution, partial exits, and the conservative same-bar priority. Live scaling
remains intentionally rejected until broker partial-order persistence and reconciliation can provide
the same guarantees.
