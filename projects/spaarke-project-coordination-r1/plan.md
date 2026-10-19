# Project Plan: Spaarke Project Coordination R1

> **Last Updated**: 2026-10-18
> **Status**: Ready for Tasks
> **Spec**: [spec.md](spec.md) (rev 2) · **Design**: [design.md](design.md) (rev 3)

---

## 1. Executive Summary

**Purpose**: Make cross-session messaging reliable without constraining it, and add a minimal, deterministic record layer for what messaging can't reach (sessions not running, shared dev deploys, peer worktrees, finding scope).

**Scope**:
- Phase 0 — messaging configuration + measurement, proved at a 14-day checkpoint
- Phase 1 — deploy ancestry guard, dev log, raw-command hook, worktree write boundary
- Phase 2 — constraint file + triage prompt; issue hygiene
- Phase 3 — README-frontmatter registry; report-only detector
- Phase 4 — pre-code coordination issues; `SessionStart` banner
- Phase 5 — Step 0/9 checks; Router acknowledgement; `/conflict-check` reuse

**Timeline**: about 14–20 working days of task effort, plus two calendar waits: the 14-day phase 0 checkpoint and a 1–2 week report-only detector period. | **Estimated Effort**: 110–150 hours

---

## 2. Architecture Context

### Design Constraints

**From ADRs and root CLAUDE.md** (must comply):
- **Root CLAUDE.md §16** — enforcement ladder: type → lint → source-scan guard → hook/permission → prose. Every guard ships with must-fire and must-not-fire controls and records "Enforced by:".
- **Root CLAUDE.md §3** — `.claude/**` edits are main-session-only, so those tasks are `parallel-safe:false`.
- **Root CLAUDE.md §11** — a three-question justification for every new surface (spec §11 table).
- **ADR-038** (`docs/adr/ADR-038-testing-strategy.md`) — tests for every script, hook and CI step; fixtures as named in each FR.
- **ADR-052** (`.claude/adr/ADR-052-workload-placement.md`) — the nightly detector is CI-hosted; nothing is added to the BFF.
- **ADR-028** — untouched; the Kudu read reuses the operator's existing management token.

**From Spec**:
- No text anywhere about when, whether, how often or in what form to message (MUST NOT, NFR-09, success criterion 11)
- Phase 0 proved before phases 3–5 start
- New hooks use `ask`, never `deny`, run under 5 s, and make no network calls
- Never block on a paused or closed project; no master ruleset change

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| Live SHA from the deployed DLL via Kudu VFS | `/ping` returns `pong` (K-1) | Shared PowerShell guard; no BFF change |
| README frontmatter registry | No shared-file contention (C-8) | Detector and banner read frontmatter only |
| Extend `guard-worktree-links.py` for E4 | Already guards links and root deletes (K-5) | One hook, more rules |
| `ack` in Router, applied by the owning session | Autonomous (C-10); no ruleset change (C-6) | `ci-router.yml` change; coordinate D-1 |
| Detector in Python, deterministic | Matches existing hooks; NFR-04 | Byte-identical output; fixtures |

### Discovered Resources

**Applicable skills**:
- `.claude/skills/ai-procedure-maintenance/` — checklist G (enforcement ladder) for every skill, hook and constraint edit
- `.claude/skills/task-execute/`, `.claude/skills/task-create/`, `.claude/skills/project-pipeline/`, `.claude/skills/design-to-spec/` — skills this project edits
- `.claude/skills/conflict-check/` — reused by the detector (FR-16); run before every shared-file PR
- `.claude/skills/devops-project-sync/`, `.claude/skills/devops-project-archive/` — lifecycle mirroring (FR-13)
- `.claude/skills/project-defer-issue-tracking/` — issue hygiene (FR-07, FR-09, FR-10)
- `.claude/skills/bff-deploy/`, `.claude/skills/ci-cd/` — deploy and CI context for FR-01/02 and FR-15/22
- `.claude/skills/code-review/`, `.claude/skills/adr-check/` — quality gates

