# BFF API Test Suite Repair — Design Document

> **Project**: `sdap-bff.api-test-suite-repair`
> **Status**: DESIGN (2026-05-30) — supersedes `bff.api-repair-overview.txt`. Awaiting owner sign-off on §4 Resolved Decisions and §5 Open Decisions before moving to `/design-to-spec`.
> **Created**: 2026-05-30
> **Supersedes**: [`bff.api-repair-overview.txt`](bff.api-repair-overview.txt) — kept in repo as the original framing
> **Role in pipeline**: This is the **design** layer. Once approved, it becomes the input to `/design-to-spec` → SPEC.md → `/project-pipeline` → tasks. No code is written from `design.md`; tasks come from SPEC.md.
> **Audience**: Project owner, ops/deploy team, AI agents executing tasks
> **Driver**: The predecessor `sdap-bff-api-remediation-fix` deliberately deferred test repair. Six weeks later the test suite is the largest active source of CI noise, and the BFF deploy gate is effectively bypassed by admin merges. Drift compounds; repair gets harder the longer we wait.
> **Why this matters**: The BFF is the single backend for every Spaarke client surface (~120 endpoints, ~99 DI registrations, ~13 background job types, 6+ client surfaces). Without a working unit-test layer, every BFF business-logic change ships with synthetic-smoke + E2E + App Insights as the only verification — and as the Insights Engine, Action Engine, and Communications modules continue adding business logic to this codebase, that verification gap is widening, not narrowing.

---

## 1. Executive Summary

This project repairs the `Sprk.Bff.Api.Tests` test suite and restores the BFF CI gate that is currently bypassed. The predecessor `sdap-bff-api-remediation-fix` (Phase 4) shipped a structural facade migration but explicitly left the test suite in disrepair, recommending follow-up — this is that follow-up.

The 2026-05-30 investigation (documented in §3) **replaces every estimate in `bff.api-repair-overview.txt`** with measured data. Headline corrections:

- **The suite is 92.9% green, not 0%.** Of 5,215 runnable tests: 4,844 pass, 269 fail (5.2%), 102 skipped. The narrative of a "broken" test suite is wrong; reality is a *long drift tail* on an otherwise healthy suite.
- **Beyond runtime failures, 17 test files don't compile** (138 errors). These were invisible to the overview's "283 failures" framing because compile failure is reported as runtime failure by `dotnet test`. The 17 files need constructor signature updates, not algorithmic rewrites.
- **The CI gate is fictional.** Master branch protection lists `Build & Test (Debug/Release)` as required, but `enforce_admins: false` allows admin bypass — and **10 of 10 most recent `sdap-ci.yml` runs failed**, **10 of 10 most recent `deploy-bff-api.yml` runs failed**, yet code merges and ships. The gate restoration is the durable deliverable; test repair is the prerequisite.
- **The "rewrite from scratch" option is off the table.** You do not rewrite a 92.9%-passing suite. The 4,844 passing tests are real signal worth preserving. Repair-in-place is the obvious approach.

The project delivers **four outcomes as one bundle** — they are sequenced but not separable:

| Outcome | Measurable target |
|---|---|
| **A. Compile health** | Test project compiles cleanly under `-warnaserror`; all 17 broken files repaired or deliberately archived |
| **B. Runtime green** | **Zero failing tests.** Every current failure is either repaired (passes), archived (removed from suite per §6.5), or filed as a real production bug (failure removed from suite; bug tracked separately). No test left in `Failed` state at project close. |
| **C. CI gate restoration** | `enforce_admins: true` for `Build & Test` checks; `dotnet test` failure blocks BFF deploy; emergency deploy path via documented incident-response procedure only (no casual `skip-tests` checkbox) |
| **D. Anti-drift governance** | Test-update obligation added to `.claude/constraints/bff-extensions.md`; code review checklist updated; per-tier ownership rules documented |

Outcome C is the load-bearing piece. Without it, the next six months of feature work re-creates today's drift and we redo this project in 2027. A, B, and D are valueless individually if C is skipped.

---

## 2. Strategic Context

### 2.1 Why this is the right next project

The 2026-05-29 BFF-AI extraction assessment concluded the BFF stays unified. That decision **raises the stakes for in-process verification.** A unified BFF that grows the Action Engine (in progress), Insights Engine Phase 2, and Communications module without a working test layer is accumulating risk at a rate visible in the recent commit log — *every project ships test additions; no project owns drift correction*.

Specifically, the predecessor project demonstrated empirically that:
- Synthetic smoke caught structural/route regressions
- E2E caught one real bug (`/api` prefix in LegalWorkspace)
- Code review caught 3 important findings
- **Unit tests caught zero** — because the suite was broken

That worked for a structural refactor (no behavior change). It will not work for the next wave of business-logic changes (capability router edits, playbook scheduling logic, scoring algorithm tweaks, attachment processing changes). The verification layer most appropriate for those changes is the layer that is broken today.

### 2.2 Why "now" specifically (not later)

| Today | In 3 months (no action) | In 6 months (no action) |
|---|---|---|
| 269 runtime failures, 17 compile-broken files | Estimated 350-450 failures, ~30 compile-broken files (Action Engine, Insights Phase 2, Communications all in flight) | Significant chunks become unrepairable — referenced services genuinely deleted, not just renamed |
| Repair effort: ~25-35h | Repair effort: ~40-55h | Repair effort: rewrite-from-scratch becomes the cheaper option |
| 4,844 passing tests preserved | Some passing tests become broken due to dependency drift | Suite becomes vestigial; gets deleted; BFF has no unit-test layer |

This is the **last cheap window.** The repair is mechanical now (signature updates, namespace fixes); it becomes semantic later (rewriting tests for code that no longer matches the original intent).

### 2.3 Coordination matrix with active work

| Stream | Status | Coordination risk | Plan |
|---|---|---|---|
| `ai-spaarke-action-engine-r1` | Active (design phase) | **HIGH** — will add new endpoints/services to BFF; new tests must follow the per-tier convention this project establishes | Sequence: Action Engine first task to ship MUST consume the test convention this project produces. Project owner to align. |
| `ai-spaarke-insights-engine-r1` | Phase 2 active | MEDIUM — adds tests under `Services/Ai/`; risk of merge conflict if repair touches same files | Coordinate via daily sync; assign repair priority order so Insights' active files come last |
| `x-email-communication-solution-r2` | Active | MEDIUM — adds tests under `Services/Communication/`; ArchivalFlow + AssociationMapping are in this project's compile-broken set | Owner of repair owns the Communications test files; coordinate with Communication project owner before touching |
| FAILURE-MODES G-2/G-3 (CI workflow gaps) | Open | LOW | Outcome C addresses adjacent workflow concerns; align in same PR |
| `sdap-bff-api-remediation-fix` | Merged | N/A | This project assumes Phase 4 facade is stable; no churn on facade in scope |

### 2.4 Authoritative constraint sources (binding for this project)

