# Unified position-order status

- Renamed the live evaluator and background manager around generic position execution rather than
  exit-only behavior.
- Replaced the open-position API's exit-only pending fields with one position-order status contract
  that includes full exit, partial profit, scale-in, and scale-out kinds.
- Replaced the operator reconciliation route with `/api/orders/reconcile-position-order`; the
  desktop now uses this sole route and does not retain a duplicate legacy endpoint.
- The portfolio screen labels pending work as a position order and shows a stock-oriented action
  label such as additional buy, partial profit, or scale-out.
