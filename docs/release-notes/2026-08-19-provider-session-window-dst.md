# Provider regular-session DST correction

Yahoo dated intraday requests now derive the US regular session from the central market calendar
instead of assuming a fixed 13:30–20:00 UTC window. The request remains 09:30–16:00 Eastern in both
summer and winter, so winter requests no longer start an hour early or omit the final trading hour.

Alpaca and Yahoo now share one deterministic local-date-to-UTC session policy. Exact summer and
winter request-window tests characterize the intentional Yahoo result correction. Provider-specific
transport and response parsing are unchanged.
