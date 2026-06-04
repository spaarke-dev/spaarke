# Phase 2 Analysis — Category 6: Public Contracts Facade

> **Authored by**: Phase 2 W1 Sub-Agent C
> **Pinned to**: commit `357e6936` (inventory snapshot)
> **HEAD at analysis time**: `12275b10` (ZERO drift in `Services/Ai/PublicContracts/`, `InsightsFacadeModule.cs`, or `AnalysisServicesModule.cs` — verified)
> **Scope boundary**: facade boundary integrity + Null-peer gap closure analysis only. Facade consolidation deferred to W2 (Cat 1 + Cat 3 dependent).

---

## §1 Phase 1 baseline (verbatim from inventory §2.6 + §3.4 + §7.6)

Inventory §2.6 table of 5 facades + `IObservationMirror`, with claim "All 5 facades active. 1 has Null peer (`NullBriefingAi`); the other 4 do NOT — which means consumers can't fall back gracefully if facade dependencies are gated off (a structural gap relative to the §2.4 Null-Object pattern)."

§3.4: "Every conditional registration has an 'ADR-032 §F.1 inspection' comment block. This is excellent rigor — and a symptom of how many feature gates exist."

§7.6 open question: "Only `BriefingAi` has Null peer. The other 4 facades don't degrade gracefully under compound-AI-OFF. Is this intentional or an asymmetric-registration anti-pattern (`bff-extensions.md` §F.1)?"

**Verdict on §7.6**: The pattern is **NOT intentional in an architecturally consistent way**. It is a mix of (a) legitimate ADR-032 application (`IBriefingAi` P3 Null peer is correct), (b) ADR-032 anti-pattern explicitly forbidden by ADR-032 §Anti-patterns (defensive `IFoo? = null` + `RequireAi()` throwing `InvalidOperationException` — 7 services + 5 endpoint handlers do this), and (c) one severe missing P3 Null peer (`IInsightsAi`, consumed as hard parameter by 3 unconditionally-mapped endpoints with no Null fallback).

---

## §2 Empirical reproduction (at HEAD; pinned to 357e6936)

| Facade | Interface file | Real impl | Null peer | Real impl ctor deps | Total grep hits | Consumed by minimal-API endpoint signatures? |
|---|---|---|---|---|---|---|
| `IBriefingAi` | `PublicContracts/IBriefingAi.cs` | `BriefingAi.cs` | **YES — `NullBriefingAi.cs`** (P3 Fail-Fast) | `IOpenAiClient` (1) | 9 files | YES — `DailyBriefingEndpoints.cs` (3 sites), `WorkspaceMatterEndpoints.cs` (1 site). 3 are `IBriefingAi? = null` (defensive nullable + null-check + 503); 2 are non-nullable inside private helper methods called from handler body. |
| `IInvoiceAi` | `PublicContracts/IInvoiceAi.cs` | `InvoiceAi.cs` | **NO** | `IPlaybookService` + `IOpenAiClient` (2) | 8 files | NO — only `Services/Finance/InvoiceAnalysisService.cs`, `InvoiceSearchService.cs`, `Services/Jobs/Handlers/InvoiceIndexingJobHandler.cs`. All three use `IInvoiceAi? = null` defensive nullable + `RequireAi()` helper throwing `InvalidOperationException`. |
| `IWorkspacePrefillAi` | `PublicContracts/IWorkspacePrefillAi.cs` | `WorkspacePrefillAi.cs` | **NO** | `IPlaybookOrchestrationService` (1) | 6 files | NO — only `Services/Workspace/MatterPreFillService.cs`, `ProjectPreFillService.cs`, `WorkspaceAiService.cs`. All three use `IWorkspacePrefillAi? = null` defensive nullable + `RequireAi()` helper throwing `InvalidOperationException`. |
| `IRecordMatchingAi` | `PublicContracts/IRecordMatchingAi.cs` | `RecordMatchingAi.cs` | **NO** | `IRecordSearchService` (1) | 3 files | NO — zero production consumers per inventory + grep (XML doc line 18-22 confirms: "Scaffolded ahead of consumer migration so that future CRUD-side record-matching needs land on the facade by default."). |
| `IInsightsAi` | `PublicContracts/IInsightsAi.cs` | `Services/Ai/Insights/InsightsOrchestrator.cs` | **NO** | `IPlaybookExecutionEngine`, `IInsightsPlaybookExecutionCache`, `IOpenAiClient`, `IPlaybookOrchestrationService`, `IIngestDocumentSource`, `IOptionsMonitor<InsightsPlaybookNameMapOptions>`, `IRagService`, `AssistantToolCallHandler` (8) | 40 files | **YES — HARD parameter (non-nullable)**: `InsightEndpoints.cs:111` (`/api/insights/ask`), `InsightsSearchEndpoint.cs:122` (`/api/insights/search`), `InsightsAssistantEndpoint.cs:132+415` (`/api/insights/assistant/query`). All three endpoints mapped UNCONDITIONALLY in `EndpointMappingExtensions.cs:199, 206, 215`. Also 2 non-endpoint consumers: `InsightsIngestJobHandler.cs:94`, `PrecedentProjectionSync.cs:57`. |
| `IObservationMirror` | `PublicContracts/IObservationMirror.cs` | `Services/Ai/Insights/Mirror/NoOpObservationMirror.cs` (default) + `Services/Insights/Observations/DataverseObservationMirror.cs` (Zone B swap-in via `services.Replace(...)` at `InsightsModule.cs:148`) | **N/A — dual real-impl with intentional swap** | `IGenericEntityService`, options (DataverseObservationMirror) | 9 files | NO endpoint signature consumption — only `ObservationEmitterNodeExecutor.cs:80` consumes via INodeExecutor registry (Singleton, factory-discovered). |

