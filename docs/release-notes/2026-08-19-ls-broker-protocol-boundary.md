# LS broker protocol and order-history correction

- LS cash orders and cancellations now use the current `CSPAT00601` and `CSPAT00801` protocol
  identifiers.
- LS balance queries now send the documented `t0424` request fields and accept numeric response
  values encoded as either JSON numbers or strings.
- LS order history now converts requested UTC intervals to the required Korean trading dates and
  filters each row by its exact broker timestamp.
- The LS broker implementation is split into focused order, account, history, transport, protocol,
  and parsing components behind the existing broker facade.

Order-history results around Korean midnight may differ from prior releases. This is intentional:
the former implementation could omit a required Korean trading date and include evidence outside
the requested UTC interval.
