# Project Plan: CI/CD + Unit Test Remediation (r1)

> **Last Updated**: 2026-06-25
> **Status**: Ready for Tasks (pipeline-initialized)
> **Spec**: [spec.md](spec.md) | **Design**: [design.md](design.md)

---

## 1. Executive Summary

**Purpose**: Combine `ci-cd-github-enhancement` + `test-architecture-reset-r1` into one delivery across three streams (A: tiered CI, B: test reset, C: hot-path coordination) shipped in ~2 weeks elapsed with one ~4-hour cutover window. Registry redesign is OUT of scope.

**Scope**:
- Stream A: `ci-router.yml` + `ci-tier1-blocking.yml` + `ci-tier2-advisory.yml`; augment `nightly-health.yml` for Tier 3; retire `sdap-ci.yml`; new `scripts/validate-markdown-links.ps1`
- Stream B: rewrite `tests/CLAUDE.md` + `.claude/constraints/testing.md`; new ADR-038 (standalone testing strategy); new `docs/standards/TEST-ARCHITECTURE.md`; reorganize tests into 6 KEEP path conventions; sliced deletion of ~60% wiring tests
- Stream C: GitHub merge queue (batch=1); new `projects/INDEX.md`; auto-`conflict-check` on hot-path watchlist; `bff-extensions.md` Hot-Path Declaration section; root CLAUDE.md §8/§10/§17 updates; new `deploy-spaarke-ai.yml`

**Timeline**: ~28 elapsed days (Phase 1: 5d, Phase 2: 5d active + serial deletions, Phase 3: cutover + 7d soak + 14d sdap-ci buffer + 30d SC measurements) | **Estimated active dev**: ~11-12 days

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-028** (Spaarke Auth Architecture) — Tier 1 auth smoke aligns with this; affects `041-build-ci-tier1-blocking-yml.poml`
- **ADR-030** (BFF feature flags / kill switches) — path-aware dispatch interaction; affects `040-build-ci-router-yml.poml`
- **ADR-032** (Null-Object kill-switch pattern) — relevant if any test PR removes a conditional service during deletion sweep

**From Spec** (binding MUST/MUST NOT rules):
- MUST keep `sdap-ci.yml` running in parallel through Phase 2; retire only post-cutover after **14 days** of new-tier stability
- MUST enforce deletion-safety via path check at Step 9.5 (no CSV consultation at runtime)
- MUST keep `deploy-promote.yml`, `deploy-infrastructure.yml`, `deploy-office-addins.yml` unchanged
- MUST NOT restore `Release` matrix before Phase 2 deletion has merged AND surviving suite green ≥7 days
- MUST NOT add commit-marker skip mechanism to Tier 1 (FR-A05) — path-aware dispatch only
- MUST NOT introduce coverage-% targets to any new directive file (binding for ≥6 months)
- MUST NOT redesign BFF DI registry or SpaarkeAi widget/route registries (OUT of scope)

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| ADR-038 is STANDALONE (not a supersession) | ADR-022 is actually PCF Platform Libraries; spec misattributes; misattribution lives in constraints/testing.md line 25 | Task `024` drafts standalone ADR-038; task `022` fixes the misattribution |
| Skip pipeline Step 4 (feature-branch creation) | Already on `work/ci-cd-unit-test-remediation-r1` worktree branch | No `feature/*` branch; commits land on worktree branch |
| Drop design.md "escape hatches first" task | Spec FR-A05 forbids commit-marker skip mechanism | Path-aware dispatch in router is the only relief |
| INDEX.md scope = last-30-day-active worktrees | Spec's 5-6 active assumption matches recent-activity subset of 18 total | Task `030` uses `git for-each-ref --sort=-committerdate refs/heads/work/*` filtered to 30d |
| All test-modifying tasks = FULL rigor | Per spec FR-B07, overrides default STANDARD-rigor skip | Step 9.5 runs code-review + adr-check on all test PRs in this project |
| Sub-slice BFF.Api.Tests deletion into 3 by antipattern | 425 .cs files too big for one PR; antipattern grouping aids review | Tasks `053a` (HttpMessageHandler), `053b` (DI + null-checks), `053c` (remaining); revisable after `020` inventory |

### Discovered Resources

