# Phase 2 Analysis — DI + Configuration Patterns (Q-001 expansion)

> **Authored by**: Phase 2 W4 Sub-Agent H
> **Pinned to**: commit `357e6936` (Phase 1 inventory snapshot)
> **HEAD at analysis time**: master HEAD as of 2026-06-04 (W1+W2+W3 docs-only merges; ZERO code drift in DI / Configuration / Options surfaces)
> **Scope boundary**: DI registration + configuration pattern decisions ONLY. Out-of-scope: per-category service decisions (W1+W2+W3 LOCKED), ADR-010 budget cap revision, options value/default/validator changes, section-name renaming, cross-team roadmap decisions, creating new DI modules / options classes.

---

## §1 Phase 1 baseline (verbatim from inventory §3 + §4 + §7.8)

### §1.1 Inventory §3.1 — Module inventory ("34 DI modules" — CORRECTED to 31 below)
Verbatim: "34 DI modules. AiModule (15/15 unconditional cap per ADR-010), AiCapabilitiesModule, AiChatModule, AiPersistenceModule, AiSafetyModule, **AnalysisServicesModule (CENTRAL HUB)**, InsightsExtractionModule, InsightsFacadeModule, InsightsIngestModule, InsightsModule, FinanceModule, WorkspaceModule, AgentModule, CacheModule, ConfigurationModule, GraphModule, JobProcessingModule, WorkersModule, RegistrationModule, etc."

### §1.2 Inventory §3.2 — Registration pattern taxonomy (6 patterns)
Verbatim: "(1) Unconditional Singleton/Scoped; (2) Compound-gated (`Analysis:Enabled && DocumentIntelligence:Enabled`); (3) Fine-grained gated (extra `if (option.Enabled)` inside compound); (4) AI-Search-keys sub-gate; (5) Null-Object dual registration (ADR-032); (6) Factory-instantiated (NOT registered) per ADR-010 budget."

### §1.3 Inventory §3.3 — ADR-010 budget commentary
Verbatim: "`AiModule.cs` explicitly tracks '15/15 unconditional cap'. Several services pushed to `AnalysisServicesModule` to keep `AiModule` under cap. Cap-management is robust but adds cognitive load."

### §1.4 Inventory §3.4 — Asymmetric registration audit
Verbatim: "Every conditional registration has an 'ADR-032 §F.1 inspection' comment block. This is excellent rigor — and a symptom of how many feature gates exist."

### §1.5 Inventory §4.1 — 27/35 options classes
Verbatim: "`Configuration/` (25 files; namespace `Sprk.Bff.Api.Configuration`) + 2 in `Options/` (namespace `Sprk.Bff.Api.Options`) + 6 inline in `Services/Ai/` = **35 options classes** total."

### §1.6 Inventory §4.2 — 4 binding patterns
Verbatim: "(1) `AddOptions<T>().BindConfiguration(...)` — newest canonical; (2) `AddOptions<T>().Bind(GetSection(...))` — variant with explicit IConfiguration; (3) `services.Configure<T>(GetSection(...))` — older pattern, no chaining; (4) Direct `configuration.GetValue<bool>(...)` at startup — bypasses IOptions entirely."

### §1.7 Inventory §4.3 — Configuration namespace split (CORRECTED — see §2.3 below)
Verbatim: "`Sprk.Bff.Api.Configuration` (25 classes) vs `Sprk.Bff.Api.Options` (2 classes) — **redundant namespace split** with no clear delineating principle."

### §1.8 Inventory §4.4 — Section name patterns
Verbatim: "Inconsistent: `Insights:IntentClassifier` (nested), `Capabilities` (flat), `AzureOpenAI` (top-level). Some keys read directly via `configuration[\"AzureOpenAI:Endpoint\"]` bypassing `IOptions<T>`."

### §1.9 Inventory §7.8 — 4 open questions (Q-001 expansion for W4)
1. Module count creeping (34 modules, 6 AI-flavored). Consolidate vs per-concern good practice?
2. `Configuration/` vs `Options/` redundant namespace split — recommend consolidation.
3. 35 options classes — pattern for "feature manifest" collapse?
4. Compound AI gate complexity — evaluate ADR-032 Null-Object + per-service fine-grained gates as replacement.

