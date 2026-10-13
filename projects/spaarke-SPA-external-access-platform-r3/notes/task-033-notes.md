# Task 033 — External-plane message read endpoint (FR-04)

Date: 2026-10-10. Rigor FULL (tags bff-api, auth, testing). Implemented by a parallel-wave implementer agent (opus/high); review passes are the main session's.

## What it does

`GET /api/v1/external/{root}/{id}/messages` returns the messages filed regarding one record, oldest first (newest last), as `ExternalCollectionResponse<ExternalMessageDto>` (`value` + `truncated`).

- **Group.** Mapped inside the `/api/v1/external` group (`ExternalAccessEndpoints.MapExternalUserEndpoints` → `externalGroup.MapExternalMessageEndpoints()`), so it inherits `ExternalCollaboration` + `CallerPrincipalAuthorizationFilter`. The handler never branches on plane.
- **Roots** (case-insensitive): `projects`, `matters`, `workassignments`, `invoices`, `servicerequests`. Any other segment is 404 `EXT_MESSAGES_ROOT_NOT_SUPPORTED`, with no read.
- **Record gate** (fail-closed, before any message read). The external module registry decides:
  - project / matter / work assignment → `module.IsRecordAccessible` (the Read-bearing accessible set composed by the one evaluator on both planes);
  - invoice → the invoice's `sprk_matter` / `sprk_project` are read app-only by id (`RecordService.GetRecordAsync`), and the row must survive the `invoices` module's `ScopeRows`;
  - service request → its `sprk_requestedby` is read app-only, and the row must survive the `service-requests` module's `ScopeRows` (the caller's own contact; CIAM only when `ExternalAccess:PartnerServiceRequestsEnabled` is on — task 011).
  - When the caller has no accessible id in that module at all, the gate refuses **without** a Dataverse read.
  - Outside the set, absent, and empty-set callers all get the same 403 `sdap.external.messages.record_not_accessible` (no existence oracle).
