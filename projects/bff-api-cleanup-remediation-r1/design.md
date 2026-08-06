# BFF API Cleanup & Remediation — Design Document

> **Slug**: `bff-api-cleanup-remediation-r1` (a **surface workstream**, not a standalone project)
> **Status**: DRAFT — for owner review before `/design-to-spec`
> **Author**: Assessment run 2026-08-05/06 (read-only audit, 6 parallel investigations + Fable verification pass). No code modified during assessment.
> **Scope target**: `src/server/api/Sprk.Bff.Api/` (+ the `Spaarke.Dataverse` / `Spaarke.Core` shared assemblies it consumes)
> **Governance**: BINDING — this project adds/removes code to the BFF, so root [`CLAUDE.md` §10 (BFF Hygiene)](../../CLAUDE.md) + [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) apply. Hot-path declaration + Placement Justification below.
> **Program**: The **BFF surface workstream** (#1) of the [`code-quality-and-assurance-r3`](../code-quality-and-assurance-r3/design.md) quality program. Per the owner's single-worktree decision (r3 §4/§4A), this executes **in the r3 worktree on the `work/code-quality-and-assurance-r3` branch** — NOT as a separate project/worktree. This folder is a semantic home for the BFF surface's design + findings. The A/B tranche split below still applies, as small PRs off the r3 branch (`/conflict-check` each).

---

## 0. Summary & verdict

Routine-maintenance review of the BFF API. The BFF is **structurally healthy and unusually well-governed** — ADR-032 null-object kill-switch gating is applied consistently, there are no captive-dependency bugs, publish size is healthy (**46.89 MB compressed incl. PDBs**, ceiling 60 MB), and the endpoint surface is largely clean. The problems are **accumulated redundancy, dead weight, and two latent production bugs** — not rot.

This project is a bounded, mostly-mechanical cleanup with **two genuine correctness fixes** and **one security decision that needs the owner**. It removes ~2,700 LOC of production dead code (+~1,150 test LOC), collapses 13 copy-pasted downcast helpers into one extension, fixes an always-failing cast on the invoice-totals path, closes an unauthenticated Dataverse-write exposure, finishes an abandoned folder migration, and improves reviewability of the DI layer.

Verified cleanliness scorecard:

| Area | Grade | Note |
|---|---|---|
| DI correctness (gating, lifetimes, dupes) | **A–** | ADR-032 applied rigorously; no captive deps; one 75-registration monolith (`CommunicationModule`) |
| Publish size / dependencies | **A** | 46.89 MB compressed; no HIGH CVE surfaced; package pins documented |
| Endpoint / facade hygiene | **B** | 4 facade-boundary violations; incomplete `Endpoints/`→`Api/` migration |
| Auth consistency | **B–** | 1 anonymous Dataverse-**write** endpoint (owner decision); 2 unguarded health probes; 2 files anonymous-by-omission |
| Service redundancy | **C+** | 13× copy-pasted downcast (+3 broken), triple `.eml` builder, financial-handler tangle, orphaned retry helper |
| Dead code | **C** | ~2.7k LOC provably dead in `src/` |
| Repo hygiene | **D** | 127 MB build artifacts in source tree; 2 tarballs committed to git |

**Two latent production bugs found during verification (promote to first-class tasks):**
1. **Invoice financial-totals path is broken.** `FinanceRollupService.cs:228` and `Services/Finance/Tools/FinancialCalculationToolHandler.cs` (~lines 140-146, 205-211) cast `IDataverseService as ServiceClient`. The only registered impl (`DataverseServiceClientImpl`) implements `IDataverseService` but does **not** derive from `ServiceClient`, so those casts **always fail** → `InvalidOperationException`. `FinancialCalculationToolHandler.ExecuteAsync` is invoked from `InvoiceExtractionJobHandler.cs:253`.
2. **~450 LOC of dead builder code in `EmailToEmlConverter`** with a conflicting dual registration (scoped in `EmailServicesModule.cs:44-46`, singleton in `OfficeWorkersModule.cs:31`).

---

## 1. Problem statement

The BFF is the single backend for every Spaarke client surface and has grown feature-by-feature across ~15 projects (R1–R7, Insights Engine, Communication, Compose, Notification Spine, etc.). Per the [2026-05-20 BFF AI extraction assessment](../../docs/assessments/bff-ai-extraction-assessment-2026-05-20.md), the codebase is structurally AI-dominant (69% of `Services/` LOC) and operationally justified to stay unified — but that assessment also surfaced the process debt (20 CRUD→AI couplings, publish-size creep) that §10 governance now guards against.

This review finds the *next layer* of debt: not architectural drift, but **duplication and dead code that accumulated below the governance threshold** — copy-paste helpers, superseded-but-not-deleted services, an abandoned folder migration, and — critically — two duplications that silently masked real bugs. Left alone, this raises maintenance cost, review burden, and the risk that the next reviewer copies a broken pattern (the `as ServiceClient` cast is already copied 3×).

**Evidence base**: 6 parallel read-only investigations (DI, services, endpoints) + a 3-agent Fable verification pass that confirmed exact file lists, LOC, test-consumer status, and reachability. Corrections the verification pass made to first-pass claims are noted inline (e.g., the Todo double-layer and `StubInsightGraph` are intentional and MUST be kept).

---

## 2. Goals

- **G1** — Delete provably-dead production code (~2,700 LOC) with zero behavior change, verified by build + full test suite + publish-size diff.
- **G2** — Fix the two latent production bugs (broken invoice-totals cast; dead/duplicate-registered `.eml` builder).
- **G3** — Collapse the 13 copy-pasted `IDataverseService`→`ServiceClient` downcasts into one shared, tested extension (kills the bug class permanently).
- **G4** — Close the auth gaps: harden the two `/healthz/dataverse*` probes; bring the `FinanceRollupEndpoints` exposure to an explicit owner decision + implement it.
- **G5** — Bring the 4 AI-facade boundary violations into `PublicContracts/` compliance (BFF §10 bullet 3 / refined ADR-013).
- **G6** — Finish the `Endpoints/`→`Api/` folder migration (zero route change) and delete the legacy folder.
- **G7** — Improve DI reviewability: decompose the 75-registration `CommunicationModule` monolith into cohesive helpers.
- **G8** — Repo hygiene: remove the 127 MB of build artifacts from the source tree; untrack the 2 committed tarballs; confirm `.gitignore` coverage.
- **G9** — Reduce publish size (measure the delta; expect a small reduction from dead-code removal).

### Non-goals

- **NG1** — Unifying the two Dataverse access stacks (ServiceClient vs raw-HTTP, ~22 files, differing OBO/app-only auth). This is a genuine architecture project (~25-30 files, 4-6 weeks, high risk, needs its own ADR). Explicitly deferred; this project only consolidates the *boilerplate within the app-only HTTP camp* if a low-risk task fits.
- **NG2** — Merging the two live `.eml` builders (`EmlGenerationService`, `GraphMessageToEmlConverter`) — their divergence is the point. Only the *dead* third builder is removed.
- **NG3** — Merging the two distinct R6 `IToolHandler` financial handlers — they are deliberately different (formula math vs figure aggregation). Only the name collision + broken cast are fixed.
- **NG4** — Migrating the `[Obsolete]` members that still have live callers (`GenericAnalysisHandler`, `DemoProvisioningOptions`, `EmailProcessingOptions`) — those are follow-on migrations, not this cleanup.
- **NG5** — Introducing base classes for the 28 `Ai/Handlers` + 27 `Ai/Nodes` (a large refactor of live dispatch surface). Noted as a backlog candidate; out of scope here to keep risk low.
- **NG6** — Any BFF↔microservice extraction (covered by the 2026-05-20 assessment; deferred).

---

## 3. Current-state inventory (verified)

### 3.1 Dead code — safe deletions (G1)

Verified against `src/` **and** the `tests/` tree (BFF exposes internals to 3 test assemblies via `InternalsVisibleTo`, so test refs were checked explicitly).

| Item | Path(s) | Prod LOC | Test LOC to update | Verdict |
|---|---|---:|---:|---|
| Scopes folder | `Services/Scopes/{ScopeInheritanceService,ScopeCopyService,OwnershipValidator}.cs` | 1,285 | 0 (prose comment only) | **DELETE** — no DI reg, no endpoint, only cross-refs each other |
| Safety cluster | `Services/Ai/Chat/Middleware/SafetyPipelineMiddleware.cs` + `Services/Ai/Safety/ConfidenceScoringService.cs` + `IConfidenceScoringService.cs` + `ConfidenceScoringRequest.cs` + `ConfidenceScoringResult.cs` + `ConfidenceLevel.cs` | 840 | 831 (2 test files) | **DELETE + update tests** — unwired; superseded by `PromptShieldChatMiddleware`; fix 2 stale `<see cref>` (CS1574, non-breaking) |
| Orphaned retry helper | `Infrastructure/Resilience/RetryPolicies.cs` | 298 | 0 | **DELETE** — zero refs anywhere; namespace stays alive via siblings |
| Dead resolver | `Services/Insights/LiveFacts/StubLiveFactResolver.cs` | 56 | 0 | **DELETE** (defensible KEEP if team wants the break-glass seam; verifiably dead today) |
| Archived src | `Services/BackgroundServices/_archive/JobProcessor.cs.archived-2025-10-03` + `Infrastructure/Resilience/_archive/RetryPolicies.cs.archived-2025-10-01` | 222 | — | **DELETE** — not compiled; remove empty `_archive/` folders |
| Archived test | `tests/unit/Sprk.Bff.Api.Tests/JobProcessorTests.cs.archived-2025-10-14` | — | 318 | **DELETE** — not compiled |
| **Total** | | **~2,701 prod** | **~1,149 test** | **~3,850 LOC** |

**Explicit KEEPs (first-pass audit was wrong — do NOT delete):**
- `Services/Insights/Graph/StubInsightGraph.cs` — **WIRED** at `InsightsModule.cs:53` (`AddSingleton<IInsightGraph, StubInsightGraph>`), live D-P17 swap-path seam.
- `Services/Todo/NullObject/Null*.cs` (4) **and** `Services/Todo/Placeholder/NotImplemented*.cs` (4) — **both runtime-selected** by a factory on `Spaarke:Graph:TodoSync:Enabled` at `TodoSyncModule.cs:84-98`. Canonical ADR-032 P2 pattern (quiet-no-op vs fail-loud). Both tested.

### 3.2 Latent production bugs (G2)

- **Bug-1 (broken cast, invoice-totals):** `FinanceRollupService.cs:228-236`, `Services/Finance/Tools/FinancialCalculationToolHandler.cs:140-146` and `~205-211` cast `IDataverseService as/is ServiceClient` — always fails under the sole `DataverseServiceClientImpl` registration. Path is live (`InvoiceExtractionJobHandler.cs:253`). Fixed for free by G3's extension.
- **Bug-2 (dead builder + dual reg):** `Services/Email/EmailToEmlConverter.cs` — `ConvertToEmlAsync` + `GenerateEmlFileNameAsync` + `BuildMimeMessage` + `Fetch*` (~450 LOC) have zero production callers; only the parser half `ExtractAttachments` (line 681) is live (`UploadFinalizationWorker.cs:1047`). Registered twice (scoped `EmailServicesModule.cs:44-46`; singleton `OfficeWorkersModule.cs:31`).

### 3.3 Redundancy — consolidations (G3)

- **13 copy-pasted downcast implementations / 17 call sites** of `IDataverseService`→`ServiceClient`, across `Services/Dataverse/*` (5), `Services/Finance/*` (3, incl. 2 broken), `Services/Workspace/TodoGenerationService.cs`, `Services/Ai/Membership/MembershipFieldDiscoveryService.cs`, + inline copies in `FetchService.cs`, `UserPrivilegeChecker.cs`, `Finance/Tools/FinancialCalculationToolHandler.cs` (×2). `Services/Dataverse/Extensions/DataverseServiceExtensions.cs` exists but has **no** unwrap helper. Fix: add `UnwrapServiceClient(this IDataverseService, string consumerName)` (throws) + `TryUnwrapServiceClient(...)` (null+log) — one place, ideally in `Spaarke.Dataverse` next to `DataverseServiceClientImpl`.
- **Financial-handler tangle:** two distinct R6 `IToolHandler`s (`Services/Ai/Handlers/FinancialCalculationToolHandler.cs`, `FinancialCalculatorHandler.cs`) — KEEP both — plus a third, **name-colliding** `Services/Finance/Tools/FinancialCalculationToolHandler.cs` (`IAiToolHandler`, DI-registered, directly injected). Fix: **rename** the Finance/Tools one (e.g. `FinanceTotalsCalculator`), fix its broken cast (G3). ⚠ **Dataverse pre-check required**: `IToolHandler` dispatch is data-driven via `sprk_analysistool.sprk_handlerclass` rows — do NOT touch any `HandlerId` string; verify the two `Ai/Handlers` rows before any change.
- **Triple `.eml` builder:** `EmlGenerationService` (133 LOC, live), `GraphMessageToEmlConverter` (229 LOC, live), `EmailToEmlConverter` (886 LOC, builder half dead — see Bug-2). Fix = delete dead half + unify the double registration + share only the Windows-strict `SanitizeFileName` helper. Do **not** merge the two live builders (NG2).
- **Orphaned `RetryPolicies.cs` vs 4-5 resilience sites:** the orphan is v7-era and *less* capable than the live sites (which honor Graph `Retry-After`, use v8 pipelines, integrate `ICircuitBreakerRegistry`). Fix = delete the orphan (G1). Optional low-priority: extract one shared circuit-breaker-registry wiring helper across `OpenAiClient`/`ResilientSearchClient`/`GraphHttpMessageHandler`. Do **not** unify the retry strategies.

### 3.4 Facade boundary violations (G5)

Non-AI code injecting AI-internal types (must use `Services/Ai/PublicContracts/`). Reference pattern: `CommunicationEnrichmentService` + `IActionSeam`/per-consumer facades.

| # | Site | Injected internal type | Fix |
|---|---|---|---|
| A-1 | `Api/Workspace/WorkspaceFileEndpoints.cs:153-154,264-265` | `IActionResolver`+`IActionRunner` | New `PublicContracts` facade (e.g. `IFileSummarizeAi`) mirroring `CommunicationTriageAi` (which legally wraps resolver+runner); preserve SSE chunk + 503 semantics |
| A-2 | `Services/Workspace/MatterPreFillService.cs:50-51,98-99` (+`IPlaybookLookupService` at 38,93) | `IActionResolver?`+`IActionRunner?` | Extend already-injected `IWorkspacePrefillAi` with `RunPrefillActionAsync(...)`; one method serves A-2 + A-3 |
| A-3 | `Services/Workspace/ProjectPreFillService.cs:41-42,89-90` (+`IPlaybookLookupService` at 32,84) | `IActionResolver?`+`IActionRunner?` | Same facade method as A-2 |
| A-4 | `Services/Workspace/WorkspaceAiService.cs:60,79` | `IPlaybookLookupService` | Relocate `IPlaybookLookupService` into `PublicContracts/` (cached lookup, no LLM internals) — also clears the extra A-2/A-3 injections |

**Note:** `IActionSeam` covers only Layer-A writes (`CreateNotification/CreateTask/UpdateRecord`) — it has no "resolve Binding → run Action" method, so A-1 genuinely needs a new facade method (this is justified new surface — see §9).

### 3.5 Auth consistency (G4)

- **B-1 (owner decision):** `Api/Finance/FinanceRollupEndpoints.cs:29,46` — both `POST .../recalculate` are `.AllowAnonymous()` and **write** derived financial fields to Dataverse under the BFF app identity, protected only by rate-limiting. No filter/HMAC/secret (handler read fully). Its own comment claims it "follows ScorecardCalculatorEndpoints exactly" but the scorecard sibling calls `.RequireAuthorization()`. Exposure = GUID-guessable integrity-churn / DoS / 404-vs-200 enumeration (values are derived, not attacker-controlled). Caller is a legacy web resource (`sprk_subgrid_parent_rollup.js`).
- **B-2 (clear fix):** `/healthz/dataverse` + `/healthz/dataverse/crud` (`EndpointMappingExtensions.cs:64-65`) have neither auth nor rate-limiting and hit Dataverse live; the sibling `/healthz/dataverse/doc/{id}` was hardened with `.AllowAnonymous().RequireRateLimiting("anonymous")`. Also both echo raw `ex.Message`. Fix: match the hardened sibling + stop echoing exception detail.
- **B-3 (owner decision, low real exposure):** `Api/OBOEndpoints.cs` (7 endpoints) + `Api/UserEndpoints.cs` (2) are anonymous-by-omission (no global fallback policy exists — confirmed no `SetFallbackPolicy`). Real exposure is low (every handler forces OBO exchange → 401-by-crash on missing bearer), but that is not policy enforcement. Recommend adding explicit `.RequireAuthorization()`.

### 3.6 Structure & hygiene (G6/G7/G8)

- **`Endpoints/`→`Api/` migration (G6):** 6 files still in the legacy `Endpoints/` folder under `Sprk.Bff.Api.Endpoints.*`, all still mapped, split across two namespaces. Moves are **namespace-only, zero route change** (route strings are literals; extension-method names unchanged). Both target namespaces already imported. 5 test-line usings + the `_archive` follow-up. Then delete the `Endpoints/` tree.
- **`CommunicationModule.cs` (G7):** 490 lines, 75 registrations, **one method, zero helpers**. Extract `AddChannelSenders` / `AddAssociationEngine` (11 `IAssociationRung` + 4 `IStructuralDetector`) / `AddThreadResolution` / `AddMembershipReconciliation` / `AddCommunicationHostedServices`. (Contrast: the 122KB `AnalysisServicesModule` is large but *already* decomposed into 13 helpers — leave it.)
- **Repo hygiene (G8):** 127 MB of build/deploy artifacts in the source dir — `app.zip`, `deploy.zip`, `deployment.zip`, `bff-api-deploy.zip` (gitignored, local clutter) + **`deployment.tar.gz` and `spe-bff-api-deployment.tar.gz` committed to git**. Untrack the 2 tarballs, delete all 6 from the working tree, confirm `.gitignore` covers the patterns.

---

## 4. Design principles

1. **Behavior-preserving by default.** Every task except the two bug-fixes (G2) and the auth changes (G4) must be provably behavior-neutral: `dotnet build` + full `dotnet test` + publish-size diff as the acceptance gate.
2. **Delete > deprecate.** Dead code is removed, not `[Obsolete]`-tagged, once verified across `src/` + `tests/`.
3. **One place per concept.** The downcast, the `.eml` filename sanitizer, and the facade boundary each converge to a single canonical location.
4. **Respect the intentional patterns.** ADR-032 null-object layers, the two distinct R6 handlers, the two live `.eml` builders, and the `StubInsightGraph` seam are deliberate — verified and preserved.
5. **Data-driven dispatch is not grep-provable.** Anything dispatched by a Dataverse `sprk_*` row (handlers, tools) requires a Dataverse config check before rename/delete.
6. **Small, reviewable, revertible commits.** Grouped by workstream; each task independently mergeable.

---

## 5. Proposed workstreams → phases

Ordered by risk-adjusted value. Each bullet is a candidate task for `/task-create`.

**Phase 1 — Dead code & hygiene (low risk, high LOC).** FULL rigor (touches `tests/`).
- 1a. Delete Scopes folder (3 files).
- 1b. Delete Safety cluster (6 src files + 2 test files); fix 2 stale crefs.
- 1c. Delete orphaned `RetryPolicies.cs` + both `_archive/` folders + archived test.
- 1d. Delete `StubLiveFactResolver` (pending owner nod on the break-glass seam).
- 1e. Repo hygiene: untrack 2 tarballs, delete 6 artifacts, verify `.gitignore`.
- Gate: build + full test + publish-size diff (expect small reduction).

**Phase 2 — Correctness fixes (the two bugs).** FULL rigor.
- 2a. Add `UnwrapServiceClient` / `TryUnwrapServiceClient` extensions; replace all 13 copies / 17 call sites; **fix the 3 broken casts**; add a focused test for the invoice-totals path.
- 2b. Remove dead builder half of `EmailToEmlConverter`; unify the dual registration; share the Windows-strict `SanitizeFileName`.

**Phase 3 — Auth.** FULL rigor.
- 3a. Harden `/healthz/dataverse` + `/healthz/dataverse/crud`; stop echoing `ex.Message`.
- 3b. Implement the owner-chosen `FinanceRollupEndpoints` decision (see §6) — verify the web-resource caller first.
- 3c. Add explicit `.RequireAuthorization()` to `OBOEndpoints` (7) + `UserEndpoints` (2) — pending owner nod.

**Phase 4 — Facade compliance (§10 bullet 3).** FULL rigor.
- 4a. Relocate `IPlaybookLookupService` into `PublicContracts/` (clears A-4 + the A-2/A-3 extra injections).
- 4b. Add the linear resolve+run facade method(s) (`IFileSummarizeAi` for A-1; `IWorkspacePrefillAi.RunPrefillActionAsync` for A-2/A-3); swap the 3 consumers; update the `AnalysisServicesModule.cs:169` comment.

**Phase 5 — Structure (optional/lower priority).** STANDARD rigor.
- 5a. Decompose `CommunicationModule.cs` into helpers (behavior-neutral).
- 5b. Rename `Services/Finance/Tools/FinancialCalculationToolHandler` after Dataverse pre-check.
- 5c. Finish `Endpoints/`→`Api/` migration; delete the legacy folder.
- 5d. (Optional) shared circuit-breaker-registry wiring helper.

**Phase 6 — Wrap-up.** `090-wrapup` task → `/test-diet` gate + final publish-size report + doc-drift audit.

---

## 6. The security decision (owner input required)

🔔 **ADR/Security — Resolution Required (per root CLAUDE.md §6 / §6.5)**

- **Item**: `Api/Finance/FinanceRollupEndpoints.cs` — two anonymous Dataverse-write endpoints.
- **Options**:
  - **(A)** Add `.RequireAuthorization()` (matches the scorecard sibling) — **breaks** the legacy `sprk_subgrid_parent_rollup.js` web-resource caller unless it is migrated to `@spaarke/auth`.
  - **(B)** Add an HMAC/shared-secret endpoint filter (like the Communication/Compose webhook pattern) — keeps the web-resource caller working, closes the anonymous exposure.
  - **(C)** Accept as-is (documented risk) and only fix the misleading "follows ScorecardCalculatorEndpoints exactly" comment.
- **Recommendation**: **(B)** — closes the exposure without a client rewrite; consistent with existing web-resource-facing patterns. Confirm the caller can compute the HMAC.
- **Same call applies to B-3** (`OBOEndpoints`/`UserEndpoints` explicit `.RequireAuthorization()`): recommend yes (defense-in-depth; real exposure already low).

---

## 7. Hot-Path Declaration (BINDING — CLAUDE.md §10 / bff-extensions §G)

```xml
<hot-path-declaration>
  <bff>Y</bff>                 <!-- entire project is BFF cleanup: deletions, consolidations, auth, facade, DI decomposition -->
  <spaarkeai>N</spaarkeai>     <!-- no src/solutions/SpaarkeAi changes -->
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```

BFF=Y → publish-size check (≤60 MB compressed) on every BFF-touching task; baseline **46.89 MB compressed incl. PDBs (2026-08-05)**. **No new NuGet packages** — this project only removes/consolidates code. `/conflict-check` MUST run before each BFF PR against [`projects/INDEX.md`](../INDEX.md) (13 of 17 active worktrees touch BFF — coordination risk is real, especially for the Finance/Communication/Email files).

---

## 8. Placement Justification (BINDING — CLAUDE.md §10)

This project **removes and consolidates** BFF code; it introduces no new server subsystem. The only new surface is:
- **`UnwrapServiceClient`/`TryUnwrapServiceClient`** extension — belongs in `Spaarke.Dataverse` next to `DataverseServiceClientImpl` (it is Dataverse-plumbing, consumed BFF-wide). Justified: replaces 13 copies + fixes a bug class.
- **New `PublicContracts` facade method(s)** (`IFileSummarizeAi`, `IWorkspacePrefillAi.RunPrefillActionAsync`) — belong in `Services/Ai/PublicContracts/` **by definition** of the §10 bullet-3 rule they implement. This *reduces* CRUD→AI coupling (moves 4 violations behind the facade).

No AI-internal type is newly injected into CRUD code; the net change to the §10 coupling count is **negative**.

## 9. Component Justification (§11 three-question gate)

| New component | Existing overlap (grep-verified) | Extend instead? | Cost-of-doing-nothing (concrete) |
|---|---|---|---|
| `UnwrapServiceClient`/`TryUnwrapServiceClient` ext | `DataverseServiceExtensions.cs` (DI-only, no unwrap); 13 private copies | This IS the extraction of 13 copies into one | 13 copies persist; the 3 broken casts stay broken (invoice totals error at runtime) |
| `IFileSummarizeAi` facade (A-1) | `IActionSeam` (writes only), `IWorkspacePrefillAi` (playbook SSE, not linear actions), `CommunicationTriageAi` (pattern to mirror) | No existing facade exposes "resolve Binding → run linear Action" for file summarize | A-1 keeps injecting `IActionResolver`/`IActionRunner` into a non-AI endpoint — §10 violation persists |
| `IWorkspacePrefillAi.RunPrefillActionAsync` (A-2/A-3) | `IWorkspacePrefillAi` already injected by both services | **Extend** the already-injected facade — one method serves both | A-2/A-3 keep the internal injection |
| `IPlaybookLookupService` relocation (A-4) | Same interface, just under wrong namespace | This is a **move**, not new surface | A-4 + the A-2/A-3 secondary injection stay non-compliant |

All other work is deletion, rename, or in-place consolidation — no new surface, so §11 does not apply.

## 10. ADR / constraint tensions

- **ADR-013 / §10 bullet 3 (AI facade)**: this project *enforces* it (Phase 4) — no tension, it's the point.
- **ADR-032 (null-object kill-switch)**: respected — the Todo double-layer and `StubInsightGraph` are preserved as ADR-032 seams.
- **ADR-038 (testing)**: Phases 1-4 modify `tests/**` → TEST-MODIFYING rigor override applies → `code-review` + `adr-check` run unconditionally; `090-wrapup` runs `/test-diet`.
- **ADR-010 (DI minimalism)**: Phase 5a decomposition keeps registrations identical (helpers only) — no new abstractions.
- No ADR *amendment* is anticipated. If the Finance auth decision (§6) lands on (C) "accept documented risk," that is a Path-A project-scoped exception to be recorded in `spec.md` + PR description.

## 11. Deliverables

- Cleaned BFF: ~2,700 fewer prod LOC, 13 downcasts → 1 extension, 2 bugs fixed, auth gaps closed, facade-compliant Workspace code, finished folder migration, decomposed `CommunicationModule`.
- Repo: 2 tarballs untracked, 127 MB artifacts removed, `.gitignore` verified.
- `notes/publish-size-report.md` (before/after compressed delta).
- `notes/dataverse-precheck.md` (financial-handler `sprk_analysistool` verification).
- Updated `src/server/api/Sprk.Bff.Api/CLAUDE.md` if any documented pattern changes.

## 12. Acceptance criteria (draft — for `/design-to-spec` to make a closed set)

- [ ] `dotnet build src/server/api/Sprk.Bff.Api/` clean; full `dotnet test` green after every phase.
- [ ] All 6 dead-code items removed; grep confirms zero dangling refs; both `_archive/` folders gone.
- [ ] Zero `IDataverseService as/is ServiceClient` casts remain outside the shared extension; invoice-totals path has a passing test.
- [ ] No non-AI file injects an `IActionResolver`/`IActionRunner`/`IPlaybookLookupService` (grep-verified); `AnalysisServicesModule.cs:169` comment updated.
- [ ] `/healthz/dataverse*` probes rate-limited + no `ex.Message` leak; Finance auth decision implemented per §6.
- [ ] `Endpoints/` folder deleted; route table byte-identical (route-dump diff).
- [ ] 2 tarballs `git rm --cached`; publish size ≤ 60 MB compressed, reported with delta vs 46.89 MB baseline.
- [ ] `/conflict-check` clean (or coordinated) against active BFF worktrees before each PR.

## 13. Risks

| Risk | Mitigation |
|---|---|
| Coordination collisions (13/17 active worktrees touch BFF) | `/conflict-check` before each PR; sequence Finance/Communication/Email tasks; prefer small PRs |
| Data-driven dispatch hides a live consumer (handlers/tools) | Dataverse pre-check task before any rename; never touch `HandlerId`/`sprk_handlerclass` strings |
| Facade change alters SSE/503 semantics (A-1) | Byte-compatible chunk contract as an explicit acceptance criterion; keep validation in the endpoint |
| `StubLiveFactResolver` / break-glass seam genuinely wanted | Owner nod before deleting (§5 1d); trivially KEEP-able |
| Auth hardening breaks the legacy rollup web resource | Verify caller first; prefer HMAC (option B) over `RequireAuthorization` |

## 14. Open questions (for `/design-to-spec` + owner)

1. **Finance auth** — which of §6 (A/B/C)? (Recommend B.)
2. **`OBOEndpoints`/`UserEndpoints`** — add explicit `.RequireAuthorization()`? (Recommend yes.)
3. **`StubLiveFactResolver`** — delete, or keep as documented break-glass seam?
4. **Scope of Phase 5** — include the optional circuit-breaker-wiring helper (5d) and the app-only HTTP boilerplate consolidation, or defer both to backlog?
5. **Should the two-Dataverse-stacks unification (NG1) be filed now** as a separate Idea/Epic on the portfolio so it isn't lost?
