# Spaarke Canonical AI Stack — Architecture Decisions

> **Author**: Phase 3 Sub-Agent I (synthesis from Phase 2 outputs)
> **Date**: 2026-06-04
> **Pinned to**: Phase 1 inventory commit `357e6936`
> **Status**: Phase 3 primary deliverable + Phase 2.5 (Cat 10) addendum in §14; pending Q-002 end-of-audit owner review
> **Source documents**: All 8 Phase 2 analysis docs + 4 wave summaries (W1+W2+W3+W4) + 1 Phase 2.5 Cat 10 analysis + 1 LATENT BUG #2 verification doc under [`phase2/`](phase2/)
> **Methodology lock**: Q-001 (scope: Cats 1-6 + DI/Configuration + **Cat 10 added per Path B authorization 2026-06-04**); Q-002 (single end-of-audit review); Q-003 (sequential cross-team coordination); Q-004 ("Spaarke Canonical AI Stack" framing — names SURFACED not LOCKED); Q-005 (ADRs DEFERRED — bullets only); Q-006 (Quarterly Review Skill DEFERRED).
>
> **⚠️ READ ALSO §14 (Phase 2.5 Cat 10 Addendum)** — Owner authorized Path B mini-audit 2026-06-04. Cat 10 (Tool Framework) ran after Phase 3 synthesis. Findings: COEXIST verdict (3 surfaces, not 2 as inventory said); 5 NEW ADR candidates (34 → 39 total); LATENT BUG #2 candidate REFUTED by Sub-Agent M verification; Bundled DELETE scope grows ~2000 → ~2285 LOC pending team confirmations (owner pre-approved).

---

## §1 Executive Summary

### §1.1 What this audit produced

Phase 2 of `bff-ai-architecture-audit-r1` ran an empirical 8-sub-agent inspection of the `Sprk.Bff.Api` AI surface across 8 categories (Intent Classification, Lookup, Search, Cache, Prompts, Public Contracts Facade, Node Executors, DI + Configuration). Pinned to commit `357e6936`; zero code drift observed across all 8 sub-agents.

The audit produced:
- **8 analysis docs** (one per category) — empirical decisions, drift verification, cross-team handoffs
- **4 wave summaries** aggregating per-category findings
- **34 ADR candidates** surfaced as bullets (per Q-005 DEFERRED authoring)
- **4 canonical reference implementations** designated
- **2 new W4 architectural patterns** designated
- **1 HIGH-priority LATENT BUG** identified with concrete structural remediation
- **3 security adjudication surfaces** routed to Security team
- **Bundled DELETE PR scope**: ~2000 LOC dead code
- **Inventory accuracy corrections**: 7+ items aggregated for the inventory-correction PR

### §1.2 The universal verdict — REJECT forced consolidation

Across all 8 categories, sub-agents independently reached the same conclusion: **forced abstractions behind generic interfaces are NOT appropriate for these architecturally distinct domains**. The "Spaarke Canonical AI Stack" framing (Q-004 lock) MUST be **descriptive pattern documentation, NOT binding interface abstractions**.

| Wave | Category | Consolidation verdict | Generic-interface candidate rejected |
|---|---|---|---|
| W1 Cat 2 | Lookup | KEEP 1 + DELETE 3 orphans | `ILookupService<T>` |
| W1 Cat 4 | Cache | KEEP 2 canonical specialists + adopt existing helper | (no new abstraction proposed; existing `GetOrCreateAsync<T>` adopted) |
| W1 Cat 6 | Public Contracts | KEEP 5 facades + add 4 Null peers | (facade boundary kept; no merger) |
| W1 Cat 7 | Node executors | KEEP all 18 | (no new abstraction — `INodeExecutor` already exists) |
| W2 Cat 1 | Intent classifiers | KEEP 3 + DELETE 1 orphan | `IIntentClassifier<TResult>` |
| W2 Cat 3 | Search services | KEEP all 4 substrates | `PlaybookEmbeddingService` ↔ `SemanticSearchService` merger |
| W3 Cat 5 | Prompt builders | KEEP 6 + EXTRACT-then-DELETE | `IPromptComposer` |
| W4 | DI + Configuration | KEEP 31 modules + 35 options classes + compound gate + ADR-010 cap | (no module/options/gate merger) |

This verdict is binding for the canonical-stack naming exercise: **names describe patterns and reference implementations; they do NOT impose new interface abstractions**.

### §1.3 4 canonical reference implementations + 2 new W4 patterns

Designated by Phase 2 sub-agents as load-bearing canonical reference impls:

1. **`EmbeddingCache` + `DistributedCacheExtensions.GetOrCreateAsync<T>`** (Cat 4 — Cache Stack)
2. **`InsightsIntentClassifier`** (Cat 1 — Intent Classifier Pattern)
3. **`RagService` / `NullRagService`** (Cat 3 — Search Substrate + ADR-032 Double-Gate Null-Object gold-standard)
4. **`OrchestratorPromptBuilder` + `CapabilityClassificationPromptBuilder`** (Cat 5 — Two-Layer Cached Prompt + Compact Single-Call Prompt)

NEW from W4:
5. **Spaarke Public-Contracts Facade DI Fascia** — unified `AddPublicContractsFacade` + `AddNullObjectsForCompoundOff` + `AddInsightsFacadeModule` post-LATENT-BUG-remediation
6. **Spaarke Endpoint↔DI Symmetry Rule** — generalization of `bff-extensions.md` §F.1 into binding architectural constraint

### §1.4 The LATENT BUG (HIGHEST-PRIORITY single finding)

W1 Cat 6 surfaced and W4 §4.5 confirmed at HEAD: `IInsightsAi` is registered unconditionally in `InsightsFacadeModule.cs:105`, but `InsightsOrchestrator` ctor transitively depends on `IOpenAiClient` + `IAiPlaybookBuilderService` + `AssistantToolCallHandler` (all conditional). Under compound-AI-OFF, the 3 unconditionally-mapped Insights endpoints will produce `500 Unable to resolve service` instead of the contracted `503 ProblemDetails errorCode=ai.insights.disabled`. The bug is INVISIBLE to current integration tests because the fixture mocks `IInsightsAi` directly.

W2 Cat 1 + W2 Cat 3 + W3 Cat 5 independently verified the bug is at the facade-registration topology layer (NOT in classifier/search/prompt layers). Option A structural remediation (W4 §4.5) is the surgical fix: move `IInsightsAi` + `IPlaybookExecutionEngine` into `AddPublicContractsFacade` (symmetric with other 4 facades) + add `NullInsightsAi` to `AddNullObjectsForCompoundOff`.

### §1.5 34 ADR candidates surfaced (Q-005 DEFERRED)

| Wave | Count | Highest-priority candidates |
|---|---|---|
| W1 | 14 | Cache Stack, Facade Null-Peer Mandate, Defensive-Nullable Prohibited, Zone B Symmetry, ActionType Registry, Runtime Kill-Switch Pattern |
| W2 | 8 | Intent Classifier Pattern, Search Substrate Architecture, DI Double-Gate Null-Object Pattern, Endpoint↔DI Symmetry Rule, Security Model Matrix |
| W3 | 5 | Prompt Construction Pattern, Prompt Co-location Rule, User-Managed Template Layer |
| W4 | 7 | **Endpoint↔DI Symmetry Rule (FORMAL)**, **§F.1 Runtime-Verifiable Detection**, DI Module Audit Convention, Per-Concern Composition, Canonical Options Design, Compound Gate Pattern, Directory Consolidation |
| **TOTAL** | **34** | — |

### §1.6 Bundled DELETE PR — ~2000 LOC dead code

| Source | LOC | Files |
|---|---|---|
| W1 Cat 2: lookup orphans | ~714 | `ActionLookupService`, `SkillLookupService`, `ToolLookupService` + interfaces + DI lines + `InsightsActionRouter.cs:402-403` dangling cref |
| W2 Cat 1: intent classifier orphan cascade (corrected by W3) | ~1280 | `IntentClassificationService` (408 LOC) + `BuildPlanGenerationService` (NEW orphan, ~530 LOC) + `PlaybookBuilderSystemPrompt.cs` dead members (~340 LOC after Option B extraction) + `ClarificationService` |
| W3 Cat 5: empty `Services/Ai/Prompts/` dir post-Option-B | trivial | directory cleanup |
| **Total** | **~2000 LOC** | Single bundled PR; ~20-30 KB compressed publish-size win |

### §1.7 3 security adjudication surfaces (routed to Security team per Q-003)

1. **`PrivilegeGroupResolver` IMemoryCache** (W1 Cat 4 §4.2) — caching DATA (allowed under ADR-009) or DECISIONS (forbidden)?
2. **`RecordSearchService` tenant-isolation model** (W2 Cat 3 §2.7) — Dataverse-layer security relied on; layered-defense reasoning needs validation
3. **`SemanticSearchService` privilege-filter gap** (W2 Cat 3 §4.3) — only `RagService` mandates privilege-group filter; intentional access model or AIPU2-027 gap?

### §1.8 Phase 3 outcome and Phase 4 inputs

