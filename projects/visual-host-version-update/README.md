# VisualHost Decoupling & `@spaarke/visuals` Extraction

> **Portfolio**: [Project #641](https://github.com/spaarke-dev/spaarke/issues/641) · Parent [Epic #535 — ENTITY FUNCTIONALITY](https://github.com/spaarke-dev/spaarke/issues/535) · Board [Project #2](https://github.com/users/spaarke-dev/projects/2)
> **Status**: ✅ Completed 2026-07-13 (merged via PR #639 + #640; v1.4.37 UAT-passed)
> **Branch**: `work/visual-host-version-update`
> **Created**: 2026-07-10

## What & Why

VisualHost is the only PCF that consumes the shared library by bundling its raw `src/`. That causes (a) a build/test conflict (transitive-dep leakage + React 16/17-vs-19 skew) and (b) traps VisualHost's reusable visuals in the PCF, so five other surfaces re-implement the same visual vocabulary. This project:

- **Phase A**: switches the "+" Create button to launch wizards via `Xrm.Navigation.navigateTo` (the established Spaarke wizard-modal pattern) — decoupling the inbound dependency and shipping the pending `cleanGuid` fix first.
- **Phase B**: extracts the visuals into a new canonical **`@spaarke/visuals`** sibling package and amends ADR-012 to sanction it.

Full context: [`design.md`](design.md) · [`spec.md`](spec.md). Original narrow framing (superseded): [`ASSESSMENT.md`](ASSESSMENT.md).

## Graduation Criteria (project is "done" when)

1. `cleanGuid` shipped to VisualHost's "+" (braced-GUID create no longer 400s) — dev.
2. Clean-worktree `build:prod` green with no undeclared-dep failures.
3. "+" launches Event/Invoice/Report Card via `navigateTo`; no shared-lib-`src` wizard imports; React-skew casts removed.
4. Regarding resolver + field mapping fire from the "+" for all three entities.
5. Drill-through/expand unchanged, config-driven.
6. `@spaarke/visuals` exists; VisualHost consumes from it; bundle ≈1.27 MiB.
7. Drifted duplication reconciled (one `VisualType`, one `EventDueDateCard`).
8. ADR-012 amendment merged.

## Hot-Path Declaration
BFF: N · SpaarkeAi: N · ci-workflows: N · skill-directives: N · root-CLAUDE.md: N
Shared-surface: modifies `@spaarke/ui-components` `package.json` + adds `Spaarke.Visuals` + wizard code pages — coordinate with PR #508.

## Out of Scope
Adoption of `@spaarke/visuals` by other surfaces (deferred); broader toolbar consolidation (separate initiative); bundling the shared lib; BFF/server changes; prod promotion.

## Key Artifacts
- [`plan.md`](plan.md) — phased WBS
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) — task registry
- [`current-task.md`](current-task.md) — active task state
