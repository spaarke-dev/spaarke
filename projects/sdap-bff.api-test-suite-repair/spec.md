# sdap-bff.api-test-suite-repair — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-05-31
> **Source**: [`projects/sdap-bff.api-test-suite-repair/design.md`](design.md) (revised 2026-05-30)
> **Predecessor**: [`projects/sdap-bff-api-remediation-fix/`](../sdap-bff-api-remediation-fix/) — Phase 4 facade shipped 2026-05-26; test suite repair deliberately deferred to this project
> **Project owner**: ralph.schroeder@hotmail.com

## Executive Summary

Repair the `Sprk.Bff.Api.Tests` (5,215 tests, currently 269 failures + 17 compile-broken files) and `Spe.Integration.Tests` test suites to a zero-failure baseline; restore the BFF CI gate that admin-bypass merging has rendered fictional (10 of 10 most recent CI runs failed; code merges and ships anyway); install anti-drift governance so the rot mechanism cannot recur. Four outcomes (compile health, runtime green, CI gate restoration, governance) ship as one project with internal parallel phases. The project is the load-bearing prerequisite for follow-on architecture, coverage, quality, and rigor projects named in design.md §11.1.

## Scope

### In Scope

- **Test repair**: `tests/unit/Sprk.Bff.Api.Tests/` (259 .cs files, 245 actual tests) — fix the 269 runtime failures and 17 compile-broken files
- **Integration test repair**: `tests/integration/Spe.Integration.Tests/` — baseline in Phase 0, repair sequenced into Phase 2+3
- **CI gate restoration**: Set `enforce_admins: true` on master branch protection; remove `skip-tests` input from [`deploy-bff-api.yml`](../../.github/workflows/deploy-bff-api.yml); create documented emergency-deploy procedure
- **Anti-drift governance**: Update [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) with test-update obligation; update PR template; update code review checklist; update [`CLAUDE.md`](../../CLAUDE.md) §10
- **Project CLAUDE.md**: Create `projects/sdap-bff.api-test-suite-repair/CLAUDE.md` encoding §6 binding rules + §4 resolved decisions; loaded by every task agent
- **Async-enumerable test helper**: Use Microsoft-shipped helper if Phase 0 researcher confirms maturity; otherwise hand-roll `AsyncEnumerableHelpers.cs`
- **CustomWebAppFactory extension** (NOT rewrite): Add missing fake config values to unblock startup failures
- **Triage ledgers**: `repair-ledger.md`, `archive-ledger.md`, `real-bug-ledger.md`, `flaky-ledger.md`, `rewrite-ledger.md`, `exit-ledger.md`

### Out of Scope

- **Production code changes** — binding rule per FR-NFR-01. If a failing test reveals a production bug, the bug is filed; the test gets `[Trait("status", "real-bug-pending-fix")]`. A separate PR/project fixes production.
- **Factory rewrite** — `CustomWebAppFactory.cs` may be extended (add fake config values, remove additional hosted services), but NOT restructured or replaced. Future project (design.md §11.1 Project 2).
- **Test architecture split** (unit vs integration project separation) — Future project per §11.1 Project 2
- **Increasing test coverage %** — Future project per §11.1 Project 3
- **Test quality upgrade** (rewriting low-value mocked-orchestration tests) — Future project per §11.1 Project 4; conflicts with FR-NFR-02 repair-not-rewrite
- **Mutation testing / chaos verification** — Future project per §11.1 Project 5
- **NSubstitute → Moq unification** — Mixed framework intentional; defer indefinitely
- **`Spaarke.Core.Tests` / `Spaarke.Plugins.Tests`** — Healthy as of 2026-05-30 baseline; no action needed
- **New-feature test coverage** for Action Engine / Insights Phase 2 / Communications — Owned by their respective projects per anti-drift governance binding

### Affected Areas