**Applicable Skills** (auto-discovered + extended by this project):
- `.claude/skills/task-execute/` — Steps 0.5 + 9.5 modified by Phase 2 Stream C
- `.claude/skills/project-pipeline/` — Steps 2 + 3 modified by Phase 2 Stream C
- `.claude/skills/conflict-check/` — hot-path watchlist + auto-trigger added by Phase 1 Stream C
- `.claude/skills/code-review/` — invoked at Step 9.5 for all test PRs (per FR-B07 rigor override)
- `.claude/skills/adr-check/` — invoked at Step 9.5 alongside code-review
- `.claude/skills/devops-project-register/` — invoked by pipeline at end of this run

**Knowledge / patterns**:
- `sdap-ci.yml` lines 591-619 — PR-comment dedup pattern (reuse verbatim for Tier 2)
- `nightly-health.yml` job-output → report-dependency pattern (Tier 3 jobs follow same shape)
- `deploy-bff-api.yml` structure (staging slot → /healthz → swap → rollback) — `deploy-spaarke-ai.yml` mirrors

**Reusable references**:
- `projects/github-actions-rationalization-r1/baseline/branch-protection-2026-06-01.json` — read-only reference for task `001`
- `tests/integration/Spe.Integration.Tests/` — integration test reference structure (already 27 files, xUnit 2.9.0 / Moq 4.20.70 / FluentAssertions 6.12.0)

---

## 3. Implementation Approach

### Phase Structure

```
Phase 0: Pre-flight (0.5d)
└─ Baseline build green, branch confirmed, baseline metrics snapshot

Phase 1: Diagnose + directive rewrites (Week 1, ~5d)
├─ Stream A: catalog sdap-ci failures, measure p50/p95, router-signal-model spike
├─ Stream B: test inventory CSV (transient), rewrite tests/CLAUDE.md + constraints/testing.md, draft TEST-ARCHITECTURE.md + ADR-038
└─ Stream C: build projects/INDEX.md (30d-active), update conflict-check skill watchlist

Phase 2: Build shadow + delete + directive enforcement (Week 2, ~5d active dev paced by serial deletions)
├─ Stream A: ship router/tier1/tier2 (shadow), augment nightly-health (Tier 3), ship deploy-spaarke-ai.yml + validate-markdown-links.ps1
├─ Stream B (SERIAL): path reorg → delete plugins → delete scheduling → delete BFF.Api (3 sub-slices)
└─ Stream C: task-execute Step 0.5/9.5, project-pipeline Step 2/3, bff-extensions Hot-Path, root CLAUDE.md §8/§10/§17

Phase 3: Cutover + monitor (~4h cutover + 7d soak + 14d sdap-ci buffer + 30d SC window)
└─ Branch protection flip → merge queue on → sdap-ci retire → Release matrix restore → SC measurements

Wrap-up (0.5d)
└─ README → Complete, lessons-learned.md, repo-cleanup
```

### Critical Path

**Blocking Dependencies**:
- Phase 2 Stream A workflow build (`040`) BLOCKED BY Phase 1 router-signal-model spike (`012`)
- Phase 2 Stream B deletions BLOCKED BY path reorg (`050`)
- Phase 2 Stream B deletions are STRICTLY SERIAL within stream (`050 → 051 → 052 → 053a → 053b → 053c`)
- Phase 3 cutover (`071`) BLOCKED BY all Phase 2 tasks complete
- Phase 3 sdap-ci retirement (`077`) GATED by cutover+14d (MUST rule)
- Phase 3 Release matrix restoration GATED by surviving suite green ≥7 days (`075`)

**Critical path** (revised 2026-06-26 twice — once for Phase 2.5 scope expansion, once for CI Router discovery): `000 → 022 → 060 → 050 → 053(collapsed) → 080 → 082 → 085 → 070 → 086 → 071 → 075 → 077 → 076 → 090` ≈ **~33-37 elapsed days** (was 28 pre-expansion; 32-35 post-expansion; 086 adds 1-2 days for fix + 2-push stability proof). **086 can run in parallel with 083-085** (touches `.github/workflows/` not `tests/`), so its calendar impact compresses if parallelized. **Hard gate**: 071 cutover requires BOTH 085 (deep cleanup) AND 086 (CI Router stability) green.

