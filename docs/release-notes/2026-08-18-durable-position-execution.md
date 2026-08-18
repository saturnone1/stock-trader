# Durable position execution foundation

- Replaced the exit-only live coordinator with one durable position-execution workflow for full
  exits, partial profit, scale-in, and scale-out orders.
- Added persisted execution kind and scaling-rule identity while retaining the existing SQLite
  column names for backward-compatible upgrades.
- Added per-position, per-rule scaling execution counts and atomic weighted-cost/quantity updates.
- Added trackable quantity-buy support to the broker port. Unsupported broker adapters fail closed.
- Broker acknowledgements and fills now require matching symbol, direction, quantity, intent kind,
  rule index, and partial-profit meaning before local state changes.
- Existing pending partial sells are preserved as partial sells during migration; they are not
  incorrectly promoted to full exits.
- Custom live scaling remains disabled until shared scaling evaluation and the central capital cap
  are wired into live monitoring.