| Path | Affected by |
|---|---|
| `tests/unit/Sprk.Bff.Api.Tests/` | Primary repair target (259 files) |
| `tests/integration/Spe.Integration.Tests/` | Secondary repair target |
| `tests/unit/Sprk.Bff.Api.Tests/Mocks/AsyncEnumerableHelpers.cs` (new, or replaced by Microsoft package reference) | IChatClient streaming cluster fix |
| `tests/unit/Sprk.Bff.Api.Tests/CustomWebAppFactory.cs` | Extended (NOT rewritten) per §4.5 |
| `tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj` | May add `Microsoft.Extensions.AI.Testing` (or equivalent) package reference if researcher verdict positive |
| `.github/workflows/deploy-bff-api.yml` | Remove `skip-tests` workflow_dispatch input |
| `.github/workflows/sdap-ci.yml` | (Read-only) Verify gate behavior; no modification expected |
| GitHub branch protection on `master` | Flip `enforce_admins: true` |
| `.claude/constraints/bff-extensions.md` | Add "Test update obligation" section |
| `.github/pull_request_template.md` | Add test-update question |
| `docs/procedures/testing-and-code-quality.md` | Add test-update obligation reference |
| `docs/procedures/bff-deploy-emergency.md` (new) | Emergency-deploy procedure with owner as sole approver |
| `CLAUDE.md` §10 (BFF Hygiene) | Reference test-update obligation |
| `projects/sdap-bff.api-test-suite-repair/CLAUDE.md` (new) | Project-level binding rules for every task agent |

## Requirements

### Functional Requirements

**Phase 0 — Baseline + Decision Capture**

1. **FR-01**: Phase 0 produces `baseline/` folder with `test-baseline-{date}.trx`, `compile-errors-{date}.txt`, `ci-gate-snapshot-{date}.json`, `integration-test-baseline-{date}.trx`. — Acceptance: All 4 files exist; trx files parseable by xUnit tooling.
2. **FR-02**: Researcher subagent investigates `Microsoft.Extensions.AI.Testing` (or equivalent companion package); verdict captured in `decisions/D-01-async-enumerable-helper.md` per design.md §5.1 decision criteria. — Acceptance: Verdict file exists with explicit "use Microsoft" or "hand-roll" decision and supporting evidence.
3. **FR-03**: Project-level `CLAUDE.md` exists encoding design.md §6 binding rules + §4 resolved decisions. — Acceptance: File loadable by task agents; references design.md §4.1 (repair-not-rewrite), §4.3 (zero failures), §4.4 (no production changes), §4.8 (rewrite escalation), §6.1-6.8.
4. **FR-04**: `priority-order.md` exists with HIGH→MEDIUM→INTEGRATION→LOW tier ordering and per-area sibling-project owner sign-offs annotating in-flight areas (Action Engine, Insights Phase 2, Communications). — Acceptance: File exists; sibling owners explicitly named or marked "no in-flight overlap."

**Phase 1 — Unblock Everything (5 parallel tracks)**

