# Pinned Workspace Persistence — Manual Test

> **Scope**: Objective #3 of `ai-spaarke-ai-workspace-UI-r1`. Validate that pin state on workspaces persists across sessions AND that stale pins (layouts deleted server-side) are cleaned up at next cold load.
>
> **Code path**:
> - Write: `pinnedWorkspaces.ts:100` (`pinWorkspace`)
> - Read: `WorkspacePane.tsx:489+` (auto-open effect)
> - Storage key: `localStorage["spaarke:workspace:pinned-list"]`
> - Stale-pin cleanup (new in this project): `pinnedWorkspaces.ts:prunePinnedToKnown` invoked at the top of the auto-open effect.

## Test A — basic persistence

1. Open SpaarkeAi (`sprk_spaarkeai` Code Page) in a fresh browser session.
2. Open DevTools → Application → Local Storage → host origin. Locate key `spaarke:workspace:pinned-list`. If absent, value is implied `[]`.
3. Click the Workspaces dropdown (top-right of Workspace pane). Hover any layout to reveal the pin button. Click the pin icon (`PinRegular` → `PinFilled`).
4. **Verify localStorage** — refresh the Application tab view. `spaarke:workspace:pinned-list` now contains `[{"layoutId":"<guid>","layoutName":"<name>"}]`.
5. Close the dropdown. Pin a second layout. localStorage now contains 2 entries in pin order.
6. **Refresh the page** (F5). After auth completes and layouts load:
   - The pinned workspaces should auto-open as tabs (in pin order, after the default tab).
   - The pin icons in the dropdown should remain filled.
7. Unpin one workspace from the dropdown. Verify the corresponding entry is removed from localStorage immediately.

**Pass criteria**: pin state survives page reload; auto-open mounts the pinned tabs in stored order.

## Test B — stale-pin cleanup (new)

1. With at least 2 layouts pinned (Test A state), open the Manage Workspaces drawer (Workspaces dropdown → "Manage workspaces").
2. Delete one of the pinned layouts.
3. Refresh the page.
4. **Verify**:
   - localStorage `spaarke:workspace:pinned-list` no longer contains the deleted layout's entry (cleanup happened during `prunePinnedToKnown`).
   - The remaining pinned workspaces still auto-open.
   - No `widget_load` dispatch fires for the deleted layoutId (no transient tab labeled with the deleted layout's name appears).
5. Open DevTools console: no warnings about unresolved layouts.

**Pass criteria**: deleted layouts are silently removed from the pinned list at next cold load; auto-open does not attempt to mount non-existent layouts.

## Test C — quota / private mode resilience

1. Open SpaarkeAi in a Private/Incognito window.
2. Pin a workspace. (localStorage write may fail silently in some private modes; the function is wrapped in try/catch.)
3. **Verify**: no console errors. UI does not throw. The pin state may not persist across reloads in private mode — that's acceptable.

**Pass criteria**: no thrown errors when localStorage is unavailable.

## Test D — corrupt storage entry

1. Open DevTools → Application → Local Storage. Edit `spaarke:workspace:pinned-list` value directly to a malformed JSON string (e.g. `not-json`).
2. Refresh the page.
3. **Verify**: `getPinnedWorkspaces()` returns `[]` (caught in try/catch); no console errors; no auto-open of pinned tabs.

**Pass criteria**: corrupt storage degrades silently to "no pins".

## Code references

- `src/solutions/SpaarkeAi/src/services/pinnedWorkspaces.ts` — `getPinnedWorkspaces`, `pinWorkspace`, `unpinWorkspace`, `prunePinnedToKnown` (new), `setPinnedWorkspacesOrder`, `moveWorkspaceToTop`.
- `src/solutions/SpaarkeAi/src/components/workspace/WorkspacePane.tsx` — auto-open effect (lines ~488–550) now waits on `layouts.length > 0`, calls `prunePinnedToKnown(knownLayoutIds)`, then dispatches `widget_load` for survivors.
- `src/solutions/SpaarkeAi/src/components/workspace/WorkspacePaneMenu.tsx` — pin toggle handler at lines ~467–492.
