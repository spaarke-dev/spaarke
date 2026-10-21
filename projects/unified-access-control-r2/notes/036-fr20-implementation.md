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
| WorkforceHome-linked systemuser (ADR-028 A9) | `Workforce` (scheme decides first — the WorkforceHome guard in `DeterminePlane` is untouched) | as systemuser | as systemuser for the read set. Its customer-directory token cannot be exchanged, so its writes are confirmed **app-only, naming its systemuser** (decision a, owner 2026-10-18; §12), as every systemuser is since option C (§13) — no sign-in marker chooses the path, and OBO is never tried |

`WorkforcePrincipalStrategy` sets `CallerPrincipal.DataverseConfirmsWrites` from `Sources.ImpersonatedRootSet` (any of
the three root types; the flag is read once per composition, so the three sets agree — if flags ever become per type,
decide this per entity type: the confirmation only ADDS rights, never removes them, so "any" is not a fail-closed
reading; fix round 2 corrected the code comment, `CallerPrincipalResolver.cs:771-772`) and
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
  if the caller also holds the child table's Create privilege (`:937-945`, `CallerHoldsChildCreateAsync` `:959`):
  `prvCreatesprk_Todo` (`TodoSourceAccessFilter.CreateTodoPrivilege`), `prvCreatesprk_Document`
  (`CommunicationRecordAuthorizationFilter.CreateDocumentPrivilege`), `prvCreatesprk_Event`
  (`EventEndpoints.CreateEventPrivilege`), `prvCreatesprk_Communication` (`ExternalMessageEndpoints.CreateMessagePrivilege`,
  `:167`, new). A null privilege name, a null reader, a `false` answer or a fault (other than the caller's own
  cancellation) strips Create — fail closed. **Fix round 2 (F2):** a null name is a route that creates nothing (PATCH
  to-do, delete-own), so it strips Create silently (`:941`); only a real refusal (blank name, no reader, `false`, fault)
  is logged `[WF-AUTHZ]`. A Create the contact grants already put in `composed`
  is not asked about (B-17 is unchanged). The BFF writes the child app-only, so without this Dataverse never checked the
  caller's Create privilege on the child table.

`SurfaceRightsFromDataverse` (`:1010`): Read, Write and Delete as Dataverse answers them; **AppendTo → Create** ("may add a
child under this record", subject to the Create-privilege half above). Dataverse's own `CreateAccess` from
RetrievePrincipalAccess is table-level (the caller's create privilege on the record's table), not "may attach to this
record", so it carries nothing. Precedent: `OperationAccessPolicy` `entity.associate_document` /
`event.attach_regarding` = AppendTo.

`CallerRightsReader(probe, httpContext)` binds the probe to this request's bearer token
(`TokenHelper.ExtractBearerTokenOrNull`); `CallerPrivilegeReader(probe, httpContext)` (`:1033`, fix round 1) binds
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
| D3 | **Revised in fix round 1 (F1 option b):** Create = AppendTo on the parent **AND** the caller's `prvCreate<child>` privilege; Dataverse `CreateAccess` ignored. Append (`prvAppend<child>`) not checked — known limit | RPA's CreateAccess is table-level; AppendTo is "attach to this record" everywhere else in the BFF. The child is written app-only, so Dataverse never asked the caller's Create privilege on the child table; the task 166 / G5 precedent (AppendTo on the source + `prvCreatesprk_Todo` / `_Document`) closes that. Append would cost another privilege round trip (the checker answers one name per call); each SpaarkeMaster role grants Append on every one of these tables it grants Create on (role XML). **Corrected in fix round 2:** only Spaarke Core User grants Create (and Write, Delete) on all four (`sprk_todo`, `sprk_document`, `sprk_event`, `sprk_communication`); Spaarke Basic User grants them on `sprk_communication` only (Basic depth), Spaarke Office Add In User only Create/Write on `sprk_event`. Internal SPA/Teams writers need Core User; Basic- or Office-only users lose the other writes under the flag, as in MDA (deployment guide §12.8 precondition 4) |
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
| F4: the SPA takes create/upload from the record's list for a per-record caller | SPA: SmartTodo "Add Task" (ViewOnly level + `canCreate: true` → offered; Collaborate + `false` → hidden; missing → hidden), EventsCalendar "Create Event" (the same three since fix round 2), project DocumentLibrary "Upload Document" (the same three since fix round 2) | SPA: the three "every other caller keeps the level rule" cases |
| Guard: every external write route reaches the confirmation or is an exemption with a reason | ARCH `EveryExternalWriteRoute_ReachesTheDataverseConfirmation_OrIsAnExemptionWithAReason`, `ExternalWriteExemptions_EachNameALiveWriteRoute_AndCarryAReason` | ARCH `ExternalWriteGuard_FiresOnAWriteRouteWithoutTheConfirmation_AndNotOnOneThatReachesIt` (must-fire and must-not-fire fixtures) |
| Flag-off byte-identical; existing tests untouched except the F1 allow cases above | `ExternalAccessContractTests`, `ExternalDeleteOwnTests` unchanged and green; every flag-off twin above | — |

**Fix round 2 additions.** New SPA test file `src/client/external-spa/tests/ProjectPage.perRecordWrite.test.tsx` (PAGE);
`DetailDocumentsDelete.test.tsx` (DETAIL) and `PerRecordWriteAccess.test.tsx` (SPA) gained cases.

| Item | Test(s) | Twin (must-not-fire) |
|---|---|---|
| 1. No constant (the red test) | ARS `Fr20_FlagOn_NoConstant_ThreeRecordsThreeDataverseAnswers`, now a theory: privilege held → Z = Read\|Write\|Create\|Delete | the not-held row → Z = Read\|Write\|Delete |
| 2. Download / version history for a per-record caller | SPA "a ViewOnly-level per-record caller may download from a project list that rendered", "the same holds on a matter list"; PAGE "a per-record caller … is offered Upload … and Download" | SPA "a ViewOnly contact is not offered download (unchanged)", "a Collaborate contact is offered download (unchanged)", "a host that says canDownload=false still wins"; PAGE "every other ViewOnly caller is offered neither" |
| 3. To-do status toggle | SPA "a ViewOnly-level per-record caller is offered the toggle on a project to-do, and PATCH decides", "a 403 reverts the toggle and says so plainly, never the server body" | SPA "a ViewOnly contact is not offered the toggle (unchanged)", "a matter to-do list stays read-only for a per-record caller (task 057)" |
| 5. A late `/me` cannot hide Upload | SPA "a /me answer that arrives after the list does not leave Upload hidden" (every list read held, answered oldest last) | — |
| 6. No false `[WF-AUTHZ]` warning | TODO `Fr20_AConfirmationThatCreatesNothing_LogsNoWarning_AndAddsNoCreate` (2 rows, called as PATCH and delete-own call it) | TODO `Fr20_AChildCreateWithNoPrivilegeReader_StillWarns_AndAddsNoCreate` |
| 7. `/me` wiring reaches the components | PAGE (above; fails with the ProjectPage prop removed); DETAIL "Project, per-record caller (task 036 fix round 2, F3): the list canCreate=true decides Upload" (fails with `uploadOverride`'s per-record line removed); SPA "a missing canCreate is cannot" for the project DocumentLibrary and EventsCalendar | DETAIL `canCreate=false` row |
| 10. The config tripwire covers the templates | CAN `Fr20ImpersonatedRootSetFlagScan_CoversThePerEnvironmentTemplates` (`appsettings*.json*` now takes `appsettings.{Development,Production}.json.template`) | CAN `Fr20ImpersonatedRootSetFlagScan_FiresOnAnythingNotLiterallyFalse` (5 rows: `true`, a token, a flattened `"true"` fire; `false` and absent do not) |

**Fix round 2 fail-before.** BFF: HEAD's `ExternalProjectDataEndpoints.cs` and the old `appsettings*.json` scan pattern
put back under the new tests, the full §6 filter below: **3 failed / 887 passed of 890** (the two F2 rows and the
template-coverage test). HEAD's version of the no-constant test: **1 failed of 1** (the red test; its new two rows pass
against both HEAD's and this round's code, because item 1 was a test defect, not a code one). SPA: HEAD's
`DocumentLibrary.tsx` and `SmartTodo.tsx` put back, `PerRecordWriteAccess.test.tsx` + `ProjectPage.perRecordWrite.test.tsx`:
**6 failed / 18 passed of 24** (download ×2, toggle ×2, the late `/me`, the PAGE per-record row; the passes are the
level-rule twins and the fix-round-1 cases, including the new "missing canCreate" rows, which cover existing behaviour).
The host wiring (item 7) was proven by mutation: removing `writeAccessPerRecord` from `ProjectPage.tsx`'s components and
the per-record line from `RecordDetailModal.tsx`'s `uploadOverride` failed the PAGE and DETAIL per-record rows (2
failed); both files were restored with `git checkout`. Everything restored: `cmp` identical.