**High-Risk Items**:
- Tier 1 flake rate stays >1% after migration — Mitigation: shadow phase measures BEFORE cutover; if >1%, pause cutover and re-triage
- `deploy-bff-api.yml` audit reveals NOT master-triggered — Mitigation: budget `044` as fix-task not just audit (+0.5d slip risk)
- Sub-slicing distribution wildly skewed — Mitigation: `053a/b/c` boundaries are revisable after `020` inventory completes; task-create generates skeletons that operator can adjust

---

## 4. Phase Breakdown

### Phase 0: Pre-flight (0.5d)

**Objectives**:
1. Confirm branch state, clean tree, master sync, baseline build green

**Deliverables**:
- [ ] `000-preflight-baseline-build.poml` complete (optional if pipeline pre-flight covered it)

**Inputs**: working tree state
**Outputs**: confirmation note (no file artifact)

---

### Phase 1: Diagnose + directive rewrites (Week 1, ~5d)

**Objectives**:
1. Catalog current CI state (failures + baseline metrics)
2. Resolve router signal model (UQ #1 spike) to unblock Phase 2 Stream A
3. Rewrite all three coverage-culture directive files
4. Draft new ADR-038 (standalone) + new TEST-ARCHITECTURE.md
5. Produce transient test inventory CSV
6. Build `projects/INDEX.md` and extend `conflict-check` watchlist

**Deliverables**:
- [ ] `notes/baseline-metrics.md` with p50/p95 + failure catalog
- [ ] `notes/test-inventory.csv` (transient — used only in Phase 2 deletion planning)
- [ ] `tests/CLAUDE.md` rewritten (drops 80/70/90%, `<1s/test`, mock-first AAA; bans `Mock<HttpMessageHandler>`)
- [ ] `.claude/constraints/testing.md` rewritten (drops 80% coverage MUST, mock-all-I/O MUST, `<100ms/test` MUST; adds 6 KEEP path categories as MUST rules; fixes line 25 ADR-022 misattribution)
- [ ] `docs/standards/TEST-ARCHITECTURE.md` new
- [ ] `docs/adr/ADR-038-testing-strategy.md` new (standalone)
- [ ] `projects/INDEX.md` new (5-6 active worktrees, hot-path declarations)
- [ ] `.claude/skills/conflict-check/SKILL.md` extended with hot-path watchlist
- [ ] Router signal model decision documented in `notes/router-signal-model-decision.md`

**Critical Tasks**:
- `012-router-signal-model-spike.poml` — MUST complete before Phase 2 `040`
- `022-rewrite-constraints-testing-md.poml` — directive rewrite (FULL rigor); load-bearing for Stream B

**Parallel Group PG-1**: 010, 011, 012, 020, 021, 022, 023, 024, 030, 031 — all 10 are parallel-safe; max concurrency 6 per wave; task `031` modifies `.claude/skills/conflict-check/SKILL.md` so must run in main session (not via sub-agent).

**Inputs**: spec.md, design.md, existing CI/test files
**Outputs**: directive files rewritten, ADR-038 + TEST-ARCHITECTURE.md drafted, INDEX.md live, baseline metrics captured

**Dev impact**: ZERO (all additive/docs-only)

---

### Phase 2: Build shadow + delete + directive enforcement (Week 2, ~5d active)

**Objectives**:
1. Ship shadow workflows (router, tier1, tier2) parallel to `sdap-ci.yml`
2. Augment `nightly-health.yml` for Tier 3 (full integration + coverage observation + Trivy + dep audit)
3. Ship `deploy-spaarke-ai.yml` (mirror of `deploy-bff-api.yml`) + audit `deploy-bff-api.yml` master-trigger
4. Reorganize tests into 6 KEEP path conventions
5. Execute serial deletion PRs (Plugins → Scheduling → BFF.Api 3 sub-slices)
6. Update all skill directives (task-execute, project-pipeline, conflict-check finished, bff-extensions, root CLAUDE.md)

**Deliverables**:
- [ ] `.github/workflows/ci-router.yml` + `ci-tier1-blocking.yml` + `ci-tier2-advisory.yml` in shadow
- [ ] `.github/workflows/nightly-health.yml` augmented (4 new jobs hooked into report dependency)
- [ ] `.github/workflows/deploy-spaarke-ai.yml` new
- [ ] `scripts/validate-markdown-links.ps1` new
- [ ] Tests under 6 KEEP paths (`auth/`, `regression/`, `data-mutation/`, `tenant/`, `contract/`, `unit/domain/`)
- [ ] `Spaarke.Plugins.Tests`, `Spaarke.Scheduling.Tests`, `Sprk.Bff.Api.Tests` DELETE category removed via separate PRs
- [ ] `.claude/skills/task-execute/SKILL.md` Step 0.5 + Step 9.5 updated
- [ ] `.claude/skills/project-pipeline/SKILL.md` Step 2 + Step 3 updated
- [ ] `.claude/constraints/bff-extensions.md` Hot-Path Declaration section added
- [ ] Root `CLAUDE.md` §8 (rigor table), §10 (BFF Hygiene), §17 (pointers) updated

**Critical Tasks**:
- `050-fr-b05-path-reorganization.poml` — MUST complete before any deletion (051, 052, 053*)
- `053c-delete-bff-remaining-by-directory.poml` — last sub-slice; blocks `070` cutover

**Parallel Groups**:
- **PG-2** (workflows): 040, 041, 042, 043, 044 — all independent files
- **PG-3** (skills): 060, 061, 062 — independent skill files; main-session-only (`.claude/` write boundary), execute sequentially in PG-3 ordering
- **SERIAL-DEL**: 050 → 051 → 052 → 053a → 053b → 053c — strict serial; each PR rebases on master

PG-2, PG-3, and SERIAL-DEL run **concurrently across streams** (no file overlap between streams).

**Dev impact**: Soft constraint on test additions — should follow new policy from Phase 1 directive rewrites.

---

### Phase 2.5: Build-vs-maintain codification + retroactive deep cleanup (added 2026-06-26)

**Objectives** (per spec FR-B08/B09/B10 — owner-directed scope expansion):

1. Codify build-vs-maintain criteria as a binding standard (Deliverable A, FR-B08) — extend ADR-038 + `.claude/constraints/testing.md` + `tests/CLAUDE.md` with ≥10 new scaffolding-test bans beyond the current 3
2. Build a project-close test-diet workflow (Deliverable B, FR-B09) — new `/test-diet` skill wired into `/repo-cleanup` and `090-wrapup-*` task
3. Execute retroactive deep cleanup (Deliverable C, FR-B10) — re-inventory with broader criteria, then sliced deletion PRs to reduce BFF unit tests from ~6,700 to ≤3,500

**Why this phase was added mid-project**:

The original Phase 2 Stream B deletion (task 053) removed only 9 files (179 tests, 2.4% reduction) because the inventory used strict signature-match criteria ("doubt = KEEP" per spec §38). Post-execution review identified that the spec's "we have ~7,900 tests, ~60% are wiring" intuition was directionally correct but the strict-criteria inventory couldn't validate it file-by-file. The owner's "build-vs-maintain" reframing (2026-06-26) provides the judgment-based criteria needed to do a deeper cleanup. Without this phase, the cutover (071) ships an architectural change but the test suite remains over-engineered by ~3,000-4,000 unit tests.

**Deliverables**:

- [ ] `docs/adr/ADR-038-testing-strategy.md` extended (or new sibling ADR) with §"Build-vs-Maintain Criteria" listing 10+ scaffolding signatures, each with concrete C# example + rationale
- [ ] `.claude/constraints/testing.md` MUST NOT rules extended with the new ban list
- [ ] `tests/CLAUDE.md` Banned Antipatterns section extended; new "expect to defend at project close" framing added
- [ ] `.claude/skills/test-diet/SKILL.md` new skill (or extension to `/repo-cleanup`) — invokable as `/test-diet`
- [ ] `task-execute` skill 090 wrap-up step modified to require `/test-diet` invocation
- [ ] `projects/ci-cd-unit-test-remediation-r1/notes/test-inventory-broader.csv` — re-inventory with new criteria
- [ ] `projects/ci-cd-unit-test-remediation-r1/notes/test-inventory-broader-summary.md` — bucket sizes + slicing recommendation
- [ ] 3-5 sliced DELETE PRs reducing BFF unit test count from ~6,700 to ≤3,500
- [ ] Final `dotnet build` + `dotnet test` verification on surviving suite

**Critical Tasks (strict serial within Phase 2.5)**:

- `080-codify-build-vs-maintain-criteria.poml` — MUST complete before 082 inventory (criteria drives the classifier)
- `081-build-test-diet-skill.poml` — can run in parallel with 080; gates 090 wrap-up update
- `082-rerun-inventory-broader-criteria.poml` — depends on 080; gates deletion PRs
- `083` / `084` / `085` — strict serial; each rebases on master after prior merges; final 085 unblocks 070→071 cutover
- **`086-fix-ci-router-startup-failure.poml`** (added 2026-06-26 after parallel-session discovery) — independent of 082-085 chain (touches workflow YAML, not test .cs); BLOCKS 071. Awaits user-supplied browser error message OR explicit bisect approval before execution.

**Parallel Groups**:

- **PG-4 (Phase 2.5 codification, parallel)**: 080 + 081 (different file domains; 080 = constraint/ADR/tests/CLAUDE; 081 = new skill SKILL.md). Both modify `.claude/` — main-session sequential per write boundary.
- **PG-5 (Phase 2.5 deletion, STRICT SERIAL)**: 082 → 083 → 084 → 085 → unblocks 070
- **PG-6 (Phase 2.5 CI remediation, parallel with PG-5)**: 086 runs in main session concurrently with PG-5 chain. Different file domain (`.github/workflows/` vs `tests/`); no collision. 086 + 085 both must complete before 071 fires.

**Calendar impact**: adds ~3-5 elapsed days of active work; +1-2 days if 086 must serialize (parallel saves the days). Phase 3 cutover (071) shifts ~1 week. Total project: ~3-3.5 weeks elapsed (vs spec's original ~2-week framing).

**Rigor levels**: All Phase 2.5 tasks are FULL rigor per spec FR-B07 (test-modifying override). Code-review + adr-check unconditional at Step 9.5.

---

### Phase 3: Cutover + monitor (~4h cutover + 7d soak + 14d sdap-ci buffer + 30d SC window)

**Objectives**:
1. Snapshot pre-cutover branch protection state
2. Execute cutover (restore Release matrix, flip branch protection, enable merge queue)
3. Verify 7-day surviving-suite green soak before Release-matrix lock-in (SC-06)
4. Retire `sdap-ci.yml` at cutover+14d minimum (MUST rule)
5. Measure 30-day success criteria

**Deliverables**:
- [ ] `notes/branch-protection-pre-cutover.json` fresh snapshot
- [ ] Branch protection on master requires only `CI / Router`
- [ ] GitHub merge queue enabled (batch=1, no speculative, timeout 30min)
- [ ] `sdap-ci.yml` deleted after 14d stability
- [ ] `Release` matrix restored to `Build & Test` after 7d green soak
- [ ] `notes/sc-measurements-30day.md` with SC-01..SC-10 results

**Critical Tasks (SERIAL-CUTOVER)**: 070 → 071 → 075 → 077 → 076

**Dev impact**: ~4h merge slowdown on cutover day; otherwise normal velocity.

---

### Wrap-up (0.5d)

**Deliverables**:
- [ ] README.md status → Complete
- [ ] `notes/lessons-learned.md` written
- [ ] `/repo-cleanup` skill invoked

---

## 5. Hot-Path Declaration (dogfooding the rule introduced by this project)

```xml
<hot-path-declaration>
  <bff-api>NO — no production code in src/server/api/Sprk.Bff.Api/** modified by this project</bff-api>
  <spaarke-ai>NO production code modified; YES adds .github/workflows/deploy-spaarke-ai.yml as new CD plumbing for src/solutions/SpaarkeAi/</spaarke-ai>
  <ci-workflows>YES — adds router/tier1/tier2, augments nightly-health, retires sdap-ci, flips branch protection. Highest-impact hot-path category for this project.</ci-workflows>
  <skill-directives>YES — modifies task-execute, project-pipeline, conflict-check SKILL.md. Coordination required with any other in-flight project modifying same skills.</skill-directives>
  <root-CLAUDE-md>YES — §8 (rigor table), §10 (BFF Hygiene), §17 (pointers). Single-file edit, coordinated via this project's worktree.</root-CLAUDE-md>
</hot-path-declaration>
```

---

## 6. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| GitHub merge queue + branch-protection feature | GA | Low | UQ #1 spike (`012`) validates required-check semantics with neutral conclusions before cutover |
| GitHub Actions analytics (SC measurements) | GA | Low | 30-day rolling window; reports compiled in `076` |
| Azure App Service deployment slots (for `deploy-spaarke-ai.yml` mirror) | GA | Low | `deploy-bff-api.yml` pattern proven |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| `sdap-ci.yml` PR-comment dedup pattern | `.github/workflows/sdap-ci.yml` lines 591-619 | Stable; reused in `ci-tier2-advisory.yml` |
| `nightly-health.yml` augmentation slots | `.github/workflows/nightly-health.yml` | Stable; 4 new jobs hook into report dependency |
| `deploy-bff-api.yml` master-trigger pattern | `.github/workflows/deploy-bff-api.yml` | Confirmed master-triggered; `deploy-spaarke-ai.yml` mirrors |
| `projects/github-actions-rationalization-r1/baseline/branch-protection-2026-06-01.json` | as path | Reference for task `001` |
| Existing 3 deletion-target test projects | `tests/unit/{Spaarke.Plugins,Spaarke.Scheduling,Sprk.Bff.Api}.Tests/` | Live; sliced deletion in tasks 051/052/053a/b/c |

---

## 7. Testing Strategy

**This project IS the testing strategy reset.** The new policy (drafted in this project itself) is:

- **Integration-heavy pyramid**: tests under `tests/integration/{auth,regression,data-mutation,tenant,contract}/**` are the 5 integration KEEP categories
- **Modest unit tests**: only `tests/unit/domain/**` is the unit KEEP category (domain logic)
- **No coverage % targets**: coverage tracked for observation, never gating
- **Mock at module boundaries**: ban `Mock<HttpMessageHandler>`, `Mock<IServiceClient>`, DI-registration tests, constructor null-checks
- **Path conventions as MUST rule**: deletion-safety enforced at code-review (Step 9.5) by path check, not CSV consultation

**For this project's own tasks**: each test-modifying task is FULL rigor. The path reorganization task (`050`) is mechanical (git-mv); deletion tasks (`051`, `052`, `053a/b/c`) MUST run `dotnet test` after each PR to confirm no behavioral regression in surviving tests.

---

## 8. Acceptance Criteria

### Technical Acceptance

**Phase 1**:
- [ ] `tests/CLAUDE.md` no longer mentions 80%, 70%, 90%, or `<1s/test`
- [ ] `.claude/constraints/testing.md` no longer has "minimum 80% line coverage" MUST rule; line 25 ADR-022 misattribution fixed
- [ ] `docs/standards/TEST-ARCHITECTURE.md` exists with 6 KEEP path conventions documented
- [ ] `docs/adr/ADR-038-testing-strategy.md` exists; `docs/adr/INDEX.md` lists ADR-038 (status: Active)
- [ ] `projects/INDEX.md` exists with 5-6 active worktrees + hot-path declarations
- [ ] `.claude/skills/conflict-check/SKILL.md` contains hot-path watchlist (`src/server/api/Sprk.Bff.Api/**`, `src/solutions/SpaarkeAi/**`) + auto-trigger criteria
- [ ] `notes/baseline-metrics.md` documents current sdap-ci p50/p95
- [ ] `notes/router-signal-model-decision.md` documents UQ #1 resolution

**Phase 2**:
- [ ] `gh workflow list` shows `ci-router`, `ci-tier1-blocking`, `ci-tier2-advisory` in shadow
- [ ] `nightly-health.yml` includes 4 new Tier 3 jobs (full integration, coverage observation, Trivy, dep audit)
- [ ] `deploy-spaarke-ai.yml` exists; `deploy-bff-api.yml` master-trigger audit documented
- [ ] `scripts/validate-markdown-links.ps1` exists and runs
- [ ] `dotnet test --list-tests` shows tests under all 6 KEEP paths
- [ ] `git log --diff-filter=D --name-only` shows 5+ sliced deletion PRs across Plugins/Scheduling/BFF.Api
- [ ] `task-execute` Step 0.5 invokes `conflict-check` on hot-path watchlist match
- [ ] `task-execute` Step 9.5 runs code-review for all test-modifying tasks (override of STANDARD)
- [ ] `project-pipeline` Step 2 reads INDEX.md; Step 3 requires `<hot-path-declaration>` for BFF/SpaarkeAi projects
- [ ] `bff-extensions.md` Hot-Path Declaration section live
- [ ] Root `CLAUDE.md` §8 rigor table updated, §10 BFF Hygiene includes Hot-Path Declaration reference, §17 pointers include ADR-038 + TEST-ARCHITECTURE.md + INDEX.md

**Phase 3**:
- [ ] `notes/branch-protection-pre-cutover.json` snapshot taken
- [ ] `gh api repos/spaarke-dev/spaarke/branches/master/protection` shows only `CI / Router` as required check
- [ ] Merge queue enabled (batch=1, no speculative, 30min timeout)
- [ ] Surviving suite green ≥7 consecutive days → Release matrix restored to `Build & Test`
- [ ] `sdap-ci.yml` deleted at cutover+14d minimum
- [ ] `notes/sc-measurements-30day.md` shows all SC-01..SC-10 measurements taken; thresholds met (or escalation logged)

### Business Acceptance

- [ ] PR-to-merge time decreases ≥ 30% vs pre-cutover baseline
- [ ] Hot-path collision incident rate drops ≥ 50% over 30 days vs prior 30 days
- [ ] Master green rate ≥ 95% over 30 days post-cutover
- [ ] No rollback triggered within first 14 days (judged by single dev; tracked in `notes/cutover-attempts.md` if rollback occurs)

---

## 9. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|------------|---------|------------|
| R-1 | Real regression slips through deletion window | Med | Med | Keep all integration/auth/security/regression tests; Debug matrix still runs even with Release temporarily off |
| R-2 | Recreate-on-touch forcing function fails ("we'll add regression test later") | High | High | Root CLAUDE.md §8 forces FULL rigor on test PRs for ≥6 months; PR template adds "Does this fix a bug? Where's the regression test?" question; Step 9.5 checks |
| R-3 | Tier 1 still flaky after migration (>1% flake) | Med | High | Tier 1 contains only build + arch + auth smoke in shadow phase; measured BEFORE flipping branch protection; if flake >1%, pause cutover and re-triage |
| R-4 | Coverage targets reintroduced by future project | Med | Med | ADR-038 explicitly bans gating on coverage; root CLAUDE.md §17 pointer reinforces |
| R-5 | Sliced deletion PRs conflict with in-flight feature PRs | Med | Med | INDEX.md is source of truth; coordinate slice sequence with hot-path declarations |
| R-6 | Merge queue surprises (queue stalling, neutral-check semantics) | Med | Low | Enable in shadow first via `012` spike; document behavior in `docs/procedures/ci-cd-workflow.md`; rollback flips merge queue off in <1min |
| R-7 | Hot-path declarations in INDEX.md go stale | High | Med | `project-pipeline` updates at project start; `task-execute` updates on hot-path changes |
| R-8 | Stream A lands without Stream B ready (or vice versa) | Low | High | This project binds them — cutover gated on Stream B Phase 2 deletion landing AND Tier 1 measured stable |
| R-9 | `deploy-bff-api.yml` audit reveals NOT master-triggered | Low | Med | Budget `044` as fix-task if needed (+0.5d slip) |
| R-10 | `Sprk.Bff.Api.Tests` deletion distribution wildly skewed | Med | Med | `053a/b/c` boundaries revisable after `020` inventory; task-create generates skeletons that operator adjusts |

---

## 10. Next Steps

1. **Pipeline complete** — README.md, plan.md, CLAUDE.md, current-task.md, tasks/*.poml, TASK-INDEX.md generated
2. **`/devops-project-register`** runs at end of this pipeline invocation to register project on portfolio board
3. **First implementation task**: invoke `task-execute 000-preflight-baseline-build.poml` (optional) or jump to Phase 1 PG-1 (parallel)
4. **PG-1 wave 1 (max 6)**: `task-execute 010, 020, 030, 011, 023, 024` in parallel
5. **PG-1 wave 2 (sequential in main session due to `.claude/` boundary)**: 031, then `task-execute 012, 021, 022` in parallel

---

**Status**: Ready for Tasks (pipeline-initialized)
**Next Action**: Start Phase 1 PG-1 via `task-execute` invocations.

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks. The hot-path declaration block at §5 is binding for this project — coordinate any merging with active worktrees that touch the same `.claude/` skill files (per `projects/INDEX.md` once it lands).*
