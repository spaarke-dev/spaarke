# Implementation Plan — Spaarke Insights Engine, Phase 1.5 (r2)

> **Status**: Pipeline-ready (2026-05-31)
> **Source**: Derived from [spec.md](spec.md) §Scope and §Wave Sequencing
> **Companion**: [README.md](README.md), [design.md](design.md), [CLAUDE.md](CLAUDE.md)

---

## Overview

Phase 1.5 evolves the Insights Engine from r1's plumbing prototype into a usable multi-tenant, multi-practice-area, multi-entity platform. **29 tasks across 5 waves**, with **Wave B executing first** per owner direction (it's a ½-day unblock that proves Phase 1 design before architectural refactors begin).

**Acceptance bar** (per spec.md §Success Criteria): `predict-matter-cost@v1` produces real Inference/Decline live on Spaarke Dev; per-practice-area routing demonstrably works; multi-entity subjects resolve; `/api/insights/search` ships; Assistant invokes either path via intent classifier; SMEs iterate prompts in `sprk_analysisaction.sprk_systemprompt` without code deploys; `IngestOrchestrator` retired.

---

## Phase 1.5 goals (from spec.md §Scope)

1. Unblock `predict-matter-cost@v1` end-to-end (Wave B)
2. Author Phase 1.5 architectural foundations (Wave A — 2D taxonomy, prompt-variant pattern, JPS-refactor node breakdown, multi-entity resolver pattern)
3. Refactor universal-ingest from code (`IngestOrchestrator.cs`) into a JPS playbook on `PlaybookExecutionEngine`; migrate prompts from `.txt` → `sprk_analysisaction.sprk_systemprompt` (Wave C)
4. Implement 2D classification (practice-area × document-type) + multi-entity subjects (`matter:`, `project:`, `invoice:`); generalize `spaarke-insights-index` scope shape (Wave D)
5. Ship generic RAG retrieval (`POST /api/insights/search`), intent classifier, Spaarke Assistant tool-call integration (Wave E)
6. Project wrap-up: lessons-learned + Phase 2 priority outline (090)

---

## Discovered Resources

### Applicable ADRs (per spec.md §"Applicable ADRs")

| ADR | Why applicable | Tasks |
|---|---|---|
| [ADR-001](../../.claude/adr/ADR-001-minimal-api-and-workers.md) — Minimal API + BackgroundService | New `/api/insights/search` endpoint (Wave E1) | 040 |
| [ADR-008](../../.claude/adr/ADR-008-endpoint-filter-authorization.md) — Endpoint filters for auth | Both Insights endpoints (`/ask`, `/search`) | 040, 042 |
| [ADR-010](../../.claude/adr/ADR-010-di-minimalism.md) — DI minimalism (≤15 non-framework registrations) | Wave D5 multi-entity resolvers + Wave E classifier + RAG | 034, 041 |
| [ADR-013](../../.claude/adr/ADR-013-ai-architecture.md) — AI Architecture (facade pattern, refined 2026-05-20) | All Wave C/D/E — CRUD code consumes Insights only via `IInsightsAi` | All BFF code tasks |
| [ADR-027](../../.claude/adr/ADR-027-subscription-isolation-and-dataverse-solution-mgmt.md) — Solution management | Wave D1 schema additions → managed solution promotion | 030 |
| [ADR-028](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) — Spaarke Auth v2 | New `/api/insights/search` endpoint auth filter | 040 |
| [ADR-029](../../.claude/adr/ADR-029-bff-publish-hygiene.md) — BFF publish hygiene | Wave C/D/E NuGet additions, publish-size baseline | 020, 040 |
| [ADR-030](../../.claude/adr/ADR-030-bff-nullobject-kill-switch.md) — BFF Null-Object Kill-Switch Pattern (NEW 2026-06-01) | Any service registered in a `*Module.cs` `if (flag) { ... }` block consumed by an unconditionally-mapped endpoint. Three patterns: P1 Promote-to-unconditional / P2 Quiet no-op / P3 Fail-fast Null-Object (via `FeatureDisabledException`) | 020, 023, 034, 040, 041 |

### Applicable skills

