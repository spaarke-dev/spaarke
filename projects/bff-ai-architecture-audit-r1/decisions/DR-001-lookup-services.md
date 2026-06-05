# DR-001 — Lookup Services (Category 2)

> **Author**: Phase 3 Sub-Agent K (synthesis from Phase 2 outputs)
> **Date**: 2026-06-04
> **Status**: PROPOSED (pending Q-002 owner review)
> **Pinned to**: Phase 1 inventory commit `357e6936`
> **Source analysis**: [`notes/phase2/analysis-lookup.md`](../notes/phase2/analysis-lookup.md)
> **Canonical authority**: [`notes/canonical-architecture-decisions.md` §2.2](../notes/canonical-architecture-decisions.md) · §3 (W1 Cat 2 row) · §8.3 (W1-9, W1-10)

## Context

Phase 1 inventory §2.2 catalogued 4 lookup services in `Services/Ai/Lookup/` (`PlaybookLookupService`, `ActionLookupService`, `SkillLookupService`, `ToolLookupService`) and surfaced the question whether a generic `ILookupService<T>` consolidation was warranted. Each service was a thin per-entity wrapper over `IGenericEntityService` Dataverse alternate-key lookups + `IMemoryCache` (1-hour TTL).

W1 Sub-Agent B audited each service against HARD GATES (grep production references, DI registration, publish-size delta). Empirical reproduction corrected inventory §2.2.1's "2 consumers" claim for `PlaybookLookupService` to **1 production consumer** (the second was a doc-cref only in `DefaultPlaybookConstants.cs`). The audit found 3 of the 4 services are orphans: `ActionLookupService`, `SkillLookupService`, `ToolLookupService` have zero production consumers across all HARD GATES.

The orphan cluster also leaves a dangling `cref` in `InsightsActionRouter.cs:402-403` and stale DI registrations in `FinanceModule.cs`. Total dead-code budget: ~714 LOC.

Cross-validation against W2 Cat 1 (intent classifier orphans) and W3 Cat 5 (prompt orphans) shows the same architectural pattern — over-anticipation of generic-consolidation needs that never materialized.

## Decision

1. **DELETE 3 orphan services** (bundled into single PR with W2 Cat 1 + W3 Cat 5 deletes per §1.6 of canonical-architecture-decisions):
   - `ActionLookupService.cs` + interface
   - `SkillLookupService.cs` + interface
   - `ToolLookupService.cs` + interface
   - `InsightsActionRouter.cs:402-403` dangling `cref` cleanup
   - `FinanceModule.cs` DI registration lines

2. **KEEP `PlaybookLookupService`** as the canonical reference implementation of the Spaarke Lookup Pattern.

3. **REJECT generic `ILookupService<T>` abstraction** as YAGNI per ADR-010 — after the DELETE cascade only one concrete remains; there is no abstraction warrant.

4. **DEGRADE the layer from "Stack" to "Pattern"** in canonical naming — single concrete impl does not warrant stack-level documentation; pattern-element documentation suffices.

## Consequences

### Positive
- ~714 LOC of dead code removed in single bundled PR (concrete publish-size measurement required per CLAUDE.md §10 bullet 4).
- Reduces `Services/Ai/Lookup/` directory cognitive load from 4 services to 1.
- Eliminates a maintenance trap — orphan code that future developers may inadvertently propagate or extend.
- Validates audit principle: REJECT forced consolidation when concrete impls do not align (canonical-architecture-decisions §5.1).

### Negative
- Loses optionality for future "we might add a `WhateverLookupService`" abstraction. Mitigated by the 4-element pattern doc (§"Canonical naming" below) — future lookup services follow the pattern; no abstraction is necessary.
- Bundled DELETE PR has Finance Intelligence as the cross-team owner — coordination required (per Q-003 sequential lock).

### Migration impact
- **Cross-team coordination**: Finance Intelligence team owns confirmation of the 3 DELETE orphans (no production consumers per all HARD GATES — should be uncontroversial).
- **Effort estimate**: **S (Small)** — single bundled PR; ~714 LOC removed; comment updates.
- **Sequencing**: Bundle with DR-005 (intent classifier orphan cascade) + DR-007 (Option B EXTRACT-then-DELETE) per §1.6 of canonical authority. Total bundle: ~2000 LOC.

## Canonical naming (Q-004 — surfaced not locked)

- **Candidate**: "Spaarke Lookup Pattern" (degraded from "Stack" to "Pattern" post-DELETE)
- **Reference impl**: `PlaybookLookupService` (`src/server/api/Sprk.Bff.Api/Services/Ai/Lookup/PlaybookLookupService.cs`)
- **Pattern elements** (4):
  1. Per-entity service (not generic) — domain boundaries are real
  2. Dataverse alternate-key lookup via `IGenericEntityService`
  3. In-process `MemoryCache` with 1-hour TTL + entity-coupled cache prefix (ADR-009 in-process exception convention; see DR-002)
  4. **Typed exception class** (e.g., `PlaybookNotFoundException`) — NOT generic `InvalidOperationException`

## ADR candidates from this decision (Q-005 — bullets only)

- **W1-9** Lookup-Service-Per-Entity Rule (anti-pattern doc, not full ADR) — LOW priority
- **W1-10** Lookup-Service Typed Exceptions — LOW priority

## Open questions for owner review (Q-002)

1. **DELETE confirmation**: Finance Intelligence owner confirms 3 orphan services + `InsightsActionRouter` dangling cref + `FinanceModule.cs` DI cleanup can be removed (no consumer surprises)?
2. **Bundling**: Bundle with DR-005 + DR-007 DELETE cascades (~2000 LOC total), or ship as standalone Finance-Intelligence-scoped PR? (Recommendation: bundle — single review pass; single publish-size measurement.)
3. **Pattern doc scope**: Author "Spaarke Lookup Pattern" as standalone pattern doc, or fold into the broader canonical-stack pattern doc?

## References

- Source analysis: [`notes/phase2/analysis-lookup.md`](../notes/phase2/analysis-lookup.md) §2-§3
- Wave summary: [`notes/phase2/wave-1-summary.md`](../notes/phase2/wave-1-summary.md) §3
- Canonical authority: [`notes/canonical-architecture-decisions.md`](../notes/canonical-architecture-decisions.md) §2.2 + §3 + §6 (inventory corrections #15) + §11.1
- Related ADR candidates: W1-9, W1-10 (LOW priority)
- Related DRs: **DR-002** (cache patterns — Lookup services consume the cache helper canonical), **DR-005** (cascade DELETE bundling), **DR-007** (cascade DELETE bundling)
- ADR cross-references: ADR-009 (cache discipline), ADR-010 (interface budget cap)
- Inventory corrections from this category: §6 row 15 (`PlaybookLookupService` consumer count 2→1)
