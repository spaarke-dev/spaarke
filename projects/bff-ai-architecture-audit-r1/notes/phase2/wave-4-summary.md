# Phase 2 Wave 4 Summary + PHASE 2 COMPLETION REPORT

> **Authored by**: Main session (final Phase 2 aggregation)
> **Pinned to**: commit `357e6936` (Phase 1 inventory snapshot)
> **HEAD at aggregation time**: master + W1+W2+W3 merged
> **Date**: 2026-06-04
> **Source documents**:
> - [`analysis-di-configuration.md`](analysis-di-configuration.md) — W4 (Sub-Agent H)
> - W1 baseline: `wave-1-summary.md` + 4 W1 analysis docs
> - W2 baseline: `wave-2-summary.md` + 2 W2 analysis docs
> - W3 baseline: `wave-3-summary.md` + 1 W3 analysis doc

---

## §1 W4 distillation — DI + Configuration Patterns (Sub-Agent H)

**Headline**: The W4 sub-agent confirmed the LATENT BUG structural root cause and proposed concrete remediation (Option A) with a generalized structural rule that closes the §F.1 anti-pattern at the architectural level. All "consolidate vs split" questions land on REJECT-consolidation (consistent with W2 Cat 1, W2 Cat 3, W3 Cat 5 verdicts) — the per-concern split is correct.

**Decision shape**:
- **KEEP per-concern** (REJECT forced consolidation): 31 modules, 35 options classes, compound AI gate, ADR-010 cap mechanism
- **CONSOLIDATE**: 1 (trivial `Options/` → `Configuration/` directory `git mv`)
- **RESTRUCTURE (HIGH PRIORITY)**: 1 (`IInsightsAi` LATENT BUG remediation — Option A)
- **RECOMMEND-RULE**: 1 (Endpoint↔DI Registration Conditionality Symmetry Rule)
- **RECOMMEND-TEST**: 1 (Runtime §F.1 detection fixture)
- **RECOMMEND-PATTERN-DOC**: 6

**Inventory corrections surfaced by W4**:
- Module count **34 → 31** (inventory §3.1 overcounted by 3)
- **`Sprk.Bff.Api.Options` namespace does NOT exist** — `Options/` directory uses `Sprk.Bff.Api.Configuration` namespace (inventory §4.3 framing error)
- `Configuration/` per-dir count **25 → 21** (inventory §4.1 per-dir error)
- 35 options classes total IS correct in inventory headline

**LATENT BUG remediation pattern (Option A — recommended)**:
1. Move `IInsightsAi` + `IPlaybookExecutionEngine` registration from `InsightsFacadeModule` to `AnalysisServicesModule.AddPublicContractsFacade` (symmetric with other 4 facades)
2. Add `NullInsightsAi` impl (~100 LOC, 6 methods throwing `FeatureDisabledException`)
3. Register `NullInsightsAi` in `AddNullObjectsForCompoundOff`
4. Fix misleading comment at `AnalysisServicesModule.cs:75-79`
5. Add integration test asserting 503 (not 500) from 3 unconditionally-mapped Insights endpoints under compound-OFF

**Generalized Endpoint↔DI Symmetry Rule** (W4 §4.1): Any service registered behind a feature flag MUST either be consumed by a similarly-gated endpoint OR have a Null peer with EXACTLY matching interface/lifetime/ServiceType AND all transitive ctor deps must satisfy the rule recursively. **This generalizes Cat 6 §F.1 into a binding architectural constraint that, if enforced via runtime test, would have caught the LATENT BUG at PR-review time.**

---

## §2 PHASE 2 COMPLETION REPORT — Cross-cutting findings across all 4 waves

### §2.1 The "REJECT forced consolidation" verdict is UNIVERSAL across Phase 2

