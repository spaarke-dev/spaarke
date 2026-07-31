# Task 053 — UAT #5 external-change detection + Reload-from-source — Deviation & Completion Note

> **Completed**: 2026-07-30 · Rigor FULL · sonnet/high · client-only.

## Root cause (confirmed)
The external-change check ran only on `window` `focus` (`ComposeWorkspace.tsx`). Compose is embedded in an iframe, where returning from the Word-web tab fires `document.visibilitychange` (→ visible) reliably but often NOT window `focus` — so the check never ran and the banner never appeared. No unconditional manual reload existed (banner Reload gated on dirty; toolbar sync icon = Refresh-Profile).

## Fix (client-only)
1. **`ComposeWorkspace.tsx`**: added a `document.visibilitychange` listener beside the `focus` one; both call `runReturnFromWordCheck` through an in-flight guard ref (`returnCheckInFlightRef`) so a single return doesn't double-run the check / double-advance the shared SPE delta cursor.
2. **"Reload from source" toolbar button** — threaded `onReloadFromSource` host → `ComposeEditor` → `ComposeFormatToolbar` (mirrors `onRefreshProfile`). Distinct icon (`ArrowClockwise24Regular`) + `data-testid="compose-format-reload-from-source"`. Dispatches the existing `requestLoad` (documentRef + sessionId) to pull the latest SPE bytes. Gated on `state.documentRef?.speDriveItemId` (only for a doc with an SPE source; hidden for born-in-editor). **Dirty-guard**: `window.confirm` before discarding unsaved edits (no silent loss, NFR-08).

## Scope note
The server SPE webhook delivery leg remains deferred (dev unprovisioned; no client push exists) — out of this task. This fix delivers reliable client-side detection (visibilitychange) + a manual escape hatch (Reload button), which is exactly what UAT #5 asked for ("there needs to be a refresh or update tool icon").

## Tests
- `ComposeFormatToolbar.test.tsx`: +4 tests (not-rendered without handler, renders+fires, distinct-from-refresh-profile, dark-mode ADR-021). Full toolbar suite **46/46 pass** (runs in-worktree — leaf test, no `@spaarke` import).
- Client typecheck: no NEW errors in ComposeEditor/ComposeFormatToolbar; ComposeWorkspace shows only the pre-existing `@spaarke/*` resolution + `unknown`/`any` cascades (identical on master, shifted by added lines).

## Step 9.5
Client-only; ADR-021 dark-mode covered by test; no server/ADR-013/007 surface. code-review: double-fire guard + dirty-guard + SPE-source gate verified. No violations.
