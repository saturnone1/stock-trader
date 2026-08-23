# Optimization path parity correction

Synchronous and background optimization now use the same out-of-sample boundary, 60/40 coarse and
fine budget, evenly distributed stage-one candidate sample, slippage, commission, and adaptive cost
model.

The Job-ID-seeded Stage 2 fallback described below was superseded on 2026-08-23 because it still
made identical requests depend on storage identity. See
`2026-08-23-deterministic-stage-two-optimization.md` for the corrected stable sequence.

Previously a background job shuffled stage-one candidates using its database job ID while the
synchronous path selected evenly spaced combinations. Re-running an identical request as a new job
could therefore rank a different candidate subset even with unchanged data. Stage-one selection is
now independent of job identity and has a golden sequence fixture. Stage-two restart fallback keeps
its persisted job seed so an interrupted job still resumes the same remaining order.
