# Yahoo historical UTC-window correction

Yahoo historical requests now preserve explicit UTC start and end instants. Previously the adapter
relabelled those clock values as server-local time before creating Unix timestamps. In the
production New York container, a summer intraday preview could therefore fetch data four hours
later than the selected range and omit most of the morning session.

Alpaca and Yahoo now share one validated UTC request-window policy. Unspecified date-only inputs are
also deterministic across developer and server time zones. Provider response parsing and stored bar
timestamps are unchanged.
