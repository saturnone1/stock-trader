# Explicit persistence and notification clocks

- Removed hidden wall-clock defaults from strategy, symbol-profile, financial, and optimization
  persistence entities; their application use cases remain the explicit timestamp owners.
- Discord alert/report timestamps now use the injected application clock.
- Email risk alerts now show a deterministic, explicitly labelled UTC generation time instead of the
  host's unspecified local time.
- Added architecture and payload regression coverage for these boundaries.
