# Current Task State — ci-cd-unit-test-remediation-r1

> **Last Updated**: 2026-06-26 ~13:30Z (by `/context-handoff` after scope-expansion artifacts written)
> **Recovery**: Read "Quick Recovery" section first — < 30 seconds reading time

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | 082 (in background sub-agent: test-inventory-broader CSV) + 086 (newly added — CI Router fix, awaiting user-supplied browser error OR explicit "bisect now") |
| **Step** | 082 sub-agent running; 086 integrated into project artifacts |
| **Status** | mid-Phase-2.5 (080+081 ✅; 082 dispatched; 086 added but not started) |
| **Mode** | 🤖 **AUTONOMOUS** — no approval gates between tasks/steps per project CLAUDE.md (EXCEPT 086 which awaits user direction — browser error message OR bisect approval) |
| **Next Action** | Wait for 082 background notification, then execute 083 (deep-cleanup PR 1) in main session. **For 086**: user can either (a) paste the browser error message from https://github.com/spaarke-dev/spaarke/actions/runs/28244069552 (cheap win), or (b) say "proceed with bisect" to start the systematic bisect on `debug/ci-router-bisect`. 086 can run in parallel with 083-085 since it touches different files (workflow YAML vs test .cs). |

### Uncommitted Changes in Working Tree (commit BEFORE compact OR after — user choice; not committed yet per user direction)

- `projects/ci-cd-unit-test-remediation-r1/spec.md` — added FR-B08, FR-B09, FR-B10 + SC-11 + owner-directive paragraph
- `projects/ci-cd-unit-test-remediation-r1/plan.md` — added Phase 2.5 section; revised critical path 28 → ~35 days
- `projects/ci-cd-unit-test-remediation-r1/README.md` — graduation criteria expanded with 3 new checkboxes; status row updated; changelog v0.3 added
- `projects/ci-cd-unit-test-remediation-r1/CLAUDE.md` — Decisions section gains 2026-06-26 scope-expansion entry
- `projects/ci-cd-unit-test-remediation-r1/tasks/TASK-INDEX.md` — Phase 2.5 section added; status summary 25→31; 071 dependency on 085 added; critical path note revised
- `projects/ci-cd-unit-test-remediation-r1/tasks/080-codify-build-vs-maintain-criteria.poml` — NEW
- `projects/ci-cd-unit-test-remediation-r1/tasks/081-build-test-diet-skill.poml` — NEW
- `projects/ci-cd-unit-test-remediation-r1/tasks/082-rerun-inventory-broader-criteria.poml` — NEW
- `projects/ci-cd-unit-test-remediation-r1/tasks/083-deep-cleanup-pr-1.poml` — NEW
- `projects/ci-cd-unit-test-remediation-r1/tasks/084-deep-cleanup-pr-2.poml` — NEW
- `projects/ci-cd-unit-test-remediation-r1/tasks/085-deep-cleanup-pr-3.poml` — NEW

**Suggested commit**: `docs(ci-cd-unit-test-remediation-r1): scope expansion 2026-06-26 — add Phase 2.5 (FR-B08/B09/B10 + SC-11) per build-vs-maintain framing`

### Critical Context (1-3 sentences)

Phase 1 (13 tasks) + Phase 2 (12 tasks) shipped to master via PR #459 (merge commit `be07090bc`). Task 070 (pre-cutover snapshot) complete — pre-cutover branch protection state = DISABLED captured. Task 071 cutover was ready to fire, but owner-directed scope expansion 2026-06-26 added Phase 2.5 (codify build-vs-maintain criteria + build `/test-diet` skill + retroactive deep cleanup to reduce BFF unit tests from ~6,700 to ≤3,500) — cutover (071) is now GATED on 085 (final deep-cleanup PR) per spec FR-B10.

---

## Full State (Detailed)

### Project status