### §2.1 Critical DI-registration findings

1. **`AddPublicContractsFacade` (`AnalysisServicesModule.cs:357-363`) is called ONLY inside the compound `if (analysisEnabled && documentIntelligenceEnabled)` gate at line 44/61**. The 4 facades (IBriefingAi, IInvoiceAi, IWorkspacePrefillAi, IRecordMatchingAi) are registered ONLY in the compound-ON branch.

2. **`AddNullObjectsForCompoundOff` (`AnalysisServicesModule.cs:211-271`) registers a Null peer for ONLY ONE of those four facades** — line 214: `services.AddScoped<IBriefingAi, NullBriefingAi>()`. The other 3 facades (`IInvoiceAi`, `IWorkspacePrefillAi`, `IRecordMatchingAi`) get NO compound-OFF registration.

3. **`InsightsFacadeModule.AddInsightsFacadeModule()` is called UNCONDITIONALLY at `Program.cs:104`**, registering `IInsightsAi → InsightsOrchestrator` (Scoped) AND `IPlaybookExecutionEngine → PlaybookExecutionEngine` (Scoped). When compound AI is OFF, `IOpenAiClient` (registered conditionally at `AnalysisServicesModule.cs:27`) and `IAiPlaybookBuilderService` (registered conditionally at `AddBuilderServices` line 367) are NOT registered. **`InsightsOrchestrator` and `PlaybookExecutionEngine` ctors will throw `InvalidOperationException` ("Unable to resolve service for type IOpenAiClient...") at request scope when AI is OFF — NOT a clean `FeatureDisabledException → 503`.** ASP.NET metadata-gen at startup may not catch this (param-inference only validates the resolvable signature type, not transitive ctor deps); the failure surfaces only on first request.

4. **Inconsistent DI commentary**: `AnalysisServicesModule.cs:75-79` claims "When AI is OFF, IInsightsAi falls back to the Null facade in `AddNullObjectsForCompoundOff`." This is **factually incorrect** — no `IInsightsAi` Null registration exists anywhere in the codebase. This is a comment-code mismatch and should be flagged as a documentation bug.

5. **The 7 service-level consumers (Finance, Workspace, Jobs) all use the ADR-032-FORBIDDEN defensive pattern**: `IFoo? = null` ctor parameter + `private IFoo RequireAi() => _x ?? throw new InvalidOperationException(...)`. ADR-032 §Anti-patterns line 138-141 explicitly states:
   > `// ❌ DON'T: [FromServices] + nullable + null-check inline (forces every endpoint to repeat)`

   They throw `InvalidOperationException` (not `FeatureDisabledException`), bypassing the unified 503 ProblemDetails conversion via `AsFeatureDisabled503()`. **This is the §F.1 anti-pattern compounded with the ADR-032 anti-pattern.**