| Skill | Used by |
|---|---|
| [task-execute](../../.claude/skills/task-execute/SKILL.md) | All — universal task driver |
| [adr-aware](../../.claude/skills/adr-aware/SKILL.md) | All — auto-loads applicable ADRs |
| [adr-check](../../.claude/skills/adr-check/SKILL.md) | All FULL-rigor tasks (Step 9.5) |
| [code-review](../../.claude/skills/code-review/SKILL.md) | All FULL-rigor tasks (Step 9.5) |
| [dataverse-create-schema](../../.claude/skills/dataverse-create-schema/SKILL.md) | Wave D1 (030) |
| [dataverse-deploy](../../.claude/skills/dataverse-deploy/SKILL.md) | Wave B4, D1, C1 deploy steps |
| [bff-deploy](../../.claude/skills/bff-deploy/SKILL.md) | Post-Wave C/E deployment |
| [dataverse-mcp-usage](../../.claude/skills/dataverse-mcp-usage/SKILL.md) | Wave B1, D1 (schema work via MCP) |
| [script-aware](../../.claude/skills/script-aware/SKILL.md) | Auto-loads applicable scripts |

### Knowledge docs

| Knowledge | Tags | Tasks |
|---|---|---|
| [knowledge/azure-ai-search/](../../knowledge/azure-ai-search/) | `ai-search`, `vector-search`, `spaarke-insights-index` | 035 (D6 index migration), 040 (E1 RAG endpoint) |
| [knowledge/agent-framework/](../../knowledge/agent-framework/) | `intent-classification`, `tool-calls` | 041 (E2 classifier), 042 (E3 Assistant tool-call contract) |

### Existing code patterns to reuse

| Pattern | File | Used by |
|---|---|---|
| Insights node executors | [`src/server/api/Sprk.Bff.Api/Services/Insights/Graph/`](../../src/server/api/Sprk.Bff.Api/Services/Insights/Graph/) (r1 — LiveFactNode, IndexRetrieveNode, EvidenceSufficiencyNode, etc.) | Wave C/D extend these |
| Endpoint pattern | Existing `/api/insights/ask` | Wave E1 (040) follows same pattern |
| DI registration | r1's `InsightsServiceCollectionExtensions.cs` | All wave additions |
| `sprk_analysisaction` row authoring (JPS prompt format) | Existing "Classify Document" action (see Spaarke Dev) | All Wave B1, C2, D2, D3 |
| `Deploy-Playbook.ps1` | `scripts/Deploy-Playbook.ps1` | Wave B3, C1 |
| `IInsightsAi` facade extension | r1's `Services/Ai/PublicContracts/IInsightsAi.cs` | Wave E1, D5 |

### Project-specific constraints

- **`.claude/constraints/bff-extensions.md`** — binding pre-merge checklist for any Wave C/D/E task that adds endpoints, services, DI registrations, or NuGet packages. Updated 2026-06-01 with binding sections **F.1 Asymmetric-Registration Tier 1.5 Anti-Pattern** (codifies ADR-030 enforcement at PR review), **F.2 Fixture-Config-FIRST Inspection Protocol** (relevant to Wave D7 fixtures), **F.3 Empirical-Reproduction-FIRST Protocol** (verify-before-fix when referencing r1 RB-T ledger entries)
- **§3.5 Zone A / Zone B grep gate** continues from r1 — every PR runs the forbidden-imports grep before merge
- **`IRagService` (existing per 2026-06-01 refactor)** — canonical RAG facade. Wave E1 (task 040) MUST consume this; do NOT inject `SearchIndexClient` directly into endpoint handlers

---

## Phase breakdown

### Wave B — Unblock synthesis (FIRST per owner direction; ~½ day)

| Task | ID | Title | Status | Est | Parallel-safe | Deps |
|---|---|---|---|---|---|---|
| 001 | B1 | Create 6 `sprk_analysisaction` rows in Spaarke Dev for Insights node ActionTypes | 🔲 | 2h | ❌ | — |
| 002 | B2 | Update `predict-matter-cost.playbook.json` to reference new action codes | 🔲 | 1h | ❌ | 001 |
| 003 | B3 | Update `Deploy-Playbook.ps1` action-wiring lint check | 🔲 | 1h | ✅ | — |
| 004 | B4 | Re-deploy playbook with action references | 🔲 | 30m | ❌ | 002, 003 |
| 005 | B5 | Live smoke verification — predict-matter-cost end-to-end | 🔲 | 1h | ❌ | 004 |
| 006 | B6 | Update Phase 1 verification doc with closed-gap status | 🔲 | 30m | ❌ | 005 |