**Knowledge and standards**:
- `docs/standards/TEST-ARCHITECTURE.md`, `docs/adr/ADR-038-testing-strategy.md` — test placement and KEEP paths
- `docs/procedures/testing-and-code-quality.md` — build and test commands
- `.claude/constraints/agent-cost.md` — model and effort per agent
- `.claude/FAILURE-MODES.md` — G-20 (worktree links), AP-1
- Claude Code docs: cross-session-messaging, settings-reference (`crossSessionInbound`, `dialogExpiry`), hooks (`sessionTitle`), env-vars (`CLAUDE_CODE_MESSAGING_SOCKET`, `CLAUDE_CODE_MESSAGING_TOKEN`)

**Reusable code (canonical implementations)**:
- `scripts/Deploy-BffApi.ps1:582-599` — Kudu VFS download with a management token
- `scripts/quality/guard-worktree-links.py` + `scripts/quality/tests/test_guard_worktree_links.py` — narrow `PreToolUse` guard with controls
- `scripts/quality/quality-alert-banner.py` + `scripts/quality/tests/test_quality_alert_banner.py` — background-cache `SessionStart` banner
- `.claude/hooks/reinject-project-state.ps1` — project identification from branch or worktree folder
- `.github/actions/quality-alert/action.yml` — open/auto-close issues from CI
- `.claude/skills/devops-project-sync/SKILL.md` — idempotent board-field sync
- `scripts/Validate-TaskPoml.ps1` — POML lint

**Dataverse**: none touched (no schema validation needed).

---

## 3. Implementation Approach

### Phase Structure

```
Phase 0: Messaging configuration (first)
└─ FR-26 baseline → FR-00 naming decision + config check → operator applies settings → 14-day checkpoint
Phase 1: Shared environment guards (parallel with the phase 0 wait)
└─ ancestry guard + dev log → wire 4 deploy paths → raw-command hook → write boundary → live verify
Phase 2: Findings (parallel with the phase 0 wait)
└─ constraint file → triage in task-execute → issue hygiene → task-create/090 → re-measure
Phase 3: Registry + detector (after the phase 0 checkpoint passes)
└─ frontmatter → bootstrap → detector → invocation → report-only checkpoint
Phase 4: Records for sessions not running
└─ pre-code coord issues → banner → (optional) live notice
Phase 5: Resolution + critical areas
└─ Step 0/9 → Router ack (after D-1) → /conflict-check reuse → handover at close
Wrap-up: 090
```

### Critical Path

**Blocking dependencies:**
- Phases 3–5 are BLOCKED BY task 005 (phase 0 checkpoint passes)
- Task 003 (config check) BLOCKED BY 002 (naming mechanism chosen)
- Tasks 011/012 (wiring) BLOCKED BY 010 (shared guard)
- Tasks 031–033 BLOCKED BY 030 (registry parser)
- Phase 4 BLOCKED BY 035 (report-only checkpoint)
- Task 051 (Router) BLOCKED BY D-1, owner approval (merge only)

**Critical path:** `001 ‖ 002 → 003 → 004 → (14 days) → 005 → 030 → 033 → 034 → (report period) → 035 → 040 → 050 → 052 → 053 → 090`

**High-risk items:**
- Task 002: naming may need mechanism 3 (lookup fallback). Mitigation: the fallback is already specified.
- Task 016: a live guard run against shared dev. Mitigation: read-only refusal test first; the owner approves any real deploy.
- Task 051: the Router change. Mitigation: D-1 first; fixtures; never blocks paused/closed owners.

---

## 4. Phase Breakdown

### Phase 0: Messaging configuration (FR-00, FR-26)

**Objectives:**
1. Measure today's cross-session messaging (baseline).
2. Give every session a stable project address, and make delivery reliable without adding any guidance.
3. Prove it at a 14-day checkpoint before phases 3–5.