6. **The 2 endpoint-level consumers of `IBriefingAi`** (`DailyBriefingEndpoints.cs:64,194` + `WorkspaceMatterEndpoints.cs:168`) use BOTH patterns simultaneously: `IBriefingAi? briefingAi = null` defensive nullable AND a `catch (FeatureDisabledException ex) => ex.AsFeatureDisabled503()` catch block. This is **belt-and-suspenders** — when AI is ON, the Null peer is unused; when AI is OFF, BOTH paths trigger. The defensive null check fires first (lines 69, 199, 173), so the `NullBriefingAi` Null peer is in fact never hit in compound-OFF on these endpoints. **The Null peer was added but the defensive `?=null` was never removed**, which means the ADR-032 effort was incomplete for these endpoints.

7. **`IObservationMirror`** is structurally distinct from the 5 facades — it is consumed only via Node executor registry (not minimal-API param-inference) and has an intentional NoOp/Dataverse swap. §F.1 anti-pattern does NOT apply. Lifetime is Singleton (matches its consumers).

---

## §3 Per-facade decision table

| # | Facade | Null-peer gap classification | Recommended action | Pattern | Migration cost | Cross-team dep |
|---|---|---|---|---|---|---|
| 1 | `IBriefingAi` | **HAS_PEER but incomplete adoption** — Null peer exists (`NullBriefingAi` P3 Fail-Fast), but 7 consumers (3 endpoints + 4 services) still use defensive `?=null` pattern. ADR-032 anti-pattern compounded with §F.1 anti-pattern. | **Keep Null peer + clean up consumers**. Remove `?=null` defensive nullable from `DailyBriefingEndpoints.cs:64,194`, `WorkspaceMatterEndpoints.cs:168`, `BriefingService.cs:62`. Keep only the `FeatureDisabledException` catch site. | P3 Fail-Fast (already in place) | M — touches 4 files + tests for each endpoint | Workspace team owns 3 of 4 consumer files; Daily-Briefing endpoint also Workspace-team-adjacent |
| 2 | `IInvoiceAi` | **MISSING_PEER + §F.1 anti-pattern via defensive nullable** — 3 service consumers (`InvoiceAnalysisService`, `InvoiceSearchService`, `InvoiceIndexingJobHandler`) use `IInvoiceAi? = null` + `RequireAi()` throws `InvalidOperationException`. NO endpoint signature consumption, but `InvoiceIndexingJobHandler` is a job handler that runs unconditionally per ADR-004 Job Contract. | **Add `NullInvoiceAi` P3 Fail-Fast peer + clean up consumers**. Replace `RequireAi()` throwing `InvalidOperationException` with direct facade injection and `FeatureDisabledException` from `NullInvoiceAi`. Note: per ADR-032 §"When NOT to use" (table row 4), background hosted services / job handlers don't strictly need Null peer because they don't participate in metadata-gen. But for consistency and to unblock cleanup of `RequireAi()`, add the peer anyway. | P3 Fail-Fast (mirror `NullBriefingAi`) | S — ~30 LOC `NullInvoiceAi` + 3 consumer cleanups | Finance team owns all 3 consumer files |
| 3 | `IWorkspacePrefillAi` | **MISSING_PEER + §F.1 anti-pattern via defensive nullable** — 3 service consumers (`MatterPreFillService`, `ProjectPreFillService`, `WorkspaceAiService`) use `IWorkspacePrefillAi? = null` + `RequireAi()`. NO direct endpoint signature consumption (consumers are services injected into endpoints). | **Add `NullWorkspacePrefillAi` P3 Fail-Fast peer + clean up consumers**. The 3 consumers are Scoped services, registered unconditionally in `WorkspaceModule`; their consumers (Workspace endpoints) ARE unconditionally mapped. Transitively, the `?=null` pattern papers over a registered-as-conditional service injected into an unconditionally-mapped chain. | P3 Fail-Fast | S — ~30 LOC + 3 cleanups | Workspace team owns all 3 |
| 4 | `IRecordMatchingAi` | **MISSING_PEER (forward-compat only)** — zero current consumers. XML doc admits: "Scaffolded ahead of consumer migration so that future CRUD-side record-matching needs land on the facade by default." | **Add `NullRecordMatchingAi` P3 Fail-Fast peer NOW as a forward-mitigation** (same rationale `AnalysisServicesModule.cs:269-270` uses for `NullInsightsIntentClassifier`: "Pre-registering the Null-Object now prevents the asymmetric-registration anti-pattern from being introduced when [first consumer] lands"). Without this, the next consumer that injects `IRecordMatchingAi` into an unconditionally-mapped endpoint will reintroduce the §F.1 anti-pattern. | P3 Fail-Fast | XS — ~25 LOC, no consumer cleanup needed | None today; preempts future cross-team gap |
| 5 | `IInsightsAi` | **MISSING_PEER + SEVERE ANTI-PATTERN** — hard `IInsightsAi insightsAi` parameter in 3 unconditionally-mapped endpoints. `IInsightsAi → InsightsOrchestrator` is registered unconditionally in `InsightsFacadeModule:105`, but `InsightsOrchestrator`'s ctor depends on `IOpenAiClient` + `IAiPlaybookBuilderService` (both conditional-only). Metadata-gen may succeed (signature-resolvable), but **request-scope DI will throw `InvalidOperationException` (not `FeatureDisabledException → 503`) when compound AI is OFF**. The `AnalysisServicesModule.cs:75-79` comment claims a Null fallback exists for `IInsightsAi`; **this is factually incorrect — no Null registration exists.** | **HIGHEST PRIORITY: Add `NullInsightsAi` P3 Fail-Fast peer**. Should throw `FeatureDisabledException("ai.insights.disabled", "...")` for all 5 methods (`AnswerQuestionAsync`, `RunIngestAsync`, `EmbedTextAsync`, `SearchAsync`, `AssistantQueryAsync`, `AssistantQueryStreamAsync`). Either: (a) keep `AddInsightsFacadeModule` unconditional + have it conditionally register Null vs real based on compound gate; OR (b) move `IInsightsAi` real-impl registration into `AnalysisServicesModule.AddPublicContractsFacade` and add `NullInsightsAi` to `AddNullObjectsForCompoundOff`. Option (b) is symmetric with the other 4 facades and easier to reason about. ALSO fix the misleading comment at `AnalysisServicesModule.cs:75-79`. | P3 Fail-Fast | M — ~100 LOC `NullInsightsAi` (6 methods) + DI rewire + 3 endpoint try/catch additions | Insights team owns; cross-cuts Wave E2/E3/F surface |
| 6 | `IObservationMirror` | **NOT_NEEDED (intentional dual real-impl)** — NoOp is registered first by `InsightsIngestModule:78` (Singleton, default); `services.Replace(...)` at `InsightsModule:148` swaps in `DataverseObservationMirror` (Zone B) when InsightsModule is registered. Consumed only by `ObservationEmitterNodeExecutor` (Singleton, registry-discovered) — not minimal-API param-inference. The XML doc comment explicitly notes mirror failures are non-fatal by design. | **KEEP AS-IS**. Pattern is correct: ADR-032 §"When NOT to use" table row 1 fits (consumer uses `IServiceProvider.GetRequiredService` semantics via registry, not direct param-inference). Document that this is NOT a §F.1 candidate. | N/A | None | N/A |

