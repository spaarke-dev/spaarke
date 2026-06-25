# Project Plan: Spaarke DevOps Project Tracking (r1)

> **Last Updated**: 2026-06-23
> **Status**: Ready for Tasks
> **Spec**: [spec.md](spec.md)

---

## 1. Executive Summary

**Purpose**: Deliver a GitHub-native portfolio tracker so a single Project #2 page answers "what's running, what's stalled, what's complete" across Spaarke's ~30+ active and ~100+ historical projects — without introducing a parallel tracking system and without disrupting the existing POML/worktree development flow.

**Scope**:
- Extend GitHub Project #2 schema (`Type=Project`, 6 fields, 7 labels, 3 issue templates) + create ~12 initial Epics
- Implement 9 new `/devops-*` Claude Code skills (lifecycle: capture → promote → start → sync → archive)
- Inject portfolio hooks into 9 existing skills (silent, additive, fail-safe)
- Backfill ~20–30 active projects onto the board
- Extend two existing docs + root `CLAUDE.md` §16

**Timeline**: Estimated 4–6 weeks at one-task-at-a-time pace; ~2–3 weeks with parallel skill authoring | **Estimated Effort**: 80–120 hours across all phases

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **None mandatory.** The Spaarke ADR catalog focuses on code/auth/AI architecture; this project's domain (DevOps tooling, skill authoring, GitHub configuration, docs) has no binding ADR coverage. Confirmed during design-to-spec Step 3 discovery and Pipeline Step 2 resource discovery.
- **ADR-010 (DI Minimalism)** — informational only; would apply if any skill introduces .NET service code (not anticipated; skills are markdown + `gh` CLI invocations).

**From Spec**:
- `gh` CLI (or `gh api graphql`) is the only allowed GitHub Project API client — no Octokit/REST wrapper (Spec MUST rules)
- Preserve all 20 existing fields on Project #2 — additive only (Spec MUST rules; NFR-01)
- All 9 new skills idempotent — repeated runs against unchanged state produce zero API mutations (NFR-04)
- No GitHub tokens/PATs committed (Spec MUST rules)
- POML tasks remain authoritative; only aggregates (`Task Count`, `Tasks Completed`) mirrored to Issues (NFR-02, D-08)
- Hook injection additive only — no existing-skill contract broken (NFR-03)
- All 9 new skill files follow Spaarke convention: `.claude/skills/{name}/SKILL.md` with YAML frontmatter + Prerequisites/Purpose/Steps/Failure-Modes sections (NFR-07)
- Doc extensions preserve existing structure — no renumbering, no rearranged scenarios (NFR-08)

### Key Technical Decisions

| Decision | Rationale | Impact |
|---|---|---|
| Extend Project #2 (not new board) | Single source of truth; reuse existing 20 fields | All new fields/views land on #2; existing views preserved |
| POML stays authoritative for tasks | Spec-driven workflow unchanged; no per-task GitHub Issue churn | Skills track aggregates only (`Task Count`, `Tasks Completed`) |
| Every Project has an Epic parent | Forces stable taxonomy; enables Roadmap rollup | `--epic` flag required on `idea-promote` and `project-start` |
| Worktree deleted on archive; folder kept with `.archived` | Reclaims disk; preserves history + branch on remote | `/devops-project-archive` runs `git worktree remove` + writes marker file |
| `gh` CLI only | Reuses existing auth; lowest dep footprint | Skills are markdown + bash/PowerShell invoking `gh` |
| Idempotent + partial-success sync | Hooks ride on normal flow; failures degrade gracefully | NFR-03, NFR-04, F7 resolution drive `/devops-project-sync` design |

### Discovered Resources

**Applicable ADRs** (resource discovery — Pipeline Step 2):
- None mandatory. ADR-010 informational only.