---

## §2 Empirical reproduction at HEAD (drift correction CRITICAL)

### §2.1 Drift verification
`git log --oneline 357e6936..HEAD -- src/server/api/Sprk.Bff.Api/Infrastructure/DI/ src/server/api/Sprk.Bff.Api/Configuration/ src/server/api/Sprk.Bff.Api/Options/` returns ONLY the W1+W2+W3 docs-only merges. **ZERO code drift in DI / Configuration / Options surfaces between snapshot `357e6936` and HEAD.**

### §2.2 Module count correction — INVENTORY HAD AN ERROR
`git ls-tree -r --name-only 357e6936 -- src/server/api/Sprk.Bff.Api/ | grep Module.cs` = **31 files**. Not 34. The 31 breakdown:

| Location | Count | Files |
|---|---|---|
| `Infrastructure/DI/` | 29 | Agent, AiCapabilities, AiChat, AiModule, AiPersistence, AiSafety, AnalysisServices, Authorization, Cache, Communication, Configuration, Cors, Documents, EmailServices, ExternalAccess, Finance, Graph, InsightsExtraction, InsightsFacade, InsightsIngest, InsightsModule, JobProcessing, Office, RateLimiting, Registration, SpeAdmin, Telemetry, Workers, Workspace |
| `Api/Reporting/` | 1 | ReportingModule (Power BI; registered via `MapPowerBiReports().AddReportingServices()`) |
| `Workers/Office/` | 1 | OfficeWorkersModule (Office worker registration helper) |

**6 AI-flavored modules** + **4 Insights-flavored**: AI/Insights = 10 of 31 (32%).

### §2.3 Options class count correction — INVENTORY OVERCOUNTED `Configuration/` BY 4

Empirical count at snapshot (verified at HEAD — zero drift):

| Location | Count | Namespace | Files |
|---|---|---|---|
| `Configuration/` | **21** (NOT 25) | `Sprk.Bff.Api.Configuration` | AgentToken, AiSearchResilience, Analysis, AssistantCitationHref, Communication, Dataverse, DemoProvisioning, DocumentIntelligence, EmailProcessing, Finance, Graph, GraphResilience, InsightsIntentClassifier, OfficeRateLimit, Redis, Reindexing, ServiceBus, SharePointEmbedded, SpeAdmin, ToolFramework, Workspace |
| `Options/` | **2** | `Sprk.Bff.Api.Configuration` (NOT `Sprk.Bff.Api.Options` — even MORE misleading than inventory said) | AiSearch, LlamaParse |
| `Services/Ai/Capabilities/` | 2 | `Sprk.Bff.Api.Services.Ai.Capabilities` | CapabilityRouter, ManifestRefresh |
| `Services/Ai/Foundry/` | 3 | `Sprk.Bff.Api.Services.Ai.Foundry` | AgentService, BingGrounding, CodeInterpreter |
| `Services/Ai/Insights/Extraction/` | 1 | `Sprk.Bff.Api.Services.Ai.Insights.Extraction` | ConfidenceThreshold |
| `Services/Insights/Observations/` | 1 | `Sprk.Bff.Api.Services.Insights.Observations` | InsightsMirror |
| `Services/Insights/LiveFacts/` | 1 | `Sprk.Bff.Api.Services.Insights.LiveFacts` | SubjectSchemeCatalog |
| `Api/Insights/` | 1 | `Sprk.Bff.Api.Api.Insights` | InsightsPlaybookNameMap |
| `Api/Reporting/` | 1 | `Sprk.Bff.Api.Api.Reporting` | PowerBi |
| Cross-cutting/inline | 2 | various | `ApiKeyAuthenticationOptions`, `ModelSelectorOptions` |
| **Total** | **35** (matches inventory headline) | — | — |

**Inventory §4.3 was structurally wrong**: the `Options/` directory has 2 files BOTH using `Sprk.Bff.Api.Configuration` namespace. There is **NO `Sprk.Bff.Api.Options` namespace in use**. The "redundant namespace split" framing should be **"redundant directory split with single namespace"** — even cleaner consolidation.

