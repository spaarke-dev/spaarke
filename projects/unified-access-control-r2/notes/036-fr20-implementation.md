# Task 036 — FR-20 flag-gated swap: implementation notes

> **Branch**: `feat/uac-036-fr20-flag-gated-swap`, cut from R3's tip `origin/work/spaarke-SPA-external-access-platform-r3`
> @ `5b2375f6f` (like #2061). Merges after R3's interim → master PR and after #2061.
> **Binding**: the task POML (`tasks/036-fr20-flag-gated-swap.poml`) — its 2026-09-30 amendments, the owner round 59 block
> (2026-10-05) that sets the rights shape, and the round 75 approval (2026-10-06) of ADR-034 path B.
> **Live**: nothing was run against dev and nothing was written to dev. The NFR-04 canary is a manual gate (below).

## 1. What changed, in one paragraph

`ExternalAccess:ImpersonatedRootSets:Enabled` (default `false`) chooses, once per composition, who decides a systemuser's
SPA/Teams access. **Off**: the ADR-034 membership walk, byte-for-byte as before. **On**: the walk is not run at all; the
systemuser's own term is Dataverse's answer from `IImpersonatedRootSetSource` at **Read**, and every write route on
`/api/v1/external` that a systemuser can reach asks Dataverse, as the caller, through the existing
`CallerRecordAccessProbe` for the caller's rights on the target record before writing. The contact-grants union, the deny
veto and the Restricted veto run unchanged in both states. A capped root set (either state) now reaches the module data
response as `"truncated": true`.

## 2. Design

### 2.1 Caller kinds per plane

| Caller | Plane | Flag off | Flag on |
|---|---|---|---|
| CIAM contact | `CiamContact` | grants (unchanged) | grants (unchanged). `DataverseConfirmsWrites` is never set — no probe call |
| Workforce contact (no licence) | `Workforce`, no `SystemUserId` | grants (unchanged) | unchanged — no probe call |
| Systemuser | `Workforce` + `SystemUserId` | membership walk at `MembershipTermRights` ∪ linked-contact grants | impersonated set at Read ∪ linked-contact grants; writes confirmed by the probe |
| WorkforceHome-linked systemuser (ADR-028 A9) | `Workforce` (scheme decides first — the WorkforceHome guard in `DeterminePlane` is untouched) | as systemuser | as systemuser for the read set; **the probe cannot exchange a customer-directory token**, so its writes deny unless grants allow (open question a) |

`WorkforcePrincipalStrategy` sets `CallerPrincipal.DataverseConfirmsWrites` from `Sources.ImpersonatedRootSet` (any of
the three root types; the flag is read per composition, so any = all, and "any" is the fail-closed reading) and
`CallerPrincipal.TruncatedRootEntityTypes` from `AccessibleRecordSet.Capped`.

### 2.2 Composition (`AccessibleRecordSetService.ComposeForSystemUserAsync`)

- `:1883` reads the flag; `:1884` logs `[WF-AUTHZ] Access source for systemuser {id} on {type}: ImpersonatedRootSet|Membership`.
- `:1895-1903` flag on: `ReadDataverseRootSetAsync` → ids at `AccessRights.Read`, `Capped = answer.Truncated`,
  `CapLimit = ImpersonatedRootSetSource.RowCap` (5,000) — the existing Capped/CapLimit fields (round 59 item 3).
- `:1904-1917` flag off: the unchanged walk at `MembershipTermRights`.
- `ReadDataverseRootSetAsync` throws on: no source registered, `Guid.Empty`, any source exception (logged `[WF-AUTHZ]`,
  rethrown). `SystemUserId == null` already threw (`:1857`, task 020 invariant). No catch above it: the
  `CallerPrincipalAuthorizationFilter` lets the exception fail the request — no handler runs (NFR-01).
- The own term replaces `membershipTerm` everywhere after, including the 4th argument of `ApplyVetoPipeline` — under the
  flag the Restricted survivor is the Dataverse term at Read, never `MembershipTermRights` (amendment 3).
- Flag on returns `Sources = (SystemUserMembership: false, ContactGrants, StandingGrantMembership: false, …,
  ImpersonatedRootSet: true)`.

### 2.3 Writes (`ExternalProjectDataEndpoints.WithDataverseRightsAsync`, `:897`)

`WithDataverseRightsAsync(caller, composed, entitySet, recordId, readCallerRights, logger, ct, childCreatePrivilege = null,
readCallerPrivilege = null)`:

- returns `composed` unchanged, asking nothing, unless `caller.DataverseConfirmsWrites` **and** `composed` has Read — so a
  record outside the set, or vetoed off it, is refused by the route with no Dataverse call;
- otherwise returns `composed | SurfaceRightsFromDataverse(probe answer)`. Widening, not replacing, because the only way
  `composed` carries more than Read under the flag is the contact-grants union (B-17).
- a probe exception (other than the caller's own cancellation) or a null reader adds `None` (NFR-01). The real probe
  already answers `None` on a missing token, a failed exchange or a non-2xx.
- **Fix round 1 (F1).** When Dataverse answers AppendTo and `composed` lacks Create, the Create it surfaces is kept only
  if the caller also holds the child table's Create privilege (`:935-940`, `CallerHoldsChildCreateAsync` `:954`):
  `prvCreatesprk_Todo` (`TodoSourceAccessFilter.CreateTodoPrivilege`), `prvCreatesprk_Document`
  (`CommunicationRecordAuthorizationFilter.CreateDocumentPrivilege`), `prvCreatesprk_Event`
  (`EventEndpoints.CreateEventPrivilege`), `prvCreatesprk_Communication` (`ExternalMessageEndpoints.CreateMessagePrivilege`,
  `:167`, new). A null privilege name, a null reader, a `false` answer or a fault (other than the caller's own
  cancellation) strips Create — fail closed, logged `[WF-AUTHZ]`. A Create the contact grants already put in `composed`
  is not asked about (B-17 is unchanged). The BFF writes the child app-only, so without this Dataverse never checked the
  caller's Create privilege on the child table.

`SurfaceRightsFromDataverse` (`:1005`): Read, Write and Delete as Dataverse answers them; **AppendTo → Create** ("may add a
child under this record", subject to the Create-privilege half above). Dataverse's own `CreateAccess` from
RetrievePrincipalAccess is table-level (the caller's create privilege on the record's table), not "may attach to this
record", so it carries nothing. Precedent: `OperationAccessPolicy` `entity.associate_document` /
`event.attach_regarding` = AppendTo.

`CallerRightsReader(probe, httpContext)` binds the probe to this request's bearer token
(`TokenHelper.ExtractBearerTokenOrNull`); `CallerPrivilegeReader(probe, httpContext)` (`:1028`, fix round 1) binds
`CallerRecordAccessProbe.CallerHoldsPrivilegeAsync` the same way.

**Why that checker (CLAUDE.md §11).** Existing: `CallerRecordAccessProbe.CallerHoldsPrivilegeAsync` (WhoAmI +
`RetrieveUserSetOfPrivilegesByNames`, OBO as the caller, uncached, fail closed) — already the child-create gate of
`TodoSourceAccessFilter` (task 166, `prvCreatesprk_Todo` + AppendTo on the source) and `CommunicationRecordAuthorizationFilter`
(G5, `prvCreatesprk_Document`), and used by `QuickCreateSourceAccessFilter`, `FinanceAuthorizationFilter`,
`RecordRouteAccessAuthorizationFilter`, `DelegationRuleFilter`, `ReportingEndpoints`, `RecordCreationService` and the
decision action executors. Extension: reused as is, no change. Rejected: `UserPrivilegeChecker` (Read only, app-only
impersonation, 24 h cache — the brief says caller-scoped and no new cache). Cost of doing nothing: a systemuser whose
role refuses `sprk_todo` Create in MDA could create one here through AppendTo on the project.

### 2.4 Write-route inventory — every write under `/api/v1/external`

Route map lines are `ExternalProjectDataEndpoints.cs` unless named. "Confirmed" = `WithDataverseRightsAsync` runs before
the write for a Dataverse-decided caller.

| Route | Map | Target record asked about | Right needed | Confirmation site |
|---|---|---|---|---|
| `POST /projects/{id}/todos` | `:153` | the project (`sprk_projects`) | AppendTo + `prvCreatesprk_Todo` → Create | `CreateTodoForRoot` `:724` → `RightsForRootWriteAsync` `:755`, `:1039` |
| `POST /matters/{id}/todos` | `:183` | the matter (`sprk_matters`) | AppendTo + `prvCreatesprk_Todo` → Create | same |
| `POST /workassignments/{id}/todos` | `:199` | the work assignment (`sprk_workassignments`) | AppendTo + `prvCreatesprk_Todo` → Create | same |
| `POST /projects/{id}/documents` | `:211` | the project | AppendTo + `prvCreatesprk_Document` → Create | `UploadDocumentForParent` `:1552` → `RightsForDocumentParentAsync` `:1092` (`:1146-1148`) |
| `POST /matters/{id}/documents` | `:259` | the matter | AppendTo + `prvCreatesprk_Document` → Create | same |
| `POST /workassignments/{id}/documents` | `:295` | the work assignment | AppendTo + `prvCreatesprk_Document` → Create | same |
| `POST /invoices/{id}/documents` | `:372` | **the invoice** (`sprk_invoices`), before the secure-roots gate | AppendTo + `prvCreatesprk_Document` → Create | `RightsForInvoiceAsync` `:1188` (`:1218`) |
| `POST /servicerequests/{id}/documents` | `:331` | — not confirmed | requester rule | decision (e), accepted |
| `POST /projects/{id}/events` | `:419` | the project | AppendTo + `prvCreatesprk_Event` → Create | `CreateEvent` `:2027` (`:2047-2049`) |
| `PATCH /todos/{id}` | `:445` | **the to-do** (`sprk_todos`); the root's Read stays the scope check | Write | `UpdateTodo` `:2115` |
| `DELETE /documents/{id}` | `:464` | the row (`sprk_documents`) | Delete (+ creator stamp + parent Read, unchanged) | `AuthorizeDeleteOwnAsync` `:2270`, step (2b) `:2317` |
| `DELETE /todos/{id}` | `:474` | the row (`sprk_todos`) | Delete (+ stamp + Read) | same |
| `DELETE /events/{id}` | `:482` | the row (`sprk_events`) | Delete (+ stamp + Read) | same |
| `DELETE /servicerequests/{id}` | `:494` | — not confirmed | requester rule | decision (e), accepted |
| `POST /{root}/{id}/messages` | `ExternalMessageEndpoints.cs:107` | the project / matter / work assignment / invoice itself; a service request is not confirmed | AppendTo + `prvCreatesprk_Communication` → Create | `PostMessageCoreAsync` `:192`, `:264-266` |
| `POST /servicerequests` (intake) | `ExternalIntakeEndpoints.cs:110` | no existing target — a new request filed by the caller's own contact | module entitlement (never membership) | not applicable |
| `POST /notifications/seen` | `ExternalNotificationFeedEndpoints.cs:109` | the caller's own last-seen marker | — | not applicable |
| `POST /contact-grants`, `POST /contact-grants/revoke` | `ContactGrantEndpoints.cs:50`, `:77` | — | — | **unreachable by a systemuser**: `ContactGrantorAuthorizationFilter` refuses one (`ContactGrantorAuthorizationFilter.cs:339`) |
| `POST /registration`, `POST /workforce/link` | `ExternalRegistrationEndpoints.cs:78`, `ExternalWorkforceLinkEndpoints.cs:75` | the caller's own identity link | — | not applicable (no core record) |
| `POST /api/dataverse/fetch` | `ExternalModuleDataEndpoints.cs:128` | read, not a write | — | — |

No write on the surface rests on `MembershipTermRights` under the flag: the only writes not confirmed are the requester's
own service request (requester rule), self-identity and self-state writes, none of which consult membership.

**Enforced by** (fix round 1): `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.ExternalWrites.cs`. Every
`MapPost`/`MapPatch`/`MapPut`/`MapDelete` on `/api/v1/external` must reach `WithDataverseRightsAsync` (a name-based walk of
at most 5 hops over the `Api/ExternalAccess/` units, on lexed code with string literals blanked) or be one of 9 exemptions,
each with a reason: `POST servicerequests`, `POST servicerequests/{id}/documents`, `DELETE servicerequests/{id}`
(requester rule, decision e), `POST notifications/seen`, `POST registration`, `POST workforce/link` (self state / identity),
`POST contact-grants`, `POST contact-grants/revoke` (systemuser refused by `ContactGrantorAuthorizationFilter`), and
`POST api/dataverse/fetch` (a read in POST shape). Controls: the real tree has no violation; every exemption names a live
route and has a reason; a fixture fires on a DELETE that names the call only in a string literal and on a PUT that never
reaches it, and does not fire on a POST two hops away, on a PATCH lambda or on a GET; an exemption silences its route. A
mutation of `CreateEvent` (confirmation removed) fired the guard; the file was restored.

### 2.5 Truncation (NFR-03)

- `FetchResponseDto.Truncated` — `[JsonPropertyName("truncated")]`, `[JsonIgnore(WhenWritingDefault)]`: omitted when
  false, so an uncapped response is byte-identical to before (the `ExternalCollectionResponse.Truncated` convention).
- `ExternalModuleDataEndpoints.ExecuteScopedFetchCoreAsync` sets it when `principal.TruncatedRootEntityTypes` is
  non-empty, on both Ok returns (including the empty-dimension 0-rows path), and logs a warning.
- **Both flag states.** Before this task the membership walk's `Capped` was never propagated to any client (pre-existing,
  fixed in scope: the AC "when the cap is hit" is not qualified by flag).
- **Over-reporting, deliberately.** A scope dimension is a lambda over the principal, so which root types a module reads
  is not knowable at the seam; ANY truncated root set marks every module fetch for that caller ("may be incomplete" is the
  safe direction).
- Client: `FetchMultipleResult.truncated?: boolean` (`IDataverseClient.ts`); `BffDataverseClient` passes the JSON through
  unchanged. Rendering the "Only 5,000 records displayed" banner is not in this task (known limit).

## 3. Decisions

| # | Decision | Why |
|---|---|---|
| D1 | Round 59 shape: the impersonated set confers Read; writes confirmed per route, per record. No batched RPA over the set | Owner round 59 item 2 (the review's cheaper shape replaces the 2026-09-30 per-root rights map) |
| D2 | Widen (`composed | Dataverse`), not replace | Keeps the B-17 contact-grants union on writes; under the flag no constant can reach `composed` |
| D3 | **Revised in fix round 1 (F1 option b):** Create = AppendTo on the parent **AND** the caller's `prvCreate<child>` privilege; Dataverse `CreateAccess` ignored. Append (`prvAppend<child>`) not checked — known limit | RPA's CreateAccess is table-level; AppendTo is "attach to this record" everywhere else in the BFF. The child is written app-only, so Dataverse never asked the caller's Create privilege on the child table; the task 166 / G5 precedent (AppendTo on the source + `prvCreatesprk_Todo` / `_Document`) closes that. Append would cost another privilege round trip (the checker answers one name per call); every SpaarkeMaster role granting Create on the four tables (Spaarke Basic User, Spaarke Core User, Spaarke Office Add In User) grants Append too (role XML) |
| D4 | PATCH to-do confirms on the to-do (Write), not its root | The target record is the to-do; the root's Read stays the scope check |
| D5 | Delete-own adds Dataverse Delete on the row; the creator stamp stays required | FR-26 stamp unchanged; Dataverse adds its own answer, never stands in for the stamp |
| D6 | Invoice upload confirms on the invoice, before the secure-roots gate | The invoice is the target record; a Dataverse-confirmed Create meets the same gate as a grant-derived one |
| D7 | Service-request writes not confirmed — **accepted (decision e)** | Requester scope is neither a grant nor membership; widening could only add nothing; replacing would refuse requesters Dataverse does not admit (their request is owned by their BU's team). Explicit access Dataverse cannot see, like B-17. The owner can reverse it |
| D8 | **Revised in fix round 1 (F4 option g):** a view scoped to ONE record asks Dataverse once for a Dataverse-decided caller (one RPA, plus the D3 privilege read when the answer is AppendTo) and reports `canCreate` = that record's create-route answer: document lists (project / matter / work assignment / invoice; a service request's list keeps the requester rule, decision e), to-do lists (three roots), the project event list (the only event list with a create route). Multi-record reads, module fetch and `/me` ask nothing; `/me` says `writeAccessPerRecord: true` instead | Round 59 rejected a per-row RPA over the set, not one confirmation for the record in hand. The level `/me` reports for such a caller says only what they may SEE (Read), so the SPA would hide controls the routes admit |
| D12 | The privilege half reuses `CallerRecordAccessProbe.CallerHoldsPrivilegeAsync`; no new checker, no cache | §2.3 "Why that checker" |
| D13 | `canCreate` on `ExternalCollectionResponse<T>` is `bool?`, written only when the list asked Dataverse; `/me` `writeAccessPerRecord` omitted when false | Flag off, and every caller other than a Dataverse-decided systemuser, get byte-identical responses |
| D14 | The route rule is mechanised by `RouteAuthorizationGuardTests.ExternalWrites` | CLAUDE.md §16 enforcement ladder; was the "enforcement follow-up" in §8 |
| D15 | Delete-own needs Dataverse Delete on the row (D5) — **accepted (decision c)** | MDA parity: a creator whose role lacks Delete cannot delete the row in MDA either |
| D16 | Non-root entity types throw under the flag — **known limit (decision d)** | Fails closed; no production caller reaches it (§8 d) |
| D9 | No DI change | `CallerRecordAccessProbe` (typed client, `ExternalAccessModule.cs:157`), `IImpersonatedRootSetSource` (`:317`) and the options binding (`BindConfiguration("ExternalAccess")`) already exist; `AccessibleRecordSetService` (`:542`) takes the source and options as optional constructor parameters. The POML names `ExternalAccessModule.cs` as a modified file; nothing there needed to change |
| D10 | Truncation in both flag states | The AC is unqualified; flag-off capping was silently dropped (pre-existing defect) |
| D11 | Flag-off membership `Capped` previously dropped — fixed in scope | Same code path; filing it separately would leave NFR-03 half-done |

## 4. AC → test map

Test files: `AccessibleRecordSetServiceTests` (ARS), `CallerPrincipalResolverTests` (CPR), `ExternalTodoScopeTests` (TODO),
`ExternalRecordDocumentRoutesTests` (DOC), `ExternalDeleteOwnTests` (DEL), `ExternalMessageSendTests` (MSG),
`ExternalModuleColumnAllowListTests` (MOD), `ImpersonationNegativeCanaryTests` (CAN).

| AC | Test(s) | Twin (must-not-fire) |
|---|---|---|
| Flag off identical; zero test edits for flag off | ARS `Fr20_FlagOff_IsTheMembershipWalk_AndNeverAsksTheSource`; every pre-existing test unchanged (diff of existing tests: additions only; one ArchTest ledger seam path updated, §6) | — |
| Flag on: shared record appears | ARS `Fr20_FlagOn_ASharedRecord_Appears_AtExactlyRead` | ARS `Fr20_FlagOff_ASharedRecord_IsAbsent` |
| Flag on: BU-only match absent | ARS `Fr20_FlagOn_ABusinessUnitOnlyMatch_IsAbsent` | ARS `Fr20_FlagOff_ABusinessUnitOnlyMatch_IsPresent` (flag-off unchanged — no under-grant) |
| Flag on: BU default team record Dataverse does not admit is absent | ARS `Fr20_ADefaultTeamOwnedRecordDataverseDoesNotAdmit_FollowsTheFlag` (both states) | same theory, flag-off row |
| Flag on parity: BU/role-depth record appears | ARS `Fr20_FlagOn_ARoleDepthRecord_Appears` | — |
| Contact-grants union in both states | ARS `Fr20_TheContactGrantsUnion_SurvivesInBothStates`; TODO `Fr20_CreateTodo_ContactGrantUnion_StillAllows` | — |
| Source throw / missing SystemUserId → deny, no fallback | ARS `Fr20_FlagOn_ASourceThrow_FailsTheComposition_AndNeverFallsBackToMembership`, `Fr20_FlagOn_AnEmptySystemUserId_FailsBeforeAnyRead`, `Fr20_FlagOn_AMissingSystemUserId_Fails`, `Fr20_FlagOn_WithNoSource_FailsClosed` | — |
| Failed rights lookup denies, never a constant | TODO `Fr20_CreateTodo_ProbeFault_Is403_AndWritesNothing`; DOC `Fr20_Upload_AProbeThatFaults_Is403_AndWritesNothing`; DEL `Fr20_AProbeThatFaults_Is403_AndNothingIsDeleted`; MSG `Fr20_AReaderThatThrows_OrNoReader_Is403_AndCreatesNoRow` | — |
| Read-not-Write → 403 on EACH write route | TODO `Fr20_CreateTodo_ReadOnlyInDataverse_Is403_AndWritesNothing` (3 roots), `Fr20_PatchTodo_IsConfirmedOnTheTodo` (Read row), `Fr20_CreateEvent_IsConfirmedOnTheProject` (Read\|Write row); DOC `Fr20_Upload_ReadAndWriteButNoAppendToInDataverse_Is403_AndWritesNothing` (4 parents); DEL `Fr20_TheCreator_WithoutDeleteInDataverse_Is403_AndNothingIsDeleted` (3 kinds); MSG `Fr20_ReadAndWriteButNoAppendToInDataverse_Is403InsufficientRights_AndCreatesNoRow` (4 roots) | TODO `Fr20_CreateTodo_FlagOff_NeverAsksDataverse`, `Fr20_PatchTodo_FlagOff_NeverAsksDataverse`, `Fr20_CreateEvent_FlagOff_NeverAsksDataverse`; DOC `Fr20_Upload_FlagOff_TheComposedRightsDecide_AndDataverseIsNeverAsked`; DEL `Fr20_FlagOff_TheSystemUserCreatorDeletes_AndDataverseIsNeverAsked`; MSG `Fr20_FlagOff_TheComposedRightsDecide_AndDataverseIsNeverAsked`, `Fr20_FlagOff_AFullAccessSystemUserPosts_AndDataverseIsNeverAsked` |
| Write granted → allowed | TODO `Fr20_CreateTodo_AppendToInDataverse_IsCreated`, PATCH and event theory rows; DOC `Fr20_Upload_AppendToInDataverse_Is201`; DEL `Fr20_TheCreator_WithDeleteInDataverse_Deletes`; MSG `Fr20_AppendToInDataverse_Is201` | — |
| Outside the set / vetoed → refused with no Dataverse call | TODO `Fr20_CreateTodo_RootOutsideTheSet_Is403_AndAsksNothing`; MSG `Fr20_ARecordOutsideTheSet_IsNotAccessible_AndAsksDataverseNothing`; DEL `Fr20_DeleteInDataverse_NeverStandsInForTheCreatorStamp` | — |
| Restricted survivor = Dataverse term at Read; contact grant removed | ARS `Fr20_ARestrictedRecord_KeepsTheOwnTermAsItsSurvivor_AndLosesTheContactGrant` (both states) | flag-off row |
| ApplyVetoPipeline in force both states | ARS `Fr20_TheDenyVeto_StaysInForce` (both states) | — |
| No constant: three records, three Dataverse answers | ARS `Fr20_FlagOn_NoConstant_ThreeRecordsThreeDataverseAnswers` (Read / Read\|Write / Read\|Write\|AppendTo\|Delete → Read / Read\|Write / Read\|Write\|Create\|Delete) | — |
| Truncated reaches the module data response | ARS `Fr20_FlagOn_TruncatedBecomesCapped`; CPR `WorkforceStrategy_CarriesTheDataverseDecisionAndTheTruncation`; MOD `ExecuteScopedFetch_WhenTheCallersSetIsTruncated_SaysSo_AndOtherwiseTheWireIsUnchanged`, `ExecuteScopedFetch_WhenTheAccessibleSetIsEmptyButTruncated_SaysSo_WithoutQuerying` | MOD theory `truncated: false` (no `truncated` key on the wire) |
| Flag defaults false; never checked in on | `appsettings.template.json` `"Enabled": false`; CAN `Fr20ImpersonatedRootSetFlag_WhenEnabledInCheckedInConfiguration_RequiresAProvisionedCanary` (pre-existing tripwire, passes) | — |
| The DataverseConfirmsWrites marker | CPR `WorkforceStrategy_CarriesTheDataverseDecisionAndTheTruncation` (impersonated × capped) | its `impersonated: false` rows |
| PlatformOwnershipColumns / BU pin unchanged | `MembershipResolverServiceTests` unchanged and green; `MembershipResolverService.cs` not in the diff | — |
| `WorkforceProjectAccessLevel` has not crept back | `git grep WorkforceProjectAccessLevel -- src tests` = 0 hits | — |
| docs §6 privilege + Org-scope warning | `docs/guides/auth-deployment-setup.md` (§6 note), `SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` §7.7 | — |
| ADR-034 path B, concise + full | `docs/adr/ADR-034-user-record-membership.md` Amendment A2; concise draft `notes/036-adr034-concise-amendment.md` (main session applies) | — |
| MANUAL LIVE GATE (i)–(v); PR cites the canary | **Not run** (brief: no canary, no dev writes). Command in §7 | — |

**Fix round 1 additions.** More test files: `CallerPrincipalTests` (ME), `RouteAuthorizationGuardTests.ExternalWrites`
(ARCH), and the SPA's `src/client/external-spa/tests/PerRecordWriteAccess.test.tsx` (SPA). The test double
`CallerRecordAccessProbeDouble` gained `Holds(...)`, `PrivilegeFault` and `PrivilegeCalls`; the pre-existing allow cases
(TODO `Fr20_CreateTodo_AppendToInDataverse_IsCreated`, the event theory, DOC `Fr20_Upload_AppendToInDataverse_Is201`, MSG
`Fr20_AppendToInDataverse_Is201`) now also hold the child Create privilege — the allow side of F1.

| Item | Test(s) | Twin (must-not-fire) |
|---|---|---|
| F1: AppendTo yes + Create privilege no → 403, nothing written, on EACH child-create route | TODO `Fr20_CreateTodo_AppendToButNoCreatePrivilege_Is403_AndWritesNothing` (3 roots), `Fr20_CreateEvent_IsConfirmedOnTheProject` (`holdsCreate: false` row); DOC `Fr20_Upload_AppendToButNoCreatePrivilegeOnSprkDocument_Is403_AndWritesNothing` (project, matter, work assignment, invoice); MSG `Fr20_AppendToButNoCreatePrivilegeOnTheMessageTable_Is403InsufficientRights_AndCreatesNoRow` (4 roots) | the allow rows above (both held → created); TODO `Fr20_CreateTodo_ContactGrantUnion_StillAllows` and the PATCH theory assert no privilege is asked |
| F1: privilege read fault → 403 | TODO `Fr20_CreateTodo_PrivilegeReadFault_Is403_AndWritesNothing`; DOC `Fr20_Upload_APrivilegeReadThatFaults_Is403_AndWritesNothing`; MSG `Fr20_APrivilegeReaderThatThrows_OrNoPrivilegeReader_Is403_AndCreatesNoRow` | — |
| F1: the asked privilege names are the live ones | MSG `Fr20_TheMessageCreatePrivilege_IsTheLiveName`; TODO / DOC assert the exact name in `PrivilegeCalls` | — |
| F4: flag-on systemuser with Write/AppendTo → `canCreate: true` on X's list; Read only → `false`; probe fault → `false` | DOC `Fr20_List_CanCreate_IsTheUploadRoutesDataverseAnswer` (12 rows: 4 parents × Read\|Write\|AppendTo with the privilege → true / without it → false / Read only → false; one probe, for the record in hand), `Fr20_List_AProbeThatFaults_ReportsCanCreateFalse_AndStillLists`; TODO `Fr20_TodoList_CanCreate_IsTheCreateRoutesDataverseAnswer` (9 rows), `Fr20_TodoList_AProbeThatFaults_ReportsCanCreateFalse`, `Fr20_ProjectEventList_CanCreate_IsTheCreateRoutesDataverseAnswer` | DOC `Fr20_List_FlagOff_ReportsCanCreateFromTheComposedRights_AndAsksDataverseNothing`; TODO `Fr20_TodoList_FlagOff_IsUnchanged_AndAsksNothing` (no `canCreate` key), `Fr20_EventList_WithoutACreateRoute_OrFlagOff_IsUnchanged` |
| F4: `/me` truthful without a per-record call | ME `Me_DataverseDecidedSystemUser_SaysWriteAccessIsPerRecord_AndKeepsItsLevels` | ME `Me_EveryOtherCaller_OmitsWriteAccessPerRecord_SoTheWireIsUnchanged` (no key on the wire) |
| F4: the SPA takes create/upload from the record's list for a per-record caller | SPA: SmartTodo "Add Task" (ViewOnly level + `canCreate: true` → offered; Collaborate + `false` → hidden; missing → hidden), EventsCalendar "Create Event" (same two), project DocumentLibrary "Upload Document" (same two) | SPA: the three "every other caller keeps the level rule" cases |
| Guard: every external write route reaches the confirmation or is an exemption with a reason | ARCH `EveryExternalWriteRoute_ReachesTheDataverseConfirmation_OrIsAnExemptionWithAReason`, `ExternalWriteExemptions_EachNameALiveWriteRoute_AndCarryAReason` | ARCH `ExternalWriteGuard_FiresOnAWriteRouteWithoutTheConfirmation_AndNotOnOneThatReachesIt` (must-fire and must-not-fire fixtures) |
| Flag-off byte-identical; existing tests untouched except the F1 allow cases above | `ExternalAccessContractTests`, `ExternalDeleteOwnTests` unchanged and green; every flag-off twin above | — |

### Fail-before evidence

The behaviour was neutered in place, the API surface kept (flag read forced false; `WithDataverseRightsAsync` returning
`composed`; delete step (2b) skipped; `scopeTruncated` forced false; strategy markers forced false/empty), the new tests
run, then the real files restored (`cmp` identical). Result: **54 failed / 44 passed** of the 98 cases in the new-test
filter (97 new + the pre-existing canary tripwire). The 43 new cases that pass before are, by design, the flag-off
must-not-fire twins and deny-direction cases a deny-everything base also satisfies (probe fault → 403, outside the set →
403, stamp first, missing SystemUserId → throw, which the task 020 invariant already did). Each deny-direction case is
paired with an allow case that fails before. After: **98 / 98 pass**.

**Fix round 1.** BFF: the four behaviours were neutered in place (the Create-privilege strip disabled; the list
confirmation removed; list `canCreate` never written; `/me` `writeAccessPerRecord` forced false), then the filter
`CallerPrincipalTests | ExternalAccessContractTests | ExternalTodoScopeTests | ExternalRecordDocumentRoutesTests |
ExternalMessageSendTests | ExternalDeleteOwnTests` run: **55 failed / 278 passed** of 333 (the passes are the flag-off
twins, the deny-direction cases and the pre-existing tests). Restored (`cmp` identical): **504 / 504**. ArchTests: the
guard fired on a mutated `CreateEvent` (confirmation removed); restored. SPA: the HEAD versions of the eight changed
sources put back → `PerRecordWriteAccess.test.tsx` **7 failed / 3 passed** (the three level-rule twins pass by design);
after: **10 / 10**.

## 5. NFR-02 round trips

| Path | Dataverse calls added |
|---|---|
| Flag on, principal composition, cache miss | 3 impersonated id-only reads (one per root type, `$select=<id>&$top=5000`), replacing the membership walk's ≥ 3 resolver pages. Cached 2 minutes per (user, type): 0 on a hit |
| Flag on, each confirmed write (to-do, upload, event, PATCH, message) | +1 RetrievePrincipalAccess as the caller (OBO; the token exchange is MSAL-cached) |
| Flag on, a child create Dataverse answers AppendTo for (to-do, upload, event, message) — fix round 1 | +1 privilege read as the caller (WhoAmI + `RetrieveUserSetOfPrivilegesByNames`; uncached). Not asked when the answer lacks AppendTo or a contact grant already carries Create |
| Flag on, delete-own | +1 RPA on the row, after the existing reads |
| Flag on, a single-record list (documents of X, to-dos of X, a project's events) — fix round 1 | +1 RPA on X, + the privilege read when it answers AppendTo: **+1 probe per single-record view** |
| Flag on, `/me`, module fetch, multi-record reads | 0 |
| Flag off; CIAM; workforce contacts | 0 |

## 6. Verification

- **Build**: `dotnet build src/server/api/Sprk.Bff.Api` and the test projects — 0 warnings, 0 errors.
- **Filtered tests (Sprk.Bff.Api.Tests)** — `AccessibleRecordSetServiceTests | CallerPrincipalResolverTests |
  ExternalTodoScopeTests | ExternalRecordDocumentRoutesTests | ExternalDeleteOwnTests | ExternalMessageSendTests |
  ExternalModuleColumnAllowListTests | MembershipResolverServiceTests | ImpersonatedRootSetSource* | CallerRecordAccessProbe* |
  ImpersonationNegativeCanaryTests`: **726 passed, 0 failed**. Consumers of the changed surface (contract, intake,
  multi-root, registration, grantee, membership paging, entitlements, org membership, workforce email, module data,
  root events): **640 passed**; seam / data-mutation consumers (effective access, grant cache, secure-parent veto,
  standing grants, unified evaluator): **177 passed**. The full unfiltered suite was not run (brief).
- **ArchTests** (`RouteAuthorizationGuard* | TenantIsolation | ExternalAccessQueryIntegrityGuard |
  ExternalChildRowLiveParentGuard | CallerIdentityGuard | ADR008`): **176 passed**. The route guard first failed on the
  three to-do creates: their ledger seam `RightsForRoot` is now one named hop deeper. `RouteAuthorizationGuardTests.Ledger.cs`
  names the hop `ExternalProjectDataEndpoints.RightsForRootWriteAsync` (seam unchanged), and the to-do / event reasons cite
  the new lines.
- **Publish** (`dotnet publish -c Release -r linux-x64 --self-contained false`, zipped with the same deflate script):
  base `5b2375f6f` 38,250,205 bytes (re-zipped 38,249,877) → branch **38,254,025** bytes: **+3,820 bytes** (+4,148 like for
  like); uncompressed 130,852,084 → 130,867,988 (+15,904). Total ≈ 36.5 MiB, under 55 / 60 MB.
- **CVE**: `dotnet list src/server/api/Sprk.Bff.Api package --vulnerable --include-transitive` — no vulnerable packages;
  no package or csproj change.
- **Client lint**: `node scripts/quality/client-lint/client-lint.mjs check --package ui-components` — ok. `tsc --noEmit`
  on Spaarke.UI.Components shows 9 pre-existing errors (unbuilt sibling packages `@spaarke/sdap-client`, `@spaarke/auth`),
  none in `IDataverseClient.ts`.
- `git grep WorkforceProjectAccessLevel -- src tests`: 0.

**Fix round 1 (2026-10-17).**

- **Build**: `Sprk.Bff.Api` 0 warnings, 0 errors. `dotnet format whitespace --verify-no-changes` on the changed files:
  the BFF files and the ArchTests file are clean. The only findings in the changed test files are on lines this round did
  not write: the expression-bodied `=> new() { … }` principal builders (`ExternalTodoScopeTests.cs:1039-1054` from the
  first 036 build, `ExternalMessageSendTests.cs:107-120` from `664e6dfa5`, `CallerPrincipalTests.cs:293,297` from
  `b03f71326`) — the file's existing idiom; not reformatted (scope).
- **Filtered tests (Sprk.Bff.Api.Tests)** — `CallerPrincipalTests | ExternalAccessContractTests | ExternalTodoScopeTests |
  ExternalRecordDocumentRoutesTests | ExternalMessageSendTests | ExternalDeleteOwnTests`: **504 passed, 0 failed**
  (fail-before: 55 failed / 278 passed of 333, §4).
- **ArchTests** (`RouteAuthorizationGuard* | TenantIsolation | ExternalAccessQueryIntegrityGuard |
  ExternalChildRowLiveParentGuard | CallerIdentityGuard | ADR008`): **179 passed** (176 + the 3 new guard tests).
- **SPA** (`src/client/external-spa`): `vitest run` **1040 / 1040** (38 files); `tsc --noEmit` 0 errors; ESLint 0;
  prettier clean (pre-commit). `node scripts/quality/client-lint/client-lint.mjs check`: **exit 0, all 10 packages ok**.
  external-spa is not one of client-lint's packages. The first run in this worktree failed 8 packages the branch does not
  touch, because their `node_modules` were not installed (type-aware rules read unresolved types, e.g. `Reporting`
  `ReportDropdown.tsx:278` "always falsy"); after `client-lint.mjs install` (no tracked file changed) every package passes.
- **Publish** (same method: `dotnet publish -c Release -r linux-x64 --self-contained false`, the same deflate script):
  base `5b2375f6f` 38,250,205 bytes (re-zipped 38,249,877) → fix round 1 **38,255,137** bytes: **+4,932 bytes** (+5,260
  like for like; +1,112 over the pre-fix branch's 38,254,025); uncompressed 130,852,084 → 130,874,252 (+22,168). Total
  ≈ 36.5 MiB, under 55 / 60 MB.
- **CVE**: `dotnet list src/server/api/Sprk.Bff.Api package --vulnerable --include-transitive` — no vulnerable packages;
  no package or csproj change.

## 7. The NFR-04 canary (manual gate — not run here)

`tests/integration/auth/UnifiedAccessControl/ImpersonationNegativeCanaryTests.cs`; environment contract in
`tests/integration/auth/README.md` (§ Provisioning the canary user, § Running it):

```bash
export SPAARKE_CANARY_DATAVERSE_URL="https://spaarkedev1.crm.dynamics.com"
export SPAARKE_CANARY_SYSTEMUSERID="<existing non-admin test user in a child BU — e.g. uac.child.user@demo.spaarke.com's systemuserid>"
export SPAARKE_CANARY_SEEDED_MATTER_IDS="<matters that user's role depth admits, derived app-only — never from the impersonated read>"
export SPAARKE_CANARY_REQUIRED=true
export AZURE_TOKEN_CREDENTIALS=AzureCliCredential
export SPAARKE_TESTS_ALLOW_OUTBOUND=1
az login   # an identity that maps to the BFF Dataverse application user (dev only — never sprk-prod-kv)

dotnet test tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj \
  --filter "FullyQualifiedName~ImpersonationNegativeCanaryTests"
```

🔴 **PASS = the impersonated set is STRICTLY SMALLER than the app-only set. Equality is a FAILURE** (impersonation is
inert). A NOT RUN is not a pass. The first run (2026-10-03, dev, `uac.child.user@demo.spaarke.com`) measured 59 vs 61 and
predates this change; the gate applies again before this PR merges and before the flag is enabled anywhere. Record the
two sizes in `notes/task-034-negative-canary.md`.

## 8. Open questions and escalations

Status after fix round 1 (main-session decisions, 2026-10-17): (b), (c), (d), (e), (g) and the enforcement follow-up are
settled; **(a) and (f) are open with the owner.**

- **(a) OPEN — owner. WorkforceHome-linked systemusers can't be probed.** The OBO probe cannot exchange a
  customer-directory (A9) token for a Dataverse token in Spaarke's tenant, so under the flag such a user's writes deny
  unless contact grants allow — and since fix round 1, their single-record lists report `canCreate: false` (the same
  answer, fail closed). Options:
  1. **Known limit** until Model 1 users carry a Spaarke-tenant token: they read under the flag, and write only where a
     contact grant allows.
  2. **(Recommended)** An **app-only RetrievePrincipalAccess naming the systemuser principal** (`Principal =
     systemusers(<id>)`), and the same for the Create privilege (`RetrieveUserSetOfPrivilegesByNames` on that
     systemuser), used only for callers whose token cannot be exchanged. Same trust basis as the A5 impersonated read
     (the BFF application user asks Dataverse about a systemuser the authenticated identity was resolved to), so it adds
     no new trust; Dataverse still answers from that user's own roles, teams and shares. Needs owner + ADR-028 sign-off
     (it departs from the probe's caller-token design). **Not built here.**
  3. Keep the flag off for tenants with WorkforceHome-linked users.
- **(b) Settled.** D3 as revised in fix round 1 (AppendTo + the child Create privilege → Create).
- **(c) Accepted.** Delete-own needs Dataverse Delete on the row as well as the creator stamp (D5, D15) — MDA parity: a
  creator whose role lacks Delete on `sprk_document`/`sprk_todo`/`sprk_event` cannot delete the row in MDA either.
- **(d) Known limit.** Non-root entity types throw under the flag. `ComposeAsync` is called only by
  `WorkforcePrincipalStrategy` for the three root types, and `IsRecordAccessibleAsync` / `IsOperationPermittedAsync`
  have no production callers, so nothing reaches it; it fails closed (D16).
- **(e) Accepted.** Service-request writes (upload, message, delete) keep the requester rule and are not confirmed (D7):
  the requester's access is explicit access Dataverse cannot see, like the B-17 contact grants. The owner can reverse
  this (requiring a Dataverse answer would refuse requesters whose request is owned by another BU's team). The guard
  lists the three routes as exemptions citing this decision.
- **(f) OPEN — owner. C-1 ordering.** The impersonated read is `$select=<id>&$top=5000` with no `$orderby`, so which
  5,000 records a capped set keeps is Dataverse's default order. The cap is flagged (`truncated`). Options: (1) leave the
  default order (no extra sort; arbitrary but stable enough for a flagged cap); (2) `$orderby=modifiedon desc` (the most
  recently active records survive a cap — matches what a user expects to see; one index-backed sort per read); (3)
  `$orderby=createdon desc`. Not chosen here (escalation trigger in the POML).
- **(g) Settled (option g, fix round 1).** Single-record views answer write affordances from Dataverse for a
  Dataverse-decided caller (D8); `/me` says `writeAccessPerRecord: true`; the SPA takes Add Task / Create Event / Upload
  Document from the record's list `canCreate` for such a caller and keeps the level rule for everyone else (§10).
- **(h) Pre-existing, fixed in scope:** flag-off membership `Capped` was silently dropped (D11).
- **Rollout:** default off; enabled on dev only after the canary passes strictly smaller and 142 and 143 are done, and
  (a) is decided. No date can be named yet. Flag-off is transitional and does **not** close the flag-off BU over-grant.
- **Enforcement — built (fix round 1):** `RouteAuthorizationGuardTests.ExternalWrites` (§2.4, D14).

### Known limits (fix round 1)

- **Append not checked** (D3). `prvAppend<child>` is not asked; the SpaarkeMaster roles that grant Create on the four
  child tables grant Append too.
- **Per-record affordances cover create/upload only.** For a flag-on caller whose `/me` level is ViewOnly, the to-do
  status toggle, document download/version history, the AI actions and invite still follow the level, so they stay
  hidden although a route may admit them (PATCH to-do is confirmed per record). Widening those needs a per-record answer
  for each — not in the brief.
- **To-do / event create is offered only on `ProjectPage`**, as before; matter and work-assignment to-do lists carry
  `canCreate` on the wire, but the SPA has no create control there.
- **The invoice document list may now hit the invoice gate read** for a Dataverse-decided caller (it shares the upload
  route's rights path), so it can 503 like upload when the invoice cannot be read. Every other caller is unchanged.
- **NFR-02**: +1 probe per single-record view for a Dataverse-decided caller (§5); `/me` and multi-record reads add 0.

## 9. Deviations

- `docs/guides/auth-deployment-setup.md` is a retired stub with no §6. The privilege and Org-scope note was added there as
  a short "§6" section (the brief names it) that points to `SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` §7.7 (the App Users) and
  §12.8 (the runbook), where the content lives.
- `ExternalAccessModule.cs` is listed as modified in the POML; no change was needed (D9).
- `TASK-INDEX.md` was not updated: this branch is cut from R3's tip, and the task closes on merge + deploy + the live gate.
- The `.claude/adr` concise amendment is a draft (`036-adr034-concise-amendment.md`) for the main session.

## 10. Fix round 1 (2026-10-17) — before → after

Commits: `42444834e` (BFF F1 + F4), `7e75082ca` (guard), `0cee354d3` (SPA F4), plus the docs commit (ADR-034 full text,
these notes, the concise-ADR draft).

| Item | Before | After |
|---|---|---|
| F1 child create (to-do ×3, upload ×3 + invoice, event, message) | AppendTo on the parent alone admitted the child (`SurfaceRightsFromDataverse` AppendTo → Create) | AppendTo **and** the caller's `prvCreate<child>` (`ExternalProjectDataEndpoints.cs:935-940`, `:954`; privilege names at `:755-757`, `:1146-1148`, `:1218`, `:2047-2049`; `ExternalMessageEndpoints.cs:167`, `:264-266`). Read fault / `false` / no reader → 403, nothing written |
| F4 single-record lists | Document list `canCreate` from the composed rights (ViewOnly for a flag-on systemuser); to-do / event lists carried no `canCreate` | Flag-on systemuser: the list's rights are the create route's own Dataverse answer (`:568-584` documents; `:713-720` to-dos; `:1990-1997` project events). Everyone else: unchanged, no `canCreate` key on to-do / event lists (`Dtos/ExternalProjectDtos.cs:28-38`) |
| F4 `/me` | Level only; a flag-on systemuser mostly ViewOnly | `writeAccessPerRecord: true` for a Dataverse-decided caller (`ExternalUserContextEndpoint.cs:96`, `Dtos/ExternalUserContextResponse.cs:17-32`); omitted otherwise |
| F4 SPA | Add Task / Create Event / Upload Document from the `/me` level | For a per-record caller: from the record's list `canCreate` (missing = cannot): `SmartTodo.tsx:651`, `EventsCalendar.tsx:588-590`, `DocumentLibrary.tsx:783`, `RecordDetailModal.tsx:390`; every other caller keeps the level rule |
| Guard | Prose rule only (§8 follow-up) | `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.ExternalWrites.cs` |
| Decisions | (a)–(g) open | (b), (c), (d), (e), (g) settled; (a), (f) open with the owner (§8) |

**Client files changed** (all in `src/client/external-spa/`): `src/api/web-api-client.ts`, `src/auth/bff-client.ts`,
`src/hooks/useAccessLevel.ts`, `src/pages/ProjectPage.tsx`, `src/components/DocumentLibrary.tsx`,
`src/components/SmartTodo.tsx`, `src/components/EventsCalendar.tsx`, `src/components/detail/RecordDetailModal.tsx`; new
test `tests/PerRecordWriteAccess.test.tsx`.