**Applicable Skills** (existing skills this project leverages or extends):
- `.claude/skills/project-pipeline/SKILL.md` — orchestrator that initialized this project
- `.claude/skills/task-execute/SKILL.md` — executes each POML task; receives a hook in Phase 4 (FR-19)
- `.claude/skills/task-create/SKILL.md` — creates the POML files in Pipeline Step 3; receives a hook in Phase 4 (FR-18)
- `.claude/skills/code-review/SKILL.md` — QA gate for skill-creation PRs
- `.claude/skills/adr-aware/SKILL.md` — proactive ADR loading (always-on)
- `.claude/skills/spaarke-conventions/SKILL.md` — coding/skill conventions (always-on)
- `.claude/skills/design-to-spec/SKILL.md` — generated this spec; receives hook in Phase 4 (FR-16)
- `.claude/skills/context-handoff/SKILL.md` — receives the highest-value hook (FR-20)
- `.claude/skills/worktree-setup/SKILL.md`, `worktree-sync/SKILL.md`, `repo-cleanup/SKILL.md`, `merge-to-master/SKILL.md` — all receive hooks in Phase 4

**Skills this project will CREATE** (9 new `/devops-*` skills):
- `/devops-portfolio-setup` (Phase 1 capstone) — one-time schema bootstrap
- `/devops-epic-create`, `/devops-idea-create`, `/devops-idea-promote` (Phase 2) — capture & promotion
- `/devops-project-start` (Phase 2) — **THE BLESSED HANDOFF** per D-13
- `/devops-project-register` (Phase 2) — inverse handoff for backfill
- `/devops-project-sync`, `/devops-portfolio-status`, `/devops-project-archive` (Phase 2) — lifecycle ops

**Knowledge Articles / Constraints**:
- [`.claude/skills/INDEX.md`](.claude/skills/INDEX.md) — skill convention (NFR-07 binding); frontmatter format
- [`.claude/skills/task-execute/SKILL.md`](.claude/skills/task-execute/SKILL.md) — hook-injection pattern exemplar (Step 9.5 invocations of code-review + adr-check)
- [`.claude/skills/worktree-setup/SKILL.md`](.claude/skills/worktree-setup/SKILL.md) — skill structure exemplar
- [`.claude/skills/design-to-spec/SKILL.md`](.claude/skills/design-to-spec/SKILL.md) — skill structure exemplar
- [`.claude/CHANGELOG.md`](.claude/CHANGELOG.md) — entry-format reference (FR-31)

**Reference Workflows** (`.github/workflows/` — 11 files exist; Phase 5 may add 2 new):
- `adr-audit.yml`, `css-reset-gate.yml`, `deploy-bff-api.yml`, `deploy-infrastructure.yml`, `deploy-office-addins.yml`, `deploy-promote.yml`, `nightly-health.yml`, `report-workflow-health.yml`, `sdap-ci-docs-only.yml`, `sdap-ci.yml`, `workflows-validate.yml`
- Phase 5 optional FR-27 (auto-comment Action) + FR-28 (scheduled sync) would add new files; both are r1 polish, not acceptance (D-19, D-22)

**Similar Prior Projects** (templates for task-decomposition patterns):
- [`projects/ci-cd-github-enhancement/`](../ci-cd-github-enhancement/) — tiered model + escape hatches (Complete)
- [`projects/github-actions-rationalization-r1/`](../github-actions-rationalization-r1/) — workflow rationalization + `actionlint` (Complete)

**Layout exemplar**: [`projects/x-ui-dialog-shell-standardization/`](../x-ui-dialog-shell-standardization/) — canonical project structure (README/plan/CLAUDE.md/current-task/tasks/notes layout)

**Existing GitHub configuration** (READ-ONLY baseline at Phase 1 start):
- GitHub Project #2 "Spaarke Core" — 20 existing fields, 7 existing default views, ~22 existing items with `Type IN (Idea, Epic, Story, Task, Bug, Spike)`
- Existing labels — verify via `gh label list` at Phase 1 start
- Existing issue templates at `.github/ISSUE_TEMPLATE/` (if any)

---

## 3. Implementation Approach

### Phase Structure

