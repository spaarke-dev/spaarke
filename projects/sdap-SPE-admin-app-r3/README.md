# sdap-SPE-admin-app-r3

**Decompose `Infrastructure/Graph/SpeAdminGraphService.cs`.**

> **Status**: seeded 2026-08-31 — `design.md` only. `/design-to-spec` has **not** been run.
> Execution is operator-gated. No worktree yet.

---

## What this is

The objective r1/r2 were originally chartered for. r2 correctly redirected to making the SPE Admin app
work — 4 of 9 screens failed and 1 failed silently — but the decomposition was deferred to a project
named `speadmingraphservice-decomposition-r1` that **was never created**.

This folder is the correction.

## The numbers

| | Lines | Public methods |
|---|---|---|
| r2 start (2026-08-20) | 4,320 | — |
| master (2026-08-31) | **6,545** | **168** |
| Delta | **+2,225 (+52%)** | |

111 public async methods across **nine** domains. The case is **cohesion, not line count** — nine
reasons to change in one type. See [`design.md`](design.md) §2.

## Two constraints to read before planning

1. 🔴 **CI must not gate on this file** (operator, 2026-08-31). No LOC gate, no re-instated
   `GodClassGuardTests`, no wiring `report-large-server-files.ps1` into CI. `design.md` §7.
2. **Zero behaviour change.** A contract test that needs editing to accommodate the refactor is
   evidence the refactor changed behaviour. `design.md` §8.

## Why now is safer than at r2 planning time

Every one of the nine domains now has contract tests pinning its actual Graph wire shape, written
after the defect they guard. Decomposing at r2 planning time would have refactored code that
fabricated settings values, silently discarded custom properties, and could not create a container
type at all. `design.md` §4.

## Next step

```
/design-to-spec projects/sdap-SPE-admin-app-r3
```

Four open questions are listed in `design.md` §9 and should be resolved during that pass — in
particular where the shared helpers live, since the wrong answer recreates a smaller god file.

## Predecessor

[`sdap-SPE-admin-app-r2`](../sdap-SPE-admin-app-r2/) — merged (PRs #859, #907), wrap-up not yet run.
Items that transfer here **only if** r2's 090 leaves them are listed in `design.md` §6.
