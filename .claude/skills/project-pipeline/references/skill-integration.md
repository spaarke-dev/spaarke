# project-pipeline — how it uses the component skills

Background for maintainers and for anyone changing a component skill's contract. It is not needed to run the pipeline. `SKILL.md` names the component skill at each step where it runs.

## Contents

- Component skills and where each runs
- Orchestration pattern
- Why call component skills
- current-task.md lifecycle
- Portfolio hook history

## Component skills and where each runs

| Skill | Relationship | Step |
|---|---|---|
| `design-to-spec` | Optional predecessor: turns a human design doc into spec.md before this skill runs | before 0 |
| `merge-to-master` | Called in audit mode for the master staleness check | 0.5 |
| `conflict-check` | Same overlap logic; the pipeline runs it at project level, `task-execute` Step 0.5 per task | 1.5 |
| `adr-aware` | Loads full ADRs during resource discovery | 2 |
| `script-aware` | Finds existing scripts to cite in tasks | 2 |
| `project-setup` | Called to generate README, plan, CLAUDE.md, current-task.md, folders | 2 Part 2 |
| `task-create` | Called to generate the POML tasks and TASK-INDEX.md; authoritative for the field set | 3 |
| `ui-test` | Step 3 adds `<ui-tests>` to PCF/frontend tasks; task-execute Step 9.7 runs them | 3, 5 |
| `push-to-github` | Git conventions for the branch and initial commit | 4 |
| `devops-project-register` / `devops-project-sync` | Registration on Board #2 | 4.2 |
| `task-execute` | Runs each task, in waves | 5 |

## Orchestration pattern

```
design-to-spec (optional predecessor)
  └─→ design.md → spec.md
        │
        ▼
project-pipeline (orchestrator)
  ├─→ Steps 0–1.7: pre-flight, staleness, spec validation, overlap, ADR tensions
  ├─→ Step 2: discovery, then CALLS project-setup (artifacts)
  ├─→ Step 3: CALLS task-create (POMLs, <ui-tests>, TASK-INDEX.md)
  ├─→ Step 4: project branch + initial commit
  ├─→ Step 4.2: CALLS devops-project-register / devops-project-sync
  └─→ Step 5: CALLS task-execute per task, in parallel waves
        └─→ task-execute Step 9.7: CALLS ui-test for PCF/frontend tasks
```

## Why call component skills

- Single responsibility: each skill does one thing well.
- Reuse: project-setup and task-create also run standalone or under other orchestrators.
- Maintainability: a change to artifact or task generation happens in one place.
- Testability: component skills can be checked independently.

The consequence for maintainers: when a component skill's contract changes (for example a new required POML field), update the component skill. Update this skill only where the pipeline consumes the change (Step 3's field list and Step 5's dispatch).

## current-task.md lifecycle

- project-setup creates it with status "none" (no active task yet).
- When task 001 starts: status "in-progress"; steps, files and decisions are tracked.
- When task 001 completes: it resets and advances to task 002 (status "not-started").
- At project completion: status "none", next action "run /repo-cleanup".

It is state, not history (root CLAUDE.md §5); `context-handoff` owns its format.

## Portfolio hook history

Added 2026-06-23 by spaarke-devops-project-tracking-r1 (task 031, FR-17) as an appendix prompt, "Register this project on the portfolio? [Y/n]". Failures only warned, and projects were missed: on 2026-10-15, 16 project folders had no Project Issue. It became the mandatory Step 4.2 on 2026-10-15 (PR #1983). On a re-run for an already registered project, Step 4.2 only invokes `/devops-project-sync`, which is silent on success; a sync failure warns and does not block (NFR-03).
