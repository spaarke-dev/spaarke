# Task 030 — Capture (Viewed) implementation decisions + OQ-6 finding

> **2026-08-13** · `navigatorCaptureService.ts` + `navItemRepository.ts` (FULL rigor, sonnet/high)

## What was built

- `src/client/shared/Spaarke.UI.Components/src/services/navigator/navItemRepository.ts` — `Xrm.WebApi` CRUD for `sprk_navitem` (`findHistoryItem` / `createHistoryItem` / `bumpHistoryItem`), using `xrmContext.ts`'s widened `getXrm()` frame-walk (task 010) instead of a locally duplicated one. Exports the deployed option-set integer maps (`NavItemType`, `NavItemSource`, `NavItemPageType`) so downstream tasks (031 retention, 040/041 render, 050/051 pins) share one source of truth rather than re-declaring the integers.
- `src/client/shared/Spaarke.UI.Components/src/services/navigator/navigatorCaptureService.ts` — `startNavigatorCapture()`, a re-adoption of `notes/retired-sidepane-code/contextService.ts`'s `startContextChangeDetection` poll loop with the `sessionStorage` sink replaced by a history `sprk_navitem` upsert. Plain start/stop function (not a React hook) so a caller can start it once and it runs independent of any component's render/visibility lifecycle (NFR-05).
- `__tests__/navigatorCaptureService.test.ts` — 6 tests covering the 4 POML-required scenarios (3 ordered rows; re-visit bump not duplicate; leaving records → null; no-resolvable-entity writes nothing) plus 2 supporting tests (display-name resolution, fresh-Xrm-per-tick).

## Capture scope decision — entityrecord only (design decision, not an owner escalation)

FR-03's acceptance test (Matter → Project → Document) and the negative acceptance criterion ("a page with no resolvable entity ... no malformed row") are both satisfied by scoping **history capture** to `getPageContext().input.pageType === 'entityrecord'` with both `entityName` and `entityId` present. `entitylist`, `dashboard`, `webresource`, and `custom` pages are treated as "no resolvable entity" for a *history* row and are skipped — no write, no malformed row.

This is deliberately narrower than the raw poll signal: the task-001 spike observed the poll sees BOTH `entityrecord` and `entitylist` visits, and left "decide whether Recent viewed records lists too or filters to entityrecord only" as an open design question for this task. A `history` row's schema (`sprk_targetlogicalname` + `sprk_targetid` dedupe key) does not have a meaningful `sprk_targetid` for an entitylist/dashboard visit, so writing one there would either violate the dedupe contract (same-entity-type list visits would need to collapse to one row per entity, a different a dedupe semantic than FR-03's per-record dedupe) or produce rows with a null id that break the criterion 6 negative case. Scoping to `entityrecord` is the reading of FR-03/NFR-02 that keeps the schema and dedupe logic coherent; it does not foreclose entitylist/custom/dashboard support elsewhere — FR-08 (bookmarks, task 051) explicitly captures "the current page" (including entitylist/custom/weblink, via `sprk_pagetype`) on a user gesture rather than the passive poll, which is the correct home for those page types.

## OQ-6 — custom-page label resolution (per task step 4 / spec unresolved question)

**Status carried forward, not newly resolved with live-browser evidence this task.** The task-001 spike report explicitly noted: *"Custom-page shape NOT yet observed (none visited) — carry into task 040/086; low risk."* This task (030) had no live MDA session to visit a custom page and inspect `getPageContext()`'s raw `input` shape for `pageType: 'custom'`.

Given the "entityrecord only" scope decision above, OQ-6 does not block task 030: custom pages fall into the "no resolvable entity" bucket for **history** capture and are skipped cleanly (satisfies the negative acceptance criterion). OQ-6's real consumer is the **bookmark "Pin this page" gesture** (FR-08, task 051), which needs a best-effort label for a custom page pinned by the user. Recommendation: task 051 (or 040/086 deploy) empirically inspects `getPageContext()` on an actual custom page (e.g. a Code Page-hosted webresource pane, or a genuine Power Apps custom page if one exists in spaarkedev1) before implementing that label, rather than guessing the shape here.

## `sprk_displayname` resolution — metadata-driven, not a hardcoded map

`useSprkMemoRepository.ts`'s `PARENT_PRIMARY_NAME_FIELD` (a hardcoded 6-entity map) was reviewed as the "closest CRUD analog" per the task brief, but Recent (Viewed) capture observes navigation across **any** entity the user visits (Matter, Project, Document, Account, Contact, an unforeseen future entity, …), not a closed parent set — a hardcoded map would silently produce a blank/wrong label for anything outside the map and would need updating every time a new entity type is added to the app. `navigatorCaptureService.ts`'s `resolveDisplayName()` instead calls `Xrm.Utility.getEntityMetadata(entityLogicalName)` (a real, already-used-in-repo client API — see `EventDetailSidePane/src/hooks/useFieldMetadata.ts`) to read `PrimaryNameAttribute`, then a single `retrieveRecord` for that field. Falls back to a formatted entity-name label (`sprk_matter` → `Matter`, mirroring `contextService.ts`'s `getEntityDisplayName` fallback) on any failure — never throws, never blocks the write.

## Escalation trigger — did NOT fire

The POML's escalation trigger ("if `getPageContext()` does not expose a reliable current-user OData literal or a resolvable entity/id … STOP and escalate") does not apply: R2 (current-user filter) was already resolved GO in the task-001 spike (`_ownerid_value eq {userId}` / `_modifiedby_value eq {userId}` both confirmed working with `getGlobalContext().userSettings.userId`), and no new blocker surfaced while building/testing 030. No escalation raised.

## Not built here (explicitly out of scope per POML)

- Retention / prune-on-write (task 031).
- Wiring `startNavigatorCapture()` into `SprkSidePaneHost` / the app-startup bootstrap (tasks 040/086) — this task ships the capture engine as a standalone, host-agnostic module; the persistent pane starts it.
- Recent (Edited) derivation (FR-04, task 042) — unrelated capture path (a `modifiedby=me` query, not this poll).