| Wave | Category | Consolidation verdict |
|---|---|---|
| W1 Cat 2 | Lookup services | KEEP 1 + DELETE 3 orphans; explicitly REJECT generic `ILookupService<T>` |
| W1 Cat 4 | Cache patterns | KEEP 2 canonical specialists + adopt existing helper across 30 sites |
| W1 Cat 6 | Public Contracts facades | KEEP 5 facades; add 4 Null peers (NOT consolidate facades) |
| W1 Cat 7 | Node executors | KEEP all 18; ActionType enum IS the central registry (no new abstraction needed) |
| W2 Cat 1 | Intent classifiers | REJECT generic `IIntentClassifier<TResult>` |
| W2 Cat 3 | Search services | REJECT `PlaybookEmbeddingService` ↔ `SemanticSearchService` merger; KEEP all 4 substrates |
| W3 Cat 5 | Prompt builders | REJECT generic `IPromptComposer` |
| W4 | DI + Configuration | REJECT module/options/compound-gate consolidation |

**All 8 categories** independently arrived at the same conclusion: **forced abstractions behind generic interfaces are NOT appropriate for these architecturally distinct domains**. The "Spaarke Canonical AI Stack" framing (Q-004 lock) MUST be **descriptive pattern documentation, NOT binding interface abstractions**.

### §2.2 Canonical reference implementations designated (Phase 2 → Phase 3 handoff)

Phase 3 (Canonical Stack naming + migration plan) will codify these designations as the load-bearing reference impls:

| Domain | Canonical reference impl | Source |
|---|---|---|
| Cache | `EmbeddingCache` (binary specialist) + `DistributedCacheExtensions.GetOrCreateAsync<T>` (generic helper) | W1 Cat 4 |
| Intent classifier | `InsightsIntentClassifier` | W2 Cat 1 |
| Search substrate (knowledge) | `RagService` + `NullRagService` (also gold-standard for ADR-032 double-gate) | W2 Cat 3 |
| Prompt — two-layer cached | `OrchestratorPromptBuilder` (also gold-standard for ADR-009 exception XML doc) | W3 Cat 5 |
| Prompt — compact single-call | `CapabilityClassificationPromptBuilder` | W3 Cat 5 |
| DI module audit pattern | `AiModule.cs:269-313` audit table | W4 |
| New W4 patterns | "Spaarke Public-Contracts Facade DI Fascia" + "Spaarke Endpoint↔DI Symmetry Rule" | W4 |

### §2.3 Cross-sub-agent validation discovered errors at every wave

The methodology of having later sub-agents read prior outputs as peer context paid dividends throughout Phase 2:

| Validation | Source | Result |
|---|---|---|
| W2 Cat 1 reviewing W1 Cat 4 | W2 Cat 1 §2.5 | **REJECTED** W1's `NullInsightsIntentClassifier` cache-dep anti-smell claim (ctor takes only `ILogger<T>`) |
| W2 Cat 1 reviewing inventory §2.1 | W2 Cat 1 §2.4 | Discovered 3 (not 2) `IntentClassification*` types |
| W2 Cat 1 reviewing W1 + inventory | W2 Cat 1 §2.3.3 | **REJECTED** the "AiPlaybookBuilderService at-risk" claim |
| W2 Cat 3 reviewing inventory | W2 Cat 3 §4.4 | Inventory mislabel: `DocumentClassifierHandler` uses `IRagService`, not `ISemanticSearchService`; `AiAnalysisNodeExecutor` is `IRecordSearchService` consumer |
| W3 Cat 5 reviewing W2 Cat 1 | W3 Cat 5 §2.3 | **W2 Cat 1's cascade DELETE estimate wrong by 10×** — whole-file `PlaybookBuilderSystemPrompt.cs` DELETE impossible because `BuilderAgentService.Build()` is a live consumer; cascade scope ~100 → ~1280 LOC |
| W3 Cat 5 reviewing inventory | W3 Cat 5 §2.1, §2.5 | Inventory missed `AnalysisContextBuilder` + `FallbackPrompts`; mis-framed `PromptLibrary` as "limited adoption" instead of "user-facing CRUD layer"; **NEW 5th orphan**: `BuildPlanGenerationService.cs` (~530 LOC) |
| W4 reviewing inventory | W4 §2.2, §2.3 | Module count **34 → 31**; `Sprk.Bff.Api.Options` namespace does NOT exist; `Configuration/` per-dir count **25 → 21** |
| W4 reviewing Cat 6 LATENT BUG | W4 §4.5 | Independently verified at HEAD; structural remediation pattern (Option A) proposed |

