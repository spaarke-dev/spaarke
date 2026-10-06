# Task 161 — communications routes authorize the exact record they act on

> **GitHub**: #1100 · **Branch**: `task/uac-r2-161` (from `work/unified-access-control-r2` @ `6b243f092`)
> **Sweep rows closed**: 7 (send, send-bulk), 8 (template render), 33 (archive), 34 (suggest-associations),
> 35 (ad-hoc create-task), 58 (threads), 59 (confirm-affinity), 60 (dismiss), 61 (create-task/apply),
> 62 (accounts verify), 79 (status — **deleted**), plus the folded-in Office suggestions leak.
> **Mandate**: owner round 9 (fix pattern), round 10 item 1 (delete zero-caller routes), POML amendments.
> **Fix round r1** (`task/uac-r2-161-r1`, 2026-10-03): the adversarial verifier's findings on `7f9159027` — see §15.

## 1. What changed, in one paragraph

Every `/api/communications` route that reads, attaches to or changes a record the caller names now carries
`.AddCommunicationRecordAuthorizationFilter(CommunicationRecordRoute.X)` **after** the identity precondition
`CommunicationAuthorizationFilter`. The new filter (`Api/Filters/CommunicationRecordAuthorizationFilter.cs`) asks
Dataverse **as the caller** about the exact record the handler is about to touch, composing only existing seams —
`CommunicationThreadReadService` (impersonated visibility + `ICommunicationAccessFilter`), `CallerRecordAccessProbe`
(RetrievePrincipalAccess / table privileges over OBO), `AuthorizationService` (the document "read" decision the
download routes make) and `IImpersonatedCommunicationQuery` (template readability). Unknown and denied ids get the
same answer; every fault denies. The template merge read now runs as the caller; the suggestion previews remove
every candidate the caller cannot read **before** the engine's ladder decides; `GET /{id}/status` is deleted.

## 2. Live-verified names (spaarkedev1, read-only, 2026-10-03)

Read with `GET EntityDefinitions(LogicalName='…')?$select=EntitySetName,PrimaryNameAttribute,PrimaryIdAttribute`,
`…/Attributes(LogicalName='…')` and `GET privileges?$filter=…` under the operator's own token. No write was made.

| Logical name | EntitySetName | PrimaryIdAttribute | Display name used by `RegardingNameFields` |
|---|---|---|---|
| `sprk_reportcard` | `sprk_reportcards` | `sprk_reportcardid` | `sprk_name` (PrimaryNameAttribute) |
| `sprk_communicationaccount` | `sprk_communicationaccounts` | — | — |
| `sprk_emailreviewlog` | `sprk_emailreviewlogs` | — | (read app-only by logical name; set not used) |
| `sprk_document` | `sprk_documents` | — | — |
| `template` | `templates` | `templateid` | `title` |
| `sprk_communication` | `sprk_communications` | `sprk_communicationid` | — |
| `sprk_communicationthread` | `sprk_communicationthreads` | — | — |
| `sprk_analysis` | **`sprk_analysises`** | `sprk_analysisid` | `sprk_name` |
| `sprk_organization` | `sprk_organizations` | `sprk_organizationid` | **`sprk_organizationname`** (`sprk_name` does NOT exist — 404 on the attribute GET) |
| `sprk_matter` / `sprk_project` / `sprk_invoice` / `sprk_event` / `sprk_workassignment` / `sprk_servicerequest` / `sprk_budget` / `contact` / `account` | `sprk_matters` / `sprk_projects` / `sprk_invoices` / `sprk_events` / `sprk_workassignments` / `sprk_servicerequests` / `sprk_budgets` / `contacts` / `accounts` | `{logicalname}id` for every one | `sprk_mattername` / `sprk_projectname` / `sprk_name` / `sprk_eventname` / `sprk_name` ×3 / `fullname` / `name` — each attribute verified to exist |

| Privilege | Live name | accessright |
|---|---|---|
| Create on sprk_event | `prvCreatesprk_Event` | 32 |
| Assign on sprk_event | `prvAssignsprk_Event` | 524288 |
| Create on sprk_document | `prvCreatesprk_Document` | 32 |

**Two catalogue defects found and fixed in `RegardingNameFields`** (no deferral, owner 2026-09-30):

- `sprk_analysis` set was `sprk_analyses`; live is `sprk_analysises`. A delegated read of the wrong set is a 404 —
  indistinguishable from "not readable" — so analysis candidates the caller WAS entitled to see were dropped. (The POML
  said the opposite, that the unused `CommunicationService.RegardingLookupMap` column was wrong; live metadata shows
  that column is right and `RegardingNameFields` was wrong. `RegardingLookupMap` is still not used.)
- `sprk_organization` display name was `sprk_name`, which does not exist on that table: every read selecting it 400'd,
  so the denorm writer (`IncomingAssociationResolver`) recorded no name for an organization and the Office picker never
  named one. Now `sprk_organizationname`.

Every value is pinned by `RegardingNameFieldsTests.EachCoveredType_AddressesItsLiveEntitySetAndNameField` and the filter
constants by `CommunicationRecordAuthorizationContractTests.TheEntitySetAndPrivilegeNames_AreTheLiveVerifiedValues`.

**SDK vs Web API value shapes (read-only SDK probe, `ServiceClient` RetrieveMultiple, ColumnSet(true), as the
operator)** — the input to the template-parity decision (§4):

| Kind | SDK value (pre-161 merge read) | Handlebars text | Web API JSON (impersonated read) |
|---|---|---|---|
| Money / Decimal | `Money(1000.0000000000m)` / `decimal` with 10 places | `1000.0000000000` | `1000.0000000000` (raw text keeps the scale) |
| Date-time (UserLocal) | `DateTime`, Kind=Utc | `2026-09-30T04:00:00.0000000Z` | `"2026-09-30T04:00:00Z"` + FormattedValue |
| Option set / statecode | `OptionSetValue(1)` → `1` | `1` | `1` + FormattedValue |
| Lookup | `EntityReference { Name }` → name | `Ralph Schroeder` | `_ownerid_value` + FormattedValue = name |
| Two-option | `bool false` | `False` | `false` + FormattedValue |
| Primary key | `Guid` | `6f9641d0-…` (lower case) | `"6f9641d0-…"` |

## 3. Dependencies on parallel tasks

- **Task 167 (every-route guard) has NOT landed on this branch.** `RouteAuthorizationGuardTests.cs` on the branch has no
  Pending waiver naming 161 and neither `CommunicationEndpoints.cs` nor `CommunicationTemplateEndpoints.cs` is in
  `GovernedFiles`. Per the amendment, no waiver, ledger entry or `GovernedFiles` entry was added; §9 is the ledger input.
  The guard's existing rules already see the new filter: `FilterMarker` credits `.AddCommunicationRecordAuthorizationFilter(`
  (`\.Add\w*AuthorizationFilter\s*[<(]`), and Rule B passes because the file references `CallerRecordAccessProbe`,
  `AuthorizationService` and `AccessRights` (all in `DecisionServices`). Only the `ClaimOnlyFilters` reason for
  `CommunicationAuthorizationFilter` changed (it claimed per-record scoping was `ICommunicationAccessFilter`'s job —
  false for every route here).
