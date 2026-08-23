# Deterministic Stage 2 optimization correction

Synchronous, background, and extracted Optimization Worker executions now build the same Stage 2
candidate pool in stable generated-combination order. Neighbor candidates are preferred, already
tested Stage 1 candidates are excluded, and any unused fine-search budget is filled from the same
deterministic remaining sequence.

Previously the background optimizer shuffled fallback candidates using the database Job ID, while
the synchronous path neither used that seed nor reliably filled the Stage 2 budget. Two identical
requests over identical prepared data could therefore produce different historical rankings solely
because they were stored as different jobs or executed through a different adapter. Those old
differences were not market behavior and were wrong.

The characterization test
`Stage2CandidatePool_UsesStableGeneratedOrderBecauseJobSeedChangedHistoricalResults` locks the new
job-identity-independent sequence. Existing stored optimization results are not rewritten; rerunning
the same request creates evidence under the corrected deterministic policy.
