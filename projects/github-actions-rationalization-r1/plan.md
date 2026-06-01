# Project Plan: GitHub Actions Rationalization R1

> **Last Updated**: 2026-06-01
> **Status**: Ready for Tasks
> **Spec**: [spec.md](spec.md)
> **Design**: [design.md](design.md)

---

## 1. Executive Summary

**Purpose**: Rationalize the repository's 13 GitHub Actions workflows so every workflow that exists works AND provides measurable value, add `actionlint` as a required pre-merge check so workflow bugs can't ship silently, and set up failure observability so CI failures cannot silently accumulate.

**Scope**:
- Audit all 13 workflows; produce inventory with last-30-day success rates
- Fix `deploy-promote.yml` cascade, `deploy-infrastructure.yml` triggers, `nightly-quality.yml` schedule
- Investigate post-PR-#314 master CI failure (run 26755019759)
- Delete or consolidate 7 untested workflows → ≤8 workflows remain
- Add `.github/workflows/workflows-validate.yml` with `actionlint`; required-status-check on master
- Author `.github/WORKFLOWS.md` + `docs/procedures/workflow-incident-response.md`
- Add `.github/workflows/report-workflow-health.yml` (weekly health report)
- Verify branch-protection gate end-to-end via deliberate-fail PR

**Timeline**: 6–9 calendar days | **Estimated Effort**: 28–42 hours

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (limited applicability — this project is DevOps/CI tooling, not application architecture):
- **ADR-029** (BFF Publish Hygiene): downstream of CI; `deploy-bff-api.yml` audit must preserve hash-verify + slot-swap rollback patterns
- **ADR-001** (Minimal API + Workers): N/A for workflow files but referenced by `deploy-bff-api.yml`'s deploy target — preserve it
- **ADR-027** (Subscription isolation & managed solutions): governance context for `deploy-*` workflows; environment-separated subscriptions
- Other ADRs are N/A — this project does not touch `src/` code

**From Spec** (binding NFRs):
- **NFR-01**: No production code changes (`src/`, `power-platform/`, `infra/`, `scripts/`)
- **NFR-02**: Each workflow file edit <50% line replacement; >50% requires escalation OR delete-and-rewrite with explicit decision record
- **NFR-03**: `enforce_admins` only disabled inside merge-window of a specific actionlint-fix PR; each disable logged in `decisions/`
- **NFR-04**: All workflow deletions via `git rm` + commit; NOT comment-out-and-leave
- **NFR-05**: `actionlint` check added to required-status-checks BEFORE Phase 5's deliberate-fail verification
- **NFR-07**: Task POML metadata declares `<repair-not-rewrite>true</repair-not-rewrite>` (inherited predecessor pattern)
- **NFR-08**: Project CLAUDE.md loaded by every task agent

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| `actionlint` via `rhysd/actionlint@v1` Action | Pinned to latest major; auto-updates within v1; widely-used canonical validator | FR-07 implementation; D-01 |
| Weekly health-report cadence (Mondays 09:00 UTC) | Aggregate triage > real-time alerts; D-02 | FR-11 cron expression |
| GitHub-issue based reporting (no Slack/Teams bridge) | Owner already monitors issues; minimizes infra | FR-11 design |
| Delete-by-default for never-used workflows | Burden of proof on retention; soft-deletes accumulate | D-03; Phase 2 dispositions |

### Discovered Resources

**Applicable Skills** (auto-discovered for task execution):
- `.claude/skills/ci-cd/` — GitHub Actions CI/CD pipeline status & workflow management
- `.claude/skills/adr-check/` — ADR compliance validation (light load on this project)
- `.claude/skills/code-review/` — Quality gate at task-execute Step 9.5 (FULL rigor tasks)
- `.claude/skills/docs-procedures/` — Author `docs/procedures/workflow-incident-response.md`
- `.claude/skills/docs-guide/` — Author `.github/WORKFLOWS.md` and supporting guides
- `.claude/skills/push-to-github/` — Per-task commits during execution
- `.claude/skills/repo-cleanup/` — End-of-project hygiene
- `.claude/skills/merge-to-master/` — Final merge of completed branch

