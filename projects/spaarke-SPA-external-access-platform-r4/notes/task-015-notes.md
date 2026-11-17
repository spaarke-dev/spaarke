# Task 015: SPA event forms (FR-35, FR-36, FR-37)

**Rigor:** FULL (tsx changes, tagged `testing`). **Model:** implementation by a Sonnet `implementer`, review by Opus.
**Contract:** task 014 as merged (`3c9a2ff4e1`), live on dev BFF `e46b6b453b`.

## What changed (src/client/external-spa)

### Add event
- The Event/Task radio is gone. The dialog fields are:
  - Title (required);
  - **Event Type** (required, options exactly `creatableEventTypes`; no client list, no GUID literal);
  - **Due Date** (required when the type `requiresDueDate`);
  - **Priority** (default Normal);
  - **Assigned To** (from `assignable-people`, "Me" first);
  - **Description** (2,000 characters).
- **When Add event is offered:** only when the list's create right is true AND `creatableEventTypes` is non-empty (014 K3). The rule lives in `recordAccess.ts` (`addEventRightOf`); `createTaskRightOf` and `canCreateTask` are no longer used.
- **Request body:** the create sends the new shape (`eventTypeId`) and never the legacy `eventType: "task"`.
- **After a create:** the list re-reads quietly. The POST's returned row has no formatted names or rights. The rows stay on screen while it re-reads, and a failed re-read shows an error bar with Try again above them.
- **Assigned To load:** a failed people read hides the field with one line and never blocks the create. A 403 (a role without Contact Read) is "nobody to offer": the field is hidden and no line shows.
- **Rows** show type, status, priority, assignee and a short description.

### Event record (the seventh template)
- **Config:** a `sprk_event` entry in `RECORD_TEMPLATES` and `RECORD_DETAIL_ENTITIES`.
  - Information: Event Type, Status, Due Date, Priority, Assigned To, Description, Created On, Created By, Regarding.
  - Toolbar: Refresh, Edit, Change status, Copy link, Delete.
- **Opening from a row:** a row title opens the event as a child record over the record. It opens from the ROW DATA (a `seed`), not from `GET /events/{id}` (014 K5). Its re-read uses the parent's list and finds the row there. Close returns focus to the row and re-reads the Events section.
- **Deep link:** `?record=event:{id}` (new token `event`) opens top-level through `RecordDeepLink`, read with `GET /events/{id}`. A 404 shows the existing not-found state.
  - The spec, POML and design said `sprk_event:`. They were amended to `event:`, to follow the `project:` / `matter:` convention (review F4).
- **One child level:** Regarding is a link only in a top-level event, where it opens the parent as a child. In a child event it is plain text, and event rows inside a child record don't open anything.
- **Template plumbing** (generic; the modal has no entity branch):
  - `RecordDetailEntity.read?`;
  - `RecordTemplateModal` `seed?` / `readRecord?`;
  - `OpenChildRecord`'s fifth argument `{ seed, read }`;
  - `RecordViewContext.refreshRecord`;
  - `RecordNotFoundError`;
  - a generic field `link` hook in `RecordDetailFields` / `InformationSection`.
- **Action hosts** now mount outside the ready branch. They keep the last ready record for the same key, so a dialog stays open when a re-read answers 404. In any other non-ready state the hosts unmount as before.

### Edit and Change status (FR-36)
- **Hosts:** `EventEditAction` and `EventStatusAction`, FormModal children with Cancel on the left. Esc keeps the draft (project rule 2026-11-11).
- **Shown only when the BFF says so:**
  - Edit when `canEdit`;
  - Change status when `canChangeStatus` and `allowedStatuses` is non-empty.
- **Edit:**
  - The type is read-only (S-4).
  - Assigned To appears only when `canReassign`.
  - The PATCH sends only the fields that changed.
- **Change status** offers exactly `allowedStatuses`.
- **After a 204:** the record re-reads, and the parent list re-reads on close.
- **Refusals:**
  - 403: "You can no longer edit this event." and a re-read.
  - 404: the message stays in the open dialog and the record shows not-found. A second Save does not re-read again.
  - 503: retryable.
  - 400: the server's message.

### Delete
- The `delete` action id now maps to a dispatcher host, `RecordDeleteAction`: service request → the existing host; event → `EventDeleteAction` (creator only; reuses `useDeleteOwn`).

### Shared lib
- `RECORD_ACTION_ICONS` gains `edit` (Edit20Regular) and `changeStatus` (ArrowSwap20Regular). This is a reviewed FR-05 change; its pinning test and spec FR-05/FR-34(a) are updated.

## Tests
- **New:** `EventRecord.test.tsx`, `EventsApi.test.ts`, `useRecord.sources.test.tsx`, plus the `eventFixtures.ts` helper.
- **Updated, not deleted:** `RecordTemplateAccess`, `RecordAccess.levels`, `RecordTemplateModal`, `RecordTemplateChildDialogs`, `DetailEventsRoots`, `PerRecordWriteAccess`, `ProjectPage.createAgreesWithList`. Before the update they had 110 failures pinning the old radio / `canCreateTask` / `eventType` behaviour.
- **Must-not-fire pairs:**
  - type required / allowed;
  - Add event hidden vs shown on `creatableEventTypes`;
  - due date by type;
  - View Only vs Collaborate;
  - a failed vs a 403 vs an empty people read;
  - child edit then close shows the new value vs cancel shows the old;
  - 404 once and twice.
- **Final results:**
  - typecheck clean;
  - lint clean;
  - vitest 63 files / 1,799 tests passed;
  - `vite build` OK;
  - shared `jest RecordCommandBar` 14/14.

## Quality gates

### Adversarial review (Opus, one full pass)
- **F1–F3:** none. **F4:** the deep-link token (docs amended, above).
- **Fixed known limits:**
  - K2: a 404 used to close the dialog and lose its message;
  - K2: a 403 on the people read showed "could not be loaded";
  - K3: no test that the parent list shows the new values after an edit.
- **Docs (K4):** FR-05 icon map.

### Scoped re-check (Sonnet) of the fix diff
- F1 (low): a second 404 dropped the dialog. Fixed: `refreshRecord` does nothing while the record is not-found.
- F2: a dead `closeDialog` flag. Removed.
- The guard was verified by removing it: both new tests then fail.

### adr-check
- **ADR-012:** shared-lib change = two icon-map entries + test; no Xrm or entity literals.
- **ADR-021:** Fluent v9 and tokens.
- **ADR-050:** FormModal children, Cancel on the left.
- **ADR-038:** fetch-seam mocks as in the rest of the suite; must-not-fire pairs.
- **Project MODAL-DECISION-CRITERIA Path C:** children of the same family, focus and Esc tested.

## Known limits
- **K2 — Edit's Due Date:** Edit can't mark Due Date required for a type that needs one, because the event DTO has no `requiresDueDate`. Clearing it shows the server's 400 message.
- **K2 — Copy link:** on an event filed under a second parent the caller can't read (014 K5), Copy link makes a link that opens "no longer available" for that caller.
- **K2 — multi-parent assignee:** the SPA knows only one parent (the list root or GET's `regarding`), so it offers that parent's people. The PATCH's 400 `assignee_not_allowed` is shown as its message.
- **K2 — truncated parent list:** a list-opened event whose parent list is cut short shows not-found on re-read (there is no fallback to GET, because of K5).
- **K1 — refused save then a failed re-read:** a 403 on save followed by a re-read that answers denied or error replaces the page, as for every record type.

rework: fix-round-2 task 015