---

## §4 Cross-cutting findings

### §4.1 §F.1 pattern coverage — the gap is real and material

The Phase 1 inventory framed §7.6 as a question; the empirical evidence is a clear answer: **4 of 5 facades have a §F.1 asymmetric-registration anti-pattern**, in two distinct manifestations:

- **Manifestation A (severe, latent)**: `IInsightsAi` registered unconditionally but transitively depends on conditional services → request-scope throws `InvalidOperationException` (NOT `FeatureDisabledException → 503`) under compound-OFF. Endpoint metadata-gen does NOT catch this (param-inference checks signature resolvability, not transitive ctor resolvability). This is a LATENT residual exactly matching the pattern documented in `bff-extensions.md` §F.1 ("LATENT cases where the missing service is only triggered by metadata-gen, not by a failing test"). It is invisible to current integration tests because the InsightsEndpointsTests fixture overrides `IInsightsAi` with a mock (per `InsightsFacadeModule.cs:38-47` XML doc note).

- **Manifestation B (visible, structural)**: `IInvoiceAi` and `IWorkspacePrefillAi` registered conditionally; 7 consumer files paper over it with defensive `?=null` + `RequireAi()` throwing `InvalidOperationException`. This is the ADR-032 §Anti-patterns explicit forbidden pattern. It survives because the consumer services themselves are also conditionally registered (Finance, Workspace), so the chain doesn't break metadata-gen — but it produces 500 errors (opaque "Unable to resolve") instead of 503 ProblemDetails with stable `errorCode`.

