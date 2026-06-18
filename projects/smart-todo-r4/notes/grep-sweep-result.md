# R4-094 — Final `sprk_todoflag` Grep Sweep (Graduation Criterion 12)

> **Run**: 2026-06-12
> **Branch**: `work/smart-todo-r4-wave2` @ `651426d6` (origin/master merged in same day; includes PR #377 + PR #384 content)
> **Command**: `grep -ri sprk_todoflag src/` (ripgrep, case-insensitive, all file types — wider than the spec's `src/**/*.{ts,tsx,cs}` glob to catch artifacts)
> **Verdict**: ✅ **PASS — 0 functional hits in source.** 1 stale generated artifact flagged (out of criterion scope; follow-up filed).

---

## Summary

| Metric | Value |
|---|---|
| Total matching lines | 50 across 33 files |
| Source files (`.ts`/`.tsx`/`.cs`/`.md`) | 45 lines across 32 files — **all non-functional** |
| Generated artifacts | 5 lines (7 occurrences) in 1 file — stale compiled bundle, see §3 |
| **Functional hits per criterion 12 (`src/**/*.{ts,tsx,cs}`)** | **0** ✅ |

---

## 1. Non-functional: explanatory comments / docstrings (29 files, 41 lines)

Every hit is inside a comment or docstring explaining that the legacy `sprk_event` + `sprk_todoflag` model was removed (R3 FR-29 / OS-1, R4 FR-02). No executable reference to the column. Representative examples:

| File | Line(s) | Nature |
|---|---|---|
| `src/solutions/SmartTodo/src/services/queryHelpers.ts` | 176, 469, 508 | Docstrings: "no `sprk_todoflag` filter — that field no longer exists" |
| `src/solutions/SmartTodo/src/services/DataverseService.ts` | 461, 750 | Comments noting flag removed in R3 Phase 1 |
| `src/solutions/LegalWorkspace/src/{types,services,hooks,components,sections,contexts}/…` | 14 files | Same pattern — legacy-removal rationale comments |
| `src/client/shared/Spaarke.UI.Components/src/components/{TodoDetail,CreateTodoWizard,CreateWorkAssignmentWizard}/…` | 9 files | Same pattern |
| `src/client/shared/Spaarke.SmartTodo.Components/src/widgets/SmartTodoWidget/SmartTodoWidget.tsx` | 5, 83 | Docstring: "Zero `sprk_todoflag` references" (R4-020 widget rebuild) |
| `src/server/api/Sprk.Bff.Api/Services/Workspace/TodoGenerationService.cs` | 92, 849 | XML-doc remarks noting legacy model removed |
| `src/solutions/DailyBriefing/src/hooks/useInlineTodoCreate.ts` | 7 | Docstring describing the legacy shape it replaced |
| `src/solutions/SmartTodo/README.md` | 16 | Doc prose |

## 2. Non-functional: absence-guard test assertions (2 files, 4 lines) — intentional

These are regression guards that **assert the string is NOT present** — exactly what criterion 12 wants enforced going forward. Keep them.

| File | Line | Assertion |
|---|---|---|
| `src/client/shared/Spaarke.SmartTodo.Components/__tests__/SmartTodoWidget.test.tsx` | 14, 46 | `assert(!q1.includes('sprk_todoflag'), 'Query must not reference sprk_todoflag')` |
| `src/client/shared/Spaarke.UI.Components/src/components/CreateTodoWizard/__tests__/todoService.test.ts` | 188–189 | `expect(payload).not.toHaveProperty('sprk_todoflag')` (+ `@odata.bind` variant) |

## 3. ⚠️ Stale generated artifact (out of criterion scope — follow-up filed)

**File**: `src/client/pcf/SpeDocumentViewer/solution/src/Controls/Spaarke.SpeDocumentViewer/bundle.js` (5 matching lines: 2000, 2010, 2030, 2130, 3070; 7 occurrences)

- This is the **checked-in compiled solution copy** of SpeDocumentViewer **v1.0.16**, last built **2026-05-11** (commit `3cca264e`) — **before** the R3/R4 legacy-model removal.
- It embeds pre-R3 `Spaarke.UI.Components` code: `TodoWizardDialog` / `todoService` ("A To Do is a sprk_event record with sprk_todoflag=true"), and a **live write path** at line 2130: `if (eventState.addTodo) { entity['sprk_todoflag'] = true; }` (legacy CreateWorkAssignmentWizard addTodo checkbox).
- **Classification**: NOT a functional source hit — it is a build artifact, `.js`, outside the criterion glob (`ts/tsx/cs`), and is regenerated from clean source on the next `npm run build:prod` + repack. Current SpeDocumentViewer *source* has zero hits.
- **Risk**: if this stale solution ZIP source is re-imported as-is, it ships code that writes to a dropped column (runtime save error if the legacy wizard path is exercised). The currently-deployed v1.0.16 (dev1 + demo, shipped 2026-05-11) predates the column drop and carries the same latent code.

**Follow-up (filed in TASK-INDEX Follow-ups)**: rebuild + repack SpeDocumentViewer against current shared libs (version bump per PCF guide) before/with the next deploy wave (R4-092 master-side deploy is the natural slot), or verify the control never surfaces the legacy To Do wizard path.

---

## Acceptance criteria

| Criterion | Result |
|---|---|
| 0 functional `sprk_todoflag` hits in `src/` (`ts/tsx/cs`) | ✅ PASS |
| Non-functional hits documented + classified | ✅ §1 (comments), §2 (guard assertions), §3 (stale artifact) |
| Evidence captured in `notes/grep-sweep-result.md` | ✅ this file |

*Graduation criterion 12: **MET**.*