### §2.4 Binding pattern distribution

| Pattern | Approximate count | Comment |
|---|---|---|
| `AddOptions<T>().BindConfiguration(...)` newest canonical | ~8 | Used in newer feature modules; allows fluent `ValidateDataAnnotations().ValidateOnStart()`. |
| `AddOptions<T>().Bind(GetSection(...))` explicit-IConfig | ~12 (entire ConfigurationModule + ManifestRefresh + CapabilityRouter) | Functionally identical to BindConfiguration. |
| `services.Configure<T>(GetSection(...))` older | ~2 (AnalysisOptions @ AnalysisServicesModule:275; ToolFrameworkOptions @ :585) | No chaining — legacy. |
| Direct `configuration.GetValue<bool>(...)` at startup (BYPASSES IOptions) | ~4 (AnalysisServicesModule:22 `DocumentIntelligence:Enabled`; :43 `Analysis:Enabled`; :517 `Insights:IntentClassifier:Enabled`; :594 `RecordMatchingEnabled`) | **Justified inline**: registration choice fixed at startup; IOptions reload wouldn't switch the registered type. |
| Direct `configuration["..."]` string read | ~3 (AiModule:92-93/112 AzureOpenAI keys; AnalysisServicesModule:382-383 Blob conn) | No options class wraps these AzureOpenAI/Blob settings. |

### §2.5 Compound gate empirical inspection (AnalysisServicesModule.cs:18-117)

Verified at HEAD:
- Line 22: `documentIntelligenceEnabled = configuration.GetValue<bool>("DocumentIntelligence:Enabled")`
- Line 23-31: if-block registers AiTelemetry + OpenAiClient + IOpenAiClient + TextExtractorService + ITextExtractor (5 services).
- Line 32-41: else-block registers `NullTextExtractor` (ADR-032 P3).
- Line 43: `analysisEnabled = configuration.GetValue<bool>("Analysis:Enabled", true)`
- Line 44-90: compound `if (analysisEnabled && documentIntelligenceEnabled)` — 11 helper-method calls + 2 direct registrations.
- Line 91-102: two else-branches; BOTH call `AddNullObjectsForCompoundOff` (10 Null peers).
- Line 104: `AddRecordMatchingServices` runs OUTSIDE (separate gate).
- Line 114: `AddUnconditionalChatAndNotificationServices` (8 services) runs ALWAYS.

**Registration pattern empirical map**:

| Pattern | Surveyed count |
|---|---|
| Unconditional Singleton/Scoped | ~30+ (across AiCapabilitiesModule, InsightsFacadeModule, InsightsModule, InsightsExtractionModule, AnalysisServicesModule.AddUnconditionalChatAndNotificationServices) |
| Compound-gated | ~15 services across 11 helper methods (AnalysisServicesModule:44-90 only) |
| Fine-grained gated inside compound | 1 (`AddInsightsIntentClassifier:517` checks `Insights:IntentClassifier:Enabled`) |
| AI-Search-keys sub-gate | 1 (`AddRagServices:539` checks `AiSearchEndpoint/Key`) |
| Null-Object dual registration | 10 (NullBriefingAi, NullPlaybookOrchestrationService, NullPlaybookService, NullRagService, NullVisualizationService, NullFileIndexingService, NullSprkChatAgentFactory, NullPendingPlanManager, NullInsightsIntentClassifier, NullTextExtractor) |
| Factory-instantiated (NOT in DI) | 5+ (PlaybookDispatcher, PlaybookEmbeddingService, CompoundIntentDetector, CapabilityClassificationPromptBuilder static, PlaybookBuilderSystemPrompt static) |

---

## §3 Per-question decision table (6 sub-questions from brief §4)