| Field | Value |
|---|---|
| **Project** | ci-cd-unit-test-remediation-r1 |
| **Worktree** | `c:\code_files\spaarke-wt-ci-cd-unit-test-remediation-r1\` |
| **Branch** | `work/ci-cd-unit-test-remediation-r1` (3 commits ahead of master post-merge; nothing pushed since merge) |
| **Master HEAD synced** | `be07090bc` (PR #459 merge) |
| **Portfolio Issue** | [#457](https://github.com/spaarke-dev/spaarke/issues/457) (Epic [#429 DEVOPS & CODE QUALITY](https://github.com/spaarke-dev/spaarke/issues/429)) |
| **Portfolio fields** | Status=In Progress · Task Count=31 · Tasks Completed=20 (updated 2026-06-26) |
| **Mode** | AUTONOMOUS per project CLAUDE.md |

### Phase progress

| Phase | Tasks | Status |
|---|---|---|
| Phase 1 — Diagnose + directive rewrites | 13 | ✅ Complete (commit dae39b18d, merged via PR #459) |
| Phase 2 — Shadow workflows + deletions + skill directives | 12 of 12 resolved | ✅ Complete (commit cc305da98, merged via PR #459) |
| Phase 2.5 — Build-vs-maintain codification + deep cleanup (NEW 2026-06-26) | 6 | 🔲 Not started — tasks 080-085 |
| Phase 3 task 070 — pre-cutover snapshot | 1 | ✅ Complete 2026-06-26 12:55Z |
| Phase 3 task 071 — cutover | 1 | ⏸ Blocked on 085 (per scope expansion) |
| Phase 3 tasks 075, 076, 077 — soak + measurements + sdap-ci retirement | 3 | 🔲 Calendar-gated after 071 |
| Wrap-up (090) | 1 | 🔲 Final |

### Scope expansion summary (2026-06-26)

**What changed**: owner reframed the test problem from "narrow signature-match wiring tests" (original spec FR-B01..B07) to "build-vs-maintain" (scaffolding tests written for design/coverage vs regression-protecting tests with ongoing value). Reframing justified by industry consensus (Beck "delete the scaffolding", Feathers characterization-vs-behavior, Google test-sizes, DHH less-tests).

**Why**: Phase 2 task 053 narrow deletion (9 files, 179 tests, 2.4% reduction) didn't achieve project intent of "way over-engineered unit testing" remediation. The strict signature-match criteria couldn't validate file-by-file the deeper structural debt that judgment-based criteria reveal.

**Effects**:
- Spec: added FR-B08 (codify ≥10 scaffolding bans), FR-B09 (`/test-diet` skill), FR-B10 (deep cleanup to ≤3,500 tests) + SC-11
- Plan: added Phase 2.5 with 6 tasks
- Critical path: 28 → ~35 elapsed days
- Task count: 25 → 31 (portfolio updated)
- Phase 3 cutover (071) GATED on 085

### Phase 2.5 task plan (in execution order)

| # | Task | Rigor | Sub-agent safe | Dependencies | Blocks |
|---|---|---|---|---|---|
| 080 | codify-build-vs-maintain-criteria | FULL | NO (.claude/ writes) | — | 082 |
| 081 | build-test-diet-skill | FULL | NO (.claude/ writes) | — | 090 |
| 082 | rerun-inventory-broader-criteria | STANDARD | YES (sub-agent) | 080 | 083 |
| 083 | deep-cleanup-pr-1 (highest-confidence) | FULL | NO (strict serial) | 082 | 084 |
| 084 | deep-cleanup-pr-2 (medium-confidence) | FULL | NO (strict serial) | 083 | 085 |
| 085 | deep-cleanup-pr-3 (final + full dotnet test) | FULL | NO (strict serial) | 084 | 071 |

**Recommended execution**:
1. Commit current scope-expansion artifacts
2. Run 080 in main session (FULL — 3h estimated; codifies criteria in ADR-038 + constraints/testing.md + tests/CLAUDE.md)
3. Run 081 in main session (FULL — 2.5h; creates `/test-diet` skill + wires into task-execute wrap-up)
4. Run 082 via sub-agent or main session (STANDARD — 3h; produces broader test inventory CSV)
5. Run 083 → 084 → 085 strict serial in main session (FULL each; sliced deletion PRs with rebases)
6. THEN run 071 cutover (after explicit user confirmation per shared-state safety rule)

### Files modified this session (full list, scoped to scope-expansion work)

See "Uncommitted Changes in Working Tree" above. Outside that scope, the session also performed:
- Phase 1 + Phase 2 implementation (already committed + merged via PR #459)
- Task 070 snapshot artifacts (committed as part of Phase 2 commits or follow-up — verify with git status if unsure)
- Router fix commit `bc3403705` (also merged via PR #459)
- Master merge commit `4e2ac3ced` (merged via PR #459)

### Decisions made (cumulative; key entries)

- **2026-06-25**: ADR-038 standalone (not supersession of ADR-022)
- **2026-06-25**: Skip pipeline Step 4 (already on work/ worktree branch)
- **2026-06-25**: INDEX.md scope = last-30-day-active worktrees
- **2026-06-26**: Sub-slice 053a/b/c collapsed to single 053 PR per inventory finding
- **2026-06-26**: Branch protection currently DISABLED on master — task 070+071 must RESTORE
- **2026-06-26**: Router signal model = Model A composite + alls-green aggregator (resolves spec UQ #1)
- **2026-06-26**: SpaarkeAi deploy target = Dataverse web resource sprk_spaarkeai (not Static Web App)
- **2026-06-26**: Path reorganization bulk move DEFERRED with documented 3-decision strategy
- **2026-06-26**: 2 of 11 originally-deleted files came back from master merge (daily-briefing-r4 + redis-cache-remediation added real behavioral tests); effective deletion = 9 files
- **2026-06-26**: Master had 6 build errors (IDistributedCache→ITenantCache fixture debt from redis-cache-remediation) — non-blocking via continue-on-error:true; this project doesn't fix them (separate scope)
- **2026-06-26**: Router/Tier2 input contract mismatch fixed (commit bc3403705) — router translates classify outputs to tier2's run-* booleans
- **2026-06-26 (latest)**: **SCOPE EXPANSION** — Phase 2.5 added with FR-B08/B09/B10 + SC-11; build-vs-maintain framing per owner directive; 071 cutover gated on 085; task count 25→31; critical path 28→~35 days

### Key findings (cumulative)

1. **17 active worktrees** (vs spec's 5-6 estimate) — hot-path coordination more critical than spec assumed
2. **Test inventory finding**: 481 KEEP / 11 DELETE files via strict signature criteria; ~6,700 BFF unit tests survive — the deeper "build-vs-maintain" framing in Phase 2.5 expects to remove 1,500-3,000 more
3. **Branch protection DISABLED on master today** — rollback target = re-disable; forward-target = enable with `CI / Router` only
4. **sdap-ci.yml has continue-on-error:true** since 2026-06-24 ("CI informational-only until test-architecture-reset-r1 lands") — this project IS test-architecture-reset-r1; task 077 retires sdap-ci at cutover+14d
5. **PR #459 merged successfully** despite Build & Test (Debug) showing fail — that's the IDistributedCache/ITenantCache integration fixture debt, non-blocking

---

## Next Action — Step by Step

**Step 1: Commit scope-expansion artifacts** (~5 min)

```bash
git add projects/ci-cd-unit-test-remediation-r1/spec.md \
  projects/ci-cd-unit-test-remediation-r1/plan.md \
  projects/ci-cd-unit-test-remediation-r1/README.md \
  projects/ci-cd-unit-test-remediation-r1/CLAUDE.md \
  projects/ci-cd-unit-test-remediation-r1/current-task.md \
  projects/ci-cd-unit-test-remediation-r1/tasks/TASK-INDEX.md \
  projects/ci-cd-unit-test-remediation-r1/tasks/080-codify-build-vs-maintain-criteria.poml \
  projects/ci-cd-unit-test-remediation-r1/tasks/081-build-test-diet-skill.poml \
  projects/ci-cd-unit-test-remediation-r1/tasks/082-rerun-inventory-broader-criteria.poml \
  projects/ci-cd-unit-test-remediation-r1/tasks/083-deep-cleanup-pr-1.poml \
  projects/ci-cd-unit-test-remediation-r1/tasks/084-deep-cleanup-pr-2.poml \
  projects/ci-cd-unit-test-remediation-r1/tasks/085-deep-cleanup-pr-3.poml

