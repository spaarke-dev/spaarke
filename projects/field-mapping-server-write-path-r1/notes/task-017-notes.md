# Task 017 — re-point the route, chat and decision callers

Phase 1 (the invoice-family pre-write rule reconcile) is commit f758480f3c. Phases 2b, 2c and 2d-1 to 2d-5 follow, in
order. 2c and 2d-2 to 2d-4 were written as separate notes files on their own branches and are folded in here (2d-5);
their text is kept, with headings one level down.

## Deviations for the PR

- **Chat and `email.draft` each keep one WhoAmI.** Chat keeps `WithForPersonAsync` (one WhoAmI plus the linked
  contact) for the "for" person (2d-3); `email.draft` keeps the WhoAmI for its pre-derivation AppendTo check, which is
  also the sender (2d-4). Both deviate from "adapters no longer resolve WhoAmI": the service leaves the "for" person to
  the adapters (step 9).
- **The route's faulted check is now 503.** A check that faulted (`record_creation.check_failed` /
  `owner_unavailable`) was an unhandled 500 on `POST /api/v1/child-records/{table}`; it is 503 through the shared map
  (2b, NFR-01).
- **Two new codes:** `record_creation.stamp_underivable` (400, an I-1 derivation data condition) and
  `record_creation.stamp_unavailable` (503, a derivation fault) — step 10 before the create (2d-1).
- **An unregistered Owned-path chat table is refused.** A chat create of e.g. `phonecall` was created by the old core;
  it is now `table_unsupported` (`VALIDATION_FAILED`), with no create (2d-2 option b, 2d-3). Only `task`, `contact` and
  `account` were registered.
- **`sprk_matternumber` from chat is refused** as `BlockedColumn` (`AccessDenied`), owner decision 2c; it was written
  before (2d-3). The tool description no longer offers it.

## Phase 2b

Branch `task/fm-017b`, created from `task/fm-017a`.

### Item 1 — the child-records route's create runs through `RecordCreationService`

- `ChildRecordEndpoints.CreateAsync` is now a thin adapter. It keeps its route filters, `RequireAuthorization`,
  `CreateTables` and the early 400 for an unsupported table. It then calls the scoped service with
  `RecordCreationPrincipal.ForCaller(user, oid)`, a `WebApi` payload and `CorrelationId = TraceIdentifier`.
  Reference: the Office adapter.
- Everything the service does after the create is no longer done by the route; the route does no step twice:
  - `CompleteIsolatedCreateAsync`;
  - the "Assigned *" grants;
  - the I-1 restamp and I-16;
  - the sharee mirror;
  - the membership event.

  Removed from the route: `CompleteRootCreateAsync`, `RootCreateCompletion`, `MirrorAsync`, the route's own WhoAmI and
  the `RootServerOwnedColumns` view.
- **No field mapping on the route** (`Mapping = null`). The browser writers still map on the client until FR-15/FR-16
  (tasks 020-029).
- **NFR-07** is met by one tested mapping at the route edge, `ChildRecordEndpoints.RefusalResult`, which uses the
  route's own builders. See the table below.
- **K-4, "upstream 401 now renders 403" — decision: keep the route's behaviour.**
  - A question asked as the caller that failed at Dataverse renders through `CallerFailure`, with the caller's own
    status, code and message. So a 401 stays 401 here.
  - The shared map's 401 → 403 applies only to other adapters.
  - Pinned by `ChildRecordCreateRefusalContractTests.AQuestionAskedAsTheCallerThatFailed_IsTheCallersOwnStatusCodeAndMessage`
    (401, 403, 429, 503).
- **CR-3 (traceId vs correlationId) — decision: keep `traceId`; add no `correlationId`.**
  - Client grep (`src/client`, `src/solutions`): the child-records callers read the error through
    `bffChildWriteAdapter.ts:157-176`. That reads `reasonCode` (falling back to `errorCode`) and `detail`; no
    child-records caller reads `traceId` or `correlationId`.
  - The callers are: `bffChildWriteAdapter`, `BffChildRecordDataverseClient`, RegardingResolver `bffWrites`,
    EventDetailSidePane, Notepad, SmartTodo, the Create*Wizard services and the CreateTodoWizard page.
  - The `correlationId` readers that do exist are on other routes: `BffDataverseClient.ts:270`,
    `communicationApi.ts:266`, the thread-list and timeline APIs, `decisionWizardService.ts:55`, `describeSendFailure.ts`
    and Office `errorMessages.ts:519`.
  - So `reasonCode` is the load-bearing field, and it is preserved for every existing refusal.
- **K-4, membership — the route never publishes twice.** Before 017 the route published nothing. Through the service it
  publishes once, for `sprk_event` and `sprk_document` (the matter is not on the route).
  - The other publishers are on separate create paths and are not re-pointed here: `EventEndpoints.cs:772`
    (POST /api/v1/events) and `DataverseDocumentsEndpoints.cs:963` (POST documents). The Office publisher was removed
    in 018.
  - Pinned by `ARouteCreateOfAMembershipTable_PublishesItsOwnerEvent_ExactlyOnce` (event and document), with a
    must-not-fire control for `sprk_todo`.
- **New behaviour on the route, not a regression (recorded per NFR-01 and FR-10):**
  - **503 for a faulted check.** A check that faulted (`record_creation.check_failed` / `owner_unavailable`) is now
    503 through the shared map. Before, it was an unhandled 500.
  - **Document search index.** A document created through the route now gets its business unit's search-index default
    (FR-10).
  - **Budget number warning.** A budget gets the number read-back warning when the number is missing.
  - **#1608 refusals.** Matter and project payloads now get the #1608 refusals (item 5). Neither table is on the
    route's accept-list.

#### NFR-07 table — `POST /api/v1/child-records/{table}`, before (f758480f3c) vs after