5. **FR-05 (P1.A)**: All 17 compile-broken files in design.md §3.2 are repaired (signature updates, obsolete API migrations) or escalated per §4.8. — Acceptance: `dotnet build tests/unit/Sprk.Bff.Api.Tests/ -c Release` returns 0 errors; no warnings under `-warnaserror`.
6. **FR-06 (P1.B)**: `IAsyncEnumerable<T>` test helper available — either via Microsoft-shipped package reference OR hand-rolled `Mocks/AsyncEnumerableHelpers.cs` per FR-02 verdict. — Acceptance: Helper compiles, has unit tests verifying its own behavior, is consumable by P23.A migration tasks.
7. **FR-07 (P1.C)**: `CustomWebAppFactory.cs` extended with missing fake config values; NO restructuring beyond config additions and hosted-service removals. — Acceptance: Diff is additive (new dictionary entries + `services.RemoveAll<IHostedService>` calls); no method signatures changed; 4,844 baseline tests still pass after factory change.
8. **FR-08 (P1.C anti-parallelism)**: P1.C runs in isolation — never concurrent with other repair tasks. — Acceptance: Git history shows no overlapping commits to factory + other test files in the same calendar hour.
9. **FR-09 (P1.D)**: `enforce_admins: true` set on master branch protection for `Build & Test (Debug)`, `Build & Test (Release)`, and `Code Quality` status checks. — Acceptance: `gh api repos/spaarke-dev/spaarke/branches/master/protection` shows `enforce_admins.enabled: true`.
10. **FR-10 (P1.D)**: `skip-tests` workflow_dispatch input removed from [`deploy-bff-api.yml`](../../.github/workflows/deploy-bff-api.yml); `test` job no longer guarded by `if: ${{ github.event.inputs.skip-tests != 'true' }}`. — Acceptance: File diff; workflow rerun confirms test job always runs.
11. **FR-11 (P1.D)**: `docs/procedures/bff-deploy-emergency.md` created with owner (ralph.schroeder@hotmail.com) as sole emergency-deploy approver, 5-business-day follow-up-fix clause, and required incident-issue template. — Acceptance: File exists; references owner by name + email; defines "emergency" criteria; references incident-issue template.
12. **FR-12 (P1.D)**: CI gate operational verified by negative-path test (a deliberately-failing test PR is blocked by `Build & Test (Release)` status check). — Acceptance: Test PR closed without merging; status check shows `failure` and merge button is disabled.
13. **FR-13 (P1.E)**: `Spe.Integration.Tests` baseline captured; failures classified by pattern (compile drift / signature drift / real Graph regression / wiremock drift) in `integration-test-triage.md`. — Acceptance: File exists; each failure has a classification; classification informs Phase 2+3 task generation.

**Phase 2+3 — Repair Execution (5 parallel tracks)**

14. **FR-14 (P23.A)**: IChatClient streaming cluster (~30-50 tests) migrated to use FR-06 helper; all in cluster end in `Pass` or §6.2 final end-state. — Acceptance: `dotnet test --filter "FullyQualifiedName~Streaming|FullyQualifiedName~ChatClient"` shows zero failures.
15. **FR-15 (P23.B)**: Factory-dependent cluster repaired — tests that needed FR-07 to reach assertion phase now have correct assertions. — Acceptance: All tests in cluster end in §6.2 final end-state.
16. **FR-16 (P23.H)**: HIGH-tier long tail (~35 files) repaired: `Services/Workspace/*`, `Services/Scorecard*`, `Services/Finance/SignalEvaluation*`, `Services/Email/EmailAssociation*`, `Services/Ai/Safety/*`, `Filters/*`, `Infrastructure/Json/*`, `Infrastructure/Resilience/*`. — Acceptance: Zero failures in HIGH tier; all touched tests trait-tagged per §6.2.
17. **FR-17 (P23.M)**: MEDIUM-tier long tail (~70 files) repaired: `Services/Ai/Chat/*`, `Services/Ai/Capabilities/*`, `Services/Ai/Nodes/*`, `Services/Communication/*`. — Acceptance: Zero failures in MEDIUM tier; Communications track owner-aligned with `x-email-communication-solution-r2` owner.
18. **FR-18 (P23.I)**: INTEGRATION tier (~25 files in `Sprk.Bff.Api.Tests/Integration/` + `Spe.Integration.Tests` failures from FR-13) repaired. — Acceptance: Zero failures in both projects' integration test categories.
19. **FR-19 (P23.L)**: LOW-tier (~88 files) triaged — each is repaired OR archived as synthetic-smoke duplicate per §6.2. — Acceptance: Zero failures in LOW tier; archive count documented; if archive count exceeds 10 files, owner approval recorded in `archive-ledger.md`.
20. **FR-20 (P23.L start gate)**: P23.L starts only after P23.H + P23.M are 50% complete. — Acceptance: Project-pipeline task order enforces sequencing.
21. **FR-21 (cross-track)**: Full-suite regression check runs every 4 hours during repair phases; any new failure in tests not currently being worked HALTS all tracks until investigated. — Acceptance: CI shows scheduled full-suite runs; incident log shows zero unhandled new-failure events.

**Phase 4 — Governance + Validation**