- **Task 146 has NOT landed.** `TaskActionCore.cs:191-192` on this branch still writes `ownerid` from
  `OwnerId = request.AssignedTo`, so the **Assign constraint applies**: an `AssignedTo` that is not the caller's own
  systemuserid requires `prvAssignsprk_Event`. When 146 lands and `ownerid` stops coming from `AssignedTo`, delete that
  check (it is one `if` in `AuthorizeTaskCreateAsync`) — recorded for whichever of 146/161 merges second.

## 4. Decisions and behaviour changes (each one visible to a user)

1. **Template merge parity — kept, escalation trigger 2 did NOT fire.** The merge read moved from the app-only
   `QueryExpression … ColumnSet(true)` to the caller's impersonated OData read. `ToMergeVariables` maps each JSON value
   back to the CLR type the SDK returned (option (a) of the trigger): a lookup `_x_value` → key `x` with its
   FormattedValue name; an ISO date string that carries a FormattedValue → `DateTime` of the same kind (`Z` → Utc); a
   non-integral number → `decimal` parsed from the raw text (scale kept); integral → `int`/`long`; booleans stay `bool`.
   The characterization test (`CommunicationTemplateEndpointTests.RenderTemplate_MergeParity_EachValueKindRendersAsBefore`)
   was written FIRST against the pre-161 handler (SDK `Entity` input) and passed (7/7 in the class); after the change the
   SAME expected strings come out of the OData row (10/10). Two kinds outside the characterized set are listed for the PR,
   not shipped silently:
   - **multi-select option set**: SDK `OptionSetValueCollection` rendered as its .NET type name (garbage); now `"1,2"`.
   - **date-only, TimeZoneIndependent**: no live row existed to probe; parsed as an Unspecified-kind `DateTime`, which
     renders `2026-03-30T00:00:00.0000000` (no `Z`). Confirm in the live gate (b) if a template uses one.
   - Field-level security now applies: a secured column the caller has no field permission for renders empty (intended).
2. **The composer's template list and the gate agree.** `createXrmEmailComposeHandlers.ts` lists templates through the
   caller's own `Xrm.WebApi`, so a listed template is readable; the gate's impersonated top-1 read asks the same question.
   `prvReadEmailTemplate` is Global in Spaarke Core User / Basic User (read-only role read).
3. **Thread rename / pin / deactivate and message deactivate now require WRITE** on the shared record (amendment,
   owner D1). Behaviour change: the non-owner participant of a Direct 1:1 thread holds only the `ReadAccess` POA share
   (`DirectThreadAccessService.cs:26`), so **they can no longer pin, rename or delete that thread, or delete a message
   in it**. Also, Spaarke Core User holds `prvWritesprk_CommunicationThread` at **Basic** depth only, so a user can pin /
   rename only threads they own (or that are shared to them with Write). Not weakened, per the amendment.
   ⚠️ **Wider than the amendment anticipated — for the owner/main session**: outbound messages are created by the BFF
   identity (`CommunicationService.cs:963-966`: "this message is owned by the app-only identity") and participants get
   only `ReadAccess` shares, and app-found-or-created threads are app-owned too. In dev the BFF application user sits in
   the ROOT business unit while users sit in a child BU, so Spaarke Core User's **Deep** Write on `sprk_communication`
   does not reach those rows: **a non-admin can no longer delete an app-owned message, or pin / rename / delete an
   app-owned thread.** Task 146 (team ownership) changes who owns those rows; until it lands this is the live behaviour.
   Live gate (d) exercises it; the choice to keep Write (owner D1) is the amendment's, not re-decided here.
4. **send-bulk over the attachment cap** now answers one 400 `ATTACHMENT_LIMIT_EXCEEDED` (the gate checks the cap before
   any rights query) instead of today's 207 with a per-recipient failure for every recipient. Same code/title/detail as
   `SendAsync`'s own (pinned by `AttachmentValidationTests.SendAsync_Over150Attachments_RefusesWithExactlyTheAnswerTheRouteGateGivesFirst`).
5. **An unresolved caller or a missing bearer token** is denied with the route's own deny answer before any record is
   looked at (so the answer cannot vary with whether the record exists): 404 bodies for route-id routes, the route's
   403 reasonCode for body routes, 200 `{"recordedSignals":0}` for confirm-affinity. Previously several of these routes
   answered 403 `CALLER_NOT_RESOLVED` from inside the service; that 403 is now unreachable through these routes.
6. **Record-thread name**: the thread's `sprk_regardingrecordname` (and `sprk_name` when no name is given) is the
   record's own primary name read **as the caller**; the body `regardingRecordName` is ignored. The type is normalized
   to lower case before it reaches `ThreadResolver` (a client sending `sprk_Matter` works, as before).
