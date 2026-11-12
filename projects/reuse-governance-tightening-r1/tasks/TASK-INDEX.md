# Task Index — Reuse Governance Tightening R1

> Status tokens are mandatory in every status cell (task-create Step 5). After any status write: `pwsh scripts/check-task-status-drift.ps1`.

| ID | Title | Phase | Status | Dependencies | Parallel | Rigor | Model / effort |
|----|-------|-------|--------|--------------|----------|-------|----------------|
| 001 | Build the capability index | 1 | ✅ [done] | none | — (.claude, main) | STANDARD | opus / high |
| 002 | Wire the capability index into the reuse gate | 1 | ✅ [done] | 001 | — (.claude, main) | STANDARD | sonnet / high |
| 010 | Reuse-scan dry-run and owner go/no-go | 2 | 🔲 [open] | 001 | — (main, owner gate) | STANDARD | opus / high |
| 011 | Add the reuse scan to design-to-spec Step 3 (if go) | 2 | 🔲 [open] | 010 | — (.claude, main) | STANDARD | opus / high |
| 012 | Pipeline Step 2 scan fallback and task-create citation (if go) | 2 | 🔲 [open] | 011 | — (.claude, main) | STANDARD | sonnet / high |
| 020 | Weekly jscpd duplication sweep in nightly-health | 3 | 🔲 [open] | 001, 041 | Group B | STANDARD | sonnet / high |
| 021 | Reviewer checks: capability duplication (F2) and requirements trace (F4) | 3 | 🔲 [open] | 001 | — (.claude, main) | STANDARD | opus / high |
| 030 | Decision records for the 9 grandfathered packages | 4 | 🔲 [open] | none | — (main, owner gate) | STANDARD | opus / high |
| 031 | Execute the decision for @spaarke/ai-context | 4 | 🔲 [open] | 030 | — (.claude + shared file) | FULL | sonnet / high |
| 032 | Execute the decision for @spaarke/legal-workspace | 4 | 🔲 [open] | 031 | — (.claude + shared file) | FULL | sonnet / high |
| 033 | Execute the decision for @spaarke/document-operations | 4 | 🔲 [open] | 032 | — (.claude + shared file) | FULL | opus / high |
| 034 | File the six remaining package actions and report defects | 4 | 🔲 [open] | 030 | Group C | MINIMAL | sonnet / medium |
| 035 | Deploy SpaarkeAi and SemanticSearch to DEV and smoke-test | 4 | 🔲 [open] | 031, 032, 033 | — (deploy, owner OK) | STANDARD | sonnet / high |
| 040 | 5(a) Microsoft Learn MCP server for the researcher | 5 | 🔲 [open] | none | — (.claude, main) | STANDARD | sonnet / high |
| 041 | 5(c) Knowledge-refresh staleness watchdog | 5 | ✅ [done] | none | Group A | STANDARD | sonnet / high |
| 042 | 5(e) Re-arm the CS0618 obsolete-API check | 5 | ✅ [done] | none | Group A | FULL | opus / high |
| 043 | 5(f) Per-session C# language-server A/B trial | 5 | 🔲 [open] | none | — (operator, machine load) | STANDARD | sonnet / high |
| 080 | Adversarial review of items 1-4 and their PRs | 6 | 🔲 [open] | 002, 012, 020, 021, 033, 034, 035 | — (.claude, main) | FULL | opus / high |
| 090 | Project wrap-up | 6 | 🔲 [open] | 080, 040, 041, 042, 043 | — (final) | STANDARD | sonnet / high |

**Total**: 19 tasks (FULL 5 · STANDARD 13 · MINIMAL 1). Tasks 011 and 012 are cancelled if task 010 records no-go.

## Owner gates

- **010** — dry-run go/no-go (and the `medium`-effort cost decision if one duplicate is missed).
- **030** — approval of each package decision before 031–034.
- **035** — explicit OK before the DEV deploy.
- **PRs** — the owner's OK before each PR is opened (040, 041, 042, 043, 080).

## Critical path

001 → 010 → 011 → 012 → 080 → 090, and 030 → 031 → 032 → 033 → 035 → 080. Phase 5 tasks are off the critical path.

## High-risk items

- **033** `document-operations` consolidation (3 consumers, one a shared package; ADR-022 if it moves into `ui-components`).
- **042** re-arming `CS0618` (unknown number of hidden obsoletions; escalation at > 25 errors).
- **Coordination**: PR #935 (`code-quality-and-assurance-r4`) edits ADR-012 and `SharedPackageCensusTests`; PR #909 edits `nightly-health.yml`.

## Parallel Execution Plan

```
Wave 1:
  Main session (sequential): 001 → 002 → 010 (owner gate) → 021 → 040
  Sub-agents (parallel, 2 agents): 041, 042 — Group A — goal-eligible: NO (2-task wave; 042 is high-risk build config)
Wave 2 (prereq 001, 041):
  Sub-agent: 020 — Group B — goal-eligible: NO (1-task wave)
  Main session (sequential): 011 → 012 (only if 010 is go), then 030 (owner gate)
Wave 3 (prereq 030 approved):
  Main session (sequential): 031 → 032 → 033 — goal-eligible: NO (touches .claude/, owner-approved architecture changes)
  Sub-agent: 034 — Group C — goal-eligible: NO (1-task wave)
Wave 4: 035 (deploy, owner OK) — goal-eligible: NO (deploy)
Any time, with the operator: 043 — goal-eligible: NO (exploratory measurement)
Wave 5: 080 (main) — goal-eligible: NO (review judgment)
Wave 6: 090 (main) — goal-eligible: NO (single task)
```

## Parallel Execution Groups

Tasks in the same group can run simultaneously once prerequisites are met.

| Group | Tasks | Prerequisite | Files Touched | Safe to Parallelize |
|-------|-------|--------------|---------------|---------------------|
| A | 041, 042 | none | 041: `nightly-health.yml`, `REFRESH-PROCEDURE.md`, `ci-cd-workflow.md` · 042: `Directory.Build.props`, C# callers | ✅ Yes |
| B | 020 | 001, 041 ✅ | `.jscpd.json`, `nightly-health.yml`, `ci-cd-workflow.md` | ✅ Yes (alongside main-session work) |
| C | 034 | 030 ✅ | `notes/defer-issues.md`, `notes/package-reviews.md`, GitHub issues | ✅ Yes (alongside 031) |

Tasks marked "— (.claude, main)" run in the main session only: sub-agents cannot write to `.claude/` (root CLAUDE.md §3).

**How to execute a group:** confirm all prerequisites are complete → invoke the Agent tool with one task-execute subagent per task in ONE message → wait for all to finish before the next group.
