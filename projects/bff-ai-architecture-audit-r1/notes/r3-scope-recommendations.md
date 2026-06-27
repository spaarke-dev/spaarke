# r3 Scope Recommendations — Insights Engine R3 (Phase 2 reconciliation)

> **Author**: Phase 3 Sub-Agent J
> **Date**: 2026-06-04
> **Audience**: r3 project owner + Insights Engine team
> **Status**: UNBLOCKS r3 Wave 2 scope-locking per audit findings
> **Pinned to**: Phase 1 inventory commit `357e6936`; [`canonical-architecture-decisions.md`](canonical-architecture-decisions.md) is the design authority
> **Companion**: [`migration-plan.md`](migration-plan.md) (PR sequencing + effort + dependencies)

---

## §1 Executive Summary for r3

### §1.1 Why this doc exists

r3 Wave 2 (Tier 2.5 `InsightsIntentClassifier` ↔ `PlaybookDispatcher` reconciliation) was PROVISIONAL pending the BFF AI architecture audit. The audit was triggered specifically because r3 design discussion surfaced parallel intent classifiers + parallel infrastructure across 5+ projects (per [recovery memory 2026-06-04](../../../../C:/Users/RalphSchroeder/.claude/projects/c--code-files-spaarke-wt-ai-spaarke-insights-engine-r2/memory/context-recovery-2026-06-04-audit-phase-1.md)). This document delivers concrete scope guidance based on Phase 2 findings + Phase 3 synthesis.

### §1.2 Audit findings that AFFECT r3 directly

