# task-create — Worked Examples

> **Parent skill**: [task-create](../SKILL.md)
> **Extracted**: 2026-05-17 from SKILL.md by ai-procedure-quality-r1 (Phase 2b Wave 2c)

Three worked examples showing how task-create decomposes different plan-md inputs into POML task files.

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