### §4.2 Facade-boundary integrity is intact at the structural level

Despite the asymmetric-registration gaps, the **facade boundary itself is working as ADR-013 intends**: external CRUD code (Finance, Workspace, Jobs) consumes AI through facade types (no direct `IOpenAiClient` / `IPlaybookService` / `IPlaybookOrchestrationService` injection in CRUD-side files). The IInsightsAi facade for Zone B is consistently consumed. The facades' XML docs (IBriefingAi lines 7-25, IInvoiceAi lines 9-30, IWorkspacePrefillAi lines 9-26, IRecordMatchingAi lines 10-23, IInsightsAi lines 9-49) are unusually high-quality and cite ADR-013 + ADR-007 explicitly. This is a strength.

### §4.3 The 4 facades collectively re-implement the §2.11 `IOpenAiClient` facade for 4 use cases

Each facade is a 1-3 method wrapper over `IOpenAiClient` and/or `IPlaybookService` / `IPlaybookOrchestrationService` / `IRecordSearchService`. The wrapped surface is genuinely small (Briefing 1 method, Invoice 3, WorkspacePrefill 1, RecordMatching 1, Insights 6) and each method delegates 1:1 to the wrapped type with input validation. There is essentially no behavior in the facade impls — they ARE the boundary layer, nothing more. This validates ADR-007's "facade-over-internal-SDK" rationale.

### §4.4 Lifetime consistency

All 5 facades are registered Scoped. This is appropriate — `IPlaybookService` is transient typed-HttpClient (incompatible with Singleton), `IPlaybookOrchestrationService` is Scoped (forbids Singleton wrapper). Scoped is the safe minimum-common-denominator for all wrappers. Documented well in `AnalysisServicesModule.cs:348-351` XML.

### §4.5 Surface size shrinkage in IInsightsAi (post-Wave F) versus other 4

IInsightsAi has grown from 3 methods (task 042 baseline per inventory) to 6 methods through Waves D/E/F (`SearchAsync` Wave E, `AssistantQueryAsync` Wave E3, `AssistantQueryStreamAsync` Wave F). This is the OPPOSITE direction from the other 4 facades, which have remained at their original narrow surfaces. The Insights facade is becoming the canonical "AI dispatch hub" because the Insights Engine is the only consumer team that has been actively building atop the facade surface. **For Wave 2 facade-consolidation analysis: IInsightsAi is the most consumer-driven evolutionary case and may be a model for the future "Spaarke Canonical AI Facade" layer.**

---

## §5 Canonical naming candidates (Q-004 framing)

Per Q-004 lock ("Spaarke Canonical AI Stack" framing), this layer of the canonical stack is the "facade boundary". Naming candidates surface but are NOT locked here (per scope):

- **"Spaarke AI Facade Boundary"** — descriptive, matches the inventory §2.6 heading. Single namespace today (`Services.Ai.PublicContracts`).
- **"Spaarke AI Public Contracts"** — the existing namespace name; reads literally.
- **"Spaarke Canonical AI Facades"** — aligns with Q-004 phrasing.
- **"Zone-A → Zone-B Boundary Layer"** — semantically accurate for `IInsightsAi` + `IObservationMirror`, but misleading for the other 4 (which are CRUD→AI, not Zone-A→Zone-B).

