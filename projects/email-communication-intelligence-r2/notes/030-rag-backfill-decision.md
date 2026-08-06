# 030 — RAG grounding (FR-D1 / FR-06) backfill decision

> **Task**: 030 · **Date**: 2026-08-05 · **Decision owner**: project owner (2026-08-05, recorded in TASK-INDEX Resolved decisions #4)

## Decision: **forward-only** — no historical re-index, no backfill job

From this change forward, both RAG-index enqueue sites tag `PostUploadIndexingRequest.ParentEntity`
with the communication's resolved regarding (via `RegardingParentEntityMapper`). Correspondence indexed
**before** this change keeps its `ParentEntity = null` grounding; **no bulk re-index / backfill job is run.**

## Rationale

1. **Grounding heals as new correspondence indexes.** Matter-scoped RAG improves continuously from the
   deploy forward — every newly captured/sent email for a matter is grounded correctly. The gap is only
   the already-indexed backlog, which ages out of relevance for most matter-scoped queries.
2. **A mass re-index mutates many production rows** and re-runs the (cost-bearing) embedding pipeline over
   the entire historical corpus — disproportionate to the incremental benefit, and would require its own
   escalation per §10 / CLAUDE.md §6 (bulk production mutation).
3. **Consistent with the sibling forward-only decision** for the C3 `sprk_canonicalhash` column (task 023).

## If a backfill is ever wanted (not in R2 scope)

Model it on `scripts/Backfill-DocumentHasFile.ps1` (paged, resumable, non-fatal): enumerate
`sprk_document` rows sourced from communications with a resolved regarding but a null index parent, and
re-enqueue `PostUploadIndexingRequest` with the mapped `ParentEntity`. Gated behind an explicit operator
decision + escalation because it re-runs embeddings at scale.

## Representable grounding types (updated 2026-08-05)

`ParentEntityContext.EntityTypes` supports the three **core auto-file types** (matter / project / service
request) plus invoice / account / contact — i.e. every regarding type a correspondence RAG query realistically
scopes by. Service request was added 2026-08-05 (operator direction, closing DEFER-030-01) after confirming the
grounding path is a generic `parentEntityType eq …` filter with no per-type index routing. The remaining
non-core regarding targets (work assignment / event / budget / report card / analysis / organization) degrade
to null grounding **by design** — RAG scoping by those is not a product need; the one-line recipe to add one
later is documented in `RegardingParentEntityMapper`'s XML docs.