| Refusal | Before: status / reasonCode | After: status / reasonCode | Body |
|---|---|---|---|
| table not in `CreateTables` | 400 `child_record.table_unsupported` | 400, same | unchanged (still at the route) |
| caller has no Dataverse identity | 403 `sdap.access.deny.caller_unresolved` (CallerUnresolvedReasonCode) | 403, same | same words |
| WhoAmI / map / AppendTo failed at Dataverse | the caller's own status / ErrorCode (`CallerFailure`) | same | same (`CallerFailure`) |
| payload not accepted (mapper, pair) | 400 `child_record.invalid_payload` | 400, same | the validation text |
| root server-owned column (WA) | 403 `child_record.denied` | 403, same | same words |
| common server-owned or creator column | 403 `child_record.denied` (G5 core's words) | 403, same | same words |
| no Create / Append / field-security right | 403 `child_record.denied` | 403, same | the check's words |
| record named missing or not AppendTo-able | 404 `child_record.not_found` | 404, same (not `record_creation.not_found`) | byte-identical |
| invoice-family: record not found | 404 `child_record.not_found` | 404, same | byte-identical |
| invoice-family: read_failed / unknown_entity | 500, guard code | 500, same | "The {noun} was not saved: …", byte-identical |
| invoice-family: any other guard code | 400, guard code | 400, same | byte-identical |
| plan: AccessDenied | 404 `child_record.not_found` | 404, same | byte-identical |
| plan: creator walled off / secure flag / access record | 403, plan code | 403, same | byte-identical |
| plan: unverifiable / secure team unresolved | 500, plan code | 500, same | byte-identical |
| plan: anything else | 409, plan code + traceId | 409, same | byte-identical |
| owner refused (resolver) | 409, owner code + traceId | 409, same | byte-identical |
| owner forbidden (secure removal) | the decision's own problem | same | `RecordOwnerRefused(resolution)` |
| secure filing refused (non-reparentable child) | 409 `NoOwnerSource` + traceId | 409, same | byte-identical |
| isolated root removed again | 500, reason code | 500, same | same words |
| no decision | 500 `child_record.no_decision` | 500, same | same words |
| check faulted / owner unavailable | unhandled 500 | **503** `record_creation.*` (shared map) | new (NFR-01) |
| autonumber conflict | n/a (no retry before) | shared map | new |

No existing status or reasonCode changed, so the escalation trigger did not fire.

Pinned by:
- `tests/integration/contract/Api/Records/ChildRecordCreateRefusalContractTests.cs`, a pure mapping test. Its
  `InvoiceFamilyRefused`, `PlanRefused` and `RecordOwnerRefused` rows are byte-equivalence tests against the route's
  surviving builders.
- The route suites (`SecureChildOwnershipAiToolTests.ChildRecordRoutes`, `RegardingPair` and `WorkAssignment*`), which
  drive the real service.

### Item 2 — the decision follow-ons

- `DecisionRouteCores.CreateChildAsync` calls `ChildRecordEndpoints.CreateAsync`, so it now reaches the service. The
  only edit to it is the forced one: it resolves `RecordCreationService` from the request's services in place of the
  six removed collaborators.
- Parity is pinned in `tests/integration/data-mutation/RecordOwnership/RouteDecisionCreateParityTests.cs`:
  - `ATodo_ThroughTheRoute_AndThroughADecisionsAddTodo_…`;
  - `AWorkAssignment_ThroughTheRoute_AndThroughADecisionsAssignWork_…`.

  Both assert the same owner, creator person and I-1 stamp (`sprk_RegardingMatter` / `sprk_RegardingProject` binds).

### Item 3 — 014 R3-1: `ServerSetLookupColumns` is normalised once

- `RecordCreationRequest.ServerSetLookupColumns` normalises in its `init` accessor: each name is trimmed and
  lower-cased, blanks are dropped, and the set compares with `OrdinalIgnoreCase`. A set given through `with` is
  normalised the same way.
- Every reader now reads that one set, and the local `.Trim()` copies are removed:
  - the K-2 guard, `ServerSetFilingColumn`;
  - the F-B guard, `ServerSetRootFilingLookups`;
  - `ProtectedFromMapping`;
  - `DropUnusableReferencesAsync`;
  - step 5's AppendTo skip (`CallerWriteAuthorization`, `CallerMayFileUnder`), which compared the column with the
    adapter's set exactly as given.
- **The bug this fixed:** before, `" SPRK_AssignedTo "` in an ordinal set was protected from mapping (that reader
  trimmed), but step 5 asked AppendTo on it and refused the create.
- Tests (both fail before the change): `Request_ServerSetLookupColumns_AreTrimmedLowerCasedAndCaseInsensitive_…` and
  `CreateAsync_APaddedMixedCaseServerSetAssignedTo_InAnOrdinalSet_CostsNoAppendTo_AndIsProtectedFromMapping`, in
  `RecordCreationServiceMappingSeamTests.Task017.cs`.
- **R3-2 (move step 6's AppendTo before step 3): not done.** It does not fall out of R3-1. The step-6 ask needs the
  secure-root plan's filing columns of the merged row, which only exist after step 3, so moving it is a reorder of the
  pipeline. Left for 017d / owner decision 2a's step-10 work.

### Item 4 — 037 K2: the copied default runs after the post-mapping defaults merge

- **Decision: after** (the recommendation). In `WithCopiedDefaults`, when the row leaves the source
  (`sprk_assignedtointernal`) unset but an adapter's post-mapping default fills it with a non-empty `EntityReference`,
  the target (`sprk_assignedto`) gets a copy of that reference.
- **Server-set only for a genuinely server-set source.** A post-mapping default may never name a server-set column
  (`PostMappingDefaultValues` refuses it). So such a copy is not added to the copies that widen the request's server-set
  set, and step 5 checks it as the caller's own.
- The route passes no defaults, so this changes nothing on the route today. It applies to Office and chat.
- Tests (both fail before the change):
  - `CopiedDefault_ASourceOnlyAnAdapterDefaultFills_IsCopiedToo`;
  - `CopiedDefault_ACopyOfAnAdapterDefault_IsNeverServerSet_ItIsCheckedAndDroppedWithItsSource`: with no AppendTo on
    the contact, both are dropped with `default_value_dropped`.
- The `RecordTableCopiedDefaultGuardTests` ArchTest still holds; the change stays in `Mapping.cs`.

### Item 5 — #1608 / decision 2b: root server-owned columns refused on every path

- `RecordTableRules.RootServerOwnedColumns` is the ONE set of root server-owned columns: `sprk_containerid`,
  `sprk_securitybu` and `sprk_accesspermission`. It is the `TableServerOwnedColumns` of `sprk_matter`, `sprk_project`
  and `sprk_workassignment`.
- Step 2 refuses these columns for every caller path: the route, chat, Office and the decision cores.
- `sprk_issecure` was NOT added to the work assignment: it would change the route's existing reasonCode for that column.
  The secure-create plan answers it today.
- Tests:
  - `CreateAsync_ARootsServerOwnedColumnInTheCallersPayload_IsRefusedAtStep2_OnEveryRoot`, nine rows (three tables ×
    three columns). The matter and project rows fail before the change.
  - `TableServerOwnedColumns_EverySecureRoot_RefusesTheSameThreeColumns` (domain).
  - `WhenTheServiceRefuses_TheRouteCreatesNothing_AndFallsBackToNothing`: the route's work assignment gets 403
    `child_record.denied`.

### Item 6 — a document's access record and Access Permission

- **`sprk_accessinheritance` is refused for every principal.** It is now the document entry's
  `TableServerOwnedColumns`, so a caller is refused at step 2.
  - A system principal skips the caller's column policy (FR-07), but it is now refused the table's
    `TableServerOwnedColumns`. It is also refused whatever `InheritedAccessPermission.RefuseAppOnlyWrite` refuses with
    `AppOnlyDocumentWriteReasonCode`: the Access Permission, its record and the submission marker.
  - The step trace is unchanged ("Skipped", "system principal (FR-07)") because an existing pipeline test pins it.
- **`sprk_accesspermission` is allowed for a caller (UAC-r2 D7) and refused for a system principal.**
- Tests:
  - `CreateAsync_ACallersDocumentNamingItsAccessInheritance_IsRefused`: fails before the change.
  - `CreateAsync_ACallersDocumentNamingItsAccessPermission_IsCreated_WithIt`: must not fire.
  - `CreateAsync_ASystemDocumentNamingItsAccessRecordOrPermission_IsRefused_BeforeAnythingIsAsked`, for
    `accessinheritance`, `accesspermission` and `restrictedbysubmission`: all three fail before the change.
  - `CreateAsync_ASystemDocumentNamingNoAccessColumn_PassesStep2`: must not fire.
  - `TableServerOwnedColumns_ADocument_RefusesItsAccessInheritance_NotItsAccessPermission` (domain).
- **Effect to note.** A future system-principal document writer that sets the submission marker would be refused. The
  invoice submission writes it through its own typed writer, not this service. No system principal creates a document
  through the service today.

### Item 7 — tests

- `RouteDecisionCreateParityTests.cs`
  - It is a partial of `SecureChildOwnershipAiToolTests`, so the route and the decision cores run over one harness.
    Deviation: the file name differs from the class name.
  - Contents: the parity tests (item 2); the negative tests for no AppendTo (a byte-identical uniform 404 on the route,
    Refused on the decision), a service refusal and a service fault (nothing created, no caller-side create); the
    membership publishes once; item 9.
- `tests/integration/contract/Api/Records/ChildRecordCreateRefusalContractTests.cs` holds the NFR-07 pins. No route
  contract test existed before.
- `RecordCreationServiceMappingSeamTests.Task017.cs` holds items 3, 4, 5, 6 and 8.
- Fixture additions (additive, no assertion weakened):
  - the shared `ScriptedUserClient` gained the entity sets and lookups of `sprk_invoicesubmission` (the regarding trio)
    and of `sprk_servicerequest`;
  - `RecordTableRulesTests.MappingProtectedColumns_AreDeclaredOnlyOnTheTablesThatNeedThem` now lists the four new
    invoice-family tables, a deliberate registry change.

### Item 8 — coordinator: invoice-family core lookups are mapping-protected on all six tables

- `MappingProtectedColumns = InvoiceFamilyCoreLookupsOf(table)` is derived from
  `InvoiceFamilyCoreRecordGuard.CoreRecordLookups[table]`, on all six family tables. The hand-written set on
  `sprk_invoice` and `sprk_billingevent` is replaced; their derived set is the same set.
- Tests:
  - `MappingProtectedColumns_EveryInvoiceFamilyTable_CoversEveryCoreLookupItsGuardReads`: superset check plus the
    rule's registration. The four new tables fail before the change.
  - Must-fire control: `MappingProtectedColumns_TheCoreLookupCheck_FiresOnASetMissingOne`.
  - Seam test: `CreateAsync_AnInvoiceSubmission_NeverMapsASecondCoreLookup_TheRuleIsSkipped_AndTheRowIsCreated`. The
    second core lookup is skipped with `mapping_rule_skipped`, and the create proceeds rather than being refused as
    `invoice_family_multiple_core_records`.
- **Finding.** On the four regarding-trio tables, the trio is ALSO an app-only access column
  (`sdap.access.access_column_server_only`). `DropAppOnlyRefusedRules` therefore already dropped that rule before this
  change, so the seam test passes before and after. The registry change makes the protection declared instead of
  incidental; the domain test is the one that fails before.

### Item 9 — coordinator: `sprk_servicerequest` registered (from 017c's escalation)

- A CHILD entry with no declarations beyond the defaults. That is what `OwnedChildWrite.CreateAsync` applies to it today:
  - the common server-owned and creator columns are refused;
  - the owner is the resolver's (its regarding matter's business-unit team);
  - it is refused under a secure owner (it is not a reparentable child; the service's step 7 has the same rule);
  - its own `sprk_regardingmatter` is its I-1 stamp.

  `BudgetInquiryExecutor` is not edited; 017c re-points it.
- The registry ArchTest (`RecordTableRulesRegistryGuardTests.CreateTables`) now includes `BudgetInquiryExecutor.Table`.
- Tests:
  - `AServiceRequestUnderAMatter_ThroughTheService_GetsTheOwnerCreatorAndStampTheOwnedCreateGives`;
  - `AServiceRequestUnderASecureMatter_IsRefusedOnBothPaths_AndNothingIsCreated`.

  Both fail with the entry removed (`table_unsupported`).

### Deviations

- `RouteDecisionCreateParityTests.cs` is a partial of `SecureChildOwnershipAiToolTests`; the reason is under item 7.
- System-principal step 2 records "Skipped" even though it now refuses two narrow sets; an existing pin is kept.
- R3-2 was not done (see item 3).

## Phase 2c — chat create / email.draft / budget inquiry re-point (stopped at escalation)

Branch `task/fm-017c` from `origin/task/fm-017a` (f758480f3c, phase 1). Rigor FULL (task 017).

### Outcome

**Re-point: STOPPED at the POML escalation triggers, nothing re-pointed.** Two of the three callers create tables that
are not in `RecordTableRules`, and the chat tool's existing refusal codes differ from the shared map's. Details below.
The one change made is the ebilling-r1 review item (server-written invoice tables refused from chat), which does not
depend on the re-point.

### ebilling-r1 review of f758480f3c, 2026-10-30

After phase 1, `sprk_invoicesubmission`, `sprk_invoiceapproval` and `sprk_invoicecorrection` have registry entries, so
the service would accept a chat create of them. ebilling's services read those rows as the record of a decision or a
correction; they are server-written. The chat `dataverse.create_record` handler now refuses them outright with the
`sprk_document` block's mechanism and shape:

- `DataverseCreateRecordHandler.ServerWrittenInvoiceFamilyTables` (the three tables) and
  `ServerWrittenInvoiceFamilyCreateBlockedMessage(table)`;
- checked in `ValidateChat` (pre-suspend: no confirm dialog) and again in `ExecuteChatAsync` (defense in depth), both
  BEFORE any Dataverse call; tool error code `VALIDATION_FAILED`, as for `sprk_document`. No affordance link (there is
  no wizard to point at; the text says to act from the invoice itself).
- `RecordTableRules` untouched. The tool description (byte-mirrored in
  `infra/dataverse/sprk_analysistool-dataverse-create-record-row.json`) is unchanged; the refusal text tells the model
  not to retry.
- Not included: `sprk_externalidentifier` (also given an entry in phase 1). Not named in the review item; ebilling-r1 to
  say whether it is server-written too.

ArchTests: the task-098 source scan (`ExternalInvoiceVisibilityGuardTests`) found the new static set, which names three
invoice-family tables, as a new surface. It reads no row, so it is listed in `NotInvoiceSurfaces` with its reason
(as `EmailDraftToolHandler::.cctor` is) rather than given the (a') predicate.

Tests: `tests/integration/data-mutation/RecordOwnership/ChatServerWrittenTableBlockTests.cs` (7): one refusal per table
through `ExecuteChatAsync` (no app-only create, no POST, no read of the caller's Dataverse), one per table through
`ValidateChat`, and a must-not-fire (`sprk_todo` under an ordinary matter is still created by the app). Must-fire: with
the block disabled, the 6 refusal tests fail and the must-not-fire passes; restored, 7/7 pass.

### Escalation 1 — chat creates tables that are not in RecordTableRules (POML trigger 3)

`OwnedChildWrite.PathFor` sends EVERY `UserOwned`/`TeamOwned` table through `OwnedChildWrite.CreateAsync` except
per-user tables and unfiled communications/threads. The service refuses any table without a registry entry
(`record_creation.table_unsupported`, `RecordCreationService.cs` ~:163). Tables chat creates today on the owned path
that are NOT in the registry:

- `task` (activity) — pinned by `SecureChildOwnershipAiToolTests.cs`
  `CreateRecord_ATableOutsideTheOwnershipSetFiledToAnOrdinaryMatter_IsCreatedByTheApp_OwnedByThatMattersTeam` (must pass
  unchanged) and `..._FiledToASecureMatter_IsRefused` (expects `VALIDATION_FAILED`, would become `table_unsupported`);
- `contact`, `account` — the tool description advertises them ("people = contact, companies = account");
- every other user/team-owned table (activities, `sprk_servicerequest`, …): the tool takes any logical name.

The POML's negative criterion ("a chat create of a table not in RecordTableRules is refused") and the must-pass-unchanged
test above contradict each other. Options for the owner/coordinator:
(a) refuse non-registry tables from chat (criterion as written): the `task` test changes (an assertion is reversed, which
the brief forbids without approval) and chat can no longer create contacts, accounts or activities — a product change;
(b) add the tables chat must create to the registry (012 recipe; 017b owns RecordTableRules) — needs the list decided
(at least `task`, `contact`, `account`);
(c) keep non-registry owned-path tables on `OwnedChildWrite.CreateAsync` for now — a second create core, which FR-13 and
"no fallback" exclude, and it blocks deleting the create half.

### Escalation 2 — budget inquiry creates `sprk_servicerequest`, not in the registry

`BudgetInquiryExecutor.Table = "sprk_servicerequest"` (`BudgetInquiryExecutor.cs:145`). Re-pointed as is, every budget
inquiry would be refused `record_creation.table_unsupported`. Needs a registry entry (017b; child, filed under
`sprk_regardingmatter`; `sprk_regardingorganization` lookup; ADR-024 typed pair) before 017c can re-point it.

### Escalation 3 — chat tool error codes vs the shared map (ADR-019 / NFR-07 trigger)

The brief says the chat handlers take code and text from the shared map, and to stop if an existing refusal's code
changes. They would change. Today's chat codes (owned path) and what the service returns:

| Today (chat tool `ErrorCode`) | Service refusal |
|---|---|
| `AccessDenied` (caller lacks Create / AppendTo / field security), e.g. `SecureChildOwnershipAiToolTests.cs:148,159,198,407`, `RegardingPair.cs:389,490,513,557` | `record_creation.denied` (403) / `record_creation.not_found` (404) |
| `VALIDATION_FAILED` (Invalid, secure filing refused) `:443` | `record_creation.invalid_payload` / `record_creation.blocked_column` / OwnerUnresolved `NoOwnerSource` |
| `INTERNAL_ERROR` (owner resolver faulted) `:469` | `record_creation.owner_unavailable` (503) |
| resolver / plan / invoice-guard codes (e.g. `SecureParentNotIsolated`, `ParentUndetermined`, guard codes) | same codes (carried) |
| WhoAmI / Dataverse client failure: the failure's own code | `PrincipalUnresolved` / `Upstream`, failure carried in `ClientFailure` |

The Office precedent (task 018, `OfficeService.cs` ~:2726-2810) maps refusal KIND to its own existing codes in the adapter
and takes only the text from the service. The same adapter for chat keeps every code above; it needs the coordinator to
confirm that "code from the shared map" means "kind from the service, existing tool code by kind" for chat.

### Owner decision 2c (sprk_matternumber / sprk_projectnumber from chat)

The service already refuses them: step 2, `IsNumberColumn(rule, c)` on the registry's `AutonumberColumn`
(`RecordCreationService.cs` ~:382) → BlockedColumn 403 `record_creation.blocked_column`. **No registry change needed.**
It reaches chat only once chat creates through the service, so the description change ("stop offering the field") is
deferred to land in that same change, per the decision. Today the description says `sprk_matternumber (text)`; the edit is
in `infra/dataverse/sprk_analysistool-dataverse-create-record-row.json` and the mirrored literal at
`DataverseCreateRecordHandler.cs` `Metadata.Description`.

### DI (for when the re-point resumes)

Tool handlers are scoped (`AddToolHandlersFromAssembly`, `IToolHandlerRegistry` scoped); `BudgetInquiryExecutor` is
scoped (`CommunicationModule.cs:134`); `RecordCreationService` is scoped and registered unconditionally in
`OfficeModule.cs:74` (`Program.cs:93`). No captive dependency. A host that composes `AddToolFramework` without the Office
module would not resolve a handler that takes the service (§10 F.1); production composes both.

### email.draft

Re-pointable in principle (`sprk_communication` has an entry; only a FILED draft takes the owned path). Not done alone:
it needs the same chat refusal adapter as escalation 3, and it pre-stamps I-1/I-16 into the payload
(`DeriveForHostAsync`) while the service's step 10 is still post-create only (owner decision 2a not yet implemented in
the service). Keeping its own pre-derivation and passing the item through the service is the behaviour-preserving way.

## Phase 2d-1

Branch `task/fm-017-d1`, created from `task/fm-017` (14b801a53f). Owner decision 2a (2026-10-22): FR-06 step 10 in spec order.

### What changed

- **Step 10 runs before the create (step 11).** `RecordCreationService.StampBeforeCreateAsync`:
  - builds the merged row in memory (every lookup the item binds, plus the ADR-024 pair id);
  - asks task 016's planner, `CoreAncestorRestamper.PlanMissingStampAsync`, for the I-1 stamp columns the row is missing
    (fill-only; no new deriver);
  - binds them into the create body under the table's navigation property, mapped as the caller through
    `DataverseWriteItemMapper` (the metadata read steps 1 and 4 already make).
  - Only the create body (`fields`) changes. The item that the owner (step 7), the plan (step 6) and step 12 read is
    unchanged, so no owner and no post-create input changes.
- **An I-1 derivation failure refuses the create; nothing is written.** Two new codes, through the shared map:

  | Failure | Kind | Status | Code |
  |---|---|---|---|
  | A data condition (`CoreAncestorResult.IsPermanent`: `MultipleRoots`, `NotFound`) | `Invalid` | 400 | `record_creation.stamp_underivable` |
  | A fault (a read or metadata failure, or a derived stamp that could not be bound) | `Unavailable` | 503 | `record_creation.stamp_unavailable` |

  - The planner now throws `CoreAncestorStampUnderivableException`, which derives from `InvalidOperationException`, so
    Repair's catch is unchanged. It carries the derivation's `IsPermanent` and `RefusalKind`.
  - No existing code or status changed.
- **I-16 is set before the create when it resolves; it never refuses.** `CoreAncestorRestamper.ResolveInheritedAccessPermissionAsync`
  is a one-line pass-through to the resolver's `ResolveInheritedAccessPermissionAsync` (the "never throws … display only"
  contract is untouched). A null is simply not set.
- **The post-create re-derivation stays.** `AfterWriteAsync`, then I-16, inside step 12, in the same place relative to the
  other post-create steps (number read-back, isolated completion, Assigned grants, I-1, I-16, sharee mirror, membership).
- **Documents** (`InheritedAccessPermission.RecordsOwnNarrowing`): step 10 is `Skipped`; post-create only (#2169).
- **System principal:** the same derivation and the same refusals.
  - **Deviation, needs a decision.** A system principal has no caller to read the table's navigation metadata as, so its
    derived stamp is not bound into the body. The post-create re-derivation writes it (step 12, unchanged).
  - Binding it before the create needs either an app-only metadata read of `ManyToOneRelationships` (a new read pattern with
    no caller identity) or a static navigation map. That map is verified only for To Do and Event; the memo, communication,
    analysis, Signal and Decision Record navigation properties are not in the code.
  - No system-principal caller exists today (decision 2d), so nothing ships with this gap.
- **Step record:** step 10 is `Ran` (a stamped or I-16 table) or `Skipped` (roots, documents, other tables). It is no longer
  `Deferred`.

### Route (`POST /api/v1/child-records/{table}`)

`ChildRecordEndpoints.RefusalResult` is not edited. Both new codes fall through to the shared map, unchanged:

| Refusal | Status | `errorCode` / `reasonCode` | Detail |
|---|---|---|---|
| `stamp_underivable` | 400 | `record_creation.stamp_underivable` | the service's words ("The event was not saved: the core record it belongs to cannot be determined …") |
| `stamp_unavailable` | 503 | `record_creation.stamp_unavailable` | the map's fixed words ("The event could not be saved right now, so nothing was created. Try again in a few minutes.") |

Every row of 017b's NFR-07 table is unchanged: `ChildRecordCreateRefusalContractTests` passes as before.

### Tests (KEEP paths)

- `tests/integration/seam/RecordCreation/RecordCreationServicePipelineSeamTests.PreCreateStamp.cs`:
  - the stamp is in the create payload, and the post-create re-derivation still reads the created row: an event under an
    event;
  - a root under a parent (a work assignment under a matter) gets no derived stamp, and step 10 is `Skipped` (roots carry no
    I-1 stamp columns);
  - 400 `stamp_underivable` (`NotFound`) and 503 `stamp_unavailable` (a timeout), each with no create attempt;
  - a system principal is refused the same way;
  - a system principal's event is stamped by the post-create re-derivation;
  - I-16 is in the payload under a Restricted matter, and an unresolvable I-16 lets the create proceed without it;
  - a document under a Restricted matter gets neither, and step 10 is `Skipped`.
- `RecordCreationServicePipelineSeamTests.cs`: the two step-order pins changed `Deferred` → `Ran`. The ordered step list
  still pins step 10 between step 9 and the create.
- `tests/integration/data-mutation/RecordOwnership/RouteCreatePreCreateStampTests.cs` (a partial of the route harness): the
  route's create payload carries the stamp; the route's 400 and 503 bodies for the two new codes; nothing created and no
  caller-side fallback.
- Fixture additions (additive):
  - the scripted caller's `sprk_event` metadata gains `sprk_regardingevent` / `sprk_RegardingEvent` (live navigation
    property, `Models.GetEventNavigationProperty`);
  - `StampWorld` answers a `QueryExpression` by primary id with `In`, as the parent-lineage walk reads live. Before this,
    every I-16 walk over `StampWorld` answered "does not exist".
  - `tests/integration/contract/Api/Office/OfficeQuickCreateRefusalMapTests.cs` gains two rows, because its registry guard
    (`EveryCodeTheServiceDeclares_HasARow`) fails on any new service code. Both codes map to the Office generic 500 (fail
    closed), and the row checks confirm that `OfficeService.MapQuickCreateRefusal` already does this. Office creates roots,
    which are never stamped, so neither code reaches Office today. This file is outside the brief's two KEEP folders, but the
    guard requires it.
- Must-fire:
  - the pre-change service fails all 13 new or changed tests;
  - dropping the permanent/transient split fails the 400 tests (seam, system, route);
  - removing the post-create `AfterWriteAsync` fails the re-derivation and system-principal tests (and existing journal
    pins);
  - removing the document skip fails the document test;
  - not setting I-16 fails the I-16 payload test.

### R3-2 (step 6's AppendTo before step 3)

Not done; the K2 known limit stands. This change moves step 10 only, from after the create to between step 9 and step 11.
The step-6 ask still needs the secure-root plan's filing columns of the merged row, which exist only after step 3. So
nothing here makes the move natural.

## Phase 2d-2 — chat platform tables registered (task, contact, account)

Branch `task/fm-017-d2`, from `origin/task/fm-017` (14b801a53f).

Owner decision 2026-10-30, option b (`decisions.md`, last entry): the chat `dataverse.create_record` keeps creating
`task`, `contact` and `account` once it is on `RecordCreationService`. It keeps today's owner, the resolver's team. Every
other unregistered table stays `record_creation.table_unsupported`.

### What today's path does, and the entry for each table

Today all three tables take `OwnedChildWrite.PathFor` → `Owned`. Each is user-owned, none is in `PerUserTables`, and none
is a communication or a thread. So `OwnedChildWrite.CreateAsync` creates them.

| Registry field | Today's behaviour (all three) | Entry |
|---|---|---|
| Kind | Not a root. `RecordCreatorPerson` stamps roots only, and no Assigned* materialisation runs. | `Child` |
| Parents | `OwnershipOf` → `RecordOwnershipContext.ForRow` over every lookup the item binds. The task's `regardingobjectid` (the polymorphic activity regarding) is read like any other lookup. The service's step 7 uses the same builder over `OwnedChildWrite.LookupsOf`. Contact and account bind no parent. | (no field; same builder) |
| Server-owned columns | Only `CommonServerOwnedColumns` and the creator column, which every entry refuses. | none (default) |
| Mapping-protected, autonumber, optional references, pre-write rules | none | defaults |
| Field mapping | The chat handler applies none. | `MappingExclusion = ChatPlatformTableIsNotMapped` (a named rule with a rationale). The default (`MappingApplies`) would declare a policy today's path does not have. |
| Search-indexed | no | `false` (default) |
| For-person, server defaults | none (`ForPersonColumns` has no entry, so no copied default) | none (defaults) |
| Ownership exception | none: the resolver's team owns the row. A task regarding a matter gets the matter's team; an unfiled row gets the caller's business-unit team. | `null` (default) |
| Under a secure owner | Refused with `SecureFilingRefused`, because none is a reparentable child. | The service's step 7 (`OwnerDecided`) applies the same rule: OwnerUnresolved / `NoOwnerSource`. |

Choices of inert value:
- `Child` is inert: only `Root` drives behaviour (`ParentLineage.SecureRootColumns`, `AssignedAccessMaterializer`, step 7's secure flag).
- `MappingExclusion` is the explicit form of "not mapped". The service maps only when a request asks (`request.Mapping`). Repair skips mapping for these tables as well.
- Behaviour the service adds that the old core lacked: the after-create I-1 restamp, the I-16 refresh and the sharee-mirror steps. These are best-effort and no-ops for these tables. Not asserted here.

No service change was needed.

### Tests

- `tests/integration/data-mutation/RecordOwnership/SecureChildOwnershipAiToolTests.ChatPlatformTables.cs` (new partial,
  same harness as 017b item 9):
  - `AChatPlatformTable_ThroughTheService_GetsTheOwnerAndRowTheOwnedCreateGives`, 4 rows: a task regarding a matter, a
    task regarding nothing, a contact, an account. Each row asserts that `PathFor` is Owned today, that both creates succeed,
    the same owner (`ChildTeam`), and that every written column is byte-identical.
  - `ATaskRegardingASecureMatter_IsRefusedOnBothPaths_AsTheSecureFilingRefusal_AndNothingIsCreated`: the old core gives
    `SecureFilingRefused`; the service gives OwnerUnresolved / `NoOwnerSource`, with the Secure owner decision. Nothing is
    created.
  - `AnUnregisteredChatTable_IsStillRefusedByTheService_AsTableUnsupported` (`phonecall`): a must-not-widen control.
- `tests/unit/domain/Dataverse/RecordCreation/RecordTableRulesTests.cs`:
  - new `ChatPlatformTables_DeclareOnlyWhatTheOwnedCreateAppliesToday` (3 rows);
  - `MappingApplies_EveryOtherTable_IsTrue` now lists the three tables. This is a deliberate registry change.
- `tests/Spaarke.ArchTests/RecordTableRulesRegistryGuardTests.cs`: `CreateTables()` now includes the owner's three
  chat tables (`ChatOfferedPlatformTables`). The chat tool has no compiled accept-list, so the guard names them literally.
- Fixture: `ScriptedUserClient.EntitySets` gained `account` (additive).
- The two existing task tests (`CreateRecord_ATableOutsideTheOwnershipSetFiledTo{ASecure,AnOrdinary}Matter_…`) are
  unchanged and pass.

**Must-fire** (with the `task` entry removed and rebuilt):
- 5 tests fail: both task rows, the secure-matter test, the domain `task` row and `MappingApplies_EveryOtherTable`.
- In ArchTests, `EveryCreateTable_HasARegistryEntry` and `Guard_MustFire_…` fail.
- The contact, account and must-not-widen tests and the two existing task tests still pass.

After the entry was restored, everything passes.

### Finding: Repair now accepts these three tables (owner or coordinator to confirm)

`RecordCreationService.RepairAsync` refuses only a table with no registry entry (`record_repair.table_unsupported`). So
registering `task`, `contact` and `account` also makes Repair accept them.
- On such a row, Repair would re-own it to the resolver's team if it is systemuser-owned.
- It would not map it (the exclusion) and would not index it.

Nothing calls `RepairAsync` in production yet. The scheduled pass will choose its tables, and it must not include
these three unless the owner wants user-created contacts, accounts and tasks re-owned.
`RecordRepairModeTests.ATableThisServiceDoesNotCreate_IsSkipped_WithNothingRead` used `account` as its example of an
unregistered table. It now uses `phonecall`; the test means the same thing.

## Phase 2d-3 — chat `dataverse.create_record` and budget inquiry on RecordCreationService

Branch `task/fm-017-d3`, from `origin/task/fm-017` (e6c20bd51c).

### Outcome: done

| Item | State |
|---|---|
| ebilling-r1: chat refuses `sprk_externalidentifier` | Done (commit `139c7ea90b`) |
| Chat create, Owned path, through `RecordCreationService` as the caller | Done |
| Budget inquiry `ExecuteAsync` through `RecordCreationService` as the caller | Done (coordinator decision B: the existing test rig was re-pointed, with no assertion weakened) |
| Owner decision 2c: drop `sprk_matternumber` from both description copies | Done. `sprk_projectnumber` was never listed. The handler and `infra/dataverse/sprk_analysistool-dataverse-create-record-row.json` are byte-equal, and `CatalogToolDescriptionParityContractTests` passes |
| `ChatCreatedChildStampTests.cs`, including owner parity | Done (20 tests) |
| Additive service change (coordinator decision A) | Done: `RecordCreationOutcome.IsolatedCompletion` |

### The additive service change (decision A)

The new property is `RecordCreationOutcome.IsolatedCompletion` (`SecureRootInheritResult?`).

- `AfterCreateAsync` takes a callback that receives the completion's result.
- `CreateAsync` sets the property on both outcomes:
  - the created outcome;
  - the "removed again" refusal.
- The property is null when no completion ran.
- Nothing else in the service changed. Office and the route ignore the property.

The chat adapter uses the property to keep today's tool ERROR with `secured.ReasonCode`, for both `RowStranded` and
`IsComplete: false`. It is never a success with a warning.

- Tested by `ChatCreate_AnIsolatedRootWhoseSecuringDidNotFinish_KeepsTodaysErrorAndItsReasonCode` (adapter).
- Through the service, tested by the existing `SecureRootInheritanceWriterTests` and `SecureRootCreateRouteTests` chat cases.
- Must-fire: with the property left unset, 5 of those chat cases fail.

### Deviation: the chat "for" person (coordinator correction)

The handler keeps `WithForPersonAsync`: ONE WhoAmI, plus the caller's linked contact.

- It passes the remapped item as `RecordCreationPayload.Mapped(forMapped)` with `ServerSetLookupColumns = {forColumn}`, so the
  "for" column costs no AppendTo.
- This deviates from "adapters no longer resolve WhoAmI". Step 9 of the service leaves the "for" person to the adapters.

Result per table (unchanged from today):

| Table | "For" column | Value |
|---|---|---|
| matter, project, work assignment | `sprk_assignedtointernal` | the caller's linked contact |
| todo, event | `sprk_assignedto` | the linked contact |
| communication | `sprk_sentby` | the caller's systemuser |
| every other table | none | — |

Verified: the service treats the work assignment's copied `sprk_assignedto` (copied from the server-set "for" person) as
server-set. `WorkAssignmentAssignedToDefault`'s `…TheCopyOfTheServerSetForPerson_CostsNoAppendTo…` passes through the service.

### Chat: existing refusals, before and after

The code comes from `RefusalErrorCode`. The text comes from `RefusalText`, which uses the shared map
(`RecordCreationProblemMap`) except where noted.

| Case | Before (old core) | After (service) |
|---|---|---|
| Caller client failure (OBO, 401, 429, 5xx) | `MapClientError` code | same code (`PrincipalUnresolved` / `Upstream` with `ClientFailure.ErrorCode`). The text is now the map's fixed words, not Dataverse's |
| Parent missing, or no AppendTo on it | `AccessDenied` | `AccessDenied` (`NotFoundOrDenied`), with the uniform not-found text |
| Caller lacks Create or Append | `AccessDenied` | `AccessDenied` (`Forbidden` / `record_creation.denied`) |
| Server-owned column in the item | `AccessDenied` (`CallerWriteAuthorization` `Denied`) | `AccessDenied` (`BlockedColumn`). **Verified row 1:** `CallerWriteAuthorization.cs:54-59` gives `Denied` |
| Invalid payload | `VALIDATION_FAILED` | `VALIDATION_FAILED` |
| Invoice-family guard (two core records) | its own code, "The record was NOT created: … (code)." | same code and shape (`PreWrite`) |
| Secure filing refused (`SecureFilingRefused`) | `VALIDATION_FAILED`, the core's sentence | `VALIDATION_FAILED`, the same sentence (`OwnerUnresolved` with `IsSecureOwner`; `refusal.Detail`) |
| Owner not decided | `RefusalCode`, "its owner could not be decided — …" | same code and shape |
| Isolated-root plan refusal (e.g. `sdap.provision.creator_no_access`) | `RefusalCode` | same code (`Forbidden` / `Undecided` with its own code) |
| Isolated row removed again (share failed) | `ReasonCode`, "…so the new record was removed again. Nothing was created." | same text and code |
| Isolated row stranded / unfinished | ERROR with `ReasonCode` | same (decision A) |
| Fault (resolver, check, create throws) | `INTERNAL_ERROR` | `INTERNAL_ERROR` (`Unavailable`), no fallback create |
| NEW: I-1 stamp cannot be derived | — | `VALIDATION_FAILED` (`stamp_underivable`) |
| NEW: I-1 stamp read fault | — | `INTERNAL_ERROR` (`stamp_unavailable`) |
| NEW: unregistered Owned-path table (e.g. `phonecall`) | created by the old core | `VALIDATION_FAILED` (`table_unsupported`), no create |
| NEW (2c): `sprk_matternumber` in the item | written | `AccessDenied` (`BlockedColumn`) |

**Verified row 2:** the copied work-assignment `sprk_assignedto` (see the deviation section).

**`columnsSet`:** this stays logical names. `WrittenColumnsOf` returns the item's columns plus each copied default's target
column, translated back from its bind key. It never reports the owner, the creator or the stamp binds.

### Budget inquiry: existing refusals, before and after

`BudgetInquiryActionExecutor.ToOutcome` is unchanged: `Refused` → `InvalidState`, and `CreateFailed` → `WriteFailed`.

| Case | Before | After |
|---|---|---|
| Caller lacks Create, Append or AppendTo | Refused, the core's words | Refused, the map's words (the uniform not-found for AppendTo) |
| Secure matter | Refused (`SecureFilingRefused`) | Refused (`OwnerUnresolved` / `NoOwnerSource`) |
| Owner, resolver or check fault | CreateFailed, "could not be created." | CreateFailed, the map's 503 words |
| NEW: I-1 stamp | — | Refused (`stamp_underivable`) or CreateFailed (`stamp_unavailable`) |
| NEW: service unresolvable or throws | — | CreateFailed; nothing is created or sent |

Rule: the `Unavailable` kind gives CreateFailed; every other kind gives Refused.

Text: `sprk_servicerequest` has no noun of its own in `RecordCreationService.NounOf`, which reads
`ChildRecordEndpoints.Noun`. The map therefore says "record", as in "A record this record is filed under was not found. The
record was not saved." `ChildRecordEndpoints.cs` is do-not-touch, so this is a finding for 2d-5 (below).

### Tests

`tests/integration/data-mutation/RecordOwnership/ChatCreatedChildStampTests.cs` is a partial of
`SecureChildOwnershipAiToolTests` with 20 tests:

- chat event under an event: the I-1 bind and I-16 Restricted are in the create payload. It uses the REAL resolver with the
  share world's column probe.
- owner parity: today's `OwnedChildWrite.CreateAsync` and the chat create give the same owner (`ChildTeam`) and the same
  creator. Today's core writes no stamp; the chat create writes it.
- the secure matter's sharee mirror on a chat to-do;
- `phonecall`: `VALIDATION_FAILED`, with no create call and no fallback;
- no AppendTo: `AccessDenied`, uniform not-found, no create;
- stamp read fault: `INTERNAL_ERROR`, no create;
- the app-only create throws: `INTERNAL_ERROR`, one attempt, no fallback;
- `IsolatedCompletion` stranded, unfinished, removed and complete;
- `columnsSet`;
- the refusal map over every kind, plus an every-kind-has-a-code check;
- the description (2c);
- budget:
  - Sent, with the team owner, the I-1 bind and the email;
  - no service: CreateFailed, nothing created or sent;
  - no AppendTo: Refused;
  - a resolver fault: CreateFailed;
  - a secure matter: Refused;
  - every refusal kind;
  - the two step-10 codes.

**Re-pointed (decision B; constructor or symbol moves only, no assertion weakened):**

- `BudgetInquiryExecutorTests` rig;
- `DecisionActionExecutorsTests`;
- `DataverseCreateRecordHandlerTests`;
- `DataverseToolNameFreezeTests`;
- `BusinessSliceDeterminismContractTests`;
- `P2LoopInjectionEvalSuiteTests`;
- `AssignedAccessWriterTriggerTests`;
- `SecureRootInheritanceWriterTests`;
- `ChatServerWrittenTableBlockTests`;
- `SecureChildOwnershipAiToolTests.CreateRecordHandler`.

`RouteRecordCreation.Default(...)` was added for these sites.

### Out-of-scope findings, for 2d-5

- `BudgetInquiryExecutor.PreflightAsync` (~:160-190) keeps its own WhoAmI and an `OwnedChildWrite.OwnershipOf` mirror of the
  old core's secure rule. The service has no preflight mode, so 2d-5 must keep `OwnershipOf` or give the service a preflight.
- `RecordTableRules.cs:443`: `ChatCallerIsTheForPerson`'s rule text names `DataverseCreateRecordHandler.WithForPersonAsync`.
  It stays true under the deviation above.
- `RecordCreationService.NounOf` / `ChildRecordEndpoints.Noun` has no noun for `sprk_servicerequest`, so the budget
  inquiry's refusal text says "record" twice.

## Phase 2d-4 — re-point email.draft

Branch `task/fm-017-d4`, created from `origin/task/fm-017` (e6c20bd51c). Rigor FULL (task 017).

### Outcome

1. **First pass: stopped at the brief's escalation trigger.** Option (a) would stop the handler writing the I-1 stamp into
   the create item. That changes the owner in one data state and an existing refusal in two others. The evidence is
   under "Why" below.
2. **Coordinator decision, 2026-10-30: option (a′).** The re-point is done: see "The re-point (option a′)" next.

### The re-point (option a′)

#### What changed in `EmailDraftToolHandler`

- **A filed draft is created through the service.**
  - It calls the scoped `RecordCreationService.CreateAsync` with `RecordCreationPrincipal.ForCaller(_dataverse, oid)`.
  - The payload is `RecordCreationPayload.Mapped(mapped.Item)`.
  - The request sets `ServerSetLookupColumns = {sprk_sentby}` and `CorrelationId = DecisionId`.
- **The stamp stays in the item.** The handler keeps writing its `DeriveForHostAsync` stamp into the create item, as
  today. Consequences:
  - Step 5 asks AppendTo on the stamped root.
  - Step 7's owner question includes the stamped root.
  - Step 10 plans nothing missing.
- **What does not change:**
  - the owned or run-as-user choice: `IsFiledUnderAnOwnershipParent` and `OwnedChildWrite.AppliesTo`;
  - the sender: `sprk_sentby` = the caller, sent as a server-set lookup;
  - the run-as-user POST of an unfiled draft.
- **Fail closed:**
  - A refusal returns the tool error and creates nothing.
  - A fault in the service propagates to the handler's catch, which returns `INTERNAL_ERROR`.
  - A result with neither an id nor a refusal is `INTERNAL_ERROR`.
  - There is no fallback create.
- **Warnings.** The service's warnings are added to the tool result's warnings. They are new: the owned create returned
  none.
- **Constructor.** The `IRecordOwnershipResolver` and `IFieldMappingDataverseService` parameters are replaced by
  `RecordCreationService`.
  - The class is now `internal sealed`. A public class cannot have a public constructor that takes the internal service
    (CS0051).
  - Handlers are found by `assembly.GetTypes()`, which includes internal types, so DI still resolves it, as it resolves
    `OfficeService`, which is also internal.
  - 2d-3's `DataverseCreateRecordHandler` will hit the same constraint.

#### Deviation: the handler still calls WhoAmI itself

This is a deviation from the acceptance criterion that callers "no longer resolve WhoAmI themselves".

- **Kept: the first WhoAmI (~:366).** It identifies the caller for the AppendTo check on the regarding record. That check
  must run before the app-only `DeriveForHostAsync`, or the derivation's answers would reveal whether the record exists
  (verifier round on #1612, F3). The same id is the sender: the service never sets `sprk_sentby`.
- **Deleted: the second WhoAmI (~:418-427).** It could not run, because "filed" implies a regarding, so the first WhoAmI
  always ran first. In its place a null caller throws `InvalidOperationException`. The catch turns that into
  `INTERNAL_ERROR`, and nothing is created.

#### Error codes

Each refusal KIND keeps the tool code the owned create gave for it. The text is always the shared map's
(`RecordCreationProblemMap.ToProblemDetails(...).Detail`), so upstream text is never shown and a not-found or denied
record gets the one uniform body. This follows the Office precedent. The mapping is `EmailDraftToolHandler.RefusalCodeAndText`.

| Kind (service codes) | Tool `ErrorCode` | Today, through `OwnedChildWrite.CreateAsync` |
|---|---|---|
| `PrincipalUnresolved`, `Upstream` | the failure's own `ErrorCode` | `ClientFailure` → `MapClientError` (same code; the text was Dataverse's) |
| `NotFoundOrDenied`, `Forbidden` + `record_creation.denied`, `BlockedColumn` | `AccessDenied` | `Denied` → `AccessDenied` |
| `Invalid` (including `stamp_underivable`) | `VALIDATION_FAILED` | `Invalid` → `VALIDATION_FAILED` |
| `OwnerUnresolved` (resolver codes), other `Forbidden` codes | the code, carried | `OwnerRefusal.RefusalCode`, carried |
| `Unavailable` (including `stamp_unavailable`, `owner_unavailable`, `check_failed`), `Undecided`, `Conflict` | `INTERNAL_ERROR` | a fault → the catch → `INTERNAL_ERROR` |

- **Correction to the first-pass plan below.** That plan mapped `OwnerUnresolved` to `VALIDATION_FAILED`. The owned
  create CARRIED the resolver's code, so the code is carried, and the not-isolated test pins it.
- **Unreachable rows.** Two of these cannot occur for a communication, which is a reparentable child with no plan and no
  pre-write rule:
  - `OwnerUnresolved` from a secure-filing refusal: today `INTERNAL_ERROR`, now `NoOwnerSource` carried;
  - `Undecided`.

#### Tests (KEEP paths)

**New file:** `tests/integration/data-mutation/RecordOwnership/EmailDraftRecordCreationTests.cs`. It is a partial of
`SecureChildOwnershipAiToolTests`, over that suite's real resolver world and app-only seam, and has 20 cases. The handler
and the service each read through `StampWorld`.

| Case | Owner | Sender | Code |
|---|---|---|---|
| A draft under an ordinary matter | child BU team | caller | created |
| A draft under an invoice under an ordinary matter (filed through its stamp) | child BU team | caller | created; the `sprk_RegardingMatter` stamp bind appears once in the create payload |
| A draft under an invoice (team-owned, ordinary BU) under a secure matter | `SecureNamedTeam` | caller | created |
| A draft under an invoice under a matter flagged secure but not isolated | none | none | `record_owner_secure_parent_not_isolated`; no app create, no user POST |
| Caller may AppendTo the invoice but not its matter | none | none | `AccessDenied`; nothing created |
| Unfiled draft | none (run-as-user, its creator) | none (no `sprk_SentBy` in the user's POST) | created as the user; no app create |
| The app-only create faults | none | none | `INTERNAL_ERROR`; no fault text; nothing created; no user POST |

It also has refusal-map theories:
- 12 kind/code rows: the tool code, and the text equal to the shared map's;
- 2 rows for `PrincipalUnresolved` and `Upstream`: the failure's own code, without its upstream text.

**Changes to existing files: construction sites only.**
- `SecureChildOwnershipAiToolTests.cs` (2) and `.RegardingPair.cs` (2) now pass `DraftCreation()` in place of
  `_world.Resolver(), _appOnly.Object`.
- `EmailDraftToolHandlerTests.cs` and `EmailDraftToolHandlerPerItemToolsTests.cs` pass `RouteRecordCreation.Over(...)`
  over the same two doubles. They existed already, so this is no new test under the unit path.
- `RegardingPair`'s control test (`EmailDraft_RegardingAnAppendableInvoice_DerivesItsAncestry_AndIsCreated`) gives the
  service's step 10 a `StampWorld` holding the same invoice the handler reads. In production one Dataverse answers both.
  The empty default world answered "not found", which is `stamp_underivable`.

No assertion changed.

**Must-fire evidence:**
- **Option (a)**, stamps left out of the item (`ancestorStamps.Take(0)`): the secure-matter, not-isolated and
  root-AppendTo tests fail (3 of 77). The ordinary-invoice payload test still passes, because the service's step 10 then
  binds the stamp itself.
- **`OwnerUnresolved` mapped to `VALIDATION_FAILED`, and no sender:** 7 fail.
  - The two `OwnerUnresolved` theory rows fail.
  - The not-isolated test fails.
  - The four sender tests fail; one of them is the existing `EmailDraft_RegardingASecureMatter_…`.
- With the code restored, all 77 `EmailDraft*` tests pass.

#### ArchTests: owner census

`RecordOwnerAssignmentCensusTests.EveryUnscannedWriterRoutesThroughTheResolver` failed. Its `ResolverCall` pattern knew
only `OwnedChildWrite.CreateAsync`, so it reported "EmailDraftToolHandler.cs: no IRecordOwnershipResolver call". The
route passed only because it still calls `OwnedChildWrite.RefileAsync`.

- **Fix:** `ResolverCall` also accepts `_?recordCreation.CreateAsync(`, the scoped service's create. That service is
  itself an unscanned writer whose step 7 calls the resolver.
- **Controls:** a new fact, `TheUnifiedCreateCountsAsAResolverCall`.
  - must fire: `_recordCreation.CreateAsync(...)` and `recordCreation.CreateAsync(...)`;
  - must not fire: `_entities.CreateAsync(...)` and `myrecordCreation.CreateAsync(...)`.
- The email.draft census entry's description now names the service.
- **2d-3 and 2d-5** get this for free when their handler holds the service as `_recordCreation`. 2d-5 deletes
  `OwnedChildWrite.CreateAsync`, so it can then drop that alternative from the pattern.

#### Verification

- Targeted run (`EmailDraft*`, `SecureChildOwnershipAiTool*`, `Communication*`): 2706 passed, 0 failed, 2 skipped.
- `verify-suites.sh` (log directory: this worktree's `.verify-logs`, not committed):
  - solution build: 0 warnings, 0 errors;
  - suites: 6239 passed, 0 failed, 7 skipped;
  - ArchTests: 1 failure, the census failure above.
- After the census fix: ArchTests 1859 passed, 0 failed.

### Why: the service decides the owner and the AppendTo checks over the item, not over step 10's stamp

- Step 10 (`RecordCreationService.StampBeforeCreateAsync`, 2d-1) binds the derived stamp into the create body (`fields`)
  only. By design, the item that step 5 (the caller checks) and step 7 (the owner) read stays unchanged.
- Today, `EmailDraftToolHandler.BuildCommunicationItem` writes the `DeriveForHostAsync` stamps into the item. As a result,
  `OwnedChildWrite.CreateAsync`:
  - asks the caller's AppendTo on the stamped root (every lookup in the item except `sprk_sentby`);
  - includes the root in the resolver's question.
- If the stamp leaves the item, the owner is decided over the regarding record alone. The resolver does not follow a
  TEAM-OWNED child parent ("its owner already IS the resolver's answer", `RecordOwnershipResolver.cs` ~:3803). So the
  root then takes no part in secure-if-any.

The stamp exists only when the regarding is an intermediate: `sprk_analysis`, `sprk_invoice` or `sprk_budget`. These
are the only email.draft regarding tables in `CoreAncestorResolver.IntermediateRootColumns["sprk_communication"]`. A root
regarding is bound directly, so no stamp is added.

A scratch probe was run and then deleted; it is not committed. It used the real resolver over `OwnershipDirectory`, asking
`RecordCreationService.OwnerQuestionOf("sprk_communication", …)` for a draft regarding an invoice, team-owned by the
child BU team, under the matter:

| World | Today (stamp in the item) | Option (a) (stamp in the body only) |
|---|---|---|
| Ordinary matter | child BU team | child BU team (same) |
| Secure and isolated matter; the invoice is still team-owned in the ordinary BU (reconcile window) | `SecureNamedTeam` | child BU team: **the owner changes, and isolation is weaker** |
| Matter flagged secure but not isolated (interrupted provisioning, C11) | refused `record_owner_secure_parent_not_isolated` | created, child BU team: **an existing refusal changes** |

Found by reading the code, not probed: a caller with AppendTo on the invoice but not on its matter is refused
`AccessDenied` today. Under option (a) they are not refused, because the matter is no longer a record the item names.
**An existing refusal changes.**

If the invoice is not team-owned, the resolver looks through to its filing, so the root is included and the owner is
the same.

### Other findings (for whoever resumes)

- **The service does not set the sender.** `RecordCreationService` never writes `sprk_sentby`. Step 9 says
  "'for' person and table defaults stay with the adapters". `RecordTableRules`' `ChatCallerIsTheForPerson` is a
  description, not a step. A `PostMappingDefaults` entry for it is an argument error, because the "for" column is
  `ProtectedFromMapping`. So on the owned path the handler must keep sending `sprk_sentby` = the caller's WhoAmI id, with
  `ServerSetLookupColumns = {sprk_sentby}`, exactly as today. That WhoAmI is the one the handler already makes for the
  pre-derivation AppendTo check on the regarding record (`EmailDraftToolHandler.cs` ~:366). That check stays: without it,
  the app-only derivation would leak whether the record exists. The second WhoAmI (~:418-427) never runs, because
  "filed" implies a regarding.
- **"A draft under an event or communication" cannot happen for email.draft.** Its regarding vocabulary
  (`RegardingLookupColumns`) has no `sprk_event` or `sprk_communication`. The stamp-only cases are the analysis, invoice
  and budget regardings. All three are ownership parents themselves (`OwnershipParentEntities`). So
  `IsFiledUnderAnOwnershipParent` is already true from the regarding alone, and its stamp branch decides nothing today.
- **Error codes planned for the re-point (not applied).** These follow the Office precedent: the existing tool code by
  kind, and the text from the shared map.

  | Kind | Code |
  |---|---|
  | `Forbidden` / `NotFound` / `BlockedColumn` | `AccessDenied` |
  | `Invalid` (including `stamp_underivable`) / `OwnerUnresolved` | `VALIDATION_FAILED` |
  | `Unavailable` (including `stamp_unavailable`, `owner_unavailable`) | `INTERNAL_ERROR` |
  | `PrincipalUnresolved` / `Upstream` | `MapClientError(ClientFailure)` |
  | resolver codes | carried |

- **Constructor.** The re-point replaces the `IRecordOwnershipResolver` and `IFieldMappingDataverseService` parameters
  with `RecordCreationService`. Every existing `new EmailDraftToolHandler(…)` must change:
  - `SecureChildOwnershipAiToolTests.cs` :481, :512;
  - `.RegardingPair.cs`;
  - `EmailDraftToolHandlerTests.cs`;
  - `EmailDraftToolHandlerPerItemToolsTests.cs`;
  - `CommunicationVisibilityFixtures.cs`;
  - `P2LoopInjectionEvalSuiteTests.cs`.

### Options for the coordinator / owner

1. **(a′) Keep the derived stamp in the create item, as today; recommended.**
   - Step 10 then finds no stamp missing and adds only what is missing (fill-only).
   - The owner question and the AppendTo check are the same as today.
   - They are also the same as the route, whose browser payloads carry the client-derived stamp.
   - No service change. It departs from decision (a) only in "stops writing the stamp".
2. **(b) Service change.** Steps 5 and 7 would read step 10's derived stamp: AppendTo on it and the owner question over
   it. This changes the service contract for every caller, the route included, and needs owner and UAC-r2 review.
3. **(c) Accept the change.** This needs owner sign-off, because of the weaker isolation in the reconcile window and the
   changed refusals. Not recommended.

## Phase 2d-5 — delete OwnedChildWrite's create half; fold the notes

Branch `task/fm-017-d5`, created from `origin/task/fm-017` (a16b1a8c25, 2d-1..2d-4 merged). Rigor FULL (task 017).

### What was deleted

Nothing in `src/` called `OwnedChildWrite.CreateAsync` after 2d-4. Deleted from
`src/server/api/Sprk.Bff.Api/Services/Dataverse/OwnedChildWrite.cs` (1505 → 1241 lines; +15 / −279):

- `CreateAsync` (the owned create), `CreateIntoIsolationAsync` (its isolated root create) and `WithCopiedDefaultsAsync`
  (the private copied-default helper only `CreateAsync` called): 231 lines;
- the `Outcome` members only that create set: `CreatedId`, `WrittenColumns`, `OwnerRefusal`, `SecureFilingRefused`,
  `PlanRefusal`, `Isolated`, `IsolatedFor`, `InvoiceFamilyRefusal`.

Kept:

- `Outcome` itself, as the answer of a caller check (`CallerWriteAuthorization` builds it with `Denied`,
  `ParentUnavailable`, `RefusedColumn`, `ClientFailure` and `Invalid`; `RecordCreationService` and the chat pair check
  read it). `Succeeded` is now `Denied`, `ClientFailure` and `Invalid` all null. That is the same answer as before for
  every `Outcome` a check can build, because the removed members were only ever set by the deleted create.
- `Merge` (still used by `KeepPairInStepAsync`), `WithLookup` and `Sets` (the chat handler's "for" person), and every
  symbol the brief listed: `RefileAsync` + `RefileOutcome`, `CheckInvoiceFamilyAsync` + `InvoiceFamilyStep`,
  `ForPersonColumns` / `ForPersonColumn`, `PathFor` / `WritePath` / `PerUserTables`, `OwnershipOf`, `ParentsOf`,
  `WritesOf`, `PairOf` / `PairWrite`, `BodyFields`, `RecordTypeLogicalNameColumn`, `BffManagedPairColumnsOf`,
  `KeepsItsCreatorWhenUnfiled`, `ServerOwnedColumns`, `EffectiveLookups`, `ReadLookupsAsCallerAsync` / `StoredLookups`,
  `PatchWithBffManagedPairAsync`, `KeepPairInStepAsync`, `CheckCallerMayRefileByPairAsync`,
  `CallerMayFileRootUnderSecure`.
- `RefileAsync` is byte-unchanged. The class and file names are unchanged. A new remarks paragraph says the create half is
  gone and what stays.

### Parity tests re-pointed to pinned values

Each test compared the service against the old core in the same harness. Before the deletion, each was run once with a
temporary probe that recorded what the old core wrote or refused (the probe was not committed). The tests now assert those
values, as constants, against the service. No assertion was weakened and no test was deleted.

| Test | Old core's recorded values, now pinned |
|---|---|
| `RouteDecisionCreateParityTests` `AServiceRequestUnderAMatter_ThroughTheService_GetsTheOwnerCreatorAndStampTheOwnedCreateGives` | owner `/teams(ChildTeam)`; no creator person; stamp `[/sprk_matters(OrdinaryMatter), null]`; the whole written row: `ownerid@odata.bind=/teams(ChildTeam); sprk_RegardingMatter@odata.bind=/sprk_matters(OrdinaryMatter); sprk_RegardingOrganization@odata.bind=/sprk_organizations(PtyFirm); sprk_RegardingRecordType@odata.bind=/sprk_recordtype_refs(DcRecordTypeMatter); sprk_direction=100000001; sprk_name=Budget question; sprk_regardingrecordid=OrdinaryMatter; sprk_regardingrecordtypelogicalname=sprk_matter` |
| `RouteDecisionCreateParityTests` `AServiceRequestUnderASecureMatter_IsRefusedOnBothPaths_AndNothingIsCreated` | `SecureFilingRefused` = "A 'sprk_servicerequest' record cannot be created under a secure record from chat: it would itself have to be made secure (its own owner team, container and sharing — the record's Make Secure path). It was NOT created. Create it from the secure record itself." The service now must give `OwnerUnresolved` / `record_owner_no_source`, a secure owner decision, and that exact detail |
| `SecureChildOwnershipAiToolTests.ChatPlatformTables` `AChatPlatformTable_ThroughTheService_GetsTheOwnerAndRowTheOwnedCreateGives` (4 rows) | task regarding a matter: `ownerid@odata.bind=/teams(ChildTeam); regardingobjectid_sprk_matter@odata.bind=/sprk_matters(OrdinaryMatter); subject=Call the firm`; task regarding nothing: `ownerid…; subject=Call the firm`; contact: `firstname=Ada; lastname=Lovelace; ownerid…`; account: `name=Babbage & Co; ownerid…` |
| `…ChatPlatformTables` `ATaskRegardingASecureMatter_IsRefusedOnBothPaths_AsTheSecureFilingRefusal_AndNothingIsCreated` | `SecureFilingRefused` = "A 'task' record cannot be filed under a secure record from chat, and was NOT created. Create it from the record itself." The service's refusal detail must equal it (plus the existing kind, code and secure-owner checks) |
| `ChatCreatedChildStampTests` `ChatCreate_AStampedEvent_IsOwnedAsTodaysCoreOwnsIt_TheStampChangesNoOwner` | owner `/teams(ChildTeam)`, creator `/systemusers(Caller)`, `sprk_RegardingEvent@odata.bind=/sprk_events(Event)`, and **no** `sprk_RegardingMatter` (the old core wrote no stamp at create; kept as a statement). The chat create through the service must match the first three and DOES write `sprk_RegardingMatter` = `/sprk_matters(OrdinaryMatter)` |
| `DataverseCreateRecordHandlerTests` (~:588) | comment only: the creator stamp is the service's step 11 now |

Two test-only fixes while pinning: the pinned values are static properties (`=>`), not `static readonly` fields, because a
partial class's static fields in one file initialise before the GUID fields of another file. The regarding-event bind is
read through `Bind(...)` because it is a mapped body value (`JsonElement`), not a string.

### Stale references

- ArchTests:
  - `RecordOwnerAssignmentCensusTests.ResolverCall` no longer accepts `OwnedChildWrite.CreateAsync(`. It keeps
    `_?recordCreation.CreateAsync(` and `OwnedChildWrite.RefileAsync(`. New control
    `TheDeletedOwnedCreate_NoLongerCountsAsAResolverCall`: a fixture writer whose only "routing" is
    `OwnedChildWrite.CreateAsync(` or `SomeCore.CreateAsync(` is not a resolver call (must fire); with
    `_recordCreation.CreateAsync(` or `OwnedChildWrite.RefileAsync(` it is (must not fire).
  - The census entries for the deleted members are removed: the two `OwnerWriteEntry` rows (`CreateAsync` Routed,
    `CreateIntoIsolationAsync` Root) and the `PersonBearingWriter` row; `RecordCreationService.cs`'s `OwnedCreateFields`,
    `IsolatedCreateFields` and `DecideOwnerAsync` rows already cover them. `OwnedChildWrite.cs` stays an `UnscannedWriter`
    (it still holds the run-as-user re-file PATCH and calls `ReparentAsync`), re-described as the re-file core.
  - The `POST /api/v1/child-records/{table}` census entry names `RecordCreationService.CreateAsync` and what it does; the
    `IsolatedCreateFields` entry names "the service's isolated create".
  - `ExternalDocumentNarrowingGuardTests`: the two allow-list rows for the deleted members are removed;
    `RecordCreationService::CreateAsync`'s reason is now self-contained.
  - `RecordTableCopiedDefaultGuardTests`: `OwnedChildWrite.cs` is no longer an allowed applier (it applies none);
    the guard is now "applied only by the server create" (method renamed
    `TheCopiedDefault_IsDeclaredOnce_AndAppliedOnlyByTheServerCreate`).
  - `RouteAuthorizationGuardTests.Ledger` (~:2286) and `ContactCreateOwnerPolicyGuardTests` (~:760): descriptions name the
    service.
- `src/` doc comments (comment-only; the one string literal changed is the `ChatPlatformTableIsNotMapped` rationale text,
  which nothing asserts): `ChildRecordEndpoints.cs`, `OfficeModule.cs`, `DataverseCreateRecordHandler.cs`,
  `EmailDraftToolHandler.cs`, `InvoiceFamilyCoreRecordGuard.cs`, `RecordCreatorPerson.cs`, `RecordCreationService.cs`,
  `RecordCreationService.Mapping.cs`, `RecordTableRules.cs`, `BudgetInquiryExecutor.cs`, `OwnedChildWrite.cs`; tests
  `SecureRootInheritanceWriterTests.WorkAssignmentAssignedToDefault.cs`, `EmailDraftRecordCreationTests.cs`,
  `SecureChildOwnershipAiToolTests.cs`. No `<see cref>` to a deleted member remains.
- `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md`: the route's create, the chat create's "for" column, the chat
  contact create and the invoice-family guard's create-side invoker name `RecordCreationService`.
  `SPAARKE-FIELD-MAPPING-FRAMEWORK.md` named no deleted member.
- What `git grep -n "OwnedChildWrite.CreateAsync\|CreateIntoIsolationAsync" -- src tests docs` still returns: only lines
  that say "deleted in 017", "replaced" or "the deleted …", plus the census control's fixture text.

### Coordinator addition: the service request's noun

`RecordCreationService.NounOf` had no arm for `sprk_servicerequest`, so it fell through to `ChildRecordEndpoints.Noun` and
the budget inquiry's not-found read "A record this record is filed under was not found. The record was not saved."
`"sprk_servicerequest" => "service request"` is added beside the matter and project arms; `ChildRecordEndpoints.cs` is not
touched.

- Test: `RecordCreationProblemMapTests.ToProblemDetails_AServiceRequestNotFound_NamesAServiceRequest_NotARecord`:
  "A record this service request is filed under was not found. The service request was not saved."
- Existing budget tests that pinned the old wording, updated to the new text:
  `BudgetInquiry_ACallerWithoutAppendToOnTheMatter_IsRefused_AsBefore_InTheSharedMapsWords_AndNothingIsCreated` (the
  not-found) and `BudgetInquiry_WhenTheOwnerDecisionFaults_IsCreateFailed_AsBefore_InTheSharedMapsWords_AndNothingIsCreated`
  (the 503 text, "The service request could not be saved right now, …").
- Must-fire: with the arm removed, those 3 tests fail (121 run); restored, they pass.

### 2d-3's findings for 2d-5

- `BudgetInquiryExecutor.PreflightAsync` keeps its own WhoAmI and its `OwnedChildWrite.OwnershipOf` mirror of the secure
  rule: `OwnershipOf` is kept (the service has no preflight mode). Its remark now names step 7 as the rule it mirrors.
- `ChatCallerIsTheForPerson`'s text (`RecordTableRules.cs` ~:443) still names `WithForPersonAsync`; still true.
- The service-request noun: fixed above.

### Verification

- Commits: `524e2d31dd` (the deletion, tests and guards), `ab82d5eb7d` (architecture doc), `432d2bfcce` (the
  service-request noun), then this notes fold.
- `verify-suites.sh` (log directory: this worktree's `.verify-logs/d5`, not committed), at `432d2bfcce`:
  - solution build: 0 warnings, 0 errors;
  - ArchTests: 1860 passed, 0 failed (1859 before + the new census control);
  - filtered suites: 6262 passed, 0 failed, 7 skipped (6261 before + the new noun test).
- Extra filter (`BudgetInquiry|EmailDraft|DataverseCreateRecord|ChatCreatedChild|ToolDescription`): 213 passed, 0 failed.
- Census must-fire: with `OwnedChildWrite\.CreateAsync` put back into `ResolverCall`, the new control
  `TheDeletedOwnedCreate_NoLongerCountsAsAResolverCall` fails ("must fire: an unknown create is not a resolver call");
  restored, it passes.
- Publish size (`publish-delta.ps1`, fresh builds of origin/master `9f4e3820b2` and `432d2bfcce`): master 40,968,163 bytes,
  branch 40,940,249 bytes, 192 files each; delta −27,914 bytes (the whole task-017 line vs master); total 39.04 MB.
- `dotnet list package --vulnerable --include-transitive`: no vulnerable packages.
- lint-staged made its usual backup stashes on commit (`1d6754c2d6`, `b88a702b3f`) and cleared them itself; none was
  dropped by hand.
