# DR-008 — DI + Configuration (Wave 4)

> **Author**: Phase 3 Sub-Agent K (synthesis from Phase 2 outputs)
> **Date**: 2026-06-04
> **Status**: PROPOSED (pending Q-002 owner review)
> **Pinned to**: Phase 1 inventory commit `357e6936`
> **Source analysis**: [`notes/phase2/analysis-di-configuration.md`](../notes/phase2/analysis-di-configuration.md)
> **Canonical authority**: [`notes/canonical-architecture-decisions.md` §2.8](../notes/canonical-architecture-decisions.md) · §1.3 (NEW W4 patterns) · §3 (W4 row) · §4 (LATENT BUG + Endpoint↔DI Symmetry Rule) · §8.1 (W4-1, W4-2) · §8.2 (W4-3, W4-4, W4-5, W4-6) · §8.3 (W4-7)
> **Priority**: HIGH — this DR introduces the audit's highest-leverage NEW finding: the Endpoint↔DI Symmetry Rule.

## Context

Phase 1 inventory §3.1 catalogued "34 DI modules" and §4.3 a "redundant `Sprk.Bff.Api.Options` namespace split"; §4.1 reported "25 files in `Configuration/`". The inventory questioned whether the module count was excessive, whether the options namespace split was intentional, and whether the compound AI gate could be simplified.

W4 Sub-Agent H audited the entire DI + Configuration topology and corrected inventory substantially:
- **Module count 34 → 31** (inventory §3.1 overcounted by 3): 29 modules in `Infrastructure/DI/` + 1 in `Api/Reporting/` + 1 in `Workers/Office/`.
- **`Sprk.Bff.Api.Options` namespace DOES NOT EXIST** — `Options/` directory uses `Sprk.Bff.Api.Configuration` namespace. The issue is directory location only, not namespace (inventory §4.3 framing error).
- **`Configuration/` per-dir count 25 → 21** (inventory §4.1).
- **35 options classes headline** (matches inventory) — single namespace `Sprk.Bff.Api.Configuration`.

W4 then independently verified the LATENT BUG (per DR-003) at HEAD via the DI registration topology layer (canonical §4.5):
- `IInsightsAi → InsightsOrchestrator` registered UNCONDITIONALLY in `InsightsFacadeModule.cs:105`.
- Transitive ctor deps `IOpenAiClient` + `IAiPlaybookBuilderService` + `AssistantToolCallHandler` are conditional-only.
- Under compound-AI-OFF, the 3 unconditionally-mapped Insights endpoints produce 500 (not contracted 503).

W4 then GENERALIZED the LATENT BUG into the **Endpoint↔DI Symmetry Rule** — the audit's highest-leverage NEW finding (load-bearing per canonical §2.9 row 8).

W4 also designated `AiModule.cs:269-313` audit-table comment block as the **gold-standard reference** for inline DI audit tables (15/15 ADR-010 budget cap with line-numbered + task-tagged registrations).

W4 introduced **2 NEW architectural patterns** beyond the 4 canonical reference impls from W1-W3:
1. **Spaarke Endpoint↔DI Symmetry Rule** — formal generalization of `bff-extensions.md` §F.1
2. **Spaarke §F.1 Runtime-Verifiable Detection Fixture** — the runtime test that closes the loop

## Decision