**Total**: 7+ inventory errors corrected; 3+ prior-sub-agent claims corrected. **Cross-sub-agent validation worked as designed.**

### §2.4 LATENT BUG verification convergence across W1+W2+W3+W4

The `IInsightsAi` LATENT BUG was surfaced by W1 Cat 6 and progressively verified:

| Wave | Layer verified | Verdict |
|---|---|---|
| W1 Cat 6 §4.1 | Facade DI layer | LATENT BUG present: `IInsightsAi` registered unconditionally; ctor depends on conditional `IOpenAiClient` + `IAiPlaybookBuilderService` |
| W2 Cat 1 §4.2 | Classifier layer | LATENT BUG NOT in `InsightsIntentClassifier` layer (DI is correct); bug is upstream |
| W2 Cat 3 §2.5 | Search layer (NullRagService) | LATENT BUG NOT in `IRagService` layer (`NullRagService` works correctly); bug fires upstream at `InsightsOrchestrator` ctor before reaching `_ragService.SearchAsync` |
| W3 Cat 5 | Prompt layer | LATENT BUG NOT in prompt construction layer |
| W4 §4.5 | DI registration topology | **CONFIRMED at HEAD**; Option A remediation proposed (move + add Null peer) |

**All 5 layer-specific verifications converge**: the bug is at the facade-registration topology layer, NOT in any of the downstream service layers. **Option A remediation is the surgical fix.**

### §2.5 ZERO code drift across all 4 waves (snapshot 357e6936 → HEAD)

Every sub-agent independently confirmed `git diff --stat 357e6936..HEAD -- [their scope]` returns EMPTY. The 8 commits between snapshot and HEAD are docs-only:
- PR #341 (audit init)
- PR #342 (r3 scaffold paused)
- PR #340 (r2 wrap-up)
- PR #343 (Phase 1 inventory)
- PR #344 (Phase 2 W1+W2)
- PR #346 (Phase 2 W3)

**Inventory `357e6936` remains fully authoritative for the snapshot.** Inventory drift findings (W4 + others) are inventory accuracy corrections, NOT code drift.

---

## §3 Phase 2 final decision distribution roll-up

| Wave | Category | KEEP | KEEP-with-CAVEAT | CONSOLIDATE | DELETE | ADD-NULL-PEER | RESTRUCTURE | RECOMMEND-RULE/TEST/PATTERN |
|---|---|---|---|---|---|---|---|---|
| W1 Cat 2 | Lookup | 1 | — | 0 (rejected) | 3 | — | — | — |
| W1 Cat 4 | Cache | 5 | 1 (PrivilegeGroupResolver pending) | 21 (adopt canonical) | 3 (orphans, same as Cat 2) | — | — | 1 (Cache canonical) |
| W1 Cat 6 | Public Contracts | 2 | 1 (consumer cleanup) | 0 (deferred to W2) | 0 | 4 (`NullInvoiceAi`, `NullWorkspacePrefillAi`, `NullRecordMatchingAi`, `NullInsightsAi`) | — | — |
| W1 Cat 7 | Node executors | 15 | 3 | 0 | 0 | 0 | — | 1 (`ACTION-TYPE-REGISTRY.md`) |
| W2 Cat 1 | Intent | 3 classifiers + 1 type = 4 | — | 0 (rejected) | 1 classifier + 1 type + 2 cascade = 4 (corrected by W3) | — | — | 1 (Intent pattern) |
| W2 Cat 3 | Search | 4 | 1 (RecordSearch security pending) | 0 (rejected) | 0 | 0 (3 explicit DO-NOT-ADD per §F.1 symmetry) | — | 1 (Search substrates pattern) |
| W3 Cat 5 | Prompts | 6 | 1 (Build extraction) | 0 (rejected) | 2 (`PlaybookBuilderSystemPrompt` 80%-dead + `BuildPlanGenerationService` NEW orphan) | — | — | 1 (Prompt pattern) |
| W4 | DI + Config | 31 modules + 35 options + gate + cap | — | 1 (`Options/` → `Configuration/`) | 0 | 1 (`NullInsightsAi` — LATENT BUG fix) | 1 (`IInsightsAi` migration) | 2 (Symmetry rule + Runtime test) + 6 (pattern docs) |

