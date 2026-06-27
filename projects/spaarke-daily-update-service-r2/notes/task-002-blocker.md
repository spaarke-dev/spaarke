# Task 002 — Blocked: Wiring seam not yet present in shared lib + LegalWorkspace shim

**Date**: 2026-06-18
**Author**: task-execute agent (Wave 2 / orchestrator-spawned)
**Task**: 002 — Wire `loadSpaarkeAiNotificationContext` injection in SpaarkeAi `main.tsx`
**Status**: BLOCKED — cannot complete within file-overlap constraints (main.tsx only)

---

## Summary

Task 002's POML and spec FR-02 require `SpaarkeAi/main.tsx` to inject `loadSpaarkeAiNotificationContext` such that the embedded Daily Briefing section calls it on cold-load. The current code architecture does NOT have a seam by which `main.tsx` can do this without ALSO modifying the LegalWorkspace Daily Briefing shim — which the Wave 2 orchestrator explicitly forbids per the file-overlap guarantee.

---

## Evidence — current data flow

```
SpaarkeAi main.tsx
  └── setDefaultWorkspaceRenderer(LegalWorkspaceRenderer)   ← module slot, ONE renderer per host
                                                              └── LegalWorkspaceApp
                                                                    └── SECTION_REGISTRY
                                                                          └── dailyBriefingRegistration  ← STATIC shape, hand-rolled, no factory
                                                                                └── DailyBriefingSection (LegalWorkspace shim)
                                                                                      └── DailyBriefingSectionShared (no loadNotificationContext prop)
```

Key files:
- `src/solutions/SpaarkeAi/src/main.tsx` — bootstraps + calls `setDefaultWorkspaceRenderer(LegalWorkspaceRenderer)`; does NOT import `loadSpaarkeAiNotificationContext`; no factory invocation exists for Daily Briefing.
- `src/solutions/LegalWorkspace/src/sections/dailyBriefing/dailyBriefing.registration.ts` — **STATIC** `SectionRegistration` (does NOT call `createDailyBriefingRegistration`); renders `DailyBriefingSection` (local) without any `loadNotificationContext` prop.
- `src/solutions/LegalWorkspace/src/sections/dailyBriefing/DailyBriefingSection.tsx` — local shim wrapping `DailyBriefingSectionShared`; does NOT forward `loadNotificationContext`.
- `src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/sections/dailyBriefing/dailyBriefing.registration.ts` — exports `createDailyBriefingRegistration({ ..., loadNotificationContext })` factory (added in task 086 / Round 4 Fix 3, per file header). **Never invoked by any consumer.**

The factory `createDailyBriefingRegistration` (with the `loadNotificationContext` option) was added by a prior project but the LegalWorkspace consumer never adopted it — it still uses the pre-069 static shape, which loses the loader option entirely.

---

## Why main.tsx alone cannot fix this

There are three architecturally possible seams:

1. **Factory-style** (the option the registration file documents):
   `main.tsx` calls `createDailyBriefingRegistration({ ..., loadNotificationContext: loadSpaarkeAiNotificationContext })` and somehow gets that into the registry.
   - Problem: no registry-mutation API exists. `LegalWorkspaceApp`'s `SECTION_REGISTRY` is a closed array inside the LegalWorkspace tree.

2. **Module-slot setter** (the option design.md line 130 anticipates):
   `setDailyBriefingNotificationLoader(loadSpaarkeAiNotificationContext)` exported from `@spaarke/ui-components` (mirroring `setDefaultWorkspaceRenderer`).
   - Problem: this setter does NOT exist in the shared lib. Calling it from `main.tsx` would fail compilation.

3. **LegalWorkspace shim modification**:
   Update `LegalWorkspace/src/sections/dailyBriefing/dailyBriefing.registration.ts` to call `createDailyBriefingRegistration({ ..., loadNotificationContext: globalLoader })` where `globalLoader` is read from a module slot SpaarkeAi sets at bootstrap.
   - Problem: orchestrator forbids modifying LegalWorkspace files in this task.

None of the three is achievable by modifying `main.tsx` alone. Writing code in `main.tsx` that imports `loadSpaarkeAiNotificationContext` and calls a non-existent setter would break the build.

---

## What task 001 (no-op) actually verified

Task 001 (per current-task.md) was correctly reported as `completed-no-op` because the factory's `loadNotificationContext?` parameter was already added by a prior project. But task 001's no-op verification was at the **shared-lib factory level** — it did NOT verify any consumer was using the factory. The LegalWorkspace shim still uses the static, pre-factory shape.

So FR-01 is met at the contract level (the option exists), but FR-02's data flow precondition (a consumer that actually invokes the factory with the option) is not met.

---

## Recommended re-sequencing

This block disappears once any ONE of the following lands:

**Option A** (smallest scope, fits R2 task-018 already in plan):
Tasks 011-018 hoist the Daily Briefing into `@spaarke/daily-briefing-components`. Task 018 replaces the LegalWorkspace static registration with a thin shim that calls the new package's factory, which accepts `loadNotificationContext` as a factory option. SpaarkeAi can then either:
- Use a setter exported by the new package, or
- Compose the shared `DailyBriefingApp` directly in its workspace tree (Pattern D dual-use).

**Option B** (smallest immediate change, NOT currently in plan):
Add `setDailyBriefingNotificationLoader` / `getDailyBriefingNotificationLoader` module slot to `@spaarke/ui-components` (mirrors `setDefaultWorkspaceRenderer`). Modify the LegalWorkspace shim to call `createDailyBriefingRegistration({ ..., loadNotificationContext: getDailyBriefingNotificationLoader() })`. Then `main.tsx` can call the setter. Two files touched in shared lib + LegalWorkspace; ~30 LOC total.

Option A is cleaner and already on the critical path. Option B is faster if the operator wants the bullets visible before P2 hoist completes.

---

## Recommendation to orchestrator

Mark task 002 as **blocked** in TASK-INDEX.md. Re-sequence: re-run task 002 AFTER task 018 (LegalWorkspace shim replaced) completes. At that point task 002 becomes a 5-line edit in `main.tsx` — import the loader, pass it to the shim's setter or factory configurator.

Alternatively, the operator may choose Option B as a tactical fix that unblocks task 003 (P1 smoke verification) sooner. That would require a small spike outside the file-overlap constraints of this task.

---

## Build baseline

`npm run build` on `src/solutions/SpaarkeAi/` **fails** at HEAD due to pre-existing TypeScript errors:
- `src/services/__tests__/insightsQueryClient.test.ts` — missing `@types/jest`
- `src/telemetry/__tests__/errorTelemetry.test.ts` — missing `@types/jest`
- `src/telemetry/errorTelemetry.ts(30,42)` — Cannot find module `@microsoft/applicationinsights-web` (despite being in package.json dependencies; suggests missing node_modules or upstream package resolution issue)
- Shared libs: 2557 errors ignored by tsc-surface-gate

These are pre-existing on the `work/spaarke-daily-update-service-r2` HEAD (`9f7501120`) before this task ran. No code modified during this task. Build state matches baseline.

---

*Filed by Wave 2 task-execute agent. Orchestrator: please re-sequence per recommendation.*
