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
| D-9 | 2026-11-11, task 006 review (F1, pre-existing, owner question) | ProjectPage offers creates the project list refused (workforce ceiling → 403) | Defect (user-visible) — **resolved in R4** (owner folded it in, 2026-11-11) | #2716 (closed) |
| D-10 | 2026-11-11, task 006 | No server answer for the to-do PATCH right; status toggle hidden in the record view | Feature gap (BFF) | #2717 |
| D-11 | 2026-11-11, task 006 review pass 2 (F2, out of scope) | Access-level label switch duplicated in five places | Maintainability | #2718 |
| D-23 | 2026-11-20, wave 4 review (F2, out of task 022's scope) | Document preview and download each carry their own parent-order loop | Maintainability — fold into task 023 | #2928 |

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

**Resolved 2026-11-11 (owner: fold into R4).** ProjectPage now uses the record view's one rule (`listCreateRightOf` in
`record/recordAccess.ts`): Add Task / Create Event / Upload are offered only when the `/me` level allows AND the list has
answered AND `canCreate` is not false. The status toggle is excluded (no server answer; D-10 #2717). Detail:
`notes/task-006-notes.md` "D-9 follow-up".

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

## D-12 — Record Documents section columns overlap at the modal width (#2769)

**Where.** `src/client/external-spa/src/components/record/sections/DocumentsSection.tsx` (the document list inside `RecordTemplateModal.tsx`).

**What.** Found in the owner's E2E on dev (2026-11-12, task 018): the file name runs into the Type badge and the Date Added column.

**Effect.** Document name, type and date are hard to read in every record with documents.

**Why deferred.** Owner decision 2026-11-12: finish the E2E first, then one rework pass.

**Fix shape.** In the post-E2E rework task: column widths / truncation at the modal width, with a layout test.

## D-13 — Retire the external to-do BFF routes (#2800)

**Where.** The external to-do routes (list / create / PATCH on projects, matters and work assignments) in `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/**`.

**What.** R4 task 009 removes To Do from the SPA (owner directive 2026-11-12). The SPA was the only client caller, so after that the routes have no caller.

**Effect.** None for users: the routes stay mapped and authorized but unused.

**Why deferred.**
- uac-r2 (round 10 item 1) asked that the routes be deleted in the same change.
- **Owner decision 2026-11-13: keep them for now** as a rollback option while Task-typed events (tasks 008/011) go live.

**Fix shape.**
- After the R4 deploy and the owner retest, re-check that the routes have no callers.
- Delete the routes.
- Update the route ledger and the external route counts.
- Remove the routes' contract tests, with the ADR-038 A3 evidence.

## D-14 — Task event-type GUID still duplicated in three BFF files (#2809)

**Where.** `TaskActionCore.cs:68`, `DailyBriefingCollector.cs:128`, `PortfolioService.cs:99`. Each has its own literal Task type id. Task 008 introduced the shared `EventTypeRef.Task` (`Spaarke.Dataverse/Models.cs`).

**Effect.** Nothing changes today. If the id differs per environment or the constant changes, the AI task action, the daily briefing and the portfolio silently create tasks of the wrong type, or miss them.

**Why deferred.** These files are outside R4 and belong to other projects.

**Fix shape.** Replace the three literals with `EventTypeRef.Task`. Optionally add an ArchTest that bans the literal elsewhere.

## D-15 — Project header icons and dashboard Tasks button do nothing

**Where.** The external SPA project page header (Mail, Tasks and Notifications icons) and the dashboard's "Tasks" button. The code already worked this way before task 009.

**Effect.** Clicking one of them does nothing, which reads as a dead action. That breaks the hide-don't-disable rule.

**Why deferred.** Found by task 009; outside its scope.

**Fix shape.** Wire each control to its destination (Tasks after task 011) or hide it. Add a test.

## D-16 — Shared DataGrid: failed command handler and failed bulk delete are silent (#2823)

**Where.** `Spaarke.UI.Components/src/components/DataGrid/commandBar/CommandBar.tsx`: the `invokeItem` handler catch and the `performBulkDelete` catch. Both are older than R4 and recorded in the client-lint suppression baseline. Task 010 part 3 touched the file, so the CI rule reported them.

**Effect.** If a delete is refused, the confirm dialog closes, the rows stay, and the user sees no message. Other failed commands are also silent.

**Why deferred.** It needs a new user-visible error surface in the shared grid. That is outside R4 scope.

**Fix shape.** Add an error surface to the grid (an inline MessageBar or an `onCommandError` prop) and route both catches to it. Add tests, then prune the suppressions.

## D-17 — External API: every request repeats access resolution (#2845)

**Status.** Handed off → `unified-access-control-r2`, on the owner's direction, 2026-11-15. Accepted by uac-r2 the same day; it will run FULL rigor on Opus with two adversarial passes, once its owner gives the go. Filed as an issue (found outside R4), not as deferred R4 scope.

**Where.** The shared external access layer in the BFF:
- `Infrastructure/ExternalAccess/NoAccessListReader.cs:182` (deny list, deliberately uncached across requests);
- `ExternalParticipationService.cs`, `AccessibleRecordSetService.cs`, `CallerPrincipalResolver.cs`, `Services/Dataverse/ParentLineage.cs`.

**Effect.** Every external request takes 3–4 s on the server: 50–60 Dataverse calls one after another at about 85 ms each. Within one request, `sprk_noaccessentries` is read 8 times, Organization.svc is called 6.7 times, `sprk_matters` is read 6.5 times, and `sprk_contactorganizations` and `contacts({id})` 4 times each. The route's own data is 1–2 calls. The owner reported the SPA as very slow. It isn't capacity: P1v3, 1 worker, `/ping` about 0.1 s.

**Source.** App Insights `spe-insights-dev-67e2xz`, read-only queries shown to the owner first, 2026-11-15. The KQL is in #2845.

**Fix shape.** Keep "live per request" (no cross-request TTL), but memoize the caller, grant set, deny list and lineage once per request, run the independent reads in parallel, and batch the per-candidate lookups. Add a test that counts Dataverse calls per request. Target: under 1 s.

## D-18 — RequestAccessMemo.For races on a fresh HttpContext.Items (#2851)

**Status.** Handed off → `unified-access-control-r2` (accepted 2026-11-16; fixed in #2845 round 2, branch `perf/2845-round2`). 014a keeps its local pre-create.

**Where.** `Infrastructure/ExternalAccess/RequestAccessMemo.cs:38`. The lock on `context.Items` doesn't cover the lazy creation of `Items` itself.

**Effect.** When concurrent compositions are the first to touch `Items` in a request, the dictionary can be corrupted (the read is treated as unknown, giving a 503) or the memo can be lost (the #2845 saving silently disappears). Task 014a's memo test reproduced it in 3 of 4 runs.

**Fix shape.** Create the memo once, single-threaded, before any fan-out (in a filter or middleware, or at the evaluator's entry). Add a concurrency test on a fresh `DefaultHttpContext`. 014a works around it locally.

## D-19 — Assignee picker omits workforce contacts with standing access (#2854)

**Status.** Open, next round. Agreed with uac-r2 in its gate review of #2852 (K1, confirmed 2026-11-16).

**Where.** `Infrastructure/ExternalAccess/RecordContactAccessService.cs` (the class remarks document the exclusion). The terms it leaves out are at `AccessibleRecordSetService.cs` ~2888-3017 (standing-grant / derived-member, ContactOnly plane only).

**Effect.** A workforce contact-only user with standing access can see the record, but:
- is not offered as an assignee;
- gets `isMe` = false.

This under-lists, which fails closed.

**Why deferred.** The service can't tell which plane a contact signs in on. Including the terms for every contact would over-list CIAM sign-ins.

**Fix shape (D-19).** A way to know a contact's sign-in plane (for example a workforce-bound marker). Then add the standing-grant / derived-member terms for those contacts only. Needs uac-r2's review.

## D-20 — QueryAsync turns a malformed 2xx into an empty list (#2860)

**Status.** Handed off → `unified-access-control-r2`, which proposed it and owns the follow-up (2026-11-16). Filed as an issue, not deferred R4 scope.

**Where.** `Spaarke.Dataverse/DataverseWebApiClient.cs`, `QueryAsync<T>` (`result?.Value ?? new`). It has 87 callers.

**Effect.** A 2xx response whose `value` is missing or null reads as "no rows". An access-relevant read can then give a believable wrong answer instead of an error.

**R4 part.** 014a adds `QueryStrictAsync<T>` and moves only `ExternalGrantLifecycle.QueryActiveRowsForRootAsync` to it. The owner chose this option (A) on 2026-11-16.

**Fix shape.** Audit the remaining callers. Move the access-relevant ones to the strict method, checking that each fails closed on a throw.

## D-21 — External event create with a type answers 500: wrong-case event-type bind key (#2892)

**Status.** Fixed in the task 016 fix PR (BFF). Found in the owner E2E, 2026-11-17 (matter root, type Task).

**Where.** `Infrastructure/ExternalAccess/ExternalDataService.cs` `EventTypeBindKey`, introduced by task 008 (c9b9492c86).

**Effect.** Every external event create that names a type wrote `sprk_eventtype_ref@odata.bind`. `@odata.bind` keys are the navigation property name and are case-sensitive; for `sprk_event` it is `sprk_EventType_Ref` (metadata-verified in 9b32c1c8ab, and the internal create already uses it). Dataverse refused the write, and the BFF answered 500 `InvalidOperationException`. The R4 form always sends a type, so every create failed.

**Why the tests missed it.** The contract tests pinned the same wrong literal; nothing compared it with the metadata or with the internal create.

**Fix.** The key is `sprk_EventType_Ref@odata.bind`; the three contract pins are corrected; `tests/integration/regression/Issue2892_ExternalEventTypeBindKeyTests.cs` pins the payload key (the internal create's key, `sprk_EventType_Ref`), with a must-not-fire pair for an untyped create.

## D-22 — Signals assign-task writes the event-type bind key in the wrong case (#2893)

**Status.** Closed, not a defect (2026-11-17, ontology-r1; checked against the source). The Signals executors write through the child-records core, which runs `DataverseWriteItemMapper.MapWebApiPayloadAsync`; it matches each `@odata.bind` key to metadata case-insensitively (~305-333) and sends the metadata navigation name. The external create (D-21) posts directly without that mapper, which is why only it failed.

**Where.** `Services/Signals/Actions/DecisionActionExecutors.cs` ~1509 (lowercase keys, mapped before the write).

## D-23 — Document preview and download each carry their own parent-order loop (#2928)

**Where.** `readDocumentFromParents` in `src/client/external-spa/src/components/record/sections/DocumentPreview.tsx` (task 022) mirrors `downloadDocumentFromParents` in `src/client/external-spa/src/components/DocumentLibrary.tsx`.

**Why it matters.** A change to the parent rule in one copy only makes Preview and Download disagree on which parent serves the file.

**Fix.** Task 023 hoists one `readFromParents(parents, read)` helper used by both (task 022 could not edit `DocumentLibrary.tsx`). **Done in task 023c** (2026-11-22): `readFromParents` in `DocumentLibrary.tsx`, used by `downloadDocumentFromParents` and `DocumentPreview`; the 023c PR closes #2928.

## D-24 — Shared DataGrid: a failed FetchXML page load shows an empty list (#2939)

**Where.** `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/DataGrid.tsx`: `useLazyLoad`'s `error` is never read (~:1149), so a failed row page renders "No records". Found by the task 023a implementer, 2026-11-22.

**Why it matters.** Every grid on the FetchXML path (34 consumers) tells a user whose load failed that there are no records, with no retry prompt.

**Fix.** Reuse task 023a's row-error banner for the FetchXML path, with a must-fire / must-not-fire test pair. It was kept out of 023a because it changes visible behaviour for every existing grid. Status: Open.

## D-25 — Move the record Invoices section, Events list and invoice Files onto the shared grid seams (#2960)

**What.** R4 task 023c put the external record Documents list on the shared DataGrid through the new host seams (rowSource, rowActions, truncateWithTooltip, rowNoun; task 023a). The record Invoices section, the Events list and the invoice Files list still use hand-rolled Fluent tables, so they lack column resize and ellipsis + tooltip, and each keeps its own width rules.

**Why it matters.** Long names and summaries in those lists stay truncated with no way to read them, and columns overlap at narrow widths, the same faults #2490 fixed for Documents.

**Fix.** One grid configuration per list (like config/external-record-documents.gridconfiguration.json plus its apply script and BFF id admission), rows from the existing record routes as rowSource, actions as rowActions. Status: Open. Issue: https://github.com/spaarke-dev/spaarke/issues/2960 (board Type=Idea).

## D-26 — Shared grid row actions do not name their row for screen readers (#2961)

**What.** The shared DataGrid row-actions column (RowActionsCell.tsx, R4 task 023a) labels each button with the static action label. In the external Documents list every row has "Download", "Version history" and "Delete" buttons with the same accessible name. Before task 023c they were "Download <document name>".

**Why it matters.** A screen-reader user moving by buttons, or reading the buttons list, hears N identical "Download, button" entries and cannot tell which document each acts on.

**Fix.** Let DataGridRowAction take an optional ariaLabel(record) function (the tooltip keeps the short label); the Documents list passes "Download <name>". Additive, with a must-fire / must-not-fire test. Status: Open. Issue: https://github.com/spaarke-dev/spaarke/issues/2961 (board Type=Bug).

## D-27 — External Documents: Delete is offered to a workforce user whose role cannot delete documents (#2962)

**What.** The external Documents list shows Delete on the caller's own rows when the list grants write (canCreate). For a workforce caller the server also applies the workforce role ceiling per privilege (WorkforceRoleCeilingGate), so a role with Create but not Delete on sprk_document gets a Delete that the server refuses.

**Why it matters.** An offered action answers 403, which breaks the R4 rule that no action the UI offers may answer 403 (FR-07).

**Fix.** The documents list answers a ceiling-aware canDelete beside canCreate; the SPA shows Delete only when it is true. Needs UAC review (uac-r2). Status: Open. Issue: https://github.com/spaarke-dev/spaarke/issues/2962 (board Type=Bug).

## D-28 — The external record Documents grid configuration is not applied by the release or provisioning path (#2963)

**What.** Since R4 task 023c the external record Documents list needs grid configuration 286be8b4-f96f-4d5a-b1d5-82fd83ca9693 in Dataverse. It is applied by scripts/Apply-ExternalRecordDocumentsGridConfiguration.ps1, which nothing runs automatically. Deploy-Release.ps1 already runs Apply-WorklistGridConfiguration.ps1 for the worklist grid, but not this one.

**Why it matters.** In any environment where the row was not applied by hand (a newly provisioned customer), every record's Documents section shows a load error and no Upload.

**Fix.** Add the apply + -Verify -RequireRow step to Deploy-Release.ps1 next to the worklist one, and to the customer provisioning run. Status: Open. Issue: https://github.com/spaarke-dev/spaarke/issues/2963 (board Type=Bug).

## D-29 — external-spa test suite: eight sign-in and Teams test files time out in a full run (#2964)

**What.** In a full vitest run of src/client/external-spa, tests in invite-code, workforce-auth, workforce-home-auth, no-mock-identity, teams-home-auth, teams-real-msal, InvoiceSubmitPage and DocumentUploadPage.externalUpload intermittently fail after about 86 s. The same eight files pass 452/452 when run on their own. Seen twice on 2026-11-22 (R4 task 023c); not established whether it predates 023c. No CI workflow runs this suite (deploy-external-spa.yml only builds).

**Why it matters.** A full local run is red for reasons unrelated to the change under test, so a real failure in those files can be dismissed as the known flake, and nothing in CI catches it.

**Fix.** Find the slow shared setup (likely dynamic MSAL/app imports under worker contention): raise those files' timeout or isolate them (poolOptions / sequence), then add the suite to a CI job. Status: Open. Issue: https://github.com/spaarke-dev/spaarke/issues/2964 (board Type=Bug).


## D-30 — External SPA: move the remaining hand-rolled empty states onto the shared EmptyState (#2975)

**What.** R4 task 024 (FR-17) moved the record lists (Documents, Events, Messages, Invoices, version history) onto the shared EmptyState from @spaarke/ui-components. Empty states outside FR-17's named files are still hand-rolled: OutsideCounselDashboard.tsx defines its own local EmptyState component (a copy of the shared one), and ContactsOrganizations.tsx, IssuedGrantsList.tsx, shell/NotificationBell.tsx, shell/QuickStartPane.tsx, shell/WidgetLibraryModal.tsx and shell/PortalWorkspaceShell.tsx each draw their own.

**Why it matters.** The same "no items" state looks different from page to page in the external SPA, and some of these do not announce the state to assistive tech (no role="status"), so a screen-reader user is not told a list is empty. The local copy in OutsideCounselDashboard.tsx duplicates a shared component and drifts from it.

**Fix.** Replace each with EmptyState (size compact for lists and panels), keeping wording and any action-conditional hint; delete the local copy and its emptyState* styles. Keep loading, error and truncated states as they are. Status: Open. Issue: https://github.com/spaarke-dev/spaarke/issues/2975 (board Type=Idea).

## D-31 — Shared DataGrid: column minimums cut short header labels at narrow widths (#3008)

**What.** The shared DataGrid's column minimum is `max(80, 0.5 x configured width)`, applied to the border box. That leaves about 30px for the label once padding and the sort chevron are taken off. With the 128px row-actions column, or in a narrow container, every column sits at its minimum. In the external record Documents grid at 1000px, the headers read "Ty…"/"D…" and dates are cut. The fitting only started running with task 028's PR #3000, so every host now shows this squeeze at narrow widths.

**Why it matters.** Users can't read the narrow columns' header names or dates without dragging them wider.

**Fix.** Base the minimum on the header label (about label width + 56px). Give wide free-text columns a lower floor (e.g. 0.35 x width). Status: Open. Issue: https://github.com/spaarke-dev/spaarke/issues/3008 (board Type=Bug).

## D-32 — External record Documents grid: empty band above the grid header (#3009)

**What.** In the record modal, an empty rounded band is drawn above the Documents section's grid header (shared DataGrid with host row source). It was seen in the task 028 E2E on 2026-11-23. Not investigated yet. The likely cause is an empty toolbar or message wrapper rendered when the host turns the command bar off.

**Why it matters.** It looks broken and wastes height in the record modal.

**Fix.** Don't render the wrapper when it has no content, and add a test that nothing empty is drawn above the header. Status: Open. Issue: https://github.com/spaarke-dev/spaarke/issues/3009 (board Type=Bug).

## D-33 — FetchService comment says the client normalizes SDK values; it does not (#3010)

**What.** The comment above `FetchService.ProjectEntity` (about lines 141-144) says `BffDataverseClient` normalizes SDK values. `BffDataverseClient.ts` (~:403) returns the JSON as is. So module-fetch rows carry option-set and Money values as `{ value: n, extensionData: null }`.

**Why it matters.** The comment led task 020 to read bare numbers, which was the cause of the Review detail and closed-request Delete defects fixed in PR #3000. The next reader could repeat the mistake.

**Fix.** Correct the comment and point it to `rawWholeNumberOf`. It's comment-only, so make it part of the next R4 BFF PR. Status: Done (task 031, 2026-11-23: the `ProjectEntity` remarks now state the wire shape and point to `rawWholeNumberOf`; its summary's wrong key name, `FormattedValues` for `@formattedValues`, is fixed too. The issue is closed by the main session). Issue: https://github.com/spaarke-dev/spaarke/issues/3010 (board Type=Bug).

## D-34 — Shared DataGrid: CSV export writes option-set and nameless lookup cells as JSON (#3045)

**What.** The shared DataGrid's export writes an option-set cell, and a lookup with no name, as raw JSON (`{"value":1,"extensionData":null}`). Module-fetch rows carry SDK objects; the grid shows the `@formattedValues` label, but `csvExport.ts`'s object fallback (~:80) stringifies the value. Task 033 fixed Money only (FR-20 scope); its render test pins the current behaviour.

**Why it matters.** An exported status or type column holds JSON instead of the label the user sees.

**Fix.** Extend the grid's `exportCellText` (added in task 033) to return the `@formattedValues` label, else the lookup name, for every column with one; update the pinned test. Needs the dataset-grid owners' OK. Status: **Done** (PR #3055, merged 2026-11-24 `8dd44cf528`: every cell exports the text it shows, dates as shown, export text hardening). Issue: https://github.com/spaarke-dev/spaarke/issues/3045 (board Type=Bug).

## D-35 — Shared DataGrid: currency cells without a server label are always shown as USD (#3046)

**What.** `renderCellValue`'s `currency` case (`DataGrid.tsx` ~:772-776) formats every amount that has no server label with `currency: 'USD'`. Since task 033 the CSV export reuses that text, so the "$" also reaches exported files.

**Why it matters.** A euro or pound amount without a formatted label is shown and exported as dollars.

**Fix.** Without a label, use the row's currency when known, else show the plain number. Status: Open. Issue: https://github.com/spaarke-dev/spaarke/issues/3046 (board Type=Bug).

## D-36 — Shared DataGrid: model-driven hosts may show option-set numbers (OData label annotations not read) (#3050)

**What.** `formattedLabelOf` (`DataGrid.tsx` ~:728) reads labels only from `@formattedValues`. Rows from `XrmDataverseClient` carry `{column}@OData.Community.Display.V1.FormattedValue` annotations, which nothing in the DataGrid path maps, so under model-driven hosts an option set likely shows and exports its integer, and a `_x_value` lookup column may be empty. Found by the #3045 fix review (2026-11-24); medium confidence, not verified live.

**Why it matters.** Model-driven grid users may see numbers instead of labels, in the cell and the export.

**Fix.** Verify on a dev model-driven grid; if confirmed, read the annotation forms in `formattedLabelOf` with tests for both row shapes. Status: Open. Issue: https://github.com/spaarke-dev/spaarke/issues/3050 (board Type=Bug).