```
Phase 1: Foundation (Week 1)
└─ Extend Project #2 schema (Type option, 6 fields, 7 labels, 3 templates)
└─ Create 12 initial Epics
└─ Verify gate: gh field-list shows all new fields

Phase 2: Skills (Week 2-3)
└─ Implement 9 /devops-* skills
└─ /devops-portfolio-setup is gate (Phase 1 codified)
└─ Smoke-test each skill against a throwaway Project Issue
└─ Verify gate: ls .claude/skills/devops-* | wc -l == 9

Phase 3: Active backfill (Week 3-4)
└─ Enumerate active projects (F6 definition)
└─ Run /devops-project-register per project (sequential, batch-of-20 + backoff)
└─ Verify gate: Active view shows expected project count

Phase 4: Automation hooks (Week 4-5)
└─ Inject hooks into 9 existing skills (sequential — .claude/ paths)
└─ Smoke-test each hooked skill on a known-good project
└─ Verify gate: grep returns 9 skill files with hook references

Phase 5: Polish (Week 5, optional)
└─ Portfolio views configured
└─ Per-project README pointer block backfilled
└─ Optional: FR-27 (auto-comment Action) + FR-28 (scheduled sync) — defer unless drift visible
└─ Verify gate: Roadmap view loads with all Epics + Projects

Phase 6: Documentation (Week 6)
└─ Extend HOW-TO-INITIATE-NEW-PROJECT.md (Step 0 + Portfolio Integration section)
└─ Extend AI-CODING-PROCEDURES-GUIDE.md (7 lifecycle scenarios)
└─ Update root CLAUDE.md §16 + .claude/CHANGELOG.md
└─ Verify gate: grep "Portfolio tracking" CLAUDE.md returns 1
```

### Critical Path

**Blocking Dependencies:**
- Phase 2 BLOCKED BY Phase 1 (skills exercise the schema Phase 1 lays down)
- Phase 3 BLOCKED BY Phase 2 (backfill calls `/devops-project-register`)
- Phase 4 BLOCKED BY Phase 2 (hooks call `/devops-project-sync` and `/devops-project-register`)
- Phase 5 BLOCKED BY Phase 1 (views require new fields) + Phase 4 (per-README pointer block writes are skill-driven)
- Phase 6 NOT BLOCKED by skills code (docs reference the skills by name); but final review BLOCKED BY Phase 5 view URLs
- Phase 1 task: `/devops-portfolio-setup` skill **DOES NOT EXIST YET** during Phase 1 — Phase 1 is hand-driven `gh` commands; the skill in Phase 2 codifies and replays them idempotently

**High-Risk Items:**
- Phase 1 schema migration touches 22 existing items (new `Type=Project` option visible to all) — Mitigation: additive only; existing items default to `Mixed` or null on `Project Type` field
- Phase 3 rate-limit risk during backfill — Mitigation: batch-of-20 + exponential backoff per NFR-05
- Phase 4 hook injection brittleness — Mitigation: additive only; hooks degrade-to-warn on failure (NFR-03)

---

## 4. Phase Breakdown

### Phase 1: Foundation — Project #2 schema + Epics (Week 1)

**Objectives:**
1. Add `Type=Project` option + 6 custom fields + 7 labels to Project #2
2. Land 3 issue templates (`epic.yml`, `project.yml`, `idea.yml`)
3. Create ~12 initial Epic Issues per §4.6 taxonomy

**Deliverables:**
- [ ] Project #2 has `Type=Project` option (existing options preserved)
- [ ] 6 new custom fields with exact schemas per FR-02
- [ ] 7 labels (`epic`, `project`, `backlog`, `worktree:active`, `worktree:archived`, `on-hold`, `cancelled`)
- [ ] 3 issue templates at `.github/ISSUE_TEMPLATE/{epic,project,idea}.yml`
- [ ] 12 Epic Issues created on Project #2 with `Type=Epic`, label `epic`

**Critical Tasks:**
- Extend `Type` field option list — MUST be additive (preserves existing 22 items)
- Create issue templates — must be parseable by skills landing in Phase 2

**Inputs**: spec.md FR-01..FR-05, design.md §4.6 Epic taxonomy
**Outputs**: Extended Project #2 schema, 12 Epic Issues, 3 issue templates committed

---

### Phase 2: Skills — 9 `/devops-*` skills (Week 2–3)

**Objectives:**
1. Codify Phase 1 schema bootstrap into `/devops-portfolio-setup` (idempotent re-run)
2. Implement Epic / Idea / Project lifecycle skills
3. Smoke-test each skill against throwaway Project Issues before Phase 3 backfill

