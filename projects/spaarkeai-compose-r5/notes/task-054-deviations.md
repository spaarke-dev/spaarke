# Task 054 — UAT #9 refresh-profile button visibility + feedback — Deviation & Completion Note

> **Completed**: 2026-07-30 · Rigor STANDARD (client-only) · sonnet/high.

## Investigation finding — the visibility gate is already CORRECT
The manual Refresh-Profile button gate (`ComposeWorkspace.tsx` `onRefreshProfile={state.documentRef?.sprkDocumentId ? … : undefined}`) is semantically correct: the button shows for a PROMOTED doc (one with a `sprk_document` record to re-profile) and hides for a transient/unpromoted mount (re-profiling an unsaved doc is a no-op — the investigation explicitly warned against a no-op button). The `saveSucceeded` reducer (`ComposeWorkspace.types.ts:478`) **already** propagates `sprkDocumentId` from the create-on-save response, so the button appears immediately after a transient doc's first successful Save — no reload needed. All POML acceptance criteria for visibility were therefore already met by existing code.

**Root cause of the UAT non-visibility**: the user's doc never promoted because its **save was blocked** (Word/WOPI lock, UAT #10/#11) and/or the edit routed to the renderer (UAT #1A). Once **050** (redline routing) and **052** (Word unlock) let saves succeed, the doc promotes → the button appears. So #9's visibility is unblocked transitively by 050/052; no gate change was needed (and relaxing the gate to unpromoted docs would create the no-op button the investigation warned against).

## Concrete change made — visible feedback (the one genuine gap)
The manual re-run is a fire-and-forget **202 with no visible result** — the UAT complaint was that neither the automatic re-trigger nor the manual button gave any signal. Added:
- `ComposeWorkspace.tsx`: `isRefreshingProfile` state; `triggerRefreshProfile` sets it true, then false ~1.5 s after the 202 (or immediately on error) so a fast response still registers as a deliberate action.
- Threaded `isRefreshingProfile` host → `ComposeEditor` → `ComposeFormatToolbar`.
- `ComposeFormatToolbar.tsx`: the Refresh-Profile button shows a `<Spinner size="tiny" />` + "Refreshing document profile…" tooltip + updated aria-label + disabled while in flight.

The automatic load-time re-trigger remains silent by design (server-side fire-and-forget, eTag-storm-guarded) — surfacing it would need a Load-response flag; deferred as out of scope (the manual button is the user-facing affordance).

## Tests
- `ComposeFormatToolbar.test.tsx`: existing gate tests (button absent without handler / present+fires with handler / dark mode) already cover visibility; +1 test for the in-flight spinner/disabled/aria state. Full toolbar suite **47/47 pass** (runs in-worktree).
- Typecheck: no NEW errors in ComposeEditor/ComposeFormatToolbar.

## Step 9.5
Client-only; ADR-021 dark-mode covered; no server/ADR surface. code-review: gate verified correct; spinner state self-contained + disabled-while-running prevents double-fire. No violations.