**Knowledge Articles** (load per task):
- [`docs/procedures/ci-cd-workflow.md`](../../docs/procedures/ci-cd-workflow.md) — Existing CI/CD pipeline guide; extend with `workflow-validation` and `branch-protection` sections
- [`docs/guides/INCIDENT-RESPONSE.md`](../../docs/guides/INCIDENT-RESPONSE.md) — Template for `workflow-incident-response.md`
- [`docs/guides/GITHUB-ENVIRONMENT-PROTECTION.md`](../../docs/guides/GITHUB-ENVIRONMENT-PROTECTION.md) — Branch-protection setup reference
- [`docs/guides/MONITORING-AND-ALERTING-GUIDE.md`](../../docs/guides/MONITORING-AND-ALERTING-GUIDE.md) — Observability patterns
- [`.claude/constraints/INDEX.md`](../../.claude/constraints/INDEX.md) — Constraint loading strategy
- [`docs/adr/ADR-029-bff-publish-hygiene.md`](../../docs/adr/ADR-029-bff-publish-hygiene.md) — For `deploy-bff-api.yml` audit (Phase 2)

**Reusable Code & Examples**:
- [`projects/sdap-bff-api-remediation-fix/inventory/ci-workflow-inventory.md`](../sdap-bff-api-remediation-fix/inventory/ci-workflow-inventory.md) — **Gold-standard workflow audit format**; reuse structure for Phase 0
- [`projects/code-quality-and-assurance-r1/`](../code-quality-and-assurance-r1/) — Patterns for nightly/weekly quality workflows, graduated enforcement, pre-commit integration
- [`projects/code-quality-and-assurance-r1/notes/lessons-learned.md`](../code-quality-and-assurance-r1/notes/lessons-learned.md) — Lessons on actionlint integration and enforcement
- [`projects/sdap-bff.api-test-suite-repair/`](../sdap-bff.api-test-suite-repair/) — **Predecessor project**; surfaced these symptoms; this project addresses root causes. Reuse: `decisions/`, `ledgers/`, `baseline/`, phase-gap task numbering, `<repair-not-rewrite>true</repair-not-rewrite>` POML pattern

---

## 3. Implementation Approach

### Phase Structure

```
Phase 0: Inventory + Baseline (~4 hrs / 1 day)
└─ Audit 13 workflows; capture success rates + triggers + value statements
└─ Investigate master CI failure (run 26755019759)
└─ Snapshot branch-protection config

Phase 1: Fix broken workflows (~8-12 hrs / 2-3 days)
└─ P2 deploy-promote.yml cascade — workflow_run filter
└─ P3 deploy-infrastructure.yml ghost triggers — strict YAML review
└─ P4 nightly-quality.yml schedule — root-cause failure

Phase 2: Rationalization (~6-8 hrs / 1-2 days)
└─ Audit each P5 untested workflow → keep / delete / consolidate
└─ Workflow count: 13 → ≤8

Phase 3: Prevention (actionlint pre-merge) (~4-6 hrs / 1 day)
└─ Add workflows-validate.yml with actionlint
└─ Add to required-status-checks on master
└─ Smoke-test with deliberate duplicate-key PR

Phase 4: Observability + Docs (~6-8 hrs / 1-2 days)
└─ Add report-workflow-health.yml (weekly)
└─ Author .github/WORKFLOWS.md
└─ Author docs/procedures/workflow-incident-response.md
└─ Document notification-routing steps

Phase 5: Validate the gate
└─ Deliberate-fail PR; confirm branch protection + actionlint both block
```

### Critical Path

**Blocking Dependencies (real, not artificial)**:
- Phase 1 fix tasks (P2/P3/P4) each depend on Phase 0 inventory line for that specific workflow (need triggers + recent-runs data)
- Phase 2 consolidation/delete decisions depend on Phase 0 audit data
- Phase 3 required-status-check (FR-08) depends on Phase 3 actionlint workflow merged
- Phase 5 deliberate-fail PR depends on Phase 3 complete (NFR-05)
- FR-14 (rolling 7-day ≥90%) depends on FR-11 (weekly report) running at least once

**No artificial Phase 0 → Phase 1 gate**. Per project decision (2026-06-01): Phase 1 fix tasks can start as soon as their specific workflow's inventory line is captured. Phase 0 is not a wholesale blocker; only the inventory entry for a given workflow is.