git commit -m "docs(ci-cd-unit-test-remediation-r1): scope expansion 2026-06-26 — add Phase 2.5 (FR-B08/B09/B10 + SC-11) per build-vs-maintain framing"
git push
```

**Step 2: Start Phase 2.5 task 080** (FULL rigor, main session, ~3h)

User prompt to trigger: `"continue with task 080"` or `"work on task 080"` or just `"continue"` (TASK-INDEX has 080 as the next 🔲).

Task 080 codifies ≥10 new scaffolding-test bans into 3 binding documents (ADR-038, `.claude/constraints/testing.md`, `tests/CLAUDE.md`). Output of 080 is the input for 082's broader-criteria classifier.

---

## Blockers

**Status**: None blocking. Just awaiting commit of scope-expansion artifacts + initiation of task 080.

---

## Session Notes

### Recent activity (this session)

- Updated post-deletion summary (effective count: 9, not 11) after master merge brought 2 conflicted files back
- Investigated PR #459 CI failures: actionlint fixed (router/tier2 input contract); Build & Test (Debug) fail is pre-existing master debt (IDistributedCache→ITenantCache fixtures), non-blocking
- Merged PR #459 to master (`be07090bc`); synced worktree + main repo
- Executed task 070 (pre-cutover branch protection snapshot — captured DISABLED state as authoritative rollback source)
- Owner directive 2026-06-26: scope expansion to Phase 2.5
- Wrote scope-expansion artifacts: spec.md FRs, plan.md Phase 2.5, README graduation criteria, CLAUDE.md decisions, TASK-INDEX.md Phase 2.5 section, 6 new task POMLs
- Updated portfolio Issue #457: Task Count 25 → 31; posted scope-expansion comment
- Invoked `/context-handoff` to write this file

### Key learnings

- The original FR-B framing (signature-match wiring tests) under-delivered. The build-vs-maintain framing (judgment-based) is the necessary deeper criterion.
- Phase 2.5 inserted mid-project is unusual but spec is alive (not historical) — the spec gains FRs; phases gain sequencing; tasks gain new POMLs. Critical-path math gets revised.
- 17 active worktrees mean every `.claude/` skill edit (080, 081) is a high-coordination action — need to watch for other projects modifying same files between rebase + push.

---

## Quick Reference

### Project Context
- **Project**: ci-cd-unit-test-remediation-r1
- **Project CLAUDE.md**: [`CLAUDE.md`](./CLAUDE.md)
- **Task Index**: [`tasks/TASK-INDEX.md`](./tasks/TASK-INDEX.md)
- **Spec** (now extended): [`spec.md`](./spec.md) — FR-B01..FR-B10 + SC-01..SC-11
- **Plan** (now extended): [`plan.md`](./plan.md) — includes Phase 2.5
- **Worktree**: `c:\code_files\spaarke-wt-ci-cd-unit-test-remediation-r1\`
- **Branch**: `work/ci-cd-unit-test-remediation-r1` (3 commits ahead of master post-PR-#459-merge)
- **Portfolio Issue**: [#457](https://github.com/spaarke-dev/spaarke/issues/457)

### Applicable ADRs
- ADR-028 (Spaarke Auth) — Tier 1 auth smoke aligned
- ADR-030 (BFF feature flags) — path-aware dispatch
- ADR-032 (Null-Object kill-switch)
- **ADR-038 (Testing Strategy)** — STANDALONE; gets extended in task 080 with build-vs-maintain criteria
- ADR-022 (PCF Platform Libraries) — UNCHANGED; not a testing ADR (misattribution fixed in task 022 Phase 1)

### Recovery commands
- `/project-continue` — full project context reload + master sync
- `"continue task 080"` — start Phase 2.5
- `"where was I?"` — quick context recovery via this file
- `"show TASK-INDEX"` — see full task registry

---

*This file is the primary source of truth for active work state. Phase 2.5 scope expansion baked in 2026-06-26 ~13:30Z via /context-handoff.*