**Acceptance**: Wave B closed when `POST /api/insights/ask` against a real Spaarke Dev matter returns either a real `InsightArtifact` (with ≥12 evidence refs from real Observations) OR a structured `DeclineResponse` from `EvidenceSufficiencyNode`. **NOT** the defensive scaffold decline.

---

### Wave A — Foundations (design docs; ~4 days)

| Task | ID | Title | Status | Est | Parallel-safe | Deps |
|---|---|---|---|---|---|---|
| 010 | A1 | Architecture overview refresh — Phase 1.5 framing | 🔲 | 4h | ✅ | — |
| 011 | A2 | Operator/developer guide refresh | 🔲 | 4h | ✅ | — |
| 012 | A3 | 2D taxonomy design — practice-area × document-type entity + matrix; initial 3 practice areas selected | 🔲 | 1d | ✅ | — |
| 013 | A4 | Prompt-variant + versioning + per-tenant-override design (within `sprk_analysisaction.sprk_systemprompt`) | 🔲 | 1d | ✅ | — |
| 014 | A5 | Universal-ingest JPS refactor design — node breakdown + parameterization | 🔲 | 6h | ✅ | — |
| 015 | A6 | Multi-entity subject design — scheme parsing, resolver pattern, index scope shape evolution | 🔲 | 6h | ✅ | — |

**Acceptance**: Six design docs land in `projects/ai-spaarke-insights-engine-r2/` (or refresh existing docs in `docs/`). Wave C/D/E task POMLs can be authored with concrete decisions in hand.

---

### Wave C — JPS compliance refactor (~4–6 days)

| Task | ID | Title | Status | Est | Parallel-safe | Deps |
|---|---|---|---|---|---|---|
| 020 | C1 | `universal-ingest@v1` JPS playbook authored in Dataverse (5–8 nodes per A5) | 🔲 | 2d | ❌ | 014 (A5), 013 (A4) |
| 021 | C2 | Prompts migrated from `.txt` files → `sprk_analysisaction.sprk_systemprompt` | 🔲 | 1d | ✅ | 013 (A4) |
| 022 | C3 | Retire `IngestOrchestrator.cs` + orphaned interfaces | 🔲 | 4h | ❌ | 020, 023 |
| 023 | C4 | Update `IInsightsAi.RunIngestAsync` to invoke JPS universal-ingest playbook | 🔲 | 4h | ❌ | 020 |
| 024 | C5 | Universal-ingest parameterization (tenant override, per-practice-area hints, cost-cap override) | 🔲 | 4h | ✅ | 020 |

**Acceptance**: zero `.txt` prompt files in `Services/Ai/Insights/Prompts/`; `IngestOrchestrator` class removed; universal-ingest runs as JPS playbook with parameter injection working.

---

### Wave D — 2D taxonomy + multi-entity (~1.5–2 weeks)

| Task | ID | Title | Status | Est | Parallel-safe | Deps |
|---|---|---|---|---|---|---|
| 030 | D1 | `sprk_documenttype_ref` entity + `sprk_practicearea_documenttype` N:N matrix | 🔲 | 1d | ❌ | 012 (A3) |
| 031 | D2 | Per-practice-area Layer 1 classification (top 3 practice areas per A3) | 🔲 | 3d | ✅ | 030, 021 |
| 032 | D3 | Per-(practice-area, document-type) Layer 2 extraction schemas (3–5 high-value pairs) | 🔲 | 3d | ✅ | 030, 021 |
| 033 | D4 | Universal-ingest playbook routes classification/extraction by practice-area | 🔲 | 1d | ❌ | 020, 031, 032 |
| 034 | D5 | Multi-entity subject schemes + per-entity `ILiveFactResolver` (`matter:`, `project:`, `invoice:`) | 🔲 | 2d | ✅ | 015 (A6), 023 |
| 035 | D6 | `spaarke-insights-index` scope shape generalization (carry `entityType` + `entityId`); migration | 🔲 | 2d | ✅ | 015 (A6) |
| 036 | D7 | Synthetic test fixtures across practice areas + entity types (LLM-generated) | 🔲 | 2d | ✅ | 031, 032, 034 |