**Deliverables:**
- [ ] `scripts/quality/` messaging-measurement script + tests; baseline report (task 001)
- [ ] Naming verification recorded; mechanism 1/2/3 chosen (task 002)
- [ ] `SessionStart` messaging-config / address / version check + tests; `.claude/settings.json` registration (task 003)
- [ ] Operator applies the user settings and updates or removes the `PATH` `claude` 2.1.6 (task 004)
- [ ] Phase 0 checkpoint report and go/no-go (task 005)

**Inputs**: spec FR-00, FR-26; `notes/naming-spike-2026-10-17.md`
**Outputs**: script, check hook, reports in `notes/`

### Phase 1: Shared environment guards (FR-01 – FR-04, FR-25)

**Objectives:**
1. Refuse a deploy that would remove a live build not contained in it, on every .NET App Service path.
2. Record every shared dev change.
3. Stop raw deploy commands and writes into another worktree going through silently.

**Deliverables:**
- [ ] Shared live-build ancestry guard + dev-log helper + tests (010)
- [ ] Guard wired into `Deploy-BffApi.ps1` and `Deploy-WorkspaceBff.ps1` (011)
- [ ] Guard wired into `provisioning/Deploy-ControlPlane.ps1` and `deploy-bff-api.yml` (012)
- [ ] Pinned dev-log issue + log-only wiring for SWA and web-resource deploy scripts (013)
- [ ] Raw-command `PreToolUse` hook + fixtures (014)
- [ ] Worktree write boundary in `guard-worktree-links.py` + fixtures (015)
- [ ] Live verification on `spaarke-bff-dev` (016, owner-approved)

**Inputs**: `scripts/Deploy-BffApi.ps1:582-599`, `guard-worktree-links.py`
**Outputs**: guard module, script edits, hook(s), tests, dev-log issue

### Phase 2: Findings — decide and dispose (FR-05 – FR-12)

**Objectives:**
1. One constraint file holding mechanics, the record rules and the triage prompt, with no messaging guidance.
2. Triage every finding at Step 9.5; keep issues tied to owners and files; close only on evidence.

**Deliverables:**
- [ ] `.claude/constraints/cross-session.md` + root `CLAUDE.md` pointer (020)
- [ ] `task-execute` Step 9.5 triage + Step 10 tracking-issue close (021)
- [ ] `project-defer-issue-tracking`: owner/area, rulings on the issue, verified closes (022)
- [ ] `task-create` issues-in-files + 090 template disposition and ageing; Board #2 views confirmed (023)
- [ ] Triage verification on a project-labelled issue set, read-only (024)
- [ ] Messaging re-measure 28 days after phase 2 (025)

**Inputs**: design rev 3 §5.6; pilot notes
**Outputs**: constraint file; skill edits

### Phase 3: Registry and detector, report-only (FR-13 – FR-15)

**Objectives:**
1. Machine-readable lifecycle and owned areas per project.
2. Deterministic report of same-file and owned-area overlaps across active projects.

**Deliverables:**
- [ ] Registry frontmatter parser + validator, `scripts/quality/project_registry.py` (030)
- [ ] Lifecycle transitions: README template, `project-pipeline`, `devops-project-sync` / `devops-project-archive`, 090 (031)
- [ ] Bootstrap frontmatter for active projects, owner-corrected (032)
- [ ] Detector script, signals 1–2 (+ signal 3 behind a flag) + fixtures (033)
- [ ] Invocation: nightly CI job, `task-create`, pre-PR (`push-to-github`), `/design-to-spec` checklist line (034)
- [ ] Report-only checkpoint: signal worth surfacing? turn on signal 3? FR-16 go? (035)

**Inputs**: task 005 passed
**Outputs**: detector, CI job, skill edits, README frontmatter

### Phase 4: Records for sessions not running (FR-17, FR-18, FR-18b)

**Deliverables:**
- [ ] Pre-code coordination-issue sync, idempotent, auto-close (040)
- [ ] `SessionStart` coordination banner (041)
- [ ] Optional inbox-socket live notice (042)

### Phase 5: Resolution and critical areas (FR-16, FR-20 – FR-23)

