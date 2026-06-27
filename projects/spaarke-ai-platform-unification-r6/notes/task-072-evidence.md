# Task 072 Evidence Note — `WorkspaceWidgetRegistry` Pillar 9 Extension

> **Task**: 072 — D-C-27 Extend `WorkspaceWidgetRegistry` with optional `getVisibleState?`
> **Rigor**: STANDARD (type extension; opt-in field)
> **Completed**: 2026-06-18
> **Branch**: `work/spaarke-ai-platform-unification-r6`
> **Wave**: Phase C, C-G16 (combined-dispatch with 073)

---

## Outcome

The `WorkspaceWidgetRegistry` registration record now carries an OPTIONAL `getVisibleState` field of type `RegistryGetAgentVisibleState = (widgetData: unknown) => SerializedWidgetState | null`. Existing registrations continue to compile unchanged (visibility opt-in is NOT retrofitted) per FR-56. A new accessor `getWorkspaceWidgetVisibleStateFn(type)` exposes the registered derivation for the Pillar 9 prompt builder (task 074).

All 3 POML acceptance criteria pass:
1. ✅ `getVisibleState?` field added as optional to registry metadata.
2. ✅ All existing widget registrations continue to compile without modification.
3. ✅ `tsc --noEmit` passes for the changes (only PRE-EXISTING `Cannot find module` errors remain for unbuilt sibling-package paths — unchanged by this task).

---

## Files

| Path | Status | Notes |
|---|---|---|
| `src/client/shared/Spaarke.AI.Widgets/src/registry/WorkspaceWidgetRegistry.ts` | MODIFIED | Added `RegistryGetAgentVisibleState` type + optional `getVisibleState` field on `WorkspaceWidgetRegistration` + 4th-arg on `registerWorkspaceWidget`/`replaceWorkspaceWidget` + `getWorkspaceWidgetVisibleStateFn` accessor |
| `src/client/shared/Spaarke.AI.Widgets/src/registry/index.ts` | MODIFIED | Re-export new accessor + type |
| `src/client/shared/Spaarke.AI.Widgets/src/index.ts` | MODIFIED | Re-export new accessor + type via package barrel |
| `projects/spaarke-ai-platform-unification-r6/notes/task-072-evidence.md` | NEW (this file) | — |

---

## Design rationale

### Why the registry signature differs from `GetAgentVisibleState`

The canonical `GetAgentVisibleState = () => SerializedWidgetState | null` (from task 071's `SerializedWidgetState.ts`) is a zero-arg closure that EACH WIDGET INSTANCE owns — it captures its own state via closure scope. That's the contract the prompt builder calls per-tab at chat-turn time, expressed at the instance level.

The REGISTRY entry, however, is global + stateless — a single registration record serves every tab of that widget type. So the registry's signature takes the tab's `widgetData` payload as input and returns the serialized variant. The prompt builder calls this with the live tab's `widgetData`.

Both signatures honor FR-55 / ADR-015 — returning `null` (or omitting the registration field entirely) means the widget contributes NOTHING to the agent prompt. The two signatures are co-existing API surfaces: instance-level for per-widget composition; registry-level for the stateless registration-time hook. Task 073 attaches derivations at the registry level (one per category); future widgets that need per-instance closure-captured logic can still expose their own `GetAgentVisibleState` independently.

### Backward-compat invariant (FR-56)

The new field is OPTIONAL on `registerWorkspaceWidget` (4th positional arg). All 16+ existing call sites pre-task-072 compile unchanged. `tsc --noEmit` over the package confirms zero new errors. `safeRegisterWidget` (defined as `Parameters<typeof registerWorkspaceWidget>`) automatically picks up the new optional argument with no changes.

---

## Verification

### tsc --noEmit (changed surface)

```bash
$ cd src/client/shared/Spaarke.AI.Widgets && npx tsc --noEmit 2>&1 | grep -v "Cannot find module" | tail -10
(empty — zero non-pre-existing errors)
```

All `Cannot find module` errors are pre-existing peer-package resolution failures (sibling packages not yet built); they predate task 072 and exist on master as of 2026-06-18.

### Acceptance criteria

| Criterion | Result |
|---|---|
| `getVisibleState?` field added as optional to registry metadata | ✅ on `WorkspaceWidgetRegistration` + `registerWorkspaceWidget` 4th arg |
| All existing widget registrations continue to compile without modification | ✅ — verified by tsc; 16+ call sites untouched |
| tsc --noEmit passes | ✅ for task-072 surface |

---

## Cross-task linkage

| Downstream task | Consumed surface |
|---|---|
| 073 (D-C-28) | Attaches 4 per-category derivations to 8 registrations using the new 4th arg |
| 074 (D-C-29/30) | Per-turn prompt builder reads via `getWorkspaceWidgetVisibleStateFn(type)` |
