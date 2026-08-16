# Task 020 — Save / Save As dropdown + Auto Save toggle (FR-01) — IMPLEMENTED

> Phase 2 (Save Dropdown / UC-2) · sonnet@high · FULL rigor · 2026-08-16 · client-only

## What shipped

- **`ComposeFormatToolbar.tsx`**: the Save split-button menu is now an explicit **Save / Save As** dropdown plus an **Auto Save** checkable toggle:
  - Primary button + menu item **"Save"** → `onSave('version')` (append an SPE version to the same document — ADR-049). Tooltip/aria-label changed "Save Version" → "Save".
  - Menu item **"Save As"** → `onSave('new')` (a REAL fork with a uniquified filename per task 012 — never a silent re-version). Replaces R6's "Save New Document"; `data-testid="compose-format-save-new"` retained.
  - **"Auto Save"** `MenuItemCheckbox` (Fluent v9), controlled by the host via `autoSaveEnabled` + `onAutoSaveToggle`. Rendered only when BOTH are wired (a host without autosave keeps the plain Save / Save As menu). `checkedValues`/`onCheckedValueChange` on the `Menu` drive it — all Fluent v9 semantic tokens (ADR-021 dark-mode).
  - `ComposeSaveMode` enum unchanged (`'version' | 'new'`) — labels/UX only.
- **Host wiring** (`ComposeWorkspace` → `ComposeEditor` → toolbar): added `const [autoSaveEnabled, setAutoSaveEnabled] = useState(true)` (autosave ON by default per spec) in `ComposeWorkspace`; threaded `autoSaveEnabled` + `onAutoSaveToggle` through `ComposeEditor` to the toolbar. **This task wires the CONTROL to the state**; the draft-safe autosave BEHAVIOR (client-only local draft, beforeunload guard, recovery) is Phase 4 (040/041), which consumes this same `autoSaveEnabled` state — kept in the workspace so 040 drives autosave off it without moving it.

## Verification

- `ComposeFormatToolbar.test.tsx`: **75/75 green** — updated the two UAT-round-1 tests that asserted the old "Save Version" aria-label/tooltip; added 6 tests (Save primary → version, menu Save → version, Save As → new, Auto Save renders checked + fires `onAutoSaveToggle(false)`, Auto Save absent when unwired, dark-theme render).
- Typecheck: no new errors in touched code (`ComposeFormatToolbar`, `ComposeEditor`, `ComposeWorkspace`).
- Client-only; `docxBridge.ts` untouched. Enum unchanged (`'version' | 'new'`).

## Note
The `<ui-tests>` (browser dark-mode + Save-As-produces-distinct-doc) are manual UAT; the component-level dark-theme render + the fork wiring (task 012) are unit-covered here.