| # | Question | Verdict | Rationale | Action |
|---|---|---|---|---|
| §4.1 | Should 34 (CORRECTED: 31) DI modules consolidate? | **KEEP per-concern. REJECT forced consolidation.** | (a) Empirical module-size distribution: 5 of 29 DI/-housed modules have ≤3 `services.Add` calls (InsightsExtraction=2, AiChat=3, CacheModule=4, InsightsFacade=2, AiPersistence=8) — but ALL FIVE represent distinct architectural boundaries (Zone A extraction, R2 agent boundary, Redis infra, Zone A→B facade, Cosmos persistence). Per-concern split is structurally valid. (b) The 4-pattern W2+W3 verdict (Cat 1, Cat 3, Cat 5 all REJECT forced consolidation) applies here too — modules are domain boundaries, not interface abstractions. (c) `AnalysisServicesModule` at ~600 LOC is the only "fat module"; consolidating SMALL modules INTO it would WORSEN the central-hub problem. (d) The 31-module total is reasonable for a unified BFF serving 6 client surfaces + 11 AI subcategories. **No module deletion or consolidation recommended.** | Pattern doc: "Spaarke Canonical DI Module Composition" (per-concern split criterion). Surface as ADR candidate. |
| §4.2 | `Configuration/` (21) vs `Options/` (2) — CORRECTED: BOTH use `Sprk.Bff.Api.Configuration` namespace. | **CONSOLIDATE — single `Configuration/` directory + single namespace (already true).** | The "Options/" directory is purely cosmetic temporal residue. Both files ALREADY use the `Sprk.Bff.Api.Configuration` namespace — there is NO actual namespace split today. Inventory §4.3 framing was wrong: the issue is **directory location inconsistency**, not namespace inconsistency. W2 Cat 3 §4.8 verified `RagService` injects from BOTH directories but ONE namespace. Cost: 2 file moves. | Trivial-PR: `git mv` `AiSearchOptions.cs` + `LlamaParseOptions.cs` to `Configuration/`. Delete empty `Options/` dir. Bundle with inventory-correction PR. **No namespace changes / no consumer changes**. |
| §4.3 | 35 options classes — feature manifest collapse? | **KEEP per-feature options classes. REJECT manifest collapse.** | Sampled 5 options classes: each has distinct (a) `SectionName` const (binding boundary), (b) per-field defaults + DataAnnotations validation, (c) per-options XML-doc rationale citing originating task/SPEC/POML, (d) ownership boundary (`InsightsIntentClassifier` is Insights team; `AssistantCitationHref` is wave-F; `ConfidenceThreshold` is Admin-tunable D-63; `AiSearch` is infra; `LlamaParse` is parser-vendor). Collapsing into `InsightsOptions { IntentClassifier {}, CitationHref {}, Extraction { Thresholds {} } }` would (i) lose per-options XML-doc anchoring, (ii) couple team-ownership boundaries, (iii) force longer consumer call paths, (iv) break IOptions hot-reload granularity. **Same pattern as W2+W3 verdicts.** | Pattern doc: "Spaarke Canonical Options Class Design" — per-feature with const SectionName + DataAnnotations + XML-doc citing source task + safe defaults. ADR candidate. |
| §4.4 | Compound AI gate — KEEP or RESTRUCTURE? | **KEEP compound gate + ADD structural rule (W2 Cat 3 §F.1 generalization).** | The compound gate is the central decision point for ~15 services. Restructuring to per-service fine-grained gates would (a) multiply gate count from 2 to ~15+ per-service flags — vastly worse operational cognitive load; (b) lose "AI-OFF deployment shape" (today: 2 flags off = entire AI stack inert with 10 Null peers); (c) require per-service Null peer registration for every service today batch-handled by `AddNullObjectsForCompoundOff` — quadratic complexity growth. The compound gate's complexity is INHERENT to AI feature gating, not accidental. **BUT** the gate complexity DOES enable the §F.1 latent bug — structural fix is NOT removing the gate, but enforcing symmetry: ANY service registered behind the gate that is consumed by an UNCONDITIONALLY-mapped endpoint MUST also have a compound-OFF Null peer registration. | KEEP gate. ADD structural rule (see §4.1 below). ADR candidate. |
| §4.5 | LATENT BUG structural remediation: `InsightsFacadeModule` unconditional vs `AddPublicContractsFacade` compound-gated split. | **STRUCTURAL FIX: migrate `IInsightsAi` registration into `AddPublicContractsFacade` + add `NullInsightsAi` to `AddNullObjectsForCompoundOff`.** | Verified at HEAD: `Program.cs:104` calls `AddInsightsFacadeModule()` UNCONDITIONALLY. `InsightsFacadeModule.cs:95+105` registers `IPlaybookExecutionEngine` + `IInsightsAi → InsightsOrchestrator` Scoped. **`InsightsOrchestrator` ctor (`InsightsOrchestrator.cs:109-129`) takes 8 ctor params, of which 7 are conditional**: `IInsightsPlaybookExecutionCache` (compound-ON only), `IOpenAiClient` (compound-ON only @ :27), `IPlaybookOrchestrationService` (Null peer exists @ :217), `IIngestDocumentSource` (InsightsIngestModule), `IOptionsMonitor<InsightsPlaybookNameMapOptions>` (Options pattern — always works), `IRagService` (Null peer exists @ :223), `AssistantToolCallHandler` (Scoped @ AnalysisServicesModule:80 — ONLY in compound-ON branch — **NO Null peer**). Cat 6 LATENT BUG CONFIRMED at HEAD. **Option A RECOMMENDED**: move `services.AddScoped<IInsightsAi, InsightsOrchestrator>()` from `InsightsFacadeModule` to `AnalysisServicesModule.AddPublicContractsFacade()` (symmetric with other 4 facades); move `IPlaybookExecutionEngine` registration into the same compound-ON helper; add `NullInsightsAi` to `AddNullObjectsForCompoundOff`. | **HIGH PRIORITY remediation PR**. Four sub-changes: (a) move `IInsightsAi` + `IPlaybookExecutionEngine` to `AddPublicContractsFacade`; (b) add `NullInsightsAi` impl (~100 LOC, 6 methods throwing `FeatureDisabledException`); (c) register `NullInsightsAi` in `AddNullObjectsForCompoundOff`; (d) FIX misleading comment at `AnalysisServicesModule.cs:75-79`. Add integration test that flips `DocumentIntelligence:Enabled=false` and asserts 503 (not 500) from `/api/insights/ask` + `/api/insights/search` + `/api/insights/assistant/query`. |
| §4.6 | ADR-010 budget cap robustness | **KEEP cap mechanism. MILD improvement: explicit per-module audit-table comment.** | `AiModule.cs:269-313` has the gold-standard inline audit (15/15 budget with each registration line-numbered + task-tagged). `AiCapabilitiesModule` does NOT have an equivalent audit table (8+ services registered, no cap accounting). `AnalysisServicesModule` doesn't track per-helper-method registration count. The cap mechanism WORKS (held the line at AiModule via 5-service promotion to `AddUnconditionalChatAndNotificationServices` per D-09 §2 B4/B5/L5, 2026-06-01). BUT discipline relies on developer-memory for non-AiModule modules. The compound-gate complexity hides cap headroom: Tier 1 + Tier 1.5 promoted services are excluded from any cap accounting. | Pattern doc: "DI Module Registration Audit Comment Convention" — every DI module SHOULD have an inline audit table mirroring `AiModule.cs:269-313` format. ADR candidate. **NO cap value change** (out of scope). |

