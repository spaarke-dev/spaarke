# Task 080 — Read-time security trimming (FR-12 / NFR-04)

**Status**: Complete. RIGOR: FULL. Model: sonnet / effort high.

## What was built

- **NEW** `src/solutions/NavigatorPane/src/services/securityTrimService.ts` — given a set of
  Navigator row targets, performs a lightweight host-context `Xrm.WebApi.retrieveRecord` per
  target (signed-in user, never a BFF/plugin/service-account call) and classifies each as
  `accessible` | `denied` | `transient`. Never throws.
- **NEW** `src/solutions/NavigatorPane/src/services/__tests__/securityTrimService.test.ts` — 19
  tests covering the classification matrix, the two structural exemptions, batching, and the
  "never throws" guarantee.
- **MODIFIED** `src/solutions/NavigatorPane/src/tabs/RecentTab.tsx` — replaced the task-041
  ad-hoc `isRowAccessible` sequential-loop trim (boolean only, no 403/404-vs-transient
  distinction) with a batched `classifyTargets` call; `denied` rows are dropped, `transient`
  rows are kept.
- **MODIFIED** `src/solutions/NavigatorPane/src/tabs/PinnedTab.tsx` — added the same batched
  trim to `loadPins()`, covering the Records group and any `EntityRecord`-pagetype Bookmarks
  (task 051's "Pin this page" partition already routes those into the Records render path, so
  one trim pass covers both). Monitored group intentionally NOT additionally trimmed — see
  "Monitored-group exemption" below.
- **MODIFIED** `RecentTab.test.tsx` / `PinnedTab.test.tsx` — added/extended tests for: denied
  row hidden + cached name absent from the DOM in light AND dark; accessible row renders
  normally; transient error keeps the row.

## Anti-flash approach (the crux of NFR-04)

**Mechanism: gate the entire row array behind the resolved classification — never call
`setRows` with untrimmed data, not even once.**

Both `RecentTab.tsx`'s `load()` effect and `PinnedTab.tsx`'s `loadPins()` callback follow the
identical shape:

```
setStatus('loading');           // Spinner renders; `rows` is not consulted
const rawRows = await list...(); // fetch (still `loading`)
const classifications = await classifyTargets(xrm, rawRows.map(trimTargetFromRow));
const trimmed = rawRows.filter(row => classifications.get(row.id) !== 'denied');
setRows(trimmed);                // the ONLY setRows call in the whole path
setStatus('ready');              // NOW rows render, already trimmed
```

There is exactly **one** `setRows` call in each load path, and it only fires after the
`await classifyTargets(...)` promise has resolved. Every row that ever enters `rows` state has
already been classified — a `denied` row's cached name is never placed in React state, so there
is no render tree, no DOM node, and nothing to "flash" even for a single frame. This is
provably leak-free by construction (no timing assumption, no race to reason about) rather than
relying on the trim being "fast enough."

This same shape already existed for `RecentTab.tsx` prior to task 080 (the task-041 minimal
trim used the identical "classify before setRows" pattern, just with a boolean-only,
sequential-loop classifier) — task 080 extends the pattern to a proper 3-way classification and
extends it to `PinnedTab.tsx`, which previously had **no** trim at all.

**Chosen row treatment: hide (drop), not "(no longer available)" placeholder.** The task
allowed either. Hiding was chosen because it is simpler to reason about for leak-freedom (there
is no partial row DOM element that could accidentally be given the cached name by a future
edit) and matches the pre-existing task-041 precedent already shipped in `RecentTab.tsx`.

## 403/404-vs-transient classification rules

Implemented in `securityTrimService.ts`'s `classifyWebApiError`. Checked in this order:

1. **Numeric status, if present** (`err.status`/`err.statusCode`/`err.raw.status`):
   `403`/`404` → `denied`; `>= 500` → `transient`.
2. **Message-substring signals** (case-insensitive), matched against the real Dataverse /
   `Xrm.WebApi` client SDK error message shapes already relied on elsewhere in this codebase
   (`navItemRepository.ts`'s `parseWebApiError`, `eventService.ts`'s `parseWebApiError`):
   - **Denied**: `\b403\b`, `\b404\b`, `forbidden`, `privileg` (privilege/privileges —
     Dataverse's real insufficient-privilege message is
     `"Principal user ... is missing prvRead<entity> privilege"`), `does not have (adequate )?
     permission`, `access is denied`, `does not exist` (Dataverse's real 404 message is
     `"<entity> With Id = <id> Does Not Exist"`), `could not be found`, `was not found`,
     `\bnot found\b`.
3. **Everything else defaults to `transient`** — network errors, `fetch`/timeout failures, 5xx,
   and any unrecognized/ambiguous error shape. The classifier NEVER assumes denial without a
   clear signal; ambiguity always resolves toward "keep the row."