**Parallelizable groups**:
- All Phase 1 fixes (010, 011, 012) — different workflow files → parallel-safe
- All Phase 2 untested-workflow dispositions — different workflow files → parallel-safe
- Phase 4 docs (041, 042, 043) — different doc files → parallel-safe

**High-Risk Items**:
- **R1** — Phase 0 confirms `sdap-ci.yml` still broken on master post-PR-#314 → project-blocker; pause Phase 1 until resolved. Mitigation: explicit task 002 to investigate run 26755019759.
- **R2** — First `workflows-validate.yml` PR can't satisfy its own required-check. Mitigation: brief `enforce_admins` bypass (predecessor pattern); logged in `decisions/D-0X`.
- **R3** — `actionlint` rejects legitimate workflows. Mitigation: smoke-pass before adding to required checks.

---

## 4. Phase Breakdown

### Phase 0: Inventory + Baseline (~4 hrs)

**Objectives:**
1. Capture last-30-day success rate, triggers, declared purpose, value statement, and recent-commits owner for each of the 13 workflows
2. Investigate post-PR-#314 master CI failure (run 26755019759); root-cause OR document for deferral

**Deliverables:**
- [ ] `baseline/workflow-inventory-2026-06-01.md` — one entry per workflow, gold-standard format from predecessor project's inventory
- [ ] `baseline/branch-protection-2026-06-01.json` — snapshot of current branch-protection config (`gh api repos/.../branches/master/protection`)
- [ ] `decisions/D-01-master-ci-failure-disposition.md` — root cause OR deferral rationale for run 26755019759

**Critical Tasks:**
- `001-workflow-inventory-and-baseline.poml` — Comprehensive audit; FIRST task (no prerequisites)
- `002-master-ci-root-cause.poml` — Investigate run 26755019759; can run in parallel with `001`

**Inputs**: `.github/workflows/*.yml`, `gh run list` access
**Outputs**: `baseline/`, initial `decisions/`

### Phase 1: Fix broken workflows (~8–12 hrs)

**Objectives:**
1. Fix P2 `deploy-promote.yml` cascade (workflow_run trigger emitting failure even after job-level success filter)
2. Investigate + fix P3 `deploy-infrastructure.yml` ghost triggers (runs on non-matching branches)
3. Root-cause + fix P4 `nightly-quality.yml` schedule failures

**Deliverables:**
- [ ] `deploy-promote.yml`: Either remove `workflow_run` trigger OR add workflow-level `if:` filter for SDAP CI success
- [ ] `deploy-infrastructure.yml`: Strict YAML review; fix trigger anomaly (likely loader-failure pattern from P1)
- [ ] `nightly-quality.yml`: Root-cause failure (loader-broken vs real test failure); fix OR delete with rationale

**Critical Tasks** (parallel-safe — different workflow files):
- `010-fix-deploy-promote-cascade.poml`
- `011-fix-deploy-infrastructure-triggers.poml`
- `012-fix-nightly-quality-schedule.poml`

**Exit gate**: Each of P2/P3/P4 runs to a real conclusion (not 0s loader-failure) OR is deleted with rationale in commit message.

**Inputs**: Phase 0 inventory line per workflow (not all of Phase 0)
**Outputs**: Updated `.github/workflows/{deploy-promote,deploy-infrastructure,nightly-quality}.yml`

### Phase 2: Rationalization (~6–8 hrs)

**Objectives:**
1. Audit each P5 untested workflow: search 90-day history for usage; make keep/delete decision per D-03
2. Consolidate overlapping workflows per D-04
3. Drop total workflow count to ≤8

**Deliverables:**
- [ ] Per-workflow disposition decision (`decisions/D-0X-workflow-disposition-{name}.md`)
- [ ] `ledgers/workflow-disposition-ledger.md` — single rollup of all keep/delete/consolidate decisions
- [ ] Workflow deletions via `git rm` + commit with rationale (NFR-04)

**Critical Tasks:**
- `020-audit-untested-workflows.poml` — Audit each P5 candidate; produce disposition recommendation
- `021..N-disposition-{workflow-name}.poml` — Execute the keep/delete/consolidate decision for each workflow (parallel-safe — different files)

**Exit gate**: Workflow count ≤8, each with documented purpose.

**Inputs**: Phase 0 inventory, Phase 1 fix-state
**Outputs**: Updated `.github/workflows/` (fewer files); `decisions/`; `ledgers/`