**Phase 2 unique action items synthesized**:

| Action | LOC impact | Bundling | Source |
|---|---|---|---|
| **Bundled orphan DELETE PR (corrected)** | ~2000 LOC | Single PR | W1 Cat 2 + W2 Cat 1 + W3 Cat 5 |
| **LATENT BUG remediation PR (Option A)** | ~100 LOC `NullInsightsAi` + DI rewire + 3 endpoint try/catch + integration test | Single PR | W1 Cat 6 + W4 |
| **Bundled 4-facade Null-peer PR** (NullInvoiceAi, NullWorkspacePrefillAi, NullRecordMatchingAi) | ~80 LOC | Bundle with LATENT BUG PR | W1 Cat 6 |
| **Cache adoption migration** | 26+ sites | Phased per-team | W1 Cat 4 + W2 Cat 1+3 |
| **`Options/` → `Configuration/` directory consolidation** | 2 file moves | Trivial PR; bundle with inventory-correction PR | W4 |
| **Pattern docs** (8 canonical patterns: Cache, Intent, Search, Prompts, DI Module, Options, Compound Gate, Symmetry Rule) | ~1000-1500 LOC docs | Phase 3 deliverable | All waves |
| **Runtime §F.1 detection fixture** | 1-2 days fixture + tests | Insights team owns | W4-2 |
| **`ACTION-TYPE-REGISTRY.md`** | ~100-200 LOC doc | Standalone | W1 Cat 7 |
| **Inventory corrections PR** (15+ findings) | minor doc edits | Single PR | All waves |

---

## §4 ADR candidates roll-up (Q-005 DEFERRED — bullets only)

| Wave | Count | Highest-priority candidates |
|---|---|---|
| W1 | 14 | Cache Stack, Facade Null-Peer Mandate, Defensive-Nullable Prohibited, Zone B Symmetry, ActionType Registry, Runtime Kill-Switch Pattern |
| W2 | 8 | Intent Classifier Pattern, Search Substrate Architecture, DI Double-Gate Null-Object Pattern, Endpoint↔DI Symmetry Rule (formal), Security Model Matrix |
| W3 | 5 | Prompt Construction Pattern, Prompt Co-location Rule, User-Managed Template Layer |
| W4 | 7 | **Endpoint↔DI Symmetry Rule (FORMAL)**, **§F.1 Runtime-Verifiable Detection**, DI Module Audit Convention, Per-Concern Composition, Canonical Options Design, Compound Gate Pattern, Directory Consolidation |
| **TOTAL** | **34** | — |

**Phase 3 will determine which ADR candidates are folded into the Canonical Stack naming + migration plan vs deferred to the follow-on ADR phase (Q-005).**

---

## §5 Phase 2 → Phase 3 handoff

### §5.1 Phase 3 scope per audit design.md §3.1
- Per-category canonical-architecture decisions (Phase 2 produced these)
- Owner review / consolidation
- Migration plan with effort estimates

### §5.2 Phase 3 inputs from W1+W2+W3+W4 (all complete)
| Input | Source | Phase 3 use |
|---|---|---|
| 8 analysis docs + 4 wave summaries | All Phase 2 outputs | Source material |
| 4 canonical reference impls | W1 Cat 4 + W2 Cat 1 + W2 Cat 3 + W3 Cat 5 | "Spaarke Canonical AI Stack" naming |
| 2 new W4 canonical patterns | W4 §4.1, §4.2 | "Spaarke Public-Contracts Facade DI Fascia" + "Spaarke Endpoint↔DI Symmetry Rule" |
| 6 pattern doc candidates | W1+W2+W3+W4 | Phase 3 deliverable scope |
| 34 ADR candidates | All waves | Follow-on ADR phase (Q-005 DEFERRED) |
| Bundled orphan DELETE PR scope (~2000 LOC) | W1+W2+W3 + W4 verification | Migration plan PR #1 |
| Bundled facade Null-peer PR scope (~180 LOC including LATENT BUG fix) | W1 Cat 6 + W4 | Migration plan PR #2 |
| Inventory-correction PR scope | All waves | Migration plan PR #3 |
| Runtime §F.1 fixture (~1-2 days) | W4-2 | Migration plan owner-driven |
| `ACTION-TYPE-REGISTRY.md` doc | W1 Cat 7 | Migration plan owner-driven |
| Effort buckets summary | wave-1-summary §8 + wave-2-summary §8 + wave-3-summary §8 + wave-4 above | Migration plan estimates |