- **Read.** `CommunicationThreadReadService.ReadRegardingAppOnlyAsync` (new app-only overload of the existing read model): one app-only query on `sprk_communications` keyed on the typed regarding lookup (`_{RegardingFieldMap.FieldFor(entity)}_value eq {id}`, `statecode eq 0`, `$orderby=createdon asc,sprk_communicationid asc`, `$top=501`), the existing row parser, the SAME shared `ICommunicationAccessFilter`, the existing DTO builder. Every row is re-checked (a row whose regarding value is not the gated id is dropped). Bounded to 500 rows; `truncated` is set when more exist.
- **Internal-only.** Reaches only a systemuser principal that `ISystemUserIdentityResolver.IsExternalAsync` says is internal. The endpoint passes `principal.SystemUserId` (null for every contact principal — CIAM and workforce contact), so contacts never receive `sprk_isinternalonly = true`; an unreadable flag hides the row (the filter's fail-closed path).
- **Columns.** Explicit `$select` (id, subject, body, body format, direction, sent-by + its formatted name, created on, internal-only, privilege, the regarding lookup) — no attachments, no From/To. The response DTO is an allow-list: `sprk_communicationid`, `sprk_subject`, `sprk_body`, `sprk_bodyformat`, `sprk_direction`, `sendername`, `createdon`.
- **No OBO, no new secret.** App-only through the BFF's existing Dataverse identity (`DataverseWebApiService` for messages, `IDataverseService` via `RecordService` for the gate read).

## Decisions

1. **Reuse, not fork (POML constraint).** `CommunicationThreadReadService` could only run impersonated, so it gained an app-only overload (`ReadRegardingAppOnlyAsync`) that reuses its parser, filter, DTO builder and constants. The app-only rows come from a new seam `IAppOnlyCommunicationQuery` over a new `DataverseWebApiService.RetrieveMultipleAppOnlyAsync`; the impersonated and app-only GETs share one private core (`RetrieveMultipleRowsAsync`), so the impersonated method's behaviour is unchanged (same Guid.Empty refusal, same annotations header).
2. **A separate seam rather than a member on `IImpersonatedCommunicationQuery`.** That interface's contract is "no app-only fallback", and nine hand-written test doubles implement it. The new seam is an optional trailing constructor parameter of `CommunicationThreadReadService`, so existing constructions are unchanged; DI registers it (`CommunicationModule`), and the method throws if it is absent (never a substitute read).
3. **Query by the message's own regarding lookup**, as the filtered query's `regarding=` facet does, not via threads. One query; messages inherit their thread's regarding when posted.
4. **The module registry is the gate authority.** It is what the module grids use, so this route and the grid can never disagree about a record; the partner service-request switch is honoured for free.
5. **`documents` → 404 (deviation from the POML root list).** `sprk_communication` has no regarding lookup to `sprk_document` (`RegardingFieldMap`; `SecureChildLineage`'s `sprk_communication` filing lookups are thread + regarding{analysis, budget, event, invoice, matter, project, reportcard, workassignment}). No message can be regarding a document, so a 200-empty would advertise a thread that task 034 could never post to. Recorded in `decisions.md`.
6. **External-licensed systemuser gets no internal-only** (stricter than "every caller that is not a systemuser"): the same `IsExternalAsync` bit every internal read applies (#675). Recorded in `decisions.md`.
7. **`sprk_bodyformat` is returned** alongside the body (beyond the POML's "body text") so the client renders an HTML body as HTML and not as markup text; the client must sanitize it (task 051).
8. **Sender display name** is the `sprk_sentby` systemuser's FormattedValue (the existing source). A message with no `sprk_sentby` (inbound email; a contact-sent message from task 034) has `sendername = null`. Selecting `_sprk_createdbycontact_value` was not done: the task 020 schema is not imported on dev yet, and selecting a missing column 400s the whole read. Task 034 should add the contact-sender name once the column is live.

## Escalation check (POML trigger)

Not triggered. The app-only query returns only rows whose typed regarding lookup equals the gated record. For a matter / project / work-assignment root this includes messages filed under a child of that root (an invoice, event, analysis …), because the core-ancestor stamp (`CoreAncestorResolver`) writes the root lookup on them. Those rows ARE covered by the composed set: the binding rule (docs/data-model/sprk_communication.md, child-access-permission.md, owner Q6 / round 81) is that "a communication's ACCESS is decided by its parent root", and the same child records are already exposed to the caller through the module grids (invoices/documents scoped by parent root). Filed communications are owned by the root's team (UAC task 146), so the internal plane's impersonated answer for a filed message is root-scoped too.

## §11 component justification

**`GET /api/v1/external/{root}/{id}/messages` (new endpoint, new file `ExternalMessageEndpoints.cs`)**
1. Existing — `GET /api/communications/threads` / `by-regarding` (`CommunicationEndpoints.cs`) and `CommunicationThreadReadService`, all impersonated as a systemuser on the internal plane.
2. Extension — the read service is extended (app-only overload), not copied; a new external-plane entry point is needed because contacts have no systemuser and the external plane forbids OBO and requires the `ExternalCollaboration` group.
3. Cost of doing nothing — the C1 detail view cannot show a record's messages to partners or workforce contacts (G1), and submitters cannot read questions on their requests (FR-04).

**`IAppOnlyCommunicationQuery` + `DataverseAppOnlyCommunicationQuery` (new seam, DI registration)**
1. Existing — `IImpersonatedCommunicationQuery` (impersonated twin).
2. Extension — adding an app-only member would contradict that interface's "no app-only fallback" contract and break its nine hand-written doubles; the new type is a pass-through over the existing `DataverseWebApiService` GET.
3. Cost of doing nothing — the internal-only negatives (FR-04: a CIAM caller never sees an internal-only message) cannot be tested against the read service without it (ADR-038 bans `Mock<HttpMessageHandler>`). ADR-010 ceiling raised 158 → 159 with this justification.

**`DataverseWebApiService.RetrieveMultipleAppOnlyAsync` (new public method, `Spaarke.Dataverse`)**
1. Existing — `RetrieveMultipleImpersonatedAsync` (same shape, impersonated only).
2. Extension — it IS an extension: both public methods now share one private core.
3. Cost of doing nothing — no app-only row read with FormattedValue annotations exists for the read model to consume.

**`ExternalMessageDto`, `AppOnlyRegardingReadResult`** — response/result shapes for the new route and overload (the allow-list is the security property; `ThreadMessageDto` carries From/To, sender id, privilege and attachments that must not cross the external plane).

## Placement Justification (`.claude/constraints/bff-extensions.md`)

In the BFF, in the existing external-access endpoint group and the existing Communication read model. The external plane's authorization (CallerPrincipal, module registry) and the communication read model (parser, internal-only filter) both live in the BFF and are reused in-process; the route is a thin composition of the two. No new package, no background work, no new options or secret.

## Files

- `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/ExternalMessageEndpoints.cs` (new) — route, root table, record gate, projection.
- `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/Dtos/ExternalMessageDtos.cs` (new) — `ExternalMessageDto`.
- `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/ExternalAccessEndpoints.cs` — maps the route in the external group.
- `src/server/api/Sprk.Bff.Api/Services/Communication/IAppOnlyCommunicationQuery.cs` (new) — seam + pass-through.
- `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationThreadReadService.cs` — optional seam parameter + `ReadRegardingAppOnlyAsync`.
- `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationThreadReadModels.cs` — `AppOnlyRegardingReadResult`.
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/CommunicationModule.cs` — registers the seam (singleton).
- `src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs` — `RetrieveMultipleAppOnlyAsync`; shared private core.
- `tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/ExternalMessageEndpointsTests.cs` (new) — 32 test cases.
- `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs` — GovernedFiles entry, HandlerDecision, census 118 → 119 with history line.
- `tests/Spaarke.ArchTests/ADR010_DITests.cs` — 1:1 interface ceiling 158 → 159 with justification.
- `tests/integration/seam/Communication/CommunicationListThreadsSeamTests.cs`, `CommunicationWorkspaceReadSeamTests.cs` — pinned constructor arity 5 → 6, and the new parameter named as the only other sanctioned addition (the banned membership/grant seams are still asserted absent).
- `projects/.../notes/decisions.md` — two entries.

## Tests (`ExternalMessageEndpointsTests`, 32 cases)

Driven through `GetMessagesCoreAsync` → the PRODUCTION module registry (built by `AddExternalAccess`) → the real read service → the real `CommunicationAccessFilter`. Only the app-only query seam (Moq) and the gate's record read (a delegate) are doubled; the impersonated seam is a Strict mock, so any impersonated call fails the test.

| Criterion | Test(s) |
|---|---|
| Accessible record → its communications | `AccessibleProject_ReturnsItsThread_NewestLast_AppOnly_KeyedOnTheRecord`; `AccessibleMatterOrWorkAssignment_ReturnsItsThread` (3); `InvoiceUnderAnAccessibleMatter_ReturnsItsThread` |
| Outside the set → 403 (another party's record, nonexistent id, Tier-1-only caller) | `AnotherPartysProject_Is403_AndNoMessageIsRead`; `AnotherPartysInvoice_AndANonexistentInvoice_GetTheSame403`; `NonexistentRecord_Is403_NotA404` (5 roots); `TierOneOnlyCaller_WithNoRecordGrants_Is403_AndCausesNoDataverseRead` (4 roots) |
| CIAM and workforce contact never get internal-only | `CiamCaller_NeverReceivesAnInternalOnlyMessage`; `WorkforceContactCaller_NeverReceivesAnInternalOnlyMessage` (both also cover an unflagged row) |
| Systemuser gets internal-only | `SystemUserCaller_ReceivesInternalOnlyMessages` |
| Requester reads own service request; others 403 | `Requester_ReadsTheThreadOnTheirOwnServiceRequest`; `SomeoneElsesServiceRequest_Is403_AndNoMessageIsRead` |
| Unsupported root → 404 | `UnsupportedRoot_Is404_AndNothingIsRead` (documents, events, organizations, contacts, todos, api) |

Beyond the stated criteria (one line each):
- `ExternalLicensedSystemUser_DoesNotReceiveInternalOnlyMessages` — pins decision 6 (#675 parity), a security narrowing.
- `CiamRequester_PartnerSwitchOff_Is403_WithNoRead_AndSwitchOn_ReadsOwnThread` — the requester scope honours FR-06's switch (gotcha 2026-10-10: test both settings).
- `ARowRegardingAnotherRecord_IsDropped_EvenIfTheQueryReturnedIt` — the brief's "every returned row is re-checked".
- `AThreadPastTheReadBound_IsReturnedCutShort_AndFlaggedTruncated` — a cap is never silent (UAC task 105 convention).
- `ExternalMessageDto_SerializesOnlyTheAllowListedFields` — the brief's explicit column allow-list.

## Fail-before / seeding proofs

The route, the overload and the seam did not exist before this task, so the test file does not compile against the pre-task tree (every route segment under `/{root}/{id}/messages` was a routing 404). The proofs below perturb the new code in place (backup copy, run, restore, `diff -q` confirmed identical):

| Perturbation | Result |
|---|---|
| **Seeding proof (POML step 5):** internal-only filter neutralised — `IsInternalUser: isInternal` → `IsInternalUser: true` in `ReadRegardingAppOnlyAsync` | **3 failed / 29 passed**: `CiamCaller_NeverReceivesAnInternalOnlyMessage`, `WorkforceContactCaller_NeverReceivesAnInternalOnlyMessage`, `ExternalLicensedSystemUser_DoesNotReceiveInternalOnlyMessages` |
| Contact treated as systemuser — endpoint passes `principal.SystemUserId ?? principal.ContactId` | **2 failed / 30 passed**: the CIAM and workforce-contact internal-only tests |
| Record gate removed — `if (!await IsRootRecordReadableAsync(...))` → `if (id == Guid.Empty)` | **15 failed / 17 passed**: every 403 case (other party's project/invoice/service request, nonexistent ×5, Tier-1-only ×4, CIAM switch-off) plus the two cases asserting the gate's record read |
| Per-row re-check removed — `bounded.Where(regarding == id)` → `bounded` | **1 failed / 31 passed**: `ARowRegardingAnotherRecord_IsDropped_EvenIfTheQueryReturnedIt` |

Restored: **32 passed, 0 failed.**

## Build, tests, publish size

| Command | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | 0 warnings, 0 errors |
| `dotnet build tests/integration/Sprk.Bff.Api.IntegrationTests/` | 0 warnings, 0 errors |
| `dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~External"` | 2211 passed, 0 failed |
| `dotnet test tests/Spaarke.ArchTests/` | 940 passed, 0 failed |
| Seam tests (`CommunicationListThreadsSeamTests`, `CommunicationWorkspaceReadSeamTests`; compiled into the unit project) | 31 passed, 0 failed |
| Full unit suite `dotnet test tests/unit/Sprk.Bff.Api.Tests/` | 19511 passed, 0 failed, 54 skipped |
| `dotnet list ... package --vulnerable --include-transitive` (BFF) | no vulnerable packages; no package added |

**Publish size** (bff-extensions §10 bullet 4 procedure: `dotnet publish -c Release` → `Compress-Archive -CompressionLevel Optimal`, PDBs included). Fresh `git archive` export of origin/master `61c9e80a1` (src/server, config, Directory.*.props, global.json) built at `C:\tmp\m033`:

| Build | Zip bytes |
|---|---|
| origin/master 61c9e80a1 | 38,210,551 |
| this branch (R3 base 2ea2600a7 + task 033) | 38,227,425 |
| **Delta** | **+16,874 B (+0.02 MB)**, which covers the whole R3 branch against master, not only task 033 |

The total is ~36.5 MB, well under the 55 MB review and 60 MB stop thresholds.

## Known limits (not fixed here)

- (K1) A message filed under two roots (e.g. `sprk_regardingmatter` = M and `sprk_regardingproject` = P) is returned to a caller who can read either root — the same OR rule the documents and invoices modules apply. If P is Restricted and M is not, a caller of M sees it.
- (K2) Per-message Dataverse narrowing (a POA share narrower than the owning team) is not mirrored: the external read follows the data-model rule "a communication's access is decided by its parent root". Filed communications are owned by the root team (task 146), so no such narrowing is produced by current write paths; `sprk_isprivate` is display-only and has no server writer.
- (K3) `sendername` is null for messages without `sprk_sentby` (inbound email; contact-sent messages until task 034 adds the creator-contact name).
- (K4) The thread is capped at 500 messages per read (flagged `truncated`); no paging.
- (K5) The gate's app-only record read for invoices / service requests propagates a non-not-found Dataverse fault as a 500 (fail-closed, but not a 403).

## Review results and fixes (2026-10-10)

**Pass 1** found one fix-now defect (F1) and a K list. **Pass 2** (over the fix diff and its direct callers/callees) found no F-class finding; its K list is the "Known limits not fixed" section below.

### Fixed

| # | Finding | Fix | Tests (all in `ExternalMessageEndpointsTests.cs`) | Fail-before |
|---|---|---|---|---|
| F1 | The external read filtered only `statecode eq 0`, so Draft rows (statecode 0, full subject/body, written by `EmailDraftToolHandler`) reached external callers | `ReadRegardingAppOnlyAsync` selects `statuscode`, filters `(statuscode eq 659490002 or statuscode eq 659490003)` (Send, Delivered) and re-checks status per returned row (missing/unreadable status is dropped). App-only external path only; internal reads untouched. Set and rationale: `decisions.md` | `DraftDeletedRecalledAndOtherUnsentRows_AreDropped_ForEveryPrincipalKind` x4 (CIAM contact, workforce contact, external-licensed systemuser, internal systemuser: Draft, Deleted, Recalled, Queued, Failed, Bounded and no-status rows dropped; Send and Delivered/inbound rows returned); `AccessibleProject_...` asserts the status clause | Yes: 4 theory cases failed against the pre-fix service |
| K1 | The 500-row bound kept the OLDEST 500 and dropped the newest | Query `createdon desc, id desc`, `$top=501`, take 500, reverse in memory: the newest 500, oldest first; `truncated` unchanged | `AThreadPastTheReadBound_ReturnsTheNewest500_OldestFirst_AndFlagsTruncated` (501 rows: newest present, oldest absent, order oldest to newest) | Yes |
| Gate | A Dataverse fault in the record gate (invoice / service-request app-only read) escaped to the global handler | Gate call moved inside the handler's try: a fault is the coded `EXT_MESSAGES_INTERNAL_ERROR` 500 and reads no messages; `RecordNotFoundException` stays a 403 (no existence oracle) | `AFaultInTheRecordGate_IsTheEndpointsCoded500_AndNoMessageIsRead`; `ARecordNotFoundInTheGate_StaysA403_NotA500` (control) | Yes (fault test) |
| Tests | A forgotten `IsExternalAsync` setup could pass as internal; no serialized-response check; no route-mapped test | `ISystemUserIdentityResolver` mock is now strict; `TheSerializedResponse_CarriesNoAddressPrivilegeOrInternalOnlyFields` (no `sprk_from` / `sprk_to` / `_sprk_sentby_value` / privilege / internal-only by name or value); `ExternalMessageRouteTests` uses `EndpointTable.AssertMapped` for GET `/api/v1/external/{root}/{id}/messages` x5 roots, POST unmapped, authorization metadata present | as named | n/a (hardening; the strict mock made `SystemUserCaller_ReceivesInternalOnlyMessages` fail until its setup was explicit, which it has) |

Fail-before method: the pre-fix `CommunicationThreadReadService.cs` and `ExternalMessageEndpoints.cs` were put back from `HEAD`, the suite run (8 failed / 37 passed of 45), then the fixed files restored (45 passed). The test double for the app-only query now answers in the query's own order (`createdon` desc), as Dataverse would.

Supersedes the earlier K4 (cap now keeps the newest 500) and K5 (a gate fault is now the coded 500).

### Known limits not fixed

- Privilege classification is stripped from the external DTO and does not gate a read. **OWNER DECISION pending**: show it, mark the message, or hide privileged messages from external callers.
- A child-filed message (e.g. filed on an invoice) rides its root's thread (the data-model rule: a communication's access is decided by its parent root).
- A row with a null `sprk_isinternalonly` is hidden from every contact (fail closed). Support note: such a row is invisible externally until the flag is set.
- HTML bodies are NOT sanitised by the server. Task 034 must sanitise on write and task 051 on render.
- No throttling or retry on the Dataverse named client (parity with the existing app-only reads).
- The ADR-010 DI ceiling bump (for `IAppOnlyCommunicationQuery`) is that test's sanctioned mechanism.
