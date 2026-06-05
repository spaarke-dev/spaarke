# DR-003 — Public Contracts Facade (Category 6) — INCLUDES LATENT BUG

> **Author**: Phase 3 Sub-Agent K (synthesis from Phase 2 outputs)
> **Date**: 2026-06-04
> **Status**: PROPOSED (pending Q-002 owner review)
> **Pinned to**: Phase 1 inventory commit `357e6936`
> **Source analysis**: [`notes/phase2/analysis-public-contracts.md`](../notes/phase2/analysis-public-contracts.md)
> **Canonical authority**: [`notes/canonical-architecture-decisions.md` §2.3](../notes/canonical-architecture-decisions.md) · §1.4 (LATENT BUG) · §3 (W1 Cat 6 row) · §4 (LATENT BUG structural remediation) · §8.1 (W1-2, W1-3, W1-4)
> **Priority**: HIGH — this DR includes the audit's single highest-priority structural finding.

## Context

Phase 1 inventory §2.6 catalogued 5 facades in `Services/Ai/PublicContracts/` (`IBriefingAi`, `IInvoiceAi`, `IWorkspacePrefillAi`, `IRecordMatchingAi`, `IInsightsAi`) plus `IObservationMirror`. Inventory §7.6 surfaced an open question: only `IBriefingAi` had a Null peer (`NullBriefingAi`); the other 4 facades had no compound-OFF fallback registration.

W1 Sub-Agent C audited each facade against HARD GATES + cross-referenced ADR-013 (facade-over-internal-SDK) + ADR-032 (Null-Object Kill-Switch) + `bff-extensions.md` §F.1 (asymmetric-registration anti-pattern). The audit produced **two material findings**:

### Finding 1: The LATENT BUG (HIGHEST-PRIORITY single audit finding)

`IInsightsAi → InsightsOrchestrator` is registered UNCONDITIONALLY in `InsightsFacadeModule.cs:105`. `InsightsOrchestrator`'s 8-parameter constructor transitively depends on `IOpenAiClient` + `IAiPlaybookBuilderService` + `AssistantToolCallHandler` — all 3 are registered ONLY in the compound-AI-ON branch of `AnalysisServicesModule`. When compound AI is OFF, the 3 unconditionally-mapped Insights endpoints (`/api/insights/ask`, `/api/insights/search`, `/api/insights/assistant/query`) will throw `InvalidOperationException("Unable to resolve service for type IOpenAiClient...")` at first-request scope, producing **500 with opaque body** instead of the contracted **503 ProblemDetails with `errorCode=ai.insights.disabled`** via `FeatureDisabledException → AsFeatureDisabled503()`.

The bug is **invisible to current integration tests** because the `InsightsEndpointsTests` fixture mocks `IInsightsAi` directly (per `InsightsFacadeModule.cs:38-47` XML doc note). Cross-validation by W2 Cat 1 (classifier layer), W2 Cat 3 (search layer), W3 Cat 5 (prompt layer), and W4 §4.5 (DI topology) independently confirmed the bug is at the **facade-registration topology layer**, NOT downstream.

A compounding documentation bug: `AnalysisServicesModule.cs:75-79` falsely claims `IInsightsAi` has a Null peer fallback — no such registration exists anywhere in the codebase.

### Finding 2: §F.1 Manifestation distinction (the missing Null peers + defensive-nullable anti-pattern)

The §F.1 anti-pattern appears in **two distinct manifestations** across the facades:
- **Manifestation A (severe, latent)**: `IInsightsAi` — described in Finding 1 above.
- **Manifestation B (visible, structural)**: `IInvoiceAi` and `IWorkspacePrefillAi` registered conditionally; 7 consumer files (Finance, Workspace, Jobs) paper over the gate with defensive `IFoo? = null` ctor parameters + `RequireAi()` helper throwing `InvalidOperationException`. ADR-032 §Anti-patterns explicitly forbids this. `IBriefingAi` consumers ALSO retain the defensive `?=null` pattern even though `NullBriefingAi` exists — incomplete ADR-032 adoption.

`IRecordMatchingAi` has zero current consumers (forward-compat scaffolding per its XML doc).

`IObservationMirror` is structurally distinct (intentional dual real-impl via `services.Replace`) — explicitly NOT a §F.1 candidate; document to prevent future audit flagging.

## Decision

1. **KEEP all 5 facades** at their current namespace `Services/Ai/PublicContracts/`. The facade boundary itself is working as ADR-013 intends — XML docs are unusually high-quality and CRUD code consistently consumes AI through facade types.

