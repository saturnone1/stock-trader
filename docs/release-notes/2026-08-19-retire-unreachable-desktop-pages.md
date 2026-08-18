# Retire unreachable desktop pages

- Removed legacy dashboard, signal, risk, and ML Svelte pages that had no navigation route.
- Removed their unused desktop API wrappers and generated-type aliases.
- Backend dashboard, signal, risk, and ML APIs remain available to supported clients and workers.

The desktop now has one explicit navigation-owned page set instead of retaining a second hidden
operational interface that could silently drift from visible screens.