| Source | Binds |
|---|---|
| [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) | Per-tier placement decisions for any new BFF code; this project ADDS the test-update obligation to this constraint (Outcome D) |
| [`src/server/api/Sprk.Bff.Api/CLAUDE.md`](../../src/server/api/Sprk.Bff.Api/CLAUDE.md) | Module conventions; test updates must respect Kiota/Graph SDK version pinning |
| ADR-010 (DI minimalism) | This project may NOT increase the DI registration count via new test scaffolding |
| ADR-013 refined (2026-05-20) | AI extends BFF; AI-coupled job tests stay in this project, not extracted |
| ADR-028 (Spaarke Auth) | `FakeAuthHandler` pattern stays; no parallel auth fake |
| [`docs/procedures/testing-and-code-quality.md`](../../docs/procedures/testing-and-code-quality.md) | Existing test conventions; this project EXTENDS but does not replace |
| [`.claude/FAILURE-MODES.md`](../../.claude/FAILURE-MODES.md) AP-1 (skill prescribes X but X is wrong) | Test-repair tasks must NOT trust the overview's stale estimates; cite measured data from §3 |

### 2.5 Strict NOT-IN-SCOPE

This project is bounded. The following are **explicitly excluded** and must be deferred:

- Production code changes (test repair does not change production behavior — see §6.1 binding rule)
- Refactoring `CustomWebAppFactory<Program>` beyond what's needed to unbreak existing tests
- Adding new test coverage for currently-uncovered code paths
- Repairing `tests/integration/Spe.Integration.Tests` (separate scope; see §5 Open Decision)
- Repairing `tests/unit/Spaarke.Core.Tests` or `tests/unit/Spaarke.Plugins.Tests` (out of scope; healthy as of 2026-05-30)
- Increasing test coverage % targets
- Migrating from NSubstitute + Moq to a single framework (current mix is intentional per task history)

---

## 3. Measured Baseline (2026-05-30)

This section captures the data that supersedes every estimate in `bff.api-repair-overview.txt`. Every task in this project must cite §3 numbers, not overview numbers.

### 3.1 Test suite scale and current health

```
Project:        tests/unit/Sprk.Bff.Api.Tests
Test files:     259 .cs files (245 actual tests + 14 support files)
Total LOC:      ~124,000
Total tests:    5,215  (after temporary exclusion of 17 broken files)
Passing:        4,844  (92.9%)
Failing:          269  ( 5.2%)
Skipped:          102  ( 2.0%)
Duration:    1m 15s    (on RalphSchroeder Windows dev box, Release config)
```

**The 92.9% pass rate is the load-bearing number for every design decision in this document.** Rewrite is off the table; repair is the only sensible path.

### 3.2 Compile-broken files (17 files, 138 errors)

| Error code | Count | Root cause |
|---|---|---|
| **CS7036** — missing constructor arg | 62 | Services added required params (mostly `ILogger<T>`, `TokenCredential`) — tests pass too few args |
| **CS1503** — argument type mismatch | 48 | Constructor signature changed types |
| **CS1061** — missing member | 12 | Method/property renamed or removed |
| **CS0618** — obsolete API in use | 8 | E.g., `EmailProcessingOptions.WebhookSecret` deprecated for `WebhookSigningKey` (task 044) |
| **CS1739** — invalid named argument | 6 | Parameter renamed |
| **CS8625** — null literal on non-nullable | 2 | Nullable-reference changes |

The 17 affected files:

```
Api/EmailWebhookEndpointTests.cs
Api/ExternalAccess/ExternalAccessEndpointTests.cs
Integration/CommunicationIntegrationTests.cs
Services/Ai/ScopeResolverServiceTests.cs
Services/Ai/Sessions/SessionRestoreServiceTests.cs
Services/Ai/Tools/SendCommunicationToolHandlerRegistrationTests.cs
Services/Ai/Tools/SendCommunicationToolHandlerScenarioTests.cs
Services/Ai/Visualization/VisualizationServiceTests.cs
Services/Ai/WorkingDocumentServiceTests.cs
Services/Communication/ArchivalFlowTests.cs
Services/Communication/AssociationMappingTests.cs
Services/Communication/AttachmentValidationTests.cs
Services/Communication/CommunicationServiceTests.cs
Services/Communication/DataverseRecordCreationTests.cs
Services/Email/EmailAttachmentExtractionTests.cs
Services/Jobs/RecordSyncJobTests.cs
Services/Workspace/TodoGenerationServiceTests.cs
```

**Triage**: every error is mechanical signature drift. No file references a deleted type. Effort per file: 15-30 min average; total: 5-8h.

### 3.3 Test file categorization (245 production test files)

| Tier | Files | % | Examples | Defect-prevention value | Repair priority |
|---|---|---|---|---|---|
| **HIGH — pure algorithm + safety** | ~35 | 14% | `Services/Workspace/*` (scoring, 2300 LOC), `Services/Scorecard*` (4), `Services/Finance/SignalEvaluation` (780 LOC), `Services/Email/EmailAssociation` (863 LOC), `Services/Ai/Safety/*` (groundedness, citations, prompt shield), `Filters/*`, `Infrastructure/Json/*`, `Infrastructure/Resilience/*` | **HIGH** — deterministic algorithms; silent bugs; real money/safety cost | **1st** |
| **MEDIUM — service orchestration (mocked)** | ~70 | 29% | `Services/Ai/Chat/*` (30), `Services/Ai/Capabilities/*`, `Services/Ai/Nodes/*`, `Services/Communication/*` (16) | Medium — mocked-dep tests; verify orchestration | **2nd** |
| **INTEGRATION** | ~25 | 10% | `Integration/*` (WireMock-based), workspace fixtures | Medium-high — closer to real behavior | **3rd** |
| **LOW — endpoint pipeline** | ~88 | 36% | `Api/*` (75) + top-level `*EndpointTests` (13). Tests route registration, auth, status codes, response shapes | LOW — most duplicates synthetic smoke. Keep response-shape contract tests; archive route-registration duplicates | **4th — triage for archive vs repair** |
| Support | 14 | 6% | Factory, globals, fakes, mocks | N/A | Touch only if needed |

### 3.4 Failure clustering (preliminary, from 2026-05-29 console output)

A full failure cluster analysis is a Phase 1 deliverable (see §7), but preliminary clustering from visible failures:

| Cluster | Approx count | Root cause | Fix scope |
|---|---|---|---|
| **IChatClient streaming / `IAsyncEnumerable` mocking** | ~30-50 | NSubstitute auto-mock doesn't handle `IAsyncEnumerable` returns | Single test helper + migrate affected tests (see §5 Open Decision 1) |
| **WebApplicationFactory startup** | ~50-100 | Config validation requires more options than `CustomWebAppFactory` provides | Extend factory (171 → ~200 LOC); bounded |
| **Individual test logic drift** | ~130-200 | Assertions hardcoded against shapes that changed | 3-5 min average per test; many; mechanical |

These match the original overview's 3-group framing, but with measured ceilings instead of estimates.

### 3.5 CI gate truth

**Branch protection on master (2026-05-30):**
```
required_status_checks: [Build & Test (Debug), Build & Test (Release), Code Quality]
required_approving_review_count: 0
enforce_admins: false              ← this is the bypass
required_signatures: false
```