**Recommendation for W2 to resolve**: keep "Public Contracts" as the namespace (it's already there, well-understood) but adopt "Spaarke Canonical AI Facade Layer" as the cross-reference name in ADRs and constraint docs. Defer to Cat 1 + Cat 3 W2 outcomes for any folder restructuring.

---

## §6 Drift report (snapshot 357e6936 vs HEAD)

Verified `git log 357e6936..origin/master -- Services/Ai/PublicContracts/ InsightsFacadeModule.cs AnalysisServicesModule.cs` → **EMPTY**. No drift in any facade interface, implementation, Null peer, or DI registration since the snapshot. All findings valid at HEAD (12275b10).

No new facades added. No new Null peers added. The "BriefingAi as the lone Null peer" + "InsightsOrchestrator unconditional + transitive-conditional deps" state is unchanged.

---

## §7 Open questions for owner review

1. **(HIGHEST URGENCY) Confirm `IInsightsAi` request-scope failure mode under compound-AI-OFF.** The empirical evidence strongly suggests it will throw `InvalidOperationException` (not `FeatureDisabledException → 503`) at first request when AI is OFF. This should be verified by an integration test that flips `DocumentIntelligence:Enabled=false` and calls `/api/insights/ask` — does it return 503 with stable `errorCode=ai.insights.disabled` (correct) or 500 with opaque "Unable to resolve service" (latent bug)? If 500 → file as HIGH severity bug. Recommendation: this verification belongs to the W2 Cat 1 sub-agent who will also touch `InsightsIntentClassifier`.

2. **Owner intent for the §F.1 anti-pattern in 7 service-level consumers**: was the `IFoo? = null` + `RequireAi()` pattern an intentional choice predating ADR-032's strengthening (2026-06-02)? If yes, the 7 consumers are legacy debt that should be tracked separately. If no (i.e., these are §F.1 anti-pattern instances that should be remediated), W3 should plan the migration.

3. **(deferred to W2)** Should `IRecordMatchingAi` be deleted now that the facade has zero consumers, or kept as forward-compat for the forthcoming Record-Match consumer migration? Inventory §6.2 lists `ActionLookupService`, `SkillLookupService`, `ToolLookupService` as orphans worth deleting; the `RecordMatchingAi` facade has a similar zero-consumer profile but is more defensible because it sits on the canonical facade boundary required by ADR-013. **Recommend KEEP** + add `NullRecordMatchingAi` (forward-mitigation per §F.1 rule).

4. **(deferred to W2)** Should the 5 facades collapse into a smaller number? Specifically `IBriefingAi` (1 method: text gen) and `IInvoiceAi` (3 methods, one of which is text gen) overlap on `GenerateNarrativeAsync` / `GetStructuredCompletionAsync`. This question depends on Cat 1 + Cat 3 outcomes (does the underlying classifier/search infrastructure consolidate?), so it stays out of scope per the brief. **Do NOT collapse in W1.**

5. **Documentation bug**: `AnalysisServicesModule.cs:75-79` comment claims `IInsightsAi` falls back to Null facade in `AddNullObjectsForCompoundOff` — this is factually incorrect and misleading for future reviewers applying §F.1 inspection. Fix as part of the W3 remediation, OR file as a standalone code-cleanup PR.

---

## §8 ADR candidates (bullet items only — per Q-005 ADRs deferred to W3)

- **(ADR-candidate A) "AI Public-Contracts Facade Null-Peer Mandate"** — every `Services/Ai/PublicContracts/` facade MUST have a Null peer registered via the canonical compound-OFF branch, even if zero consumers exist today (forward-mitigation per §F.1). MUST throw `FeatureDisabledException` with stable `ErrorCode` matching `ai.<feature>.disabled`. Cross-references: ADR-032 (mechanism), ADR-013 (boundary). Closes the §7.6 inventory question definitively.

- **(ADR-candidate B) "Defensive-Nullable Facade Injection Prohibited"** — codify the ADR-032 §Anti-patterns rule into a top-level MUST NOT, scoped to the facade boundary. CRUD-side and endpoint-side consumers MUST inject facade types as non-nullable hard parameters; the Null peer is the gate, not `RequireAi()`. Provides a binding rule for the 7 current §F.1 anti-pattern sites identified in this analysis.

- **(ADR-candidate C) "Zone B Endpoint Mapping → Zone A Facade Registration Symmetry"** — when a Zone B endpoint (`Api/Insights/**`) is mapped unconditionally, its consumed Zone A facade (`IInsightsAi`, future Zone-B-visible facades) MUST also be registered unconditionally AND symmetric with a Null peer. The current state (`IInsightsAi` real registered unconditionally, but transitive deps conditional) breaks this rule.

- **(ADR-candidate D, weaker) "Facade XML Documentation Pattern"** — the 5 existing PublicContracts XML docs are unusually rigorous and could be codified as a pattern. Lower priority; defer.

---

# Sub-Agent C Final Status Report

1. **Status**: COMPLETE
2. **Output file path**: `projects/bff-ai-architecture-audit-r1/notes/phase2/analysis-public-contracts.md`
3. **Facades analyzed**: **6** (5 PublicContracts facades + IObservationMirror).
4. **Decision distribution**:
   - keep-as-is: **2** (IBriefingAi structural Null peer kept; IObservationMirror dual-impl kept)
   - keep + cleanup consumers: **1** (IBriefingAi consumer-side `?=null` removal)
   - add-Null-peer (P3 Fail-Fast): **4** (`NullInvoiceAi`, `NullWorkspacePrefillAi`, `NullRecordMatchingAi`, `NullInsightsAi`)
   - consolidate/deprecate/delete: **0** (per brief §4 scope boundary — deferred to W2)
5. **Drift findings**: ZERO. No facade files changed between snapshot 357e6936 and HEAD 12275b10.
6. **Cross-cutting observations**:
   - §F.1 anti-pattern coverage gap is REAL (4 of 5 facades affected) and MATERIAL (will produce 500 errors instead of 503 ProblemDetails under compound-OFF).
   - The §F.1 manifestation comes in two flavors: latent transitive-conditional (IInsightsAi — most severe) and visible defensive-nullable (7 consumer sites for IInvoiceAi/IWorkspacePrefillAi/IBriefingAi).
   - One factual error in code: `AnalysisServicesModule.cs:75-79` comment claims a Null `IInsightsAi` registration exists; it does not.
   - Facade boundary integrity (CRUD doesn't import AI internals) IS intact — the gap is in failure-mode discipline, not boundary placement.
7. **Open questions for owner**:
   - URGENT: verify `IInsightsAi` request-scope failure mode under compound-OFF (likely 500, should be 503).
   - Owner intent on the 7 §F.1 anti-pattern consumer sites (legacy debt to track, or remediate now?).
   - Documentation-bug PR for `AnalysisServicesModule.cs:75-79` (file separately or roll into W3 remediation).
8. **Recommendations for W2**:
   - **Cat 1 (Intent classification) sub-agent**: `IInsightsAi` wraps `IInsightsIntentClassifier` (which DOES have a Null peer per `AnalysisServicesModule.cs:269-270`). When considering consolidation of the 4 intent classifiers, note that `InsightsIntentClassifier`'s consumer is `AssistantToolCallHandler` which is invoked via `IInsightsAi.AssistantQueryAsync()`. The facade is the call-chain root for that classifier.
   - **Cat 3 (Search services) sub-agent**: `IInsightsAi.SearchAsync()` wraps `IRagService` (which HAS Null peer `NullRagService`); `IRecordMatchingAi` wraps `IRecordSearchService` (no Null peer). When considering search-substrate consolidation, the facade layer is consumer-stable — wrapping consolidation can happen entirely below the facade without breaking consumer contracts.
   - **DI + Configuration sub-agent**: The `AddPublicContractsFacade` + `AddNullObjectsForCompoundOff` + `AddInsightsFacadeModule` split across two DI modules is the structural ROOT CAUSE of the `IInsightsAi` asymmetry. Recommend treating these three as a unified "facade DI fascia" in W2's DI analysis. The simplest remediation is to merge IInsightsAi's registration into `AddPublicContractsFacade` (symmetric with the other 4 facades) and add `NullInsightsAi` to `AddNullObjectsForCompoundOff`.