1. **KEEP 31 DI modules** at HEAD (corrected from inventory's 34). No consolidation. Per-concern composition is the correct architectural pattern; REJECT functional-area collapse.

2. **KEEP 35 options classes** per-feature. Single namespace `Sprk.Bff.Api.Configuration`. Canonical Options Class Design Pattern (per-feature options with const `SectionName` + DataAnnotations + per-field XML doc citing originating task/SPEC/POML + `ValidateDataAnnotations().ValidateOnStart()`).

3. **KEEP compound AI gate** as-is — 3-tier composition (compound → fine-grained sub-gate → resource-prerequisite sub-gate). Operational simplicity at `AnalysisServicesModule.cs:18-117`.

4. **DESIGNATE `AiModule.cs:269-313` as canonical DI audit-table reference** (gold-standard for inline audit tables; line-numbered + task-tagged registrations).

5. **DESIGNATE `AddPublicContractsFacade` (post-LATENT-BUG-remediation per DR-003) as canonical DI fascia reference**.

6. **CONSOLIDATE `Options/` → `Configuration/` directory** via trivial `git mv` (per W4 §3). The `Sprk.Bff.Api.Configuration` namespace already covers both; directory consolidation is one-step. Bundle with inventory-correction PR.

7. **CODIFY the Endpoint↔DI Symmetry Rule** as the audit's highest-leverage NEW architectural rule (W4-1 ADR candidate, HIGH priority):

   > **Endpoint Mapping ↔ DI Registration Conditionality Symmetry Rule**:
   > Any service registered behind a feature flag (compound AI gate, fine-grained option flag, AI-Search-keys sub-gate) MUST satisfy one of these conditions:
   > 1. Its consumer endpoint is mapped behind the SAME feature flag (symmetric conditionality), OR
   > 2. A Null-Object peer is registered in the gate-OFF branch with EXACTLY the same interface type + lifetime + same `ServiceDescriptor.ServiceType` (ADR-032 P3 Fail-Fast), AND every TRANSITIVE ctor dependency of the real impl satisfies the same rule, AND a startup integration test exists that asserts gate-OFF state returns 503 not 500.
   >
   > The rule applies recursively through the service-resolution chain — if facade `F` is registered unconditionally but its ctor depends on conditional service `S`, then EITHER `S` must have a Null peer OR `F`'s registration must move into the conditional branch.

8. **CODIFY the §F.1 Runtime-Verifiable Detection Fixture** as the runtime gate that closes the loop (W4-2 ADR candidate, HIGH priority):

   > A reusable `WebApplicationFactory<Program>` subclass that bootstraps the BFF with each compound gate independently flipped (4 combinations) + probes every unconditionally-mapped endpoint + asserts NO 500 with "Unable to resolve" body. The only acceptable failure modes under gate-OFF are (a) 503 ProblemDetails with stable errorCode `ai.*.disabled`, or (b) 200 OK with degraded payload.

9. **EXECUTE LATENT BUG Option A structural remediation** (per DR-003) as the canonical instance of the Endpoint↔DI Symmetry Rule — bundled PR ~280 LOC.

10. **CORRECT 3 inventory items** (canonical §6 rows 1, 2, 3): module count 34→31; `Sprk.Bff.Api.Options` namespace does NOT exist (directory location issue only); `Configuration/` per-dir 25→21.

## Consequences

### Positive
- **Endpoint↔DI Symmetry Rule** is the highest-leverage architectural finding of the audit — codifies a binding rule that would have caught the LATENT BUG at PR-review time AND prevents future §F.1 anti-patterns recursively through transitive ctor chains.
- **Runtime §F.1 Detection Fixture** closes the loop — generalizes `bff-extensions.md` §F.1 from comment-block-verifiable to runtime-verifiable. Future CI integration is straightforward.
- Per-concern DI module composition retained — 31 modules avoid collapse-induced coupling.
- 35 options classes retained — per-feature options with strong typing + validation.
- 3 inventory framing corrections close real misperceptions about the namespace topology.
- `Options/` → `Configuration/` directory consolidation is a trivial `git mv` with zero behavior change.
- Cross-team load-bearing patterns surface: Symmetry Rule + Runtime fixture become reusable across all BFF-adjacent projects.

### Negative
- Endpoint↔DI Symmetry Rule has BROAD reach — every BFF-touching project must apply it on every conditional registration. Requires CLAUDE.md §10 amendment + `bff-extensions.md` §F new sub-mechanism (§F.4) authoring.
- Runtime §F.1 Detection Fixture authoring (~1-2 days Insights team effort) is non-zero cost; lands as Insights-team-owned but applies to all teams' endpoints.
- Cross-team coordination cost: Insights team owns LATENT BUG remediation + Runtime fixture; all other BFF teams must adopt the Symmetry Rule going forward.

### Migration impact
- **Cross-team coordination**: Insights team (LATENT BUG owner + Runtime fixture author); all BFF-touching teams (Symmetry Rule adoption going forward). Sequential per Q-003 lock.
- **Effort estimate**: **M (Medium)** — ~280 LOC remediation (per DR-003) + ~1-2 days Runtime fixture authoring + 2 file moves (`Options/` → `Configuration/` via `git mv`) + inventory-correction PR.
- **Sequencing**: HIGH-priority bundled PR. The Symmetry Rule codification + LATENT BUG remediation should land together as a single coherent unit. Runtime fixture can ship as a follow-on (HIGH priority).

## Canonical naming (Q-004 — surfaced not locked)

- **Candidate names** (2 NEW W4 load-bearing names):
  - "Spaarke Endpoint↔DI Symmetry Rule" (LOAD-BEARING — the audit's highest-leverage NEW finding)
  - "Spaarke DI Module Audit Convention" (descriptive over `AiModule.cs:269-313` gold-standard)
- **Reference impls**:
  - Per-concern DI modules: 31 modules at HEAD under `src/server/api/Sprk.Bff.Api/Infrastructure/DI/` (29) + `Api/Reporting/` (1) + `Workers/Office/` (1)
  - DI Module audit-table convention: `AiModule.cs:269-313` (15/15 ADR-010 budget cap; line-numbered + task-tagged registrations)
  - Per-feature options classes: 35 options classes; single namespace `Sprk.Bff.Api.Configuration`
  - Compound AI gate: `AnalysisServicesModule.cs:18-117` (3-tier composition)
  - Post-LATENT-BUG-remediation `AddPublicContractsFacade` (per DR-003)
- **Pattern elements (DI Module)** (4):
  1. Per-concern composition criterion (NOT functional area collapse)
  2. ADR-010 cap mechanism (`AiModule` 15/15 cap; promotions to `AnalysisServicesModule` documented per Tier 1.5)
  3. Inline audit-table comment block (registration line numbers + originating task references)
  4. `§F.1 inspection` comment block on conditional registrations (current discipline)
- **Pattern elements (Options class)** (4):
  1. Single namespace `Sprk.Bff.Api.Configuration`
  2. `AddOptions<T>().BindConfiguration(...)` newest canonical binding pattern
  3. Per-options const `SectionName` + DataAnnotations + per-field XML doc citing originating task/SPEC/POML
  4. `ValidateDataAnnotations().ValidateOnStart()` chaining
- **Pattern elements (Compound Gate)** (3):
  1. 2 compound flags = entire AI stack inert with 10 Null peers (operational simplicity)
  2. Fine-grained sub-gates INSIDE compound-ON branch (e.g., `Insights:IntentClassifier:Enabled` @ AnalysisServicesModule:508-534)
  3. Resource-prerequisite sub-gates (e.g., AI-Search-keys @ `AddRagServices:539-561`)

## ADR candidates from this decision (Q-005 — bullets only)

- **W4-1** Endpoint↔DI Registration Conditionality Symmetry Rule (FORMAL) — HIGH priority (LOAD-BEARING audit finding)
- **W4-2** §F.1 Runtime-Verifiable Detection Mechanism — HIGH priority (closes the loop)
- **W4-3** DI Module Audit Comment Convention — MEDIUM priority (every DI module SHOULD have inline audit table mirroring `AiModule.cs:269-313`)
- **W4-4** DI Module Per-Concern Composition Principle — MEDIUM priority (pattern doc; REJECTS forced consolidation)
- **W4-5** Canonical Options Class Design Pattern — MEDIUM priority (per-feature options with const SectionName + DataAnnotations + XML doc)
- **W4-6** Compound AI Gate Pattern + Fine-Grained Sub-Gate Composition — MEDIUM priority (3-tier compound gate model)
- **W4-7** `Sprk.Bff.Api.Options` → `Sprk.Bff.Api.Configuration` directory consolidation — LOW priority (trivial directory consolidation)

## Open questions for owner review (Q-002)

1. **REJECT consolidation confirmation** (canonical §11.2 Q-10): Owner accepts per-concern split (31 modules + 35 options + compound gate)?
2. **Endpoint↔DI Symmetry Rule codification** (canonical §11.4 Q-18): Standalone ADR + add to `bff-extensions.md` §F as new sub-mechanism §F.4, OR standalone ADR section?
3. **Runtime §F.1 detection fixture authorship** (canonical §11.4 Q-19): Authorize Insights team to author (~1-2 days)?
4. **`AddPublicContractsFacade` canonical lock** (canonical §11.3 Q-15+Q-16): Owner locks "Spaarke Public-Contracts Facade DI Fascia" + "Spaarke Endpoint↔DI Symmetry Rule" as load-bearing canonical names?
5. **`Options/` → `Configuration/` directory consolidation** (canonical §11.7 Q-29): Bundle with inventory-correction PR OR standalone tidy PR?
6. **DI Module audit-table convention retrofit** (canonical §11.7 Q-30): Retrofit non-`AiModule` modules with `AiModule.cs:269-313`-style audit tables? Owner-team-driven.
7. **CLAUDE.md §10 amendment**: Add Endpoint↔DI Symmetry Rule as new §10 bullet point (binding for BFF additions), or document via `bff-extensions.md` §F.4 only?

## References

- Source analysis: [`notes/phase2/analysis-di-configuration.md`](../notes/phase2/analysis-di-configuration.md)
- Wave summary: [`notes/phase2/wave-4-summary.md`](../notes/phase2/wave-4-summary.md)
- Canonical authority: [`notes/canonical-architecture-decisions.md`](../notes/canonical-architecture-decisions.md) §1.3 (NEW W4 patterns) + §2.8 (DI Layer) + §3 + §4 (LATENT BUG + Symmetry Rule) + §5.3 (cross-sub-agent validation) + §6 (inventory corrections rows 1, 2, 3) + §8 + §11.2 Q-10 + §11.3 Q-15+Q-16 + §11.4 Q-18+Q-19 + §11.7 Q-29+Q-30
- Related ADR candidates: W4-1 (HIGH — LOAD-BEARING), W4-2 (HIGH), W4-3/W4-4/W4-5/W4-6 (MEDIUM), W4-7 (LOW)
- Related DRs:
  - **DR-003** — LATENT BUG is the canonical instance of the Endpoint↔DI Symmetry Rule; Option A structural remediation is the surgical fix
  - **DR-001** — Lookup module DI cleanup (`FinanceModule.cs`)
  - **DR-002** — Cache stack DI registrations (specialist wrappers + helper)
  - **DR-004** — `INodeExecutor` DI registry pattern + Runtime Kill-Switch Pattern (distinct from DI Null-Object)
  - **DR-005** — Intent classifier DI lifetimes (Singleton + `IOptions<T>` binding; `NullInsightsIntentClassifier` dual registration)
  - **DR-006** — Double-Gate Null-Object Pattern (peer to ADR-030 single-gate; `RagService`/`NullRagService` gold-standard)
  - **DR-007** — `OrchestratorPromptBuilder` ADR-009 in-process exception XML doc (intersection with DR-008's compound gate)
- ADR cross-references: ADR-010 (interface budget cap), ADR-013 (facade-over-internal-SDK), ADR-018 (compound feature gates), ADR-030 (Null-Object Kill-Switch — single-gate; peer to Double-Gate per DR-006), ADR-032 (Null-Object P3 Fail-Fast), `bff-extensions.md` §F (asymmetric-registration anti-pattern + proposed §F.4 sub-mechanism)
- Inventory corrections from this category: §6 rows 1 (module count 34→31), 2 (Options namespace nonexistent), 3 (Configuration per-dir 25→21)