**Deliverables:**
- [ ] `task-execute` Step 0 item resolution + Step 9 diff check and `review:<owner>` label (050)
- [ ] Router critical-area `ack`, with stale-ack removal (051)
- [ ] `/conflict-check` reuses the detector (052)
- [ ] Owned-area handover in the 090 template (053)

### Wrap-up (090)

- [ ] Final messaging measurement; success criteria 1–11 verified; lessons learned; `/test-diet`; frontmatter `closed`

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| Claude Code ≥ 2.1.236 on every binary | VS Code extension 2.1.300; `PATH` 2.1.6 | Medium | Task 004 |
| GitHub API (`gh`) | Available | Low | Hooks read cache; never wait on network |
| Kudu VFS on `spaarke-bff-dev` / control plane | Available | Low | Same access as today's hash check |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| Board #2 triage fields | GitHub Project 2 | Ready |
| Registration MUST | `.claude/skills/project-pipeline/SKILL.md` Step 4.2 | Merged (#1983) |
| `ci-router.yml` change (D-1) | Owner approval (CI project no longer active) | Pending |
| PR #2043 (hooks in `.claude/settings.json`, root `CLAUDE.md`) | Merged 2026-10-18; in branch | Keep its entries unchanged |

---

## 6. Testing Strategy

**Unit tests** (pytest, `scripts/quality/tests/`; Pester for PowerShell where the guard lives):
- Every hook and guard: must-fire and must-not-fire fixtures, as listed per FR
- Detector: byte-identical output; signal fixtures; ignore list; paused/closed exclusion
- Measurement script: deterministic counts on fixture transcripts

**Integration tests**:
- Deploy guard against a stubbed Kudu response (ancestor / not ancestor / unknown / unreadable / supersede)
- Router acknowledgement fixtures (un-acked fails, acked passes, stale ack removed, paused owner never blocks)

**Live checks (owner-approved)**:
- Guard refusal against `spaarke-bff-dev` from a branch lacking the live commit (016)
- Two live worktree sessions addressed by project slug (002/005)

---

## 7. Acceptance Criteria

### Technical Acceptance

**Phase 0:** sessions addressed by project; no held, expired or misaddressed delivery notices in 14 days; send volume not lower than baseline.
**Phase 1:** E1 replay refused on all four paths; one dev-log entry per deploy; hooks pass their fixtures.
**Phase 2:** the constraint file contains no messaging guidance; the triage pilot method respects on-issue rulings.
**Phase 3:** detector output is deterministic; every active project's frontmatter parses.
**Phase 4:** a restarted session sees a new `coord` issue at start; reruns cause zero mutations.
**Phase 5:** the Router fixtures pass; `/conflict-check` has a single overlap implementation.

### Business Acceptance

- [ ] Cross-session messaging volume ≥ baseline at every measurement (success criterion 10)
- [ ] No unguarded shared deploy after phase 1

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|------------|---------|------------|
| R1 | Process wording suppresses messaging | Low | High | No guidance text; FR-26 gates the next phase |
| R2 | VS Code rename doesn't set the address | Medium | Medium | Mechanism 3 fallback (task 002) |
| R3 | The guard refuses a legitimate deploy | Medium | Medium | `-Supersede "<reason>"`, logged |
| R4 | Router gate blocks the wrong PRs | Medium | Medium | Globs proved by replay (020); owner approves before the 051 merge (D-1) |
| R5 | Write-boundary false positives slow sessions | Medium | Low | `ask` not `deny`; fail-open; must-not-fire fixtures for measurement worktrees and scratchpad |
| R6 | Shared skill files collide with other projects | High | Medium | Small PRs after `/conflict-check`; sequential `.claude/**` tasks |

---

## 9. Next Steps

1. Owner reviews spec, plan and `tasks/TASK-INDEX.md`.
2. In a new session: "work on task 001".

---

**Status**: Ready for execution (not started)
**Next Action**: Owner review, then task 001 in a new context session

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks.*
