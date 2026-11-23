# Task 003 — RecordTemplateConfig + record template shell (FR-01, FR-02)

Executed 2026-11-11 by a sub-agent (Opus, effort high, rigor FULL, directional). Not committed (main session commits).

## Files

| File | Change |
|---|---|
| `src/client/external-spa/src/components/record/recordReads.ts` | New. R3's record and rollup reads (`useRecord`, `useRelatedRows`, `buildFetchXml`, `postFetch`, types) moved byte-for-byte from `RecordDetailModal.tsx`; only `export` added. No new read. |
| `src/client/external-spa/src/components/detail/RecordDetailModal.tsx` | Imports the moved reads; read block and now-unused imports removed. Behaviour unchanged (47/47 R3 tests). |
| `src/client/external-spa/src/components/detail/record-targets.ts` | `recordLinkFor(entity, id, {origin, pathname})` — the inverse of `parseRecordLink`, for Copy link. |
| `src/client/external-spa/src/components/record/recordTemplate.types.ts` | New. `RecordTemplateConfig`, section ids/labels, tab and header-fact types; toolbar ids = `RecordActionIconId`. |
| `src/client/external-spa/src/components/record/recordTemplates.ts` | New. `RECORD_TEMPLATES` (six types, FR-01 table exact, tabs off), `RECORD_ACTION_TEXT`, `recordTemplateTitle`, `recordText`, `getRecordTemplate`. |
| `src/client/external-spa/src/components/record/RecordTemplateHeader.tsx` | New. Name + up to three facts (value over label) + optional "Your access". |
| `src/client/external-spa/src/components/record/RecordTemplateModal.tsx` | New. The template on SprkModal `lg`: fixed chrome, scrolling content, 66/34 columns, R3 states, Refresh, Copy link, navigation hold, `renderSection` seam for task 005. |
| `src/client/external-spa/tests/RecordTemplateModal.test.tsx` | New. 40 vitest tests. |

## Decisions

- **Header (step 5): Fluent primitives in the SPA, not `RecordHeaderShell`.** The shared shell is the model-driven form
  header: a `HeaderToolbar` row (with its own icon strip) over a `FieldGrid` of form-input cells at a fixed 2 or 3
  columns, with no narrow layout. Used unchanged it would put an empty second icon strip under the record toolbar, and
  the facts would not stack at 375px (NFR-03); the facts would still have to be built here. Recorded in the component's
  doc comment.
- **Title** is "{label} {number}" with a space (owner 2026-11-08, AC), falling back to "{label} {name}", then "{label}".
  Whitespace-only values count as empty (`recordText`), so no title or heading is ever blank-padded.
- **State test ids and texts** are R3's (`record-detail-denied` etc.) so task 005 can port R3's tests unchanged.
- **Chrome only for a ready record.** Loading shows only the spinner; denied, not found, error and deleted show no
  toolbar, header or tabs (denied: nothing of the record on screen).
- **Refresh** keeps the shown record on screen with a "Refreshing record" progress bar until the re-read answers; the
  content is keyed by record, not by attempt, so sections stay mounted across a refresh. Sections get `refreshSignal`.
- **"Your access"** renders only when the `access` prop is supplied; task 006 supplies it.
- **Navigation hold.** One stable `onBusyChange` callback per section; any busy section disables the footer Close,
  makes `onBeforeNavigate` return false and ignores ×/Escape/backdrop (all routed through `onClose`).
- **Copy link** builds `{origin}{path}?record={token}:{id}` (POML shape) from `window.location`; a missing clipboard
  or a rejected write shows an inline warning with the link, never throws.
- **Escalation 2 (no data source)**: Matter "Responsible attorney" and "Last modified", Project "Project lead" are left
  out; every attribute a config names is in the entity's existing read fields (tested). No column added.
- **Escalation 1** did not fire: the fixed chrome lives inside the SprkModal body; SprkModal is unchanged.

## Deviations

- One file beyond the POML list: `RecordTemplateHeader.tsx` (the step 5 header, kept separate for cohesion).
- RecordOpener's "still opens the R3 modal" is covered by the existing `RecordDeepLink.test.tsx` (it mocks
  `RecordDetailModal` and asserts the opener renders it; 59/59), not by a new test.

## Test scope

40 tests, each mapped to an acceptance criterion: config map = FR-01 table, deep-link/feed coverage, read-field
coverage (no new column), no `sprk_` literal in the modal source (FR-01 config-only), `recordLinkFor` round-trip for all
tokens + rejects, title fallbacks, Document one column, chrome outside content + thin-scrollbar and overflow atomic
classes, 66/34 + stack-below-1000px classes and left-first order, tabs absent/present/switching, loading/denied/403/
not found/error + Try again/malformed id/deleted/onSettled, toolbar per type (only Refresh and Copy link), Refresh
re-read with stale record, `refreshSignal`, Copy link success/rejection/no clipboard, hold and release. Beyond the
contract: the 403-is-denied case (guards R3 parity of the moved read) and `refreshSignal` (the seam task 005 relies on).

Fail-before: the `recordLinkFor` tests fail (8) against HEAD's `record-targets.ts`; the title-fallback test failed
before the whitespace fix; every template test fails without the new modules.

Results: `npm run typecheck` 0 errors; `npm run lint` clean; `npm test` 48 files, 1332/1332 passed.

## Review (Step 9.5: code-review + adr-check, one pass)

| # | Finding | Class | Outcome |
|---|---|---|---|
| R1 | Whitespace-only number/name gave a blank-padded title ("Matter   ") and heading | F2 (user-visible) | Fixed before review by `recordText` (trim); covered by the title-fallback test |
| R2 | Copy link omits the `?customer=` key the grant email carries; a multi-customer CIAM user opening it in a new tab gets the picker | K2 (spec-fixed shape) | D-4, Issue PENDING — owner question |
| R3 | If a section is busy and the user presses Refresh and the re-read fails, the error state unmounts the sections mid-operation | K1 (no real path yet: no section reports busy until task 005) | Handoff to task 005: hold or keep the stale record on a failed refresh while busy |
| R4 | SprkModal body keeps its `scrollbarGutter: stable` gutter although the body never scrolls | K3 (shared component unchanged by design) | Note only |

adr-check: ADR-050 (SprkModal `lg`, footer Close only, nav in header) ✓; ADR-021 (tokens via `makeStyles`, no hex) ✓;
ADR-012 (no shared-lib change; consumes `RecordCommandBar` / `RECORD_ACTION_ICONS` / `thinScrollbarStyle`) ✓; FR-05
(icons only from `RECORD_ACTION_ICONS`) ✓; ADR-038 (network seam mocked only, KEEP path `tests/`) ✓;
`ModalUnmountedWhileOpenGuardTests` pattern (no `{cond && <X open />}`) ✓.

## UI tests left for deploy task 018 (no browser here; jsdom has no layout)

From task 001: overflow-menu arrow keys, focus rings, high-contrast rendering of `RecordCommandBar`.
From task 003: each state renders; columns at 1440 (66/34), 1000 (66/34) and 375px (stacked, left first, header facts
wrap, no sideways scroll); only the content region scrolls (chrome fixed); light, dark, high-contrast and Teams themes;
Copy link in a browser and in the Teams host (clipboard permission).
