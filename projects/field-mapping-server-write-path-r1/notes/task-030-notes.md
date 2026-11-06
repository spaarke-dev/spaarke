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

## 2. Office To Do: what converting it involves (not started)

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

Nothing was built or published, so there is no publish-size delta and no CVE result. No test was added or changed.

rework: none task 030
