# Task 008 notes — BFF levels on /me, event type "task", isTask

## Gate: uac-r2 UAC review (2026-11-13) — PASS WITH CHANGES

Proposal sent 2026-11-13. It covered: (A) `/me` `records[]`, (B) `eventType` closed set {"task"}, (C) `isTask` on the event lists.

uac-r2's conditions, all folded into the POML constraints:
- **A — source.** The principal's own `MatterAccess` / `WorkAssignmentAccess`, filtered to Read. This is the same derived view as `AccessibleMatterIds` / `AccessibleWorkAssignmentIds`. Write it once as a helper next to `ReadableProjects` and project each entry through `ExternalAccessLevels.ToDisplayLevel`.
  - Why this source: `RightsForRoot` reads exactly those dictionaries, so the label equals what the routes admit, by construction.
  - **A1.** Parity test, including a None key and a vetoed key; their ids must be absent from the response body.
  - **A2.** For `WriteAccessPerRecord` callers the label is the composed READ view. The SPA never gates write actions on it. Add a test.
  - **A3.** Additive only. `projects` is unchanged, and `/me` makes no new reads.
- **B — eventType.** Closed set; any other value is 400 `sdap.event.type_not_allowed`. The Task type id lives in one shared constant.
  - **B1.** For a DataverseConfirmsWrites caller (a systemuser), probe AppendTo on the Task type row first (app-only, naming the systemuser). No → 403 with the existing refusal; unanswered → 503.
  - For a contact, the closed set is the authorization.
  - Test both paths.
- **C — isTask.** Read the lookup server-side only. Emit only the boolean. Add an FR-18 ReadableColumns entry with its review note.
- **D — to-do routes.** Under uac-r2 round 10 item 1, a route with no caller is deleted, not left mapped. The external SPA is the only caller of the external to-do routes, so uac-r2 wants them deleted (ledger and route count updated) in the same change that removes the SPA's last call. Keeping them for a rollback window is an owner call.
  - **This conflicts with the owner directive of 2026-11-12:** the to-do BFF routes stay until a filed retirement.
  - **Owner decision 2026-11-13: keep the routes for now** (rollback option). Retirement is filed as D-13 #2800.

## Implementation record (2026-11-13)

Not committed (the caller commits after review). Nothing deployed. No Azure or Dataverse calls.

### Files changed
- `src/server/shared/Spaarke.Dataverse/Models.cs` — new `EventTypeRef` (`EntitySet`, `TaskId`, `Task`): the ONE shared Task event-type constant (B).
- `src/server/api/Sprk.Bff.Api/Services/Signals/Actions/DecisionActionExecutors.cs` — `EventTypeRefSet` / `TaskEventTypeId` now alias `EventTypeRef` (values unchanged).
- `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/CallerPrincipalResolver.cs` — `CallerPrincipal.ReadableRecordLevels`, next to `ReadableProjects` (A). It reads `MatterAccess` / `WorkAssignmentAccess`, keeps entries whose rights have Read, and projects them through `ExternalAccessLevels.ToDisplayLevel`.
- `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/Dtos/ExternalUserContextResponse.cs` — trailing `Records` param + `RecordAccessEntry(RecordType, RecordId, AccessLevel)`.
- `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/ExternalUserContextEndpoint.cs` — builds `records` after the projects loop. The ledger-cited lines `:46` and `:75` did not move.
- `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/Dtos/ExternalProjectDtos.cs` — `ExternalEventDto.IsTask` (`isTask`); `CreateExternalEventRequest.EventType` (`eventType`).
- `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ExternalDataService.cs`:
  - closed set `TaskEventType = "task"`, `IsCreatableEventType`, errorCode constant;
  - `_sprk_eventtype_ref_value` added to the event-list select and to `EventRow`;
  - `MapEvent` sets `IsTask` (a GUID equal to `EventTypeRef.Task`; masked or malformed → false);
  - `BuildEventCreatePayload` binds `sprk_eventtype_ref@odata.bind` for "task" and throws on any value outside the set (a second line of defence).
- `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/ExternalProjectDataEndpoints.cs` — `CreateEventForRoot`:
  - 400 for a type outside the set;
  - B1 probe `TaskTypeAppendToRefusalAsync` through the existing `CallerRightsReader` (app-only, naming the systemuser);
  - the existing 403 is factored into `DenyEventCreate` with the same detail and reasonCode.
