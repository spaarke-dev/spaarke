# Deferred work and found issues — External Access Platform R4

Each entry here has a matching GitHub issue (project rule, `CLAUDE.md` §2 Process). An entry marked
**Issue: PENDING** was recorded by a sub-agent that may not file issues; the main session files it and replaces the
marker with the issue number.

| # | Found | Item | Severity | Issue |
|---|-------|------|----------|-------|
| D-1 | 2026-11-11, task 002 review pass 2 (F1, pre-existing) | External event create does not write the ADR-024 resolver fields | Defect (user-visible) | #2686 |
| D-2 | 2026-11-11, task 002 review pass 2 (K1, pre-existing) | External event create's `Location` header names a route that does not exist | Defect (contract) | #2687 |
| D-3 | 2026-11-11, task 002 review pass 2 (K4) | Documents-list `canCreate` ceiling is tested for service requests only | Test gap | #2688 |
| D-4 | 2026-11-11, task 003 review (K2, owner question) | Copy link omits the `?customer=` key the grant email carries | UX (multi-customer users) | #2693 |
| D-5 | 2026-11-11, task 005 review (K4, pre-existing) | external-spa test files are never type-checked | Tooling gap | #2699 |
| D-6 | 2026-11-11, task 004 (pre-existing) | Create Event dialog collects an event type that is never sent | Defect (user-visible) | #2703 |
| D-7 | 2026-11-11, task 004 (shared library gap) | SprkModal presets give no focus return for a controlled child dialog | Component gap (shared) | #2707 |
| D-8 | 2026-11-11, task 004 review (K-class, pre-existing) | A child dialog opened within ~250 ms of its parent modal is left aria-hidden | Accessibility (shared) | #2704 |
| D-9 | 2026-11-11, task 006 review (F1, pre-existing, owner question) | ProjectPage offers creates the project list refused (workforce ceiling → 403) | Defect (user-visible) | #2716 |
| D-10 | 2026-11-11, task 006 | No server answer for the to-do PATCH right; status toggle hidden in the record view | Feature gap (BFF) | #2717 |
| D-11 | 2026-11-11, task 006 review pass 2 (F2, out of scope) | Access-level label switch duplicated in five places | Maintainability | #2718 |

## D-1 — External event create does not write the ADR-024 resolver fields

**Where.** `ExternalDataService.BuildEventCreatePayload` (`src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ExternalDataService.cs`),
used by `POST /api/v1/external/{projects|matters|workassignments}/{id}/events`.

**What.** The payload binds only the root lookup (`sprk_RegardingProject|Matter|WorkAssignment@odata.bind`). It does
not write `sprk_regardingrecordid`, `sprk_regardingrecordname`, `sprk_regardingrecordurl` or
`sprk_RegardingRecordType@odata.bind`, though `sprk_event` has them (`docs/data-model/` sprk_event, lines 96-101) and
the internal create writes them (`Api/Events/EventEndpoints.cs` ~`:955-958`).

**Effect.** An event created from the external SPA is missed by any read keyed on the resolver fields, e.g.
`GET /api/v1/events?regardingRecordId={matterId}` in the internal app. Pre-existing on the project route since R3;
task 002 extends the same payload to matters and work assignments.

**Why deferred.** Task 002's contract is "the project route behaves exactly as before"; fixing it changes the write
path for all three roots (record-type lookup resolution, name/url derivation) and the payload assertions in
`ExternalDataServiceTests`. The code remark that claimed the fields were a `sprk_todo`-only construct was corrected
in task 002.

**Fix shape.** Reuse the resolver-field writer the internal `EventEndpoints` uses (no new helper), add the four
fields in `BuildEventCreatePayload`, extend `ExternalRootEventCreateContractTests` to assert them, and add a
data-mutation test. Owner of the decision: R4 (candidate for a P1 follow-up task) or the event/write-path owner.

## D-2 — External event create's `Location` header names a route that does not exist

**Where.** `CreateEventForRoot` in `Api/ExternalAccess/ExternalProjectDataEndpoints.cs` (inherited from the project
route): `Results.Created($"/api/v1/external/{segment}/{id}/events/{eventId}", …)`.

**What.** No `GET …/events/{eventId}` route is mapped on the external plane; a client that follows `Location` gets
404. The SPA does not follow it today. `ExternalRootEventCreateContractTests` pins the current value so a change is
deliberate.

**Fix shape.** Either point `Location` at an existing read (the list route) or drop it and answer 201 with the body
only — a contract change for all three roots, so it needs the owner's choice.

## D-3 — Documents-list `canCreate` ceiling is tested for service requests only

**Where.** `UploadCeiling` map in `ExternalProjectDataEndpoints.cs`;
`tests/integration/contract/Api/ExternalAccess/ExternalRecordDocumentRoutesTests.cs`
(`List_ForAWorkforceContact_CanCreate_FollowsTheUploadRoutesCeiling`).

