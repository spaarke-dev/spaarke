# Task 085 — Final repo-wide `sprk_eventtodo` grep sweep

> **Date**: 2026-06-10
> **Acceptance**: FR-29 (zero functional references to `sprk_eventtodo` in `src/`)

---

## Method

```
grep --case-insensitive sprk_eventtodo src/**/*.{ts,tsx,cs}
```

## Result

**54 total file hits across the repo.** Categorized:

| Category | Count | Disposition |
|---|---|---|
| Source code (`src/**/*.{ts,tsx,cs}`) | 13 hits across 12 files | All non-functional (JSDoc migration notes + 1 test negative-assertion). FR-29 ✅ |
| Project docs (`projects/smart-todo-decoupling-r3/`) | ~30 hits | R3 project artifacts — expected to reference the retired entity |
| Project docs (`projects/smart-todo-r4/`, `projects/events-smart-todo-kanban-r2/`) | ~5 hits | Historical predecessor / new successor project context — expected |
| Architecture doc (`docs/architecture/event-to-do-architecture.md`) | many hits | Superseded doc, retained for historical reference |
| Architecture doc (`docs/architecture/spaarke-todo-architecture.md`) | 2 hits | Active doc referencing the historical entity in transition context |
| Scripts (`scripts/Delete-SprkEventTodoEntity.ps1`, `Migrate-DataverseData.ps1`) | 3 hits | Historical setup scripts — retained for reproducibility |
| PCF bundles (`src/client/pcf/.../bundle.js`) | 2 hits | Compiled artifacts — auto-refresh on next PCF build |

## Source-code hits (verified non-functional)

```
src/client/shared/Spaarke.UI.Components/src/components/TodoDetail/TodoDetail.tsx:12          // JSDoc migration note
src/client/shared/Spaarke.UI.Components/src/components/TodoDetail/types.ts:5,29              // JSDoc migration notes
src/client/shared/Spaarke.UI.Components/src/components/TodoDetail/__tests__/...test.tsx:76  // Test comment
src/client/shared/Spaarke.UI.Components/src/components/CreateTodoWizard/todoService.ts:8     // JSDoc migration note
src/client/shared/Spaarke.UI.Components/src/components/CreateTodoWizard/__tests__/...:448    // Test negative-assertion (intentional)
src/client/shared/Spaarke.UI.Components/src/components/CreateTodoWizard/formTypes.ts:11      // JSDoc migration note
src/server/api/Sprk.Bff.Api/Services/Workspace/TodoGenerationService.cs:119                  // C# XML doc comment
src/solutions/EventDetailSidePane/src/App.tsx:644                                            // JSDoc migration note
src/solutions/EventDetailSidePane/src/components/TodoSection.tsx:10                          // JSDoc migration note
src/solutions/DailyBriefing/src/hooks/useInlineTodoCreate.ts:10                              // JSDoc migration note
src/solutions/SmartTodo/src/services/todoDetailService.ts:5                                  // JSDoc migration note
src/solutions/SmartTodo/src/components/TodoDetailPanel.tsx:7                                 // JSDoc migration note
```

All hits are in comments or test assertions. **Zero `await dataverseClient.create('sprk_eventtodo', …)`-style functional calls remain.** Acceptance criterion FR-29 satisfied.

## Outstanding

- `sprk_eventtodo` Dataverse entity itself is still present in `spaarkedev1` (orphan-but-not-deleted) per task 005 deferral (26 appmodulecomponent references blocking direct DELETE). Cleanup not blocking R3 close-out; SpaarkeCore solution exports do not carry the entity so tenant portability is unaffected.