- Tests:
  - `tests/Spaarke.ArchTests/ExternalLookupAbsentReadGuardTests.cs` — `EventRow` reviewed-site count 3→4, with a reason;
  - `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs` — the GET `/me` reason cites `:86` (`ReadableRecordLevels`);
  - `tests/integration/contract/Api/ExternalAccess/ExternalRootEventReadTests.cs` — row shape + shipped-query pin;
  - new: `ExternalMeRecordLevelsContractTests.cs`, `ExternalEventTypeCreateContractTests.cs`, `ExternalEventIsTaskReadContractTests.cs` (same folder).

### Wire contract
- `GET /api/v1/external/me` adds `records: [{ "recordType": "matter" | "workassignment", "recordId": "<guid>", "accessLevel": "ViewOnly" | "Collaborate" | "FullAccess" }]`. The handler always emits it (empty when there are none). `projects` is unchanged.
- `POST /api/v1/external/{projects|matters|workassignments}/{id}/events` takes an optional `"eventType": "task"` (exact, case-sensitive).
  - Any other value, including `""`, `"Task"`, `" task"` and the GUID → 400 ProblemDetails, `errorCode` `sdap.event.type_not_allowed`. Nothing is created.
  - Omitted → today's behaviour; no type is bound.
- The three event lists add `"isTask": true | false`. The lookup GUID never reaches the wire.
- B1 adds no errorCode; it reuses existing reasonCodes:
  - no AppendTo → 403 `sdap.access.deny.insufficient_rights` (the existing create refusal);
  - no answer → 503 `sdap.access.dataverse_unavailable`.

### Decisions
- **Order in `CreateEventForRoot`:**
  1. the existing write gate (a ViewOnly caller is still a 403 before the type is considered);
  2. the closed-set 400;
  3. due-date validation;
  4. the B1 probe (only for `eventType == "task"` and `DataverseConfirmsWrites`).

  Contacts are not probed: the closed set is their authorization.
- **B1 probe = the existing `CallerRightsReader`**, not Signals' `CallerHoldsAsync`. That one is OBO, and OBO is banned on the external plane (`ExternalPlaneNoOboGuardTests`). The probe reads `(sprk_eventtype_refs, Task)`. A thrown error or a null answer → 503; rights without AppendTo → 403. Logs carry the root kind and TraceId only.
- **A1 "vetoed key" modelling.** The principal prunes None entries. A vetoed key is therefore modelled as rights present without Read (`Write|Create|Delete`). Such a key is absent from `records` and its read route refuses it. An off-grid `Read|Write` entry displays as `ViewOnly` (ToDisplayLevel).
- **A2.** A `WriteAccessPerRecord` caller gets the composed READ view. Tested; the flag is false for contacts.
- **DEVIATION — C's "ReadableColumns entry".** `ExternalAccessModule.cs` has no `sprk_event` module. Event lists are built by `BuildEventsQuery` directly, not through the generic module seam. Adding a `sprk_event` module only to hold this column would open `sprk_event` to the generic readable-columns path, which widens the external read surface. So I did not add an entry. Instead:
  - the FR-18 / UAC review note ("UAC review uac-r2 2026-11-13, task 008") sits at the `BuildEventsQuery` select;
  - the column is pinned by the shipped-query test and the lookup-read ratchet.

  uac-r2 should confirm this placement satisfies C, or say where the entry should live.

### Test results
- New tests: 50/50 pass.
  - `ExternalMeRecordLevelsContractTests`: 12.
  - `ExternalEventTypeCreateContractTests`: 29. Must-fire/must-not-fire pairs: typed vs untyped create; outside-set 400 vs "task" 201; probe-AppendTo yes 201 / no 403 / unanswered 503 vs untyped-not-probed and contact-not-probed.
  - `ExternalEventIsTaskReadContractTests`: 9 (true only for the Task type; masked or malformed → false; GUID never on the wire).
- Fail-before: with the three source files reverted to HEAD, 30 failed / 64 passed of 94. The passes are the must-not-fire and DTO-shape tests. All pass after.
- Affected suites, final run: 1070/1070 pass.
- Broader `category=external-access` + ExternalAccess filter: 6595/6595 pass (run before the final doc-comment tweaks).
- ArchTests:
  - targeted (RouteAuthorization, NoObo, LayerDependency, ProblemDetails, ExternalAccess): 166/166;
  - first full run: 2542/2543. The one failure was the lookup-read ratchet `3d`, fixed by recording the reviewed `EventRow` site;
  - re-run of the guard + route ledger: 133/133.
- The full BFF suite was not run (per the brief).

