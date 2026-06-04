# Phase 2 Wave 3 Summary — Cat 5 Prompts Sub-Agent Analysis

> **Authored by**: Main session (aggregation after W3 Cat 5 sub-agent completed)
> **Pinned to**: commit `357e6936` (Phase 1 inventory snapshot)
> **HEAD at aggregation time**: `3abbe918` (W1+W2 merged via PR #344)
> **Date**: 2026-06-04
> **Source documents**:
> - [`analysis-prompts.md`](analysis-prompts.md) — Cat 5 (Sub-Agent G)
> - W1 baseline: `wave-1-summary.md` + 4 W1 analysis docs
> - W2 baseline: `wave-2-summary.md` + 2 W2 analysis docs

---

## §1 Cat 5 distillation

### §1.1 Cat 5 — Prompt builders (Sub-Agent G)

**Headline**: The inventory's Category-5 narrative is materially incomplete. Empirical reading at HEAD discovers:
- **5 explicit prompt sources** (not 3 as inventory claimed) — adds `AnalysisContextBuilder` (DI Scoped) + `FallbackPrompts` (static).
- **5th orphan**: `BuildPlanGenerationService.cs` (~530 LOC, not in inventory §6.2's list of 4).
- **W2 Cat 1's cascade DELETE estimate was wrong by 10×**: ~100 LOC → **~1280 LOC** actual. `PlaybookBuilderSystemPrompt.cs` is ~80% dead and ~20% alive — whole-file DELETE is INCORRECT because `BuilderAgentService.Build()` consumes `PlaybookBuilderSystemPrompt.Build(actions, skills, knowledge)` (lines 729-927, ~200 LOC live production code).
- **PromptLibrary is mis-framed by inventory** — it's a user-facing CRUD facade for end-user-managed templates (Cosmos + Dataverse tiers per AIPU2-035/036), NOT an LLM-call-site abstraction. ZERO non-endpoint consumers.
- **Inline-prompt count overstated**: 7 inventory claims → 3-5 empirically (after removing mislabels and self-documented time-boxed sites).

**Decision shape**: 6 KEEP-as-is + 1 KEEP-with-EXTRACT (the `Build()` method) + 2 DELETE (dead members + `BuildPlanGenerationService` orphan) + 0 CONSOLIDATE-via-interface + 3 KEEP-inline + 1 KEEP-time-boxed-inline.

**Consolidation verdict — REJECT generic `IPromptComposer`**: Same multi-axis empirical NO as Cat 1 and Cat 3 — input shapes domain-specific, output shapes diverge (`IList<ChatMessage>` vs `OrchestratorPrompt` record vs `string`), token-budget enforcement per-builder (≤600 vs 9000 with rebalancing vs none), DI lifetimes differ (static vs Singleton vs Scoped), stable-prefix caching only in Orchestrator. **Pattern-doc canonicalization recommended.**

**Canonical reference impls designated**:
- `OrchestratorPromptBuilder` (Singleton, two-layer cached + ADR-009 exception documented — Cat 5 confirms Cat 4 designation as gold-standard for ADR conformance).
- `CapabilityClassificationPromptBuilder` (static-class compact builder with ≤600 token target).

**Recommended path forward for `PlaybookBuilderSystemPrompt.cs`** (W2 Cat 1's bundled DELETE PR MUST be adjusted):
- **Option B (EXTRACT-THEN-DELETE — RECOMMENDED)**: Create `Services/Ai/Builder/BuilderAgentSystemPrompt.cs` with the live `Build(...)` method (~200 LOC). Update `BuilderAgentService.cs:270` reference. DELETE entire `Services/Ai/Prompts/PlaybookBuilderSystemPrompt.cs` (~969 LOC). DELETE empty `Services/Ai/Prompts/` directory.
- Plus DELETE `BuildPlanGenerationService.cs` (~530 LOC NEW orphan).

---

## §2 Cross-cutting findings spanning W3 + W2 + W1

### §2.1 W3 reinforces "REJECT forced consolidation" as the dominant audit verdict

All three independent W2+W3 sub-agents (Cat 1, Cat 3, Cat 5) reached the same conclusion via independent empirical analysis: **forced abstractions behind generic interfaces are NOT appropriate for these architecturally distinct categories**. The "Spaarke Canonical AI Stack" framing (Q-004 lock) should be **descriptive pattern documentation, NOT binding interface abstractions** across all three categories.

**Canonical reference impls designated by W2+W3**:
- Cat 1: `InsightsIntentClassifier` (intent classifier pattern)
- Cat 3: `RagService`/`NullRagService` (search-substrate pattern + ADR-032 double-gate sub-mechanism)
- Cat 5: `OrchestratorPromptBuilder` + `CapabilityClassificationPromptBuilder` (two-layer cached prompt + compact prompt patterns)

**Combined**: 4 canonical reference implementations now designated for the "Spaarke Canonical AI Stack" pattern documentation phase.

### §2.2 W3 corrects W2 substantially — cross-sub-agent validation worked

W3 Sub-Agent G identified **3 significant errors in W2 Cat 1 §3.1 + wave-2-summary**:
- **Whole-file DELETE of `PlaybookBuilderSystemPrompt.cs` is impossible** — `BuilderAgentService.Build()` consumes the live `Build(actions, skills, knowledge)` method. Option B (EXTRACT-THEN-DELETE) is the correct path.
- **Cascade DELETE scope is ~1280 LOC, not ~100 LOC** — 13× larger than W2 Cat 1 estimated.
- **NEW 5th orphan** (`BuildPlanGenerationService.cs`, ~530 LOC) — missed by both inventory §6.2 and W2 Cat 1's cascade audit.

This is the **second instance** of W2→W3 cross-validation finding W2 errors. W2 Cat 1 already corrected 2 W1 Sub-Agent A false positives. The methodology of having later sub-agents read prior outputs as peer context is paying dividends.

**Combined bundled orphan-DELETE PR scope** grows substantially:
- W1 Cat 2 lookup orphans (3 files + DI cleanup + dangling cref): ~714 LOC
- W2 Cat 1 intent classifier orphan cascade (corrected): ~1280 LOC (`IntentClassificationService` + `PlaybookBuilderSystemPrompt` pruning + `ClarificationService` + `BuildPlanGenerationService` NEW orphan)
- Total: **~2000 LOC** in single bundled DELETE PR (vs W2's ~1700-1800 LOC estimate).

If Option B is selected (RECOMMENDED), additional impact:
- Extract live `Build()` method (~200 LOC) to new file → no LOC change
- Delete `Services/Ai/Prompts/` empty directory
- Total file delta: **3 file deletes + 1 file create + 1 reference update + DI line removals + cref cleanup**.

### §2.3 PromptLibrary reframe is a significant inventory correction

The inventory §2.5.4 line "PromptLibrary [...] limited adoption — most LLM-calling services do NOT route through it" is **architecturally misleading**. Cat 5 §4.3 establishes:
- PromptLibrary is the BACKING SERVICE for an end-user-managed template store.
- Uses `{{variableName}}` Mustache substitution (not LLM-message-list assembly).
- Authorization is per-user/per-team/per-tenant; LLM-call sites have service-principal context.
- ZERO non-endpoint consumers — exactly as designed.

**Reframe**: PromptLibrary is in the "user-managed-template" architectural layer, NOT the "LLM-call-site" layer. The "limited adoption" inventory framing should be replaced with a clear architectural-layer distinction.

### §2.4 Cat 5 surfaces TWO inventory misses + ONE reframe

1. `AnalysisContextBuilder` (`IAnalysisContextBuilder` Scoped) — 4th explicit builder; inventory §2.5 missed it.
2. `FallbackPrompts.cs` — 5th source (static fallback constants); inventory §2.5 missed it.
3. PromptLibrary architectural-layer reframe (§2.3 above).

### §2.5 W3 reinforces ADR-009 documentation convention as binding precedent

Cat 5 §4.2 confirms Cat 4 §4.3's finding: **`OrchestratorPromptBuilder.cs:36-44` is the gold-standard "ADR-009 exception" XML doc convention**. This is THE binding precedent for any future BFF service that needs in-process caching of structural/derived metadata.

Cat 5 ADR-CAND-G-02 recommends codifying this XML doc convention. Cross-coordinates with Cat 4's ADR-CAND-A-01 "BFF Canonical Cache Stack."

### §2.6 ZERO code drift across all W3 sub-agents (continues W1+W2 pattern)

Cat 5 independently verified `git log --oneline 357e6936..HEAD -- [5 Cat 5 paths]` returns EMPTY. The W1+W2 commit (PR #344) on master is docs-only. Inventory `357e6936` remains fully valid at HEAD `3abbe918` for Cat 5 surfaces.

### §2.7 Cat 7 deferred re-dispatch + Cat 6 cross-references — NOT TRIGGERED by W3

Cat 5 §8 explicitly confirms: no additional Cat 7 (node executors) or Cat 6 (public contracts) re-dispatch triggered by Cat 5. The W1+W2 baseline analyses for those categories remain valid.

---

## §3 Decision distribution roll-up (W3 totals)

| Source | KEEP-as-is | KEEP-with-EXTRACT | KEEP-inline | KEEP-time-boxed-inline | DELETE | CONSOLIDATE | REJECT-interface |
|---|---|---|---|---|---|---|---|
| Cat 5 (Prompts) | 6 | 1 | 3 | 1 | 2 | 0 | 1 (verdict) |

**W3 unique action items synthesized**:
1. **EXTRACT then DELETE** (Option B): create `Services/Ai/Builder/BuilderAgentSystemPrompt.cs` with live `Build()` method; delete `Services/Ai/Prompts/PlaybookBuilderSystemPrompt.cs` + empty directory.
2. **DELETE 5th orphan**: `BuildPlanGenerationService.cs` (~530 LOC).
3. **Inventory corrections** (3 items): add `AnalysisContextBuilder` to §2.5; add `FallbackPrompts.cs` to §2.5; reframe PromptLibrary §2.5.4 paragraph.
4. **Designate canonical reference impls**: `OrchestratorPromptBuilder` + `CapabilityClassificationPromptBuilder`.
5. **Pattern doc**: "Spaarke Canonical Prompt Construction Pattern" (NOT interface).
6. **Time-boxed inline trigger**: `InsightsIntentClassifier.BuildPrompt()` extraction trigger (Phase 2 multi-playbook; Insights team owns).

**W1+W2+W3 combined unique action items**:

| Action | LOC impact | Bundling opportunity | Source |
|---|---|---|---|
| **Bundled orphan DELETE PR** (lookups + intent cascade + Build* orphan + Prompts dir cleanup) | **~2000 LOC** (corrected from W2 estimate ~1700-1800) | Single PR | W1 Cat 2 + W2 Cat 1 + W3 Cat 5 |
| ADD 4 Null-peer facades + DI rewire + cleanup | ~180 LOC | Standalone PR | W1 Cat 6 |
| Cat 4 cache adoption migration | 26 sites | Phased per-team | W1 Cat 4 + W2 expansions |
| Pattern docs (3 canonical patterns: Cache, Intent, Search, Prompts) | ~600-800 LOC docs | Phase 3 deliverable | W1+W2+W3 |
| Doc bug fix + inventory corrections | minor | Trivial PRs | Multiple |
| Security adjudication cycle | depends | Owner-driven | W1 Cat 4 + W2 Cat 3 |
| Author `ACTION-TYPE-REGISTRY.md` | ~100-200 LOC | Standalone | W1 Cat 7 |
| **NEW from W3**: Reframe inventory §2.5 PromptLibrary paragraph | minor | Inventory correction | W3 Cat 5 |
| **NEW from W3**: Add `AnalysisContextBuilder` + `FallbackPrompts` to inventory §2.5 | minor | Inventory correction | W3 Cat 5 |

---

## §4 HIGH-URGENCY findings carried forward + new

### §4.1 HIGH-URGENCY from W1 (unchanged)
- **LATENT BUG**: `IInsightsAi` 500 instead of 503 under compound-AI-OFF. Remediation: add `NullInsightsAi` + DI rewire + integration test. **W2 Cat 1 + Cat 3 + W3 Cat 5 verifications all CONFIRM** the bug is at the `InsightsOrchestrator` facade wrapping layer, NOT downstream in classifier, search, or prompt layers.

### §4.2 NEW HIGH-URGENCY from W3 Cat 5
- **W2 Cat 1's cascade DELETE was INCORRECT** — must be adjusted before bundled orphan DELETE PR opens. Pick Option A (PRUNE-IN-PLACE) or Option B (EXTRACT-THEN-DELETE — recommended). Without this adjustment, the bundled PR would fail with compile errors on `BuilderAgentService.cs:270`.

### §4.3 MEDIUM from W3
- **NEW 5th orphan**: `BuildPlanGenerationService.cs` (~530 LOC). AI Chat Playbook Builder team confirmation needed before DELETE.
- **PromptLibrary inventory reframe** — current framing is architecturally misleading.

---

## §5 Drift summary (snapshot 357e6936 → HEAD 3abbe918)

| Category | Code drift (`Services/Ai/`) | Inventory accuracy | Consumer-side drift |
|---|---|---|---|
| Cat 5 (Prompts) | ZERO | 5 corrections: explicit-builder count 3→5; cascade scope ~100→~1280 LOC; NEW 5th orphan; inline count 7→3-5; PromptLibrary reframe | None |

**Aggregate verdict**: Phase 1 inventory remains authoritative for the snapshot at `357e6936`, BUT W3 reveals that the Category-5 narrative has material accuracy gaps (in addition to the minor numeric corrections W2 surfaced for Cat 1 + Cat 3). Inventory should be annotated with W3 corrections before Phase 3.

---

## §6 W4 dispatch recommendations

### §6.1 W4 scope (DI + Configuration — FINAL wave per Q-001)

W4 is the LAST wave of Phase 2 per locked priority order. It depends on ALL prior analyses:
- **W1 Cat 4 (Cache patterns)**: DI registration patterns surfaced; `EmbeddingCache` canonical reference designated
- **W1 Cat 6 (Public Contracts)**: facade DI fascia (`AddPublicContractsFacade` + `AddNullObjectsForCompoundOff` + `AddInsightsFacadeModule`)
- **W1 Cat 7 (Node executors)**: `INodeExecutor` registry pattern; need for `ACTION-TYPE-REGISTRY.md`
- **W2 Cat 1 (Intent)**: `AnalysisServicesModule.cs:372` orphan DI line removal; `IntentClassificationService` orphan + cascade
- **W2 Cat 3 (Search)**: `Configuration/` vs `Options/` namespace split; inconsistent `IOptions<T>` adoption; double-gate Null-Object pattern
- **W3 Cat 5 (Prompts)**: `AnalysisServicesModule.cs:282` (`IAnalysisContextBuilder` Scoped) inventory miss to add to W4; `OrchestratorPromptBuilder` Singleton confirmed gold-standard

**All preconditions MET.** W4 can dispatch immediately following W3 PR merge.

### §6.2 W4 inputs from W1+W2+W3

| Input | Source | W4 implication |
|---|---|---|
| 34 DI modules with 6 directly AI-flavored | Inventory §3.1 | Consolidate or per-concern split good practice? |
| `Configuration/` vs `Options/` namespace split — redundant with no clear delineator | Inventory §4.3 + W2 Cat 3 §4.8 | Recommend consolidation |
| 35 options classes across BFF | Inventory §4.1 | Pattern for "feature manifest" collapse? |
| Compound AI gate complexity (3-way triad in `AnalysisServicesModule`) | Inventory §7.8 | Evaluate ADR-032 Null-Object + per-service fine-grained gates as replacement |
| §F.1 anti-pattern coverage gap (4/5 facades) | W1 Cat 6 | LATENT BUG remediation roadmap |
| Endpoint mapping ↔ DI registration symmetry rule | W2 Cat 3 §2.4 | ADR-CAND-F-4 generalizes Cat 6's specific instance |
| Canonical `DistributedCacheExtensions.GetOrCreateAsync<T>` 0% adoption | W1 Cat 4 + W2 expansions | Promote opt-in → MUST |
| `INodeExecutor` registry + ActionType central enum | W1 Cat 7 | Working pattern; `ACTION-TYPE-REGISTRY.md` doc needed |
| Orphan DI line removal: `AnalysisServicesModule.cs:372` | W2 Cat 1 | Bundle with orphan DELETE PR |
| Inventory miss: `AnalysisServicesModule.cs:282` (`IAnalysisContextBuilder` Scoped) | W3 Cat 5 | Add to W4 inventory |
| `AiCapabilitiesModule.cs:102-104` (`OrchestratorPromptBuilder` Singleton) gold-standard | W3 Cat 5 | Reference impl for W4 |

### §6.3 W4 sub-agent brief MUST include

- Self-contained brief with verbatim quotes from inventory §3 + §4
- Read ALL 7 prior analysis docs (4 W1 + 2 W2 + 1 W3) as peer context
- Explicit OUT-OF-SCOPE: do NOT re-litigate per-category decisions (W1+W2+W3 outputs are locked)
- HARD GATE on any DI/Configuration change recommendations
- **Apply W2+W3 pattern-doc framing**: REJECT forced consolidations; recommend descriptive pattern docs
- Empirical-reproduction-FIRST rule
- Harness write-block acknowledgment
- Output: `analysis-di-configuration.md`
- Effort budget: ~2-3 days (cross-cutting; touches everything)

### §6.4 Cat 7 deferred re-dispatch — NOT TRIGGERED

W3 Cat 5 confirms: no additional Cat 7 (node executors) re-dispatch triggered. W1 Sub-Agent D's analysis remains valid in full.

---

## §7 Packaged for end-of-audit owner review (Q-002 — additions to W1+W2 summaries §7)

The following W3 questions augment W1+W2's packaged-for-owner-review list. All packaged for SINGLE end-of-audit review per Q-002.

### §7.1 Confirm DELETE adjustments

1. **(Cat 5) Confirm 5th orphan `BuildPlanGenerationService.cs`** (~530 LOC). AI Chat Playbook Builder team confirmation needed.
2. **(Cat 5) Adjust W2 Cat 1's `PlaybookBuilderSystemPrompt.cs` cascade DELETE scope** from "whole-file" to corrected ~1280 LOC scope. Pick Option A (PRUNE-IN-PLACE) or Option B (EXTRACT-THEN-DELETE — recommended).

### §7.2 Inventory corrections to apply

3. **(Cat 5) Add `AnalysisContextBuilder` to inventory §2.5** as 4th explicit builder.
4. **(Cat 5) Add `FallbackPrompts.cs` to inventory §2.5** as 5th source.
5. **(Cat 5) Reframe inventory §2.5 PromptLibrary paragraph** — user-CRUD layer, not LLM-call-site layer.
6. **(Cat 5) Correct inventory §2.5.4 inline-prompt count** (7 → 3-5).

### §7.3 Designate canonical reference impls

7. **(Cat 5) `OrchestratorPromptBuilder` as canonical for two-layer cached prompts + ADR-009 exception convention** (confirms Cat 4 designation).
8. **(Cat 5) `CapabilityClassificationPromptBuilder` as canonical for compact single-call prompts**.

### §7.4 Pattern doc adoption

9. **(Cat 5) "Spaarke Canonical Prompt Construction Pattern"** — pattern doc, NOT interface. §5.1 Candidate A.

### §7.5 Low priority

10. **(Cat 5) Delete empty `Services/Ai/Prompts/` directory** after Option B.
11. **(Cat 5) Time-boxed inline `InsightsIntentClassifier.BuildPrompt()` extraction trigger** — Phase 2 multi-playbook (Insights team owns trigger).

### §7.6 ADR candidates (W3 additions to W1+W2's running total)

| # | ADR candidate | Source | Priority |
|---|---|---|---|
| W3-1 | **Spaarke Canonical Prompt Construction Pattern** — 4-element pattern doc, NOT binding interface | Cat 5 ADR-CAND-G-01 | HIGH |
| W3-2 | **In-process MemoryCache XML-doc convention for ADR-009 exceptions** | Cat 5 ADR-CAND-G-02 (cross-coordinate with Cat 4) | MEDIUM |
| W3-3 | **Prompt source co-location rule** — co-locate with sole consumer; forbid generic `/Prompts/` dirs | Cat 5 ADR-CAND-G-03 | MEDIUM |
| W3-4 | **User-managed prompt template architectural layer** — codifies PromptLibrary reframe | Cat 5 ADR-CAND-G-04 | LOW |
| W3-5 | **Time-boxed inline prompts MUST document extraction trigger** | Cat 5 ADR-CAND-G-05 | LOW |

**Total ADR candidates (W1+W2+W3)**: 14 (W1) + 8 (W2) + 5 (W3) = **27 ADR candidates surfaced** as bullet items for the follow-on ADR phase.

---

## §8 Effort + sequencing roll-up update

Updates to W1+W2 summary §8 effort buckets (W3 additions only):

| Bucket | Effort | Cross-team needs | Recommended timing |
|---|---|---|---|
| **Bundled orphan DELETE PR (W1+W2+W3 corrected)** | **~2000 LOC** (vs W2 estimate ~1700-1800) | Finance Intelligence + AI Chat Playbook Builder team confirmations | Immediate after owner sign-off; ungated by other waves |
| **Pattern doc authoring** (Cat 1 + Cat 3 + Cat 5 canonical patterns) | ~1-1.5 weeks (added Cat 5) | None | Phase 3 deliverable |
| **Inventory corrections** (3 Cat 5 misses + W2 numeric corrections) | XS | None | Trivial PR; bundle with bundled DELETE PR |

**W3 adds ~3-5 days to total downstream migration footprint, mostly the inventory corrections + pattern doc additions.**

---

## §9 Status + handoff

- **W3 status**: COMPLETE (1/1 sub-agent finished — Cat 5; analysis doc persisted; aggregation summary authored)
- **Cat 7 deferred re-dispatch**: NOT TRIGGERED by W3 (per Cat 5 §8)
- **W4 precondition (DI + Configuration)**: MET — all 7 prior analysis docs available
- **Next step**: Main session commits + opens PR with auto-merge for `notes/phase2/analysis-prompts.md` + `wave-3-summary.md` + `current-task.md` update. Branch: `work/audit-r1-phase2-wave3` off master.
- **W4 dispatch**: defer until W3 PR merges (clean baseline for W4 sub-agent to read 7 peer outputs).
- **Owner consultation**: not required mid-wave; Q-002 single end-of-audit review still applies.

---

*W3 summary authored 2026-06-04 by main session from the 1 W3 sub-agent analysis. Sub-agent attribution preserved; recommendations are aggregated, not re-interpreted.*