---

## §4 Cross-cutting findings

### §4.1 LATENT BUG structural root cause + remediation pattern (highest-leverage finding)

The W1 Cat 6 LATENT BUG is the canonical instance of an **endpoint-mapping ↔ DI-registration asymmetry**. Structural pattern:

```
[UNCONDITIONALLY-MAPPED ENDPOINT] →injects→ [UNCONDITIONALLY-REGISTERED FACADE] →ctor-depends-on→ [CONDITIONALLY-REGISTERED SERVICE]
                ✓                                       ✗ (asymmetric)                                ✗
```

The third arrow is the bug surface: `InsightsOrchestrator` ctor demands services that may not exist. Metadata-gen at startup checks signature-resolvability (param-inference) but NOT transitive ctor-resolvability. The runtime failure surfaces as `InvalidOperationException("Unable to resolve...")` instead of `FeatureDisabledException → 503`.

**Generalized structural rule (proposed ADR-candidate W4-1)**:

> **Endpoint Mapping ↔ DI Registration Conditionality Symmetry Rule**:
> Any service registered behind a feature flag (compound AI gate, fine-grained option flag, AI-Search-keys sub-gate) MUST satisfy one of these conditions:
> 1. Its consumer endpoint is mapped behind the SAME feature flag (symmetric conditionality), OR
> 2. A Null-Object peer is registered in the gate-OFF branch with EXACTLY the same interface type + lifetime + same `ServiceDescriptor.ServiceType` (ADR-032 P3 Fail-Fast), AND every TRANSITIVE ctor dependency of the real impl satisfies the same rule, AND a startup integration test exists that asserts gate-OFF state returns 503 not 500.
>
> The rule applies recursively through the service-resolution chain — if facade `F` is registered unconditionally but its ctor depends on conditional service `S`, then EITHER `S` must have a Null peer OR `F`'s registration must move into the conditional branch.