### Build, publish size, CVE
- `dotnet build src/server/api/Sprk.Bff.Api --no-incremental`: 0 warnings, 0 errors.
- Publish size (Release, framework-dependent linux-x64, `Compress-Archive` Optimal, PDBs included (4)). Both sides were built from fresh short-path worktrees, since removed with `Remove-WorktreeSafe.ps1`:
  - master `3ae2a63362`: 191 files, 42,093,112 bytes (40.143 MB);
  - branch (HEAD `15dca35e03` + these 8 BFF/shared files): 191 files, 42,097,973 bytes (40.148 MB);
  - **delta +4,861 bytes (~+0.005 MB)**. File counts match.
- `dotnet list src/server/api/Sprk.Bff.Api package --vulnerable --include-transitive`: no vulnerable packages. No package changes.
- No new DI registration, package or background work.

### Out-of-scope observations (not fixed)
1. **Task type id still duplicated.** `Services/Ai/.../TaskActionCore.cs:68`, `DailyBriefingCollector.cs:128` and `PortfolioService.cs:99` keep private copies of the Task event-type GUID.
   - Failure scenario: if the Task type row is re-keyed (for example, a customer environment seeds it with a different id), the external create and Signals follow `EventTypeRef`. The AI task action, the daily briefing and the portfolio keep the old id: they create or count tasks of a type that no longer exists, or miss tasks silently.
   - Candidate for consolidation onto `EventTypeRef`; owned outside this project.
2. **`isTask` on the create response is untested.** The contract stubs return a DTO built by the stub, not by `MapEvent`.
   - Failure scenario: if the create path ever maps the created row without the type column, a 201 for `eventType:"task"` returns `isTask:false` until the list is re-read. No test would catch it.
   - The list path is covered by the real `ExternalDataService` over FakeDataverse.

## UAC confirmation on condition C (uac-r2, 2026-11-13)

**Confirmed: no `sprk_event` module.** uac-r2 confirmed that leaving out a `sprk_event` module in ExternalAccessModule satisfies condition C, and is the better shape, because adding one would open the generic readable-columns path. The controls that stand in for it:
- the review note at the `BuildEventsQuery` select;
- the shipped-query pin;
- the `ExternalLookupAbsentReadGuardTests` count (EventRow 3→4);
- `isTask` is the only value on the wire.

**Accepted as described:**
- A1: keep the rights-without-Read case in the parity test.
- B1.
- The publish size (+4,861 B).

The diff goes to uac-r2 after the internal review round.

## Review round 1 (2026-11-13)

Still not committed. No SPA files, nothing under `.claude/`.

### Fixes
- **F2-1 (FR-07) — `canCreateTask` on the three event lists.**
  - New list field `"canCreateTask": boolean`, additive. It is on `ExternalCollectionResponse<T>` as nullable with WhenWritingNull, so it is omitted on every other list (pinned by a to-do list test).
  - `canCreate` is unchanged and still means an untyped Add event.
  - Semantics: `canCreateTask = canCreate && (contact → true; Dataverse-decided caller → AppendTo on the Task row of sprk_eventtype_ref)`.
  - The probe runs only when `canCreate` is true and the caller is Dataverse-decided: one per list call, app-only, naming the systemuser.
  - An unanswered or thrown probe → `false`. The list is still 200.
  - **uac-r2 addendum (a), same helper, not a copy.** `TaskTypeAppendToAsync` (returns `bool?`: AppendTo yes / no / no answer) is the ONE check:
    - the create's `TaskTypeAppendToRefusalAsync` maps it to null / 403 / 503;
    - the list maps it to true / false / false.
  - Parity test: a systemuser denied AppendTo on the Task row gets list `canCreateTask=false` AND a 403 on the Task create; one who holds it gets `true` AND 201. Covered on all three roots.
  - **uac-r2 addendum (b), logging.** An unanswered probe (null answer or thrown read) is logged at **Information** with `TraceId` and the root kind only: no tenant, oid, UPN, email or token. The create's 403 refusal log also gained the TraceId (still Warning).
- **F2-2.** Dropped the true-by-construction `DecisionExec.TaskEventTypeId == EventTypeRef.TaskId` assertion. The test is renamed `EventTypeRef_Task_IsTheLiveTaskRow` and pins `EventTypeRef.Task` to the independently written live GUID, plus the entity set.
- **K4 closed.** `FakeDataverse` (in `ExternalRootEventReadTests.cs`) now answers `POST sprk_events` as Dataverse does:
  - with `Prefer: return=representation` → 201 with the created row, each `{nav}@odata.bind` returned as `_{nav}_value`;
  - without the header → 204, no body.

  New test `CreateEventAsync_ReturnsIsTask_FromTheCreatedRow` (×2, "task" → true, untyped → false) drives the REAL `ExternalDataService.CreateEventAsync` over it.
