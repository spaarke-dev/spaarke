# D-02 — deploy-promote.yml artifact contract verified intact (Wave A claim corrected)

> **Date**: 2026-06-01
> **Task**: 010 — Fix deploy-promote.yml cascade
> **Status**: Accepted
> **Author**: task-execute agent (Wave B)

---

## Context

The Wave A workflow inventory (`projects/github-actions-rationalization-r1/baseline/workflow-inventory-2026-06-01.md`, deploy-promote.yml row, line 161) asserted:

> "the workflow expects an artifact `deployment-packages` from sdap-ci, but sdap-ci.yml does NOT produce that artifact name (it produces `test-results-*` and `coverage-reports-*`). So even if loader-failure is fixed, the artifact contract is broken."

Task 010 verified this claim against the actual `sdap-ci.yml` and found it to be **incorrect**.

## Finding

`sdap-ci.yml` DOES produce an artifact named `deployment-packages`. See `.github/workflows/sdap-ci.yml` lines 211–238 (`integration-readiness` job):

```yaml
integration-readiness:
  name: Integration Readiness
  ...
  steps:
    ...
    - name: Package artifacts
      uses: actions/upload-artifact@v6
      with:
        name: deployment-packages
        path: ./publish/**
        retention-days: 30
```

The artifacts the inventory noted (`test-results-*`, `coverage-reports-*`) are uploaded by the `build-test` job (lines 90–101) — additional artifacts, not replacements. `deployment-packages` exists alongside them.

`deploy-promote.yml` downloads `deployment-packages` in three jobs (deploy-dev, deploy-staging, deploy-prod). The artifact contract between the two workflows is **intact and correct**.

## Decision

**No artifact-name change is required.** Task 010's fix is limited to the P2 cascade repair (gate the `summary` job on the same trigger-conclusion filter as the `plan` job).

The Wave A inventory document should be updated in a future doc-drift pass (NOT in this task — out of scope for task 010, per NFR-01-style scope discipline) to remove the incorrect artifact-contract assertion. Alternatively, this decision record stands as the corrective record.

## Implications

- `deploy-promote.yml` IS functional once the cascade is fixed (no second underlying bug).
- The inventory's recommendation "FIX" (not "DELETE") remains correct.
- FR-03 acceptance: a deliberately-failing SDAP CI run on master will produce a deploy-promote run with conclusion `skipped` (all jobs gated on `workflow_run.conclusion == 'success'`), not `failure`.

## Cross-references

- Task: `projects/github-actions-rationalization-r1/tasks/010-fix-deploy-promote-cascade.poml`
- Workflow: `.github/workflows/deploy-promote.yml`
- Upstream workflow: `.github/workflows/sdap-ci.yml` (line 236 — `name: deployment-packages`)
- Wave A inventory entry to correct: `projects/.../baseline/workflow-inventory-2026-06-01.md` line 161