22. **FR-22 (P4.A)**: [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) gains a "Test update obligation" section detailing: when a PR modifies `src/server/api/Sprk.Bff.Api/Services/`, it MUST include corresponding test additions/updates; exceptions require explicit code review sign-off citing reason. — Acceptance: File diff; new section present; cross-referenced from CLAUDE.md §10.
23. **FR-23 (P4.A)**: `.github/pull_request_template.md` adds one question: "If this PR modifies `src/server/api/Sprk.Bff.Api/Services/`, has a corresponding test been added/updated? (Yes / No / Not applicable — explain)" — Acceptance: File diff; question present.
24. **FR-24 (P4.A)**: [`docs/procedures/testing-and-code-quality.md`](../../docs/procedures/testing-and-code-quality.md) (or the code review checklist within it) gains one line: "Verify test-update obligation per `.claude/constraints/bff-extensions.md`." — Acceptance: File diff.
25. **FR-25 (P4.A)**: [`CLAUDE.md`](../../CLAUDE.md) §10 (BFF Hygiene) gains a bullet referencing the test-update obligation. — Acceptance: File diff; bullet present.
26. **FR-26 (P4.B)**: Full test suite runs 3 consecutive times with ZERO failures across `Sprk.Bff.Api.Tests` AND `Spe.Integration.Tests`. — Acceptance: 3 TRX files showing `Failed: 0` in summary line.
27. **FR-27 (P4.B)**: `exit-ledger.md` published with per-§6.2-state counts, per-tier file disposition, sibling-project coordination outcomes, and total effort actual vs. design.md §10 estimate. — Acceptance: File exists with all sections.
28. **FR-28 (P4.B)**: `rewrite-ledger.md`, `real-bug-ledger.md`, `flaky-ledger.md` exist with entries for any non-Pass tests; each entry has a date or owner sign-off. — Acceptance: Files exist; counts reconcile with FR-27 exit ledger.
29. **FR-29 (P4.B)**: Total rewrite count (escalations approved per §4.8) is ≤5% of touched files. — Acceptance: `rewrite-ledger.md` count / touched-files count ≤ 0.05.
30. **FR-30 (P4.B)**: Last 5 `sdap-ci.yml` runs on master are SUCCESS; last 3 `deploy-bff-api.yml` runs are SUCCESS (or N/A if no deploys in window). — Acceptance: `gh run list --workflow=sdap-ci.yml --branch=master --limit=5` and `gh run list --workflow=deploy-bff-api.yml --limit=3` confirm.

### Non-Functional Requirements

