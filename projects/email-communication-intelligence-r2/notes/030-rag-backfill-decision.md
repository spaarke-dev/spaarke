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

## Known limitation (tracked follow-up, not a backfill item)

`ParentEntityContext.EntityTypes` supports only matter / project / invoice / account / contact. A
communication whose **primary** regarding is a **service request** (or work assignment / event / budget /
report card / analysis / organization) degrades to null grounding — the pre-existing behavior — because
`RegardingParentEntityMapper` will not fabricate an unsupported scheme or misfile to a lower-priority
regarding. Extending `ParentEntityContext.EntityTypes` is a change to the AI-owned contract (ADR-013 / §11)
and is out of scope for this Communication-side task. **Filed as a deferral** (see `notes/defer-issues.md`).