2. **EXECUTE LATENT BUG Option A structural remediation** (per canonical-architecture-decisions §4.3) as a HIGH-priority bundled PR (~280 LOC):
   - **MOVE** `services.AddScoped<IInsightsAi, InsightsOrchestrator>()` from `InsightsFacadeModule.cs:105` to `AnalysisServicesModule.AddPublicContractsFacade:357-363` (symmetric with the other 4 facades).
   - **MOVE** `services.AddScoped<IPlaybookExecutionEngine, PlaybookExecutionEngine>()` from `InsightsFacadeModule.cs:95` to the same compound-ON helper.
   - **ADD** `NullInsightsAi` impl (~100 LOC; 6 methods throwing `FeatureDisabledException("ai.insights.disabled", "...")`): `AnswerQuestionAsync`, `RunIngestAsync`, `EmbedTextAsync`, `SearchAsync`, `AssistantQueryAsync`, `AssistantQueryStreamAsync`.
   - **REGISTER** `NullInsightsAi` in `AnalysisServicesModule.AddNullObjectsForCompoundOff`.
   - **FIX** misleading comment at `AnalysisServicesModule.cs:75-79`.
   - **ADD** integration test asserting 503 (not 500) from 3 unconditionally-mapped Insights endpoints under compound-AI-OFF (`DocumentIntelligence:Enabled=false`).