- **NFR-01 (binding rule)**: Test changes do NOT modify production code. No PR in this project may touch `src/`, `power-platform/`, `infra/`, or `scripts/`. If a failing test reveals a production bug, file the bug; mark the test `[Trait("status", "real-bug-pending-fix")]`. Per design.md §4.4, §6.1.
- **NFR-02 (binding rule)**: Repair-not-rewrite is non-negotiable. A diff replacing >50% of a test file's lines requires escalation per design.md §4.8 BEFORE work proceeds. Code review rejects unescalated >50% replacements.
- **NFR-03**: No new DI registrations in tests. Preserves ADR-010 baseline.
- **NFR-04**: Archive count >10 files in a single phase triggers owner escalation per design.md §5.4 refinement.
- **NFR-05 (project-level escalation)**: If escalated rewrite count exceeds 5% of touched files, the project pauses for design-review with owner — signals that the repair-not-rewrite thesis is wrong and the design needs revisiting.
- **NFR-06**: No silent deletion of tests. Archive via rename to `*.cs.archived-YYYY-MM-DD` (matches `JobProcessorTests.cs.archived-2025-10-14` precedent).
- **NFR-07 (anti-parallelism guard)**: Tasks that modify `CustomWebAppFactory.cs` run in isolation — never parallel with other repair tasks. Per design.md §7.0.
- **NFR-08**: Project CLAUDE.md is the agent-visible source of truth at execution time. Conflicts with design.md resolve in favor of CLAUDE.md (CLAUDE.md is loaded; design.md is reference). Per design.md §6.8.
- **NFR-09**: Every task POML's front-matter declares `repair_not_rewrite: true` and references the project CLAUDE.md.
- **NFR-10**: Every touched test ends in a §6.2 final end-state with explicit `[Trait("status", …)]`. `Failed` is NOT a valid end-state at project close.
- **NFR-11**: Compile-broken files must compile cleanly under `-warnaserror` after repair (matches the CI gate's build requirement).
- **NFR-12**: Parallelism is the project's structural advantage. Tasks within a phase run concurrently per design.md §7.0 unlock points; serial execution is the fallback only if concurrent agent capacity is unavailable.

## Technical Constraints

### Applicable ADRs

| ADR | Relevance |
|---|---|
| **ADR-001** (Minimal API) | Tests target this pattern; CI gate enforcement supports it |
| **ADR-007** (SpeFileStore facade) | Affects SPE-related test repair (Integration tests, file-operation tests) |
| **ADR-010** (DI minimalism) | Tests must NOT increase BFF DI registration count via new scaffolding |
| **ADR-013 refined** (AI extends BFF) | AI-coupled test repair stays in-process; tests do NOT extract to external test projects |
| **ADR-028** (Spaarke Auth) | Existing `FakeAuthHandler` pattern preserved; no parallel auth fake |
| **ADR-002** (Plugin assembly size) | Not directly affected (no plugin changes); referenced for awareness |

### MUST Rules (from ADRs + design.md binding rules)

- ✅ MUST repair existing tests in place; MUST NOT replace >50% of a file without escalation per §4.8 (NFR-02)
- ✅ MUST cite design.md §3 measured numbers, NOT the overview's stale "283 failures" framing
- ✅ MUST archive via rename, never delete (NFR-06)
- ✅ MUST tag every touched test with `[Trait("status", …)]` from §6.2 taxonomy
- ✅ MUST run full suite before AND after any `CustomWebAppFactory.cs` change (NFR-07)
- ✅ MUST get owner sign-off before exceeding 10 archives in a single phase (NFR-04)
- ❌ MUST NOT modify `src/`, `power-platform/`, `infra/`, or `scripts/` (NFR-01)
- ❌ MUST NOT increase BFF DI registration count via test scaffolding (NFR-03)
- ❌ MUST NOT rewrite `CustomWebAppFactory.cs`; extension only (§4.5)
- ❌ MUST NOT leave any test in `Failed` state at project close (FR-03, §4.3)
- ❌ MUST NOT silently delete tests (NFR-06)
- ❌ MUST NOT bypass the §4.8 rewrite escalation procedure
- ❌ MUST NOT proceed past §4.8 hard limit (5% of touched files escalated) without owner design-review

### Existing Patterns to Follow

- **Test factory pattern**: See [`tests/unit/Sprk.Bff.Api.Tests/CustomWebAppFactory.cs`](../../tests/unit/Sprk.Bff.Api.Tests/CustomWebAppFactory.cs) — extend; do not rewrite
- **Fake auth pattern**: See [`tests/unit/Sprk.Bff.Api.Tests/Mocks/`](../../tests/unit/Sprk.Bff.Api.Tests/Mocks/) `FakeGraphClientFactory.cs` and `FakeAuthHandler` — replicate pattern for new fakes
- **Archive precedent**: See [`tests/unit/Sprk.Bff.Api.Tests/JobProcessorTests.cs.archived-2025-10-14`](../../tests/unit/Sprk.Bff.Api.Tests/) — naming convention for archived tests
- **Predecessor remediation patterns**: See [`projects/sdap-bff-api-remediation-fix/`](../sdap-bff-api-remediation-fix/) for ledger conventions, task taxonomy, and Phase 4 facade migration
- **Testing procedures**: See [`docs/procedures/testing-and-code-quality.md`](../../docs/procedures/testing-and-code-quality.md)
- **Project CLAUDE.md convention**: See sibling projects' `CLAUDE.md` files for structure

## Success Criteria

(From design.md §9 — 14 criteria, all required)

1. [ ] Test project compiles cleanly under `-warnaserror` — Verify: `dotnet build tests/unit/Sprk.Bff.Api.Tests/ -warnaserror` returns 0 errors
2. [ ] **ZERO failing tests** across `Sprk.Bff.Api.Tests` AND `Spe.Integration.Tests` — Verify: `dotnet test` summary lines for both projects show 0 in `Failed`
3. [ ] Every touched test ends in a §6.2 final end-state — Verify: Phase 4 audit script counts traits + archive renames; total equals touched-files count
4. [ ] CI gate is operational — Verify: Deliberately-failing PR is blocked by `Build & Test (Release)`
5. [ ] `enforce_admins: true` for required status checks; `skip-tests` removed from `deploy-bff-api.yml` — Verify: `gh api repos/.../branches/master/protection` + `deploy-bff-api.yml` diff
6. [ ] Last 5 `sdap-ci.yml` runs on master are SUCCESS — Verify: `gh run list --workflow=sdap-ci.yml --branch=master --limit=5`
7. [ ] Last 3 `deploy-bff-api.yml` runs are SUCCESS (or N/A) — Verify: `gh run list --workflow=deploy-bff-api.yml --limit=3`
8. [ ] `.claude/constraints/bff-extensions.md` includes test-update obligation — Verify: File diff
9. [ ] CLAUDE.md §10 references the test-update obligation — Verify: File diff
10. [ ] `docs/procedures/bff-deploy-emergency.md` exists with named approver — Verify: File exists; owner named
11. [ ] Project CLAUDE.md exists and is referenced by every task agent — Verify: File exists; task POML front-matter references it
12. [ ] Rewrite escalations stayed under 5% of touched files — Verify: `rewrite-ledger.md` count / touched-file count ≤ 0.05
13. [ ] Exit ledger published with per-state counts + sibling-coordination outcomes — Verify: `exit-ledger.md` exists
14. [ ] `real-bug-ledger.md` + `flaky-ledger.md` exist with fix-by dates for any non-Pass entries — Verify: Files exist; every entry has a date or owner sign-off

## Dependencies

### Prerequisites

- Design.md §3 measured baseline (already captured 2026-05-30)
- Design.md §4 Resolved Decisions + §5 Locked Decisions (already locked)
- Design.md §6 Binding Rules (already locked)
- Owner sign-off on design.md §13 (this spec.md's existence implies sign-off)
- Predecessor `sdap-bff-api-remediation-fix` Phase 4 facade stable (verified 2026-05-26)
- The 3 in-progress namespace fixes (kept per §5.6) form the project's first commit

### External Dependencies

- **GitHub API access** for `enforce_admins` flip on master branch protection (admin token required)
- **Researcher subagent** for Phase 0 `Microsoft.Extensions.AI.Testing` verdict (per FR-02)
- **Sibling-project owner cooperation** for priority-sequencing in-flight areas:
  - `ai-spaarke-action-engine-r1` owner (Action Engine adds new BFF endpoints/services)
  - `ai-spaarke-insights-engine-r1` owner (Insights Phase 2 adds tests under `Services/Ai/`)
  - `x-email-communication-solution-r2` owner (Communications adds tests under `Services/Communication/`; multiple files in this project's compile-broken set)

### Coordination Constraints

- Do NOT freeze sibling projects during repair (per design.md §4.7); coordinate via daily sync
- Communications-related test files (ArchivalFlow, AssociationMapping, AttachmentValidation, CommunicationService, DataverseRecordCreation, EmailAttachmentExtraction) have **highest sibling-coordination risk**; assign owner before touching

## Owner Clarifications

Captured from the 2026-05-29 / 2026-05-30 / 2026-05-31 design conversation:

| Topic | Question | Answer | Impact |
|---|---|---|---|
| Failure target | The original overview said "≤30 remaining" — why should there be any? | **Zero**. ≤30 was wrong for this project; the whole scope IS the test suite. Acceptable destinations: Pass / Skipped-with-reason / archived. Never `Failed`. | Drove §4.3 rewrite and §6.2 taxonomy redesign |
| Repair vs rewrite | If repair-not-rewrite is critical, how do we surface a real rewrite need? | Non-negotiable default. In project CLAUDE.md, every task POML front-matter. Escalation via §4.8 — agent halts, files request, owner approves. Hard limit: 5% of touched files. | Drove §4.1 NON-NEGOTIABLE upgrade and §4.8 new section |
| §5 Open Decisions stance | Robust vs easy? | "Take the most robust path, not the easy path; but do not over-engineer if not critical or material to quality." | All §5 items resolved on the robust side, with NO CI script for governance (would over-burden PR process) |
| Triage authority | Per-file judgment cadence? | Agent judges per §6 binding rules + §4.8 escalation. Owner reviews per-phase exit ledger, NOT per-decision. | §5.4 lockup |
| Namespace fixes (3 files) | Keep or revert? | Keep. They're legitimate fixes; reverting would be process theater. | First commit of project |
| Phasing | Parallel where possible? | Yes — without sacrificing quality, completeness, accuracy. | Drove §7 redesign: Phase 2+3 merged; 5 parallel tracks; anti-parallelism guard for factory |
| Emergency-deploy approver | Who is authorized? | Owner-only (ralph.schroeder@hotmail.com). | FR-11; `docs/procedures/bff-deploy-emergency.md` content |
| IChatClient mocking | Microsoft helpers or hand-rolled? | "If using Microsoft helpers is best practice then follow it." | §5.1 rewritten: Microsoft-first; hand-rolled fallback; Phase 0 researcher decides per criteria |
| Worktree execution | Spec-level FR or not? | Decided post-spec (after spec.md write). Not encoded in spec. | No FR; project-pipeline / worktree-setup handles |

## Assumptions

Proceeding with these assumptions (owner did not explicitly specify; flag if wrong before `/project-pipeline`):

- **Researcher subagent availability**: Assuming `.claude/agents/researcher.md` subagent is operational for Phase 0 FR-02 (Microsoft helper verdict). If unavailable, FR-02 falls back to a manual NuGet + Microsoft Learn check.
- **Sibling-project owner responsiveness**: Assuming Action Engine / Insights Phase 2 / Communications owners respond to coordination sync within 1 business day. If unresponsive, priority-order defaults to "active areas last" without sign-off.
- **Test infrastructure stability**: Assuming the 4,844 passing tests as of 2026-05-30 remain a valid baseline through Phase 0. If a major sibling change introduces regressions between baseline and Phase 1 start, Phase 0 re-baselines.
- **CI runner availability**: Assuming GitHub Actions runners are not rate-limited during the 16-27 day project window. If they are, full-suite 4-hour regression checks (FR-21) drop to 12-hour cadence.
- **No production-bug discoveries blocking the project**: Assuming the proportion of test failures that turn out to be real production bugs (FR-NFR-01 `real-bug-pending-fix` path) is ≤10% of touched failures. If higher, project pauses for owner review (signals the BFF has more latent bugs than expected, which is a different conversation).
- **Branch protection admin token**: Assuming the owner has admin access to flip `enforce_admins: true` (FR-09). If GitHub org policy restricts this, project pauses pending org-admin involvement.

## Unresolved Questions

Still need answers — flag if known, otherwise resolved in Phase 0 baseline:

- [ ] **Microsoft.Extensions.AI.Testing existence + maturity** — resolved by FR-02 researcher verdict in Phase 0. Blocks: Phase 1 P1.B (helper build) approach decision.
- [ ] **Owner-unavailability backup for emergency deploys** — if owner is offline and a true emergency deploy is needed, what's the fallback? Currently FR-11 is owner-only; assumption is owner has near-100% availability. Worth confirming in Phase 1 P1.D before publishing `docs/procedures/bff-deploy-emergency.md`. Blocks: nothing critical; can ship with "owner-only, no backup" and revisit if first emergency reveals the gap.
- [ ] **Sibling-coordination cadence** — daily sync per §4.7, but mechanism unspecified (Slack? GitHub issue? standing meeting?). Phase 0 FR-04 resolves when sibling owners sign off on priority order. Blocks: nothing; first sync establishes pattern.

---

*AI-optimized specification. Original design: [`design.md`](design.md) (2026-05-30 revision). Generated by `/design-to-spec` skill on 2026-05-31.*