**What.** Task 002 made the documents list's `canCreate` apply the upload route's workforce ceiling. The over-the-wire
test covers the service-request root (the only root the document-route fixture serves); project, matter and work
assignment use the same map entry shape but are not driven over HTTP.

**Fix shape.** Extend the document-route fixture to serve a project/matter/WA document list for a workforce contact
and add the three cases.

## D-4 — Copy link omits the `?customer=` key the grant email carries

**Where.** `recordLinkFor` in `src/client/external-spa/src/components/detail/record-targets.ts`, called by Copy link in
`components/record/RecordTemplateModal.tsx` (task 003).

**What.** Task 003's POML fixes the link as `{origin}{path}?record={token}:{id}`, the inverse of `parseRecordLink`.
The grant email's link is `{PortalUrl}/?customer={key}&record={type}:{id}` (`GrantAccessNotifier.cs` ~`:546`). The
backend for a fresh tab is chosen at bootstrap (`resolveBackendSelection`): a `?customer=` key wins, else the tab's
active key (sessionStorage, so not carried to a new tab), else the only candidate, else the picker.

**Effect.** A single-customer user is unaffected. A CIAM user with several customers who opens a copied link in a new
tab (or a colleague of theirs) gets the customer picker before the record opens; choosing the other customer shows
"You don't have access to this record". Nothing leaks: the selection fails closed and the read is scoped by the server.

**Why deferred.** The link shape is a spec/POML decision (FR-04); adding the active customer key changes it.

**Fix shape.** If the owner agrees: pass the tab's active customer key (the `backend-selection` seam, never a host)
to `recordLinkFor` and emit `?customer={key}&record=…`, the same shape as the grant email; extend the round-trip
tests. Owner question.

## D-5 — external-spa test files are never type-checked

**Issue: #2699**

**Where.** `src/client/external-spa/tsconfig.json` (`"include": ["src"]`), used by `npm run typecheck`; vitest
transpiles tests with esbuild and does not type-check them.

**What.** No script or CI step type-checks `src/client/external-spa/tests/**`. A type error in a test (a wrong prop on
a component under test, a stale mock shape) passes typecheck, lint and vitest. An ad-hoc check of the task 005 test
files (a temporary tsconfig including them) found one pre-existing error: `tests/DetailDocumentsDelete.test.tsx:412`
TS6133 (unused parameter `path`); none from task 005.

**Effect.** No user-visible effect; tests can drift from the component contracts they exercise without a signal.

**Why deferred.** Outside task 005's scope (a tooling change for the whole SPA test tree); the full test tree may hold
more pre-existing errors that need a ratchet baseline.

**Fix shape.** Add a `tsconfig.test.json` (extends `tsconfig.json`, includes `tests`) and a `typecheck:tests` script;
run it in the SPA CI job with a baseline of the existing errors (new errors fail, known ones listed and worked down),
per root `CLAUDE.md` §16 enforcement ladder.

## D-6 — Create Event dialog collects an event type that is never sent

**Issue: #2703**

**Where.** `src/client/external-spa/src/components/EventsCalendar.tsx`, `CreateEventDialog` (the `eventType` state and
its `Select`; identical at the commit before task 004), and the `POST .../events` body built in `handleSubmit`.

**What.** The dialog shows an Event Type select (default `meeting`) but the request body carries only `sprk_name`,
`sprk_duedate` and the regarding lookup. The choice is dropped.

**Effect.** A user who picks "Deadline" (or any non-default type) gets an event with no type. Task 004 keeps the request
payload unchanged by rule, so this is carried over, not introduced.

**Why deferred.** Sending the type changes the request payload and needs the BFF create endpoint to accept it (it does
not today): a contract change outside task 004.

**Fix shape.** Either send the type and have `BuildEventCreatePayload` map it to the event-type column, or remove the
select. Owner question: which.

## D-7 - SprkModal presets give no focus return for a controlled child dialog

**Issue: #2707**

**Where.** `src/client/shared/Spaarke.UI.Components/src/components/SprkModal/` (the presets); worked around locally in
`src/client/external-spa/src/components/createAction.tsx` (`useChildDialog`, `restoreFocusTo`, `currentFocusTarget`).

**What.** A controlled dialog opened from a toolbar button (not a `DialogTrigger`) returns focus to the page body when it
closes, so the next Esc reaches no dialog and the Esc that should close the record does nothing. (Esc itself is settled:
the owner chose Path C on 2026-11-11, so `FormModal` / `ConfirmModal` keep ignoring Esc per the modal standard and the
earlier local Esc override was removed.)

**Effect.** Without the local workaround a keyboard user who cancels a child loses their place. The workaround lives in
external-spa only; any other host nesting a preset inside a modal from a plain button has the same gap.