**Acceptance**: ≥3 practice areas route correctly through Layer 1/2; `subject: "project:<guid>"` and `subject: "invoice:<guid>"` resolve live facts; index migration backward-compatible (Phase 1 Observations remain queryable).

---

### Wave E — Hybrid consumption + Assistant (~1.5–2 weeks)

| Task | ID | Title | Status | Est | Parallel-safe | Deps |
|---|---|---|---|---|---|---|
| 040 | E1 | `POST /api/insights/search` — wraps existing `IRagService` (effort 3d → 1.5d after master sync) | 🔲 | 1.5d | ✅ | 035 (D6) |
| 041 | E2 | Intent classifier (LLM-based for Phase 1.5; `forceMode` override) | 🔲 | 2d | ✅ | 040 |
| 042 | E3 | Spaarke Assistant integration (authors tool-call contract first, then implements) | 🔲 | 1w | ❌ | 040, 041 |
| 043 | E4 | Decision-tree doc: when to author a playbook vs rely on RAG | 🔲 | 4h | ✅ | 040, 041 |

**Acceptance**: `/api/insights/search` returns ranked Observations + LLM synthesis with citations; intent classifier routes natural-language queries; Assistant invokes either path via the new tool-call contract.

---

### Wrap-up

| Task | ID | Title | Status | Est | Parallel-safe | Deps |
|---|---|---|---|---|---|---|
| 090 | — | Project wrap-up — lessons-learned + Phase 2 priority outline + archive | 🔲 | 4h | ❌ | all prior |

---

## Parallel execution groups (high level — see TASK-INDEX.md for details)

| Group | Wave | Tasks | Prerequisite |
|---|---|---|---|
| W-B-serial | B | 001 → 002 → 004 → 005 → 006 (003 can run in parallel with 001) | — |
| W-A-parallel | A | 010, 011, 012, 013, 014, 015 (all independent) | B complete |
| W-C-mixed | C | 020 serial; 021 + 024 parallel after 020; 022 + 023 after dependencies | A4, A5 |
| W-D-parallel | D | 030 first; then 031 + 032 parallel; 033 after; 034 + 035 + 036 parallel | C + A3, A6 |
| W-E-mixed | E | 040 first; then 041 + 043 parallel; 042 after 041 | D6 |

---

## Critical path

`001 → 002 → 004 → 005` (Wave B, ~½ day) → `014 (A5) + 013 (A4)` (Wave A, ~1.5 days) → `020 (C1)` (~2 days) → `023 (C4)` (~½ day) → `030 (D1)` (~1 day) → `031 (D2)` (~3 days) → `033 (D4)` (~1 day) → `035 (D6)` (~2 days) → `040 (E1)` (~3 days) → `041 (E2)` (~2 days) → `042 (E3)` (~1 week) → `090` (~½ day).

**Critical path ≈ 4–5 weeks**. Parallel work brings total to ~5–7 weeks if executed sequentially per wave.

---

## High-risk items

| Risk | Wave | Mitigation |
|---|---|---|
| `PlaybookExecutionEngine` doesn't cleanly support Insights conditional-branch semantics | Wave A5, C1 | Wave 020 exercises engine; patches stay in engine (don't fork) |
| Prompt migration changes runtime behavior subtly | Wave C2 | Side-by-side comparison harness; new prompts get `@v2`; cutover with rollback |
| Per-practice-area prompt authoring is more time per area than estimated | Wave D2 | Initial scope = 3 practice areas (per A3); expand per customer cadence |
| `spaarke-insights-index` schema migration disrupts Phase 1 Observations | Wave D6 | Migration plan in A6; new fields nullable; old Observations queryable |
| Spaarke Assistant tool-call contract authoring lengthens Wave E3 | Wave E3 | E3 first sub-task = contract authoring; coordinate early with Assistant team |
