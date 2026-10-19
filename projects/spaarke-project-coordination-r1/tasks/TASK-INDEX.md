# Task Index — spaarke-project-coordination-r1

> **Last Updated**: 2026-10-18
> **Spec**: [../spec.md](../spec.md) (rev 2) · **Plan**: [../plan.md](../plan.md)
> **Status tokens**: 🔲 [open] · 🔄 [wip] · ✅ [done] · ⚠️ [escalated] · 🟡 [blocked]. The bracketed token is mandatory (FAILURE-MODES G-16).

Execution is owner-gated: nothing has started. The owner reviews first and starts task 001 in a new session.

## Tasks

| ID | Title | Phase | Status | Deps | Gate | Parallel | Rigor · Tier |
|----|-------|-------|--------|------|------|----------|--------------|
| 001 | Messaging measurement script + baseline (FR-26) | 0 | 🔲 [open] | none | startable | P0-A | STANDARD · sonnet/high |
| 002 | Naming verification, interactive (FR-00 a) | 0 | 🔲 [open] | none | startable (owner ~2 min) | P0-A | MINIMAL · sonnet/medium |
| 003 | SessionStart messaging-config check (FR-00) | 0 | 🔲 [open] | 002 | 002 done | — (.claude) | FULL · sonnet/high |
| 004 | Owner applies settings + PATH claude; verify (b)–(d) | 0 | 🔲 [open] | 003 | owner present | — | MINIMAL · sonnet/medium |
| 005 | Phase 0 checkpoint (14 days after 004) | 0 | 🔲 [open] | 001, 004 | applied date + 14 days | — | MINIMAL · sonnet/medium |
| 010 | Live-build ancestry guard + dev-log helper (FR-01, FR-03) | 1 | 🔲 [open] | none | startable | P1-A | FULL · sonnet/xhigh |
| 011 | Wire guard: Deploy-BffApi, Deploy-WorkspaceBff (FR-02) | 1 | 🔲 [open] | 010 | 010 done | P1-B | FULL · sonnet/high |
| 012 | Wire guard: Deploy-ControlPlane, deploy-bff-api.yml (FR-02) | 1 | 🔲 [open] | 010 | 010 done | P1-B | FULL · sonnet/high |
| 013 | Pinned dev-log issue; log-only SWA + web-resource deploys (FR-03) | 1 | 🔲 [open] | 010 | 010 done | P1-B | STANDARD · sonnet/high |
| 014 | Raw-command PreToolUse hook (FR-04) | 1 | 🔲 [open] | none | startable | P1-A (main session) | FULL · sonnet/high |
| 015 | Worktree write boundary (FR-25) | 1 | 🔲 [open] | 014 | 014 done | — (.claude) | FULL · sonnet/xhigh |
| 016 | Live guard verification on spaarke-bff-dev | 1 | 🔲 [open] | 011, 012, 013 | merged; owner OK for any deploy | — | STANDARD · sonnet/high |
| 020 | Constraint file cross-session.md + root CLAUDE.md pointer (FR-05) | 2 | 🔲 [open] | 002 | 002 done | — (.claude) | STANDARD · opus/high |
| 021 | task-execute Step 9.5 triage, Step 10 tracking close (FR-06, FR-08) | 2 | 🔲 [open] | 020 | 020 done | — (.claude) | FULL · opus/high |
| 022 | project-defer-issue-tracking hygiene (FR-07, FR-09, FR-10) | 2 | 🔲 [open] | 020, 021 | 021 done | — (.claude) | STANDARD · sonnet/high |
| 023 | task-create issues-in-files; 090 disposition; Board views (FR-11, FR-12) | 2 | 🔲 [open] | 020, 022 | 022 done | P2-A (main session) | STANDARD · sonnet/high |
| 024 | Triage verification on a labelled set (SC 4) | 2 | 🔲 [open] | 021, 022 | 022 done | P2-A | STANDARD · sonnet/high |
| 025 | Messaging re-measure, 28 days after phase 2 | 2 | 🔲 [open] | 005, 024 | phase 2 merged + 28 days | — | MINIMAL · sonnet/medium |
| 030 | Registry frontmatter parser + validator (FR-13) | 3 | 🔲 [open] | 005 | **phase 0 passed** | — | STANDARD · sonnet/high |
| 031 | Lifecycle transitions in skills + template (FR-13) | 3 | 🔲 [open] | 030 | 030 done | P3-A (main session) | STANDARD · sonnet/high |
| 032 | Bootstrap frontmatter for active projects (FR-13) | 3 | 🔲 [open] | 030 | 030 done | P3-A | STANDARD · sonnet/high |
| 033 | Coordination detector, signals 1–2 (+3 flagged) (FR-14) | 3 | 🔲 [open] | 030 | 030 done | P3-A | FULL · sonnet/xhigh |
| 034 | Detector invocation: nightly, task-create, pre-PR, design-to-spec (FR-15) | 3 | 🔲 [open] | 031, 032, 033 | P3-A done | — (.claude) | STANDARD · sonnet/high |
| 035 | Report-only checkpoint (owner decisions) | 3 | 🔲 [open] | 034 | ≥ 7 nightly reports | — | MINIMAL · sonnet/medium |
| 040 | Pre-code coordination issues (FR-17) | 4 | 🔲 [open] | 035 | owner "surface" at 035 | P4-A | FULL · sonnet/xhigh |
| 041 | SessionStart coordination banner (FR-18) | 4 | 🔲 [open] | 035 | owner "surface" at 035 | P4-A | FULL · sonnet/high |
| 042 | Optional live inbox notice (FR-18b) | 4 | 🔲 [open] | 041 | owner wants it | — | STANDARD · sonnet/high |
| 050 | task-execute Step 0 items, Step 9 diff check (FR-20, FR-21) | 5 | 🔲 [open] | 040 | 040 done | P5-A (main session) | FULL · opus/high |
| 051 | Router critical-area `ack` (FR-22) | 5 | 🔲 [open] | 035 | owner approval (D-1) before merge | P5-A | FULL · opus/high |
| 052 | /conflict-check reuses detector (FR-16) | 5 | 🔲 [open] | 035, 050 | owner confirmed FR-16 at 035 | — (.claude) | STANDARD · sonnet/high |
| 053 | Owned-area handover at close (FR-23) | 5 | 🔲 [open] | 031, 052 | 052 done | — (.claude) | MINIMAL · sonnet/medium |
| 090 | Project wrap-up | wrap-up | 🔲 [open] | all | all others done/deferred | — | FULL · opus/high |