### Fail-before evidence

The behaviour was neutered in place, the API surface kept (flag read forced false; `WithDataverseRightsAsync` returning
`composed`; delete step (2b) skipped; `scopeTruncated` forced false; strategy markers forced false/empty), the new tests
run, then the real files restored (`cmp` identical). Result: **54 failed / 44 passed** of the 98 cases in the new-test
filter (97 new + the pre-existing canary tripwire). The 43 new cases that pass before are, by design, the flag-off
must-not-fire twins and deny-direction cases a deny-everything base also satisfies (probe fault → 403, outside the set →
403, stamp first, missing SystemUserId → throw, which the task 020 invariant already did). Each deny-direction case is
paired with an allow case that fails before. After: **98 / 98 pass**.

**Fix round 1.** BFF: the four behaviours were neutered in place (the Create-privilege strip disabled; the list
confirmation removed; list `canCreate` never written; `/me` `writeAccessPerRecord` forced false). The figure first
recorded here, "55 failed / 278 passed of 333", came from a run that omitted `ExternalAccessContractTests` and
`ExternalDeleteOwnTests` from the filter it names. **Re-run honestly in fix round 2** (the same four neuterings,
re-applied in place on the fix-round-2 code, then restored, `cmp` identical) with the full named filter
`CallerPrincipalTests | ExternalAccessContractTests | ExternalTodoScopeTests | ExternalRecordDocumentRoutesTests |
ExternalMessageSendTests | ExternalDeleteOwnTests`: **58 failed / 449 passed of 507**. 507 = the 504 round-1 cases + 3
new round-2 cases in `ExternalTodoScopeTests` (the F2 theory's 2 rows and its twin), which also fail under the disabled
strip; so the round-1 cases alone are **55 failed / 449 passed of 504** (the passes are the flag-off twins, the
deny-direction cases and the pre-existing tests). After: **504 / 504** (round 1), **507 / 507** (now). ArchTests: the
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
| Flag on, a WorkforceHome-linked systemuser (decision a, §12) | The same counts on the app-only path: +1 app-only RPA naming the systemuser per confirmed write (and per single-record view), +1 app-only `RetrieveUserSetOfPrivilegesByNames` on that systemuser when the answer has AppendTo. No WhoAmI and no token exchange; uncached, as on the OBO path |
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

**Fix round 2 (2026-10-17).**

- **Build**: `Sprk.Bff.Api` (`--no-incremental`) 0 warnings, 0 errors; `Sprk.Bff.Api.Tests` and `Spaarke.ArchTests` 0
  warnings, 0 errors.
- **Filtered tests (Sprk.Bff.Api.Tests)** — the §6 classes plus the fix-round-1 classes: `AccessibleRecordSetServiceTests |
  CallerPrincipalResolverTests | ExternalTodoScopeTests | ExternalRecordDocumentRoutesTests | ExternalDeleteOwnTests |
  ExternalMessageSendTests | ExternalModuleColumnAllowListTests | MembershipResolverServiceTests | ImpersonatedRootSetSource* |
  CallerRecordAccessProbe* | ImpersonationNegativeCanaryTests | ExternalAccessContractTests | CallerPrincipalTests`:
  **890 passed, 0 failed** (fail-before §4). The full unfiltered suite was not run (brief).
- **ArchTests** (same filter as above): **179 passed**. `RouteAuthorizationGuardTests.Ledger.cs` reasons cite
  `ExternalProjectDataEndpoints.cs` line numbers in prose; this round shifts the lines after `:937` by 5, and many of those
  citations already predate fix round 1 — not asserted by any test, not rewritten here (known limit below).
- **SPA** (`src/client/external-spa`): `vitest run` **1056 / 1056** (39 files) on a quiet machine. A first full run
  concurrent with client-lint timed out 12 tests in 6 untouched files (`App.*`, `DocumentUploadPage.externalUpload`,
  `main-bootstrap`, `no-mock-identity`); those 6 files alone: 73 / 73; the full re-run alone: 1056 / 1056. `tsc --noEmit`
  0 errors; ESLint 0 on the changed files; prettier clean. `client-lint.mjs check`: **exit 0, all 10 packages ok**.
- **Publish** (`dotnet publish -c Release -r linux-x64 --self-contained false`; both trees zipped with PowerShell
  `Compress-Archive -CompressionLevel Optimal`, so like for like): base `5b2375f6f` 39,194,164 → fix round 2
  **39,200,411** bytes: **+6,247 bytes**; uncompressed 130,852,084 → 130,874,248 (+22,164; fix round 1 was 130,874,252,
  so this round is −4 bytes). Total ≈ 37.4 MiB zipped, under 55 / 60 MB.
- **CVE**: no package or csproj change.

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
settled. **(a) and (f) were DECIDED by the owner on 2026-10-18** and are built (§12). Every question is now settled.

- **(a) DECIDED (owner, 2026-10-18): option 2, built (§12).** A systemuser resolved through the WorkforceHome stored
  home link is confirmed app-only, naming that systemuser: `RetrievePrincipalAccess` with `Principal =
  systemusers(<id>)` for the row rights, and `RetrieveUserSetOfPrivilegesByNames` on that systemuser for the child
  Create privilege. The path was chosen from the resolved principal (`CallerPrincipal.ResolvedThroughHomeLink`, removed
  in fix round 3 once option C made every systemuser app-only, §14), never by trying OBO and falling back. It fails closed (fault, missing id, unreadable answer),
  is uncached, and drives both the write routes and the lists' `canCreate`. The original analysis follows.
- *(as escalated)* **WorkforceHome-linked systemusers can't be probed.** The OBO probe cannot exchange a
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
     (it departs from the probe's caller-token design). **Chosen by the owner; built in §12.**
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
- **(f) DECIDED (owner, 2026-10-18): option 2, built (§12).** The impersonated read is now
  `$select=<id>&$orderby=modifiedon desc&$top=5000`, so a capped set keeps the most recently modified records; the cap
  is still flagged (Truncated / Capped unchanged). The cache key version is bumped to 2 so no entry from the old query
  shape is served. The flag-off membership walk keeps primary-id ascending order (§12). The original analysis follows.
- *(as escalated)* **C-1 ordering.** The impersonated read was `$select=<id>&$top=5000` with no `$orderby`, so which
  5,000 records a capped set keeps is Dataverse's default order. The cap is flagged (`truncated`). Options: (1) leave the
  default order (no extra sort; arbitrary but stable enough for a flagged cap); (2) `$orderby=modifiedon desc` (the most
  recently active records survive a cap — matches what a user expects to see; one index-backed sort per read); (3)
  `$orderby=createdon desc`. Not chosen here (escalation trigger in the POML).
- **(g) Settled (option g, fix round 1; widened in fix round 2).** Single-record views answer write affordances from
  Dataverse for a Dataverse-decided caller (D8); `/me` says `writeAccessPerRecord: true`; the SPA takes Add Task / Create
  Event / Upload Document from the record's list `canCreate` for such a caller and keeps the level rule for everyone else
  (§10). Fix round 2 (§11): download and version history are offered wherever the list rendered, and the project to-do
  status toggle is offered with PATCH deciding.
- **WorkforceHome is no longer a precondition.** With (a) built, the deployment guide (§12.8 precondition 3), the template
  comment and ADR-034 A2 describe the app-only confirmation instead of "refused under the flag until (a) is settled".
- **(h) Pre-existing, fixed in scope:** flag-off membership `Capped` was silently dropped (D11).
- **Rollout:** default off; enabled on dev only after the canary passes strictly smaller and 142 and 143 are done. No
  date can be named yet. Flag-off is transitional and does **not** close the flag-off BU over-grant.
- **Enforcement — built (fix round 1):** `RouteAuthorizationGuardTests.ExternalWrites` (§2.4, D14).

### Known limits (fix round 1)

- **Append not checked** (D3). `prvAppend<child>` is not asked; each SpaarkeMaster role grants Append on every child
  table it grants Create on (only Spaarke Core User grants Create on all four — D3, corrected in fix round 2).
- ~~**Per-record affordances cover create/upload only.**~~ Superseded by fix round 2 (§11): download / version history
  and the project to-do status toggle now follow the per-record rule; invite and the issued-grants list stay hidden
  (correct: the contact-grant routes refuse a systemuser); the AI actions are not level-gated at all.
- **To-do / event create is offered only on `ProjectPage`**, as before; matter and work-assignment to-do lists carry
  `canCreate` on the wire, but the SPA has no create control there.
- **The invoice document list may now hit the invoice gate read** for a Dataverse-decided caller (it shares the upload
  route's rights path), so it can 503 like upload when the invoice cannot be read. Every other caller is unchanged.
- **NFR-02**: +1 probe per single-record view for a Dataverse-decided caller (§5); `/me` and multi-record reads add 0.

## 9. Deviations

- `docs/guides/auth-deployment-setup.md` is a retired stub with no §6. The privilege and Org-scope note was added there as
  a short "§6" section (the brief names it) that points to `SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` §7.7 (the App Users) and
  §12.8 (the runbook), where the content lives.
- `ExternalAccessModule.cs` is listed as modified in the POML; no code change was needed (D9). Fix round 2 corrected its
  stale comment ("NOT consumed by AccessibleRecordSetService yet") at `:314`.
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

## 11. Fix round 2 (2026-10-17) — before → after

From adversarial passes A and B. Tests and fail-before: §4 "Fix round 2 additions"; verification: §6 "Fix round 2".

| # | Item | Before | After |
|---|---|---|---|
| 1 | F4 red test | ARS `Fr20_FlagOn_NoConstant_ThreeRecordsThreeDataverseAnswers` (`AccessibleRecordSetServiceTests.cs:2808`) named no child privilege, so round 1's rule stripped Z's Create: deterministic failure | A theory (`:2771`) passing `childCreatePrivilege` and a privilege reader: held → Z = Read\|Write\|Create\|Delete; not held → Read\|Write\|Delete |
| 2 | F1 download / version history | `DocumentLibrary.tsx:752` `canDownload ?? canUploadOrDownload(accessLevel)`: a flag-on systemuser's ViewOnly level hid both, though the routes need only Read (`ExternalProjectDataEndpoints.cs:616-618`, versions `:1892-1895`) | `DocumentLibrary.tsx:766` `canDownload ?? (writeAccessPerRecord \|\| canUploadOrDownload(accessLevel))`: offered wherever the list rendered (the list read already passed Read). Contacts unchanged (`writeAccessPerRecord` false → the same expression) |
| 3 | F1 to-do status toggle | `SmartTodo.tsx:339` / `:647` from the `/me` level only | `SmartTodo.tsx:659` `allowToggle = allowEdit \|\| (writeAccessPerRecord && root === 'projects')`, passed to `TaskItem` (`:338`, `:882`, `:903`); PATCH confirms Write per to-do. A 403 reverts the optimistic state and shows `TOGGLE_DENIED` (`:264`, "You do not have permission to change this task.") in the existing warning `MessageBar`; any other failure shows the ProblemDetails `detail` or a fixed fallback (`problemMessage`, the SPA's idiom), never the raw body. The failure text changes for **every** caller, contacts included (they used to see the raw body or "Failed to fetch"); the concise ADR A2 says so, and `PerRecordWriteAccess.test.tsx` pins the contact 403 and the non-403 branches (re-check of round 2) |
| 4 | F4 docs, config, comments | Guide: AppendTo alone admits a child; (a) not a precondition; (g) open; roles unnamed. Template comment without (a). ADR "Residual" says under-offers until (g). No service-request exemption in the ADR. `ExternalAccessModule.cs:314` "NOT consumed … yet" | Guide §12.8 row `:2186` (AppendTo + `prvCreate<child>`), preconditions 3 (WorkforceHome / (a)) and 4 (Spaarke Core User; Basic / Office Add In facts) at `:2201`, `:2207`, settled (g) at `:2234`. `appsettings.template.json:82` names (a). ADR-034 full A2: service-request exemptions (`:412`), read-only/toggle affordances, corrected role facts, residuals rewritten (`:472`, `:476`). Concise ADR: **Edit 5** drafted in `036-adr034-concise-amendment.md` (main session applies). Module comment `:314` corrected |
| 5 | F1 (low) late `/me` hides Upload | The fetch stored an already-interpreted `canCreate` and depended on `writeAccessPerRecord`; a list read started before `/me` answered could land last and leave Upload on the level rule | `DocumentLibrary.tsx:797`, `:802` store the raw `canCreate`; `:762` interprets it at render; the fetch no longer depends on `writeAccessPerRecord` (`:810`) — the race is gone by construction |
| 6 | F2 (low) false `[WF-AUTHZ]` warning | Every flag-on PATCH to-do and delete-own where Dataverse answered AppendTo logged "No child-table Create privilege … fail closed" | `ExternalProjectDataEndpoints.cs:941`: a null privilege strips Create silently; the helper (`:959`) is asked only when a privilege is named and logs only a real refusal |
| 7 | F3 SPA wiring untested | Nothing failed if ProjectPage or RecordDetailModal dropped `writeAccessPerRecord`; no "missing canCreate" rows for project DocumentLibrary / EventsCalendar | `tests/ProjectPage.perRecordWrite.test.tsx` (new), `DetailDocumentsDelete.test.tsx` per-record theory, the two "missing canCreate" rows; mutation-proven |
| 8 | F2 comment contradicts code | `CallerPrincipalResolver.cs:771-772` "ANY is the fail-closed reading" | Reworded: the sets agree because the flag is read once per composition; decide per type if flags ever differ — the confirmation only adds rights |
| 9 | Notes accuracy | Round-1 fail-before "55 / 278 of 333" did not match its named filter; D3 role claim wrong | §4 re-run honestly (55 failed / 449 passed of 504 round-1 cases); D3 corrected; §4 / §6 / §8 updated |
| 10 | Known limit made cheap | The config tripwire scanned `appsettings*.json`, missing `appsettings.{Development,Production}.json.template` | `ImpersonationNegativeCanaryTests.cs:468` `appsettings*.json*`, with a coverage test and must-fire / must-not-fire rows |

### Affordance inventory — where the `/me` level gates a write for a per-record caller

| Affordance | File:line | Route confirms per record? | Done |
|---|---|---|---|
| Add Task | `SmartTodo.tsx:655` | yes (create to-do, AppendTo + privilege) | fix round 1: the list's `canCreate` |
| To-do status toggle | `SmartTodo.tsx:659` | yes (PATCH `/todos/{id}`, Write on the to-do) | fix round 2: offered on a project's to-dos; PATCH decides; 403 reverts with a plain message |
| To-do / event toggles in the record detail view | `RecordDetailModal.tsx:520-521` | — | unchanged: read-only for every caller by design (task 054 / 057), no `writeAccessPerRecord` passed |
| Create Event | `EventsCalendar.tsx:588-592` | yes | fix round 1: the list's `canCreate` |
| Upload Document | `DocumentLibrary.tsx:762-763`, `RecordDetailModal.tsx:390` | yes | fix round 1, made race-free in fix round 2 |
| Download / version history | `DocumentLibrary.tsx:766` | Read only (`ExternalProjectDataEndpoints.cs:616-618`, `:1892-1895`) | fix round 2: offered wherever the list rendered |
| Delete own | `DocumentLibrary.tsx` / `SmartTodo.tsx` / `EventsCalendar.tsx` (`isCreatedByCaller`) | yes (stamp + Delete) | not level-gated; unchanged |
| Invite User, issued-grants list | `ProjectPage.tsx:295` (`canInvite`) | **no** — contact-grant routes; `ContactGrantorAuthorizationFilter.cs:339` refuses a systemuser | kept hidden (correct) |
| AI actions | `useAccessLevel.ts:159` (`canUseAi`) has no consumer; the header "AI Assistant" button (`ProjectPage.tsx:555`) and `AssistantPane` are not level-gated | — | nothing to change |
| Event status / edit | none — the event-as-to-do toggle was removed (R3 task 007) | — | nothing to change |

### Known limits (fix round 2)

- **A probe fault answers 403, not 503.** A RetrievePrincipalAccess or privilege-read fault adds no rights, so the route
  refuses with 403 (fail closed) rather than signalling a transient upstream fault the client could retry.
- **No WhoAmI-vs-`SystemUserId` identity assertion.** The probe asks as the bearer of the token; nothing asserts that
  Dataverse's WhoAmI for that token equals the principal's resolved `SystemUserId`. No path was found where they differ.
- **An impersonated-read (source) fault returns a generic 500** (fails closed; the request never falls back to membership).
- **Ledger line citations drift.** `RouteAuthorizationGuardTests.Ledger.cs` reasons cite `ExternalProjectDataEndpoints.cs`
  lines in prose; they are not asserted and were already stale before this round.
- Carried from fix round 1: Append not checked; to-do / event create offered only on `ProjectPage`; the invoice document
  list may 503 like upload; +1 probe per single-record view. ((a) and (f) were open here; decided 2026-10-18, §12.)

## 12. Owner decisions (a) and (f) (2026-10-18) — before → after

| # | Item | Before | After |
|---|---|---|---|
| a | A WorkforceHome-linked systemuser's writes under the flag | The OBO probe could not exchange the customer-directory token, so every write was refused unless a contact grant allowed it (service-request writes excepted), and the user's single-record lists said `canCreate: false` | Dataverse is asked app-only, naming the resolved systemuser: `RetrievePrincipalAccess` with `Principal = systemusers(<id>)` for the row (`CallerRecordAccessProbe.GetPrincipalRightsAppOnlyAsync`, over the existing `DataverseWebApiService.RetrievePrincipalRightsAsync`), and `RetrieveUserSetOfPrivilegesByNames` on that systemuser for the child Create privilege (`PrincipalHoldsPrivilegeAppOnlyAsync`, over the new `DataverseWebApiService.PrincipalHoldsPrivilegeAsync`). The same AppendTo + `prvCreate<child>` / Write / Delete rules apply, and the same answer drives the list `canCreate`; `/me` already said `writeAccessPerRecord` |
| a | Path choice | — | From the resolved principal: `WorkforcePrincipalResolver.ResolveLinkedHomeSystemUserAsync` marks `WorkforcePrincipal.ResolvedThroughHomeLink`; `WorkforcePrincipalStrategy` carries it to `CallerPrincipal.ResolvedThroughHomeLink` (systemusers only); `ExternalProjectDataEndpoints.CallerRightsReader` / `CallerPrivilegeReader` pick the app-only delegate when it is set (`AppOnlyConfirmationPrincipal`). Never "try OBO, fall back". Every other caller gets the OBO readers exactly as before, and no route call site changed (`ExternalMessageEndpoints` uses the same factories). The WorkforceHome guard stays first in `ResolveAsync` |
| a | Fail closed | — | A fault, a missing (`Guid.Empty`) systemuser id, a blank entity set or privilege, no client, a non-success response or malformed JSON → no rights (`None` / `false`); the caller's own cancellation is rethrown. Uncached (the OBO probe caches nothing either). Logged as `[DELEGATION] … via app-only-named-principal`, and the route log says `answered via {Path}`; ids only, no tokens or names |
| f | Which 5,000 records a capped impersonated set keeps | Dataverse's default order (`$select=<id>&$top=5000`) | `$select=<id>&$orderby=modifiedon desc&$top=5000` (`ImpersonatedRootSetSource.cs`): the most recently modified survive. Truncated / Capped unchanged. `CacheVersion` 1 → 2, so no set read under the old shape is served after deploy |
| f | Flag-off membership cap order | Primary id ascending (`MembershipResolverService.cs:1092-1096`) | **Unchanged, reported.** It is the FR-14 stable total order the paging cookie depends on, in the canonical resolver that AI scoping shares. `modifiedon` is not unique, so switching needs a two-key order, a paging-cookie re-check and resolver-wide test changes: not trivial, so not done here |

**Tests** (fail-before by neutering in place, then restoring and `cmp`-checking all five files):

| Test | Proves | Before |
|---|---|---|
| `ExternalTodoScopeTests.Fr20a_HomeLinkedSystemUser_PatchTodo_IsConfirmedAppOnlyOnTheTodo` (Write / no Write) | an edit is allowed with Write and refused without; the app-only RPA is on the to-do; OBO is never called | fail (both) |
| `…Fr20a_HomeLinkedSystemUser_CreateTodo_NeedsAppendToAndTheCreatePrivilege_AskedAppOnly` (×3 roots) | AppendTo + `prvCreatesprk_Todo`, both asked app-only | fail (all) |
| `…Fr20a_HomeLinkedSystemUser_AnAppOnlyFault_Refuses` (rights / privilege) | a fault refuses, nothing is written, OBO is never tried | fail (both) |
| `…Fr20a_HomeLinkedPrincipal_WithoutASystemUserId_FailsClosed` | a missing id is refused with no Dataverse call | fail |
| `…Fr20a_ANormalSystemUser_StillUsesObo_AndNeverTheAppOnlyPath` | OBO unchanged; the app-only path is never called | pass (twin) |
| `…Fr20a_AContactCaller_IsUnaffected` | a contact: no probe of either kind | pass (twin) |
| `…Fr20a_HomeLinkedSystemUser_TodoList_CanCreate_IsTheAppOnlyAnswer` (×3) | the list `canCreate` follows the app-only answer | fail (all) |
| `CallerPrincipalTests.Me_HomeLinkedDataverseDecidedSystemUser_SaysWriteAccessIsPerRecord` | `/me` says `writeAccessPerRecord` | pass (by design: `/me` asks nothing, D8) |
| `CallerPrincipalResolverTests.WorkforceStrategy_CarriesTheHomeLink_FromTheResolvedPrincipal` (×3) | the strategy carries the flag for a home-linked systemuser only | 1 fail (home-linked) / 2 pass (twins) |
| `WorkforcePrincipalResolverTests.Home_StoredLinkToAnEnabledSystemUser_IsMarkedResolvedThroughTheHomeLink` / `Home_WorkforceHomeContact_IsNotMarkedResolvedThroughTheHomeLink` | the resolver marks the home-link branch only | fail / pass (twin) |
| `CallerRecordAccessProbeAppOnlyTests` (new, 19) | the wire shape over the OfflineService pattern: the URL, `MSCRMCallerID` = the principal, the app-only bearer, the exact privilege name; 7 fail-closed rows each for rights and privilege; cancellation rethrown | 5 fail / 14 pass (the fail-closed rows hold either way) |
| `ImpersonatedRootSetSourceTests.GetAsync_OrdersByMostRecentlyModified_SoACappedSetKeepsTheNewest` (×3 root types) | the query carries `$orderby=modifiedon desc` before `$top=5000`; `CacheVersion` ≥ 2 | fail (all) |

New tests in all: **41 cases; 21 failed and 20 passed before; 41 pass after.** The 20 that pass before are twins,
fail-closed rows (they hold on both paths) and the `/me` row.

**Verification:**

- `dotnet build src/server/api/Sprk.Bff.Api/` — 0 warnings, 0 errors.
- Filtered unit + integration run (the touched classes plus `ExternalAccessContractTests`, `ExternalDeleteOwnTests`,
  `CallerPrincipalTests`, `AccessibleRecordSetServiceTests`, `ImpersonationNegativeCanaryTests`,
  `ExternalRecordDocumentRoutesTests`, `ExternalMessageSendTests`, `DataverseRecordShareWireTests`,
  `CallerPrivilegeCheckTests`, `DelegationProbeFailClosedTests`, `AccessCacheInvalidationTests`,
  `AccessCacheFaultCachingTests`): **1006 / 1006 passed**.
- ArchTests `RouteAuthorizationGuard*|TenantIsolation|CallerIdentityGuard|PoaShareClientSingletonGuard|ADR010`:
  **186 / 186 passed** (`RouteAuthorizationGuardTests.ExternalWrites` included).
- SPA: not changed, so no vitest run.

**Publish size** (`dotnet publish -c Release -r linux-x64 --self-contained false`, zipped with `Compress-Archive
-CompressionLevel Optimal`): fresh master `a82578cdc` is 39,190,821 B zipped / 130,852,348 B on disk; this commit is
39,203,219 B / 130,883,556 B, so **+12,398 B zipped vs master** (+6,501 B is the rest of PR #2078 at `953fd6446`;
+5,897 B is this change). The file list is the same, `deps.json` is identical, and no package was added. The total is
about 39.2 MB zipped, well under 55 MB.

**Component justification (CLAUDE.md §11):**

1. *Existing* — `CallerRecordAccessProbe` (OBO only), `DataverseWebApiService.RetrievePrincipalRightsAsync` (app-only +
   impersonation, reused as is), `UserPrivilegeChecker` (Read only, cached 24 h — the wrong cache for a write gate),
   `IDataverseRecordShareService` (the POA seam, not a privilege reader).
2. *Extension* — the probe gains two virtual methods and an optional constructor dependency on the existing
   `DataverseWebApiService` singleton (resolved by `ActivatorUtilities`): **no new interface, no DI registration**.
   `DataverseWebApiService` gains one privilege read next to its existing RetrievePrincipalAccess wrapper, because no
   app-only privilege-by-name read existed (the others go through `IDataverseUserClient`, OBO). `WorkforcePrincipal` and
   `CallerPrincipal` each gain one init property.
3. *Cost of doing nothing* — under the flag, every write by a WorkforceHome-linked systemuser is refused (except
   service-request and grant-allowed writes) and their lists say `canCreate: false`, so the flag could not be enabled for
   a tenant whose internal users sign in through WorkforceHome.

**Known notes:**

- The app-only RetrievePrincipalAccess has no 404 replication-lag retry; the OBO `GetCallerRightsAsync` retries a 404
  twice. A 404 answers no rights (fail closed), so the effect is at most a spurious refusal right after a record is
  created.
- The privilege-response parse exists twice: `Spaarke.Dataverse` cannot reference the BFF's `ResponseGrantsPrivilege`.
- `modifiedon desc` has no tie-breaker (kept exactly as decided). Ties matter only at the 5,000-row boundary of a set
  that is already flagged as capped.
- The concise ADR edits (ADR-034 A2; ADR-028 A5, with an optional A9 pointer) are drafted for the main session in
  `036-adr-edits-owner-a-f.md`. Edit 5c of `036-adr034-concise-amendment.md` is marked superseded.

## 13. Option C (owner 2026-10-20) — before → after

| # | Item | Before | After |
|---|---|---|---|
| C | Write confirmation on `/api/v1/external` | OBO probe (`GetCallerRightsAsync` / `CallerHoldsPrivilegeAsync`) for a Spaarke-tenant systemuser; app-only only for a home-linked one | App-only, naming the resolved systemuser, for **every** systemuser (`ExternalProjectDataEndpoints.CallerRightsReader` / `CallerPrivilegeReader`; `AppOnlyConfirmationPrincipal` = `SystemUserId ?? Guid.Empty`). No OBO on the plane. `CallerPrincipal.ResolvedThroughHomeLink` no longer chooses a path (removed in fix round 3, §14) |
| F | 404 right after a create | App-only path mapped 404 to None at once | Re-asked on `CallerRecordAccessProbe.NotFoundRetryDelay`'s schedule (400 ms, 1200 ms) through a new optional `reaskOnNotFound` hook on `DataverseWebApiService.RetrievePrincipalRightsOrUnknownAsync`; 403 answered at once |
| Fault | 403 `0x8004A110` (no `prvActOnBehalfOfAnotherUser`) | Read as "no rights", logged at Information | `RetrievePrincipalRightsOrUnknownAsync` → UNKNOWN → None with its own Warning (`answered a fault, not an access answer`) |
| K iii | Slow Dataverse | No bound on the app-only calls | 15 s linked timeout (`AppOnlyQuestionTimeout`) on both app-only questions; a timeout answers None / false with a Warning; the caller's own cancellation is rethrown |
| Guard | No-OBO rule | Prose only | `tests/Spaarke.ArchTests/ExternalPlaneNoOboGuardTests.cs`: no token-taking probe question, MSAL OBO exchange, `ForUser*`, `*AsUserAsync` or `IDataverseUserClient` in `Api/ExternalAccess/External*.cs`; vacuity, must-fire and must-not-fire controls |
| K i, ii | Guide §12.8 | No WorkforceHome live check; "writes exactly what the same user could write in the model-driven app" | Verify (vi) WorkforceHome check and (vii) disabled / unlicensed / team-role checks; parity stated as expected, not proven for team-inherited roles |
| K iv | Probe comments | Class summary and constructor warning implied the probe serves `/api/v1/external` | The OBO questions serve internal routes only; the external plane uses the app-only questions |

Tests and mutations: see the PR description for this round. ADR edits: `036-adr-edits-owner-a-f.md` § Option C.

## 14. Fix round 3 (2026-10-20) — before → after

| # | Item | Before | After |
|---|---|---|---|
| F3-1 | No-OBO guard scope | `Api/ExternalAccess/External*.cs` only; five OBO names | `External*.cs` plus every file the `MapGroup("/api/v1/external")` mappings bind, derived to closure (`ExternalPlaneNoOboGuardTests.BoundFiles`): today adds `MeEntitlementsEndpoint.cs`, `ContactGrantEndpoints.cs`, `ContactGrantorAuthorizationFilter.cs`, `WorkforceCeilingEndpointFilter.cs`, `WorkforceRoleCeilingGate.cs`, `Api/Filters/CallerPrincipalAuthorizationFilter.cs`. Also bans `ExtractBearerToken*` (every OBO question needs the caller's token) and `ProbeGrantorCeilingAsync` (`GrantExternalAccessEndpoint`, OBO). Controls: derivation finds today's files, does not follow the `/api/v1/external-access` group, picks up a new filter / declaration / handler; a census fails on a filter in `Api/ExternalAccess/` the derivation does not reach and that is not declared off the plane |
| F3-2 | Dataverse CreateAccess | No test pinned that `SurfaceRightsFromDataverse` ignores CreateAccess | Direct theory plus route cases (to-do create, event create, upload): `Read \| Create` with the child Create privilege held is 403 and writes nothing |
| K7 | `CallerPrincipal.ResolvedThroughHomeLink` | Set, read by nothing but tests | Removed with its feeder (`WorkforcePrincipal.ResolvedThroughHomeLink`, the `ForSystemUser` parameter, the resolver's argument) and the tests that pinned only it |
| K3 | #2121 merge (PR #2122) | — | `AccessibleRecordSetServiceTests.Fr20_FlagOn_ASecureIdInDataversesAnswer_IsDecidedByTheImpersonatedAnswerAlone`: flag on, a Secure id in Dataverse's answer stays at Read, the strict membership mock is never called. #2122's Secure lookup-only filter belongs to the flag-off path only (`!dataverseDecides`); this test fails if the merge runs it on the flag-on path |
| K5 | 403 `0x80040220` on the app-only question | Always "no rights" (Information log) | UNKNOWN (the fault Warning) when the error message names `prvActOnBehalfOfAnotherUser`; otherwise "no rights" as before. Fails closed either way |

**K5 ambiguity (documented, not resolved).** Microsoft documents no error code for an impersonated call whose
application user lacks `prvActOnBehalfOfAnotherUser`. The repo's `CannotActOnBehalfOfAnotherUser` (`0x8004A110`) comes
from the web-service error catalog and has not been observed live (`DataverseImpersonation.cs`; researcher note
`dataverse-impersonation-async-functions-2026-09-14.md`). `0x80040220` is Dataverse's generic missing-privilege code
("Principal user … is missing prv… privilege"), so it is a plausible answer for that fault too. When its message names
the privilege, `DataverseWebApiService.RetrievePrincipalRightsOrUnknownAsync` now reads it as UNKNOWN; a `0x80040220`
whose message does not name it still reads as "no rights". **Live check (task 034 canary):** if every systemuser's
confirmation logs `[DELEGATION] Rights … : None`, check `prvActOnBehalfOfAnotherUser` on the BFF application user
before reading it as a roles problem. **The same reading reaches the JIT sync** (`SpeContainerMembershipSync.cs:420`, via
`RetrievePrincipalRightsOrUnknownAsync`): a 403 `0x80040220` naming `prvActOnBehalfOfAnotherUser` now KEEPS the JIT
container grant for that run instead of removing it — task 171's intended direction (only an answer about the holder
may revoke).

**Known limits carried to the PR:**
- **K4.** An enabled but unlicensed, or Read-access-mode, systemuser under `MSCRMCallerID` impersonation is not pinned
  by any offline test (Microsoft does not document the answer). Verify in the task 034 canary.
- **K6.** A child create asks two questions in sequence (the parent's rights, then the child table's Create privilege),
  each bounded at 15 s, so up to 30 s before a refusal; each list GET asks once per request for its `canCreate`.

## 15. Fix round 4 (2026-10-20) — before → after

| # | Item | Before | After |
|---|---|---|---|
| F1 | A Dataverse fault on a write confirmation | The probe collapsed a timeout, an UNKNOWN 403 and a 429/5xx (thrown and caught) into `AccessRights.None` / `false`, so the write routes answered **403 "no access"**; delete-own step 2b broke `AuthorizeDeleteOwnAsync`'s own contract ("a read that FAILED is a 503 … never a 403") | The probe exposes `GetPrincipalRightsAppOnlyOrUnknownAsync` (`AccessRights?`) and `PrincipalHoldsPrivilegeAppOnlyOrUnknownAsync` (`bool?`): `null` = no answer (timeout, UNKNOWN 403, throttle/5xx, transport, unreadable body, no app-only client). The old `GetPrincipalRightsAppOnlyAsync` / `PrincipalHoldsPrivilegeAppOnlyAsync` remain as `?? None` / `?? false`. The readers ask the nullable ones; `WithDataverseRightsAsync` returns `ConfirmedRights(Rights, Unanswered)`, and each write gate answers `IsUnavailableFor(needed)` with a **503 that writes nothing**: to-do create, event create, upload, message → `sdap.access.dataverse_unavailable` (new, `ExternalProjectDataEndpoints.DataverseRightsUnavailable`); to-do PATCH → its existing `sdap.access.todo_write_unavailable`; delete-own → its existing `sdap.access.delete_own_unavailable`. A real denial (an answer without the right) stays 403. Lists keep `canCreate: false` (UI affordance only). A secure-roots refusal on an invoice upload stays a 403. Log lines on `null` say "no answer … unavailable, not refused", never "Dataverse does not grant". `.ProducesProblem(503)` added to the to-do / event creates, the three root uploads, the to-do PATCH and the message post |
| K1 | No-OBO guard scope derivation | Derived from the group's mappings only; a route bound to the plane some other way would escape the scan | `ExternalPlaneNoOboGuardTests.Control_EveryLiveExternalRoute_IsRegisteredInAScannedFile`: every live `/api/v1/external` route in `RouteAuthorizationGuardTests`' route census (whole-BFF scan — `Program.cs` and `Infrastructure/` included) must be registered in a scanned file; vacuity ≥ 20 plane routes; must-fire (a plane route in `Infrastructure/…` and `Program.cs`) and must-not-fire (a scanned plane route, `/api/v1/external-access`, `/api/v1/externalthings`) controls. Passes today: every plane route is in a scanned file |
| K3 | K5's message match | Substring, case-insensitive | Whole token (`DataverseWebApiService.NamesImpersonationPrivilege`): a longer privilege name that merely contains `prvActOnBehalfOfAnotherUser` is an answer about the user. JIT sync effect documented in §14 |
| K6 | 404 re-ask and the 15 s bound | Passing the outer `ct` instead of the linked token to `ReaskAfterNotFoundAsync` survived the tests (`TestProbe.DelayAsync` ignored its token) | `TestProbe.DelayAsync` honours its token (and can really wait); `GetPrincipalRightsAppOnly_AReaskThatOutlastsTheBound_IsCutOffByIt` fails when `ct` is passed |

**Known limits carried to the PR:**
- **K2** (#2147). Each confirmed write asks Dataverse once or twice, each list GET once for its `canCreate`; there is no
  per-user rate limit on the external data routes.
- **K4** (#2148). The 5,000-row cap of the impersonated read is flagged on the module data responses but is invisible on
  `/me` and `/projects`.
- **SPA mapping of the new 503.** `src/client/external-spa` maps an unknown 503 reason code — see the PR body.
