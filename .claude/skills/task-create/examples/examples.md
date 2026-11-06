# task-create — Worked Examples

> **Parent skill**: [task-create](../SKILL.md)
> **Extracted**: 2026-05-17 from SKILL.md by ai-procedure-quality-r1 (Phase 2b Wave 2c); Examples 4–5 added 2026-11-03 (skill-authoring-standard restructure)
> **Last Reviewed**: 2026-11-03

## Contents

- Example 1 — decomposing a 4-phase plan into 14 POML files
- Example 2 — fine-grained decomposition
- Example 3 — a plan.md with no WBS (graceful stop)
- Example 4 — phase layout with deployment tasks (Step 3.6)
- Example 5 — the Step 6 output summary in full
- Example 6 — `<rigor-reason>` and `<model-tier-reason>` values (Steps 3.5.5, 3.5.5b)

## Example 1: Decompose SDAP Refactor Plan

**Trigger**: "/task-create sdap-refactor"

**Input plan.md phases**:
```
Phase 1: Assessment (2 tasks)
Phase 2: API Restructure (5 tasks)
Phase 3: Worker Migration (4 tasks)
Phase 4: Testing (3 tasks)
```

**Output**:
```
tasks/
├── TASK-INDEX.md
├── 001-inventory-endpoints.poml
├── 002-document-dependencies.poml
├── 010-create-minimal-api-structure.poml
├── 011-migrate-policy-endpoints.poml
├── 012-migrate-document-endpoints.poml
├── 013-migrate-sync-endpoints.poml
├── 014-implement-auth-filters.poml
├── 020-extract-sync-worker.poml
├── 021-extract-notification-worker.poml
├── 022-implement-job-queue.poml
├── 023-configure-worker-hosting.poml
├── 030-unit-tests.poml
├── 031-integration-tests.poml
└── 032-e2e-validation.poml
```

(A current run would also add the Step 3.6 deployment tasks and the mandatory Step 3.7 wrap-up task, `090-project-wrap-up.poml`.)

## Example 2: Fine-Grained Decomposition

**Trigger**: "Create tasks for redis-caching project with fine granularity"

**Result**: 15 tasks instead of 8, each ~1-2 hours

## Example 3: Handle Missing Dependencies

**Trigger**: "/task-create my-project"

**If plan.md lacks WBS**:
```
⚠️ Cannot create tasks: plan.md missing work breakdown structure

Required in plan.md Section 5:
  - At least one phase with name and description
  - Deliverables or outcomes for each phase

Would you like me to help complete the plan first?
```

## Example 4: Phase Layout With Deployment Tasks (Step 3.6)

Deployment tasks sit at the end of the phase that produces the deliverable; post-deployment integration tests follow all deployment tasks; the wrap-up task is last.

```
Phase 1: Implementation
  001-010: Build features
  009: Unit tests
Phase 1-Deploy:
  015: Deploy BFF API to Azure
  016: Deploy PCF to Dataverse
  017: Integration tests (post-deployment)
Phase 2: Next feature set...
...
090: Project wrap-up
```

## Example 5: Step 6 Output Summary

```
✅ Tasks created for: projects/{project-name}/

Task breakdown:
  Phase 1: {n} tasks (001-00{n})
  Phase 2: {m} tasks (010-01{m})
  ...
  Wrap-up: 1 task (090-project-wrap-up)
  Total: {total} tasks

Rigor level distribution:
  FULL: {count} tasks (code implementation, architecture changes)
  STANDARD: {count} tasks (tests, new files, constraints)
  MINIMAL: {count} tasks (documentation, inventory)

Task list with rigor levels:
  001 - {title} (FULL - {reason})
  002 - {title} (STANDARD - {reason})
  003 - {title} (MINIMAL - {reason})
  ...
  090 - Project Wrap-up (FULL - code-review + adr-check + repo-cleanup)

Files created:
  - tasks/TASK-INDEX.md
  - tasks/001-{slug}.poml
  - tasks/002-{slug}.poml
  ...
  - tasks/090-project-wrap-up.poml  # MANDATORY final task

Execution order recommendation:
  1. Start with task 001 (no dependencies)
  2. ...
  N. End with task 090 (project-wrap-up) - updates README, plan, documents completion

Run /task-execute 001 to begin implementation.
```

## Example 6: Field-Value Examples (Steps 3.5.5, 3.5.5b)

Each reason names the specific trigger from the step's decision rule.

```xml
<!-- Step 3.5.5 — rigor -->
<rigor>FULL</rigor>
<rigor-reason>Task tags include 'bff-api' (code implementation)</rigor-reason>

<rigor>STANDARD</rigor>
<rigor-reason>Task tags include 'testing', 'integration-test'</rigor-reason>

<rigor>MINIMAL</rigor>
<rigor-reason>Documentation task (no code implementation)</rigor-reason>

<!-- Step 3.5.5b — model tier -->
<model-tier>opus</model-tier>
<model-tier-reason>Cross-cutting migration of 4 wizard families onto a shared module (high blast radius)</model-tier-reason>

<model-tier>opus</model-tier>
<model-tier-reason>ADR-024 resolver-compliance migration of an existing non-compliant wizard</model-tier-reason>

<model-tier>sonnet</model-tier>
<model-tier-reason>New single-component wizard following the canonical workAssignmentService.ts reference</model-tier-reason>
```