**Recent CI run conclusions (last 10 of each):**
- `sdap-ci.yml`: 10/10 = **failure** (most recent: 2026-05-29 21:03 UTC, master push)
- `deploy-bff-api.yml`: 10/10 = **failure** (spanning 2026-05-20 → 2026-05-28)

**The deploy workflow allows `skip-tests: true` via `workflow_dispatch`** — this is the formal override path that has likely been used for at least the last 6 BFF deploys.

This is the most important finding in the entire investigation. Test repair without CI gate restoration is theater.

### 3.6 Tests targeting deleted services

Diffed all 254 test-target names against all 1,591 class definitions in BFF + shared:
- 92 test targets have no matching class name
- **~85 of those are scenario/behavior test files** named after concepts (`Authorization`, `Cache`, `EndpointGrouping`, `FileOperations`, `Phase2Integration`) — that's normal naming, not "dead test"
- **Truly dead tests** (targeting genuinely removed services) are estimated **<10 files**

Drift is mostly mechanical (namespace, signature) rather than semantic (code is gone). This validates the repair-vs-rewrite decision.

---

## 4. Resolved Decisions

These are the design's defaults. Owner can override any before kickoff.

### 4.1 Repair, not rewrite — NON-NEGOTIABLE

**Decision**: Repair the existing `Sprk.Bff.Api.Tests` project in place. Do not create new projects, do not split into unit + integration tiers, do not rewrite individual tests from scratch when a signature/namespace/assertion update suffices.

**This is the project's load-bearing rule.** It is enforced at three levels:
1. **Project CLAUDE.md** (created in Phase 0) states this rule in §1
2. **Every task POML's front-matter** declares `repair_not_rewrite: true` as a binding constraint
3. **Code review** rejects any test PR where the diff replaces >50% of a test file's lines without escalation per §4.8

**Why**: 92.9% pass rate is real signal. The structural design (in-process HTTP integration via `WebApplicationFactory<Program>`) is acceptable; the rot is content, not structure. Splitting into projects costs 100-140h with no commensurate benefit when the existing project mostly works.

**Counter-argument considered and rejected**: A fresh `Sprk.Bff.Api.UnitTests` (pure logic) + `Sprk.Bff.Api.IntegrationTests` (in-process HTTP) would solve the "everything depends on one 171-line factory" coupling problem. But it would discard 4,844 passing tests, require CI workflow rewiring across 4 workflows, and the coupling problem is mitigable by Outcome D governance.

**Escalation path**: See §4.8. Rewrite is NOT forbidden; it is gated.

### 4.2 Repair in place, archive selectively

**Decision**: Of 17 compile-broken + 269 runtime failures, default action is repair. Archive only when:
- The file targets a deleted service (estimated <10 files)
- The file is a pure duplicate of synthetic-smoke coverage (subset of the LOW-tier ~88 files)
- The cost to repair exceeds the value of the defect-prevention coverage (judgment call, requires sign-off per file)

Archive method: rename file to `*.cs.archived-YYYY-MM-DD` (matches existing `JobProcessorTests.cs.archived-2025-10-14` precedent).

### 4.3 Failure target: ZERO failing tests at project close

**Decision**: Every test in the suite at project close must be in `Passed`, `Skipped` (with explicit reason trait), or removed (archived per §6.5 or in a real-bug ledger). NO test left in `Failed` state.

**Why zero (not ≤30)**: A failing test is signal. If we keep 30 failing tests "because the long tail is expensive," we are saying 30 pieces of signal are acceptable to ignore. That defeats the purpose of having a test suite. Three legitimate destinations exist for any failure; "leave it failing" is not one of them:

| Outcome for a failing test | Status at project close | Counted as failure? |
|---|---|---|
| **Repaired** — assertion/signature updated; test passes | `Passed` | No |
| **Archived** — targets deleted code, or is a confirmed duplicate of synthetic-smoke coverage | Removed from suite via §6.5 rename | No (not in suite) |
| **Real bug pending production fix** — test is correct, production has a bug; test is `Skip`'d with `[Trait("status", "real-bug-pending-fix")]` + entry in `real-bug-ledger.md` | `Skipped` | No (Skipped, not Failed) |
| **Flaky** — non-deterministic; not the right defense | Either quarantined to a separate `[Trait("category", "flaky")]` collection with a fix-by date, OR archived if the test design itself is flawed | Skipped (only if quarantined with a date) |

The exit ledger reports counts per status. Zero are in `Failed`.

**Why the predecessor pattern of "≤30" was wrong for this project**: that pattern was used in task 071 to declare a partial-fix scope acceptable when the larger project had higher-priority work. This project's WHOLE scope IS the test suite. Accepting failures means accepting that the project didn't complete its own deliverable.

**Exception** (rare, requires owner sign-off): if a test cluster requires production changes to fix and those changes are explicitly out of scope (per §4.4 binding rule), the cluster is `Skip`'d under `[Trait("status", "real-bug-pending-fix")]` and tracked in `real-bug-ledger.md`. The cluster is NOT a `Failed` test at project close; it is a documented production bug awaiting a separate PR.

### 4.4 Test changes must NOT change production

**Decision (binding rule)**: This project's PRs may not modify any file outside `tests/`. If a test fails because production has a bug (not because the test drifted), the failure is documented as a real bug and the test is marked `[Trait("status", "real-bug-pending-fix")]`. A separate PR/project fixes the production bug.

**Why**: Test repair that silently changes production is the classic "I fixed the test by making production worse" anti-pattern. The 92.9% pass rate gives us a regression baseline; this rule preserves it.

### 4.5 No factory rewrite

**Decision**: `CustomWebAppFactory.cs` (171 LOC) may be extended (adding fake config values, removing additional hosted services) but not rewritten. Replacing the factory pattern is out of scope.

**Why**: The factory is shared by ~200+ tests; rewriting risks the 4,844 passing tests. Defer factory restructuring to a separate project if Action Engine + future work surface it as a real bottleneck.

### 4.6 One project, internal phases

**Decision**: All four outcomes (A–D) ship as a single project `sdap-bff.api-test-suite-repair` with internal phases. No spinoff projects.

**Why**: The outcomes are interdependent — Outcome C (CI gate) has no value without Outcomes A+B (passing tests); Outcomes A+B rot without Outcome D (governance). Splitting them creates coordination overhead and the risk that the load-bearing outcome (C) gets deferred again.

### 4.7 Coordinate, don't lock

**Decision**: Do NOT freeze sibling BFF projects during repair. Coordinate via daily sync. Repair priority order will avoid in-flight feature areas where possible.

**Why**: Action Engine, Insights Engine Phase 2, and Communications are too important to gate on this project. Repair-priority sequencing (HIGH-tier first, in-flight areas last) is sufficient coordination.

### 4.8 Rewrite is GATED, not forbidden