**Deliverables:**
- [ ] `/devops-portfolio-setup` (FR-06) — bootstraps Phase 1 schema; idempotent
- [ ] `/devops-epic-create` (FR-07) — Epic Issue creation
- [ ] `/devops-idea-create` (FR-08) — Idea Issue capture (no folder)
- [ ] `/devops-idea-promote` (FR-09) — Path A (1→1) + Path B (N→1)
- [ ] `/devops-project-start --from-issue #N` (FR-10) — THE BLESSED HANDOFF
- [ ] `/devops-project-register --from-folder` (FR-11) — inverse handoff for backfill
- [ ] `/devops-project-sync` (FR-12) — idempotent local→GitHub field sync
- [ ] `/devops-portfolio-status` (FR-13) — dashboard + `--snapshot`
- [ ] `/devops-project-archive` (FR-14) — Complete/Cancel + worktree delete + `.archived` marker
- [ ] All 9 skills follow NFR-07 file convention; listed in `.claude/skills/INDEX.md`

**Critical Tasks:**
- `/devops-portfolio-setup` MUST be first (validates Phase 1 schema; enables re-run anywhere)
- `/devops-project-start` is the load-bearing skill — get it right before backfill

**Inputs**: spec.md FR-06..FR-14, NFR-07, skill exemplars (`task-execute`, `worktree-setup`, `design-to-spec`)
**Outputs**: 9 new SKILL.md files, INDEX.md updated, smoke-test artifacts in `notes/spikes/`

---

### Phase 3: Active backfill (~20–30 projects, Week 3–4)

**Objectives:**
1. Enumerate active/in-flight worktrees per F6 definition
2. Run `/devops-project-register --from-folder` for each (sequential, with backoff)
3. Verify Active projects view matches expected count

**Deliverables:**
- [ ] Enumeration list captured in `notes/backfill-enumeration-{date}.md`
- [ ] Project Issue created for each active worktree with all 6 fields populated
- [ ] Each Issue has correct `Parent issue` Epic
- [ ] Ordering: by Epic, then by most-recent commit activity within Epic (F4 resolution)

**Critical Tasks:**
- Enumeration (F6 criteria): worktree exists + (active task OR open PR OR commits in 30d)
- Rate-limit hygiene: batches of 20 + exponential backoff (NFR-05)

**Inputs**: F6 definition, `/devops-project-register` skill (Phase 2)
**Outputs**: ~20–30 Project Issues on Project #2; Active view populated

---

### Phase 4: Automation hooks into 9 existing skills (Week 4–5)

**Objectives:**
1. Inject portfolio-update hooks into 9 existing skills (additive only)
2. Each hook is silent on success, degrades-to-warn on failure (NFR-03)
3. Smoke-test each hooked skill on a known-good project — no regression

**Deliverables (one task per hooked skill):**
- [ ] FR-16 — `design-to-spec` hook: post-spec → `/devops-project-sync` → `In Progress`
- [ ] FR-17 — `project-pipeline` hook: start-of-skill register-or-sync
- [ ] FR-18 — `task-create` hook: set `Task Count` on Issue
- [ ] FR-19 — `task-execute` hook: increment `Tasks Completed`; prompt at last task
- [ ] FR-20 — `context-handoff` hook: always `/devops-project-sync` at end (highest-value)
- [ ] FR-21 — `worktree-setup` hook: link-or-prompt register
- [ ] FR-22 — `worktree-sync` hook: end-of-sync sync
- [ ] FR-23 — `repo-cleanup` hook: archive-candidate prompt + `/devops-project-archive`
- [ ] FR-24 — `merge-to-master` hook: PR # comment + conditional archive prompt

**Critical Tasks:**
- All hook-injection tasks modify `.claude/skills/` files → `parallel-safe: false` per root CLAUDE.md §3 (Sub-Agent Write Boundary)
- Smoke-test ordering: simple hooks first (`task-create`, `worktree-sync`); complex prompts last (`repo-cleanup`, `merge-to-master`)

**Inputs**: Phase 2 skills (`/devops-project-sync`, `/devops-project-register`, `/devops-project-archive`)
**Outputs**: 9 updated SKILL.md files with hook references in their `Steps` sections

