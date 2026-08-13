# Code Quality & Assurance R3 — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-08-06
> **Source**: `projects/code-quality-and-assurance-r3/design.md` (program) + `projects/code-quality-and-assurance-r3/workstreams/bff-api/design.md` (BFF surface workstream #1)
> **Lineage**: r1 (quality *system* — tooling/scorecard, C→B) → r2 (first *structural* remediation, 17 tasks ✅, B→A-) → **r3 (standing quality *program*)**

## Executive Summary

R3 is a **standing quality program** — not a polish sprint — that (a) re-baselines the codebase grade honestly against a fixed rubric, (b) drives verified per-surface assessments to an **A+ senior-panel standard**, and (c) hardens forcing-functions so the grade *holds* as the codebase keeps growing. It runs as a **single project in one worktree** (`work/code-quality-and-assurance-r3`), with each code surface (BFF, shared client libs, shared server libs, PCF, Dataverse model, code pages, plugins) executed as a **workstream/phase** in one `TASK-INDEX.md`. The BFF surface is already assessed (workstream #1); the remaining surfaces run the same repeatable, **Fable-adversarially-verified** multi-agent Workflow assessment before their remediation is planned.

## Scope

### In Scope

- **Program scaffolding** (authored once, referenced by every surface):
  - `docs/standards/CODE-QUALITY-RUBRIC.md` — the single D1–D11 scoring contract + A–F scale.
  - A reusable **`quality-assessment` multi-agent Workflow** (fan-out per rubric dimension → **mandatory Fable adversarial-verification pass** → prioritized remediation `design.md`).
  - `notes/SCORECARD.md` — one living scorecard; each surface appends its verified row → program aggregate.
- **Phase 0 re-baseline** — re-score every surface against the rubric using the verified method; publish an honest current grade (supersedes the stale March "95/100"). **No aggregate grade is published until Phase 0 completes.**
- **Full per-surface assessment (all remaining surfaces, run FIRST)** — read-only, Fable-verified, producing a per-surface remediation `design.md` for each. Surfaces: shared client libs, shared server libs, PCF controls, Dataverse data model + ALM, code-page solutions + build/config sprawl, plugins. **Assessment documentation is the gating deliverable** that the remediation plan/tasks are built from.
- **BFF surface remediation (workstream #1 — already assessed, fully scoped)** — dead-code deletion (~2.7k LOC), 2 latent prod-bug fixes, 13→1 downcast consolidation, auth closure, AI-facade compliance, folder migration, DI decomposition, repo hygiene.
- **Forcing-functions** (§9 make-it-stick layer) — expanded `Spaarke.ArchTests` fitness functions; mechanical baseline (`TreatWarningsAsErrors` + analyzers + `.editorconfig`; strict ESLint + `tsc --noEmit`); CI gates (CVE, publish/bundle-size, doc-drift). **Activated per-surface** (each surface flips its own gate on as its last workstream step — no repo-wide big-bang).
- **Horizontal sweeps** — security (auth consistency via `@spaarke/auth`, secrets, XSS/injection, CORS), test quality (ADR-038 KEEP + `/test-diet`), dependency/CVE hygiene, observability, doc-drift.
- **Reconciliation & portfolio** — reconcile the archived 12-item R3 draft (§3, no item dropped silently); register the program under existing **Epic #427**; add the `projects/INDEX.md` hot-path row; file **NG1** (two-Dataverse-stacks unification) as a backlog **Idea**.

- **Deployment & configuration hygiene** (accepted 2026-08-13 from the r1 ask): a config-architecture assessment (#1, FR-24), uniform fail-fast config validation (#2, FR-25), the Phase-C credential-model finish (#3 / NG1 slice one, FR-26), and Graph app-role single-source constants (#4, FR-27) — with the ArchTest/CI forcing-functions that keep them.

### Out of Scope

- **Two Dataverse *access-stack* unification** (`ServiceClient` vs raw-HTTP client implementations, ~25–30 files, needs its own ADR) — **on the task-011 assess-then-decide track** (updated 2026-08-13): task 011 produces the verified NG1 design + fresh re-estimate; FR-26 lands the credential-model slice first (which lowers the remaining risk); remediation of the access-stack merge is decided on 011's design, not pre-committed here. Distinct from FR-26 (credential model) — the two are different axes. Idea #742 tracks the remaining access-stack work.
- **BFF↔microservice extraction** — covered by the 2026-05-20 assessment; deferred.
- **Merging the two live `.eml` builders** (`EmlGenerationService`, `GraphMessageToEmlConverter`) — divergence is intentional; only the *dead third* builder is removed.
- **Merging the two distinct R6 `IToolHandler` financial handlers** — deliberately different (formula math vs figure aggregation); only the name collision + broken cast are fixed.
- **Base classes for the 28 `Ai/Handlers` + 27 `Ai/Nodes`** — large live-dispatch refactor; backlog candidate.
- **Migrating `[Obsolete]` members with live callers** — follow-on migrations, not this cleanup.
- **New features of any kind** — the net code delta is behavior-preserving / removal-and-consolidation.

### Affected Areas

- `src/server/api/Sprk.Bff.Api/**` — BFF remediation (dead code, bugs, downcast, auth, facade, folder migration, DI). Hot path.
- `src/server/shared/Spaarke.Dataverse/**`, `Spaarke.Core/**` — `UnwrapServiceClient` extension home; consumed shared-server surface.
- `src/client/shared/Spaarke.*` (16 pkgs, ~39k LOC) — highest-leverage client surface; `@spaarke/auth` package hygiene.
- `src/client/pcf/**` (36 controls, ~49.5k LOC) — lifecycle/ADR-022/dead-control sweep.
- `src/solutions/**` (35 code pages, ~68k LOC) + 69 `package.json` roots — build/config-sprawl surface.
- `src/dataverse/plugins/**` — `BaseProxyPlugin` invert-vs-decommission.
- `docs/standards/CODE-QUALITY-RUBRIC.md` (new), `notes/SCORECARD.md` (new), `tests/**/Spaarke.ArchTests/**` (expanded).
- `.github/workflows/**` — CI gate wiring (coordinate with `ci-cd-unit-test-remediation-r1`).
- Legacy web resource `sprk_subgrid_parent_rollup.js` — migrate to `@spaarke/auth` (FR-09).

## Requirements

### Functional Requirements

**Program scaffolding**

1. **FR-01 (Rubric)**: Publish `docs/standards/CODE-QUALITY-RUBRIC.md` defining dimensions D1–D11 (Architecture, Correctness, Security, Performance, DRY/dead-code, Consistency, Testability, Dependency hygiene, Observability, ALM/build hygiene, Doc accuracy) with an A–F per-dimension → surface → aggregate scoring scale. — Acceptance: file exists; all 11 dimensions have explicit "what A+ looks like" criteria; referenced by every subsequent assessment.
2. **FR-02 (Assessment engine)**: Build a reusable `quality-assessment` multi-agent Workflow: parallel read-only finders (one per rubric dimension/cluster returning `file:line` evidence) → **mandatory Fable adversarial-verification stage** (verifies each finding, checks test-only consumers + data-driven dispatch, refutes false positives) → synthesized prioritized remediation `design.md`. — Acceptance: workflow runs end-to-end on one surface producing a verified design; manual agent fan-out documented as fallback only. Requires operator per-run opt-in ("use a workflow").
3. **FR-03 (Scorecard)**: Publish `notes/SCORECARD.md` living scorecard; each surface appends its verified per-dimension row at assessment/wrap-up → program aggregate view. — Acceptance: file exists with BFF row seeded; append-per-surface convention documented.
4. **FR-04 (Phase-0 re-baseline)**: Re-score every surface against the rubric via the verified method; publish honest current grades. — Acceptance: no aggregate grade published until every surface is scored; the stale March "A (95/100)" is explicitly superseded.

**Full assessment (gating deliverable — runs before remediation planning)**

5. **FR-05 (Per-surface assessment)**: Run the `quality-assessment` workflow (Fable-verified) across all remaining surfaces — shared client libs → shared server libs → PCF → Dataverse model + ALM → code pages + build sprawl → plugins — each producing a prioritized remediation `design.md`. Assessments are read-only ⇒ conflict-free ⇒ may run anytime. — Acceptance: each surface has a Fable-verified assessment design with severity/LOC/effort/risk + tranche split; remediation plan/tasks for a surface are created only AFTER its assessment design exists.

**BFF surface remediation (workstream #1 — already assessed)**

6. **FR-06 (BFF dead code)**: Delete the 6 verified dead-code items (~2,701 prod + ~1,149 test LOC): Scopes folder (3 files), Safety cluster (6 src + 2 test, fix 2 stale crefs), orphaned `RetryPolicies.cs`, **`StubLiveFactResolver` (approved for deletion)**, both `_archive/` folders, archived test. **Preserve** the verified KEEPs (`StubInsightGraph` wired at `InsightsModule.cs:53`; Todo `NullObject/` + `Placeholder/` ADR-032 factory pair). — Acceptance: grep confirms zero dangling refs; build + full test green; publish-size reduction reported.
7. **FR-07 (BFF bug fixes)**: Fix Bug-1 (invoice financial-totals — `IDataverseService as ServiceClient` always-failing casts at `FinanceRollupService.cs:228` + `Finance/Tools/FinancialCalculationToolHandler.cs:140-146,205-211`, live via `InvoiceExtractionJobHandler.cs:253`) and Bug-2 (dead `EmailToEmlConverter` builder half + conflicting dual registration). — Acceptance: invoice-totals path has a passing focused test; single correct registration for the `.eml` converter.
8. **FR-08 (Downcast consolidation)**: Add `UnwrapServiceClient(this IDataverseService, string consumerName)` (throws) + `TryUnwrapServiceClient(...)` (null+log) in `Spaarke.Dataverse` next to `DataverseServiceClientImpl`; replace all 13 copies / 17 call sites; fix the 3 broken casts (kills the bug class). — Acceptance: zero `IDataverseService as/is ServiceClient` casts remain outside the shared extension (grep-verified).
9. **FR-09 (BFF auth closure — via `@spaarke/auth`)**: Resolve the Finance auth exposure by the canonical **`@spaarke/auth` / ADR-028** path (owner decision): add `.RequireAuthorization()` to both `FinanceRollupEndpoints` `recalculate` endpoints AND migrate the legacy `sprk_subgrid_parent_rollup.js` web-resource caller to `@spaarke/auth`. If this surfaces a gap in `@spaarke/auth`'s web-resource support, fix it **here and elsewhere** (elevate to the security horizontal, FR-17). Also: harden `/healthz/dataverse` + `/healthz/dataverse/crud` (rate-limit + stop echoing `ex.Message`); add explicit `.RequireAuthorization()` to `OBOEndpoints` (7) + `UserEndpoints` (2) **(approved)**. — Acceptance: no anonymous Dataverse-write endpoint remains; web-resource caller authenticates via `@spaarke/auth`; healthz probes rate-limited with no exception-detail leak.
10. **FR-10 (BFF facade compliance)**: Bring the 4 AI-facade boundary violations into `Services/Ai/PublicContracts/` compliance (BFF §10 bullet 3 / refined ADR-013): relocate `IPlaybookLookupService` into `PublicContracts/` (clears A-4 + A-2/A-3 extra injections); add `IFileSummarizeAi` for A-1 (mirroring `CommunicationTriageAi`, preserving SSE chunk + 503 semantics); add `IWorkspacePrefillAi.RunPrefillActionAsync` for A-2/A-3. — Acceptance: no non-AI file injects `IActionResolver`/`IActionRunner`/`IPlaybookLookupService` (grep-verified); `AnalysisServicesModule.cs:169` comment updated.
11. **FR-11 (BFF folder migration)**: Finish the `Endpoints/`→`Api/` migration (6 files, namespace-only, zero route change) and delete the legacy `Endpoints/` tree. — Acceptance: route table byte-identical (route-dump diff); legacy folder gone.
12. **FR-12 (BFF structure)**: Decompose the 490-line / 75-registration `CommunicationModule.cs` into cohesive helpers (behavior-neutral, registrations identical); rename `Services/Finance/Tools/FinancialCalculationToolHandler` (e.g. `FinanceTotalsCalculator`) **after a mandatory Dataverse `sprk_analysistool.sprk_handlerclass` pre-check** (never touch a `HandlerId` string); include the optional Phase-5 shared circuit-breaker-registry wiring helper + app-only HTTP boilerplate consolidation **(approved)**. — Acceptance: DI registration set unchanged post-decomposition; `notes/dataverse-precheck.md` records the handler-row verification before any rename.
13. **FR-13 (BFF repo hygiene)**: Untrack the 2 committed tarballs (`deployment.tar.gz`, `spe-bff-api-deployment.tar.gz`) via `git rm --cached`; delete all 6 build artifacts (~127 MB) from the working tree; confirm `.gitignore` covers the patterns. — Acceptance: `git ls-files` shows no tarballs; source tree clean; `.gitignore` verified.

**Forcing-functions (make-it-stick)**

14. **FR-14 (ArchTests)**: Expand `Spaarke.ArchTests` into real fitness functions — e.g. "no non-AI code injects AI-internal types", "no `IDataverseService as ServiceClient` casts", layer-dependency rules, God-class LOC/ctor-dep thresholds. — Acceptance: each rule fails on a seeded violation and passes on current clean code.
15. **FR-15 (Mechanical baseline)**: Enable `TreatWarningsAsErrors=true` + Roslyn analyzers + `.editorconfig` (C#); strict ESLint (`--max-warnings 0`, `no-console`) + `tsc --noEmit` (TS). — Acceptance: baseline is table-stakes-clean per surface as that surface's gate activates.
16. **FR-16 (CI gates)**: Wire CVE scan, publish/bundle-size budgets, and doc-drift into the existing R1 PR/nightly layers, **activated per-surface** (each surface flips its own gate on as its last workstream step). — Acceptance: no gate is enabled repo-wide while another surface is still dirty; coordinate `.github/workflows` edits with `ci-cd-unit-test-remediation-r1`.

**Horizontal sweeps**

17. **FR-17 (Security)**: Repo-wide auth-consistency sweep centered on `@spaarke/auth` (ADR-028) — token handling, secrets in KV, XSS/injection boundaries, CORS; absorb any `@spaarke/auth` web-resource gap surfaced by FR-09 and fix it everywhere it recurs. — Acceptance: findings tracked as tasks; no data path without auth.
18. **FR-18 (Test quality)**: Reconcile tests against ADR-038 KEEP categories + `/test-diet`; resolve the 138-failing / KV-dependent integration-test situation (old item #4); re-count. — Acceptance: suite green + trustworthy; scaffolding tests removed, MAINTAIN tests kept at KEEP paths.
19. **FR-19 (Dependency/CVE hygiene)**: `dotnet list package --vulnerable --include-transitive`; `npm audit`; version-pin consistency; lockfile freshness. — Acceptance: no HIGH CVE; no new NuGet packages added by BFF work; pins documented.
20. **FR-20 (Observability)**: Sweep logging consistency, correlation IDs, PII-in-logs, telemetry on critical paths. — Acceptance: findings tracked; no PII in logs on audited paths.
21. **FR-21 (Doc-drift)**: Run `doc-drift-audit` across `.claude/` + `docs/`; fix stale references. — Acceptance: audit clean or drift filed as tasks.

**Reconciliation & portfolio**

22. **FR-22 (R3-draft reconciliation)**: Maintain the reconciliation record of the archived 12-item March R3 draft (§3) — each item CARRY/ABSORB/UPGRADED and `VERIFY`'d during its surface's Phase-0. — Acceptance: no item dropped silently; each has a confirmed disposition.
23. **FR-23 (Portfolio + NG1)**: Register the program under existing **Epic #427** (one Project Issue; surfaces = workstreams, no per-surface Issues); add the `projects/INDEX.md` hot-path row; file **NG1** (two Dataverse *access-stack* unification) via `/devops-idea-create` (Type=Idea, backlog). — Acceptance: Project Issue exists under #427; INDEX row present; NG1 Idea created. Verify no orphan R3 Issue first. **NG1 track (updated 2026-08-13)**: NG1 is no longer "deferred out of scope" — it is on an **assess-then-decide** track owned by task **011** (shared-server assessment produces the verified NG1 design + fresh re-estimate); FR-26 lands its credential-model slice first; remediation of the remaining access-stack unification is decided on 011's verified design.

**Deployment & configuration hygiene (added 2026-08-13 — accepted from the `customer-provisioning-orchestration-r1` ask; see `notes/deployment-refactors-assessment-2026-08-12.md`)**

24. **FR-24 (#1 — config-architecture assessment)**: Assess the configuration & deployment architecture (Key Vault single-source-of-truth vs the current 5 config sources / 94 deploy-time tokens / client-config endpoint / cache ceremony) via the `quality-assessment` workflow → `workstreams/config-deployment/design.md`. Read-only ⇒ conflict-free. **Assessment-first**: #1 remediation is task-created only after this verified design exists (cross-surface: BFF + PCF + code-pages + external-spa + Office add-ins). — Acceptance: a Fable-verified config-deployment design exists with the config-source inventory + a sized remediation plan. (Task 017.)
25. **FR-25 (#2 — fail-fast config validation)**: Bring the existing *partial* config-validation discipline to **uniform** coverage — `[Required]` on customer-critical `IOptions<T>` + `IValidateOptions<T>` cross-property invariants + `.ValidateDataAnnotations().ValidateOnStart()` — so a fresh BFF missing a required setting **crashes at startup** with the offending keys named, not at first user request. Enforced going forward by ArchTest (FR-14/task 040) + CI gate (FR-16/task 042). — Acceptance: negative — a BFF started with a required customer-critical setting missing fails at startup naming the keys; valid config still boots. (Task 061.)
26. **FR-26 (#3a — drop the vestigial Dataverse S2S app-reg)**: Drop the vestigial *separate* `spaarke-dataverse-s2s-*` app-registration + its `Dataverse-S2S-*` Key Vault secrets + 24-month rotation from provisioning/deploy/docs. Grounded 2026-08-13: this app-reg has **zero code consumers** (consolidated to `API_CLIENT_SECRET` on 2026-01-07) — scripts/docs/KV only, no BFF code change. Delivers r1's literal "2 app-regs → 1" ask. Leaves OBO + the SPE per-tenant container-type secrets + the BFF's own (still-secret) Dataverse path untouched. — Acceptance: safety-gate grep proves zero code consumers first; provisioning/rotation/docs no longer reference the S2S app-reg; operator KV-deletion checklist names only `Dataverse-S2S-*`; no BFF publish impact. (Task 060.)
    - **#3b (the substantive piece) → NG1 / task 011.** Correction of record: my 2026-08-12 "credential half already landed" was **wrong for the shared-lib camp** — AUTHV2-042 migrated only the `Services/Ai` raw-HTTP camp; the BFF's own shared-lib Dataverse path (`DataverseServiceClientImpl`/`DataverseWebApiService`) is **still `ClientSecret`-based** (a live ADR-028 §24 MUST violation, no amendment needed). Migrating it to MI is an **identity-attribution change** entangled with the two access-stack files, so #3b is folded into task 011's NG1 verified design (FR-05) and flagged report-only by task 040 rule (c) until 011's design lands it.
27. **FR-27 (#4 — Graph app-role single source of truth)**: Move the ~11-role Graph app-role expected-list from `scripts/Register-EntraAppRegistrations.ps1` into a compile-time constant (`GraphAppRoles.cs`: GUID + display name + owning module + why-required) + a Graph-SDK verification helper; the provisioning script + r1's H10 become *consumers* of the constant. Enforced by ArchTest (task 040) + CI gate (task 042). — Acceptance: adding a Graph role is exactly one code edit; the verifier detects drift between the constant and the SP grants. r3 owns the code constant + BFF verifier; r1 owns applying grants. (Task 062.)

### Non-Functional Requirements

- **NFR-01 (Publish size)**: BFF publish ≤ **60 MB compressed** (baseline **46.89 MB incl. PDBs**, 2026-08-05). Report absolute size + delta on every BFF-touching task. BFF work is expected to *reduce* size. Escalation: ≥+5 MB single-task delta → justify; ≥55 MB cumulative → architecture review; ≥60 MB → HARD STOP.
- **NFR-02 (Behavior-preserving)**: Every task except the explicit bug-fixes (FR-07) and auth changes (FR-09) must be provably behavior-neutral — `dotnet build` + full `dotnet test` + publish-size diff as the acceptance gate. Delete > deprecate.
- **NFR-03 (Conflict discipline)**: Assessments are read-only ⇒ zero merge-conflict risk ⇒ run anytime. `/conflict-check` MUST run before **every** remediation PR against `projects/INDEX.md` (19 active worktrees touch BFF).
- **NFR-04 (Small PRs)**: Remediation lands as small, reviewable, revertible per-surface PRs off the one branch; most-contested surfaces (BFF, Finance/Communication/Email files) sequenced into quiet windows.
- **NFR-05 (Adversarial verification)**: The Fable verification pass is **non-negotiable** for every assessment — it caught 2 real BFF bugs *and* corrected 2 false-positive "dead code" claims that were load-bearing.
- **NFR-06 (Supply chain)**: No new HIGH CVE; **no new NuGet packages** introduced by BFF work.
- **NFR-07 (Coverage)**: Coverage is an *observation, never a gate* (ADR-038).
- **NFR-08 (Data-driven dispatch)**: Anything dispatched by a Dataverse `sprk_*` row (handlers, tools) requires a Dataverse config check before rename/delete — not grep-provable.

## Technical Constraints

### Applicable ADRs

- **ADR-028** — Spaarke Auth v2 (canonical): all client→BFF auth via `@spaarke/auth`; drives FR-09 + FR-17.
- **ADR-013** (refined 2026-05-20) — AI facade: CRUD code uses `Services/Ai/PublicContracts/`; no direct `IActionResolver`/`IActionRunner`/`IPlaybookLookupService` injection. Drives FR-10.
- **ADR-032** — Null-Object kill-switch: preserve the Todo `NullObject/`+`Placeholder/` factory pair and `StubInsightGraph` seam.
- **ADR-038** — Testing strategy: KEEP categories, behavior-over-mocks, coverage = observation, `/test-diet` at wrap-up. Drives FR-18 + NFR-07.
- **ADR-010** — DI minimalism: `CommunicationModule` decomposition adds helpers only, no new abstractions.
- **ADR-022** — PCF platform libraries (PCF surface assessment; standalone, not superseded by ADR-038).
- **ADR-002** — Plugins (`BaseProxyPlugin` invert-vs-decommission).

### MUST Rules

- ✅ MUST route all client→BFF auth through `@spaarke/auth` (ADR-028); no anonymous Dataverse-write endpoint.
- ✅ MUST use `Services/Ai/PublicContracts/` for any CRUD→AI capability; MUST NOT inject AI-internal types into CRUD code.
- ✅ MUST run the Fable adversarial-verification pass on every assessment before acting on findings.
- ✅ MUST run `/conflict-check` before every remediation PR; MUST keep BFF publish ≤ 60 MB compressed.
- ✅ MUST perform a Dataverse `sprk_analysistool.sprk_handlerclass` pre-check before renaming/deleting any handler/tool; MUST NOT touch a `HandlerId` string.
- ✅ MUST verify dead code against `src/` **and** `tests/` (BFF exposes internals via `InternalsVisibleTo` to 3 test assemblies) before deletion.
- ❌ MUST NOT unify the two Dataverse stacks (NG1), merge the two live `.eml` builders, or merge the two distinct R6 financial handlers in this program.
- ❌ MUST NOT enable a forcing-function gate repo-wide while another surface is still dirty (per-surface activation only).

### Existing Patterns to Follow

- Facade: `CommunicationTriageAi` / `CommunicationEnrichmentService` (legal resolver+runner wrap) — reference for A-1's `IFileSummarizeAi`.
- Auth: `ScorecardCalculatorEndpoints` (`.RequireAuthorization()` sibling); `@spaarke/auth` binding at `.claude/patterns/auth/spaarke-sso-binding.md`.
- Assessment: the proven BFF pass (6 parallel read-only investigations + 3-agent Fable verification).
- ADR-032 seams: `TodoSyncModule.cs:84-98` factory; `InsightsModule.cs:53` swap-path.

## Placement & New Components (per CLAUDE.md §10 / §11)

### Hot-Path Declaration (single — covers all surfaces)

```xml
<hot-path-declaration>
  <bff>Y</bff>                 <!-- BFF workstream: dead-code removal, downcast consolidation, 2 bug fixes, auth, facade, DI -->
  <spaarkeai>Y</spaarkeai>     <!-- client-surface workstreams touch src/solutions/SpaarkeAi/** (shared libs, code pages) -->
  <ci-workflows>Y</ci-workflows>   <!-- forcing-functions §9: CVE/size/lint/doc-drift gates in .github/workflows -->
  <skill-directives>Y</skill-directives> <!-- rubric may update .claude/constraints + code-review/adr-check skills -->
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```

**Placement Justification (§10)**: The program's code delta is **net-negative** — it removes/consolidates code (dead-code deletion, 13→1 downcast, folder migration) and adds no new BFF endpoints/services/packages. The only additive surface is `UnwrapServiceClient`/`TryUnwrapServiceClient` (in `Spaarke.Dataverse`, replaces 13 copies) and `PublicContracts/` facade methods (which *reduce* CRUD→AI coupling). Publish size expected to drop. `ci-workflows=Y` coordinates with `ci-cd-unit-test-remediation-r1` (owns existing-workflow edits). This program's `projects/INDEX.md` row must be added so the ~19 other BFF worktrees see it.

### New Components (§11 three-question gate)

| New component | Existing overlap (grep-verified) | Can extend instead? | Cost-of-doing-nothing (concrete failure) |
|---|---|---|---|
| `UnwrapServiceClient`/`TryUnwrapServiceClient` ext | `DataverseServiceExtensions.cs` (DI-only, no unwrap); 13 private copies | This IS the extraction of 13 copies into one | 13 copies persist; the 3 broken casts stay broken → invoice-totals `InvalidOperationException` at runtime |
| `IFileSummarizeAi` facade (A-1) | `IActionSeam` (writes only); `IWorkspacePrefillAi` (playbook SSE, not linear actions); `CommunicationTriageAi` (pattern to mirror) | No existing facade exposes "resolve Binding → run linear Action" for file summarize | A-1 keeps injecting `IActionResolver`/`IActionRunner` into a non-AI endpoint — §10 violation persists |
| `IWorkspacePrefillAi.RunPrefillActionAsync` (A-2/A-3) | `IWorkspacePrefillAi` already injected by both services | **Extend** the already-injected facade — one method serves both | A-2/A-3 keep the internal injection |
| `IPlaybookLookupService` relocation (A-4) | Same interface, wrong namespace | This is a **move**, not new surface | A-4 + the A-2/A-3 secondary injection stay non-compliant |
| `docs/standards/CODE-QUALITY-RUBRIC.md` | R1 scorecard (informal) | Extends R1's scorecard into a standing standard | No comparable ruler → surface grades not comparable; progress unmeasurable |
| `quality-assessment` Workflow | Manual BFF-pass fan-out (one-off) | This IS the reusable extraction of the one-off method | Each surface re-improvises fan-out+verify → inconsistent, error-prone assessments |

All other work is deletion, rename, move, in-place consolidation, or extension of existing surface — §11 does not apply.

## ADR Tensions (per CLAUDE.md §6.5)

| ADR | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| ADR-028 | "All client→BFF auth via `@spaarke/auth`" | The legacy `sprk_subgrid_parent_rollup.js` web resource currently calls the anonymous Finance endpoint; migrating it may surface a gap in `@spaarke/auth`'s web-resource support | **C (comply) + fix gap** | Owner directive: use `@spaarke/auth`; if a gap exists, fix it here and elsewhere. This *enforces* ADR-028 rather than deviating from it. |
| ADR-013 / §10 bullet 3 | "No AI-internal types injected into CRUD code" | The BFF currently has 4 violations | **C (comply)** | FR-10 *enforces* the rule — no tension, it is the point. |
| ADR-032 | Null-object kill-switch seams | Dead-code sweep could delete a seam | **C (comply)** | Verified KEEPs (`StubInsightGraph`, Todo double-layer) preserved; ADR-032 respected. |

> No ADR **amendment** is anticipated. The listed ADRs apply without exception. If the per-surface assessments (FR-05) surface a genuine ADR tension, it will be recorded here and resolved via CLAUDE.md §6.5 (A exception / B amendment / C comply) before that surface's remediation.

## Success Criteria

1. [ ] `docs/standards/CODE-QUALITY-RUBRIC.md` published; every surface scored against D1–D11 — Verify: file + scored `SCORECARD.md` rows.
2. [ ] Reusable `quality-assessment` Workflow runs end-to-end with a Fable verification stage — Verify: one full surface run produces a verified design.
3. [ ] Honest re-baseline published; no unverified aggregate grade; March "95/100" superseded — Verify: `notes/SCORECARD.md` Phase-0 output.
4. [ ] Every remaining surface has a Fable-verified assessment `design.md` **before** its remediation is planned — Verify: assessment design precedes each surface's task-create.
5. [ ] BFF surface executed: 6 dead-code items removed (zero dangling refs), 2 bugs fixed (invoice-totals test passing), 13 downcasts → 1 extension, auth closed via `@spaarke/auth`, 4 facade violations behind `PublicContracts/`, `Endpoints/` deleted (route-dump identical), `CommunicationModule` decomposed, 2 tarballs untracked — Verify: build + full test green; grep checks; publish-size delta vs 46.89 MB reported.
6. [ ] Forcing-functions live: ArchTests expanded (fail-on-seeded-violation), analyzers-as-errors on, lint/CVE/size/doc-drift CI gates green — **per-surface activation** — Verify: each surface's last step flips its gate; no repo-wide big-bang.
7. [ ] Horizontals executed: security (`@spaarke/auth` consistency), test-quality (`/test-diet` + 138-failing reconciliation), CVE (no HIGH), observability (no PII in logs), doc-drift — Verify: each sweep's findings tracked to closure.
8. [ ] Archived 12-item R3 draft fully reconciled (§3) — no item dropped silently — Verify: reconciliation table each item disposition + VERIFY.
9. [ ] Portfolio: program registered under Epic #427; `projects/INDEX.md` row added; NG1 filed as an Idea — Verify: Project Issue + INDEX row + Idea Issue.
10. [ ] Aggregate grade reaches **A+ (senior-panel standard)** with forcing-functions preventing re-drift — Verify: final `SCORECARD.md` aggregate + live gates.

## Dependencies

### Prerequisites

- BFF surface assessment (workstream #1) — **DONE** (`workstreams/bff-api/design.md`).
- Existing R1 CI/nightly quality layer + `Spaarke.ArchTests` project (to extend).
- Epic **#427** `[Epic]: Code Quality` (registration parent — verify at execution; verify no orphan R3 Issue first).
- Operator per-run opt-in for the Workflow tool ("use a workflow") on each assessment turn.

### External Dependencies

- Fable model availability for the mandatory adversarial-verification stage.
- Dataverse access for the `sprk_analysistool.sprk_handlerclass` pre-check (FR-12) and any data-driven-dispatch verification.
- Coordination with `ci-cd-unit-test-remediation-r1` (owns existing `.github/workflows` edits) and other active BFF worktrees via `/conflict-check`.
- Confirmation that the `sprk_subgrid_parent_rollup.js` web-resource caller can authenticate via `@spaarke/auth` (FR-09).

## Owner Clarifications

*Answers captured during design-to-spec interview (2026-08-06):*

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Finance auth | Close the anonymous Dataverse-write endpoint via HMAC (B), RequireAuthorization (A), or accept-risk (C)? | **Use `@spaarke/auth`** — if this identifies a gap in that approach, fix it here and elsewhere | FR-09: `.RequireAuthorization()` + migrate the web-resource caller to `@spaarke/auth`; any `@spaarke/auth` web-resource gap becomes a horizontal fix (FR-17). Overrides the design's HMAC recommendation. |
| Task scope | Task out only scaffolding + BFF now (assess-then-task later), or everything up front? | **Run the full assessment (Fable) first for complete documentation, then build plan/tasks** | Assessment (FR-05) is the gating deliverable; remediation tasks for surfaces 2–6 are created only after their Fable-verified assessment designs exist. |
| BFF extras | Approve OBO/User `.RequireAuthorization()`, StubLiveFactResolver deletion, optional Phase-5 items? | **All three approved** | FR-06 (delete StubLiveFactResolver), FR-09 (OBO/User auth), FR-12 (optional Phase-5 helpers included). |
| NG1 filing | File the two-Dataverse-stacks unification now so it isn't lost? | **File as an Idea now** | FR-23: `/devops-idea-create` (Type=Idea, backlog) during the pipeline. |

## Assumptions

- **Assessment ordering**: surfaces assessed by leverage (client libs → server libs → PCF → data model → code pages → plugins) per design §7; BFF already assessed. Adjustable if the owner reprioritizes.
- **Epic parent**: Epic **#427** is the correct registration parent (per session handoff); verified at `/project-pipeline` time.
- **`@spaarke/auth` web-resource support**: assumed feasible for the `sprk_subgrid_parent_rollup.js` caller; if a real gap exists, FR-09 escalates it to FR-17 as a broader fix.
- **Publish-size direction**: BFF work assumed to *reduce* compressed publish size from 46.89 MB; reported per task regardless.
- **Single-branch execution**: all workstreams execute on `work/code-quality-and-assurance-r3`; surface folders (`workstreams/{surface}/`, e.g. `workstreams/bff-api/`) are semantic homes only, not separate execution units.

## Unresolved Questions

- [ ] **Web-resource `@spaarke/auth` feasibility** — can `sprk_subgrid_parent_rollup.js` compute/attach a `@spaarke/auth` token, or does it need migration to a supported surface first? Blocks: FR-09 auth task execution (the one owner-gating item for BFF remediation).
- [ ] **Per-surface remediation scope** — each surface's tranche split (low-conflict-now vs quiet-window) is set only after its assessment. Blocks: nothing now; resolved per surface at FR-05 output.
- [ ] **Phase-5 boilerplate consolidation depth** — the app-only HTTP boilerplate consolidation (approved) may reveal more sites than expected; scope confirmed against the assessment. Blocks: FR-12 final task sizing.

---
*AI-optimized specification. Original design: `projects/code-quality-and-assurance-r3/design.md` + `projects/code-quality-and-assurance-r3/workstreams/bff-api/design.md`. Both preserved as project artifacts.*
