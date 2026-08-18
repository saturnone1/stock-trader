# Live position account routing

- Fixed automatic position monitoring so each durable position uses its owning trading account rather
  than whichever account is currently active.
- Pending position orders now reconcile through their stored account, while accountless legacy rows
  retain an explicit active-account fallback.
- Disabled owning accounts can still reduce risk but cannot receive automatic scale-in orders.
- Isolated the position monitoring cycle from its background scheduler and made polling and immediate
  order-resolution timing startup-validated settings.
- Added regression coverage for two accounts holding the same symbol with different prices and equity,
  durable pending-order routing, the legacy fallback, and same-cycle duplicate evaluation prevention.