**Why deferred.** `src/client/shared/**` is out of scope for task 004 (no edits allowed).

**Fix shape.** Make SprkModal restore focus to the opener on close (and, when the opener is gone, to the enclosing dialog),
then delete the local `useChildDialog` / `restoreFocusTo` helpers.

## D-8 - A child dialog opened within ~250 ms of its parent modal is left aria-hidden

**Issue: #2704**

**Where.** Shared SprkModal / Fluent tabster modalizer; seen with the record modal's child dialogs (task 004).

**What.** When the parent modal opens, tabster's delayed pass (~250 ms) sets `aria-hidden` outside the active modal. A
child opened before that pass finishes is hidden by it. Pre-existing (R3's recall dialog shows it too); tests wait 350 ms
before opening a child and assert the child is not aria-hidden.

**Effect.** Only a very fast keyboard/AT user who opens a child within ~250 ms of the record opening hits it.

**Fix shape.** Confirm in a real browser (task 018 ui-tests); if reproducible, fix in the shared SprkModal with a test that
an immediately opened child is not aria-hidden.

## D-9 — ProjectPage offers creates the project list refused

**Where.** The standalone ProjectPage's own create buttons: `src/client/external-spa/src/components/EventsCalendar.tsx` and `SmartTodo.tsx` (standalone rule `levelAllows ?? listCanCreate === true`), `DocumentLibrary.tsx` (`uploadFromList` is `null` for a level-decided project caller), and the to-do status toggle in `SmartTodo.tsx`. Same gate at the commit before task 006.

**What.** For a project caller without `writeAccessPerRecord`, ProjectPage offers Create Event / Add Task / Upload / the status toggle on the `/me` level alone. The BFF's create and PATCH routes also apply the workforce role ceiling (`WorkforceRoleCeilingGate`), which the `/me` level does not reflect. Since task 002 each project list returns `canCreate` equal to the POST's decision, and ProjectPage ignores a `canCreate: false`.

**Effect.** A workforce contact at Collaborate on a project whose role lacks Create on `sprk_event` sees "Create Event" on ProjectPage; the POST answers 403. Same for Add Task, Upload and the toggle with their own ceilings. A CIAM partner is unaffected (Collaborate maps to Create, so level and list agree). Violates R4 FR-07 / NFR-04 "no action the UI offers may answer 403". The record view (task 006) is not affected.

**Why deferred.** Task 006's POML says both "ProjectPage behaviour is unchanged; its tests pass unchanged" and "[the /me level] must agree with the list when both exist" — conflicting; owner decision needed.

**Fix shape.** Apply the record view's rule (`createRightOf` in `record/recordAccess.ts`) in standalone mode too: offer only when the level allows AND the list's `canCreate` is true. Updates ProjectPage tests. The toggle needs a server PATCH answer (D-10) or stays level-only with the ceiling gap.

## D-10 — No server answer for the to-do PATCH right

**Where.** `src/client/external-spa/src/components/SmartTodo.tsx` (status toggle); BFF `PATCH /api/v1/external/todos/{id}` in `Api/ExternalAccess/ExternalProjectDataEndpoints.cs` (ceiling `WorkforceRoleCeilingGate.UpdateTodo`, Ambiguous-root refusal).

**What.** No server answer tells the SPA whether the caller may PATCH a to-do (no list- or row-level flag). Task 006 therefore keeps the to-do status toggle hidden inside the record view on every root (project, matter, work assignment), so it is never offered to fail.

**Effect.** In the record view nobody can mark a to-do complete, even callers the PATCH would admit. ProjectPage still offers it on the `/me` level (see D-9).

**Why deferred.** Adding the answer is a UAC-reviewed BFF change (task 006 escalation rule: a new server answer is a new task).

**Fix shape.** Add `canUpdate` to the to-do list response (same evaluation as the PATCH: rights + Dataverse confirmation + `UpdateTodo` ceiling; per row if the Ambiguous-root case matters), contract tests list = PATCH, then show the toggle when it is true.

## D-11 — Access-level label switch duplicated in five places

**Where.** The access-level → label switch exists in five places in `src/client/external-spa/src`: `pages/ProjectPage.tsx` ~:177, `components/ContactsOrganizations.tsx` ~:74, `components/OutsideCounselDashboard.tsx` ~:293, `components/InviteUserDialog.tsx` ~:79, and task 006's `components/record/recordAccess.ts` (now exported).

**What.** The same View Only / Collaborate / Full Access labels are hand-written in each.

**Effect.** No user-visible effect today; a label change (or a new level) must be made five times and drifts otherwise.

**Why deferred.** Outside task 006's files.

**Fix shape.** Use the label map exported from `record/recordAccess.ts` (or move it next to `mapAccessLevelString` in `hooks/useAccessLevel.ts`) in all five places; no behaviour change.