### §4.2 §F.1 should be runtime-verifiable, NOT comment-block-verifiable

The current `.claude/constraints/bff-extensions.md` §F.1 enforcement relies on PR reviewers reading code-comment blocks and running a static-scan recipe. **Empirical failure**: every conditional registration in `AnalysisServicesModule.cs` AND `InsightsModule.cs:82-91` has the comment-block discipline applied. The `IInsightsAi` LATENT BUG slipped through anyway because the bug is at the TRANSITIVE ctor-resolution layer, not at the immediate registration site. Comment-block discipline is inadequate.

**Recommended remediation pattern (ADR-candidate W4-2)**:

> **§F.1 anti-pattern detection MUST include runtime verification**:
> 1. A reusable integration-test fixture (xUnit `IClassFixture` or `WebApplicationFactory<Program>` subclass) that bootstraps the BFF with each compound gate independently flipped to OFF (4 combinations: both ON, DocumentIntelligence-OFF only, Analysis-OFF only, both OFF).
> 2. For each combination, the fixture probes every unconditionally-mapped endpoint registered in `EndpointMappingExtensions.cs` with a synthetic request shape triggering each handler's hot path.
> 3. The test asserts NO handler returns 500 with body containing "Unable to resolve" — the only acceptable failure modes under gate-OFF are (a) 503 ProblemDetails with stable errorCode `ai.*.disabled`, or (b) 200 OK with degraded payload.

### §4.3 Compound gate is the right structural choice — DO NOT distribute

**W4 verdict: NO** to distributing the compound gate per-service.
1. **Operational simplicity**: 2 compound flags vs ~15+ per-service flags.
2. **Forward-mitigation already works via fine-grained sub-gates**: the existing pattern (`Insights:IntentClassifier:Enabled` @ AnalysisServicesModule:508-534) is the fine-gate INSIDE the compound-ON branch — 2-level control.
3. **AI-Search-keys sub-gate is a working compound-tier-3** (`AddRagServices`): 3-tier gating (compound → fine-grained → resource-prerequisite) works cleanly.
4. **The compound gate is NOT the bug source**: the bug is asymmetric registration symmetry violations, not the gate itself.

### §4.4 ADR-010 budget mechanism is robust but should generalize

`AiModule.cs:269-313`'s inline audit table is gold-standard. None of the other AI-flavored modules has an equivalent. Recommend ADR-candidate W4-3 (audit-table convention).

### §4.5 Cross-Wave LATENT BUG verification convergence

W2 Cat 1 §2.5 verified `NullInsightsIntentClassifier` ctor takes only `ILogger<T>` (clean). W2 Cat 3 §4.2 verified `NullRagService` double-gate pattern is gold-standard. W3 Cat 5 confirmed `OrchestratorPromptBuilder` Singleton is gold-standard for ADR-009 in-process cache exception. **All three independent verifications converge: the LATENT BUG is at the facade-registration layer, NOT downstream in the classifier/search/prompt layers.** W4's structural remediation is consistent with this convergent finding.

### §4.6 Inventory corrections aggregated into W4

1. **Module count 34 → 31** (inventory §3.1 overcounted by 3).
2. **`Sprk.Bff.Api.Options` namespace does NOT exist** (inventory §4.3 framing error — `Options/` directory uses `Sprk.Bff.Api.Configuration` namespace).
3. **`Configuration/` directory count 25 → 21** (inventory §4.1 per-dir error). Total of 35 options classes is correct in inventory headline despite per-dir errors.