**Decision**: A test rewrite (defined: replacing >50% of a test file's lines, or creating a replacement file under a different name) requires explicit escalation before the work proceeds.

**Escalation procedure** (mandatory):
1. Task agent halts work on the file
2. Task agent files a `rewrite-request-T-XX-FileName.md` in `escalations/` with:
   - Why repair was insufficient (specific evidence: signature drift count, test logic dependencies on removed types, behavioral contract mismatch)
   - Proposed rewrite scope (what's preserved, what's new)
   - Estimated effort delta vs. repair
3. Owner reviews and approves OR redirects to alternative repair strategy
4. Approved rewrites are tracked in `rewrite-ledger.md` at project close (transparency about how many escapes happened)

**Why this matters**: The default of "repair" can quietly drift into "rewrite" if no friction exists. The escalation creates friction. But "never rewrite" would be wrong — sometimes the test target genuinely changed enough that repair produces a worse test than rewrite. The escalation surfaces those cases instead of forbidding them.

**Hard limit**: If escalated rewrites exceed 5% of touched test files, project pauses for design-review with owner — that signals the "repair not rewrite" thesis is wrong and the design needs revisiting.

---

## 5. Locked Decisions on Approach

These were open in the initial draft; they are now resolved per owner direction ("take the most robust path, not the easy path; but do not over-engineer if not critical or material to quality"). Tasks proceed against these as binding.

### 5.1 IChatClient streaming mocking: Microsoft helpers preferred; hand-rolled fallback

**Resolved**: Use Microsoft-shipped testing helpers (e.g., `Microsoft.Extensions.AI.Testing` or equivalent companion package to the existing `Microsoft.Extensions.AI` v10.3.0 reference) if they exist and are mature as of Phase 0. Only fall back to hand-rolled `AsyncEnumerableHelpers.cs` if Microsoft has not shipped a usable helper.

**Phase 0 responsibility**: Researcher subagent (per `.claude/agents/researcher.md`) verifies in ~30 minutes whether a Microsoft-shipped helper exists and is mature (NuGet stable, documented, used in Microsoft samples or Microsoft.Extensions.AI test code). Verdict is recorded in `decisions/D-01-async-enumerable-helper.md`. The verdict drives P1.B in Phase 1.

**Decision criteria (researcher applies)**:
- ✅ Use Microsoft helper IF: it exists at a stable version (not preview), provides `IChatClient` streaming mocks specifically, is referenced in Microsoft samples or Microsoft.Extensions.AI test code, and integrates with our existing NSubstitute/Moq mix without conflict.
- ⚠️ Hand-roll IF: no Microsoft helper exists, OR it exists only as preview, OR it requires a major framework migration to consume.
- 🆘 Escalate to owner IF: a Microsoft helper exists but its API requires non-trivial test rewrites — owner decides repair-vs-Microsoft trade-off.

**Why Microsoft-first**: aligns with owner direction "use the most robust available." A Microsoft-maintained helper updates with the SDK; our hand-rolled version becomes future maintenance debt that someone owns forever.

**Why hand-rolled fallback**: if Microsoft hasn't shipped one, we still need to fix ~30-50 broken tests. Hand-rolled is the working alternative.

**Effort impact**: net-neutral. Microsoft-helper path saves ~4-6h of helper authoring but adds ~30 min researcher investigation and ~1-2h of integration verification. Hand-rolled path matches the original ~4-6h estimate.

**Why this is robust over easy**: the "easy" path was "commit to hand-rolled, ship faster." The robust path is "30-min check first, use the better tool if it exists." That's the same principle as preferring `Microsoft.AspNetCore.Mvc.Testing` over hand-rolled HTTP test scaffolding (which we already do).

### 5.2 CI gate: full enforce_admins + documented incident-response emergency path

**Resolved: Option C with strict criteria.** `enforce_admins: true` for `Build & Test (Debug)`, `Build & Test (Release)`, and `Code Quality` status checks. The `skip-tests` workflow_dispatch input is REMOVED from [`deploy-bff-api.yml`](../../.github/workflows/deploy-bff-api.yml). Emergency deploy is a documented procedure requiring (a) a filed incident, (b) named approver from a documented allowlist, and (c) auto-creation of a follow-up issue to fix the underlying cause within 5 business days.

**Why robust over easy**: The current "admin bypass merging" pattern is the entire reason this project exists. Anything less than full enforce_admins recreates the rot mechanism. The documented emergency path acknowledges that real emergencies happen without giving everyday merges a casual escape hatch.

**Why this is not over-engineering**: incident-response procedure is a one-page document. enforce_admins is one API call. The total operational cost is minutes, not hours per month. The cost of NOT doing this is the next test-suite-repair project in 2027.

### 5.3 Integration test scope: in scope

**Resolved: Option A.** `tests/integration/Spe.Integration.Tests/` is in scope. Phase 0 runs the baseline; repair work is sequenced into Phase 2/3 alongside `Sprk.Bff.Api.Tests` work.

**Why robust over easy**: integration tests are the layer that catches real Graph/SPE behavior unit tests can't (mocked Graph ≠ real Graph). The 2026-05-29 BFF audit identified Graph drift as a recurring incident source. Repairing only the unit tests leaves the higher-value verification layer broken.

**Effort impact**: adds ~12-20h to project total. Project total revised in §10.

### 5.4 Triage authority: agent judges, following binding rules

**Resolved: Option A (refined).** AI agent judges per-file repair-vs-archive decisions, **strictly bounded by the binding rules in §6 and the escalation procedure in §4.8.**

**Refinements** (these prevent the silent-drift risk):
- Agent applies §6.2 triage taxonomy explicitly — every decision tagged in the PR description
- Agent escalates per §4.8 for any rewrite (>50% lines replaced)
- Agent escalates if archive count exceeds 10% of touched files in a single phase (signals over-aggressive archiving)
- Owner does NOT pre-approve individual decisions; owner reviews the per-phase exit ledger

**Why robust over easy**: per-decision owner approval would burden the owner with hundreds of micro-decisions. The binding rules + escalation triggers + ledger-at-phase-exit gives agent judgment AND owner oversight without per-decision PR-process burden.

### 5.5 Anti-drift enforcement: code review checklist + PR template question

**Resolved: Option D (no CI script).** Anti-drift is enforced by:
1. New section in [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) titled "Test update obligation" with explicit checklist
2. PR template (`.github/pull_request_template.md`) adds one question: "If this PR modifies `src/server/api/Sprk.Bff.Api/Services/`, has a corresponding test been added/updated?"
3. Code review checklist in [`docs/procedures/`](../../docs/procedures/) gets one new line: "verify test-update obligation per `.claude/constraints/bff-extensions.md`"

**Why NOT a CI script** (Option C from initial draft): user direction is "do not want PR process overburdened." A CI script that fails PRs without test changes produces false positives on doc-only PRs, config-only PRs, and refactor PRs where test coverage is unchanged-but-still-valid. Manual review by humans (reinforced by template + checklist) catches the same signal without the noise.

**Why this is robust enough**: The 2026-05-30 audit showed every project shipped tests; the problem wasn't "tests weren't written" — it was "tests were written, then drifted as services evolved, and no project owned correction." That's a code-review-discipline problem, not a CI-enforcement problem. Solving it with CI gates would be over-engineering against the wrong failure mode.

### 5.6 Three namespace fix edits: KEEP

**Resolved: Option A.** The three in-progress namespace fixes are kept in working tree and become the project's first commit.

**Why**: They are legitimate fixes for real drift. Reverting them would discard correct work to "preserve a clean starting state" — that's process theater, not engineering value. The fixes are exactly what Phase 1 would re-do; doing them now saves the re-discovery cost.

---

## 6. Binding Rules (apply to every task in this project)

### 6.1 Test changes do not change production

(Restated from §4.4 because it's load-bearing.) No PR in this project may modify `src/`, `power-platform/`, `infra/`, or `scripts/`. If a failing test reveals a production bug, that bug is filed and the test is marked `[Trait("status", "real-bug-pending-fix")]`. A separate PR/project addresses the production bug.

### 6.2 Triage taxonomy

Every touched test must end in one of these explicit end-states by project close. The Phase 4 exit ledger reports counts per state. The four "intermediate" states are valid DURING phases but MUST be resolved before project close.

**Final end-states** (acceptable at project close):

```
[Trait("status", "repaired")]              // Pass. Test asserts current behavior; signature/namespace/assertion updated.
[Trait("status", "real-bug-pending-fix")]  // Skip. Test is CORRECT; production has a bug. Filed in real-bug-ledger.md.
                                           //   Test stays Skip'd in main; production fix unblocks it.
[Trait("status", "flaky-quarantined")]     // Skip. Non-deterministic; quarantined with a fix-by date in flaky-ledger.md.
                                           //   ONLY acceptable if the flakiness is environmental (timing, ports, etc.)
                                           //   NOT acceptable as a hiding place for tests we couldn't be bothered to fix.
```

OR removed from suite via §6.5 archive (file renamed; no longer counted):

```
- archived-duplicate     // Coverage exists in synthetic smoke or E2E. Test was redundant.
- archived-dead-target   // Targets deleted code. Repair has no possible passing state.
- archived-rewrite       // Replaced by a new file per §4.8 escalation; old file archived for git history.
```

**Intermediate states** (valid mid-phase, MUST resolve before phase exit):

```
[Trait("status", "in-progress-task-T-XX")]      // Active repair work
[Trait("status", "blocked-on-cluster-fix")]     // Waiting on a cluster fix (e.g., IChatClient helper); resolved when cluster ships
```

**Forbidden at project close**: `Failed`. Zero tests in the suite are in `Failed` state when the project closes. See §4.3.

### 6.3 Cite measured numbers, not overview estimates

Tasks must cite §3 numbers (5,215 / 4,844 / 269 / 17 / etc.). Tasks must not cite the overview's "283 failures" or "~50-100 / ~30-50 / ~130-200" ranges as authoritative.

### 6.4 No new failure modes

If a repair would require touching `CustomWebAppFactory.cs` in a way that risks the 4,844 passing tests, the repair must be batched, the full suite must run before and after, and any new failures must be triaged before merge.

### 6.5 No silent deletion

Archive only via rename to `*.archived-YYYY-MM-DD` (matches existing precedent). Delete-from-disk is prohibited; this preserves git history continuity and the option to restore.

### 6.6 Existing test conventions honored

Tasks follow [`docs/procedures/testing-and-code-quality.md`](../../docs/procedures/testing-and-code-quality.md). New helpers (e.g., the IChatClient `IAsyncEnumerable` helper from §5.1) follow existing namespace and folder conventions.

### 6.7 Rewrite escalation enforced per §4.8

Every task POML includes `repair_not_rewrite: true` in its front-matter. If the assigned agent determines a file needs rewriting (>50% line replacement), the agent HALTS, files an escalation per §4.8, and waits for owner approval before resuming. Code review enforces this — diffs with >50% line replacement and no escalation record in `escalations/` are rejected.

### 6.8 Project-level CLAUDE.md is binding

`projects/sdap-bff.api-test-suite-repair/CLAUDE.md` (created in Phase 0) restates these rules and is loaded by every task agent before work begins. Conflicts between this design.md and the project CLAUDE.md resolve in favor of CLAUDE.md (it is the agent-visible source of truth at execution time).

---

## 7. Phased Delivery Plan — Parallelism-First

One project. Phases are gates, not silos. Within and across phases, the dependency structure is designed to maximize parallel execution without sacrificing quality, completeness, or accuracy. The dependency rules in §7.0 are binding.

### 7.0 Dependency rules (binding for SPEC.md task generation)

**Hard dependencies** (a downstream task may NOT start until its upstream completes):

| Upstream | Downstream | Why |
|---|---|---|
| Phase 0 (Decisions + Baseline) | Everything else | Locks the baseline data and resolves §5 (already done in this doc, but Phase 0 captures the official artifact set) |
| P1.A1 (Compile fixes for 17 files) | Any task that runs `dotnet test` | Test project must compile to produce a runtime failure count |
| P1.B1 (`AsyncEnumerableHelpers` shipped) | P2.A1-A2 (IChatClient cluster migrations) | Migrations consume the helper |
| P1.C1 (CustomWebAppFactory extension) | P2.B1-Bn (Factory-dependent test repairs) | Repairs may rely on new fake config values |

**Soft dependencies** (preferable but not required — can parallelize with awareness):

| Task | Aware-of | Why |
|---|---|---|
| Any HIGH-tier repair (P3.H*) | Phase 0 baseline | To verify the test was failing for the expected reason |
| Any LOW-tier archive decision (P3.L*) | Synthetic smoke coverage map | To verify the archive-as-duplicate claim |

**Parallelism unlocks** (sets of tasks that MUST be runnable concurrently):

| Unlock point | What parallelizes |
|---|---|
| After P0 | P1.A (compile), P1.B (helper build), P1.C (factory ext.), P1.D (CI gate restoration), P1.E (integration baseline) — **5 parallel tracks** |
| After P1.A + P1.B + P1.C complete | P2.A (IChatClient migrations), P2.B (factory-dependent repairs), P3.H (HIGH-tier long tail), P3.M (MEDIUM-tier long tail), P3.I (INTEGRATION-tier repairs) — **5 parallel tracks**; HIGH/MEDIUM/INTEGRATION can run together since each touches disjoint file sets |
| After P3 reaches "stable" (no new failures for 3 consecutive full runs) | P3.L (LOW-tier triage/archive), P4 (governance docs) — **2 parallel tracks** |

**Anti-parallelism guard**: any task that modifies `CustomWebAppFactory.cs` runs in **isolation** — never parallel with other repair tasks. Factory changes have global blast radius across 4,844 tests; serialization prevents tangled regressions.

---

### Phase 0 — Baseline + Decision Capture (1 day, serial)

**Goal**: Produce the artifact set every subsequent task depends on.

**Deliverables**:
- `baseline/` folder:
  - `test-baseline-2026-MM-DD.trx` — full TRX from `dotnet test` with current state
  - `compile-errors-2026-MM-DD.txt` — full error log + per-file inventory
  - `ci-gate-snapshot-2026-MM-DD.json` — `gh api repos/.../branches/master/protection` output + last 30 CI runs
  - `integration-test-baseline-2026-MM-DD.trx` — `Spe.Integration.Tests` baseline (per §5.3)
- `decisions/` folder: D-01 through D-06 capturing the §5 resolutions in single-purpose files (for audit + future-project reference)
- `CLAUDE.md` for the project (encodes §6 binding rules + §4 resolved decisions; loaded by every task agent)
- `priority-order.md`: HIGH→MEDIUM→INTEGRATION→LOW with sibling-project owner sign-offs annotating in-flight areas
- Optional: researcher-subagent verdict on `Microsoft.Extensions.AI.Testing` helpers (per §5.1 — if mature, override hand-rolled helper plan)

**Exit gate**: All baseline artifacts exist; CLAUDE.md is in place; owner signs off priority order.

**Parallelism within Phase 0**: baseline data capture, decisions write-up, researcher investigation can all parallelize (3 tracks).

---

### Phase 1 — Unblock Everything (3-5 days, 5 parallel tracks)

Five tracks run in parallel after Phase 0 exits. Cross-track integration only at Phase 1 exit gate.

**Track P1.A — Compile recovery** (~5-8h):
- Fix 17 compile-broken files via signature updates and obsolete API migrations
- Each file: ≤30 min triage + fix; if any file needs rewrite, escalate per §4.8
- After each batch of 4 files, run `dotnet build` to track error count drop
- Exit: `dotnet build tests/unit/Sprk.Bff.Api.Tests/` returns 0 errors; full runtime failure count captured

**Track P1.B — `AsyncEnumerableHelpers` design + build** (~4-6h):
- (Subject to researcher verdict from Phase 0) Build hand-rolled `IAsyncEnumerable` test helper in `Mocks/AsyncEnumerableHelpers.cs`
- Unit tests for the helper itself (this IS new test code; out-of-scope rule doesn't apply since the helper is infrastructure, not behavior)
- Documentation in test project README
- Exit: helper compiles, has its own tests, is ready for cluster migration

**Track P1.C — `CustomWebAppFactory` extension** (~3-5h):
- Inventory missing fake config values causing startup failures (extract from `baseline/` test run logs)
- Add fake values to factory; do NOT restructure existing logic (§4.5)
- Verify no regression in 4,844 baseline (this track runs in ISOLATION per §7.0 anti-parallelism guard)
- Exit: factory updated; full run confirms baseline preserved + N additional tests now reach assertion phase

**Track P1.D — CI gate hardening** (~4-6h):
- Set `enforce_admins: true` for required `Build & Test` and `Code Quality` checks
- Remove `skip-tests` workflow_dispatch input from `deploy-bff-api.yml`
- Author `docs/procedures/bff-deploy-emergency.md` with named approver allowlist + 5-day fix-by clause
- Verify gate works: deliberately push a failing-test PR; confirm it blocks
- Exit: CI gate operational; verified by negative-path test

**Track P1.E — Integration test baseline + triage** (~4-6h):
- Run `Spe.Integration.Tests`; capture pass/fail counts
- Classify failures by pattern (compile drift / signature drift / real Graph regression / wiremock drift)
- Produce `integration-test-triage.md` mirroring §3 categorization
- Exit: integration baseline documented; classified failure list ready for Phase 2/3 absorption

**Phase 1 exit gate**: All 5 tracks complete. Post-Phase-1 runtime failure count documented (will be higher than 269 because compile-broken files now contribute their runtime failures). This is the **true repair baseline** that Phases 2+3 work against.

---

### Phase 2+3 (MERGED) — Repair Execution (10-18 days, 5 parallel tracks)

Phase 2 (cluster fixes) and Phase 3 (long tail) collapse into one parallel execution phase. There is no point sequencing them — cluster fixes and tier-by-tier long-tail work touch disjoint file sets.

**Track P23.A — IChatClient streaming cluster** (~8-12h):
- Migrate the 30-50 affected tests to use `AsyncEnumerableHelpers` (from P1.B)
- Each test migration: <15 min; batchable
- After batch of 10, run full suite (regression check)
- Exit: all IChatClient streaming tests pass OR are archived per §6.2

**Track P23.B — WebApplicationFactory-dependent cluster** (~4-8h):
- The tests that needed P1.C's factory extension to reach assertion phase — now repair their assertions
- Group by failure pattern (response shape changed, error code changed, etc.)
- Exit: all factory-dependent tests pass OR are archived

**Track P23.H — HIGH-tier long tail** (~10-15h):
- 35 files: Workspace scoring, Scorecard, Finance signals, Email association, Ai/Safety, Filters, Infrastructure
- Each file: full triage per §6.2; status trait applied to every touched test
- Largest files first (PriorityScoringServiceTests at 772 LOC, EffortScoring at 872 LOC, etc.)
- Exit: zero failures in HIGH tier (Pass / Skipped-real-bug / archived)

**Track P23.M — MEDIUM-tier long tail** (~12-18h):
- 70 files: Ai/Chat/*, Ai/Capabilities/*, Ai/Nodes/*, Communication/*
- COORDINATION: Communications track owner-aligned with `x-email-communication-solution-r2` project owner (this is the highest-risk sibling-coordination area per §2.3)
- Exit: zero failures in MEDIUM tier

**Track P23.I — INTEGRATION tier** (~8-12h):
- 25 files in `Sprk.Bff.Api.Tests/Integration/` PLUS the in-scope `Spe.Integration.Tests` failures from P1.E
- WireMock fixtures may need updating; that IS part of test-only changes
- Exit: zero failures in INTEGRATION tier

**Sub-track P23.L — LOW-tier triage** (~6-10h, starts when HIGH+MEDIUM 50% complete):
- 88 endpoint pipeline files: triage each as repair OR archive-duplicate (per §6.2)
- Default action: archive if duplicates synthetic smoke; repair if tests a response-shape contract that smoke doesn't
- Owner approval triggered if archive count exceeds 10 files in this tier (per §5.4 refinement)
- Exit: zero failures in LOW tier (Pass / archived)

**Cross-track regression checks**: every 4h, an automated full-suite run is queued. Any new failure (in tests not currently being worked) HALTS all tracks until investigated. Likely cause: a factory edit or a shared helper change. Anti-parallelism guard kicks in.

**Phase 2+3 exit gate**: Zero failures in suite. Every touched test has a §6.2 status trait. `repair-ledger.md`, `archive-ledger.md`, `real-bug-ledger.md`, `flaky-ledger.md` populated.

---

### Phase 4 — Governance + Validation (2-3 days, 2 parallel tracks)

**Track P4.A — Governance documents** (~4-6h):
- Update [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) per §5.5
- Update PR template
- Update code review checklist in [`docs/procedures/`](../../docs/procedures/)
- Update CLAUDE.md §10 to reference test-update obligation
- Update `task-execute` skill if rigor-level rules need an addition for BFF-touching tasks

**Track P4.B — Validation + exit ledger** (~4-6h):
- Run full suite 3 times; confirm zero failures across all runs
- Confirm CI gate still operational (negative-path retest)
- Publish `exit-ledger.md`: counts per §6.2 status; per-tier file disposition; coordination outcomes with sibling projects; total effort actual vs. §10 estimate
- Confirm all §9 success criteria met
- Archive `baseline/` snapshots as the "before" state for future audits

**Phase 4 exit gate**: Owner sign-off; project marked complete; success criteria §9 all green.

---

### 7.E Parallelism summary

```
Time →

Phase 0 (1 day, serial)
   └─ Decisions + Baseline + CLAUDE.md
        │
Phase 1 (3-5 days, 5 parallel tracks)
   ├─ P1.A  Compile recovery
   ├─ P1.B  AsyncEnumerableHelpers
   ├─ P1.C  Factory extension (ISOLATED — anti-parallelism guard)
   ├─ P1.D  CI gate hardening
   └─ P1.E  Integration test baseline
        │
Phase 2+3 MERGED (10-18 days, 5 parallel tracks)
   ├─ P23.A  IChatClient cluster
   ├─ P23.B  Factory-dependent cluster
   ├─ P23.H  HIGH-tier long tail
   ├─ P23.M  MEDIUM-tier long tail
   ├─ P23.I  INTEGRATION-tier (incl. Spe.Integration.Tests)
   └─ P23.L  LOW-tier triage (starts when H+M 50% done)
        │
Phase 4 (2-3 days, 2 parallel tracks)
   ├─ P4.A   Governance documents
   └─ P4.B   Validation + exit ledger
```

**Wall-clock minimum**: ~16 days if all parallel tracks run efficiently. **Wall-clock realistic**: ~22-28 days accounting for owner sync points, sibling project coordination, and regression-investigation pauses.

**Person-hours total**: ~50-75h (revised from 38-59h to include integration test scope and the formal escalation procedure overhead).

---

## 8. Risk Register

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| **Repair silently changes production behavior** (test changed to match buggy production) | MEDIUM | HIGH | §6.1 binding rule (no production changes); §6.2 triage taxonomy (`real-bug-pending-fix` status); per-PR review focuses on "did the test get easier to pass for the wrong reason?" |
| **CI gate restoration blocked by political pressure** ("we need to ship X today") | MEDIUM | HIGH | §5.2 Option C provides documented emergency path; owner sign-off in Phase 0 protects against mid-project re-litigation |
| **Sibling projects (Action Engine, Insights Phase 2) introduce new drift faster than repair pace** | MEDIUM | MEDIUM | §4.7 coordination model; priority order sequences active areas last; daily sync during repair phases |
| **IChatClient cluster fix turns out harder than estimated** (Microsoft.Extensions.AI evolution) | MEDIUM | MEDIUM | §5.1 Option C (researcher subagent first); fallback to Option B (Moq) if A fails |
| **17 compile-broken files reveal more drift than the visible 138 errors** | LOW | MEDIUM | Phase 1 Track 1A explicitly measures post-fix runtime failure delta before Phase 2 begins; budget contains 25% buffer |
| **CustomWebAppFactory extension introduces regression in 4,844 passing tests** | LOW | HIGH | §4.5 (no rewrite); §6.4 (run before+after); changes batched, not interleaved with individual repairs |
| **Failure-cluster grouping wrong** (we think one fix solves N tests; turns out it solves M < N) | MEDIUM | LOW | Phase 2 exit gate is empirical (count ≤150), not assumed |
| **Test trait taxonomy under-used by repair tasks** (tests get fixed but not tagged) | LOW | MEDIUM | Phase 4 validates tag presence; task templates include trait as required |
| **The 92.9% baseline is stale by Phase 4** (drift introduced during the project itself) | MEDIUM | LOW | Run full suite at every phase exit; track baseline drift in `baseline/` folder |

---

## 9. Success Criteria

The project ships when ALL of the following are true:

| # | Criterion | Verification |
|---|---|---|
| 1 | Test project compiles cleanly under `-warnaserror` | `dotnet build tests/unit/Sprk.Bff.Api.Tests/ -warnaserror` returns 0 errors |
| 2 | **ZERO failing tests** across `Sprk.Bff.Api.Tests` AND `Spe.Integration.Tests` | `dotnet test` summary lines for both projects show 0 in `Failed` column |
| 3 | Every touched test ends in a §6.2 final end-state (`repaired`, `real-bug-pending-fix`, `flaky-quarantined`, or archived via §6.5) | Phase 4 audit script counts traits + archive renames; total = touched-files count |
| 4 | CI gate is operational | A deliberately-failing PR is blocked by `Build & Test (Release)` status check |
| 5 | `enforce_admins: true` for required status checks; `skip-tests` removed from `deploy-bff-api.yml` | `gh api repos/.../branches/master/protection` + `deploy-bff-api.yml` diff |
| 6 | Last 5 `sdap-ci.yml` runs on master are SUCCESS | `gh run list` |
| 7 | Last 3 `deploy-bff-api.yml` runs are SUCCESS (or N/A if no deploys in window) | `gh run list` |
| 8 | `.claude/constraints/bff-extensions.md` includes test-update obligation per §5.5 | File diff |
| 9 | CLAUDE.md §10 references the test-update obligation | File diff |
| 10 | `docs/procedures/bff-deploy-emergency.md` exists with named approver allowlist | File exists |
| 11 | Project CLAUDE.md exists and was loaded by every task agent | File exists; task POML front-matter references it |
| 12 | Rewrite escalations stayed under 5% of touched files | `rewrite-ledger.md` count / touched-file count ≤ 0.05 |
| 13 | Exit ledger published with per-state counts + sibling-coordination outcomes | `exit-ledger.md` exists |
| 14 | `real-bug-ledger.md` + `flaky-ledger.md` exist with fix-by dates for any non-Pass entries | Files exist; every entry has a date or owner sign-off |

---

## 10. Effort Estimate (revised after §5 resolution + integration scope addition)

The overview estimated 13-20h. Revised based on measured baseline AND locked-in scope additions (integration tests, formal escalation procedure):

| Phase | Person-hours | Wall-clock (with parallelism) | Composition |
|---|---|---|---|
| Phase 0 — Baseline & Decisions | 4-6h | 1 day | Documentation + decision capture; 3 parallel tracks |
| Phase 1 — Unblock (5 parallel tracks) | 20-31h | 3-5 days | P1.A 5-8h + P1.B 4-6h + P1.C 3-5h (isolated) + P1.D 4-6h + P1.E 4-6h |
| Phase 2+3 — Repair (5 parallel tracks) | 48-75h | 10-18 days | P23.A 8-12h + P23.B 4-8h + P23.H 10-15h + P23.M 12-18h + P23.I 8-12h + P23.L 6-10h |
| Phase 4 — Governance + Validation | 8-12h | 2-3 days | 2 parallel tracks |
| **Total person-hours** | **80-124h** | — | All work, all tracks |
| **Total wall-clock** | — | **16-27 days** | If parallelism executed well |

**Why higher than the prior draft's 38-59h**:
- §5.3 lock-in (integration tests in scope): +12-20h
- §4.8 formal escalation procedure: ~2-4h overhead (file authoring, decision capture)
- §6 expanded binding rules: ~2-3h per-track compliance overhead
- More conservative HIGH/MEDIUM tier estimates (predecessor pattern was 4-6 min/test; this project's tier-by-tier discipline pushes that to 6-10 min/test average)

**Why wall-clock is shorter than person-hours suggests**: parallel tracks. 80-124 person-hours spread across 5 concurrent tracks compresses calendar time substantially. The 16-day floor assumes (a) every parallel track is staffed simultaneously, (b) cross-track regression checks reveal no major issues, (c) sibling-project coordination doesn't introduce blocking pauses. The 27-day realistic estimate accounts for those frictions.

**If solo-executed (no parallelism)**: estimate ~28-40 calendar days at typical pace. The parallelism savings is the project's structural advantage; do not surrender it without cause.

---

## 11. Out of Scope (explicitly deferred)

Items considered and excluded from this project's scope:

| Item | Why deferred | Where it goes |
|---|---|---|
| Factory rewrite (split into pure unit + in-process integration projects) | High cost (100-140h); current factory works for 4,844 tests; would risk the regression safety net during repair | Follow-on §11.1 Project 2 |
| Increasing test coverage % | Different project; this one repairs existing coverage breadth, not depth | Follow-on §11.1 Project 3 |
| Test quality upgrade (rewriting low-value mocked-orchestration tests) | Conflicts with §4.1 repair-not-rewrite; would balloon scope | Follow-on §11.1 Project 4 |
| Mutation testing / chaos verification | Premature on a not-yet-green suite | Follow-on §11.1 Project 5 |
| Test data / fixture management standardization | Each test area currently invents its own pattern; standardization is non-trivial | Follow-on (folded into Project 2 or 3) |
| Contract-test vs. smoke-test discipline (formal split) | Boundary stays fuzzy after repair; formalizing it requires architecture work | Follow-on Project 2 |
| Performance test discipline | One file exists; no standard. Not the rot mechanism we're fixing here. | Future, possibly outside test-suite track |
| NSubstitute → Moq unification (or vice versa) | Mixed framework is intentional per task history | Defer indefinitely; no compelling case |
| Spaarke.Core.Tests / Spaarke.Plugins.Tests | Healthy as of 2026-05-30 baseline | No action needed |
| New-feature test coverage (Action Engine, Insights Phase 2, Communications) | Owned by their respective projects; §5.5 governance binds them to test-update obligation | Their projects, not this one |

### 11.1 Follow-on Projects (gap-naming for tracking)

This project restores the suite to a stable baseline. It does **not** make the suite ideal. The following follow-on projects are NOT scoped, NOT estimated, and NOT committed — they are named here to set accurate expectations and to record the backlog so future audits don't re-litigate "should we look at the BFF tests?"

| # | Project (proposed name) | What it delivers | Sequencing trigger |
|---|---|---|---|
| **1** | `sdap-bff.api-test-suite-repair` (THIS PROJECT) | Restored baseline; zero failures; CI gate operational; anti-drift governance. **Load-bearing — gates 2-5.** | NOW |
| **2** | `sdap-bff-test-architecture-r1` (proposed) | Split into `Sprk.Bff.Api.UnitTests` (pure logic, no `WebApplicationFactory`) + `Sprk.Bff.Api.IntegrationTests` (in-process HTTP). Formalize contract-test vs. smoke-test boundary. Standardize fixture management. | After Action Engine ships — the factory bottleneck will be most visible then |
| **3** | `sdap-bff-test-coverage-r1` (proposed) | Code coverage % becomes a meaningful target. Identify uncovered code paths; prioritize; write new tests. | After Project 2 — coverage % is only meaningful when architecture is right |
| **4** | `sdap-bff-test-quality-r1` (proposed) | Audit existing tests for defect-prevention value. Rewrite or archive low-value mocked-orchestration tests. May fold into Project 3. | Concurrent with 3, or merged |
| **5** | `sdap-bff-test-rigor-r1` (proposed) | Mutation testing (Stryker.NET or equivalent). Verify tests catch the mutations they claim to. | After 3-4 stabilize — mutation testing on a healthy suite |

**Why named here, not promised**: each is a real project worth doing, but committing to them now would prematurely lock architectural decisions that should be made when each is scoped (especially Project 2's split, which depends on what Action Engine looks like at that time). The point of §11.1 is **honesty about the gap**, not a binding roadmap.

**Where this matters for THIS project**: it justifies why §4.5 (no factory rewrite) and §4.3 (zero failures, not coverage target) are correct scoping choices, not corner-cutting.

---

## 12. Pipeline Position

```
bff.api-repair-overview.txt (2026-05-28, framing)
        ↓
design.md (THIS DOCUMENT — 2026-05-30, measured baseline + decisions)
        ↓ /design-to-spec
SPEC.md
        ↓ /project-pipeline
plan.md + tasks/TASK-INDEX.md + tasks/T-XX-*.poml
        ↓ /task-execute per task
Phase 0 → 1 → 2 → 3 → 4
        ↓
Outcomes A + B + C + D shipped; project closed
```

---

## 13. Sign-Off

Before this document moves to `/design-to-spec`, owner must confirm:

1. **§4 Resolved Decisions** (8 items) stand as binding — especially §4.1 (repair not rewrite, non-negotiable), §4.3 (zero failures at close), §4.4 (no production changes), §4.8 (rewrite gated + escalation)
2. **§5 Locked Decisions** (6 items) are the resolved approach for IChatClient cluster, CI gate strictness, integration test scope, triage authority, anti-drift enforcement, and the three in-flight namespace fixes
3. **§6 Binding Rules** (8 rules) — particularly the new §6.7 (rewrite escalation enforcement) and §6.8 (project CLAUDE.md authority)
4. **§7 Phased Delivery with parallelism** — particularly §7.0 dependency rules and the 5-parallel-track structure in Phase 1 and Phase 2+3
5. **§11 NOT-IN-SCOPE list** is correct
6. **Coordination plan** with active sibling projects (§2.3) — Action Engine, Insights Engine Phase 2, Communications

**What changed from initial draft (2026-05-30 revision)**:
- §1: Outcome B target changed from "≤30 failures" to "zero failures" (per owner direction)
- §4.1: Repair-not-rewrite escalated to NON-NEGOTIABLE with three-level enforcement
- §4.3: Zero-failure target replaces ≤30 with three-destination taxonomy
- §4.8 (new): Rewrite gated by escalation procedure; not forbidden but surfaced
- §5: All 6 decisions LOCKED per owner direction ("robust path, not easy path; do not over-engineer")
- §6.2: Triage taxonomy restructured around zero-failure end-states
- §6.7-6.8 (new): Rewrite escalation enforcement; project CLAUDE.md authority
- §7: Phases 2+3 merged; 5-parallel-track structure; explicit anti-parallelism guard for factory edits
- §9: Success criteria expanded to 14 items reflecting integration tests + rewrite ceiling + ledgers
- §10: Person-hours revised to 80-124h; wall-clock 16-27 days with parallelism

**Status**: AWAITING SIGN-OFF on revised design.

