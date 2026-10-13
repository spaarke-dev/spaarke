# Task 050 — Record detail view (FR-01–FR-03)

**Status (2026-10-10):** implemented by an implementer agent (sonnet/high). The adversarial review is pending.

## Files
- `src/client/external-spa/src/components/detail/RecordDetailModal.tsx` (new)
- `src/client/external-spa/src/components/detail/RecordDetailFields.tsx` (new)
- `src/client/external-spa/src/widgets/GridWidgetBody.tsx` (modified)
- `src/client/external-spa/tests/RecordDetailModal.test.tsx` (new, 34 tests)

`ExternalDataGrid.tsx` was **not** changed, although the POML lists it. It already forwards `onRecordOpen` and `onRecordsLoaded`. Its text is also pinned by `ExternalSpaGridViewSelectorGuardTests` (a security guard), so any future change must update `PinnedWrapperSource` in the same change.

## Gates (run in `src/client/external-spa`)
- `npm run typecheck`, `npm run lint` and `npm run build`: all exit 0.
- `npm test`: 77 passed (43 existing + 34 new).
- Fail-before check: with `GridWidgetBody.tsx` at HEAD, 33 of the 34 new tests fail. The remaining one, the unknown-entity no-op, is meant to pass either way.
- ArchTests `ExternalSpaGridViewSelectorGuardTests`: 220/220.

## Deviations
1. **ADR-050, path C (comply).**
   - The detail view uses `SprkModal` with its own `nav` header, which provides the 1-of-N counter and prev/next.
   - It does not nest `RecordNavigationModalShell`: ADR-050, as amended on 2026-10-07, forbids nesting that shell's envelope.
   - The shell's dirty-check protocol isn't needed here, because the view is read-only and has no iframe.
2. **Read route for child records.**
   - `GET /api/v1/external/record/{entity}/{id}` is used only for Project, Matter and Work Assignment.
   - Document, Invoice and Service Request are read through an id-filtered module fetch instead. The record route always returns 403 for them: `ExternalModuleRegistry.IsRecordAccessible` (about lines 187-205) checks the record's own id against the parent or requester id set.
   - If the fetch returns zero rows, the view shows the denied state.

## Deferred
- **039:** events for Matter and Work Assignment.
  - Today these show an empty state, and no read is attempted.
  - The seam is the `entity === 'sprk_project'` branch in `EventsAndTasksSection`.
  - `SmartTodo` and `web-api-client` are project-only, so they need a change too.
- **051:** messages.
- **054:**
  - document upload and delete;
  - documents linked to a service request through `sprk_relatedservicerequest` (not on the documents allow-list yet).
- **Limits:**
  - Browse order is the grid's loaded order. A sort applied in the grid header is not reflected.
  - The shared grid fires `onRecordOpen` only from the primary-name link.
  - No live UI test in the browser or Teams yet, including dark mode; that waits for the deploy (070/072).

## Review fixes (2026-10-10)

**F1 (fixed): option-set columns rendered as raw integers.**
- Cause: Project, Matter and Work Assignment were read through `GET /api/v1/external/api/dataverse/record/{entity}/{id}`. `RecordService.ProjectEntityToDictionary` unwraps `OptionSetValue` to an int and emits no `@formattedValues` (only `FetchService.ProjectEntity` does), so "Status: 1" and "Priority: 100000001" were shown.
- Fix: all six entities now use the one id-filtered module fetch (`POST …/api/dataverse/fetch`, `<idAttribute> = {id}`), which carries `@formattedValues`. `readVia` and the record-route branch are removed from `RecordDetailFields.tsx` / `RecordDetailModal.tsx`. This supersedes Deviation 2 above (the record route is no longer used by this view at all).
- Security-equivalent: for the root modules the scope dimension is the record's own id (`RecordIdAttribute` = `sprk_projectid` / `sprk_matterid` / `sprk_workassignmentid` with `AccessibleRecordIds` in `ExternalAccessModule.cs`). `Tier2ScopeFilterInjector` ANDs the caller's accessible-set filter onto the client's id filter and `ScopeRows` re-checks each row. A record outside the set returns 0 rows, which is the existing denied state.
- Allow-list check: every requested column is on the module's `ReadableColumns` (project: id, name, number, statuscode, modifiedon; matter: id, name, number, statuscode; work assignment: id, name, number, priority, responseduedate, statuscode, regardingproject). No server change.
- Tests (`tests/RecordDetailModal.test.tsx`):
  - new `option-set labels` > `sprk_project` / `sprk_matter` / `sprk_workassignment`: "shows the formatted label, never the raw option-set integer" (raw `statuscode` / `sprk_priority` plus `@formattedValues`);
  - rewritten for the new seam: "reads the record from the external plane only, through the one id-filtered module fetch" (x6, asserts the record route is never called), the browsing tests, "a failed read (5xx) is an error with retry, not a denied state", new "a 403 from the fetch is the denied state, not an error", and the 404 / rollup tests.
  - The test double also models the real record route (raw integer, no `@formattedValues`), so a regression to it fails the label tests with "expected '1' to be 'Active'".
- Fail-before: against the unfixed components the 3 label tests fail (Received "1" / "100000002" / "1") and the rewritten seam tests fail; after the fix `npx vitest run` is 81/81.

**K4 (fixed): non-GUID row id.** The error state now carries `retryable`; a malformed id shows the message without "Try again" (retrying cannot succeed). Test: "a row id that is not a GUID is never sent to the BFF or interpolated into FetchXML" also asserts no retry button. A 5xx keeps "Try again".

**Known limits recorded**
- **K2:** a deleted record shows "You don't have access to this record", not "no longer available", on the fetch path. The server response is identical (0 rows) for "deleted" and "not yours", so there is no existence oracle; the "no longer available" state remains for an explicit 404 only.
- **K3:** the view uses `SprkModal` plus `nav` directly instead of the `BrowseModal` preset. ADR-050 MUST #1 permits a thin config of `SprkModal`; `BrowseModal`'s `PreviewGridBody` does not fit a sectioned detail view.
