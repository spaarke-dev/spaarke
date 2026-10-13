# sdap-SPE-admin-app-r3

> **Portfolio**: [Project #1192](https://github.com/spaarke-dev/spaarke/issues/1192) · [Board #2](https://github.com/users/spaarke-dev/projects/2) — _auto-registered 2026-10-04 (existence only; Epic/Task Count/Start Date not yet set)_

**Make the SPE container Graph code a first-class product component, then decompose `SpeAdminGraphService.cs`.**

> **Status**: seeded 2026-08-31; **re-scoped 2026-10-07** — read [`design.md` §0](design.md) first.
> `/design-to-spec` has **not** been run. Execution is operator-gated. No worktree yet.

## 🔑 Scope as of 2026-10-07 (design.md §0)

| Phase | What | Status |
|---|---|---|
| **1a** | Extract the helpers core product code now calls (business-unit stamp/binding, custom properties, raw Graph request, base URL, retry) into a **first-class SPE container component**; product binds to it **directly** | Do first |
| **1b** | Split the rest of `SpeAdminGraphService` into ~5 per-area files (`partial class`) — no behaviour change | In scope |
| **2** | Full per-area services | **Gated**: consumer analysis + uac-r2 coordination → explicit go / no-go recorded in design.md |

Why re-scoped: uac-r2 made core product paths (container creation, external access) depend on helpers
inside this admin-tool class. Usage (dev, 30 d): SPE Admin endpoints **341 calls / 1 user**; product SPE
container Graph calls **4,225, every day**.

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
| master (2026-10-07) | **7,338** | — |
| Delta since r2 start | **+3,018 (+70%)** | |

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

The four §9 questions were **answered 2026-10-07** (design.md §0.3). 🔔 Coordinate with **uac-r2** before
Phase 1a — it moves helpers that project's code calls (design.md §0.5).

## Predecessor

[`sdap-SPE-admin-app-r2`](../sdap-SPE-admin-app-r2/) — code complete and live (incl. the secret-free managed-identity
change, PR #1291); wrap-up (090) held for UAT. r2 deliberately does **not** take Phase 1 (design.md §0.4).
Items that transfer here **only if** r2's 090 leaves them are listed in `design.md` §6.