| Finding | r3 Impact | Source |
|---|---|---|
| **`PlaybookDispatcher` KEEP** (W2 Cat 1 verdict — 2-stage vector+LLM matching) | r3 Wave 2 uses existing `PlaybookDispatcher`; DOES NOT rebuild a parallel classifier | [§2.5](canonical-architecture-decisions.md#25-layer-5--spaarke-canonical-intent-classifier-pattern) |
| **`InsightsIntentClassifier` KEEP as canonical reference impl** | r3 Wave 2 reconciles into the canonical pattern (7 elements); preserves Insights's domain-specific structure | [§2.5](canonical-architecture-decisions.md#25-layer-5--spaarke-canonical-intent-classifier-pattern) |
| **DELETE 5-orphan cascade including `IntentClassificationService` + `BuildPlanGenerationService`** (~1,280 LOC) | r3 should NOT depend on these — they're confirmed dead code | [§1.6](canonical-architecture-decisions.md#16-bundled-delete-pr--2000-loc-dead-code) |
| **REJECT generic `IIntentClassifier<TResult>` consolidation** | r3 should NOT introduce a new generic classifier abstraction; preserve per-classifier domain-specific result types | [§5.1](canonical-architecture-decisions.md#51-reject-forced-consolidation--the-dominant-phase-2-finding) |
| **`playbook-embeddings` AI Search index KEEP** (renamed `spaarke-playbook-index` post-r3 Wave 1) | r3 Wave 2 leverages the renamed index directly; no new search substrate | [§2.6](canonical-architecture-decisions.md#26-layer-6--spaarke-canonical-search-substrate-architecture) |
| **LATENT BUG: `IInsightsAi` returns 500 instead of 503 under compound-AI-OFF** | r3 cannot ship Tier 2.5 expecting reliable 503 contract until audit Migration PR #1 lands; sequence accordingly | [§4](canonical-architecture-decisions.md#4-the-latent-bug-and-structural-remediation-pattern) |
| **`CapabilityRouter` KEEP** (3-tier keyword → LLM → fallback) | r3 should know about this peer router; r3 Wave 2 scope doesn't consume it but AIPU R2 owns it | [§2.5](canonical-architecture-decisions.md#25-layer-5--spaarke-canonical-intent-classifier-pattern) |
| **Spaarke Canonical Intent Classifier Pattern (7 elements)** | r3 Wave 2 implementation should explicitly conform to all 7 pattern elements | [§2.5](canonical-architecture-decisions.md#25-layer-5--spaarke-canonical-intent-classifier-pattern) |
| **`PlaybookBuilderSystemPrompt.cs` Option B EXTRACT-then-DELETE** | r3 should NOT touch this — audit PR #2 owns the cleanup | [§4.2](canonical-architecture-decisions.md#42-cross-wave-verification-convergence) |
| **Co-location rule for prompts** | r3 InsightsIntentClassifier extraction (Tier 2.5 F-2) MUST co-locate prompt with sole consumer, NOT under generic `/Prompts/` | [§2.7](canonical-architecture-decisions.md#27-layer-7--spaarke-canonical-prompt-construction-pattern) |

### §1.3 Audit findings that r3 should KNOW ABOUT but not act on

| Finding | Why r3 should know |
|---|---|
| 8 canonical pattern docs being authored | r3 Wave 2 implementation should reference Spaarke Canonical Intent Classifier Pattern doc when authored |
| 34 ADR candidates DEFERRED to follow-on phase | r3 may reference ADR candidates in design DR-### records; ADRs themselves land later |
| ACTION-TYPE-REGISTRY.md missing | r3 should NOT allocate a new `ActionType` enum value for Tier 2.5 (reconciliation should NOT introduce new node executors) |
| Runtime §F.1 detection fixture pending (Migration PR #8) | r3 should NOT block on the fixture; r3 Tier 1.1 (`NullInsightsAi`) is the prerequisite |
| 3 security adjudication surfaces (RecordSearch + SemanticSearch + PrivilegeGroupResolver) | None of these are in r3 scope; informational only |
| Cache adoption migration ~26 sites | r3 should NOT bundle this migration into Wave 2; audit's Migration PR #5 owns |
| 3-tier compound AI gate is the binding pattern | r3 Wave 2 reconciliation must respect compound gate symmetry per Endpoint↔DI Symmetry Rule |

### §1.4 r3 Wave 2 scope recommendation (CONCRETE)

**LOCK r3 Wave 2 scope to**: `InsightsIntentClassifier` ↔ `PlaybookDispatcher` reconciliation via the existing `spaarke-playbook-index` (post-Tier-1.5 rename) substrate, using `PlaybookDispatcher`'s two-stage matching (vector + LLM refinement) as the primary intent-routing mechanism. Insights becomes a thin wrapper with a "no playbook matched → RAG fallback" branch.

**Reduce r3 Wave 2 estimate** from original ~2-3 weeks to **~1 week (5 days)** per the audit-verified existence of `PlaybookDispatcher` + `spaarke-playbook-index` infrastructure.

**REJECT** any net-new abstractions (no `IIntentClassifier<T>`, no new search substrate, no new generic classifier service) per audit Phase 2 universal "REJECT forced consolidation" verdict.

**SEQUENCE** r3 Wave 2 AFTER audit Migration PR #1 (LATENT BUG remediation) lands — otherwise r3 inherits the 500-not-503 contract under compound-AI-OFF.

---

## §2 Direct recommendations for r3 Wave 1 (LOCKED, ~5d total)

### §2.1 Audit confirms r3 Wave 1 is consistent

The audit's findings VALIDATE all 5 r3 Wave 1 Tier 1 items as compatible with the canonical architecture decisions. No Wave 1 scope changes are required.

| r3 Wave 1 item | Audit consistency check | Recommendation |
|---|---|---|
| **1.1 `NullInsightsAi` facade** | ALIGNED. Audit's Migration PR #1 (LATENT BUG Option A) IS this exact work, bundled with 4 other Null peers + structural DI relocation. | r3 Wave 1.1 should be SUPERSEDED by audit Migration PR #1 — they ship together; r3 doesn't ship a separate `NullInsightsAi` PR. |
| **1.2 v1.2 contract — `spe://drive/X/item/Y` evidence-ref href** | NOT IN AUDIT SCOPE (audit covers BFF AI architecture only; href contract is r3-specific). No conflict. | Ship as r3 Wave 1 owns. |
| **1.3 Test-fixture hygiene** | ALIGNED. r3 Wave 1.3 fixture work is independent of audit; complementary to audit Migration PR #8 (Runtime §F.1 fixture). | Ship as r3 Wave 1 owns. Coordinate test-fixture taxonomy with audit Migration PR #8 author (Insights team — same team). |
| **1.4 Telemetry maturity** | NOT IN AUDIT SCOPE. No conflict. | Ship as r3 Wave 1 owns. |
| **1.5 Index rename `playbook-embeddings` → `spaarke-playbook-index`** | ALIGNED. Spaarke naming convention `spaarke-<resource>-index`. Audit assumes this rename is complete for Tier 2.5 reconciliation (per [canonical-architecture-decisions.md §2.6](canonical-architecture-decisions.md#26-layer-6--spaarke-canonical-search-substrate-architecture)). | Ship as r3 Wave 1 owns — DOES UNBLOCK Wave 2 reconciliation. Sequence Tier 1.5 BEFORE Tier 2.5 (per r3 design.md §2.1.1 "Coordination with Tier 2.5"). |

### §2.2 Critical sequencing note for r3 Wave 1.1

r3 Wave 1.1 (`NullInsightsAi` facade) is **DUPLICATIVE** with audit Migration PR #1. Two options:

| Option | Description | Recommendation |
|---|---|---|
| **A (RECOMMENDED)** | r3 Wave 1.1 is REMOVED from r3 scope; audit Migration PR #1 ships the work | Cleanest separation; r3 doesn't duplicate effort. r3 Wave 1 estimate reduces ~0.5d. |
| B | r3 Wave 1.1 ships first (smaller, focused PR); audit Migration PR #1 ships the 4 Null peers + structural relocation later | More PRs; risk of partial Option A coverage. NOT RECOMMENDED. |

**Recommended**: r3 owner adopts Option A; r3 Wave 1 becomes 4 items (1.2 + 1.3 + 1.4 + 1.5); audit Migration PR #1 ships `NullInsightsAi` + 4 Null peers + structural relocation + comment fix + integration test as a unified PR.

---

## §3 Direct recommendations for r3 Wave 2 (the audit's primary unblock)

### §3.1 Concrete scope guidance

#### DO (audit-confirmed canonicals to leverage)

- **DO**: Leverage existing `PlaybookDispatcher` (`src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookDispatcher.cs`) for playbook selection. Audit verdict W2 Cat 1: KEEP (2-stage vector+LLM matching is canonical for SprkChat + Insights — shared invocation pattern).
- **DO**: Leverage existing `playbook-embeddings` AI Search index (renamed `spaarke-playbook-index` after r3 Wave 1.5). Audit verdict W2 Cat 3: KEEP (per-substrate architecture rejects merger).
- **DO**: Leverage `CapabilityRouter` (3-tier keyword → LLM → fallback) if r3 Wave 2 needs capability-level routing (distinct from playbook-level). Audit verdict W2 Cat 1: KEEP (peer pattern).
- **DO**: Conform new code to **Spaarke Canonical Intent Classifier Pattern** (7 elements per [canonical-architecture-decisions.md §2.5](canonical-architecture-decisions.md#25-layer-5--spaarke-canonical-intent-classifier-pattern)):
  1. Singleton DI lifetime (stateless, threadsafe)
  2. `IOptions<T>` binding from feature-specific config section
  3. ADR-032 §F.1 dual registration with Null peer (always-registered)
  4. JSON-schema-constrained LLM decoding (deterministic output shape)
  5. SHA-256 cache key construction over normalized query (in-process `MemoryCache` with documented ADR-009 exception)
  6. OTEL-instrumented latency tracking with FR-05-style budget (500ms target)
  7. Domain-specific result type (NOT generic `IClassifier<T>`)
- **DO**: Co-locate extracted prompt with `InsightsIntentClassifier` consumer (NOT under generic `/Prompts/` dir). Audit verdict W3 Cat 5: co-location rule (ADR-CAND-G-03).
- **DO**: Use JPS `sprk_analysisaction.sprk_systemprompt` row for the extracted prompt (per r3 design.md §2.2.1 F-2 work item + project's "no .txt prompt files" principle).
- **DO**: Preserve `InsightsIntentClassifier`'s domain-specific result type (`Services.Ai.Insights.Routing.IntentClassificationResult`). Audit candidate rename `InsightsRoutingDecision` is LOW-priority — does NOT block Wave 2.

#### DO NOT (audit-rejected anti-patterns)

- **DO NOT**: Build a new generic intent classifier abstraction (e.g., `IIntentClassifier<TResult>`). Audit verdict: REJECTED across all 8 categories. Forced abstraction loses type safety + diverges per-substrate concerns.
- **DO NOT**: Introduce a new search substrate (e.g., re-create `playbook-embeddings` with different shape). Audit verdict W2 Cat 3: REJECT `PlaybookEmbeddingService ↔ SemanticSearchService` merger.
- **DO NOT**: Add new prompt builders without justification — apply Spaarke Canonical Prompt Construction Pattern (4 elements). Audit verdict W3 Cat 5: REJECT generic `IPromptComposer`.
- **DO NOT**: Modify `PlaybookBuilderSystemPrompt.cs` — audit Migration PR #2 owns the Option B EXTRACT-then-DELETE; r3 should NOT touch.
- **DO NOT**: Introduce new `ActionType` enum values for reconciliation work (reconciliation is wrapping pattern, not new node executors).
- **DO NOT**: Inject `IOpenAiClient` or `IPlaybookService` directly into r3 Wave 2 endpoints — use the Public-Contracts Facade (`IInsightsAi`) per ADR-013 / CLAUDE.md §10.3.

#### WAIT FOR (sequencing dependencies)

- **WAIT FOR**: Audit Migration PR #1 (LATENT BUG remediation) to land BEFORE r3 Wave 2 ships. Otherwise r3 Wave 2 deploys against a broken 500-not-503 contract under compound-AI-OFF. Recommended: open r3 Wave 2 PR draft for review parallel with PR #1; merge r3 Wave 2 only after PR #1 lands.
- **WAIT FOR**: r3 Wave 1.5 (index rename) to ship in Dev BEFORE r3 Wave 2 reconciliation work begins — avoids double-rename.
- **OPTIONAL WAIT**: Audit Migration PR #8 (Runtime §F.1 detection fixture) — r3 Wave 2 doesn't strictly need this, but having the fixture green would build confidence that r3 Wave 2 doesn't reintroduce §F.1 anti-pattern.

---

## §4 Inputs that change r3 Wave 2 scope

### §4.1 The reduction story — ~2-3 weeks → ~1 week

Original r3 design.md §2.5 framing (pre-audit): "build playbook self-description schema + indexing substrate" — implied building infrastructure from scratch.

Recovery memory ([context-recovery-2026-06-04-audit-phase-1.md](../../../../C:/Users/RalphSchroeder/.claude/projects/c--code-files-spaarke-wt-ai-spaarke-insights-engine-r2/memory/context-recovery-2026-06-04-audit-phase-1.md)) documented: "r3 Tier 2.5 reconciliation: ~2-3 weeks → ~1 week" after audit discovers existing `PlaybookDispatcher` + `playbook-embeddings` index.

Audit Phase 2 confirms this: **the substrate exists**. r3 Wave 2 is wrapping + extraction work, not infrastructure build-out.

### §4.2 Concrete reconciliation work items

Per r3 design.md §2.2.1 + audit Phase 2 findings, the reconciliation work items are:

| ID | Title | Audit alignment | Effort |
|---|---|---|---|
| **F-1** | Reconciliation decision spike | Audit-confirmed options: (a) replace `InsightsIntentClassifier` with `PlaybookDispatcher` wrapper; (b) refactor Insights to share Stage 1 vector search only; (c) keep separate (NOT RECOMMENDED per audit "REJECT consolidation" — but reconciling consumption is different from forcing abstraction). | **0.5d** |
| **F-2** | Migrate Insights classifier prompt to JPS Action | Audit ALIGNED: co-location rule (W3 Cat 5 ADR-CAND-G-03) + canonical pattern element #7 (domain-specific result type). Prompt becomes `sprk_analysisaction.sprk_systemprompt` row per CLAUDE.md §4. | **1d** |
| **F-3** | Implement reconciliation per F-1 decision | Audit RECOMMENDS option (a): Insights becomes thin wrapper around `PlaybookDispatcher` with "no playbook matched → RAG fallback" branch. Preserves FR-05 safety + Spaarke Canonical Intent Classifier Pattern elements 4-7. | **2-3d** |
| **F-4** | Index Insights playbooks | Audit ALIGNED. Ensure `predict-matter-cost@v1` + future Insights playbooks present in `spaarke-playbook-index` (renamed) with `description` + `triggerPhrases` + `tags`. | **0.5d** |
| **F-5** | JPS authoring flow: auto-populate metadata at playbook creation | Audit ALIGNED. `Deploy-Playbook.ps1` (or successor) auto-populates `description` + `triggerPhrases` + triggers re-indexing. | **0.5-1d** |

**Total: ~5 days (1 week)** — confirms r3 design.md §2.2.1 estimate.

### §4.3 Audit-derived additional consideration

The audit's **Spaarke Endpoint↔DI Symmetry Rule** (W4 §4.1) is binding for r3 Wave 2:
- If r3 Wave 2 introduces any new service registered behind a feature flag (e.g., `Insights:Reconciliation:Enabled`), it MUST satisfy the rule:
  1. Consumer endpoint mapped behind SAME feature flag (symmetric), OR
  2. Null peer registered in gate-OFF branch with same lifetime + identical `ServiceDescriptor.ServiceType` (ADR-032 P3 Fail-Fast), AND transitive ctor deps satisfy same rule, AND startup integration test asserts gate-OFF returns 503 not 500.

**Practical impact**: r3 Wave 2 reconciliation should NOT introduce new feature-flagged services. If `Insights:Reconciliation:Enabled` flag is desired for staged rollout, it must be paired with a Null peer + integration test.

---

## §5 Cross-project coordination needs for r3

### §5.1 AIPU R2 — `CapabilityRouter` owner

| Need | Detail |
|---|---|
| What audit needs from AIPU R2 | Confirm `CapabilityRouter` (3-tier keyword → LLM → fallback) usage patterns; Insights r3 Wave 2 doesn't directly consume but pattern coexistence is documented |
| What r3 needs from AIPU R2 | Awareness of `CapabilityRouter` peer pattern (audit Sub-Agent E §2.3.1 documented 2 behavioral + 2 sentinel consumers) |
| Sequencing | Out-of-band; no blocking dep on r3 Wave 2 |

### §5.2 SprkChat r2 — `PlaybookDispatcher` owner

| Need | Detail |
|---|---|
| What audit needs from SprkChat | Confirm `PlaybookDispatcher` shared invocation pattern is suitable for Insights consumption (audit verdict KEEP; no SprkChat objection expected) |
| What r3 needs from SprkChat | Confirm semantic shape of `PlaybookDispatcher` API surface is stable for r3 Tier 2.5 consumption; coordinate XML doc amendment (per audit Migration PR #3 — SprkChat row) lead with tenant-scoping rationale |
| Sequencing | r3 Wave 2 F-1 decision spike consults SprkChat owner; ~1-day coordination cycle |

### §5.3 Insights Engine r2 (predecessor; closed)

| Need | Detail |
|---|---|
| Coordination needed | NONE — r2 closed; no active development; r3 inherits codebase as-is |

### §5.4 Audit team (Migration PR #1 LATENT BUG remediation)

| Need | Detail |
|---|---|
| What audit needs from r3 | Confirmation that r3 Wave 1.1 is superseded by audit Migration PR #1 (per §2.2 Option A) |
| What r3 needs from audit | Migration PR #1 must land BEFORE r3 Wave 2 ships (per §3.1 WAIT FOR) |
| Sequencing | r3 owner + audit team align timing; recommended: r3 Wave 1 starts immediately (1.2 + 1.3 + 1.4 + 1.5); r3 Wave 2 starts after audit Migration PR #1 lands |

### §5.5 R5 (Spaarke Assistant tool-call consumer)

| Need | Detail |
|---|---|
| Coordination needed | NONE for r3 Wave 2 (per r3 design.md §2.2.1 "no contract change; R5 sees same `POST /api/insights/assistant/query` v1.1") |
| Out-of-band | R5 may benefit from r3 Wave 2 improvements (better playbook routing → fewer RAG fallbacks); informational only |

### §5.6 AI Chat Playbook Builder team (Migration PR #2 DELETE cascade)

| Need | Detail |
|---|---|
| What audit needs from AI Chat Playbook Builder | Confirm DELETE of `IntentClassificationService` + `BuildPlanGenerationService` + Option B EXTRACT for `PlaybookBuilderSystemPrompt.cs` |
| What r3 needs from AI Chat Playbook Builder | NONE directly; r3 does NOT depend on the deleted services |
| Sequencing | Out-of-band; no blocking dep on r3 Wave 2 |

---

## §6 What r3 should DOCUMENT in its design.md updates

### §6.1 Replace Wave 2 PROVISIONAL section with this guidance

Update r3 [`design.md` §2.2.1](../../ai-spaarke-insights-engine-r2/projects/ai-spaarke-insights-engine-r3/design.md) (currently PROVISIONAL pending audit):

- Status: **LOCKED** (was PROVISIONAL); unblocked by `bff-ai-architecture-audit-r1` Phase 3
- Total effort: confirmed **~5 days (1 week)** per audit verification
- Decision authority: [`canonical-architecture-decisions.md`](canonical-architecture-decisions.md) — cite §2.5 (Intent Classifier Pattern) + §2.6 (Search Substrate) + §4 (LATENT BUG) + §4.4 (Endpoint↔DI Symmetry Rule)
- Migration dependency: cite [`migration-plan.md`](migration-plan.md) PR #1 + PR #2 as dependencies; PR #1 is BLOCKING

### §6.2 Add new section: r3 Wave 2 audit-compatibility commitments

Recommend r3 design.md add a new section (e.g., §2.2.2) documenting:
1. **Pattern conformance**: All 7 elements of Spaarke Canonical Intent Classifier Pattern (per [§2.5](canonical-architecture-decisions.md#25-layer-5--spaarke-canonical-intent-classifier-pattern))
2. **Substrate reuse**: Existing `spaarke-playbook-index` + `PlaybookDispatcher` (no new substrate)
3. **Endpoint↔DI Symmetry Rule compliance**: No new feature-flagged services without Null peers
4. **Co-location rule**: Extracted prompt co-located with `InsightsIntentClassifier`, NOT under generic `/Prompts/`
5. **Result type preservation**: `Services.Ai.Insights.Routing.IntentClassificationResult` retained; no generic abstraction

### §6.3 Update r3 Wave 1 scope

Update r3 [`design.md` §2.1](../../ai-spaarke-insights-engine-r2/projects/ai-spaarke-insights-engine-r3/design.md) to reflect Option A (recommended) from §2.2 above:
- **Remove** Tier 1.1 `NullInsightsAi` facade (superseded by audit Migration PR #1)
- r3 Wave 1 total effort: ~5 days → ~**4.5 days** (saves 0.5d)
- r3 Wave 1 scope: 1.2 + 1.3 + 1.4 + 1.5 (4 items)
- Add note: "r3 Wave 1.1 superseded by `bff-ai-architecture-audit-r1` Migration PR #1 (bundled LATENT BUG remediation + 4 Null peers + structural relocation). r3 receives `NullInsightsAi` via audit PR; no separate r3 PR."

### §6.4 Reference docs to cite

In r3 design.md updates, reference:
- [`canonical-architecture-decisions.md`](canonical-architecture-decisions.md) — design authority for canonical-stack naming + per-category verdicts
- [`migration-plan.md`](migration-plan.md) — PR sequencing + effort estimates
- This document ([`r3-scope-recommendations.md`](r3-scope-recommendations.md)) — concrete r3 guidance
- [`.claude/constraints/bff-extensions.md`](.claude/constraints/bff-extensions.md) §F.1-F.3 — binding rules
- ADR-013 (Public-Contracts Facade), ADR-032 (Null-Object pattern), ADR-010 (DI module budget)

---

## §7 r3 timeline implications

### §7.1 Original estimate vs revised estimate

| Wave | Original estimate | Revised estimate | Delta |
|---|---|---|---|
| r3 Wave 1 (Tier 1) | ~5 days | ~4.5 days (after Tier 1.1 superseded by audit PR #1) | -0.5d |
| r3 Wave 2 (Tier 2.5) | ~2-3 weeks (provisional pending audit) | **~1 week (5d)** confirmed | -1.5 to -2 weeks |
| r3 Wave 3+ | TBD per design | Unchanged | — |
| **Total r3 short-term** | ~3-4 weeks | **~1.9 weeks (~9.5 days)** | **~-1.5 to -2 weeks saved** |

**The audit's primary success story for r3**: ~50-66% reduction in Wave 2 effort by leveraging existing `PlaybookDispatcher` + `spaarke-playbook-index` infrastructure. This validates the audit-trigger hypothesis (parallel infrastructure across projects existed; surfacing it saved weeks of re-build).

### §7.2 Sequencing on calendar

| Week | r3 Wave activity | Audit activity | Sequencing rationale |
|---|---|---|---|
| W0 | Phase 4 owner review | Phase 4 owner review | Both r3 + audit unblocked by same Phase 4 |
| W1-W2 | r3 Wave 1.2 + 1.3 + 1.4 + 1.5 (parallel, 4.5d) | Audit Migration PR #1 (LATENT BUG, ~2 weeks) | r3 Wave 1 parallel to audit PR #1 |
| W2-W3 | r3 Wave 1.5 complete; Wave 2 design + F-1 decision spike | Audit PR #1 lands; Migration PR #2 + PR #3 + PR #4 ship | r3 Wave 2 design starts after PR #1 lands |
| W3-W4 | r3 Wave 2 F-2 + F-3 + F-4 + F-5 (~4d) | Audit PR #5 cache adoption Sprint 1 starts | r3 Wave 2 ships ~end-W4 |
| W5+ | r3 Wave 3+ (TBD per design) | Audit PR #5 cache adoption continues; pattern docs | — |

**Total r3 calendar to Wave 2 ship**: ~4 weeks from Phase 4 owner review (vs original ~5-6 weeks).

### §7.3 Risk on r3 timeline

| Risk | Mitigation | Severity |
|---|---|---|
| Audit Migration PR #1 slips beyond ~W2 → r3 Wave 2 ship slips | r3 Wave 2 implementation work (F-2 + F-4 + F-5) can ship without PR #1; only F-3 (Insights wrapper around PlaybookDispatcher) MUST wait for PR #1 contract correctness | MEDIUM |
| F-1 decision spike yields option (c) "keep separate" — undermines reconciliation rationale | Audit verdict W2 Cat 1: REJECTED forced consolidation, but did NOT mandate non-reconciliation. F-1 spike result is binding; if (c), r3 Wave 2 scope reduces further to just F-2 + F-4 + F-5 (~2-3 days). | LOW |
| SprkChat team consultation on F-1 takes >1 day | Schedule SprkChat consultation immediately at F-1 start; allow 2-day budget | LOW |
| `spaarke-playbook-index` not yet renamed when F-3 starts | r3 Wave 1.5 sequencing must complete in Dev before Wave 2 F-3; r3 design.md §2.1.1 already documents this | LOW (mitigated) |

---

## §8 r3 → audit follow-on coordination

### §8.1 What r3 will produce that feeds audit follow-on phase

r3 Wave 2 will produce:
- **Concrete reference implementation** of Spaarke Canonical Intent Classifier Pattern applied in reconciliation context (Insights as thin wrapper around `PlaybookDispatcher`)
- **JPS prompt extraction precedent** — `InsightsIntentClassifier` prompt moved to `sprk_analysisaction.sprk_systemprompt`; provides authoritative example for ADR-CAND-G-04 ("User-managed prompt template architectural layer")
- **Playbook auto-population flow** (F-5) — `Deploy-Playbook.ps1` auto-populates `description` + `triggerPhrases`; informs audit ACTION-TYPE-REGISTRY.md authoring (Migration PR #4) on metadata-as-build-time-artifact pattern
- **Validated Endpoint↔DI Symmetry Rule application** — r3 Wave 2's adherence to the rule provides positive case study for ADR-CAND-W4-1 authoring

### §8.2 What audit follow-on phase will produce that r3 consumes

After audit Phase 4 + ADR phase (Q-005 DEFERRED, ~2-3 weeks):
- **34 ADRs authored** (HIGH 11 + MEDIUM 12 + LOW 11) — r3 Wave 2 design references ADR candidates by number; r3 implementation conforms to ADRs as they land
- **8 pattern docs** (Migration PR #7) — r3 documentation should cite Spaarke Canonical Intent Classifier Pattern doc when authored
- **Runtime §F.1 detection fixture** (Migration PR #8) — r3 Wave 2 tests can leverage this fixture for regression confidence; r3 should NOT author its own duplicate

### §8.3 Cross-project decision-record handoff

r3 should author DR-### records in [`projects/ai-spaarke-insights-engine-r3/decisions/`](../../ai-spaarke-insights-engine-r3/decisions/) covering:
- DR-001 r3 Wave 1.1 supersession by audit Migration PR #1
- DR-002 r3 Wave 2 F-1 decision spike outcome (options a/b/c)
- DR-003 r3 Wave 2 reconciliation architecture (Insights wrapper around `PlaybookDispatcher`)
- DR-004 r3 Wave 2 JPS prompt extraction approach (F-2 design)
- DR-005 r3 Wave 2 playbook auto-population pattern (F-5 design)

Each DR cites [`canonical-architecture-decisions.md`](canonical-architecture-decisions.md) + [`migration-plan.md`](migration-plan.md) sections as design authorities.

---

## §9 Footer

This document UNBLOCKS r3 Wave 2 scope-locking per Phase 3 of `bff-ai-architecture-audit-r1`. The audit's primary success for r3 is the ~1.5-2 week effort reduction (Wave 2: ~2-3 weeks → ~1 week) by validating existence of `PlaybookDispatcher` + `spaarke-playbook-index` infrastructure that r3 Wave 2 reconciles into.

**r3 owner action items**:
1. Apply §6.3 update to r3 [`design.md` §2.1](../../ai-spaarke-insights-engine-r3/design.md) — remove Tier 1.1 (superseded)
2. Apply §6.1 update to r3 [`design.md` §2.2.1](../../ai-spaarke-insights-engine-r3/design.md) — replace PROVISIONAL with LOCKED + cite audit docs
3. Apply §6.2 update to r3 design.md — add audit-compatibility commitments section
4. Author DR-### records per §8.3
5. Sequence r3 Wave 2 implementation per §7.2 (Wave 2 F-3 starts AFTER audit Migration PR #1 lands)
6. Coordinate SprkChat consultation for F-1 decision spike (~1-day consultation cycle)

**Out-of-band coordination needed**: SprkChat team (PlaybookDispatcher owner), Insights team (audit Migration PR #1 + PR #8 + r3 Wave 2 ownership overlap).

*r3 scope recommendations authored 2026-06-04 by Phase 3 Sub-Agent J synthesizing canonical-architecture-decisions.md (Sub-Agent I) + r3 project design files + recovery memory + 4 wave summaries.*