### Phase 3: Prevention (~4–6 hrs)

**Objectives:**
1. Add `actionlint` as a pre-merge validator on PRs touching `.github/workflows/**`
2. Add `actionlint` to required-status-checks on master branch protection
3. Smoke-test with deliberate-fail PR

**Deliverables:**
- [ ] `.github/workflows/workflows-validate.yml` — actionlint on every `.github/workflows/**` PR
- [ ] Branch-protection updated: required-status-checks list includes `actionlint` (4 contexts total)
- [ ] Deliberate-fail smoke-test PR opened, blocked by `actionlint`, closed without merge; evidence in `baseline/`

**Critical Tasks** (sequential):
- `030-add-actionlint-workflow.poml`
- `031-configure-required-status-check.poml`
- `032-deliberate-fail-pr-smoke-test.poml`

**Exit gate**: actionlint check is required + verified blocking on a deliberate-fail PR (NFR-05).

**Inputs**: Phase 1 + Phase 2 complete
**Outputs**: `.github/workflows/workflows-validate.yml`, updated branch-protection config

### Phase 4: Observability + Docs (~6–8 hrs)

**Objectives:**
1. Add weekly workflow-health-report workflow (D-02)
2. Author `.github/WORKFLOWS.md` per-workflow purpose, owner, SLA, trigger model, common failure modes
3. Author `docs/procedures/workflow-incident-response.md` runbook
4. Document GitHub notification routing setup (owner action)

**Deliverables:**
- [ ] `.github/workflows/report-workflow-health.yml` — weekly cron (Mondays 09:00 UTC); creates/updates "CI Health Report" issue
- [ ] `.github/WORKFLOWS.md` — per-workflow doc table
- [ ] `docs/procedures/workflow-incident-response.md` — incident runbook
- [ ] Notification-routing documentation embedded in `.github/WORKFLOWS.md`

**Critical Tasks** (041–043 parallel-safe — different doc files):
- `040-add-weekly-health-report-workflow.poml`
- `041-author-workflows-md.poml`
- `042-author-workflow-incident-response.poml`
- `043-document-notification-routing.poml`

**Exit gate**: Weekly report runs once successfully; issue exists with at least one snapshot; docs reviewed.

**Inputs**: Phase 2 final workflow set; Phase 3 actionlint in place
**Outputs**: New `.github/workflows/report-workflow-health.yml`; new `.github/WORKFLOWS.md`; new `docs/procedures/workflow-incident-response.md`

### Phase 5: Validate the Gate

**Objectives:**
1. Open a deliberate-fail PR similar to predecessor's task 023
2. Confirm branch protection blocks merge (Build & Test failing) AND `actionlint` blocks workflow changes
3. Document evidence

**Deliverables:**
- [ ] Deliberate-fail PR opened, blocked by 2+ required checks, closed without merge
- [ ] Evidence captured in `baseline/branch-protection-verification-{date}.md`

**Critical Tasks:**
- `050-verify-branch-protection-gate-end-to-end.poml`

**Exit gate**: branch protection + actionlint both verified blocking.

### Wrap-up

- `090-project-wrap-up.poml` — Update README status to Complete; create `notes/lessons-learned.md`; archive project artifacts; final repo-cleanup.

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| GitHub Actions service | GA | Low | Standard external risk; no mitigation |
| `gh` CLI on local + runner | Available | Low | Pre-installed on `ubuntu-latest`; locally on dev machines |
| `rhysd/actionlint@v1` Action | GA | Low | Pinned to major v1; auto-update within major |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| Predecessor PR #314 merged on master | git | ✅ Done 2026-06-01 |
| `enforce_admins: true` restored | branch-protection | ✅ Done 2026-06-01 |
| `docs/procedures/ci-cd-workflow.md` | repo | Production (extend in Phase 4) |
| `docs/guides/GITHUB-ENVIRONMENT-PROTECTION.md` | repo | Production (reference in Phase 3/4) |
| Predecessor project artifacts | `projects/sdap-bff.api-test-suite-repair/` | Closed; reuse patterns |

---

## 6. Testing Strategy

This project does not modify application code, so traditional unit/integration tests don't apply. Verification is workflow-execution based:

**Workflow run validation**:
- Each fixed workflow (P2/P3/P4) must run cleanly on its declared triggers ≥1 time post-fix
- `nightly-quality.yml` requires 3 consecutive successful nightly runs (FR-05) OR documented deletion
- `actionlint` workflow must fire on every `.github/workflows/**`-touching PR

**Smoke testing**:
- Deliberate-fail PR (FR-13): introduce duplicate-YAML-key, confirm `actionlint` blocks merge
- Phase 5 verification PR: confirm branch protection blocks merge end-to-end

**Acceptance verification**:
- `gh api repos/.../branches/master/protection/required_status_checks` returns 4 contexts (FR-08)
- `ls .github/workflows/ | wc -l` returns ≤8 (FR-06)
- Weekly health-report issue exists with ≥1 snapshot (FR-11, FR-14)

---

## 7. Acceptance Criteria

### Technical Acceptance

**Phase 0:**
- [ ] `baseline/workflow-inventory-2026-06-01.md` exists with one entry per workflow (13 entries)
- [ ] `decisions/D-01-master-ci-failure-disposition.md` exists with root-cause OR deferral rationale

**Phase 1:**
- [ ] `deploy-promote.yml`: deliberate SDAP CI fail does NOT produce a deploy-promote failure record (FR-03)
- [ ] `deploy-infrastructure.yml`: feature-branch push with no `infrastructure/bicep/**` changes produces zero runs (FR-04)
- [ ] `nightly-quality.yml`: 3 consecutive successful nightly runs OR file removed (FR-05)

**Phase 2:**
- [ ] Workflow count ≤8 (FR-06)
- [ ] Each deleted workflow has a `decisions/D-0X-` record (NFR-06)
- [ ] All deletions via `git rm`, not comment-out (NFR-04)

**Phase 3:**
- [ ] `actionlint` is in required-status-checks list on master (FR-08)
- [ ] Deliberate-fail PR blocked from merging (FR-07)

**Phase 4:**
- [ ] Weekly health-report workflow runs ≥1 time successfully (FR-11)
- [ ] `.github/WORKFLOWS.md` exists; doc-drift audit passes (FR-09)
- [ ] `docs/procedures/workflow-incident-response.md` exists (FR-10)
- [ ] Notification-routing documentation embedded (FR-12)

**Phase 5:**
- [ ] Deliberate-fail PR (Phase 5) blocked by 2+ checks; closed unmerged (FR-13)

### Business Acceptance

- [ ] Rolling 7-day workflow success rate ≥90% by project close (FR-14) — measured by Phase 4 weekly report
- [ ] Owner confirms `dev@spaarke.com` is receiving spaarke-dev notifications (FR-12 owner action)

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|------------|---------|------------|
| R1 | Phase 0 baseline shows `sdap-ci.yml` STILL broken on master post-PR-#314 | Medium | High | Treat as project-blocker; pause Phase 1; FR-02 task 002 dedicated to root cause |
| R2 | First `workflows-validate.yml` PR can't satisfy its own check | High | Medium | Brief `enforce_admins` bypass; NFR-03; logged in `decisions/` |
| R3 | `actionlint` rejects legitimate workflows (false positives) | Low | Medium | Phase 1 smoke pass identifies false positives before required-check add |
| R4 | Deleted workflow turns out to be load-bearing | Low | Medium | D-03 default-delete forces "speak now"; recoverable via `git log` |
| R5 | Notification routing change locks owner out | Low | High | Owner-driven manual step (D-05); documented before/after |
| R6 | Cascade failures hide in workflows we haven't found | Low | Medium | Comprehensive Phase 0 inventory covers all 13 |
| R7 | Active Dependabot PRs (#244, #263, #264, etc.) touching `.github/workflows/` conflict with our changes | Medium | Low | Coordinate via task ordering; rebase as needed |

---

## 9. Next Steps

1. **Review this plan** — owner skim for accuracy
2. **Run `/task-create projects/github-actions-rationalization-r1`** to generate POML task files + TASK-INDEX.md
3. **Begin Phase 0** — execute task 001 (workflow inventory) and task 002 (master CI root cause) in parallel

---

**Status**: Ready for Tasks
**Next Action**: Generate POML task files

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks. Per NFR-08, the project's CLAUDE.md must be loaded by every task agent.*