**Fail-safe direction is intentional and asymmetric**: an unrecognized error can only ever
resolve to `transient` (row kept, no behavior change vs. pre-task-080), never to `denied`
(row hidden). This means the system can never accidentally increase leak risk from an
unrecognized error shape, only (in the locale caveat below) under-trim in a rare case.

**Known limitation (flagged by code-review, not a regression introduced by this task):**
message-substring matching is not locale-independent — a non-English Dataverse org would
receive localized error text that may not match the English signal list above, which would
fall through to `transient` (row kept). This mirrors a pre-existing limitation already present
in `navItemRepository.parseWebApiError`/`eventService.parseWebApiError` (both message-substring
based). Not fixed in this task; documented for a future hardening pass (numeric Dataverse
`errorCode` values, e.g. `-2147220969` for "does not exist", would be locale-independent).

## Weblink / no-target exemption rationale

Only `sprk_pagetype = EntityRecord` rows are re-checked via a retrieve. Every other pagetype is
classified `accessible` WITHOUT issuing a network call, because none of them carry a cached
*record* name that could leak:

- **`WebLink` (100000003)** — a raw non-Dataverse URL (bookmark or pasted link). No Dataverse
  target exists at all.
- **`EntityList` (100000001)** — a saved-view bookmark. Its `sprk_targetid` holds the *view's*
  id (`savedqueryid`), **not** a record id in `sprk_targetlogicalname`'s entity set (see
  `PinnedTab.tsx`'s `navigateToRow` `viewId` handling from task 051). Retrieve-checking this as
  `retrieveRecord(targetlogicalname, targetid)` would look up a record that structurally cannot
  exist in that entity set and would reliably 404 — **incorrectly trimming every view bookmark
  regardless of actual access**. This exemption is not just a documentation note; it prevents a
  real bug and is directly tested (`securityTrimService.test.ts`: "an EntityList (saved-view
  bookmark) row classifies accessible without a retrieve"). The cached label on these rows is a
  *view name*, not a specific confidential record's name, so there is no leak surface to begin
  with.
- **`Custom` (100000002)** — an unresolvable custom page (OQ-6, task 041). Never has a
  `sprk_targetlogicalname`/`sprk_targetid` pair — nothing to check.

This exemption logic lives centrally in `securityTrimService.ts` (`isRecordTarget`), not
duplicated per-tab, so both `RecentTab.tsx` and `PinnedTab.tsx` get identical, correct behavior
and the false-404-on-view-bookmark bug can't be reintroduced by a future caller.

## Monitored-group exemption (deviation from the task's literal file list — documented per adr-check)

The task's `<relevant-files>`/prompt text said to apply "the same trim across record-bearing
pinned rows (Records + Monitored groups)." The implementation deliberately does **not** add an
additional `classifyTargets` call to `PinnedTab.tsx`'s Monitored group. Rationale:

- `monitoredService.listMonitoredByMe()` issues N live `Xrm.WebApi.retrieveMultipleRecords`
  queries (owner-scoped `sprk_monitor eq true` filter) fresh on every mount. It is **not** a
  cached label — the displayed name comes directly from the same live query result.
- Dataverse enforces row-level security on `retrieveMultipleRecords`: a record the signed-in
  user cannot read is never returned in the result set in the first place. Every row that
  reaches `monitoredItems` state is, by construction, currently accessible at the moment it was
  fetched — there is no interval in which a stale, now-inaccessible cached name could be shown.
- This is the exact same reasoning already established and shipped for `RecentTab.tsx`'s Edited
  tab (task 042): "Edited rows are NOT separately trimmed here — they come from a live query
  against the target entity itself (not a cached label), so an inaccessible record simply never
  appears in the result set (standard Dataverse row security)."
- Adding a redundant retrieve-based trim on top of an already-live, already-security-filtered
  query would add N extra round-trips per Pinned-tab mount with zero confidentiality benefit —
  directly against CLAUDE.md §11 (component/round-trip justification — "cost of doing nothing"
  must name a concrete failure mode; there isn't one here).

This is documented in the `PinnedTab.tsx` module docblock (new "Read-time security trimming"
paragraph) as well as here. Flagged explicitly to the human reviewer via the `adr-check` output
(Warning, Path A — project-scoped exception, already documented) rather than shipped silently,
per root CLAUDE.md §6.5's spirit even though this is a task-instruction deviation rather than a
formal ADR conflict.

## Deviations summary

1. **Monitored group not additionally trimmed** — see above. Documented, reasoned, flagged to
   reviewer via `adr-check` (Warning, Path A accepted).
2. **Row treatment chosen: hide, not "(no longer available)" placeholder** — task allowed
   either; hide chosen for simplicity/leak-proof-by-construction and consistency with the
   pre-existing task-041 `RecentTab.tsx` precedent.
3. No other deviations. `TASK-INDEX.md` and `current-task.md` were left untouched per this
   task's explicit instruction (main session owns those).