3. **ADD 4 Null peers** (bundled in the same PR for ~80 additional LOC; total bundled ~280 LOC):
   - `NullInvoiceAi` (P3 Fail-Fast peer; bundle with consumer cleanup — remove `RequireAi()` from 3 Finance consumers).
   - `NullWorkspacePrefillAi` (P3 Fail-Fast peer; bundle with consumer cleanup — remove `RequireAi()` from 3 Workspace consumers).
   - `NullRecordMatchingAi` (forward-mitigation; preempts §F.1 anti-pattern when future consumer lands).
   - `NullInsightsAi` (per Decision #2 above).

4. **CLEAN UP `IBriefingAi` consumers** — remove the defensive `IBriefingAi? = null` + `RequireAi()` pattern from 4 consumer sites (`DailyBriefingEndpoints.cs:64,194`, `WorkspaceMatterEndpoints.cs:168`, `BriefingService.cs:62`); rely on `NullBriefingAi` Null peer + `FeatureDisabledException` catch sites.

5. **DOCUMENT `IObservationMirror` as explicitly NOT a §F.1 candidate** in `bff-extensions.md` §F.1 to prevent future audit flagging of intentional dual real-impl + `services.Replace` pattern.

6. **GENERALIZE the LATENT BUG into the Endpoint↔DI Symmetry Rule** (see DR-008 for the binding rule; this DR is the canonical instance the rule was inferred from).

## Consequences

### Positive
- **LATENT BUG eliminated** — Insights endpoints return 503 ProblemDetails (contracted) instead of 500 opaque body when compound AI is OFF.
- ADR-032 Null-Object pattern coverage at 5/5 facades — pattern parity restored across all facades.
- Eliminates the 7-consumer-file defensive-nullable anti-pattern explicitly forbidden by ADR-032 §Anti-patterns.
- Documentation accuracy restored at `AnalysisServicesModule.cs:75-79`.
- Endpoint↔DI Symmetry Rule (DR-008) gains its canonical instance — the rule becomes more diagnose-able when reviewers can cite "the InsightsAi case".

### Negative
- Bundled PR is ~280 LOC — moderate review surface. Mitigated by clear pattern parity (4 new Nulls mirror the existing `NullBriefingAi`).
- Cross-team coordination required: Insights (LATENT BUG owner), Finance (Invoice consumers), Workspace (Workspace + Briefing consumers).
- Risk of integration-test fixture brittleness if any sub-team's tests mock these facades — mitigation: integration tests audit during the PR.

### Migration impact
- **Cross-team coordination**: Insights team (LATENT BUG owner — the team most affected by the failure mode) + Finance Intelligence (3 Invoice consumer cleanups) + Workspace (3 Workspace consumer cleanups + 4 Briefing consumer cleanups).
- **Effort estimate**: **M (Medium)** — ~280 LOC across DI registration moves + 4 Null impls + 10 consumer cleanups + integration test.
- **Sequencing**: HIGH-priority bundled PR. Recommended Phase 4 lock to execute ahead of ADR follow-on phase OR alongside it (canonical §11.4 Q-17 owner-adjudication).
- **Verification**: The new integration test (Decision #2 bullet 6) is the runtime gate that proves the LATENT BUG eliminated. Once landed, it becomes the canonical regression test cited by DR-008's "Runtime §F.1 Detection Fixture" (canonical §4.5).

## Canonical naming (Q-004 — surfaced not locked)

- **Candidate**: "Spaarke Public-Contracts Facade DI Fascia" (W4 §5 — NEW name; load-bearing post-remediation)
- **Reference impls post-remediation**:
  - 5 facades in `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/`
  - 5 Null peers (1 existing + 4 to add)
  - Unified DI fascia: `AnalysisServicesModule.AddPublicContractsFacade` + `AnalysisServicesModule.AddNullObjectsForCompoundOff` + `InsightsFacadeModule` (cleanup post-Option-A)
- **Pattern elements** (5):
  1. Single namespace `Services.Ai.PublicContracts/` (boundary marker)
  2. Narrow facade surface (1-6 methods per facade)
  3. ADR-032 P3 Fail-Fast Null peer for EVERY facade
  4. `FeatureDisabledException` throwing with stable `errorCode=ai.<feature>.disabled` → 503 ProblemDetails via `AsFeatureDisabled503()`
  5. Endpoint-side hard-parameter injection (NO defensive `IFoo? = null` — ADR-032 §Anti-patterns forbidden)
- **Gold-standard XML doc**: All 5 facades cite ADR-013 + ADR-007 explicitly (W1 Cat 6 §4.2 calls them "unusually high-quality")
- **Adjacent finding**: `IObservationMirror` explicitly NOT a §F.1 candidate (intentional dual real-impl)

## ADR candidates from this decision (Q-005 — bullets only)

- **W1-2** AI Public-Contracts Facade Null-Peer Mandate — HIGH priority
- **W1-3** Defensive-Nullable Facade Injection Prohibited — HIGH priority
- **W1-4** Zone B Endpoint Mapping → Zone A Facade Registration Symmetry — HIGH priority (superseded by W4-1 formal)
- **W1-12** Facade XML Documentation Pattern — LOW priority

## Open questions for owner review (Q-002)

1. **LATENT BUG remediation prioritization** (canonical §11.4 Q-17): Ship Option A with Phase 3 ADR landing, OR as separate URGENT remediation PR ahead of ADRs?
2. **Endpoint↔DI Symmetry Rule codification** (canonical §11.4 Q-18): Standalone ADR + add to `bff-extensions.md` §F as new sub-mechanism §F.4, OR standalone ADR section?
3. **Runtime §F.1 detection fixture authorship** (canonical §11.4 Q-19): Authorize Insights team to author (~1-2 days)?
4. **Owner intent for `IFoo? = null` + `RequireAi()` pattern** (canonical §11.8 Q-31): Legacy debt predating ADR-032 strengthening (track separately), or §F.1 anti-pattern (remediate now)?
5. **`IRecordMatchingAi` forward-mitigation** (canonical §11.2): Add `NullRecordMatchingAi` now even though no consumers exist — owner accepts the precautionary pattern?
6. **Documentation bug repair**: Bundle `AnalysisServicesModule.cs:75-79` comment fix with LATENT BUG remediation PR (canonical §11.6 Q-24)?

## References

- Source analysis: [`notes/phase2/analysis-public-contracts.md`](../notes/phase2/analysis-public-contracts.md) §2-§4
- Wave summaries: [`notes/phase2/wave-1-summary.md`](../notes/phase2/wave-1-summary.md), [`notes/phase2/wave-4-summary.md`](../notes/phase2/wave-4-summary.md) §4.5 (LATENT BUG structural confirmation)
- Canonical authority: [`notes/canonical-architecture-decisions.md`](../notes/canonical-architecture-decisions.md) §1.4 (LATENT BUG executive summary) + §2.3 (Layer 3 canonical) + §4 (LATENT BUG structural remediation pattern) + §8.1 (HIGH ADR candidates) + §11.4 (LATENT BUG open questions) + §11.8 Q-31
- Related ADR candidates: W1-2 (HIGH), W1-3 (HIGH), W1-4 (HIGH; superseded by W4-1), W1-12 (LOW)
- Related DRs: **DR-008** (LATENT BUG is at facade DI fascia layer — Endpoint↔DI Symmetry Rule's canonical instance; Runtime §F.1 Detection Fixture closes the loop)
- ADR cross-references: ADR-007, ADR-013, ADR-018, ADR-030, ADR-032, `bff-extensions.md` §F.1
- Inventory corrections from this category: §6 row 18 (facade Null-peer framing — two distinct §F.1 manifestations)