---

### Phase 5: Polish for shared audience (Week 5, partially optional)

**Objectives:**
1. Configure portfolio views on Project #2 (required for FR-25)
2. Verify per-project README pointer block landed during Phase 3+4 (FR-26)
3. Optionally ship FR-27 (auto-comment Action) + FR-28 (scheduled sync) — defer unless drift visible

**Deliverables:**
- [ ] FR-25 — 6 portfolio views configured on Project #2
- [ ] FR-26 — Per-project README pointer block present on every active project's README (verification only; pointer block is auto-written by `/devops-project-start` and `/devops-project-register` in earlier phases)
- [ ] (Optional) FR-27 — GitHub Action auto-comments `/devops-project-start --from-issue #N` on Type=Project change
- [ ] (Optional) FR-28 — Scheduled `workflow_dispatch` Action for nightly drift sync

**Critical Tasks:**
- View configuration is GitHub-UI-driven (no API mutation needed for filters); document the filter strings precisely
- F5 resolution: defer FR-27/FR-28 by default; revisit only if drift visible

**Inputs**: Phase 1 schema, Phase 4 hooks (drive README pointer blocks)
**Outputs**: Portfolio views URL-stable; pointer block audit log

---

### Phase 6: Documentation (Week 6)

**Objectives:**
1. Extend `docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md` (Step 0 + Portfolio Integration section)
2. Extend `docs/procedures/AI-CODING-PROCEDURES-GUIDE.md` (7 lifecycle scenarios)
3. Update root `CLAUDE.md` §16 + `.claude/CHANGELOG.md`

**Deliverables:**
- [ ] FR-29 — HOW-TO-INITIATE-NEW-PROJECT.md extended (Step 0, Portfolio Integration section, 9 skills in command reference, troubleshooting)
- [ ] FR-30 — AI-CODING-PROCEDURES-GUIDE.md extended (7 new scenarios in "what to say / what happens / what to check" pattern)
- [ ] FR-31 — Root `CLAUDE.md` §16 has portfolio row; `.claude/CHANGELOG.md` has entry

**Critical Tasks:**
- NFR-08: preserve existing structure of host docs — no section renumbering
- All three doc changes can happen in parallel (different files; modify-only)

**Inputs**: Phase 2 skill names + invocation syntax; Phase 5 view URLs
**Outputs**: 3 updated doc files; CHANGELOG entry; root CLAUDE.md row

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|---|---|---|---|
| GitHub Projects v2 GraphQL API | GA | Low | Stable API; document mutation payloads in skill files |
| GitHub Issues API | GA | Low | Stable; only standard mutations used |
| `gh` CLI v2.40+ | Installed + Authenticated | Low | Verified via `gh auth status` |
| GitHub Issue Templates form schema | GA | Low | YAML form templates are stable |

### Internal Dependencies

| Dependency | Location | Status |
|---|---|---|
| 9 existing Spaarke skills (to be hooked) | `.claude/skills/{design-to-spec,project-pipeline,task-create,task-execute,context-handoff,worktree-setup,worktree-sync,repo-cleanup,merge-to-master}/SKILL.md` | Current |
| Skill structure convention | `.claude/skills/INDEX.md` | Current |
| Hook-injection exemplar | `.claude/skills/task-execute/SKILL.md` Step 9.5 | Current |
| Spaarke CHANGELOG style | `.claude/CHANGELOG.md` | Current |
| Existing GitHub Project #2 | https://github.com/users/spaarke-dev/projects/2 | Live |

---

## 6. Testing Strategy

**No unit tests** — skills are markdown + bash/PowerShell + `gh` CLI; no compiled units.

**Smoke tests** (per-skill, Phase 2):
- Each skill exercised against a throwaway Project Issue (created/destroyed in test loop)
- Verify idempotency: run twice; second run produces zero API mutations
- Verify partial-success: simulate transient API failure mid-sync; confirm subsequent run heals

**End-to-end test** (Phase 2 → Phase 3 → Phase 5):
- Create a fresh Idea Issue → promote (Path A) → `/devops-project-start --from-issue` → folder/worktree scaffolded → `/devops-project-sync` → `/devops-project-archive` → marker file present, worktree gone
- Full round-trip should produce no manual GitHub UI clicks beyond initial Idea creation

