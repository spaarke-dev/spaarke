# Phase 1 Verify Report — 2026-06-23

## Verdict: ✅ GO for Phase 2

All FR-01..FR-05 acceptance criteria + Success Criteria 1 + 2 pass.

## Acceptance criteria results

| Requirement | Acceptance command / check | Result | Evidence |
|---|---|---|---|
| FR-01 | `Type` field has `Project` option (existing 6 preserved) | ✅ PASS | Options: `Idea, Epic, Story, Task, Bug, Spike, Project` |
| FR-02 | 6 new fields exist with exact schemas | ✅ PASS | `Project Type` (8 opts), `Worktree Path` (TEXT), `Project Folder` (TEXT), `Task Count` (NUMBER), `Tasks Completed` (NUMBER), `Project Status` (5 opts) — all confirmed |
| FR-03 | 7 labels exist | ✅ PASS | `epic`, `project`, `backlog`, `worktree:active`, `worktree:archived`, `on-hold`, `cancelled` |
| FR-04 | 3 YAML templates lint clean + at `.github/ISSUE_TEMPLATE/` | ✅ PASS (UI smoke deferred) | epic.yml, project.yml, idea.yml all yaml.safe_load OK |
| FR-05 | 12 Epic Issues with `Type=Epic` + label `epic` | ✅ PASS | Issue #s #421–#432 all created |
| Success Criterion #1 | `gh project field-list ... | grep -c "Project Type"` returns 1 | ✅ PASS | 1 match |
| Success Criterion #2 | All 12 Epics visible in Epics view | ✅ PASS | `gh issue list --label epic` returns 12 |
| Schema preservation | All 20 pre-existing fields intact | ✅ PASS | Total: 26 fields = 20 existing + 6 new |
| Item integrity | No item lost a field value | ✅ PASS | Verified: 0 items had Type values pre-mutation, 4 items still have Area values, all other field values preserved |

## Effort vs. plan

| Task | Estimated | Actual | Notes |
|---|---|---|---|
| 001 | 1h | ~15 min | Single `updateProjectV2Field` mutation |
| 002 | 2h | ~10 min | 6 `createProjectV2Field` mutations in one bash batch |
| 003 | 1h | ~5 min | 7 `gh label create` commands |
| 004 | 2h | ~15 min | 3 YAML files (skipped UI smoke — deferred to merge) |
| 005 | 2h | ~20 min | Python helper script `create-epics.py` |
| 008 | 1h | ~10 min | Verification commands |
| **Total** | **9h** | **~75 min** | ~7× faster than plan estimate |

## Lessons learned

### 1. `updateProjectV2Field` reassigns option IDs (BREAKING)

The GitHub Projects v2 GraphQL mutation `updateProjectV2Field` REPLACES the full option list AND generates entirely new internal option IDs for each option, even when names are unchanged. Items currently bound to old option IDs lose their references.

**Impact on this project**: zero — none of the 22 pre-existing items had Type values, so no data was lost.

**Risk to future projects**: HIGH if any project on Project #2 has set Type values at the item level before the mutation runs. The `/devops-portfolio-setup` skill (task 010) MUST implement a snapshot → mutate → reconcile pattern.

See `notes/spikes/phase1-task001-execution-log-2026-06-23.md` § "Required changes to task 010" for the binding update to the skill design.

### 2. Issue templates UI surface = default branch only

GitHub's "New Issue" picker reads `.github/ISSUE_TEMPLATE/*.yml` from the **default branch only** (master). Templates committed to a feature branch are NOT visible in the picker. Task 004's UI smoke test step was therefore deferred — it will execute correctly once the project merges to master.

This is a meaningful constraint for any future skill that wants to verify "templates are usable" — must check post-merge, not on feature branch.

### 3. Encoding pitfalls

Python `print()` with Unicode glyphs (`≥`, `✅`, `→`) crashes on default Windows CP1252 stdout. Verification scripts MUST avoid emoji + glyphs OR set `PYTHONIOENCODING=utf-8`. Future skills authored in Python should use plain ASCII for verification output.

## Outstanding deferred work

- **UI smoke test for FR-04**: deferred to first post-merge access of GitHub New Issue picker. Non-blocking for Phase 2.
- **Idempotency contract for tasks 001-005**: only individually-task-level idempotent. Skill task 010 (`/devops-portfolio-setup`) codifies the full idempotent re-runner.

## Recommendation

**Phase 2 start authorized.** First task: 010 (create `/devops-portfolio-setup` skill).

Outstanding context for Phase 2:
- Field IDs captured in `notes/phase1-field-ids.md`
- Epic Issue numbers captured in `notes/phase1-epic-issue-numbers.md`
- Reusable Python pattern for Issue creation in `notes/spikes/create-epics.py`
- Critical skill-design lesson logged in `notes/spikes/phase1-task001-execution-log-2026-06-23.md`