FR-24 (status digest) is deferred and has no task (spec, C-9).

## Phase gates

- **Phase 0 first.** Tasks 030–053 stay blocked until 005 records the owner's "go". If 005 fails, the owner decides on reverting FR-00 and whether phases 3–5 proceed.
- **Phases 1 and 2 don't touch messaging.** They may run during the 14-day phase 0 wait. Exception: 020 waits for 002, because section A records the chosen mechanism.
- **Messaging drop rule (SC 10).** A drop or shift measured at 005, 025 or 090 blocks the next phase until the wording that caused it is found and removed.

## Parallel Execution Groups

Tasks in the same group can run at the same time once prerequisites are met. A "main session" task touches `.claude/` (root CLAUDE.md §3), so it runs in the main session while the group's other tasks run as subagents.

| Group | Tasks | Prerequisite | Files Touched | Safe to Parallelize |
|-------|-------|--------------|---------------|---------------------|
| P0-A | 001, 002 | none | `scripts/quality/measure-*` vs `notes/naming-spike-*` | ✅ Yes |
| P1-A | 010, 014 (main) | none | `scripts/LiveBuildGuard.psm1` vs `scripts/quality/guard-raw-*`, `.claude/settings.json` | ✅ Yes (014 in main session) |
| P1-B | 011, 012, 013 | 010 ✅ | Separate deploy scripts and the workflow | ✅ Yes |
| P2-A | 023 (main), 024 | 022 ✅ | `task-create/SKILL.md` vs a notes file | ✅ Yes (023 in main session) |
| P3-A | 031 (main), 032, 033 | 030 ✅ | Skills/template vs project READMEs vs detector | ✅ Yes (031 in main session) |
| P4-A | 040, 041 | 035 ✅ | Sync script + nightly job vs banner script | ✅ Yes |
| P5-A | 050 (main), 051 | 040 ✅ / 035 ✅ | `task-execute/SKILL.md` vs `ci-router.yml` + ack script | ✅ Yes (050 in main session) |

**Sequential (main session, `.claude/`)**: 003 → (004) · 014 → 015 · 020 → 021 → 022 → 023 · 034 · 052 → 053.

## Parallel Execution Plan

```
Phase 0 + 1 + 2 (phase 0 wait overlaps phases 1–2):
  Wave 1 (parallel): 001, 002 [P0-A] + 010 (subagent), 014 (main) [P1-A] — goal-eligible: NO (002 owner-interactive; 014 .claude)
  Wave 2: 003 (main) ‖ 011, 012, 013 [P1-B] — goal-eligible: NO (deploy-touching)
  Wave 3: 004 (owner) ‖ 015 (main) ‖ 020 (main, after 002) — goal-eligible: NO
  Wave 4: 016 (owner OK) ‖ 021 → 022 (main) — goal-eligible: NO (live dev; .claude)
  Wave 5: 023 (main) ‖ 024 [P2-A] — goal-eligible: NO (two tasks)
  Wait: 005 at applied date + 14 days; 025 at phase 2 merge + 28 days
Phase 3 (after 005 "go"):
  Wave 6: 030 — goal-eligible: NO (single task)
  Wave 7: 031 (main) ‖ 032, 033 [P3-A] — goal-eligible: NO (032 owner review; .claude)
  Wave 8: 034 (main) — NO (.claude)
  Wait: ≥ 7 nightly reports, then 035 (owner decisions)
Phase 4:
  Wave 9: 040, 041 [P4-A] — goal-eligible: NO (two tasks)
  Wave 10: 042 (optional)
Phase 5:
  Wave 11: 050 (main) ‖ 051 [P5-A] — NO (Router: owner approval before merge)
  Wave 12: 052 → 053 (main)
Wrap-up: 090
```

No wave is `/goal`-eligible. Every wave has an owner step, a `.claude/` edit, a deploy, or fewer than three tasks (task-create Step 3.85).

## Critical path

`001 ‖ 002 → 003 → 004 → (14 days) → 005 → 030 → 033 → 034 → (report period) → 035 → 040 → 050 → 052 → 053 → 090`
