# Trade activity query and contract correction

- Recommendation execution status, pending time, risk/reward, and stop distance now come from one
  application query observed at a single timestamp.
- Completed-trade pages now use deterministic timestamp-and-ID ordering and central pagination
  limits. Negative offsets, page sizes outside 1–500, and reversed date ranges return validation
  errors before database access.
- Invalid pattern or date query text and unknown numeric pattern codes now return HTTP 400 instead
  of silently removing the filter.
  This intentional behavior change prevents a malformed filtered request from appearing to be a
  valid unfiltered trading result.
- Filter parsing errors use the same response contract as page/range validation, so the desktop
  shows the concrete reason instead of a generic HTTP 400 message.
- Recommendation and history responses are explicit generated contracts. The desktop reads only
  their canonical camel-case fields and no longer masks API casing drift.
- Recommendation and history screens display central investor-facing strategy and order-mode names
  while the API retains stable codes. Custom trades display their actual stored strategy name.