- **Stale comment.** `ExternalUserContextEndpoint.cs:133` now cites ledger lines `:46, :75, :86`.
- **Adjusted test (not mine, uac-r2's file).** `tests/integration/auth/UnifiedAccessControl/ExternalTodoScopeTests.cs` `Fr20_RootEventList_CanCreate_IsTheCreateRoutesDataverseAnswer` asserted "one app-only probe" on the event list. With `canCreate` true the list now also asks the Task row. The assertion now says:
  - the root is still probed once;
  - the Task row is probed once when `canCreate` is true, and zero times otherwise.

  The intent is unchanged.

### Files changed this round
- `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/Dtos/ExternalProjectDtos.cs` — `CanCreateTask`.
- `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/ExternalProjectDataEndpoints.cs`:
  - `ListEventsForRoot` sets `CanCreateTask`;
  - `TaskTypeAppendToAsync` is the shared check;
  - `TaskTypeAppendToRefusalAsync` maps it and now takes the trace id.
- `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/ExternalUserContextEndpoint.cs` — comment only.
- `tests/integration/contract/Api/ExternalAccess/ExternalEventTypeCreateContractTests.cs` — F2-2 rename; +12 test cases.
- `tests/integration/contract/Api/ExternalAccess/ExternalEventIsTaskReadContractTests.cs` — +2 test cases (K4).
- `tests/integration/contract/Api/ExternalAccess/ExternalRootEventReadTests.cs` — `FakeDataverse` create branch.
- `tests/integration/auth/UnifiedAccessControl/ExternalTodoScopeTests.cs` — the probe-count assertion above.

### Tests added this round (14 cases)
- `List_CanCreateTask_ForADataverseDecidedCaller_IsAppendToOnTheTaskType_AndTheTaskCreateAgrees` (×6: 3 roots × held/denied; this is the parity test).
- `List_CanCreateTask_WhenTheTaskTypeProbeIsUnanswered_IsFalse_AndTheListStill200`.
- `List_CanCreateTask_ForAContact_EqualsCanCreate_WithoutProbing` (×3: CIAM Collaborate, CIAM ViewOnly, workforce contact).
- `List_CanCreateTask_WhenCanCreateIsFalse_IsFalse_WithoutProbingTheTaskType`.
- `TodoList_DoesNotCarryCanCreateTask` (must-not-fire: the field is on the event lists only).
- `CreateEventAsync_ReturnsIsTask_FromTheCreatedRow` (×2).

### Results
- BFF build (`--no-incremental`): 0 warnings, 0 errors. Test project build: 0 warnings, 0 errors.
- Fail-before:
  - with `CanCreateTask = canCreate` (probe answer ignored), 4 of 41 fail in `ExternalEventTypeCreateContractTests`: the three denied-parity cases and the unanswered case;
  - all pass after restore.
- Affected classes (`ExternalEventTypeCreate`, `ExternalEventIsTaskRead`, `ExternalMeRecordLevels`, `ExternalRootEventRead`, `ExternalRootEventCreate`): 151/151.
- Wider run (`category=external-access` | `ExternalAccess` | `UnifiedAccessControl`): 6655/6655. The first pass found the 2 `Fr20_RootEventList_CanCreate…` cases described above; fixed.
- ArchTests (`ExternalLookupAbsentReadGuard`, `RouteAuthorizationGuard` incl. ledger, `ExternalPlaneNoObo`, `ProblemDetails`): 199/199.
- Publish size: not re-measured. This round adds source only (about 60 lines in two BFF files) and no package, so it is unchanged within noise vs round 0's +4,861 bytes.

## UAC diff review (uac-r2, 2026-11-13): PASS on c9b9492c86

uac-r2 checked each of these and passed it:
- `/me` records: their source, the read filter on them, and the parity test.
- Create: the order of checks (root rights → validation → closed-set 400 → B1 → owner → write).
- One shared helper (`TaskTypeAppendToAsync`) backs both `canCreateTask` and the create, and the unanswered case is logged.
- One constant for the Task type id (`EventTypeRef`).
- Only the `isTask` boolean leaves the server.
- The edit to `ExternalTodoScopeTests` is confirmed.
- Publish size (+4.9 KB) and the CVE check (none) are accepted.

**Next.** Merge on our gates. The change reaches dev in uac-r2's next master + spike deploy, after the owner approves the exact SHA.