**Regression test** (Phase 4):
- For each of 9 hooked skills, run on a known-good project before injection (capture baseline); run after injection (compare). Hooks must NOT change non-portfolio behavior.

**Documentation verification** (Phase 6):
- `grep "Portfolio tracking" CLAUDE.md` returns 1
- `grep -c "^##" docs/procedures/AI-CODING-PROCEDURES-GUIDE.md` shows expected count of new scenarios

---

## 7. Acceptance Criteria

### Technical Acceptance

**Phase 1:**
- [ ] `gh project field-list 2 --owner spaarke-dev --format json | jq '.fields | length'` returns 26+ (20 existing + 6 new)
- [ ] `gh label list | grep -E "^(epic|project|backlog|worktree:active|worktree:archived|on-hold|cancelled)\s"` returns 7 lines
- [ ] All 3 issue templates open from GitHub "New issue" UI

**Phase 2:**
- [ ] `ls .claude/skills/devops-*` returns 9 SKILL.md files
- [ ] Each skill listed in `.claude/skills/INDEX.md`
- [ ] All 9 skills pass smoke test (idempotency + partial-success + happy-path)

**Phase 3:**
- [ ] Active projects view item count matches F6-derived enumeration count
- [ ] Spot-check 5 random projects: all 6 fields populated; Parent issue Epic correct

**Phase 4:**
- [ ] `grep -l "devops-project-sync\|devops-project-register" .claude/skills/{design-to-spec,project-pipeline,task-create,task-execute,context-handoff,worktree-setup,worktree-sync,repo-cleanup,merge-to-master}/SKILL.md` returns 9 lines
- [ ] Smoke-run each hooked skill on a known-good project — zero non-portfolio behavior change

**Phase 5:**
- [ ] 6 portfolio views configured and load without errors
- [ ] `grep -L "> \*\*Portfolio\*\*" projects/*/README.md` returns no active projects

**Phase 6:**
- [ ] `grep "Portfolio tracking" CLAUDE.md` returns 1
- [ ] AI-CODING-PROCEDURES-GUIDE.md has 7 new `##` scenario headers added
- [ ] `.claude/CHANGELOG.md` has an entry referencing this project

### Business Acceptance

- [ ] A non-implementer can answer in <30 seconds from a single GitHub page: "What Epics are active? How many projects in each? Rough portfolio status?"
- [ ] `/devops-portfolio-status --snapshot` produces a stakeholder-readable markdown file with zero raw field IDs leaking

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|---|---|---|---|---|
| R1 | Drift between local state and GitHub fields | Medium | Medium | `/devops-project-sync` is idempotent + hook-driven; Phase 5 optional scheduled drift check |
| R2 | Rate limit during backfill | Low–Medium | High | Batch in groups of 20 + exponential backoff per NFR-05 |
| R3 | POML format drift breaks parsing | Low | Medium | Spec assumes format stability over r1; hooks read only `TASK-INDEX.md` checkboxes |
| R4 | Hook injection breaks existing skill contract | Low | High | Additive only; hooks silent on success, degrade-to-warn on failure |
| R5 | Phase 1 schema visible to all 22 existing items | Low | Low | Additive only; existing items default to `Mixed` or null on new fields |
| R6 | "Active/in-flight" definition contested mid-backfill | Low | Low | Locked in F6 resolution (worktree + active-task OR open-PR OR 30d-commits) |
| R7 | Concurrent PR (`.github/workflows/*.yml`) merges before Phase 5 Actions land | Low | Low | Phase 5 Actions are optional; rebase on top if needed; check `gh pr list` before starting Phase 5 |

---

## 9. Next Steps

1. **Review this plan.md** with the project owner
2. **Run `/task-create` (via project-pipeline Step 3)** to generate ~42 task POMLs across 6 phases
3. **Begin Phase 1 (Task 001)** — extend Project #2 `Type` field with `Project` option

---

**Status**: Ready for Tasks
**Next Action**: Pipeline Step 3 — generate task POML files via `task-create`

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks.*
