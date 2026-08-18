# Broker capability boundary

The account screen now shows, in Korean, which live operations each broker actually supports:
account and position lookup, order-history lookup, protected new entry, additional buy, full or
partial sell, and cancellation. Protected entry means the broker preserves the configured stop-loss
and profit target. LS account and position operations remain available, but automatic new entry is
blocked because its current adapter submits only the buy order without those protections.

The same metadata is enforced by the server. Unsupported new-entry, scaling, and exit operations are
rejected before any durable order claim is written, and background reconciliation checks order-history
support explicitly. The obsolete default-broker setting and duplicate keyed-DI factory were removed,
leaving account-owned broker construction as the single runtime path.