7. **Suggestions**: an unreadable candidate is removed before the ladder decides, so `Status`, `AutoFileEligible`,
   `Conflict` and `Written` are those the engine would compute if the hidden record did not exist (amendment — this
   supersedes the POML notes item 4, which had left the status computed over every candidate). A structural signal or a
   contributor whose provenance names a removed id is removed with it. A missing caller context
   (`UserContextRequired` / OBO not configured / OBO exchange failed) fails the request (403 on suggest-associations,
   the Office route's existing 500) — never an empty list. Implementation: one optional admission callback on
   `IncomingAssociationResolver.EvaluateAsync` (the same rungs and the same ladder; the inbound/outbound filing path
   passes none) — not a fork of the evaluate path (ADR-045).
8. **Organization candidates are named again** (the `RegardingNameFields` fix, §2) and analysis candidates are no longer
   silently dropped.
9. **`sprk_reportcard`** is now addressable. Caveat found while reading: `sprk_communicationthread` has no
   `sprk_regardingreportcard` lookup (`CommunicationThreadReadService.cs:49-55`), so `POST /threads` regarding a report
   card passes the gate and then fails in `ThreadResolver`'s create (500), exactly as it did before 161. Not a regression;
   recorded here, not fixed (a schema question, outside this task).
10. **(r1) The regarding-type 400 comes before the caller lookup** on `POST /threads` and `POST /template/render`
    (verifier item 8). A request whose regarding type has no live-verified entity set (or, on `/threads`, whose
    regarding is missing) gets its 400 `VALIDATION_ERROR` with NO Dataverse call — not even the app-only systemuser
    lookup the caller precondition runs. Visible consequence: such a request with no bearer token (or an unresolved
    caller) now gets the 400 instead of the route's 403. The 400 depends only on the body, so it is no oracle.
11. **(r1) A missing template's 404 is always the one `TemplateNotFound` body** (verifier item 9). The handler's
    not-found branch now returns `CommunicationTemplateEndpoints.TemplateNotFound(templateId)` — the body the filter
    denies an unreadable template with — instead of echoing the service's error text. For the service's real message
    (`Email template not found: {id}`) the bytes are unchanged; a not-found worded differently would now carry the
    canonical detail too, so "unknown equals denied" no longer rests on the service's wording.

## 5. Routes deleted (owner round 10 item 1)

Searched before deciding: every file in the repo except `projects/**`, build output and `node_modules`, for the route
paths and handler names; and the published API descriptions — `src/solutions/CopilotAgent/spaarke-bff-openapi.yaml`,
`spaarke-api-plugin.json`, `declarativeAgent.json` (the only Spaarke OpenAPI / plugin manifests; the `knowledge/**`
manifests are third-party samples). The OpenAPI document publishes exactly one route of this group: `POST /api/communications/send`.

| Route | Decision | Evidence |
|---|---|---|
| `GET /api/communications/{id:guid}/status` | **DELETED** (route, handler, `CommunicationStatusResponse` DTO) | No caller: `src/**` has only two client *comments* naming it (`EmailComposer.tsx:1107`, `EmailComposer.reducer.ts:317`); no script, flow, ribbon or test calls it (the two tests that mentioned it re-implemented the private handler's logic — mirror tests, deleted with it: `CommunicationStatusEndpointTests.cs`, region 4 of `CommunicationIntegrationTests.cs`). Not in the OpenAPI document. Only `docs/architecture/sdap-overview.md` listed it (line removed). Its absence is pinned by `Status_TheDeletedRoute_AnswersNobody_AndReadsNothing`. |
| `POST /api/communications/{id:guid}/suggest-associations` | kept, fixed | Caller: `scripts/uat-rung-preview-harness.ps1:29`. |
| `POST /api/communications/send-bulk` | kept, fixed | Published for direct use: `docs/guides/communication-user-guide.md:226` ("API-Based (Administrator/Developer Use)") — the only bulk-send surface. |
| `POST /api/communications/accounts/{id:guid}/verify` | kept, fixed | Caller: the operator runbook `docs/guides/COMMUNICATION-ADMIN-GUIDE.md:130`, `:488`, `:685`, `:696` (curl), and the form's Verify button it documents (`:447`). |

Stale references left (client comments only — editing them would put a client build in scope for no behaviour):
`EmailComposer.tsx:1107`, `EmailComposer.reducer.ts:317`.

Observation (verifier r1, item 2 — not a consumer, left unchanged): `scripts/Capture-BffBaseline.ps1` probes every route
listed in `projects/sdap-bff-api-remediation-fix/baseline/endpoints-smoke.json`, which still lists `/status`. A rerun
of that historical baseline capture now gets 404 for it instead of 401. It is another project's frozen baseline
artefact, not a caller, so it is not edited here.

## 6. Escalation triggers — checked, none fired

| Trigger | Result |
|---|---|
| 1 legitimate user denied (SPE-only document access, unmapped association type, archive / reconcile privileges) | Role reads (read-only, root-BU role definitions): **Spaarke Core User** holds `prvCreatesprk_Event` Deep, `prvAssignsprk_Event` Deep, `prvCreatesprk_Document` Deep, `prvAppendTosprk_Communication` Deep, `prvWritesprk_Communication` Deep, `prvAppendTosprk_Matter`/`_Project`/`_Invoice` Deep, `prvAppendTocontact` **Basic**, `prvWritesprk_CommunicationThread` Basic, `prvWritesprk_CommunicationAccount` Basic; **Spaarke Basic User** adds `prvAppendTosprk_Organization` Deep. So the listed denials do not occur for the round-11 test user's roles. ⚠️ One to exercise live: `prvAppendTocontact` is **Basic**, so filing an email / task / thread against a contact the user does not own is now refused — the same refusal Dataverse gives that user for the same lookup write through `Xrm.WebApi`. Clients send ADR-024 family types only (`communicationApi.ts:309`, `TaskReconcileTab.tsx:251-256`). |
| 2 template parity | Kept (§4.1). |
| 3 composer host passes an uncatalogued type | The composer passes `primary?.entityType` (`EmailComposer.tsx:750`) — the primary association, an ADR-024 family type. No host passes `sprk_communication` / a non-Spaarke table. |
| 4 Assign privilege missing for reconcile users | Spaarke Core User holds `prvAssignsprk_Event` Deep — not fired. Live gate (g) re-reads it for the actual test user. |
| 5 confirm flow records a target outside the typed lookups | `applyRegardingSelection` writes the typed `sprk_regarding*` lookup before calling confirm-affinity (`ConnectionsWriteHandler.ts:205-244`, `EmailConnectionsReview.tsx:163-176`). Live gate (d) confirms. |
| 6 verify operators lack Write on the account | The runbook is an administrator procedure (System Administrator holds Global Write). A non-admin holds only Basic Write via Core / Basic User → can verify only accounts they own. Record right kept (owner D1/C9); the SystemAdmin-policy alternative is NOT adopted. |
| 7 guard cannot credit the shape | 167 not landed; the existing `FilterMarker` + Rule B credit it. |
| 8 needs ExternalAccess edits / 2nd policy key | None: `CallerRecordAccessProbe` reused as is; no `OperationAccessPolicy` key added (AppendTo/Write compared as flags). |
| 9 ADR exception / client contract change | None. The handlers' response shapes are unchanged; `status` was deleted under round 10. |

## 7. Tests

**New**: `tests/integration/contract/Api/Communication/CommunicationRecordAuthorizationContractTests.cs` (host:
`CommunicationRecordAuthorizationHost.cs` — the REAL `MapCommunicationEndpoints` / `MapCommunicationTemplateEndpoints`
via `WebApplicationFactory<Program>`, substituting only module boundaries: caller resolver, app-only entity service,
impersonated query, identity resolver, delegated user client, the access data source behind the REAL
`AuthorizationService`, `CallerRecordAccessProbe`'s virtual seams, channel senders, document metadata service, the
engine's `IAssociationRung` plug-in point, the SPE download and the inert core-ancestor resolver; no HTTP mocks);
`RegardingNameFieldsTests.cs` (lock-step by source scan + live pins).
**Changed**: `CommunicationTemplateEndpointTests` (merge read as the caller, parity, faults), `CommunicationThreadReadServiceTests`
(+4 for `ReadVisibleCommunicationAsync`), Office `CommunicationsEndpointsContractTests` (+ the trimmed-suggestions 200 via
the engine's rung plug-in), `CommunicationsDelegatedReadGuardTests` (follows the moved candidate read),
`CommunicationRenameThreadContractTests` / `CommunicationSetThreadPinnedContractTests` / `CommunicationCreateRecordThreadContractTests`
(caller token + Write/AppendTo grants + the caller-read name), `AttachmentValidationTests` (+ cap parity),
`CommunicationIntegrationTests` (status mirror region deleted), `RouteAuthorizationGuardTests` (ClaimOnlyFilters reason).
**Deleted**: `CommunicationStatusEndpointTests.cs` (mirror of the deleted handler).

`CommunicationService.SendAsync` has no caller check: `git diff` touches no line of `CommunicationService.cs`, and the existing
`CommunicationServiceChannelRoutingTests.SendAsync_SharedMailbox_RoutesThroughChannelSenderSeam` calls `SendAsync` with no
`HttpContext` in SharedMailbox mode and still dispatches.

**Changed in r1** (verifier items 8, 9, 11, 14, 15):
- `CreateRecordThread_TypeWithNoEntitySet_Is400BeforeAnyDataverseCall` and `TemplateRender_ATypeOutsideTheCatalogue_Is400BeforeAnyDataverseCall`
  now assert, through `AssertNoDataverseCallAtAll`, that NOT ONE Dataverse round trip happens — including the
  `ICallerSystemUserResolver` lookup (`_host.Callers` has no invocation), the identity resolver, the probe, the
  impersonated and delegated reads, the document data source, the app-only entity service and the impersonated write.
- `Proposal_VisibleToTheCaller_ReachesTheServiceUnchanged` (asserted only "not the not-found answer") is replaced by
  `ProposalApplyAndUndo_AuthorizedCaller_WriteTheTargetAsTheCallerAsToday` (apply / undo): 200, the target field written
  through `IFieldMappingDataverseService.UpdateRecordFieldsAsync` **as the caller** (`impersonateSystemUserId` = the
  caller's systemuserid) with apply's `newValue` / undo's `oldValue`, and exactly one audit row.
- `TaskUndo_VisibleCommunication_ReachesTheServiceUnchanged` is replaced by `TaskUndo_AuthorizedCaller_CancelsTheTaskAsTheCallerAsToday`:
  200, `sprk_eventstatus` written on that task as the caller, one compensating audit row naming the communication.
- The deny tests for the proposal routes and tasks undo, and `AssertNothingDownstream` (every-route theories), also assert
  no impersonated write happened.
- Host: `CommunicationRecordAuthorizationHost` substitutes one more module boundary, `IFieldMappingDataverseService`
  (the Spaarke.Dataverse interface behind `IActionSeam.UpdateRecordAsync`'s impersonated PATCH), so the authorized
  Job B/C paths run to completion in-process instead of reaching for a live Dataverse.
- New unit test `CommunicationTemplateEndpointTests.RenderTemplate_WhenTheServiceReportsNotFoundInOtherWords_AnswersTheSameBodyTheRecordFilterDeniesWith`
  — reason: pins item 9's guarantee (the handler's 404 is the filter's deny body whatever the service's wording); no
  other test can see it, because every other not-found fixture uses the service's exact text.

Tests beyond the closed AC set, each with its reason: `ToMergeVariables_DateOnlyAndPlainStrings_KeepTheirSdkShapes` (pins the
two mapping rules the parity fixture cannot reach — date-only kind, and that an un-annotated ISO-looking string stays text);
`TheCatalogue_CoversEveryLiveVerifiedTypeAndNothingElse` (a type added without a live pin fails); `ATypeOutsideTheCatalogue_IsNotGuessed`
(case-sensitivity is the contract callers normalize against).

## 8. Seeding (each check removed once, a named test must fail, then restored and touched)

Harness: each seed replaced the check's source text once (an exact single match was asserted), ran the named test filter, restored the file byte for byte from a copy and refreshed its mtime. All 36 bite. Afterwards a search for every seed text in `src/` found none left behind.

| Seed (check removed) | Result | Named test(s) that failed |
|---|---|---|
| S01 send: attachment Read | BITES | `Send_AnAttachmentTheCallerCannotRead_Is403_AndUnknownOrUnparseableIdsGetTheSameAnswer(sendMode:`; `Send_AnAttachmentTheCallerCannotRead_Is403_AndUnknownOrUnparseableIdsGetTheSameAnswer(sendMode:` |
| S02 send: thread visibility | BITES | `Send_AThreadTheCallerCannotSee_Is403ForAMessage_AndAnUnknownThreadGetsTheSameAnswer` |
| S03 send: inherit-from visibility | BITES | `Send_AnInheritFromCommunicationTheCallerCannotSee_Is403_AndAnUnknownOneGetsTheSameAnswer` |
| S04 send: association AppendTo | BITES | `Send_AnAssociationWithoutAppendTo_Is403_AndUnknownOrUnaddressableTargetsGetTheSameAnswer` |
| S05 send: copied regarding AppendTo | BITES | `Send_AVisibleInheritFromCommunicationWhoseRegardingTheCallerCannotAttachTo_Is403` |
| S06 send: attachment cap before rights | BITES (first seed `if (false)` did not compile under warnings-as-errors; re-seeded with a runtime-false condition) | `Send_151Attachments_GetsTodays400_BeforeAnyRightsQuery` |
| S07 template: type allow-list | BITES | `TemplateRender_ATypeOutsideTheCatalogue_Is400BeforeAnyDataverseCall` |
| S08 template: Read on the regarding record | BITES (first seed `if (false)` did not compile under warnings-as-errors; re-seeded with a runtime-false condition) | `TemplateRender_ARegardingRecordTheCallerCannotRead_Is403WithNoTraceOfIt_AndAnUnknownOneGetsTheSameAnswer` |
| S09 template: readable template | BITES | `TemplateRender_ATemplateTheCallerCannotRead_GetsExactlyWhatANonExistentTemplateGets` |
| S10 template: merge read AS THE CALLER | BITES | `RenderTemplate_MergeParity_EachValueKindRendersAsBefore`; `TemplateRender_AuthorizedCaller_ReadsTheRegardingAsTheCaller_NeverThroughTheAppOnlyService` |
| S11 archive: visibility | BITES | `Archive_InvisibleOrInternalOnlyCommunication_GetsTheHandlersOwnNotFound_AndNothingIsArchived` |
| S12 archive: AppendTo on the communication | BITES | `Archive_VisibleButWithoutTheRightToFileAgainstIt_Is403AndNothingIsWritten(missing:` |
| S13 archive: Create on sprk_document | BITES | `Archive_VisibleButWithoutTheRightToFileAgainstIt_Is403AndNothingIsWritten(missing:` |
| S14 suggest: visibility | BITES | `SuggestAssociations_InvisibleCommunication_GetsTheHandlersOwnNotFound_AndTheEngineNeverRuns` |
| S15 suggestions: candidate trimming | BITES | `GetSuggestions_ACandidateTheCallerCannotRead_IsAbsentEverywhere_ReadableOnesKeepNamesAndFiling`; `SuggestAssociations_ACandidateTheCallerCannotRead_IsAbsentEverywhere_AndTheStatusIsDecidedWithoutIt` |
| S16 suggestions: status decided over the readable set | BITES | `SuggestAssociations_ACandidateTheCallerCannotRead_IsAbsentEverywhere_AndTheStatusIsDecidedWithoutIt` |
| S17 suggestions: signals naming a removed id | BITES | `SuggestAssociations_ACandidateTheCallerCannotRead_IsAbsentEverywhere_AndTheStatusIsDecidedWithoutIt` |
| S18 confirm-affinity: visibility + regarding match | BITES | `ConfirmAffinity_EveryDeny_IsTheZeroSignalsNoOp_AndAffinityIsNeverWritten(reason:`; `ConfirmAffinity_EveryDeny_IsTheZeroSignalsNoOp_AndAffinityIsNeverWritten(reason:` |
| S19 confirm-affinity: Write | BITES | `ConfirmAffinity_EveryDeny_IsTheZeroSignalsNoOp_AndAffinityIsNeverWritten(reason:` |
| S20 threads: AppendTo | BITES | `CreateRecordThread_WithoutAppendTo_Is403_AndANonExistentRecordGetsTheSameAnswer` |
| S21 threads: unmapped type 400 | BITES | `CreateRecordThread_TypeWithNoEntitySet_Is400BeforeAnyDataverseCall` |
| S22 threads: name read as the caller | BITES | `CreateRecordThread_AuthorizedCaller_NamesTheThreadWithTheRecordsOwnName_NotTheBodys`; `CreateRecordThread_WithValidRegarding_Returns200_CreatesRecordAnchoredThreadOwnedByCaller` |
| S23 create-task: communication visibility | BITES | `CreateAdHocTask_InvisibleCommunication_IsTheNotFoundANonExistentIdGets` |
| S24 task create: AppendTo on the regarding | BITES | `ProposalCreateTaskApply_VisibleButMissingARight_Is403_AndNothingIsCreated(missing:`; `CreateAdHocTask_EachMissingRight_Is403_AndNothingIsCreated(missing:`; `CreateAdHocTask_EachMissingRight_Is403_AndNothingIsCreated(missing:` |
| S25 task create: type with no entity set | BITES | `CreateAdHocTask_EachMissingRight_Is403_AndNothingIsCreated(missing:` |
| S26 task create: Create on sprk_event | BITES | `ProposalCreateTaskApply_VisibleButMissingARight_Is403_AndNothingIsCreated(missing:`; `CreateAdHocTask_EachMissingRight_Is403_AndNothingIsCreated(missing:` |
| S27 task create: Assign for another owner | BITES (first seed `if (false)` did not compile under warnings-as-errors; re-seeded with a runtime-false condition) | `ProposalCreateTaskApply_VisibleButMissingARight_Is403_AndNothingIsCreated(missing:`; `CreateAdHocTask_EachMissingRight_Is403_AndNothingIsCreated(missing:` |
| S28 proposals: communication visibility | BITES | `Proposal_WhoseCommunicationTheCallerCannotSee_GetsTheServicesOwnNotFound_AndNothingIsWritten(action:`; `Proposal_WhoseCommunicationTheCallerCannotSee_GetsTheServicesOwnNotFound_AndNothingIsWritten(action:`; `Proposal_WhoseCommunicationTheCallerCannotSee_GetsTheServicesOwnNotFound_AndNothingIsWritten(action:` |
| S29 create-task/apply: proposal visibility | BITES | `Proposal_WhoseCommunicationTheCallerCannotSee_GetsTheServicesOwnNotFound_AndNothingIsWritten(action:` |
| S30 tasks undo: visibility | BITES | `TaskUndo_InvisibleCommunication_IsTheNotFoundANonExistentIdGets_BeforeAnyWrite` |
| S31 verify: Write on the account | BITES | `VerifyAccount_WithoutWrite_GetsTheHandlersOwnNotFound_AndNoGraphOrWrite` |
| S32 thread rename/pin/delete: Write | BITES | `ThreadWrite_ByAReadOnlyCaller_Is403_AndTheThreadIsNotWritten(route:`; `ThreadWrite_ByAReadOnlyCaller_Is403_AndTheThreadIsNotWritten(route:`; `ThreadWrite_ByAReadOnlyCaller_Is403_AndTheThreadIsNotWritten(route:` |
| S33 message delete: Write | BITES | `MessageDeactivate_ByAReadOnlyCaller_Is403_AndAWriterDeactivates` |
| S34 preconditions: bearer token + resolvable caller | BITES | `EveryRoute_FailsClosed_TheSameWayWhetherOrNotTheRecordExists(route:`; `EveryRoute_FailsClosed_TheSameWayWhetherOrNotTheRecordExists(route:`; `EveryRoute_FailsClosed_TheSameWayWhetherOrNotTheRecordExists(route:` |
| S35 RegardingNameFields lock-step | BITES | `BothFunctions_AcceptExactlyTheSameKeys`; `TheCatalogue_CoversEveryLiveVerifiedTypeAndNothingElse` |
| S36 visibility: the shared access filter | BITES | `ReadVisibleCommunicationAsync_InternalOnlyRowForAnExternalCaller_IsNotVisible`; `Archive_InvisibleOrInternalOnlyCommunication_GetsTheHandlersOwnNotFound_AndNothingIsArchived` |
| S37 (r1) request shape decided before the caller lookup — seed: the caller precondition moved back ahead of `CheckRequestShape()` | BITES | `CreateRecordThread_TypeWithNoEntitySet_Is400BeforeAnyDataverseCall`; `TemplateRender_ATypeOutsideTheCatalogue_Is400BeforeAnyDataverseCall` (both: the 400 still came back, but `_host.Callers` had been invoked) |
| S38 (r1) the handler's not-found branch returns `TemplateNotFound` — seed: restored `Results.Problem(detail: result.Error, …)` | BITES | `CommunicationTemplateEndpointTests.RenderTemplate_WhenTheServiceReportsNotFoundInOtherWords_AnswersTheSameBodyTheRecordFilterDeniesWith` |
| S39 (r1) an authorized proposal reaches the service — seed: the filter answers a VISIBLE proposal with a 403 (which the replaced weak test, asserting only "not 404 / not PROPOSAL_NOT_FOUND", would have passed) | BITES | `ProposalApplyAndUndo_AuthorizedCaller_WriteTheTargetAsTheCallerAsToday(action: "apply")`; `(action: "undo")` |
| S40 (r1) an authorized communication reaches the task-undo service — seed: the filter answers a VISIBLE communication with a 403 (the replaced weak test would have passed) | BITES | `TaskUndo_AuthorizedCaller_CancelsTheTaskAsTheCallerAsToday` |

r1 harness: the same single-exact-match replace / build / named filter / byte-for-byte restore + mtime touch, scripted;
`git status` afterwards showed only the intended edits.

## 9. Route authorization ledger input (for task 167's ledger — main session)

Test class prefix `Sprk.Bff.Api.Tests.Api.Communication.CommunicationRecordAuthorizationContractTests.` unless stated.

| Route key | Mechanism that now decides | Deny test (proves a caller without rights is refused) |
|---|---|---|
| `POST /api/communications/send` | filter `CommunicationRecordAuthorizationFilter` (Send): `AuthorizationService` "read" per attachment, `CanCallerSeeThreadAsync`, `ReadVisibleCommunicationAsync` + AppendTo on copied regarding, AppendTo per association (`CallerRecordAccessProbe`) | `Send_AnAttachmentTheCallerCannotRead_Is403_AndUnknownOrUnparseableIdsGetTheSameAnswer` |
| `POST /api/communications/send-bulk` | filter (SendBulk) | `SendBulk_AnUnreadableAttachment_Is403ForTheWholeRequest_AndAnUnknownOneGetsTheSameAnswer` |
| `POST /api/communications/template/render` | filter (TemplateRender): Read on the regarding (`CallerRecordAccessProbe`) + impersonated template read; handler reads the regarding as the caller (`IImpersonatedCommunicationQuery`) | `TemplateRender_ARegardingRecordTheCallerCannotRead_Is403WithNoTraceOfIt_AndAnUnknownOneGetsTheSameAnswer` |
| `GET /api/communications/{id:guid}/status` | **DELETED** (§5) | `Status_TheDeletedRoute_AnswersNobody_AndReadsNothing` |
| `POST /api/communications/{id:guid}/archive` | filter (Archive): visibility + AppendTo + `prvCreatesprk_Document` | `Archive_InvisibleOrInternalOnlyCommunication_GetsTheHandlersOwnNotFound_AndNothingIsArchived` |
| `POST /api/communications/{id:guid}/suggest-associations` | filter (visibility); handler trims candidates as the caller (`SuggestionCandidateAccess`) | `SuggestAssociations_InvisibleCommunication_GetsTheHandlersOwnNotFound_AndTheEngineNeverRuns` |
| `POST /api/communications/{id:guid}/confirm-affinity` | filter (ConfirmAffinity): visibility + typed regarding match + Write; deny = 200 no-op | `ConfirmAffinity_EveryDeny_IsTheZeroSignalsNoOp_AndAffinityIsNeverWritten` |
| `POST /api/communications/threads` | filter (CreateRecordThread): AppendTo on the regarding | `CreateRecordThread_WithoutAppendTo_Is403_AndANonExistentRecordGetsTheSameAnswer` |
| `POST /api/communications/{communicationId:guid}/create-task` | filter (CreateAdHocTask): visibility, AppendTo, `prvCreatesprk_Event`, `prvAssignsprk_Event` for another owner | `CreateAdHocTask_EachMissingRight_Is403_AndNothingIsCreated` |
| `POST /api/communications/proposals/{reviewLogId:guid}/dismiss` | filter (proposal visibility via its communication) | `Proposal_WhoseCommunicationTheCallerCannotSee_GetsTheServicesOwnNotFound_AndNothingIsWritten(action: "dismiss")` |
| `POST /api/communications/proposals/{reviewLogId:guid}/create-task/apply` | filter: proposal visibility, AppendTo on the target, Create (+ Assign) on sprk_event | `ProposalCreateTaskApply_VisibleButMissingARight_Is403_AndNothingIsCreated` |
| `POST /api/communications/accounts/{id:guid}/verify` | filter (AccountVerify): Write on the account | `VerifyAccount_WithoutWrite_GetsTheHandlersOwnNotFound_AndNoGraphOrWrite` |
| `POST /api/communications/proposals/{reviewLogId:guid}/apply` | filter (proposal visibility) | `Proposal_WhoseCommunicationTheCallerCannotSee_GetsTheServicesOwnNotFound_AndNothingIsWritten(action: "apply")` |
| `POST /api/communications/proposals/{reviewLogId:guid}/undo` | filter (proposal visibility) | `Proposal_WhoseCommunicationTheCallerCannotSee_GetsTheServicesOwnNotFound_AndNothingIsWritten(action: "undo")` |
| `POST /api/communications/{communicationId:guid}/tasks/{taskId:guid}/undo` | filter (communication visibility) | `TaskUndo_InvisibleCommunication_IsTheNotFoundANonExistentIdGets_BeforeAnyWrite` |
| `POST /api/communications/threads/{threadId:guid}/rename` | filter (Write on the thread) + handler visibility | `ThreadWrite_ByAReadOnlyCaller_Is403_AndTheThreadIsNotWritten(route: "rename")` |
| `PATCH /api/communications/threads/{threadId:guid}/pin` | filter (Write on the thread) + handler visibility | `ThreadWrite_ByAReadOnlyCaller_Is403_AndTheThreadIsNotWritten(route: "pin")` |
| `DELETE /api/communications/threads/{threadId:guid}` | filter (Write on the thread) + handler visibility | `ThreadWrite_ByAReadOnlyCaller_Is403_AndTheThreadIsNotWritten(route: "deactivate")` |
| `DELETE /api/communications/{id:guid}` | filter (Write on the message) + handler visibility | `MessageDeactivate_ByAReadOnlyCaller_Is403_AndAWriterDeactivates` |
| `GET /api/office/communications/by-message-id/{internetMessageId}/suggestions` | handler decision: query runs as the caller (delegated `IDataverseUserClient` reads; the list is trimmed by `SuggestionCandidateAccess`) | `Sprk.Bff.Api.Tests.Api.Office.CommunicationsEndpointsContractTests.GetSuggestions_ACandidateTheCallerCannotRead_IsAbsentEverywhere_ReadableOnesKeepNamesAndFiling` |

All of these also pass `EveryRoute_ACallerWithNoRights_IsDenied_AndNothingDownstreamRuns` (one theory row per route,
through the real mappers) and the identity-first row `EveryRoute_ACallerWithNoOidClaim_IsStoppedByTheIdentityPreconditionFirst`.

## 10. Task 127 correction

Task 127's criterion "The suggestions response contains no candidate the caller cannot read — neither name nor id"
was not met by the code: `ResolveCandidateNamesAsync` dropped an unreadable candidate's NAME but the route returned
`Suggestions = suggestions` unfiltered (id, entity, confidence, provenance), and `Status` was computed with it. Fixed
here by moving that method into `Services/Communication/SuggestionCandidateAccess.cs` (it no longer exists in
`Api/Office/CommunicationsEndpoints.cs`) and removing the candidate from the decision. 127's POML is not reopened.

## 11. Placement (CLAUDE.md §10, `.claude/constraints/bff-extensions.md` §A)

In the BFF: these are existing BFF routes; the work is one endpoint filter composing existing decision seams, one moved
helper, one method on an existing read service, one optional parameter path on the existing engine, and catalogue
fixes. No new service registration, package, option, job, column or endpoint (one endpoint deleted). ADRs: ADR-001
(Minimal API), ADR-003 (fail closed), ADR-008 (endpoint filters), ADR-019 (ProblemDetails), ADR-024 (regarding family),
ADR-045 (one engine evaluate path), ADR-013 (no new CRUD→AI dependency: `IEmailTemplateService` was already injected;
`IDataverseUserClient` is general infrastructure since task 126). Publish-size measurement skipped on instruction
(no package change). CVE check: see §12.

## 12. Verification run

Run once at the end, from this worktree, with the machine under heavy load from other agents' suites (268
dotnet/testhost processes, CPU 85-100 %).

| Run | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/ -warnaserror` | succeeded, 0 warnings, 0 errors |
| Affected classes (before the full run) | `CommunicationRecordAuthorizationContractTests` 159/159; communication contract + integration 160 passed / 3 skipped; Office communications + delegated-read guard 14/14; template endpoint 10/10; `RegardingNameFieldsTests`, `CommunicationThreadReadServiceTests`, `AttachmentValidationTests`, `CommunicationServiceChannelRoutingTests` all green |
| Full BFF unit suite `tests/unit/Sprk.Bff.Api.Tests` | 14,244 passed / 54 skipped / **143 failed** in 1 h 8 m — every one of the 143 is `TaskCanceledException` / "The client aborted the request" after 2-5 minutes (the 100 s `HttpClient` timeout under contention), across 55 classes, none an assertion failure |
| Isolated re-run of those 55 classes | **560 passed / 8 skipped / 0 failed** (26 m) — contention, per the task's rule; both results reported |
| NetArchTest `tests/Spaarke.ArchTests` | 346/346 passed |
| `tests/integration/Sprk.Bff.Api.IntegrationTests` | 104/104 passed |
| `tests/integration/Spe.Integration.Tests` | 403 passed / 25 skipped / 0 failed |
| `dotnet list package --vulnerable --include-transitive` (BFF) | "no vulnerable packages"; no `.csproj` / `Directory.Packages.props` change in the diff |
| Publish size | not measured by this task — skipped on instruction (no package change). **Measured by the r1 adversarial verifier, per CLAUDE.md §10 hazards 3 and 4 — the PR cites these**: fresh worktrees at short paths `C:\wq161m` (base `6b243f092`) and `C:\wq161b` (branch `7f9159027`), `dotnet publish -c Release`, zipped with PowerShell `Compress-Archive` Optimal: base **45.657 MB**, branch **45.683 MB**, delta **+0.026 MB**, **212 files on each side**. Ceiling 60 MB. (r1 adds no package, project file or dependency — about 50 lines of filter code and description strings; not re-measured, on instruction.) |
| `OperationAccessPolicyCompletenessTests` | green in the full run (no key added) |

**r1 fix round — verification** (2026-10-03, from the isolated r1 worktree, machine again under heavy load: 271
dotnet/testhost processes, CPU 100 %):

| Run | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/ -warnaserror` | succeeded, 0 warnings, 0 errors |
| Affected classes first | `CommunicationRecordAuthorizationContractTests` + `CommunicationTemplateEndpointTests` 170/170; every `Communication`-filtered class 1303 passed / 8 skipped / 0 failed (the verifier's 1302 + the one new unit test) |
| Full BFF unit suite `tests/unit/Sprk.Bff.Api.Tests` | 14,385 passed / 54 skipped / **3 failed** of 14,442 in 34 m — all three `TaskCanceledException` / "The client aborted the request" after ~3 minutes (contention), in `RagSendToIndexIndexNameContractTests`, `InternalUserShareContractTests`, `DocumentProfileContractTests` — none touched by this task |
| Isolated re-run of those 3 classes | **19 passed / 0 failed** — contention, per the task's rule; both results reported |
| NetArchTest `tests/Spaarke.ArchTests` | 346/346 passed |
| `tests/integration/Sprk.Bff.Api.IntegrationTests` | 104/104 passed |
| `tests/integration/Spe.Integration.Tests` | 403 passed / 25 skipped / 0 failed |
| Seeding of the r1 guards | S37-S40 (§8) all bite |

The verifier's own runs on `7f9159027` (item 4, fresh short-path worktrees): build 0 warnings; communication-filtered
classes 1302 / 8 skipped / 0 failed; full BFF unit suite 14,387 passed / 54 skipped / 0 failed in 17 m; NetArchTest
346/346; `Sprk.Bff.Api.IntegrationTests` 104/104; `Spe.Integration.Tests` 403 / 25 skipped / 0 failed. This closes the
original run's 143 contention failures (criterion "build / suites / publish size", verifier item 16).

**r1 conflict check** (`/conflict-check`, 2026-10-03): 21 open PRs, none touches any file in the r1 diff; master since
`6b243f092` changed none of them (only `Api/Filters/TodoSourceAccessFilter.cs` in that folder);
`work/email-communication-intelligence-r2` / `-r3` show no change to `CommunicationEndpoints.cs` or
`CommunicationTemplateEndpoints.cs` since the base; `task/uac-r2-167-r1` edits `RouteAuthorizationGuardTests.cs`, which r1
does not touch.

**Conflict check** (`/conflict-check`, 2026-10-03): 18 open PRs, none touches any file in this diff; the active
peer on `CommunicationEndpoints.cs` (email-communication-intelligence-r2, and r3) is fully merged into master
(`merge-base == head` for both branches); master and the branch are identical for every target file. Parallel UAC tasks
(146, 167 and the other sweep tasks) edit `RouteAuthorizationGuardTests.cs` — this diff changes one reason string there.

**Step 9.5 — code-review** (coverage-first; nothing Critical):

| # | Finding | Severity / confidence | Disposition |
|---|---|---|---|
| 1 | Each gated request resolves the caller's systemuser 1-3 times (filter precondition, read service, template handler) — `ICallerSystemUserResolver` does an uncached app-only lookup each time | Warning / high | Accepted; latency only. A per-request memo (e.g. `HttpContext.Items`) is a follow-up across all communication routes, not this task |
| 2 | Denials pay `CallerRecordAccessProbe`'s ~1.6 s not-found backoff; attachment checks run sequentially (≤150 `AuthorizeAsync`, cached data source) | Warning / high | Accepted (documented in the class remarks and POML notes) |
| 3 | `CommunicationRecordAuthorizationFilter.cs` is 787 lines, ~87 branch points | Suggestion / medium | Cohesive single responsibility (authorize the communication routes; one method per route family, a dispatch switch) — legitimate per CLAUDE.md §11.5 |
| 4 | `Safely` turns any exception (including a programming bug) into a deny | Suggestion / high | Intended (ADR-003 fail closed); each is logged at Warning with the check name and route |
| 5 | The filter consumes `ICallerSystemUserResolver`, which lives under `Services/Ai/Context` | Suggestion / medium | Already injected by the same endpoint file's handlers and by `CommunicationThreadReadService`; not an AI-internal type in ADR-013's sense (only `IOpenAiClient` / `IPlaybookService` are guarded). Relocation (as task 126 did for `IDataverseUserClient`) is a candidate follow-up |
| 6 | Office route: a missing user context in the candidate helper becomes the route's generic 500 "Suggestions Failed" rather than a 403 | Suggestion / high | Non-2xx as required; the route's catch-all is task 127's contract, left unchanged |
| 7 | Message / app-owned-thread writes now need Write that, in dev, non-admins do not hold on app-owned rows | Warning / high (behaviour) | Required by the amendment (owner D1); recorded §4.3 for the owner; task 146 changes the ownership |
| 8 | Injection surfaces: every OData path built here uses an allow-listed entity set (`RegardingNameFields`) or a constant and a `Guid` | — | No finding |
| 9 | AI-smell scan: no single-implementation interface added, no log-and-rethrow, no null checks on non-nullable parameters, no restating comments found on review | — | Clean |

**Step 9.5 — adr-check**: compliant — ADR-001 (Minimal API; no Functions), ADR-002 (no plugin; checks in the BFF
request path), ADR-003 (fail closed on every seam; machine-readable reasonCodes; authorization before every
`SpeFileStore` download/upload; no decision cached), ADR-007 (no Graph type added outside Infrastructure), ADR-008
(endpoint filter in each route's chain), ADR-009 (no in-memory cross-request cache), ADR-010 (no new interface or DI
registration; the filter is constructed per request like `FinanceAuthorizationFilter`), ADR-019 (ProblemDetails),
ADR-024 (regarding family reused; no new map), ADR-028 (no credential constructed; OBO via the existing probe),
ADR-038 (no `Mock<HttpMessageHandler>`, no DI-registration or constructor null-check test; one B6 mirror test deleted
with its route), ADR-045 (one engine evaluate path; the admission callback is an optional filter on it), ADR-052 (no
background work). Warning (low confidence): ADR-013 — see code-review #5. No violation; no §6.5 path needed.

## 13. MANUAL LIVE GATE (dev — main session, the round-11 non-admin test user in "Spaarke Business Unit 1"; ids to 8 chars)

Prerequisite: deploy this branch's BFF to `spaarke-bff-dev` per `bff-deploy` (coordinate per bff-extensions §F.4). All
steps below are reads or user actions through the product; no schema change.

- (a) Compose an email with an attachment the user cannot open (a document under a secure matter the user is not shared
  on) → `POST /send` 403 `sdap.access.deny.communication.send`; no email arrives at the recipient.
- (b) Insert a template with a secure matter the user cannot open as the regarding → 403
  `sdap.access.deny.communication.template_render`. Then, on a record the user CAN open, insert a template whose field
  code names a field-secured column the user has no field permission for (e.g. `contact.sprk_externalobjectid`, task 141
  FLS) → that code renders empty. Insert a template with a date, money, option-set, lookup and two-option code and
  compare the text with production behaviour (expect identical; note any date-only TimeZoneIndependent column).
- (c) `POST /{id}/archive` and `POST /{id}/suggest-associations` on a communication the user cannot see → 404
  `COMMUNICATION_NOT_FOUND`; read-only check afterwards: no new `sprk_document` with `sprk_relatedcommunication` = that id.
  `GET /{id}/status` → 404 (route deleted).
- (d) Same user, records they CAN open: compose + send with an attachment; insert a template; Save to SharePoint (archive);
  create a record thread (name shown = the record's own name); confirm an association in the email workspace and read
  back `sprk_affinity` (read-only) — a row incremented for the confirmed target; from the reconcile tabs: apply,
  dismiss, undo, and create-task assigned to themselves AND to a colleague — all succeed.
  Also: pin a record thread the user does NOT own → expect 403 (behaviour change §4.3); pin one they own → 200;
  delete a message the user sent in a Direct thread (app-owned row) → expect 403 `MESSAGE_DELETE_FORBIDDEN` for a
  non-admin — record the result for the owner (§4.3).
- (e) `POST /accounts/{id}/verify` by the test user on an account they do not own → 404 and `sprk_verificationstatus`
  unchanged (read before/after); by an administrator (owner of the account / System Administrator) → 200.
- (f) Office add-in on an email whose engine suggestion is a matter the user cannot open → that matter is absent from the
  suggestions (no card, no id in the network response) and the status shown is not "Ambiguous" on its account.
- (g) Read-only, record in this note: the test user's effective `prvCreatesprk_Event`, `prvCreatesprk_Document`,
  `prvAssignsprk_Event`, `prvWritesprk_CommunicationAccount`, `prvAppendTocontact` (via `RetrieveUserPrivileges` or
  the role read used in §6). If Assign is missing, trigger 4 fires — stop and ask the owner (land 146 first, recommended).

Results: _pending — main session_.

## 14. `.claude/**` edits needed (main session)

None required for correctness. Optional pointer for `.claude/patterns/api/endpoint-filters.md` (add one line under the
filter list): "`CommunicationRecordAuthorizationFilter` — per-record gate for `/api/communications` routes; one
`CommunicationRecordRoute` value per route fixes the id source, the right and the deny answer (task 161)."

## 15. Adversarial verifier round r1 (`task/uac-r2-161-r1`, on `7f9159027`)

Every item closed or confirmed. Items 14 and 15 are the same defect as item 8; item 16 is items 4 and 5.

| # | Verifier finding | Disposition |
|---|---|---|
| 1 | Scope and evidence: one commit, all eleven sweep rows closed (row 79 by deletion), the Office fold-in and the amendment routes | Confirmed; no change. |
| 2 | Deletion check for `GET /{id}/status`: no caller, not published; `endpoints-smoke.json` still lists it | Confirmed. The smoke list is another project's frozen baseline artefact, not a consumer; recorded in §5, not edited. |
| 3 | 31 checks re-seeded independently; every one bites | Confirmed. r1 adds S37-S40 (§8). |
| 4 | Suites green on fresh worktrees | Recorded in §12 next to r1's own run. |
| 5 | Publish size measured by the verifier: 45.657 → 45.683 MB, +0.026 MB, 212 files each, Compress-Archive | Recorded in §12 for the PR. Not re-measured (instruction); r1 adds no package or dependency. |
| 6 | Live metadata: set and name values, all twelve regarding lookups on `sprk_communication` | Confirmed; no change. |
| 7 | No fail-open path; byte-identical denials; no `CallerId` assignment; no edits to the protected files | Confirmed; r1 keeps all of it (no edit to `CommunicationService.cs`, `Infrastructure/ExternalAccess`, `Api/ExternalAccess`, `OperationAccessPolicy`, `EntityAccessFilter`, `SemanticSearchAuthorizationFilter`). |
| 8 / 14 / 15 | The regarding-type 400 on `POST /template/render` and `POST /threads` came only AFTER the caller precondition's app-only systemuser lookup; the tests' mocked resolver hid it | **Fixed.** `Gate.CheckRequestShape()` runs before `CheckCallerPreconditionsAsync()` (both helpers `RecordThreadTarget` / `TemplateRegardingTarget` are reused by the authorize methods, so the rule lives once). Both `…Is400BeforeAnyDataverseCall` tests now assert `AssertNoDataverseCallAtAll` — `_host.Callers` (the `ICallerSystemUserResolver`) has no invocation, nor the identity resolver, probe, impersonated / delegated reads, document data source, app-only service or impersonated write. Seed S37 bites both. Behaviour note §4.10. |
| 9 | `TemplateNotFound`'s summary claimed the handler used it; the handler built its own body | **Fixed in code, not just the comment.** The handler's not-found branch returns `TemplateNotFound(request.TemplateId)`; the summary now says exactly that. New unit test (§7) proves the handler's 404 equals the filter's deny body even when the service rewords its error; seed S38 bites. Behaviour note §4.11. |
| 10 | Stale route descriptions ("Caller resolved server-side (403 fail-closed)"; "403 if the caller cannot see the thread") | **Fixed.** create-task, proposals apply / dismiss / create-task/apply / undo and tasks undo now state that an unknown or invisible id, a missing bearer token and an unresolved caller get the same 404 (`COMMUNICATION_NOT_FOUND` / `PROPOSAL_NOT_FOUND`), plus the AppendTo / Create / Assign rights where they apply; rename / pin / thread delete / message delete state the Write requirement. The matching route comments are corrected, and `.Produces(404)` is added to create-task and tasks undo (the BFF publishes no OpenAPI; metadata only). |
| 11 | The authorized-path tests for apply / undo / tasks-undo asserted only "not the not-found answer" | **Fixed.** Replaced by `ProposalApplyAndUndo_AuthorizedCaller_WriteTheTargetAsTheCallerAsToday` and `TaskUndo_AuthorizedCaller_CancelsTheTaskAsTheCallerAsToday`: 200, the write made **as the caller** with the right value, one audit row. The host substitutes `IFieldMappingDataverseService` (module boundary, ADR-038-compliant) so the paths complete in-process. Seeds S39 / S40 show each catches a filter that wrongly refuses an authorized caller with a non-404 — which the replaced tests would have passed. |
| 12 | Write now required for thread / message writes; in dev, app-owned rows mean non-admins lose message delete and pin / rename / delete on app-owned threads until task 146 re-owns them | Not a code defect; the amendment forbids weakening it (§4.3). **Open for the owner — deployment sequencing**: (a) RECOMMENDED — deploy 161 to a shared environment together with or after task 146, so app-owned messages and threads are re-owned before Write is enforced; (b) deploy 161 first and accept the interim loss for non-admins. No code change either way. |
| 13 | ADR-045: the admission callback is an acceptable reading of "not a fork" | Confirmed; no change. |
| 16 | Build / suites / publish-size criterion not met by the task as first reported | Closed by the verifier's runs and r1's run (§12); the PR must carry the publish-size numbers in §12. |
| 17 | MANUAL LIVE GATE (a)-(g) still open | **Not closed — main session.** §13 is unchanged; r1 adds no live step. |
