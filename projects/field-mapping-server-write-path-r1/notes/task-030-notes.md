# Task 030 notes: Office To Do and Office document save onto the unified create

Branch `task/fm-030`, cut from `dc08e14990` (the work branch: master, 046b and 049). Rigor FULL, opus at high effort.

**Status: stopped before any production change.** The document-save half needs design decisions the POML does not
answer, and the task as written is larger than one execution pass. This note records what was found and proposes a
split. No code, test or configuration was changed.

## 1. Document save: what the unified create cannot take today

The POML asks that `OfficeDocumentPersistence.CreateDocumentWithSpePointersAsync` (the save route) and
`UploadFinalizationWorker.CreateDocumentRecordAsync` (the worker: Email/Attachment parent, attachment children, the
fallback create) create through `RecordCreationService`, with the association set in the create. Five properties of
today's document create have no counterpart on `RecordCreationRequest`, or conflict with a rule the service enforces.
Line numbers are at `dc08e14990`; re-grep before use.

| # | Today's create does | The service | Effect |
|---|---|---|---|
| D1 | Takes the row's key from `preAssignedDocumentId`: the Word add-in has already stamped that id into the uploaded bytes (word-add-in-r1 FR-02, task 014). | Always mints a new key (`RecordCreationService.CreateAsync`, `var id = Guid.NewGuid()`). No request option names the key. | A Word save through the service would create a row whose id differs from the one stored in its file. |
| D2 | Takes the owner from the caller: the worker's carried `OwningTeamId`, so attachment children match their parent (`ResolveDocumentOwnerTeamAsync`); and the save's owner asked again with the editable canonical included (UAC 194b V-fix 3). | Decides the owner itself (`DecideOwnerAsync`, record-first). No request option pins an owner. | NFR-01 asks for the carried owner as an explicit service input. That needs a new owner input on the shared core, which is an owner-rule change (UAC-r2 asked to be consulted before any such change, `notes/w2-coordination.md`). |
| D3 | Writes the email narrowing in the same create (`ApplyEmailCommunicationOutcome`): Access Permission Restricted plus `sprk_accessinheritance` for an unidentified email (task 130/189), or plus `sprk_restrictedbysubmission` for the submission `.eml` (task 095). The invariant: "no document exists, even briefly, with neither". | `sprk_accessinheritance` is a `TableServerOwnedColumns` entry on `sprk_document` (`RecordTableRules.cs`), refused in any principal's payload at step 2. For a system principal, step 2 also refuses Access Permission and the marker (`InheritedAccessPermission.RefuseAppOnlyWrite`, `AppOnlyDocumentWriteReasonCode`). | An Email or Attachment document cannot be created through the service without breaking the task 130/189/095 invariant, unless the core gains a document-narrowing input. That is a UAC-r2 decision. |
| D4 | Writes the SPE pointer (`sprk_graphdriveid`, `sprk_graphitemid`, `sprk_filepath`) and the email-archive columns (`sprk_emailmessageid`, `sprk_isemailarchive`) in a follow-up update. | Asks, at step 5 for a caller principal, that no column of the payload is field-secured. `scripts/Set-DocumentPointerFieldSecurity.ps1` secures the pointer columns. | The pointer values are known at create time (the upload runs first), but a caller-checked create cannot carry them. The POML allows a pointer-only update only "if the pointer values cannot exist at create time", which is not the case here. Keeping the update needs an owner ruling on a different justification. |
| D5 | Is not mapped. | Refuses `Mapping` with a system principal (`ArgumentException`: the app-only metadata reader is task 033's). | The worker (system principal, no caller token) cannot apply field mapping, so FR-18's "mapping (if profiled)" cannot hold for worker-created documents until task 033 lands. |

### Options for the document half (owner or main session to choose)

- **(A) Association in the create, no service yet.** Put the `DocumentAssociationMap` lookup in the existing
  `CreateDocumentAsync` request in both creators (`CreateDocumentRequest` would need the four direct slots), and keep
  the update for the pointer and email columns only. This closes word gaps G1/G2: the association no longer depends
  on a second call. It does not route the save through the service, so the save gets no mapping and no service I-1
  derivation. Small, and touches no core file.
- **(B) Re-plan as 030b with core inputs.** Add to `RecordCreationRequest`: a caller-chosen key (D1); a pinned owner
  for a system create that carries one (D2, UAC-r2 review); and a document-narrowing input the service writes as its
  own (D3, UAC-r2 review). Decide D4's justification, and wait for task 033's system mapping reader (D5). Several
  sub-tasks, comparable to 018a/018b/018c.

## 2. Office To Do: what converting it involves (done in 030a: section 5)

This half is implementable without core changes, following the 018b quick-create adapter
(`OfficeService.QuickCreateViaCreationServiceAsync`):

- The request maps to a Web API payload: name, description, due date, priority and effort scores, the regarding
  lookup with its pair (`sprk_regardingrecordid`, `sprk_regardingrecordtype`), the record name, and the document and
  communication carriers. The caller is the principal (`RecordCreationPrincipal.ForCaller`).
- Assigned To: the requested contact goes in the caller's payload. The caller's linked contact becomes a
  `PostMappingDefaults` entry on the registry's for-person column (`sprk_assignedto`), as 018b does.
- Mapping uses `RecordCreationMapping.ForCaller` with no explicit source (inferred from the regarding pair).
- Deleted from `OfficeService`: `ResolveTodoOwnerTeamAsync`, the creator-person stamp, both `CoreAncestorResolver.StampAsync`
  calls, `ApplyInheritedAccessPermissionAsync` and the `_genericEntityService.CreateAsync` call. The service's steps 6½,
  7, 10 and 12 own the stamp, I-16, the owner and the creator person.
- Refusal map (NFR-07): no owner → 403 `OFFICE_022`; an AppendTo or not-found refusal on a regarding record or carrier →
  the `TodoSourceAccessFilter` deny body (403 `OFFICE_009`, `source_record_inaccessible`, its fixed detail); a stamp
  that cannot be derived → the existing 403 `OFFICE_010`; anything else → the existing failure. Matching the filter's
  body needs the endpoint's `SdapProblemException` catch to pass through a `reasonCode`, as the quick-create endpoint does.
- Test seam: `TodoRegardingTestWebAppFactory` captures `IDataverseService.CreateAsync`. Its users must move to
  `RecordCreationWriteCapture` plus `OfficeCallerDataverseDouble` scripted for `sprk_todo` (lookups, entity sets, the
  record-type catalog, the caller's AppendTo), as task 031 did for the event and analysis suites. Affected:
  `OfficeTodoRegardingContractTests` (12 test methods), `OfficeTodoSourceAuthorizationContractTests` (13), the
  `sprk_todo` section of `OfficeRecordOwnershipTests` (it asserts the owner request's target, which the service asks
  over the row's lookups), and the `RecordOwnerAssignmentCensusTests` rows `OfficeService.cs/CreateTodoAsync` (owner
  write) and `OfficeService.cs/ResolveTodoOwnerTeamAsync` (person-bearing writer).

This is an 018b-sized change on its own.

## 3. Proposed split

| Task | Scope | Depends on |
|---|---|---|
| 030a | Office To Do through the service (section 2), with the test-seam move. | none |
| 030b | Document save: option (A) now, or (B) after the owner and UAC-r2 decide D1–D4. | owner ruling; (B) also needs task 033 for D5 |

## 4. Gates

This line covers the 030b-only status: the stopped document-save half built nothing, published nothing and changed no test. Task 030a (section 5) did add and change tests, and its gate results are in section 5.

## 5. Task 030a: Office To Do through the unified create (done)

Coordinator go-ahead for 030a; 030b stays outside the service as the owner's named exception (decisions.md, owner direction
2026-11-06). 030a is the adapter only. It makes no service-core change, and the document save, new-version save
(`existingDocumentId` + `isNewVersion`) and re-file (`documentId`) paths are untouched.

### What changed

- `OfficeService.CreateTodoAsync` is a thin adapter over `RecordCreationService.CreateAsync`. It runs as the caller
  (`RecordCreationPrincipal.ForCaller`, WhoAmI under the caller's token) with the mapping source inferred
  (`RecordCreationMapping.ForCaller`).
  - The request becomes a Web API payload: name, state, scores, description, due date (`yyyy-MM-dd`), the regarding lookup
    with the ADR-024 pair and name, the best-effort record type, the document and email carriers, and the Assigned To contact.
  - With no assignee, the caller's linked contact is a post-mapping default on Assigned To, so a profile can still fill it.
    The `todo_assignee_unset` warning is logged when nothing was written.
  - The service derives the I-1 stamps, sets Access Permission, decides the owner (record first, then carriers), stamps the
    creator person and makes the one app-only create.
- `ResolveTodoOwnerTeamAsync`, the old entity build, its stamp calls, the `IGenericEntityService.CreateAsync` call, and the
  now-unused `CoreAncestorResolver` dependency are deleted.
- `TodoSourceAccessFilter.DeniedReasonCode`, `DeniedDetail` and `Deny` are now internal, so the route renders the service's
  refusal with the filter's own builder.
- The endpoint's `SdapProblemException` catch maps an `OFFICE_009` tagged `source_record_inaccessible` to `Deny`.

### Refusal map (NFR-07; the codes the pane maps are unchanged)

| Service refusal | Response |
|---|---|
| `NotFoundOrDenied` (no AppendTo on, or no such, record regarding / carrier / assignee) | 403 `OFFICE_009`, reasonCode `source_record_inaccessible`, the filter's one detail: the same body as the route filter's refusal (only `correlationId` differs) |
| `OwnerUnresolved` | 403 `OFFICE_022`, the same detail texts as before (record vs carrier-only) |
| any other (stamp underivable or unavailable, Forbidden, Invalid, Unavailable, ...) | 403 `OFFICE_010` (the route's generic refusal, as before) |

The To Do regarding check's uniform refusal is 403, not 404. The filter's remarks give the reason, and 030a keeps it. The
coordinator's brief said "uniform 404", so this was flagged.

### Tests

- New: `tests/integration/contract/Api/Office/OfficeTodoCreateThroughServiceContractTests.cs` (9 cases).
  - One app-only create through the service, none through `IGenericEntityService`; WhoAmI asked; the owner from the resolver.
  - A matter-to-To-Do profile fills an empty column, and the user's value wins.
  - Four uniform-body refusals compared byte for byte with the filter's own deny: a regarding record without AppendTo, a
    missing one, and the document, email and assignee without AppendTo (the last three are one parameterised case).
  - `OFFICE_022` and `OFFICE_010` are pinned.
- New host: `OfficeTodoRecordCreationHost.cs`. It is the caller's Dataverse (`OfficeCallerDataverseDouble`) seeded for
  `sprk_todo`, plus `RecordCreationWriteCapture` over the app-only writer.
- `OfficeCallerDataverseDouble` (shared, minimal) gains `OpenTables`: an unlisted row in an open table exists with full
  rights unless listed in `NoAppendTo` / `Missing`. The To Do suites name fresh ids per test.
- Existing suites converted, factories only, with test bodies unchanged:
  - `OfficeTodoRegardingContractTests` (`TodoRegardingTestWebAppFactory`) and
    `OfficeTodoSourceAuthorizationContractTests` (`TodoSourceAccessTestWebAppFactory`).
  - `CreatedEntities` is now the service's create viewed as the SDK entity, because the capture seam moved.
- `OfficeRecordOwnershipTests.CreateTodo_WithARecordAndADocument_...`: the requester and the creator person are now asserted as
  the WhoAmI caller, with the oid carried as `CallerObjectId`. The seam moved, as in task 031 (ADR-028).
- Failing first: with the old `OfficeService`, `OfficeEndpoints` and `TodoSourceAccessFilter` restored, 7 of the 9 new cases
  fail. The 2 that pass are the `OFFICE_022` / `OFFICE_010` pins, which are code-preservation pins.
- ArchTests:
  - `RecordOwnerAssignmentCensusTests`: the `OfficeService.cs` `sprk_todo` create row, the `CreateTodoAsync` owner-write row and
    the `ResolveTodoOwnerTeamAsync` person-bearing row are removed with comments.
  - `FeatureRolePrivilegeGuardTests`: Office To Do rows added for what the service now asks as the caller: Append on
    `sprk_todo`, AppendTo (Deep) on matter, project, invoice, document, communication and contact, and AppendTo on the record
    type. The `OfficeService.cs` line citations moved by the six deleted lines.
  - `DATAVERSE-WRITE-PATH-ARCHITECTURE.md`'s Office To Do matrix row was updated to match.

### Section 11

No new production surface: no endpoint, service, DI registration, package or column. Three `TodoSourceAccessFilter` members
widened from private to internal, so the route reuses the one refusal body instead of copying it. Test-only additions are the
host file and the double's `OpenTables`.

### Known limits

- A To Do filed only under a document or email carrier reads no mapping source: no profile applies, as on the events route.
- A requested assignee the caller may not AppendTo is now refused with the uniform body. Before, only Read was asked.
- A refusal of kind Forbidden, Invalid or Unavailable is the generic `OFFICE_010`, as every other failure was before.
- A caller whose own contact they cannot AppendTo gets the To Do without an assignee; the response carries no warning. The server
  logs `todo_assignee_unset` with reason `caller_contact_not_appendable` and the service's own mapping warning; the reason
  `caller_has_no_linked_contact` is logged only when the caller has no linked contact.
- `OFFICE_010` now also covers `Upstream` (a Dataverse 429 or 5xx during the caller's checks) and `PrincipalUnresolved` (the
  caller's token exchange failed), as well as an underivable stamp. Before, these did not arise on this route: the caller's
  rights were checked only by the route filter.
- AppendTo is now asked on the communication or document the To Do carries, not only on the assignee (a carrier the caller
  cannot AppendTo gets the uniform 403 `OFFICE_009`).
- A field-mapping profile that fills Assigned To outranks the caller's own contact. Order: the requested contact, else the
  mapped value, else the caller's linked contact. Owner-confirmed 2026-11-06. Pinned by
  `Post_Todo_NoAssignee_AMappedAssignedTo_BeatsTheCallersOwnContact` and
  `Post_Todo_AnExplicitAssignee_BeatsTheMappedValue_AndTheCallersOwnContact`.

### Fix round (review of 030a)

- F2: the unset-assignee log no longer says the caller has no linked contact when a contact was offered but the service did not
  write it.
- F4: `IOfficeService.CreateTodoAsync` documentation, the architecture-doc line citations and the stale comments on
  `TodoSourceAccessFilter` and `OfficeEndpoints` now describe the create service as the creator; section 4 above is corrected.
- K3: host cases for the typed record-type pair (`MatterTypeId`) and for a caller who lacks the record-type Read or AppendTo (the
  uniform 403); `FeatureRolePrivilegeGuardTests` now lists Read on the record type for the Office To Do.
- The route filter's refusal detail also advises choosing a different person to assign the To Do to, so it covers the assignee
  case without naming any record. It is still one string for every denial; status and codes are unchanged.

rework: none task 030

### Fix round 2 (OFFICE_010 transient text)

- Problem: `OfficeService.MapTodoRefusal` returned null for every refusal except `OwnerUnresolved` and `NotFoundOrDenied`, so a
  transient failure got the route's generic 403 OFFICE_010 "Failed to create the To Do.", with no hint to retry.
- Now mapped to an `SdapProblemException` that keeps errorCode OFFICE_010, status 403 and the existing title
  ("Cannot Create Entity"), with a fixed "try again" detail (`OfficeService.TransientDetail`): `Unavailable` (a check or the
  owner decision faulted, including the I-1 stamp read), `Undecided`, and `Upstream` whose status is 429, 503 or 504. The
  upstream status is `RecordCreationProblemMap.StatusOf(refusal)` (from `ClientFailure.StatusCode`; no text is parsed), so a raw
  408 (as 504) or another 5xx / a call that never left the BFF (as 503) is transient here exactly as it is for the HTTP adapters.
- Unchanged (route's generic path, null): Forbidden, BlockedColumn, Invalid, PrincipalUnresolved, Conflict, Upstream 400/403/404.
- `Undecided` with a provisioning or secure-create code: the quick-create mapper passes those through under their own code at 500.
  The To Do route has one code and a fixed contract, so every `Undecided` says to try again and, if it keeps failing, to contact
  an administrator. The code stays in the log line. The detail carries no exception text, record id or service reason.
- Tests: `OfficeTodoRefusalMapTests` (map table, the pinned detail, the non-transient controls) and two cases in
  `OfficeTodoCreateThroughServiceContractTests` (a faulted stamp read gives the try-again detail; no create right gives the
  generic detail). With the mapping arms removed, 10 failed (9 map rows, 1 contract); the pin and the controls passed, as they
  describe unchanged behaviour.
