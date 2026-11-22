# Task 024 — shared EmptyState (FR-17)

2026-11-22. Rigor FULL; Sonnet tier per POML, run in the main (Opus) session. No delegation: four small, coupled SPA edits plus one shared-grid option (Step 8.0).

## Sites found (Step 1)

| File | Site | Before | After |
|---|---|---|---|
| `DocumentLibrary.tsx` | Documents list (shared grid since 023c) | the grid's config message "No documents on this record yet." (no action hint) | shared `EmptyState` (compact) through the grid's new `emptyState` option: heading "No Documents", the pre-023c sentence, the Upload hint only when Upload is offered ("Use the Upload button…" outside the template, "Use Upload above…" in it) |
| `DocumentLibrary.tsx` | Version history dialog, no versions | `Text` + `emptyStateText` style | `EmptyState` compact, History icon, "No version history" + today's sentence |
| `EventsCalendar.tsx` | Events list | hand-rolled `emptyState*` styles (dashed band) | `EmptyState` compact, same icon, heading and action-conditional sentence |
| `detail/RecordMessages.tsx` | empty thread | `styles.empty` box with one sentence | `EmptyState` compact: heading "No messages yet", the rest of the sentence as the hint, only when a way to post is offered; `data-testid="record-messages-empty"` kept on a wrapper |
| `record/sections/InvoicesSection.tsx` | record Invoices section | `styles.empty` box | `EmptyState` compact: "No invoices" + "No invoices for this record." |

Not empty states, unchanged: Messages "not available for this record" (style renamed `empty` → `unavailable`), every loading, error, access-denied and truncated state.

## Deviations from the POML

- **`SmartTodo.tsx` no longer exists** (to-dos left the SPA, owner 2026-11-12), and `RecordDetailModal.tsx` was deleted in task 005; its `RelatedList` is now `record/sections/InvoicesSection.tsx`, converted here. `InvoiceFilesList.tsx` (task 021) already used `EmptyState`.
- **A shared-grid change (owner sign-off needed, like 023c's two).** The Documents list's empty state is drawn by the shared DataGrid, so the SPA could not put `EmptyState` there without a host seam. Added an additive, optional `emptyState?: React.ReactNode` to `DataGridProps`: when given it replaces `display.emptyStateMessage`; shown only when the list is truly empty (never while loading, after a failed load or for a truncated list). Absent = unchanged for every other consumer. `EmptyState.tsx` itself is unchanged (ADR-012 constraint).
- **Defect found and fixed in the shared grid (pre-existing since 023a):** `useHostRowSource` started with `isLoading: false`, so the render that turned a host source on (before the effect that starts the read) counted as an empty list. With the shared `EmptyState` (a `role="status"` / `aria-live` region) that one render would mount and could announce "No Documents" while the list was still loading; the SPA test caught it as an order-dependent failure. Fix: the hook reports loading until page 1 for the current key has answered (rows or error). Regression test `DataGrid.emptyState.test.tsx` "the empty state never mounts…" fails on the old hook (verified) and passes on the fix.
- **Icons:** Messages and Invoices had no icon and keep none (POML: icons keep their current glyphs). Version history gains the History icon the dialog's rows already use.
- The dashed band around the Events empty state is gone: the shared component's compact look replaces it (the point of FR-17).

## Review (one adversarial pass, Opus)

No F1/F3/F4. Every acceptance criterion met; the hook cannot leave `loading` stuck (every path settles or turns the source off).
- **F2-1 fixed:** the record-template "no action offered" case had no must-not-fire test for Documents and Messages. Added to `RecordTemplateAccess` View-only (each empty state names no action; mutation check: forcing the Messages hint on fails it) and `DocumentLibrary.sharedGrid` (template, upload not granted).
- **F2-2 fixed:** `docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md` host-options table gains the `emptyState` row and the "loading until page 1 answers" note.
- **Known limits (K4):**
  - Invoices and Version history gain a short heading above their unchanged sentence (EmptyState needs a heading).
  - The grid's empty wrapper padding sits around EmptyState's own, so the Documents empty block is a little taller than the other sections. Cosmetic.
  - `emptyStateMessage` in `config/external-record-documents.gridconfiguration.json` is now always overridden for this host.
  - Messages "not available for this record" (404 root-not-supported) stays as it was: not a "no items" state.
- **Out of scope, filed:** D-30 #2975 (Idea). The empty states outside FR-17's files are still hand-rolled, including a local copy of `EmptyState` in `OutsideCounselDashboard.tsx`.

## Verification

- Shared lib: jest DataGrid 20 suites / 178 tests pass; `tsc` clean; client lint (ui-components) ok.
- SPA: `tsc` clean; eslint clean on the changed files; `npm run build` ok; full vitest 69 files / 1935 tests pass (before the F2-1 test additions; those two files re-run green after).

rework: fix-round-1 task 024
- Tests changed: `DocumentLibrary.sharedGrid.test.tsx` (empty state with Upload hint, must-not-fire without Upload, template "above" hint, truncated empty shows no "No Documents"), `RecordTemplateAccess.test.tsx` (Documents now also says "above"), and the Documents empty-state text in `DetailDocumentsDelete`, `ProjectPage.createAgreesWithList`, `RecordTemplateSections`. New: `DataGrid.emptyState.test.tsx` (must-fire, control without the option, rows / truncated / failed must-not-fire, the no-flash regression).