---

## §5 Canonical naming candidates (Q-004 framing — descriptive only)

Per Q-004 lock — W4 surfaces candidates but does NOT lock:

- **"Spaarke DI Module Composition"** — descriptive name for the 31-module per-concern split pattern.
- **"Spaarke Canonical Options Class Design"** — descriptive name for the per-feature options class pattern.
- **"Spaarke BFF Compound AI Gate"** — descriptive name for the compound-gate pattern.
- **"Spaarke Public-Contracts Facade DI Fascia"** — descriptive name for the unified `AddPublicContractsFacade` + `AddNullObjectsForCompoundOff` + `AddInsightsFacadeModule` layer (once LATENT BUG remediation lands).
- **"Spaarke Endpoint↔DI Symmetry Rule"** — descriptive name for the §4.1 binding rule.
- **"Spaarke DI Module Audit Convention"** — descriptive name for the `AiModule.cs:269-313` audit-table pattern.

**Recommendation for Phase 3**: adopt "Spaarke Public-Contracts Facade DI Fascia" + "Spaarke Endpoint↔DI Symmetry Rule" as the load-bearing canonical-stack names (these are the two new patterns). The other 4 names are descriptive overlays on existing patterns.

---

## §6 Drift report (snapshot 357e6936 vs HEAD)

Verified via `git log --oneline 357e6936..HEAD -- DI/ Configuration/ Options/` — returns ONLY docs-only merges. **ZERO code drift.**

All §3 + §4 findings valid at HEAD. The `IInsightsAi` LATENT BUG is still present at HEAD (`InsightsFacadeModule.cs:86-108` unchanged; `AnalysisServicesModule.cs:74-79` misleading comment unchanged; `AddPublicContractsFacade:357-363` still has only 4 facades).

**Inventory drift findings (not code drift)** documented in §4.6.

---

## §7 Open questions for owner review (Q-002 single end-of-audit)

1. **LATENT BUG remediation prioritization**: ship Option A remediation with Phase 3 ADR landing, OR as separate URGENT remediation PR ahead of ADRs?
2. **Endpoint↔DI symmetry rule** (W4-1): codify as standalone ADR + add to `bff-extensions.md` §F as new sub-mechanism §F.4? OR standalone ADR section?
3. **Runtime §F.1 detection test** (W4-2): authorize Insights team to author the runtime probe fixture (~1-2 days estimated for fixture + test scaffolding)?
4. **`Options/` → `Configuration/` directory consolidation** (§3 §4.2): trivial `git mv` — bundle with broader Phase 3 inventory-correction PR OR standalone tidy PR?
5. **Module audit-table convention** (W4-3): retrofit `AiCapabilitiesModule` / `AnalysisServicesModule` / others with `AiModule`-style audit tables? Owner-team-driven (added in next feature change touching each module).

---

## §8 ADR candidates (per Q-005 — bullets only, deferred)

| # | ADR candidate | Source | Priority | Cross-references |
|---|---|---|---|---|
| W4-1 | **Endpoint↔DI Registration Conditionality Symmetry Rule** — binding structural rule preventing the §F.1 latent-bug pattern | W4 §4.1 + Cat 6 §F.1 + W2 Cat 3 ADR-CAND-F-4 generalization | **HIGH** (closes LATENT BUG structural root cause) | ADR-013; ADR-032; `bff-extensions.md` §F.1; W4-2 |
| W4-2 | **§F.1 Runtime-Verifiable Detection Mechanism** — integration-test fixture probing 4 compound-gate combinations | W4 §4.2 + Cat 6 §7 URGENT verification | **HIGH** (operationalizes W4-1) | W4-1; `bff-extensions.md` §F; `tests/integration/Sprk.Bff.Api.IntegrationTests/` |
| W4-3 | **DI Module Audit Comment Convention** — every DI module SHOULD have inline audit table mirroring `AiModule.cs:269-313` | W4 §4.4 | **MEDIUM** | ADR-010; W2-G2 |
| W4-4 | **DI Module Per-Concern Composition Principle** — pattern doc explaining per-concern split criterion; REJECTS forced consolidation | W4 §3 §4.1 | **MEDIUM** | ADR-010 §Module pattern; W2/W3 REJECT-consolidation framing |
| W4-5 | **Canonical Options Class Design Pattern** — pattern doc for per-feature options class with const SectionName + DataAnnotations + XML-doc | W4 §3 §4.3 | **MEDIUM** | inventory §4.2; W2-Cat3 §4.8 |
| W4-6 | **Compound AI Gate Pattern + Fine-Grained Sub-Gate Composition** — pattern doc explaining 3-tier compound gate model | W4 §4.3 + W2 Cat 3 §4.2 (RagService double-gate) | **MEDIUM** | ADR-018; ADR-032 |
| W4-7 | **`Sprk.Bff.Api.Options` → `Sprk.Bff.Api.Configuration` directory consolidation** — trivial directory consolidation | W4 §3 §4.2 + inventory §4.3 correction | **LOW** | Bundle with inventory-correction PR |