### §5.3 Phase 3 sufficiency check — ✅ CONFIRMED

W4 Sub-Agent H §10 explicitly confirms: **all W1+W2+W3+W4 outputs are sufficient for Phase 3 (Canonical Stack naming + migration plan + Phase 4 review)**. No additional Phase 2 dispatches required.

### §5.4 Phase 4 = single end-of-audit owner review per Q-002

Per Q-002 lock, the owner review is packaged for END-OF-AUDIT, not mid-phase. Phase 3 produces the materials for that review:
- Canonical Stack naming document
- Migration plan with effort estimates
- Consolidated open-questions list (all 4 waves)
- Consolidated security adjudication surfaces (3 from W1+W2)
- Consolidated cross-team confirmations needed

---

## §6 Status + handoff

- **W4 status**: COMPLETE (1/1 sub-agent finished — Sub-Agent H; analysis doc persisted; this aggregation summary authored)
- **Phase 2 status**: **COMPLETE** (4 waves; 8 categories analyzed; 34 ADR candidates surfaced; ZERO code drift across all sub-agents; LATENT BUG structural remediation pattern proposed)
- **Cat 7 deferred re-dispatch**: NOT TRIGGERED (verified by Cat 1, Cat 3, Cat 5 verdicts; W4 confirms)
- **Phase 3 precondition**: MET
- **Next step**: Main session commits + opens PR with auto-merge for `notes/phase2/analysis-di-configuration.md` + `wave-4-summary.md` + `current-task.md` update. Branch: `work/audit-r1-phase2-wave4` off master.
- **Phase 3 dispatch**: defer until W4 PR merges; then PHASE 2 IS DONE → Phase 3 begins (Canonical Stack naming + migration plan).
- **Owner consultation**: not required mid-phase; Q-002 single end-of-audit review still applies.

---

## §7 Phase 2 metrics summary

| Metric | Value |
|---|---|
| Waves completed | 4 (W1 + W2 + W3 + W4) |
| Sub-agents dispatched | 8 (A, B, C, D, E, F, G, H) |
| Categories analyzed | 8 (Cat 4 Cache, Cat 2 Lookup, Cat 6 Public Contracts, Cat 7 Node executors, Cat 1 Intent, Cat 3 Search, Cat 5 Prompts, W4 DI+Config) |
| Analysis docs produced | 8 (one per category) |
| Wave summaries produced | 4 |
| ADR candidates surfaced | 34 (deferred to follow-on per Q-005) |
| Canonical reference impls designated | 4 (EmbeddingCache, InsightsIntentClassifier, RagService/NullRagService, OrchestratorPromptBuilder + CapabilityClassificationPromptBuilder) |
| New W4 canonical patterns | 2 (Public-Contracts Facade DI Fascia; Endpoint↔DI Symmetry Rule) |
| Code drift confirmed | ZERO across all 8 sub-agents |
| Inventory errors corrected | 7+ (across waves) |
| Prior-sub-agent claims corrected | 3+ (cross-validation worked) |
| HIGH-priority findings | 1 LATENT BUG (W1 Cat 6) + W4 structural remediation pattern |
| Total LOC impact of bundled DELETE PR | ~2000 LOC (corrected) |
| Total ADR-deferred work | ~2-3 weeks follow-on phase per Q-005 |
| Q-001 scope adherence | ✅ All 6 categories + DI/Config covered |
| Q-002 single review cadence | ✅ Honored (no mid-wave decisions) |
| Q-003 sequential cross-team coordination | ✅ Honored (security/team adjudication surfaces packaged) |
| Q-004 canonical naming | ✅ Candidates surfaced, NOT locked |
| Q-005 ADR deferral | ✅ Honored (34 bullets, NONE authored) |
| Q-006 Quarterly skill | ✅ Honored (deferred to follow-on) |

---

*W4 summary + Phase 2 completion report authored 2026-06-04 by main session. Phase 2 complete; Phase 3 unblocked.*