Phase 3 delivers (this document + Sub-Agent J migration plan + Sub-Agent K DR-### records). Phase 4 = single end-of-audit owner review per Q-002 with packaged decisions, ADR candidates, migration plan, security adjudication surfaces, and cross-team coordination needs.

---

## §2 The Spaarke Canonical AI Stack (Q-004 naming synthesis)

> **Framing**: pattern-doc canonicalization, NOT binding interface abstraction. Names below are SURFACED for owner lock at Phase 4. Each layer cites its canonical reference impl(s), cross-references the source analysis doc + wave summary, and lists the pattern elements.

### §2.1 Layer 1 — Spaarke Canonical Cache Stack

**Canonical reference impl(s)**:
- `EmbeddingCache` (binary specialist; Singleton; 7-day TTL; SHA-256 keys; `Buffer.BlockCopy` float[]→byte[]; ADR-009 compliant) — at `src/server/api/Sprk.Bff.Api/Services/Ai/EmbeddingCache.cs`
- `InsightsPlaybookExecutionCache` (peer specialist; stream-draining wrapper; D-P13 SPEC §3.1)
- `Spaarke.Core.Cache.DistributedCacheExtensions.GetOrCreateAsync<T>` (generic helper; at `src/server/shared/Spaarke.Core/Cache/DistributedCacheExtensions.cs`; XML-doc explicitly names `EmbeddingCache`/`ChatSessionManager`/`InsightsPlaybookExecutionCache` as adoption targets)

**Pattern elements** (5):
1. `IDistributedCache` substrate (Redis) — single-tier substrate; in-process `MemoryCache` only with EXPLICIT ADR-009 exception XML doc
2. Typed wrapper for binary/streaming payloads (`EmbeddingCache` pattern) — opt-in
3. Generic `GetOrCreateAsync<T>` for JSON payloads (canonical helper) — opt-in becoming MUST
4. SHA-256 hash key construction with structured prefix (`sdap:embedding:{base64-sha256}`)
5. Graceful degradation on cache failure + OTEL metrics (`InsightsCacheMetrics` precedent)

**Gold-standard ADR-009 in-process exception XML doc**: `OrchestratorPromptBuilder.cs:36-44` (the ONLY file in `Services/Ai/` that documents in-process `MemoryCache` use per ADR-009 §"MUST document ADR-009 exception"; W3 Cat 5 confirms W1 Cat 4 designation).

**Cross-references**: [Cat 4 analysis §1-§2.8](phase2/analysis-cache.md) · [Cat 4 §3 decisions table](phase2/analysis-cache.md#3-per-service-decision-table) · [W2 Cat 1 cache expansion](phase2/wave-2-summary.md#24-cat-4-cache-consolidation-lever-expanded) · [W3 Cat 5 §2.5 cache cross-coordinate](phase2/wave-3-summary.md#25-w3-reinforces-adr-009-documentation-convention-as-binding-precedent) · [ADR-009 in-process exception](.claude/adr/) · ADR-010 (cap) · ADR-030 (kill-switch)

**Adoption gap**: 0% adoption of `DistributedCacheExtensions.GetOrCreateAsync<T>` inside `Services/Ai/` despite XML-doc explicitly naming adoption targets. **26 sites pending consolidation** (21 from W1 Cat 4 + 5 new from W2 Cat 1+3).

---

### §2.2 Layer 2 — Spaarke Lookup Pattern (degraded to pattern, not stack)

**Canonical reference impl**: `PlaybookLookupService` (1 surviving service post-DELETE cascade; typed `PlaybookNotFoundException` instead of generic `InvalidOperationException`)

**Status**: After DELETE of 3 orphans (`ActionLookupService`, `SkillLookupService`, `ToolLookupService`), only 1 lookup-service concrete remains. Generic `ILookupService<T>` REJECTED as YAGNI per ADR-010 (only 1 concrete remains — no abstraction warrant).

**Pattern elements** (4 for future Lookup services if needed):
1. Per-entity service (not generic) — domain boundaries are real
2. Dataverse alternate-key lookup via `IGenericEntityService`
3. In-process `MemoryCache` with 1-hour TTL + entity-coupled cache prefix
4. **Typed exception class** (e.g., `PlaybookNotFoundException`) — NOT generic `InvalidOperationException`

**Cross-references**: [Cat 2 analysis §2-§3](phase2/analysis-lookup.md) · [W1 §3 decision distribution](phase2/wave-1-summary.md#3-decision-distribution-roll-up-w1-totals)

---

### §2.3 Layer 3 — Spaarke Public-Contracts Facade DI Fascia

**Canonical reference impl(s) post-remediation**:
- 5 facades in `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/`: `IBriefingAi`, `IInvoiceAi`, `IWorkspacePrefillAi`, `IRecordMatchingAi`, `IInsightsAi`
- 5 Null peers required (1 existing + 4 to add): `NullBriefingAi` (existing P3 Fail-Fast canonical) + new `NullInvoiceAi`, `NullWorkspacePrefillAi`, `NullRecordMatchingAi`, `NullInsightsAi`
- Unified DI fascia: `AnalysisServicesModule.AddPublicContractsFacade` (all 5 real) + `AnalysisServicesModule.AddNullObjectsForCompoundOff` (all 5 Nulls) + `InsightsFacadeModule` (cleanup post-Option-A migration of `IInsightsAi`)

**Pattern elements** (5):
1. Single namespace `Services.Ai.PublicContracts/` (boundary marker)
2. Narrow facade surface (1-6 methods per facade)
3. ADR-032 P3 Fail-Fast Null peer for EVERY facade
4. `FeatureDisabledException` throwing with stable `errorCode=ai.<feature>.disabled` → 503 ProblemDetails via `AsFeatureDisabled503()`
5. Endpoint-side hard-parameter injection (NO defensive `IFoo? = null` — ADR-032 §Anti-patterns forbidden)

**Gold-standard XML doc**: All 5 facades cite ADR-013 + ADR-007 explicitly. W1 Cat 6 §4.2 calls the docs "unusually high-quality."

**Cross-references**: [Cat 6 analysis §2-§4](phase2/analysis-public-contracts.md) · [Cat 6 §F.1 two manifestations](phase2/analysis-public-contracts.md#41-f1-pattern-coverage--the-gap-is-real-and-material) · [W4 §4.5 Option A remediation](phase2/analysis-di-configuration.md#41-latent-bug-structural-root-cause--remediation-pattern-highest-leverage-finding) · [W4 §5 naming candidate](phase2/analysis-di-configuration.md#5-canonical-naming-candidates-q-004-framing--descriptive-only) · ADR-013, ADR-032, `bff-extensions.md` §F.1

**Critical adjacent finding**: `IObservationMirror` is NOT a §F.1 candidate — it has an intentional dual real-impl (NoOp default + Dataverse swap via `services.Replace`). Document explicitly to prevent future audits flagging it.

---

### §2.4 Layer 4 — Spaarke Node Executor Registry

**Canonical reference impl(s)**:
- `INodeExecutor` interface at `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/INodeExecutor.cs`
- `ActionType` enum at `INodeExecutor.cs:78-207` (the central registry — compile-time defense + runtime duplicate-detection at `NodeExecutorRegistry.cs:89`)
- 18 confirmed executors at HEAD (CORRECTED from inventory's 16; see [Cat 7 §2.1](phase2/analysis-node-executors.md))

**Pattern elements** (4):
1. Single source of truth: `ActionType` enum is the central registry
2. Block organization (implicit but consistent): 0-2 AI primitives, 10-12 reserved computation, 20-29 external integration, 30-39 control flow, 40-49 output, 50-59 notification/query, 60-69 Foundry, 70-149 Insights Engine
3. Compile-time defense (enum) + runtime duplicate-detection (registry init)
4. Per-executor Single `SupportedActionTypes` element (multi-action-type capability of interface is unused)

**Missing companion artifact**: `ACTION-TYPE-REGISTRY.md` allocation contract — block reservations, next-available, owner-per-block, deprecation policy. **HIGH-priority deliverable** to preempt collision risk from parallel-project worktrees.

**Runtime Kill-Switch Pattern** (distinct from DI Null-Object): `AgentServiceNodeExecutor.cs:198-212` catches `FeatureDisabledException` from injected `AgentServiceClient` and returns structured `NodeOutput.Error(... NODE_AGENT_FEATURE_DISABLED ...)`. This is the canonical reference for runtime-gated executors (peer to ADR-030).

**Cross-references**: [Cat 7 analysis §2-§3](phase2/analysis-node-executors.md) · [W1 §3](phase2/wave-1-summary.md#3-decision-distribution-roll-up-w1-totals) · ADR-010, ADR-030

---

### §2.5 Layer 5 — Spaarke Canonical Intent Classifier Pattern

**Canonical reference impl**: `InsightsIntentClassifier` (`src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Routing/InsightsIntentClassifier.cs`)

**Pattern elements** (7) (from [Cat 1 §4.1.3](phase2/analysis-intent-classification.md)):
1. Singleton DI lifetime (stateless, threadsafe)
2. `IOptions<T>` binding from feature-specific config section
3. ADR-032 §F.1 dual registration with `NullInsightsIntentClassifier` peer (always-registered)
4. JSON-schema-constrained LLM decoding (deterministic output shape)
5. SHA-256 cache key construction over normalized query (in-process `MemoryCache` with documented ADR-009 exception)
6. OTEL-instrumented latency tracking with FR-05-style budget (500ms target)
7. Domain-specific result type (NOT generic `IClassifier<T>`)

**KEEP siblings (3 + 1 type)**:
- `CapabilityRouter` (3-tier: keyword → LLM → fallback) — AIPU2-012; 2 behavioral consumers + 2 sentinel/telemetry
- `PlaybookDispatcher` (2-stage: vector + LLM) — factory-instantiated for per-request tenantId; 3 behavioral + 1 sentinel
- `IntentClassification` type in `AiPlaybookBuilderService` (independent service; own inline classifier; 4 production consumers)

**DELETE orphan cluster** (~1280 LOC corrected): `IntentClassificationService` (408) + `BuildPlanGenerationService` (NEW 5th orphan, ~530) + `PlaybookBuilderSystemPrompt.cs` 80%-dead members + `ClarificationService`

**Type B `IntentClassificationResult` rename**: LOW priority. Candidate `InsightsRoutingDecision` or namespace disambiguation sufficient.

**Cross-references**: [Cat 1 analysis](phase2/analysis-intent-classification.md) · [W2 §1.1](phase2/wave-2-summary.md#11-cat-1--intent-classification-sub-agent-e) · [W3 Cat 5 cross-correction](phase2/wave-3-summary.md#22-w3-corrects-w2-substantially--cross-sub-agent-validation-worked)

---

### §2.6 Layer 6 — Spaarke Canonical Search Substrate Architecture

**Canonical reference impl**: `RagService` / `NullRagService` (`src/server/api/Sprk.Bff.Api/Services/Ai/RagService.cs` + `NullRagService.cs`)

**Pattern elements** (5):
1. Hybrid keyword + vector + semantic ranking against Azure AI Search
2. Mandatory tenant filter + mandatory privilege-group filter (AIPU2-027 fail-closed)
3. P3 Fail-Fast Null peer for AI-Search-keys-missing OR compound-OFF
4. `IEmbeddingCache` integration for embedding generation
5. Graceful degradation with OTEL spans

**KEEP siblings (3 substrates + 1 pending security review)**:
- `SemanticSearchService` — hybrid RRF/vector/keyword; pipeline of `IQueryPreprocessor`/`IResultPostprocessor`; tenant filter only (no privilege-group filter — **SECURITY ADJUDICATION SURFACE**)
- `RecordSearchService` — `spaarke-records-index`; NO tenant filter (Dataverse-mediated security — **SECURITY ADJUDICATION SURFACE**)
- `PlaybookEmbeddingService` — `playbook-embeddings` index; system-config artifacts (admin-scoped); factory-instantiated

**REJECT: `PlaybookEmbeddingService ↔ SemanticSearchService` merger** — different indices, document shapes, security models, API surfaces, lifecycles, DI patterns ([Cat 3 §2.6](phase2/analysis-search.md)).

**Double-Gate Null-Object Pattern (NEW from W2 Cat 3 §4.2)**:
> Pattern peer to ADR-030 single-gate. Two-tier registration: compound-AI-OFF branch registers Null + compound-AI-ON + resource-credentials-missing branch ALSO registers Null. `RagService`/`NullRagService` is the gold-standard reference impl. ADR-CAND-F-2 surfaced.

**Null-peer asymmetry RESOLVED**: 1-of-4 has Null peer is INTENTIONAL and CORRECT — `RagEndpoints` mapped unconditionally; the other 3 substrates conditionally mapped SYMMETRICALLY with their DI registration. NOT a §F.1 anti-pattern. **3 explicit DO-NOT-ADD-Null-peer** (responding to inventory §7.3 bullet 1).

**Tenant + Privilege-Filter Matrix** (architecturally documentable distinction):
| Service | Tenant filter | Privilege filter | Security model |
|---|---|---|---|
| RagService | MANDATORY | MANDATORY (AIPU2-027 fail-closed) | Layer-1 |
| SemanticSearchService | YES | **NO — security adjudication surface** | Layer-1 |
| RecordSearchService | NO | NO | Dataverse-mediated (layered-defense — adjudication surface) |
| PlaybookEmbeddingService | NO | NO | Admin-scoped system config |

**Cross-references**: [Cat 3 analysis](phase2/analysis-search.md) · [W2 §1.2](phase2/wave-2-summary.md#12-cat-3--search-services-sub-agent-f) · [W2 §2.6 security surfaces](phase2/wave-2-summary.md#26-new-security-adjudication-surfaces-for-q-002-owner-review) · ADR-032

---

### §2.7 Layer 7 — Spaarke Canonical Prompt Construction Pattern

**Canonical reference impl(s)**:
- **Two-layer cached prompt**: `OrchestratorPromptBuilder` (Singleton; Layer 1 stable prefix cached by manifest hash with 20-min TTL; Layer 2 per-turn suffix never cached; 9000-token total budget with rebalancing) — at `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/OrchestratorPromptBuilder.cs`
- **Compact single-call prompt**: `CapabilityClassificationPromptBuilder` (static class; ≤600 token target for Layer 2 routing) — at `src/server/api/Sprk.Bff.Api/Services/Ai/Capabilities/CapabilityClassificationPromptBuilder.cs`

**Pattern elements** (4):
1. Co-located with sole consumer (NO generic `/Prompts/` dir for shared prompts — W3 surfaces co-location rule as ADR-CAND-G-03)
2. Token budget per-builder (≤600 vs 9000) — NOT a single shared budget
3. Output shape per-builder (`IList<ChatMessage>` vs `OrchestratorPrompt` record vs `string`)
4. Two-layer caching opt-in via stable prefix (Singleton only) — ADR-009 in-process exception XML doc convention

**5 explicit prompt sources at HEAD** (CORRECTED from inventory's "3 + many inline"):
1. `CapabilityClassificationPromptBuilder` (canonical compact)
2. `OrchestratorPromptBuilder` (canonical two-layer cached)
3. `PlaybookBuilderSystemPrompt` (80% dead + 20% alive — needs Option B EXTRACT-then-DELETE)
4. `AnalysisContextBuilder` (`IAnalysisContextBuilder` Scoped) — **MISSED by inventory**
5. `FallbackPrompts` (static fallback constants) — **MISSED by inventory**

**REJECT: `IPromptComposer` generic interface** — input shapes domain-specific, output shapes diverge, token budgets per-builder, DI lifetimes differ (static vs Singleton vs Scoped), stable-prefix caching only in Orchestrator.

**PromptLibrary reframe (architecturally significant inventory correction)**: PromptLibrary is the BACKING SERVICE for an end-user-managed template store (Cosmos + Dataverse tiers per AIPU2-035/036), NOT an LLM-call-site abstraction. Uses `{{variableName}}` Mustache substitution. ZERO non-endpoint consumers — exactly as designed. Inventory §2.5.4 "limited adoption" framing is wrong; PromptLibrary is in the user-managed-template layer, NOT the LLM-call-site layer.

**Option B EXTRACT-then-DELETE for `PlaybookBuilderSystemPrompt.cs`** (REQUIRED to make bundled DELETE PR viable):
1. Create `Services/Ai/Builder/BuilderAgentSystemPrompt.cs` with live `Build(actions, skills, knowledge)` method (~200 LOC)
2. Update `BuilderAgentService.cs:270` reference
3. DELETE entire `Services/Ai/Prompts/PlaybookBuilderSystemPrompt.cs` (~969 LOC)
4. DELETE empty `Services/Ai/Prompts/` directory

Without Option B, the bundled DELETE PR would fail to compile.

**Cross-references**: [Cat 5 analysis](phase2/analysis-prompts.md) · [W3 §1.1](phase2/wave-3-summary.md#11-cat-5--prompt-builders-sub-agent-g) · [W3 §2.2 cross-corrects W2](phase2/wave-3-summary.md#22-w3-corrects-w2-substantially--cross-sub-agent-validation-worked) · ADR-009 exception

---

### §2.8 Layer 8 — Spaarke DI Module Composition + Configuration

**Canonical reference impl(s)**:
- Per-concern DI modules: 31 modules at HEAD (CORRECTED from inventory's 34) under `src/server/api/Sprk.Bff.Api/Infrastructure/DI/` (29 modules), `Api/Reporting/` (1), `Workers/Office/` (1)
- DI Module audit-table convention: `AiModule.cs:269-313` (15/15 ADR-010 budget cap with line-numbered + task-tagged registrations) — gold-standard reference
- Per-feature options classes: 35 options classes (matches inventory headline); single namespace `Sprk.Bff.Api.Configuration` (inventory's "Options namespace" framing is WRONG — does NOT exist)
- Compound AI gate: `AnalysisServicesModule.cs:18-117` (3-tier: compound `Analysis:Enabled && DocumentIntelligence:Enabled` → fine-grained sub-gate → resource-prerequisite sub-gate)

**Pattern elements (DI Module)** (4):
1. Per-concern composition criterion (NOT functional area collapse)
2. ADR-010 cap mechanism (`AiModule` 15/15 cap; promotions to `AnalysisServicesModule` documented per Tier 1.5)
3. Inline audit-table comment block (registration line numbers + originating task references)
4. `§F.1 inspection` comment block on conditional registrations (current discipline)

**Pattern elements (Options class)** (4):
1. Single namespace `Sprk.Bff.Api.Configuration`
2. `AddOptions<T>().BindConfiguration(...)` newest canonical binding pattern
3. Per-options const `SectionName` + DataAnnotations + per-field XML doc citing originating task/SPEC/POML
4. `ValidateDataAnnotations().ValidateOnStart()` chaining

**Pattern elements (Compound Gate)** (3):
1. 2 compound flags = entire AI stack inert with 10 Null peers (operational simplicity)
2. Fine-grained sub-gates INSIDE compound-ON branch (e.g., `Insights:IntentClassifier:Enabled` @ AnalysisServicesModule:508-534)
3. Resource-prerequisite sub-gates (e.g., AI-Search-keys @ `AddRagServices:539-561`)

**NEW Endpoint↔DI Symmetry Rule** (W4 §4.1 — the load-bearing generalization):
> Any service registered behind a feature flag (compound AI gate, fine-grained option flag, AI-Search-keys sub-gate) MUST satisfy one of these conditions:
> 1. Its consumer endpoint is mapped behind the SAME feature flag (symmetric conditionality), OR
> 2. A Null-Object peer is registered in the gate-OFF branch with EXACTLY the same interface type + lifetime + same `ServiceDescriptor.ServiceType` (ADR-032 P3 Fail-Fast), AND every TRANSITIVE ctor dependency of the real impl satisfies the same rule, AND a startup integration test exists that asserts gate-OFF state returns 503 not 500.

**NEW §F.1 Runtime-Verifiable Detection Fixture** (W4 §4.2):
> A reusable `WebApplicationFactory<Program>` subclass that bootstraps the BFF with each compound gate independently flipped (4 combinations) + probes every unconditionally-mapped endpoint + asserts NO 500 with "Unable to resolve" body.

**Inventory corrections from W4**:
- Module count **34 → 31** (inventory §3.1 overcounted by 3)
- `Sprk.Bff.Api.Options` namespace **does NOT exist** — `Options/` directory uses `Sprk.Bff.Api.Configuration` namespace (inventory §4.3 framing error)
- `Configuration/` per-dir count **25 → 21** (inventory §4.1)

**Cross-references**: [W4 analysis](phase2/analysis-di-configuration.md) · [W4 §4.1 Symmetry Rule](phase2/analysis-di-configuration.md#41-latent-bug-structural-root-cause--remediation-pattern-highest-leverage-finding) · [W4 §4.2 Runtime fixture](phase2/analysis-di-configuration.md#42-f1-should-be-runtime-verifiable-not-comment-block-verifiable) · [W4 §5 naming candidates](phase2/analysis-di-configuration.md#5-canonical-naming-candidates-q-004-framing--descriptive-only) · [Wave 4 summary](phase2/wave-4-summary.md) · ADR-010, ADR-013, ADR-018, ADR-030, ADR-032, `bff-extensions.md` §F.1

---

### §2.9 Recommended canonical names for Q-002 owner lock

Per Q-004 (names SURFACED not LOCKED), recommended names for owner lock at Phase 4:

| # | Layer | Recommended canonical name | Status |
|---|---|---|---|
| 1 | Cache | **Spaarke Canonical Cache Stack** | NEW name; descriptive over existing pattern |
| 2 | Lookup | **Spaarke Lookup Pattern** (degraded — not a stack) | Pattern-only post-DELETE |
| 3 | Public Contracts Facade | **Spaarke Public-Contracts Facade DI Fascia** (W4 §5) | NEW name; load-bearing |
| 4 | Node Executors | **Spaarke Node Executor Registry** | Existing pattern, name canonicalized |
| 5 | Intent Classifier | **Spaarke Canonical Intent Classifier Pattern** | Pattern-doc, NOT interface |
| 6 | Search | **Spaarke Canonical Search Substrate Architecture** + **Double-Gate Null-Object Pattern** | Two distinct sub-patterns |
| 7 | Prompts | **Spaarke Canonical Prompt Construction Pattern** | Pattern-doc, NOT interface |
| 8 | DI/Config | **Spaarke Endpoint↔DI Symmetry Rule** + **Spaarke DI Module Audit Convention** | Symmetry rule is load-bearing |

The two load-bearing NEW names are #3 and #8's Symmetry Rule. The other 6 names are descriptive overlays on existing patterns that the audit has codified.

---

## §3 Per-Category Decisions Roll-Up

> Distilled from wave summaries §3 tables. Each row = canonical verdict + reference impl + cross-team owner + migration cost.

| Cat | Domain | Verdict | Canonical reference impl | Cross-team owner | Migration cost | Analysis doc |
|---|---|---|---|---|---|---|
| W1 Cat 2 | Lookup | **KEEP 1 + DELETE 3 orphans** + REJECT generic | `PlaybookLookupService` | Finance Intelligence | S (single PR; ~714 LOC) | [`analysis-lookup.md`](phase2/analysis-lookup.md) |
| W1 Cat 4 | Cache | **KEEP 2 canonical + ADOPT helper @ 26 sites** + REJECT new abstraction | `EmbeddingCache` + `DistributedCacheExtensions.GetOrCreateAsync<T>` | SprkChat + Insights + Workspace + Finance + Foundry + Security (multi-team) | L (3-5 weeks phased) | [`analysis-cache.md`](phase2/analysis-cache.md) |
| W1 Cat 6 | Public Contracts | **ADD 4 Null peers + RESTRUCTURE 1** (LATENT BUG Option A) | 5 facades + 5 Null peers (after remediation) | Insights + Workspace + Finance (cross-cutting) | M (~180 LOC + integration test) | [`analysis-public-contracts.md`](phase2/analysis-public-contracts.md) |
| W1 Cat 7 | Node executors | **KEEP all 18 + AUTHOR `ACTION-TYPE-REGISTRY.md`** + KEEP runtime kill-switch precedent | `INodeExecutor` + `ActionType` enum + `AgentServiceNodeExecutor` runtime kill-switch | All teams (registry doc); Foundry (kill-switch) | XS (doc) | [`analysis-node-executors.md`](phase2/analysis-node-executors.md) |
| W2 Cat 1 | Intent classifiers | **KEEP 3 + DELETE 1 orphan cascade** (~1280 LOC) + REJECT generic | `InsightsIntentClassifier` | AI Chat Playbook Builder (orphan owner); Insights (canonical) | M (bundled DELETE PR) | [`analysis-intent-classification.md`](phase2/analysis-intent-classification.md) |
| W2 Cat 3 | Search services | **KEEP all 4 substrates + 3 EXPLICIT DO-NOT-ADD-Null** + 2 security adjudication + REJECT merger | `RagService`/`NullRagService` (double-gate gold-standard) | AIPL (Rag); AIPU R1 (Semantic); record-matching (Record); SprkChat (Playbook) | S (no code change post-decisions); Security adjudication TBD | [`analysis-search.md`](phase2/analysis-search.md) |
| W3 Cat 5 | Prompt builders | **KEEP 6 + EXTRACT-then-DELETE (Option B)** + DELETE 5th orphan + REJECT generic | `OrchestratorPromptBuilder` + `CapabilityClassificationPromptBuilder` | AI Chat Playbook Builder (orphan); AIPU R1/R2 (canonicals) | M (Option B + cascade DELETE) | [`analysis-prompts.md`](phase2/analysis-prompts.md) |
| W4 | DI + Configuration | **KEEP 31 modules + 35 options + compound gate + ADD Endpoint↔DI Symmetry Rule + RESTRUCTURE LATENT BUG + CONSOLIDATE `Options/`→`Configuration/` dir** | `AiModule.cs:269-313` audit table + `AddPublicContractsFacade` post-remediation | Insights (LATENT BUG); all teams (Symmetry Rule + Runtime fixture); Insights (fixture) | M (~280 LOC remediation + fixture + 2 file moves) | [`analysis-di-configuration.md`](phase2/analysis-di-configuration.md) |

---

## §4 The LATENT BUG and Structural Remediation Pattern

### §4.1 The bug — `IInsightsAi` 500 instead of 503 under compound-AI-OFF

**Severity**: HIGH. **Visibility**: LATENT (current integration tests mock `IInsightsAi` directly, masking the failure mode).

**Mechanism** (per [Cat 6 §2 + §4](phase2/analysis-public-contracts.md), confirmed at HEAD by [W4 §4.5](phase2/analysis-di-configuration.md)):

```
[3 UNCONDITIONALLY-MAPPED ENDPOINTS]
 /api/insights/ask
 /api/insights/search
 /api/insights/assistant/query
        ↓ injects (hard parameter, non-nullable)
[IInsightsAi → InsightsOrchestrator] (registered UNCONDITIONALLY in InsightsFacadeModule.cs:105)
        ↓ ctor depends on
[8 ctor params; 7 conditional:]
 - IInsightsPlaybookExecutionCache (compound-ON only)
 - IOpenAiClient (compound-ON only @ AnalysisServicesModule:27)
 - IPlaybookOrchestrationService (Null peer exists @ :217)
 - IIngestDocumentSource (InsightsIngestModule)
 - IOptionsMonitor<...> (Options pattern — always works)
 - IRagService (Null peer exists @ :223)
 - AssistantToolCallHandler (Scoped @ AnalysisServicesModule:80 ONLY in compound-ON branch — NO Null peer)
        ↓
[Metadata-gen at startup passes] (param-inference only checks signature-resolvability, not transitive ctor-resolvability)
[First request to any of 3 endpoints]
        ↓
[InsightsOrchestrator ctor throws InvalidOperationException("Unable to resolve service for type IOpenAiClient...")]
        ↓
[ASP.NET returns 500 with opaque body]

EXPECTED: 503 ProblemDetails with stable errorCode=ai.insights.disabled via FeatureDisabledException → AsFeatureDisabled503()
```

**Compounding factor**: `AnalysisServicesModule.cs:75-79` contains a comment claiming `IInsightsAi` falls back to Null peer — **factually incorrect**. This comment-code mismatch misleads future §F.1 inspections.

### §4.2 Cross-wave verification convergence

All 4 W2+W3+W4 waves verified independently that the bug is at the **facade-registration topology layer**, NOT downstream:

| Wave | Layer verified | Result |
|---|---|---|
| W1 Cat 6 §4.1 | Facade DI layer | LATENT BUG present: transitive-conditional ctor deps |
| W2 Cat 1 §4.2 | Classifier layer | LATENT BUG NOT here — `InsightsIntentClassifier` DI is correct |
| W2 Cat 3 §2.5 | Search layer | LATENT BUG NOT here — `NullRagService` works correctly; bug fires upstream in `InsightsOrchestrator` ctor BEFORE reaching `_ragService.SearchAsync` |
| W3 Cat 5 | Prompt layer | LATENT BUG NOT here |
| W4 §4.5 | DI registration topology | **CONFIRMED at HEAD**; Option A remediation proposed |

**All 5 verifications converge**: Option A structural remediation is the surgical fix.

### §4.3 Option A structural remediation (W4 §4.5 — recommended)

Single bundled PR (~280 LOC total):

1. **Move** `services.AddScoped<IInsightsAi, InsightsOrchestrator>()` from `InsightsFacadeModule:105` to `AnalysisServicesModule.AddPublicContractsFacade:357-363` (symmetric with other 4 facades — `IBriefingAi`, `IInvoiceAi`, `IWorkspacePrefillAi`, `IRecordMatchingAi`)
2. **Move** `services.AddScoped<IPlaybookExecutionEngine, PlaybookExecutionEngine>()` from `InsightsFacadeModule:95` to the same compound-ON helper
3. **Add** `NullInsightsAi` impl (~100 LOC; 6 methods throwing `FeatureDisabledException("ai.insights.disabled", "...")`):
   - `AnswerQuestionAsync`, `RunIngestAsync`, `EmbedTextAsync`, `SearchAsync`, `AssistantQueryAsync`, `AssistantQueryStreamAsync`
4. **Register** `NullInsightsAi` in `AnalysisServicesModule.AddNullObjectsForCompoundOff`
5. **Fix** misleading comment at `AnalysisServicesModule.cs:75-79`
6. **Add** integration test asserting 503 (not 500) from 3 unconditionally-mapped Insights endpoints under compound-AI-OFF (`DocumentIntelligence:Enabled=false`)

**Bundle with**: 4 Null-peer facades (`NullInvoiceAi`, `NullWorkspacePrefillAi`, `NullRecordMatchingAi`) for ~80 additional LOC. Total bundled PR ~280 LOC.

### §4.4 Endpoint↔DI Symmetry Rule (W4 §4.1 — generalization)

The LATENT BUG is the canonical instance of an **endpoint-mapping ↔ DI-registration asymmetry**. W4 §4.1 generalizes this into a binding architectural rule (proposed ADR-CAND-W4-1):

> **Endpoint Mapping ↔ DI Registration Conditionality Symmetry Rule**:
> Any service registered behind a feature flag (compound AI gate, fine-grained option flag, AI-Search-keys sub-gate) MUST satisfy one of these conditions:
> 1. Its consumer endpoint is mapped behind the SAME feature flag (symmetric conditionality), OR
> 2. A Null-Object peer is registered in the gate-OFF branch with EXACTLY the same interface type + lifetime + same `ServiceDescriptor.ServiceType` (ADR-032 P3 Fail-Fast), AND every TRANSITIVE ctor dependency of the real impl satisfies the same rule, AND a startup integration test exists that asserts gate-OFF state returns 503 not 500.
>
> The rule applies recursively through the service-resolution chain — if facade `F` is registered unconditionally but its ctor depends on conditional service `S`, then EITHER `S` must have a Null peer OR `F`'s registration must move into the conditional branch.

**This rule, if enforced via runtime test, would have caught the LATENT BUG at PR-review time.** It generalizes `bff-extensions.md` §F.1 from a comment-block-verifiable property to an architectural-and-runtime-verifiable property.

### §4.5 Runtime §F.1 Detection Fixture (W4 §4.2)

Codification of the runtime test that closes the loop (proposed ADR-CAND-W4-2):

> A reusable integration-test fixture (xUnit `IClassFixture` or `WebApplicationFactory<Program>` subclass) that bootstraps the BFF with each compound gate independently flipped to OFF (4 combinations: both ON, DocumentIntelligence-OFF only, Analysis-OFF only, both OFF). For each combination, the fixture probes every unconditionally-mapped endpoint registered in `EndpointMappingExtensions.cs` with a synthetic request shape triggering each handler's hot path. The test asserts NO handler returns 500 with body containing "Unable to resolve" — the only acceptable failure modes under gate-OFF are (a) 503 ProblemDetails with stable errorCode `ai.*.disabled`, or (b) 200 OK with degraded payload.

**Effort**: ~1-2 days fixture + tests; Insights team owns (the team most affected by the LATENT BUG).

---

## §5 Cross-Cutting Verdicts (Universal Across 8 Categories)

### §5.1 REJECT forced consolidation — the dominant Phase 2 finding

All 8 categories independently arrived at the same conclusion: **forced abstractions behind generic interfaces are NOT appropriate for these architecturally distinct domains**. Generic-interface candidates explicitly rejected:
- `ILookupService<T>` (Cat 2)
- `IIntentClassifier<TResult>` (Cat 1)
- `IPromptComposer` (Cat 5)
- `PlaybookEmbeddingService` ↔ `SemanticSearchService` merger (Cat 3)
- Module / options / compound-gate consolidations (W4)

Reasons are parallel across categories:
- Output shapes are domain-specific and would lose type safety if abstracted
- Multi-stage cascade designs differ structurally (3-tier vs 2-stage vs single-call vs hybrid RRF vs vector-only)
- Telemetry, concurrency, and security models are per-substrate
- DI lifetimes differ (Singleton vs Scoped vs static vs factory-instantiated)
- The shared substrate (Azure OpenAI / Azure AI Search) is a thin coincidence that does not justify abstraction

**Binding for Phase 3 deliverable**: the "Spaarke Canonical AI Stack" framing (Q-004) is **descriptive pattern documentation, NOT binding interface abstractions**.

### §5.2 Pattern doc canonicalization, NOT interface abstractions

Per-category recommendation across W2+W3+W4 sub-agents:
- Document the canonical reference impl ([§2.1-§2.8](#2-the-spaarke-canonical-ai-stack-q-004-naming-synthesis))
- Document the pattern elements (3-7 per layer)
- Cite ADR cross-references
- Surface ADR candidates as bullets (per Q-005) for follow-on phase
- Drive ADOPTION of existing canonicals (not introduce new ones) where canonicals exist but are under-adopted (Cat 4 cache helper at 0% adoption is the headline case)

### §5.3 Cross-sub-agent validation worked

The methodology of having later sub-agents read prior outputs as peer context paid dividends throughout Phase 2:

| Validation | Source | Result |
|---|---|---|
| W2 Cat 1 reviewing W1 Cat 4 | W2 Cat 1 §2.5 | **REJECTED** W1's `NullInsightsIntentClassifier` cache-dep anti-smell (ctor takes only `ILogger<T>`) |
| W2 Cat 1 reviewing inventory §2.1 | W2 Cat 1 §2.4 | Discovered 3 (not 2) `IntentClassification*` types |
| W2 Cat 1 reviewing W1 + inventory | W2 Cat 1 §2.3.3 | **REJECTED** "AiPlaybookBuilderService at-risk" claim |
| W2 Cat 3 reviewing inventory | W2 Cat 3 §4.4 | Mislabels: `DocumentClassifierHandler` uses `IRagService`; `AiAnalysisNodeExecutor` is `IRecordSearchService` consumer |
| W3 Cat 5 reviewing W2 Cat 1 | W3 Cat 5 §2.3 | **W2 Cat 1's cascade DELETE estimate wrong by 13×** — whole-file DELETE impossible; cascade scope ~100 → ~1280 LOC |
| W3 Cat 5 reviewing inventory | W3 Cat 5 §2.1, §2.5 | Missed `AnalysisContextBuilder` + `FallbackPrompts`; mis-framed PromptLibrary; **NEW 5th orphan**: `BuildPlanGenerationService.cs` (~530 LOC) |
| W4 reviewing inventory | W4 §2.2, §2.3 | Module count 34→31; `Sprk.Bff.Api.Options` namespace does NOT exist; `Configuration/` per-dir 25→21 |
| W4 reviewing Cat 6 LATENT BUG | W4 §4.5 | Independently verified at HEAD; structural Option A remediation pattern proposed |

**Total**: 7+ inventory errors corrected; 3+ prior-sub-agent claims corrected. **Cross-sub-agent validation worked as designed.**

### §5.4 Empirical-reproduction-FIRST methodology validated

CLAUDE.md §10 F.3 (Empirical-Reproduction-FIRST Protocol) was applied rigorously by every sub-agent:
- Cat 2 (Sub-Agent B) corrected `PlaybookLookupService` consumer count 2→1 (doc-cref vs ctor injection)
- Cat 7 (Sub-Agent D) corrected executor count 16→18
- W2 Cat 1 (Sub-Agent E) corrected W1 Sub-Agent A false positives
- W3 Cat 5 (Sub-Agent G) corrected W2 Cat 1 cascade scope
- W4 (Sub-Agent H) corrected inventory module + options counts

**Outcome**: cross-validation flagged + fixed errors at every wave. Phase 2 final findings are empirically grounded.

### §5.5 ZERO code drift across all 4 waves

Every sub-agent independently confirmed `git diff --stat 357e6936..HEAD -- [their scope]` returns EMPTY. The 8 commits between snapshot and HEAD are docs-only (PR #341 audit init + PR #342 r3 scaffold + PR #340 r2 wrap-up + PR #343 Phase 1 inventory + PR #344 Phase 2 W1+W2 + PR #346 Phase 2 W3). **Inventory `357e6936` remains fully authoritative for the snapshot.** Inventory drift findings are inventory accuracy corrections, NOT code drift.

---

## §6 Inventory Corrections (consolidated for inventory-correction PR)

> Tabulating ALL inventory accuracy gaps surfaced by W2+W3+W4 cross-validation. Source: scattered across all 4 wave summaries; consolidated here for the single inventory-correction PR.

| # | Inventory location | Inventory claim | Correction | Surfaced by |
|---|---|---|---|---|
| 1 | §3.1 module count | "34 DI modules" | **31 modules** at HEAD (29 in `Infrastructure/DI/` + 1 in `Api/Reporting/` + 1 in `Workers/Office/`) | W4 §2.2 |
| 2 | §4.3 namespace split | "`Sprk.Bff.Api.Options` (2 classes) — redundant namespace split" | **`Sprk.Bff.Api.Options` namespace does NOT exist.** `Options/` directory uses `Sprk.Bff.Api.Configuration` namespace. Issue is directory location only, not namespace. | W4 §2.3 |
| 3 | §4.1 per-dir count | "`Configuration/` (25 files)" | **21 files** in `Configuration/` at HEAD. Total 35 options classes headline is correct. | W4 §2.3 |
| 4 | §2.5.4 explicit prompt count | "3 explicit builders + many inline" | **5 explicit prompt sources** at HEAD: adds `AnalysisContextBuilder` (Scoped) + `FallbackPrompts` (static). Inline count 7 → 3-5 (post-mislabel removal). | W3 Cat 5 §2.1 |
| 5 | §2.5.4 PromptLibrary framing | "limited adoption — most LLM-calling services do NOT route through it" | **Architecturally misleading.** PromptLibrary is a user-facing CRUD facade for end-user-managed templates (Mustache substitution, Cosmos + Dataverse tiers, per-user/team/tenant authz). ZERO non-endpoint consumers — exactly as designed. Reframe as user-managed-template layer, NOT LLM-call-site layer. | W3 Cat 5 §4.3 |
| 6 | §2.1 IntentClassification types | "2 `IntentClassificationResult` types" | **3 related types**: Type A (`Services.Ai.IntentClassificationResult`, DELETE), Type B (`Services.Ai.Insights.Routing.IntentClassificationResult`, RENAME-LOW), Type C (`Services.Ai.IntentClassification` in PlaybookBuilderService, KEEP). | W2 Cat 1 §2.4 |
| 7 | §2.7 executor count | "16 registered concrete executors" | **18 confirmed** at HEAD (Foundry + Insights Engine blocks miscounted) | W1 Cat 7 §2.1 |
| 8 | §2.7 ActionType labels | "(default)" ActionType labels on first 9 executors | **Explicit numerics** (`AiAnalysis = 0`, `CreateTask = 20`, etc.) | W1 Cat 7 §2.1 |
| 9 | §2.7 AgentServiceNodeExecutor | "Singleton (kill-switched)" | **Runtime kill-switch, not DI**: DI is unconditional; kill-switch is runtime-only at `AgentServiceNodeExecutor.cs:198-212` | W1 Cat 7 |
| 10 | §2.3.2 DocumentClassifierHandler | "consumer of `ISemanticSearchService`" | **Imports `IRagService` at HEAD**, NOT `ISemanticSearchService` | W2 Cat 3 §2.3.2 |
| 11 | §2.3.3 AiAnalysisNodeExecutor | (not listed as `IRecordSearchService` consumer) | **NEW consumer**: lines 37, 44 in `Services/Ai/Nodes/AiAnalysisNodeExecutor.cs` inject `IRecordSearchService` | W1 Cat 7 + W2 Cat 3 §2.3.3 |
| 12 | §2.1 CapabilityRouter consumers | "4 production" | **2 behavioral + 2 sentinel/telemetry** | W2 Cat 1 §2.3.1 |
| 13 | §2.1 PlaybookDispatcher consumers | "4 production via factory" | **3 behavioral + 1 sentinel** | W2 Cat 1 §2.3.2 |
| 14 | §6.2 orphan list | "4 confirmed UNUSED orphans" | **5 orphans** — adds `BuildPlanGenerationService.cs` (~530 LOC, 5th NEW orphan missed by inventory + W2 Cat 1) | W3 Cat 5 §2.3 |
| 15 | §2.2.1 PlaybookLookupService consumers | "2 consumers" | **1 production consumer** (the 2nd was a doc-cref only in `DefaultPlaybookConstants.cs`) | W1 Cat 2 §2.3 |
| 16 | §2.5 explicit builders | "3 explicit builders" | **4 formally registered/instantiable** + 1 static fallback const = 5 sources (missed `AnalysisContextBuilder` + `FallbackPrompts`) | W3 Cat 5 §2.1 |
| 17 | §2.4.2 cache consumer table | 32 inline cache consumers | 32 confirmed; 2 unclassified (`StandaloneChatContextProvider`, `AnalysisChatContextResolver`) need reconciliation | W1 Cat 4 §6.4 |
| 18 | §2.6 facade Null-peer claim | "1 has Null peer; the other 4 do NOT" | **CORRECT but framing incomplete**: 4 facades affected by §F.1 in TWO distinct manifestations (latent transitive-conditional for `IInsightsAi`; visible defensive-nullable for others). Add §F.1 manifestation distinction. | W1 Cat 6 §4.1 |

**Bundling**: Single PR — inventory accuracy corrections only; no code changes. Can bundle with `Options/` → `Configuration/` directory `git mv` consolidation (W4 §3 row 2).

---

## §7 Security Adjudication Surfaces (Q-003 sequential — routed to Security team)

### §7.1 RecordSearchService tenant-isolation model (W2 Cat 3 §2.7)

**Surface**: `RecordSearchService.cs` XML doc disclaims tenant isolation in favor of Dataverse-layer security. Search results carry only non-sensitive metadata projections; every consumer chain resolves through Dataverse before exposing content.

**Question for Security team**: Is the layered-defense reasoning internally consistent IF:
- (a) index carries only non-sensitive metadata projections (verifiable claim)
- (b) every consumer chain resolves through Dataverse before exposing content (architectural assumption — needs review)
- (c) correlation-attack entropy risk on `spaarke-records-index` is acceptable (open question)

**Recommendation**: Security team + Record-Matching feature team adjudication. If acceptable, document architecturally; if not, retrofit tenant filter.

### §7.2 SemanticSearchService privilege-filter gap (W2 Cat 3 §4.3)

**Surface**: Only `RagService` mandates a privilege-group filter (AIPU2-027 fail-closed). `SemanticSearchService` filters by tenant but NOT by privilege groups.

**Question for Security team**: Intentional access-model difference (e.g., SemanticSearch operates over org-wide non-sensitive content), or security gap predating AIPU2-027?

**Recommendation**: Security team adjudication. If gap, retrofit privilege-group filter to match `RagService` pattern.

### §7.3 PrivilegeGroupResolver ADR-009 compliance (W1 Cat 4 §4.2)

**Surface**: `Security/PrivilegeGroupResolver.cs` caches per-user privileges in `IMemoryCache`. ADR-009 forbids caching authorization DECISIONS.

**Question for Security team**: Is "resolved per-user privileges" caching:
- DATA (allowed under ADR-009 — privilege lookup is reference data)
- DECISIONS (forbidden — authorization outcome is decision)

**Recommendation**: Security team adjudication. If decisions, immediate ADR-009 conformance fix; if data, document explicit ADR-009 exception with XML doc convention (per Cat 4 OrchestratorPromptBuilder gold-standard).

---

## §8 ADR Candidates (34 total, surfaced not authored — Q-005)

> Tabulating all 34 ADR candidates by priority + source wave. Per Q-005 lock: ADRs DEFERRED to follow-on phase. Bullets only. Grouped HIGH/MEDIUM/LOW.

### §8.1 HIGH-priority ADR candidates (closes LATENT BUG + structural rules)

| # | Source | ADR candidate | Cross-references |
|---|---|---|---|
| W4-1 | W4 §4.1 + Cat 6 §F.1 + W2-7 | **Endpoint↔DI Registration Conditionality Symmetry Rule (FORMAL)** — binding structural rule preventing §F.1 latent-bug pattern; recursive transitive-ctor enforcement | ADR-013; ADR-032; `bff-extensions.md` §F.1; W4-2 |
| W4-2 | W4 §4.2 + Cat 6 §7 | **§F.1 Runtime-Verifiable Detection Mechanism** — integration-test fixture probing 4 compound-gate combinations | W4-1; `bff-extensions.md` §F; integration test infra |
| W1-1 | Cat 4 + W2 Cat 1+3 cache | **BFF Canonical Cache Stack** — `IDistributedCache` only; `GetOrCreateAsync<T>` only; specialist wrappers for binary/streaming; `MemoryCache` with EXPLICIT ADR-009 exception XML doc | ADR-009; ADR-030 |
| W1-2 | Cat 6 §8 ADR-cand A | **AI Public-Contracts Facade Null-Peer Mandate** — every facade in `PublicContracts/` MUST have Null peer + `FeatureDisabledException` + `ai.<feature>.disabled` errorCode | ADR-013; ADR-032 |
| W1-3 | Cat 6 §8 ADR-cand B | **Defensive-Nullable Facade Injection Prohibited** — top-level MUST NOT scoped to facade boundary | ADR-032 §Anti-patterns |
| W1-4 | Cat 6 §8 ADR-cand C | **Zone B Endpoint Mapping → Zone A Facade Registration Symmetry** — unconditional endpoints require unconditional symmetric Null peers | ADR-013; W4-1 |
| W2-1 | Cat 1 ADR-CAND-E-01 | **Spaarke Canonical Intent Classifier Pattern** — 7-element pattern doc, NOT binding interface | W3-1; ADR-032 §F.1 |
| W2-4 | Cat 3 ADR-CAND-F-1 | **Search-Substrate Canonical Architecture** — 4-substrate stack pattern doc | W2-5; W2-6 |
| W2-5 | Cat 3 ADR-CAND-F-2 | **DI Double-Gate Null-Object Pattern** — peer to ADR-030 single-gate | ADR-030; ADR-032 |
| W2-7 | Cat 3 ADR-CAND-F-4 | **Endpoint Mapping ↔ DI Registration Symmetry Rule** (generalizes W1-4; superseded by W4-1 formal) | W4-1 |
| W3-1 | Cat 5 ADR-CAND-G-01 | **Spaarke Canonical Prompt Construction Pattern** — 4-element pattern doc, NOT binding interface | W2-1 |

### §8.2 MEDIUM-priority ADR candidates

| # | Source | ADR candidate | Cross-references |
|---|---|---|---|
| W1-5 | Cat 7 | **ActionType Central Registry + Allocation Contract** — block reservations, allocation doc, owner-per-block, deprecation policy | ADR-010 |
| W1-6 | Cat 7 | **Runtime Kill-Switch Pattern** (peer to ADR-030 DI Null-Object) — distinguishes DI kill-switch from runtime kill-switch | ADR-030 |
| W1-7 | Cat 4 | **ADR-009 Amendment**: promote `GetOrCreateAsync<T>` from opt-in to MUST | ADR-009 |
| W2-2 | Cat 1 ADR-CAND-E-02 | **BFF Tenant-Scoping → Factory-Instantiation Rule** | ADR-010 |
| W2-3 | Cat 1 ADR-CAND-E-03 | **Public API Type Naming for Result Shapes** | ADR-013 |
| W2-6 | Cat 3 ADR-CAND-F-3 | **Search-Substrate Security Model Matrix** — per-substrate filter requirements | Security team |
| W3-2 | Cat 5 ADR-CAND-G-02 | **In-process MemoryCache XML-doc convention for ADR-009 exceptions** | W1-1; ADR-009 |
| W3-3 | Cat 5 ADR-CAND-G-03 | **Prompt source co-location rule** — co-locate with sole consumer; forbid generic `/Prompts/` dirs | W3-1 |
| W4-3 | W4 §4.4 | **DI Module Audit Comment Convention** — every DI module SHOULD have inline audit table mirroring `AiModule.cs:269-313` | ADR-010 |
| W4-4 | W4 §3 §4.1 | **DI Module Per-Concern Composition Principle** — pattern doc; REJECTS forced consolidation | ADR-010 |
| W4-5 | W4 §3 §4.3 | **Canonical Options Class Design Pattern** — per-feature options with const SectionName + DataAnnotations + XML doc | inventory §4.2 |
| W4-6 | W4 §4.3 + W2-5 | **Compound AI Gate Pattern + Fine-Grained Sub-Gate Composition** — 3-tier compound gate model | ADR-018; ADR-032 |

### §8.3 LOW-priority ADR candidates

| # | Source | ADR candidate | Cross-references |
|---|---|---|---|
| W1-8 | Cat 4 | **ADR-032 Amendment**: clarify Null-Object ctor minimality (no cache deps) | ADR-032 |
| W1-9 | Cat 2 | **Lookup-Service-Per-Entity Rule** (anti-pattern doc, not full ADR) | — |
| W1-10 | Cat 2 | **Lookup-Service Typed Exceptions** | — |
| W1-11 | Cat 4 | **PrivilegeGroupResolver cache audit** (depends on §7.3 Security adjudication) | ADR-009 |
| W1-12 | Cat 6 | **Facade XML Documentation Pattern** | — |
| W1-13 | Cat 7 | **Simplify `SupportedActionType` to singular** | — |
| W1-14 | Cat 7 | **ActionType enum member lifecycle policy** | — |
| W2-8 | Cat 3 ADR-CAND-F-5 | **Shared Embedding-Cache Helper** — `IEmbeddingCache.GetOrGenerateAsync` extension | W1-1; Cat 4 |
| W3-4 | Cat 5 ADR-CAND-G-04 | **User-managed prompt template architectural layer** — codifies PromptLibrary reframe | — |
| W3-5 | Cat 5 ADR-CAND-G-05 | **Time-boxed inline prompts MUST document extraction trigger** | — |
| W4-7 | W4 §3 §4.2 | **`Sprk.Bff.Api.Options` → `Sprk.Bff.Api.Configuration` directory consolidation** — trivial directory consolidation | Inventory correction |

**Total**: 11 HIGH + 12 MEDIUM + 11 LOW = **34 ADR candidates** for the follow-on ADR phase (~2-3 weeks per audit design.md §3.1).

---

## §9 Cross-team coordination needs (Q-003 sequential)

> Per Q-003 lock: cross-team coordination is sequential, not parallel. Each team gets a packaged need + recommendation + priority.

| Team | What audit needs | What audit recommends team know | Priority |
|---|---|---|---|
| **Finance Intelligence** | Confirm DELETE of 3 lookup orphans (`ActionLookupService`, `SkillLookupService`, `ToolLookupService`) — single PR ~714 LOC | The 3 services have zero production consumers per all HARD GATES (grep, DI, publish-size) | HIGH — ungated by other waves |
| **AI Chat Playbook Builder** | Confirm DELETE of `IntentClassificationService` orphan cascade + 5th orphan `BuildPlanGenerationService.cs` (~530 LOC) + Option B EXTRACT for `PlaybookBuilderSystemPrompt.cs` | The intent-classifier orphan cluster + `BuildPlanGeneration` are dead code; live `Build(actions, skills, knowledge)` method is preserved at new path | HIGH — required before bundled DELETE PR |
| **Insights** | LATENT BUG remediation Option A (move `IInsightsAi` registration + add `NullInsightsAi`); Runtime §F.1 detection fixture authoring (~1-2 days) | LATENT BUG is HIGH severity; current integration tests mock `IInsightsAi`, masking the failure mode | HIGH |
| **AIPL** | Confirm `EmbeddingCache` + `DistributedCacheExtensions.GetOrCreateAsync<T>` as canonicals; cache adoption migration on team-owned services | 0% adoption inside `Services/Ai/`; helper already exists | MEDIUM — phased migration |
| **AIPU R1** | `OrchestratorPromptBuilder` designated canonical for two-layer cached prompts; `SessionPersistenceService` cache adoption | Cat 5 + Cat 4 designations cross-reference | MEDIUM |
| **AIPU R2** | `CapabilityManifest` cache pattern designated keep-special-case (correctness-critical) | Stateful structure, not key-value — does NOT fit generic wrapper | LOW |
| **SprkChat** | Cache adoption (8+ sites in `Chat/`); `PlaybookDispatcher` XML doc amendment (lead with tenantId rationale, ADR-010 secondary) | `PlaybookDispatcher` factory-instantiation is load-bearing for per-request tenantId, NOT just ADR-010 budget | MEDIUM |
| **Workspace** | `IBriefingAi` consumer cleanup (4 sites — remove defensive `?=null` + `RequireAi()` per ADR-032 §Anti-patterns); add `NullWorkspacePrefillAi` | Bundle with 4-facade Null-peer PR | MEDIUM |
| **Foundry** | Runtime Kill-Switch Pattern codification (`AgentServiceNodeExecutor` precedent); cache adoption | Distinct pattern from DI Null-Object | LOW |
| **Finance Intelligence (Invoice)** | Add `NullInvoiceAi` P3 Fail-Fast peer; remove `RequireAi()` defensive nullable in 3 consumer sites | Bundle with 4-facade Null-peer PR + LATENT BUG remediation | MEDIUM |
| **Records-Matching** | Add `NullRecordMatchingAi` (forward-mitigation, zero current consumers) | Preempts future §F.1 anti-pattern when consumer lands | LOW (forward-mitigation) |
| **Security** | 3 adjudication surfaces (RecordSearchService model + SemanticSearchService privilege filter + PrivilegeGroupResolver ADR-009) | See §7 above | HIGH — multi-team |
| **All teams (`ACTION-TYPE-REGISTRY.md`)** | Author allocation-tracking doc (block reservations + next-available + owner-per-block) | Preempts collision risk from parallel-project worktrees | MEDIUM (standalone PR) |

---

## §10 Follow-on phase scope (Q-005 + Q-006 DEFERRED)

### §10.1 ADR follow-on phase (Q-005 DEFERRED)

Per audit design.md §3.1 + Q-005 lock: 34 ADR candidates → ADR follow-on phase (~2-3 weeks estimated).
- HIGH priority (11 candidates): LATENT BUG remediation + Endpoint↔DI Symmetry Rule + Runtime §F.1 fixture + Facade Null-Peer Mandate + Cache Stack
- MEDIUM priority (12 candidates): pattern docs + module audit conventions + per-feature options pattern
- LOW priority (11 candidates): rules + amendments + clarifications

### §10.2 Quarterly Review Skill institutionalization (Q-006 DEFERRED)

Per Q-006 lock: quarterly skill DEFERRED to follow-on. Phase 3 surfaces the need (long-term drift-handling mechanism between audits) but does NOT author.

### §10.3 Pattern doc authoring (~600-800 LOC docs)

Phase 3 deliverable scope per W4 §5 + W3 §6:
- "Spaarke Canonical Cache Stack" (per §2.1)
- "Spaarke Canonical Intent Classifier Pattern" (per §2.5)
- "Spaarke Canonical Search Substrate Architecture" + "Double-Gate Null-Object Pattern" (per §2.6)
- "Spaarke Canonical Prompt Construction Pattern" (per §2.7)
- "Spaarke DI Module Audit Convention" (per §2.8)
- "Spaarke Canonical Options Class Design" (per §2.8)
- "Spaarke BFF Compound AI Gate" (per §2.8)
- "Spaarke Public-Contracts Facade DI Fascia" (per §2.3, post-remediation)
- "Spaarke Endpoint↔DI Symmetry Rule" (per §2.8)

**Phase 3 deliverable**: Sub-Agent K DR-### records will inform per-category authoring. Sub-Agent J migration plan will sequence the deliverables.

### §10.4 Cross-team coordination cycles

Per §9 above: cross-team coordination needs are sequential per Q-003. Owner adjudication at Phase 4 single end-of-audit review per Q-002 will package decisions for each team.

---

## §11 Open questions packaged for Q-002 single end-of-audit review

> Consolidated and deduplicated from W1+W2+W3+W4 §7 sections.

### §11.1 Confirm DELETE verdicts

1. **(Cat 2)** DELETE 3 lookup orphans + `InsightsActionRouter.cs:402-403` dangling cref + `FinanceModule.cs` DI cleanup — Finance Intelligence owner confirmation needed
2. **(Cat 1 + Cat 5)** DELETE `IntentClassificationService` orphan cascade — corrected scope ~1280 LOC including `BuildPlanGenerationService` (NEW 5th orphan). AI Chat Playbook Builder team confirmation needed
3. **(Cat 5)** Confirm Option B EXTRACT-then-DELETE for `PlaybookBuilderSystemPrompt.cs` (vs Option A PRUNE-IN-PLACE)
4. **(Cat 5)** Confirm 5th orphan `BuildPlanGenerationService.cs` (~530 LOC). AI Chat Playbook Builder team confirmation

### §11.2 Confirm KEEP / REJECT verdicts

5. **(Cat 1)** Generic `IIntentClassifier<TResult>` consolidation REJECTED. Owner accepts pattern-doc canonicalization?
6. **(Cat 3)** `PlaybookEmbeddingService ↔ SemanticSearchService` consolidation REJECTED. Owner accepts?
7. **(Cat 3)** 3 explicit DO-NOT-ADD-Null-peer for SemanticSearch + RecordSearch + PlaybookEmbedding. Owner accepts?
8. **(Cat 5)** Generic `IPromptComposer` REJECTED. Owner accepts pattern-doc?
9. **(Cat 4)** Drive adoption of `DistributedCacheExtensions.GetOrCreateAsync<T>` — promote from opt-in to MUST. Owner adjudicates: existing 30 sites migrate (multi-team multi-week) or only new sites?
10. **(W4)** REJECT module/options/compound-gate consolidation. Owner accepts per-concern split?

### §11.3 Confirm canonical reference impls (Q-004 naming lock)

11. **(Cat 4)** `EmbeddingCache` + `DistributedCacheExtensions.GetOrCreateAsync<T>` as canonical Cache Stack
12. **(Cat 1)** `InsightsIntentClassifier` as canonical Intent Classifier Pattern
13. **(Cat 3)** `RagService`/`NullRagService` as canonical Search Substrate + Double-Gate Null-Object
14. **(Cat 5)** `OrchestratorPromptBuilder` + `CapabilityClassificationPromptBuilder` as canonical Prompt Construction
15. **(W4)** "Spaarke Public-Contracts Facade DI Fascia" as load-bearing canonical name post-remediation
16. **(W4)** "Spaarke Endpoint↔DI Symmetry Rule" as load-bearing canonical name

### §11.4 LATENT BUG remediation

17. **(Cat 6 + W4)** LATENT BUG remediation prioritization: ship Option A with Phase 3 ADR landing, OR as separate URGENT remediation PR ahead of ADRs?
18. **(W4-1)** Endpoint↔DI symmetry rule: codify as standalone ADR + add to `bff-extensions.md` §F as new sub-mechanism §F.4? OR standalone ADR section?
19. **(W4-2)** Runtime §F.1 detection fixture: authorize Insights team to author (~1-2 days)?

### §11.5 Security adjudication

20. **(Security §7.1)** `RecordSearchService` tenant-isolation model — Security team + Record-Matching feature team
21. **(Security §7.2)** `SemanticSearchService` privilege-filter gap — Security team
22. **(Security §7.3)** `PrivilegeGroupResolver` ADR-009 conformance — Security team

### §11.6 Documentation / inventory corrections

23. **Inventory-correction PR**: bundle 18 corrections from §6 above
24. **Documentation bug PR**: `AnalysisServicesModule.cs:75-79` factually incorrect comment about `IInsightsAi` Null fallback (bundle with LATENT BUG remediation)
25. **`PlaybookDispatcher.cs:99-102` XML doc amendment**: lead with tenant-scoping rationale, ADR-010 budget secondary
26. **Author `ACTION-TYPE-REGISTRY.md`**: standalone PR; block reservations + next-available + owner-per-block + deprecation policy

### §11.7 Low priority

27. **(Cat 1)** Type B `IntentClassificationResult` rename — namespace disambiguation or explicit `InsightsRoutingDecision` rename?
28. **(Cat 5)** Time-boxed inline `InsightsIntentClassifier.BuildPrompt()` extraction trigger (Phase 2 multi-playbook — Insights team owns)
29. **(W4 §3 §4.2)** `Options/` → `Configuration/` directory consolidation: bundle with inventory-correction PR OR standalone tidy PR?
30. **(W4 §4.4)** Module audit-table convention: retrofit non-`AiModule` modules with `AiModule.cs:269-313`-style audit tables? Owner-team-driven

### §11.8 Owner intent question

31. **(Cat 6 §7)** Owner intent for `IFoo? = null` + `RequireAi()` pattern in 7 service-level consumers: legacy debt predating ADR-032 strengthening (track separately), or §F.1 anti-pattern (remediate now)?

---

## §12 Phase 3 → Phase 4 handoff

### §12.1 Phase 3 outputs sufficient for Q-002 owner review

This document (Sub-Agent I synthesis) covers:
- Canonical Stack naming candidates (§2)
- Per-category decisions roll-up (§3)
- LATENT BUG structural remediation (§4)
- Cross-cutting verdicts (§5)
- Inventory corrections (§6)
- Security adjudication surfaces (§7)
- 34 ADR candidates (§8)
- Cross-team coordination needs (§9)
- Follow-on phase scope (§10)
- 31 open questions packaged (§11)

**Sufficient for Phase 4 owner review when combined with**:
- Sub-Agent J's migration plan (sequencing + effort estimates + dependency graph)
- Sub-Agent K's DR-### per-category records (decision-record format for each of 8 categories)

### §12.2 Phase 4 = single end-of-audit owner review per Q-002

Per Q-002 lock, the owner review is packaged for END-OF-AUDIT, not mid-phase. Phase 3 produces the materials for that review:
- This document — Canonical Stack naming + per-category roll-up + open questions + ADR candidates
- Sub-Agent J migration plan — sequencing + effort + bundled PR scopes
- Sub-Agent K DR-### records — per-category decision-record artifacts

**Owner reads all three** → makes binding decisions on §11's 31 packaged questions → unblocks ADR follow-on phase (Q-005) + migration plan execution (cross-team coordination per Q-003).

### §12.3 Recommendations for Sub-Agents J + K

**Sub-Agent J (Migration Plan)** should use:
- §3 (Per-Category Decisions Roll-Up) for migration-PR identification
- §4 (LATENT BUG Option A) as the HIGH-priority PR #1
- §6 (Inventory Corrections) for the bundled inventory-correction PR
- §8 ADR candidates (HIGH priority subset) for ADR follow-on phase sequencing
- §9 (Cross-team coordination) for team-by-team migration-cycle sequencing
- §10 (Follow-on phase scope) for total effort estimation
- Wave summaries' §8 effort buckets (W1 §8 + W2 §8 + W3 §8 + W4 in wave-4-summary §3)

**Sub-Agent K (DR-### Records)** should produce one DR-### per category:
- DR-001 Lookup (W1 Cat 2)
- DR-002 Cache (W1 Cat 4)
- DR-003 Public Contracts Facade (W1 Cat 6) — INCLUDES LATENT BUG
- DR-004 Node Executors (W1 Cat 7)
- DR-005 Intent Classifier (W2 Cat 1)
- DR-006 Search Substrates (W2 Cat 3)
- DR-007 Prompt Construction (W3 Cat 5)
- DR-008 DI + Configuration (W4)

Each DR-### should reference the source analysis doc + this document's §2 canonical name + §8 ADR candidates + §9 cross-team owners.

---

## §13 Audit's binding constraint to itself

> Every claim in this document traces back to a Phase 2 analysis doc or wave summary. The document is a SYNTHESIS, not a NEW analyst.

| Audit principle | Honored? |
|---|---|
| Q-001 scope (Cats 1-6 + DI/Configuration) | ✅ All 8 categories covered |
| Q-002 single end-of-audit review | ✅ Packaged for Phase 4 owner review, NOT mid-phase |
| Q-003 sequential cross-team coordination | ✅ Per-team needs surfaced; no parallel demands |
| Q-004 canonical naming SURFACED (not LOCKED) | ✅ Names surfaced as candidates; owner locks at Phase 4 |
| Q-005 ADRs DEFERRED — bullets only | ✅ 34 candidates surfaced; ZERO authored |
| Q-006 Quarterly Review Skill DEFERRED | ✅ Need surfaced in §10.2; NOT authored |
| Pinned to commit 357e6936 | ✅ All findings pinned |
| Empirical-reproduction-FIRST | ✅ Source analysis docs are empirically grounded |
| Cross-sub-agent validation | ✅ §5.3 documents 7+ inventory + 3+ prior-sub-agent corrections |
| REJECT forced consolidation framing | ✅ Universal verdict applied across §2 + §5.1 |

---

*Phase 3 Sub-Agent I synthesis authored 2026-06-04. Source attribution preserved throughout; ALL claims trace back to Phase 2 analysis docs + wave summaries. Phase 4 = single end-of-audit owner review per Q-002 (this document + Sub-Agent J migration plan + Sub-Agent K DR-### records).*

---

## §14 — Phase 2.5 Cat 10 Addendum (Tool Framework Mini-Audit)

> **Authored by**: Main session integration 2026-06-04 post-Sub-Agent L + Sub-Agent M
> **Authorization**: Path B per owner direction 2026-06-04 ("go with Path B")
> **Source documents**: [`phase2/analysis-tool-framework.md`](phase2/analysis-tool-framework.md) (Sub-Agent L) + [`phase2/verification-latent-bug-2.md`](phase2/verification-latent-bug-2.md) (Sub-Agent M)

### §14.1 Cat 10 headline: TRIALITY, not duality

Inventory §2.10 framed "2 parallel tool surfaces" — empirically WRONG. Sub-Agent L identified **3 coexisting surfaces** with genuinely distinct execution contexts:

- **Layer 9 — Spaarke Canonical Chat Agent Tool Pattern** (12 `AIFunction` tools in `Chat/Tools/`, factory-instantiated per session by `SprkChatAgentFactory`)
- **Pattern-level — Playbook Tool Handler Pattern** (4 `IAiToolHandler` impls; 3 ORPHAN candidates for DELETE)
- **Layer 10 — Spaarke Canonical Analysis Tool Handler Registry** (4 `IAnalysisToolHandler` impls + `IToolHandlerRegistry` assembly-scan; canonical reference impl: `GenericAnalysisHandler`)

**Cat 10 verdict**: COEXIST (intentional). REJECT consolidation — consistent with universal Phase 2 verdict.

### §14.2 LATENT BUG #2 candidate — REFUTED by Sub-Agent M verification

Sub-Agent L (Cat 10 §4.6) hypothesized a second LATENT BUG: `IToolHandlerRegistry` registered only in compound-AI-ON branch + `HandlerEndpoints` mapped unconditionally → 500 under compound-AI-OFF.

**Sub-Agent M empirically REFUTED this hypothesis.** Sub-Agent L cited `HandlerEndpoints.cs:17` (method definition) as evidence of unconditional mapping — but that's just the method signature. The actual **call site at `EndpointMappingExtensions.cs:130` IS inside the same compound-AI gate** that gates `AddToolFramework`. Gates are symmetric by construction. Failure mode under compound-OFF: 404 (routing miss), not 500/503. Reasonable behavior.

**Methodology recommendation captured**: always cite the CALL SITE (`app.MapXxxEndpoints()`) when claiming an endpoint is mapped unconditionally — not the method definition line.

**Cross-validation success**: this is the **4th instance** of audit cross-sub-agent validation catching an error (W2 corrected W1; W3 corrected W2; W4 corrected inventory; M corrected L). Methodology validated.

### §14.3 Cat 10 per-surface decisions

| # | Surface | Verdict | Path forward |
|---|---|---|---|
| 1 | `Chat/Tools/` (12 × `AIFunction`) | **KEEP — designate Layer 9 canonical** | No action; reference impl: AIPL-053 / AIPU2-061 / AIPU2-063 patterns |
| 2 | `IAiToolHandler` (4 impls; 3 orphan) | **DOWNSIZE** | DELETE 3 orphans (`InvoiceExtractionToolHandler`, `DataverseUpdateToolHandler`, `SendCommunicationToolHandler`) — **owner pre-approved 2026-06-04**; bundle into PR #2 (~285 LOC) |
| 3 | `IAnalysisToolHandler` + `IToolHandlerRegistry` | **KEEP — designate Layer 10 canonical** | No action; reference impl: `GenericAnalysisHandler` (assembly-scan + HandlerId-keyed lookup) |

### §14.4 New canonical layers added to Spaarke Canonical AI Stack

| Layer | Name | Reference impl | ADR candidate |
|---|---|---|---|
| 9 | **Spaarke Canonical Chat Agent Tool Pattern** | `SprkChatAgentFactory.ResolveTools()` | L-2 |
| 10 | **Spaarke Canonical Analysis Tool Handler Registry** | `GenericAnalysisHandler` + `IToolHandlerRegistry` | L-3 |

Plus pattern-level (no canonical layer): **Spaarke Playbook Tool Handler Pattern** (post-DOWNSIZE).

**Total Spaarke Canonical AI Stack layers: 8 → 10** (Phase 3 § 2 layers 1-8 plus Cat 10 layers 9-10).

### §14.5 ADR candidates added (5 new — L-5 refuted)

| # | Candidate | Priority |
|---|---|---|
| L-1 | **Three-Surface Tool Framework Pattern** — codifies 3 distinct surfaces + explicit REJECT-CONSOLIDATION clause | HIGH (prevents re-litigation) |
| L-2 | **Chat-Tool Factory-Instantiation Pattern** | MEDIUM |
| L-3 | **Analysis Tool Handler Registry Pattern** | MEDIUM |
| L-4 | **`IAiToolHandler` Orphan-Impl Mitigation Convention** | MEDIUM |
| ~~L-5~~ | ~~`IToolHandlerRegistry` Symmetry Rule application (LATENT BUG candidate)~~ | **REFUTED — removed** |
| L-6 | **Tool-Surface Directory Naming Rule** — no mixed-surface directories | LOW |

**Total ADR candidates: 34 → 39** (5 added; L-5 not counted).

### §14.6 Inventory accuracy corrections (4 new — added to §6 PR #3 scope)

| Inventory error | Empirical correction |
|---|---|
| `Chat/Tools/` count "10 AIFunction tools" | **12** AIFunction tools |
| `Tools/` "5 IAiToolHandler impls" | **MIXED** directory: 2 `IAiToolHandler` + 3 `IAnalysisToolHandler` + 1 helper |
| `IAiToolHandler` total impls "5" | **4** (`FinancialCalculationToolHandler`, `InvoiceExtractionToolHandler`, `DataverseUpdateToolHandler`, `SendCommunicationToolHandler`) |
| "Two parallel surfaces" framing | **"Three coexisting surfaces"** (TRIALITY) |

**Total inventory drift findings: 18 → 22** (4 added).

### §14.7 Bundled DELETE PR scope update

Per owner pre-approval 2026-06-04 ("we no additional team confirmation required-- proceed"):

- W1+W2+W3 bundled DELETE: ~2000 LOC
- Cat 10 §2.4 orphan tool handlers added: ~285 LOC
  - `InvoiceExtractionToolHandler` + interface
  - `DataverseUpdateToolHandler` + interface
  - `SendCommunicationToolHandler` + interface (concrete-singleton registration too)
  - DI line removals in `FinanceModule.cs:176, 181` + `CommunicationModule.cs:30`
- **Updated bundled DELETE total: ~2285 LOC**

### §14.8 Phase 2.5 deliverables (additions to Phase 3)

| File | Lines | Author |
|---|---|---|
| `notes/phase2/analysis-tool-framework.md` | 250+ | Sub-Agent L |
| `notes/phase2/verification-latent-bug-2.md` | ~150 | Sub-Agent M (verification) |
| `notes/canonical-architecture-decisions.md` §14 (this section) | ~150 | Main session integration |

### §14.9 Phase 4 readiness — RECONFIRMED

All Phase 3 + Phase 2.5 deliverables now complete. Phase 4 = single end-of-audit owner review per Q-002. Owner has already pre-approved:
- PR #1: LATENT BUG #1 fix (~280 LOC) — LATENT BUG #2 refuted; no bundle needed; PR #1 stands alone
- PR #2: Bundled DELETE (~2285 LOC) — fast-tracked, no cross-team gates
- PR #3: Retroactive doc corrections (22 items including Cat 10's 4)
- PR #9: `docs/architecture/` + `docs/guides/` + `.claude/patterns/` updates

---

*Phase 2.5 Cat 10 addendum integrated 2026-06-04 by main session. Path B authorization closed; audit ships with 8 + 2 canonical layers + 39 ADR candidates + 22 inventory corrections + 1 confirmed LATENT BUG + 1 refuted LATENT BUG hypothesis (methodology validation).*