**Total W1+W2+W3+W4 ADR candidates surfaced**: 14 (W1) + 8 (W2) + 5 (W3) + 7 (W4) = **34 ADR candidates** for the follow-on ADR phase.

---

# Sub-Agent H Final Status Report

1. **Status**: COMPLETE
2. **Output file path**: `projects/bff-ai-architecture-audit-r1/notes/phase2/analysis-di-configuration.md`
3. **Modules + options classes analyzed at HEAD**:
   - **31 DI modules** (CORRECTED from inventory's 34): 29 in `Infrastructure/DI/`, 1 in `Api/Reporting/`, 1 in `Workers/Office/`
   - **35 options classes** (matches inventory headline; per-directory breakdown corrected — 21 in `Configuration/`, 2 in `Options/` (both `Sprk.Bff.Api.Configuration` namespace), 12 inline)
4. **Decision distribution**:
   - KEEP per-concern (REJECT forced consolidation): 31 modules, 35 options classes, compound gate, ADR-010 cap mechanism
   - CONSOLIDATE: 1 (`Options/` → `Configuration/` directory)
   - RESTRUCTURE (HIGH PRIORITY): 1 (`IInsightsAi` LATENT BUG remediation — Option A)
   - RECOMMEND-RULE: 1 (Endpoint↔DI symmetry rule)
   - RECOMMEND-TEST: 1 (Runtime §F.1 detection fixture)
   - RECOMMEND-PATTERN-DOC: 6
5. **Drift findings**:
   - **CODE drift**: ZERO.
   - **INVENTORY drift** (corrections): Module count 34 → 31; `Sprk.Bff.Api.Options` namespace does NOT exist (single namespace; directory split only); `Configuration/` per-dir count 25 → 21.
   - **LATENT BUG status**: CONFIRMED at HEAD.
6. **Cross-cutting observations**: see §4 above. Headline — LATENT BUG structural root cause is asymmetric DI registration topology; remediation pattern: Option A (move + add `NullInsightsAi`). §F.1 anti-pattern detection MUST become runtime-verifiable. REJECT-consolidation verdict extends to modules/options/compound-gate. Compound gate's complexity is INHERENT, not accidental. `Sprk.Bff.Api.Options` namespace does NOT exist — trivial directory consolidation.
7. **Open questions for owner**: 5 questions (see §7 above).
8. **Phase 3 dispatch readiness**: **✅ CONFIRMED** — all W1+W2+W3+W4 outputs sufficient for Phase 3 (Canonical Stack naming + migration plan + Phase 4 review). Canonical stack: 4 reference impls + 2 new W4 canonical patterns. Migration plan inputs: bundled orphan DELETE PR (~2000 LOC), bundled facade Null-peer PR (~180 LOC) including LATENT BUG fix per Option A, bundled inventory-correction PR including `Options/` directory consolidation, 6 pattern docs + 7 W4 ADR candidates. Runtime §F.1 fixture (W4-2) — Insights team owns. Methodology: empirical-reproduction-FIRST + cross-sub-agent validation worked (W2+W3+W4 corrected 4+ inventory/prior-sub-agent errors).

**Phase 2 is COMPLETE.** Phase 3 (Canonical Stack naming + migration plan + Phase 4 review) is unblocked.
