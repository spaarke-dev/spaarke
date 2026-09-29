# Email Communication Intelligence — R3

> **Status**: DRAFT charter — awaiting owner review before `/design-to-spec` → `/project-pipeline`.
> **Created**: 2026-09-07 · **Branch**: `work/email-communication-intelligence-r3`
> **Builds on**: `email-communication-intelligence-r2` (shipped, merged to master).

## What R3 is

R3 makes email→record matching **honest, measurable, and legible**. Four workstreams (see [`design.md`](design.md)):

| WS | Item | ADR gate | Depends on |
|---|---|---|---|
| **S** | **R3-SIGNAL-1** — "related to X" ranks high but stays auto-file-ineligible (split ranking from auto-file safety in FR-12) | ADR-045/FR-12 (§6.5 — Path A refinement) | G4 (measure before tuning) |
| **G4** | Tiered eval harness — golden labeled set + per-rung precision/recall + "no silent override" invariant test | none | — |
| **G5** | Party/relationship graph (deterministic) + suggest-only learned scorer (facade-routed, kill-switched) | none (§3.2) | G5.2 ⟵ G4.1 |
| **C** | Host `resolveDisplayName` wiring → real names on GUID-only thread/attachment cards (the R3-CARD-1 deferral) | none | — |
| **D** | Email-metadata facets (from/to/thread/date filterable in the RAG index) | none | **OPEN owner Y/N** (reindex cost) |

**Already shipped in r2** (recorded as closed context, not r3 work): R3-CARD-1 (GUID-safe card identity) + R3-CARD-2 ("See all" candidates modal) — PR #951.

## Explicitly NOT in scope
- The "5-tier matching ladder" as greenfield — **already built** as the 13-rung Association Engine. Do not rebuild.
- `IEmailFilterService` — **does not exist**. Do not build.
- Learned-scorer *auto-file* — the one ADR-045 Path-B amendment case; **not recommended**, deferred.

## Graduation criteria
1. Per-rung precision/recall exists for the engine on a ≥200-case golden set (today: none).
2. "Related to X" ranks in the visible candidate set for the PAT-942665/PAT-942404 regression case **with no new misfile** (auto-file counts unchanged), verified on the golden set.
3. Thread/attachment cards show a real record name+number in both reconciliation hosts.
4. Learned scorer (when enabled) improves top-1/top-3 recall on the golden set with **zero** change to auto-file counts.

## Source context (authored in r2)
- `projects/email-communication-intelligence-r2/notes/email-r3-candidate-backlog.md`
- `projects/email-communication-intelligence-r2/notes/G4-G5-matching-enhancements-scope.md`
- `projects/email-communication-intelligence-r2/notes/email-matching-and-triage-go-forward-plan.md`

## Sequencing
`G4.3 → G4.1 → G4.2 → S1` (measurement precedes the signal change); `G5.1` + `C1` in parallel; `G5.2` after G4.1.

## Next steps
See [`design.md`](design.md) §9. Owner reviews the §5 gates → `/design-to-spec` → `/project-pipeline`.
