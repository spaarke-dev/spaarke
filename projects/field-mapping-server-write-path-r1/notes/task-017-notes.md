# Task 017 — re-point the route, chat and decision callers

Phase 1 (the invoice-family pre-write rule reconcile) is commit f758480f3c. Phase 2b (017b) is below. 017c appends its
own section later (chat `dataverse.create_record`, `email.draft`, the budget inquiry).

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
