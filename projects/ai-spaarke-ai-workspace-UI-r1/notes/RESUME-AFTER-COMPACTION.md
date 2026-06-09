# RESUME AFTER COMPACTION — ai-spaarke-ai-workspace-UI-r1

> **Date saved**: 2026-06-09 — UPDATED after Phase A + B completion
> **Active branch**: `feature/ai-spaarke-ai-workspace-UI-r1`
> **Active PR**: #372
> **Last commit on branch**: `1240fd65` (Phase B — AppErrorBoundary + safeRegister, pushed)
> **Working baseline still deployed in spaarkedev1**: `sprk_spaarkeai` web resource at commit `60b98770` (Phase A + B NOT yet deployed; rebuild + redeploy in Final step)

---

## Progress since the previous save

### Phase A — STATIC HARDENING (✅ COMPLETE)
- A.1: `tsconfig.base.json` at repo root (committed `fe4d37389`)
- A.2: 3 Code Pages extend base (committed `fe4d37389`)
- A.3: Softened `noUnusedLocals`/`noUnusedParameters` in base; fixed 3 SpaarkeAi-owned tsc errors in `ContextPaneController.test.tsx` (committed `2aeb2d98`)
- A.4: `scripts/tsc-surface-gate.mjs` — surface-scoped tsc gate (filters to `src/**` errors so shared-lib hygiene doesn't block builds). Wired into SpaarkeAi + DailyBriefing `npm run build` and `npm run typecheck`. **WLW deferred — 24 pre-existing owned errors (real type bugs in App.tsx + missing @testing-library/react types).** (committed `2aeb2d98`)
- A.5: Added `pdfjs-dist@^5.7.284` + `mammoth@^1.12.0` to SpaarkeAi's `package.json` (dynamic imports via SprkChat hook). (committed `2aeb2d98`)

### Phase B — RUNTIME RESILIENCE (✅ COMPLETE)
- B.1: `AppErrorBoundary` in `@spaarke/ui-components/src/components/AppErrorBoundary/`. Self-contained FluentProvider in fallback render path (works even if parent FluentProvider crashed). Surface, onError, fallback props. (committed `1240fd65`)
- B.2-B.3: Wrapped all 3 Code Page entry points (SpaarkeAi/main.tsx, DailyBriefing/main.tsx, WorkspaceLayoutWizard/main.tsx). WLW tsconfig also got the @spaarke/auth + @spaarke/ui-components source path mappings. (committed `1240fd65`)
- B.4: `safeRegister` helper in `@spaarke/ui-components/src/utils/`. Thin try/catch wrapper that logs + returns undefined on registration error. (committed `1240fd65`)
- B.5: Wrapped 26/28 registration calls — `register-workspace-widgets.ts` (21 calls via local `safeRegisterWidget`) + `register-context-widgets.ts` (5 calls via local `safeRegisterContext`). 3 single-call register-*.ts files NOT wrapped (no cascade risk for single-call modules). (committed `1240fd65`)

### Pending (per original plan)
- **Phase C** — Playwright smoke tests + CI integration
- **Phase D** — `createRegistry` wired into WorkspaceLayoutWidget render path + per-widget Error Boundary at WorkspaceTabManager
- **Final** — Rebuild + redeploy SpaarkeAi/WLW/DailyBriefing; verify in spaarkedev1; resume iteration 2 bisect (DataGrid `ResizeObserver`, Matters widget chain, Calendar filter-chip rewrite)

---

---

## TL;DR — what's happening

1. Project was completing iteration 2 (testing-feedback fixes) when the SpaarkeAi page blanked.
2. Root cause: `tabs.some(...)` referenced a prop that was never destructured in `WorkspacePaneMenu.tsx`. TypeScript was happy (prop existed on the interface), but vite's prod build doesn't run `tsc --noEmit`, so the bundle shipped with a runtime ReferenceError.
3. Operator decision: **stop and fix systemic brittleness first** — same gaps exist across all 50+ Spaarke client surfaces. Then resume iteration 2.
4. **The brittleness fix plan is approved (Phases A+B+C+D, ~8h estimated)** and documented in `brittleness-remediation-plan.md`. Phase A is in progress.

---

## State of working tree (at save time)

### Committed on branch (`60b98770`):
- Iteration 2 partial: `CalendarLtr24Regular/Filled` icon swap on the Calendar widget's expand/collapse button; `tabs.some(...)` + `dispatch(...)` in `handlePinToggle`; **`tabs` destructured from props** (the root-cause fix).

### Uncommitted in working tree (Phase A.1 + A.2 of brittleness plan):
- New file: `tsconfig.base.json` at repo root — shared strict TS settings.
- Modified: `src/solutions/SpaarkeAi/tsconfig.json` — now extends `tsconfig.base.json`; AI.Context/Outputs paths aligned to `/src` (was stale `/dist`).
- Modified: `src/solutions/WorkspaceLayoutWizard/tsconfig.json` — extends base.
- Modified: `src/solutions/DailyBriefing/tsconfig.json` — extends base; preserves its `jsx: react-jsx` (the other two are `jsx: react`).
- Reverted: `src/solutions/LegalWorkspace/src/sections/matters.registration.ts` (untracked rollback artifact from earlier bisect).

### Phase A.3 in progress (NOT YET TOUCHED):
Running `npx tsc --noEmit` in SpaarkeAi after the tsconfig path fix reveals **112 pre-existing errors**:
- **3 errors in SpaarkeAi-owned code** (all in `src/components/context/__tests__/ContextPaneController.test.tsx`) — fixable individually.
- **92 errors in shared libs** (mostly `noUnusedLocals` / `noUnusedParameters` warnings in `@spaarke/ai-context`, `@spaarke/ai-outputs`, `@spaarke/ai-widgets`, `@spaarke/events-components`).
- A handful of **"Cannot find module"** errors in `@spaarke/ai-widgets/widgets/workspace/CreateMatterWizardWidget.tsx`, `DocumentUploadWizardWidget.tsx`, `register-workspace-widgets.ts` pointing at non-existent paths like `@spaarke/ui-components/src/components/CreateMatterWizard`, `FileUpload/FileUploadZone`, `@spaarke/ai-outputs/src/output-widgets/BudgetDashboardWidget`. These need investigation — they may be deleted-file references or path-resolution gaps.

**Key strategic question for Phase A.3 resumption**: 92 of 112 errors are in shared libs. Three options:
1. Fix every error upstream (substantial — touches multiple shared libs).
2. Soften the base `noUnusedLocals` / `noUnusedParameters` to `false` so the shared-lib errors don't surface in SpaarkeAi's tsc run; each surface opts back in if it wants.
3. Use TS project references properly so shared libs are pre-checked in their own context and SpaarkeAi trusts their types via `dist/`. Big architectural change — defer.

**Recommendation when resuming**: Option 2 for this PR + a Phase B follow-up to fix shared-lib hygiene. The remaining 3 SpaarkeAi-owned errors get fixed inline.

---

## Plan reference — read in this order on resume

1. [`brittleness-remediation-plan.md`](./brittleness-remediation-plan.md) — full Phase A/B/C/D plan + R6 coordination notes
2. This file — current state
3. Original [`design.md`](../design.md) — project overall scope (Batches 1–3 of testing-feedback work)

---

## Active todo list at save (15 items, 1 in_progress)

```
[in_progress] A1: Create tsconfig.base.json at repo root         ← effectively done; needs commit
[pending]     A2: Have 3 Code Pages extend tsconfig.base.json    ← effectively done; needs commit
[pending]     A3: Fix pre-existing tsc failures                  ← see decision above
[pending]     A4: Add tsc --noEmit gate to 3 Code Pages' build
[pending]     A5: Persist pdfjs-dist, mammoth, applicationinsights-web in package.json
[pending]     B1: Create AppErrorBoundary in @spaarke/ui-components
[pending]     B2-B3: Wrap 3 Code Page entry points in AppErrorBoundary
[pending]     B4: Create safeRegister/createRegistry helpers
[pending]     B5-B6: Wrap registration calls in try/catch
[pending]     C1: Add playwright + smoke test infrastructure
[pending]     C2: Add code-pages-smoke CI job
[pending]     C3: Add CI tsc gate for Code Pages
[pending]     D1: Wire createRegistry into WorkspaceLayoutWidget
[pending]     D2: Per-widget Error Boundary at WorkspaceTabManager
[pending]     Final: rebuild + deploy + verify; resume iteration 2 bisect
```

After brittleness phases land, **resume iteration 2 bisect**:
- Slice 1 of iteration 2 partially landed via `60b98770`. Remaining iteration 2 changes to layer in:
   - DataGrid `ResizeObserver` responsive column sizing.
   - Matters widget chain (Matters config row at `113ad380-9e63-f111-ab0c-70a8a53ec687`; `matters.registration.ts` + sectionRegistry + section/index export + catalog entry + `matters-list` widget registration).
   - Calendar filter-chip rewrite (Popover-based chips replacing Dropdown row; Date Range chip with Quick Select; gray toolbar bg).

---

## Operator decisions captured during the session

| Decision | Made when | Notes |
|---|---|---|
| Roll back iteration 2 commit `8b9411d8` on the branch to keep PR in sync with deployed | mid-session | Done; revert commit is `fee6b785`. |
| Re-apply iteration 2 in slices (smallest first) after research | mid-session | Slice 1's `tabs` bug fix landed in `60b98770`. |
| Investigate brittleness BEFORE continuing iteration 2 | late session | Plan in `brittleness-remediation-plan.md`. |
| Brittleness scope = A + B + C + D in-session | late session | E (lazy refactor + staging) deferred to Phase B follow-up. |
| AI.Context/Outputs tsconfig path: align with other shared libs (`/src`) | late session | Not dist/; consistency with the 5 other shared libs. |
| R6 coordination documented in plan | late session | Plan has explicit R6 section. R6 will extend `WorkspaceWidgetRegistry`; my `safeRegister<T>` helper is type-generic to accommodate. |

---

## How to resume

1. Read this file + [`brittleness-remediation-plan.md`](./brittleness-remediation-plan.md).
2. `git status` and `git log --oneline -5` to confirm working tree state matches the save above.
3. Pick up at Phase A.3 — fix the 3 SpaarkeAi-owned errors in `ContextPaneController.test.tsx`. Defer the 92 shared-lib errors per Option 2 (soften `noUnusedLocals`/`noUnusedParameters` in `tsconfig.base.json`).
4. Proceed sequentially through the todo list.
5. After Phase D, **return to iteration 2 bisect** — layer in the DataGrid `ResizeObserver` next, then Matters, then Calendar filter chips. Use `localhost` smoke test after each one (now that infrastructure exists).

---

## Quick file references

| What | Where |
|---|---|
| Brittleness plan | `projects/ai-spaarke-ai-workspace-UI-r1/notes/brittleness-remediation-plan.md` |
| Project design.md | `projects/ai-spaarke-ai-workspace-UI-r1/design.md` |
| Project deployment notes | `projects/ai-spaarke-ai-workspace-UI-r1/notes/entity-view-widget-deployment.md` |
| New file: tsconfig base | `tsconfig.base.json` (repo root) |
| Iteration 2 partial commit | `60b98770` |
| Reverted iteration 2 full commit | `8b9411d8` (the commit) + `fee6b785` (the revert) |
| Active feature branch | `feature/ai-spaarke-ai-workspace-UI-r1` |
| PR | #372 |
| Deployed env | `https://spaarkedev1.crm.dynamics.com` |
| Deployed `sprk_spaarkeai` web resource ID | `5206a442-3451-f111-bec7-7ced8d1dc988` |

---

*End of resume doc. Commit the current uncommitted tsconfig changes before context compaction.*
