# Task 026: Signal and Decision Record invoice-line subject

Completed 2026-10-16; fix-round-1 2026-10-17. Rigor FULL. uac-r2 decision D-94 (option A): `sprk_billingevent` (the Invoice Line) is an intermediate stamp root.

## Commits

| SHA | Subject |
|---|---|
| 33ad6c984 | feat: invoice-line subject lookup and firm reason schema script |
| a49fa8699 | feat: file Signals and Decision Records under an invoice line |
| b8e4c019a | feat: SpaarkeMaster source for the invoice-line subject and firm reason |
| b5f4a4c78 | test: invoice-line subject derivation, refusal, restamp and storage |
| 68fcc3cff | docs: task 026 notes and completion |

The diff range of the task is `ef3efce73..b5f4a4c78`.

Fix-round-1:

| SHA | Item | Subject |
|---|---|---|
| 484d76300 | 1 | fix: refusal kinds; a two-root line Signal is CoreRecordAmbiguous |
| 387f2eb60 | 3a | fix: a Decision Record's core must be its invoice-family subject's own |
| 64136db3d | 3b | fix: a line belongs only to an invoice of its own core record |
| 3525050bd | 6 | test: the intermediate-root lockstep is order-sensitive |
| 3e97b2984 | 2 | feat: field-secure the Signal firm reason (D-95(2)) |
| (this commit) | 4, 5 | docs: notes, deploy order, project CLAUDE.md, rework line |

## Live changes (spaarkedev1)

All changes are additive. There were no imports, no user role or team changes and no PublishAllXml.

### Task 026 (2026-10-16, about 23:40Z)

`scripts/ebilling/Deploy-EbillingSignalLineSubjectSchema.ps1` adds the components to the SpaarkeMaster solution.

| Run | Result |
|---|---|
| WhatIf | 4 would-create |
| Apply | 4 created, published (entity-scoped PublishXml for sprk_signal and sprk_decisionrecord only) |
| Rerun | 0 created, 4 skipped, no publish |

The four components:
- The relationship `sprk_signal_reginvoiceline` (sprk_signal.sprk_regardinginvoiceline → sprk_billingevent). Delete and Archive are RemoveLink; everything else is NoCascade.
- The relationship `sprk_decisionrecord_reginvoiceline`, with the same shape.
- The column `sprk_signal.sprk_firmreason`: Memo, MaxLength 2000.
- A catalog row in `sprk_recordtype_ref`, "Invoice Line" (INVL), with id `fb30d221-bbc9-f111-a05a-7c1e520a989f`, regarding field `sprk_regardinginvoiceline` and display-name field `sprk_name`.

### Fix-round-1, item 2: firm reason field security (owner D-95(2))

`scripts/ebilling/Set-EbillingFirmReasonFieldSecurity.ps1`:

| Run | Result |
|---|---|
| WhatIf | all preconditions OK; would create the profile, add its member, secure the column and grant both profiles |
| Write | created the profile `Spaarke Ontology Writer Firm Reason Writers` (`ec323a05-f95c-4308-aac5-6d899032691e`) in SpaarkeMaster; added `# mi-ontology-writer-dev` (appId 69040982-…) as its only member; secured `sprk_signal.sprk_firmreason`; granted `Spaarke BFF-Managed Field Readers` read=4 and the new profile 4/4/4; masked window 15.2 s; published sprk_signal |
| Rerun | everything OK, nothing written, no publish |
| -Verify | PASS, exit 0 |

- **Who reads it.** Every user reads it through `Spaarke BFF-Managed Field Readers`, which is already on all 6 business-unit default teams (checked live; the script FAILs and writes nothing if one is missing, because adding it would be a team change). No team or role assignment was made.
- **Other identities.** The writer MI is also on the `Spaarke` default team. The BFF application users (`# mi-bff-api-dev`, `SDAP-BFF-SPE-API`, `# spaarke-bff-api-prod`) are System Administrators and bypass field security. No code reads or writes the column yet (task 130 will), and 0 Signals carried a value.
- **Why a new profile.** Ontology's D-136 profile `Spaarke Ontology Writer Field Writers` has the same member, but its `-Verify` (`scripts/Set-ChildRecordCreatorPersonSchema.ps1`) FAILs on any row outside `sprk_createdbyperson`. That `-Verify` was re-run after this change and still PASSes.
  - Existing: the D-136 profile.
  - Extension: extending it would break ontology's gate.
  - Cost of doing nothing: either no non-administrator identity could write the firm reason, or any Signal writer could change what a firm is told.
- **Solution source.** SpaarkeMaster carries `IsSecured=1` on the column, the reader's row, the new profile and its root component. `FirmReasonFieldSecurityAgreementTests` pins the script against the source.

## D-94 conditions

1. **Live metadata read.** `EntityDefinitions(sprk_billingevent)/ManyToOneRelationships` was read on 2026-10-16 and is cited in the comments in all three tables.
   - Core lookups: `sprk_matter`→sprk_matter, `sprk_project`→sprk_project, `sprk_regardingworkassignment`→sprk_workassignment.
   - Excluded on purpose: `sprk_invoice` (D-75) and `sprk_resolvedtimekeeper` (a party).
2. **Restamp on re-file, and reconciler coverage.**
   - `CoreAncestorRestamper.AfterWriteAsync` is generic over `IntermediateRootColumns`. StampSourceColumns for sprk_signal and sprk_decisionrecord include `("sprk_regardinginvoiceline", "sprk_billingevent")`. They are pinned in `OntologyStampedChildRestampTests.Registration_*` and `CoreAncestorResolverTests`.
   - Corrected in fix-round-1. These are the BFF paths that can change a line's core lookups, checked against the code on 2026-10-17. Each one calls `AfterWriteAsync` after its write.

     | Path | Writes a line's core lookups? | AfterWriteAsync | FR-05 guard |
     |---|---|---|---|
     | `POST /api/v1/child-records/sprk_billingevent` (ChildRecordEndpoints.CreateAsync) | on create only | `ChildRecordEndpoints.cs:298` | yes (OwnedChildWrite) |
     | `PATCH /api/v1/child-records/{table}/{id}` | **no**: `RefileTables` (`:97-100`) leaves out sprk_billingevent, so the route answers 400 `child_record.table_unsupported` | (`:546` is this route; it never runs for a line) | — |
     | chat `dataverse.update_record` (DataverseUpdateRecordHandler) | yes | `DataverseUpdateRecordHandler.cs:336` | yes (OwnedChildWrite.RefileAsync, before anything else) |
     | playbook update (UpdateRecordActionCore, ActionSeam) | yes (rendered lookups) | `UpdateRecordActionCore.cs:251` | **no** |
     | output orchestrator (DataverseUpdateHandler) | yes (EntityReference fields) | `DataverseUpdateHandler.cs:95` | **no** |
     | field-mapping push (FieldMappingEndpoints) | yes (a mapping rule onto a line) | `FieldMappingEndpoints.cs:1068` | **no** |

     Our reviewer was right. ontology-r1's `:298/:546` reading holds only for the create (`:298`): `:546` is the re-file route, which refuses a line. A line edited on its model-driven form reaches its Signal and DR rows only through the reconciliation job (K3).
   - Tests:
     - re-filing a line matter → project re-stamps the row and leaves the decision context byte-identical;
     - a two-root line stamps nothing;
     - the reconciliation job finds a stale line row (report-only by default, repaired when released).
3. **Must-fire tests, and writer refusal on NoAncestor.**
   - A line with no core is refused: CoreRecordUnresolved in SignalWriter, SubjectUnfileable in DecisionRecordWriter.
   - A line with two roots is CoreRecordAmbiguous in SignalWriter (fix-round-1 item 1) and SubjectUnfileable in DecisionRecordWriter.
   - An invoice with two cores is CoreRecordAmbiguous or SubjectUnfileable, never resolved matter-over-project.
   - A line's invoice is never read for its root.
4. **Storage resolver.**
   - A line of a secure matter or a secure project takes that root's container.
   - A two-root line gets 409 `container_ancestor_unresolved`, with no business-unit or fallback container, before any root read.
   - A to-do whose pair alone names a line is `container_ancestor_unverifiable`.
   - **Deviation, accepted by uac-r2 as meeting condition 4:**
     - A document or to-do regarding a line in a secure matter gets 409 `container_ancestor_unverifiable`. It fails closed, with no container and no business-unit fallback.
     - This is unreachable today, because no upload or storage route takes a line as its parent (project CLAUDE.md gotcha).
     - It becomes a carrier (`sprk_invoice`) in uac-r2 #2047.
5. **Jest.** The suite is `PolymorphicResolverService.coreAncestor.test.ts`. The TS mirror derives a line's project and never selects `_sprk_invoice_value`. Both suites: 83/83 pass.

## Fix-round-1

The coordinator merged three reviews into six items.

1. **Refusal kinds.**
   - `CoreAncestorResult` / `CoreAncestorStampOutcome` carry `RefusalKind` (Transient, NotFound, MultipleRoots). The property is additive: Status, Stamps, Error and IsPermanent are unchanged for every caller. `Refusals_CarryTheirKind_AndTheSamePermanenceAsBefore` and `Successes_CarryNoRefusalKind` cover every existing intermediate and core table.
   - SignalWriter maps only a permanent MultipleRoots on an invoice-family subject to CoreRecordAmbiguous. NotFound and Transient stay CoreRecordUnresolved.
   - A two-root line and a two-core invoice now give the same reason (this was K4).
2. **Firm reason field security.** See Live changes.
3. **Line core.**
   - (a) DecisionRecordWriter refuses SubjectUnfileable when an invoice or line subject's `request.Core` is not its single derived core.
   - (b) FR-05 / task 013 already refused a line naming no core or more than one, but nothing compared a line's core with its invoice's. `InvoiceFamilyCoreRecordGuard.CheckWriteAsync` now refuses `invoice_family_line_invoice_core_record_mismatch` when a line names an invoice of another core record, or of none or several. An unreadable invoice fails closed with `read_failed` or `record_not_found`.
     - `sprk_invoice` is now a guarded lookup of the line, so an update that touches it is checked.
     - D-75 holds: the invoice is compared, never the source of the line's core. `ColumnsToRead`, the stored-row twin, is unchanged.
     - The write core reads a stored line's invoice from `sprk_invoices`.
     - This covers the child-record create route and the chat create and update tools. The three unguarded paths in the condition 2 table are a pre-existing FR-05 coverage gap (ISS-010 scoped the guard to the write core). They are reported to the coordinator, not fixed here.
4. These notes.
5. **Deploy order.**
   - From task 026 on, the BFF's Signal and Decision Record reads `$select` `sprk_regardinginvoiceline`: SecureChildLineage and the restamp / reconciliation stamp sources.
   - A BFF deployed where that column does not exist answers 400 on every Signal and Decision Record read.
   - Task 053 now has a binding step 5 (schema verified before the BFF; elsewhere the SpaarkeMaster import comes first, then the firm reason script), and DEF-021 has the same line.
6. **Lockstep order.** `IntermediateRootColumns_MatchTheTypeScriptSide` uses `WithStrictOrdering`. Swapping two TS rows fails it.

## Fix-round-2

Source: the ontology-r1 and uac-r2 re-checks of fix-round-1, plus DEF-022.

1. **D-96: an invoice re-file keeps its lines** (owner; uac-r2 F1). Commit 553bc6f41.
   - An update that writes an invoice's core lookup (`sprk_matter`, `sprk_project`, `sprk_regardingworkassignment`) is refused with `invoice_family_line_invoice_core_record_mismatch` when any of its lines names another core record, or none.
   - The check is one bounded query run as the app, through the existing `IGenericEntityService`: `sprk_billingevent`, `sprk_invoice` = id, own column ≠ new core or empty, or another core column set; top 1; id only. A line the caller cannot see blocks too. The refusal names no line.
   - A read fault, or a path with no app-only reader, fails closed with `read_failed`. A create has no lines, so it is not checked. A supersedes-only update does not count lines.
   - Where it is wired:
     - `InvoiceFamilyCoreRecordGuard.CheckWriteAsync` takes the line check as an optional argument. The guard owns `LinesOfAnotherCoreRecord`, its query and `WritesCoreLookup`.
     - `OwnedChildWrite.CheckInvoiceFamilyAsync` and `RefileAsync` take an optional `IGenericEntityService`.
     - `PATCH /api/v1/child-records/{table}/{id}` and the chat update tool pass it.
     - The event and communication filing routes write no invoice, so they pass none.
   - D-75 holds: lines are never restamped from their invoice.
   - The ChildRefile matter → project test now has one line already on the project.
2. **DEF-022 (#2056).** Commit 553bc6f41.
   - `InvoiceFamilyCoreRecordGuard.CheckGenericUpdateAsync` runs BEFORE the write on three paths:
     - `UpdateRecordActionCore`: the `IGenericEntityService` comes from its scope factory.
     - `DataverseUpdateHandler`: its own `IGenericEntityService`.
     - the field-mapping push: the endpoint's `IGenericEntityService`, passed to `ApplyMappingsToChildRecordsAsync`.
   - It runs when the target is `sprk_invoice` or `sprk_billingevent` and the patch sets or clears a guarded lookup.
   - The stored lookups are read as the app and overlaid with the patch. A patch value can be an `EntityReference`, a null, or a `{nav}@odata.bind` → `/{set}({id})`; anything else is `lookup_not_a_core_record`. The D-96 line check is included.
   - Refusals carry the guard's code:
     - The two throwing paths throw `RecordOwnerUnresolvedException` with the guard's code, which ActionSeam and the node executor already handle.
     - The push records a code-only error: the guard's reason can name records the caller has not read.
   - Write mechanics are unchanged. No DI registration was added (ADR-010 ceiling untouched; ArchTests green).
3. **uac-r2 K4.** Commit 6951d67ee.
   - `CoreAncestorResolver.StampFailureOf` was extracted from `DeriveAndProbeAsync`. It now reads `result.RefusalKind ?? (result.IsPermanent ? null : Transient)`.
   - A permanent error built with the public constructor is no longer labelled Transient.
4. **053 step 5 (ontology-r1 K3 / K1).** Commit 5734d5ca9.
   - The step now reads "write run, then -Verify". On a fresh environment, -Verify passes only after the write run.
   - -Verify's existing (p2) check must show "Spaarke BFF-Managed Field Readers" on every business unit's default team. The script already runs (p2) in every mode and fails on a missing team, so the script is unchanged.
5. **Task 130 constraint.** Commit 5734d5ca9. The firm-reason projection reads `sprk_signal.sprk_firmreason` as the BFF / app identity. A caller-identity read without the reader profile silently returns null.

**Mutation proofs.** Each mutation was applied to the source, then the targeted classes were run, then the source was restored.

| Mutation | Tests failing |
|---|---|
| Line check skipped in the guard | 9: 3 guard unit tests, 6 route / tool tests |
| Line check not passed by `OwnedChildWrite` | 6 route / tool tests |
| Guard call skipped on all three DEF-022 paths | 3: one refusal test per path |
| K4 expression reverted | 1 |

**Counts.**

| Check | Result |
|---|---|
| Targeted classes (new and touched) | 339 / 339 |
| Brief filter (Invoice, OwnedChild, CoreAncestor, RecordOwnership, FieldMapping, UpdateRecord, DataverseUpdate, ChildRecord, Signal) | 4,290 passed, 6 skipped, 0 failed |
| Spaarke.ArchTests | 1,126 / 1,126 |
| BFF build (-m:1) | green; no warnings in changed files |
| Publish size (linux-x64, Compress-Archive, 192 files each side) | 16c76e7d8 → 553bc6f41: 39,462,110 → 39,467,379 B (+5,269 B); total 39.5 MB |

**Re-check verdicts (on fix-round-1).**
- ontology-r1: CLEAN. The separate firm-reason writer profile is confirmed.
- uac-r2: no F-class finding in the changed code. F1 is item 1 here. K1 is accepted. K4 is item 3 here.

**Known limits added.**
- The DEF-022 guard covers `sprk_invoice` and `sprk_billingevent`, as briefed. The other four family tables (submission, approval, external identifier, correction) are not checked on those three paths. No playbook or mapping targets them today.
- Resolved in fix-round-2b: on the two throwing paths, the refusal's message started "No owner could be resolved" (the reused `RecordOwnerUnresolvedException`). See below.
- Resolved in fix-round-2b: on the generic paths, the other invoice (superseded, or a line's) is read as the app, so the guard's reason could name its core record to a caller who cannot read it. See below.

**Fix-round-2b** (coordinator, §8.5: the disclosure is on a real path, so it cannot stay a known limit). Commit 386651e2f.
- **No ids in refusals.**
  - `UpdateRecordActionCore` and `DataverseUpdateHandler` now throw `InvoiceFamilyCoreRecordGuard.RefusalException`.
  - Its message is the stable code plus a fixed sentence: "The invoice-family core-record rule refused this write ({code}); nothing was written." It never includes the reason.
  - The field-mapping push uses the same sentence.
  - The child-record / `OwnedChildWrite` path (and the chat update tool, which runs through it) is unchanged. Its reasons name only ids from the caller's own payload or from rows read as the caller. The D-96 line check, the only read made as the app there, names no line, only the core record the caller's own write names.
- **Accurate message.**
  - `RecordOwnerUnresolvedException` gains an `(entity, code, message)` constructor that uses the message verbatim, so ActionSeam's existing catch (`{code}: {message}`) and the node executor surface it unchanged.
  - The type is sealed, so a derived exception was not possible. No catch site and no DI changed.
- **Tests.**
  - For both paths and both mismatch cases (superseded invoice, line's invoice), the exception message and reason contain no GUID, contain the code, and do not start "No owner could be resolved".
  - The two-core cases and the push assert the same.
  - The child-record hidden-line refusal names only the caller's target project.
- **Mutation proof.** With the guard's reason put back into the two throwing paths, 6 tests fail: the 4 mismatch cases and the 2 two-core cases.
- **Counts.**
  - Targeted classes (generic paths, `SecureChildOwnershipAiToolTests`, guard): 451 / 451.
  - Brief filter: 4,294 passed, 6 skipped, 0 failed.
  - ArchTests: 1,126 / 1,126.

## #2081 follow-up

Source: GitHub #2081 (split out by uac-r2 from this task's review: the invoice-family half). Commits `dcbf7edc1`, `c528cc6d0`, `c99f0cb10` on top of `e1501dbbc`.

**The defect.** `UpdateRecordFieldsAsync` PATCHes with no `If-Match`, so it is an upsert. A generic path given a stale or deleted `sprk_invoice` / `sprk_billingevent` id, with a patch that touched no guarded lookup (`change.Count == 0` returned null), created an orphan row: no core record (FR-05), owned by the app (UAC D-11), silent.

| # | Item | Change | Fails-before evidence |
|---|---|---|---|
| 1 | Orphan upserts | `CheckGenericUpdateAsync` reads the stored row (app-only, by id) for EVERY write of an invoice or line, before looking at the patch. Missing (null or object-does-not-exist) → `invoice_family_record_not_found`. No reader, or a read fault → `invoice_family_read_failed`. A row that exists with a patch touching no guarded lookup is allowed, after one read | 3 paths × 2 tables × null/fault × lookup/text: the 12 text-patch cases fail before; read-fault and no-reader cases fail before for text patches |
| 2 | `DecisionRouteCores.UpdateChildAsync` | passes `appOnlyEntities: IGenericEntityService` from `RequestServices`, as `ChildRecordEndpoints.RefileAsync` and the chat update tool do. Why they pass it: D-96 counts an invoice's lines as the app; without it an invoice core-lookup move fails closed `read_failed` | reverting only this file fails 2 of the 3 decision-route tests |
| 3 | D-96 end to end | tests through the three DEF-022 paths (playbook update, output orchestrator, field-mapping push) and the decision route: an invoice moved while a line stays behind is refused; moved with all lines on it is written; a line moved while attached to its invoice is refused | the refusal-with-hint cases fail before (no hint); the decision route cases fail before |
| 4 | No `IGenericEntityService` | `UpdateRecordActionCore` with an empty service scope: refused `read_failed` with the exact text, nothing written, for a rename or a lookup write on both tables. A table outside the family still writes | the rename cases fail before (they were written unread) |
| 5 | Detach, move, re-attach | the `invoice_family_line_invoice_core_record_mismatch` refusal carries a fixed hint, in the guard's reason and in `CodeOnlyRefusalMessage` (the generic paths' text) | hint cases fail before |
| 6 | `ActionSeam` duplicated code | the code leads the message only when the message does not already carry it. Exact message pinned in `ActionSeamTests` | both variants fail before |

**What "the three generic paths" means here.** The brief's wording ("create / update / upsert record actions and the decision route") does not match a create or upsert core: there is none. The paths this note's fix-round-2 named are the three PATCH paths (`UpdateRecordActionCore`, `DataverseUpdateHandler`, the field-mapping push); the decision route is the fourth, now covered separately.

**The hint (item 5).** Text: "To move an invoice that has lines: detach each line (clear its invoice), move the invoice, move each line to the same matter, project or work assignment, then re-attach the lines to the invoice."
- Source: this note's fix-round-1/2 do not state a sequence. It is derived from D-75 and the guard: a line with no invoice is decided on its own lookup alone, so detaching is allowed; while attached, neither side can move first (the invoice is blocked by D-96, the line by the line/invoice comparison). The generic-path test `D96_ALineDetachedFromItsInvoice_AndMovedInOneWrite_IsWritten` proves the detach-and-move step is accepted.
- The code stays `invoice_family_line_invoice_core_record_mismatch`. The text names no record and no user data. It is also appended to the line-side reason (a line pointed at an invoice of another core record), where the same sequence applies.
- Not checked live (no live Dataverse writes): that a caller can clear a line's invoice on the form or in a chat tool. BFF only; no UI text was added.

**Test changes that follow from item 1.**
- `…NoCoreLookup_IsWritten_AndReadsNothingForTheGuard` (handler, playbook core) → `…AfterTheGuardConfirmsTheRowExists`: they now assert one stored-row read and no line query.
- The guard unit test "a patch touching no guarded lookup is not checked" is split: another table stays unchecked; an invoice or line is read once.
- `FieldMappingPushAuthorizationContractTests`: `PushHost` answers the pushed children's stored rows (they come from the caller's own query, so they exist).

**Behaviour change to know about.** One extra app-only `Retrieve` per invoice or line written through these three paths (a rename used to read nothing). `ActionSeam`'s message for an owner refusal also lost its duplicate code prefix ("No owner could be resolved … (code)").

**Still open from #2081 (not this note's):** the general update-only fix for every other table on `UpdateRecordActionCore` and `DataverseUpdateHandler`, and the four other family tables (submission, approval, external identifier, correction), which the guard does not cover on these paths. `SignalEvaluationService.cs:256` was checked: it is a deliberate create-by-upsert of a Signal with an id derived from (matter, signal type), never from a caller's id, so it cannot resurrect a stale id.

**Counts.**

| Check | Result |
|---|---|
| Fail-before (new and changed tests against the unfixed source) | 46 failed of 575 in the focused filter |
| Focused classes (InvoiceFamily, SecureChildOwnershipAiToolTests, ActionSeamTests, DecisionRoute) | 575 / 575 |
| Wide filter (Invoice, OwnedChild, CoreAncestor, RecordOwnership, FieldMapping, UpdateRecord, DataverseUpdate, ChildRecord, Signal, ActionSeam, DecisionRoute, DecisionAction, SecureChild, BillingEvent, ApplySeam) | 5,138 passed, 6 skipped, 0 failed |
| Spaarke.ArchTests | 1,175 / 1,175 |
| BFF build | 0 warnings, 0 errors |
| Publish size (Release, framework-dependent linux-x64, `Compress-Archive -CompressionLevel Optimal` over `api-publish/*`, PDBs included, 192 files each side, fresh worktrees `C:\wt2081m` / `C:\wt2081b`) | `e1501dbbc` 39,598,563 B → `c99f0cb10` 39,598,912 B (+349 B) |

The baseline is this branch's base commit, not master: the change is the only difference, so the delta is this task's alone. No package was added, so no CVE re-check was needed.

### #2081 fix round 2

Source: uac-r2's re-check of the follow-up (every claim verified; three items). Commits `98a964515`, `490d74259` on top of `ca40c9ba0`. Supersedes the hint wording above.

| # | Item | Change | Mutation proof (the change undone, the tests run) |
|---|---|---|---|
| 1 | Check-then-act | `IFieldMappingDataverseService.UpdateExistingRecordFieldsAsync(entity, id, fields, Guid? impersonateSystemUserId, ct)`: `If-Match: *` plus `MSCRMCallerID`; 404 or 412 is `KeyNotFoundException`; an empty caller id is refused before anything is sent. An overload, not an optional parameter, because existing tests set the method up in expression trees. `InvoiceFamilyCoreRecordGuard.UpdateExistingAsync` maps a vanished row to `invoice_family_record_not_found`. The playbook update, the output orchestrator (simple and optimistic paths) and the push write `sprk_invoice` / `sprk_billingevent` through it; every other table keeps its upsert | call sites reverted: 31 fail. Overload ignoring the caller: 2 fail (header, empty id) |
| 2 | Existence oracle | `CheckGenericUpdateAsync(..., writeIsImpersonated)`: when true, a not-found row (or other invoice) answers `invoice_family_read_failed`. The write-time vanish follows the same flag. The playbook update passes `ImpersonateSystemUserId is not null`. The push keeps `not_found` (ids come from the caller's own query) | mapping disabled: 13 fail. Write-time flag ignored: 3 fail |
| 3 | Hint wording | "To move an invoice that has lines, or a line of one, detach each line (clear its invoice), move the invoice, move each line to the same matter, project or work assignment, then re-attach the lines to the invoice." Pinned in the guard, the generic paths, the decision route and `CodeOnlyRefusalMessage` tests | old wording restored: 10 fail |

**Deviation (item 2).** The brief lists the output orchestrator among the impersonated writes. It is not: `DataverseUpdateHandler` writes app-only (no `impersonateSystemUserId` anywhere in it), so no access check follows its read and `not_found` hides nothing. It keeps `not_found`. The rule is the flag, so if the orchestrator ever impersonates, it passes `writeIsImpersonated: true`. The playbook update impersonates only when its input carries a caller (the Job B apply seam); the executor path is app-only and keeps `not_found`.

**Still true after this round (known limit).** The D-96 mismatch and multiple-core codes are returned on the impersonated path too. They are decided from rows the app read, so they tell an impersonated caller something about a row they may not read (that its lines sit on another core). D-96 was accepted by the owner on that basis (uniform code, no line named); the hint adds no id.

**Counts.**

| Check | Result |
|---|---|
| Focused (InvoiceFamily, SecureChildOwnershipAiToolTests, ActionSeamTests, DecisionRoute, FieldMapping, RecalculateWriteNoCreate) | 751 / 751 |
| Wide filter (round 1's list plus RecalculateWriteNoCreate, Rollup, Scorecard, Finance, ExternalAccess) | 8,942 passed, 9 skipped, 0 failed |
| Spaarke.ArchTests | 1,175 / 1,175 |
| BFF build | 0 warnings, 0 errors |
| Publish size (same convention as above, fresh worktrees `C:\wt2081m` / `C:\wt2081b`, 192 files each side) | `ca40c9ba0` 39,600,343 B → `490d74259` 39,601,810 B (+1,467 B) |

### #2081 round 3

The update-only wire was unpinned: every guard test mocks `IFieldMappingDataverseService`, so removing `If-Match: *` or the impersonation argument from `DataverseWebApiService.UpdateExistingRecordFieldsAsync` (impersonation overload) passed everything. `DataverseWebApiServiceImpersonationTests` now pins it with the recording stub handler: PATCH to `{entitySet}({id})`; `If-Match: *`; one `MSCRMCallerID` equal to the id (the metadata lookup stays app-only); none for a null caller, which keeps `If-Match: *`; `Guid.Empty` throws `ArgumentException` and no PATCH is sent; 404 and 412 throw `KeyNotFoundException`; 204 succeeds; 400, 403 and 500 stay `HttpRequestException`. Mutations: removing the `If-Match` line fails 6 tests (3 new impersonation-file tests, plus 3 in `RecalculateWriteNoCreateTests`); dropping the impersonation argument fails 4 (2 new, 2 in `RecalculateWriteNoCreateTests`).

**Known limit (optional K, skipped).** Under `If-Match: *` a 404 can come from a missing `@odata.bind` target rather than the primary row, and the write reports `not_found` for it. The service reads the error body only as a message string for logging (no code or entity/id extraction), so telling the two apart would mean parsing localisable message text; that is not reliable, so the 404 stays one `KeyNotFoundException`. The effect is a mislabelled reason on a failed write, never a created row or a granted write.

## Files

**Ontology-owned (ontology-r1 review requested):**
- `src/server/api/Sprk.Bff.Api/Services/Signals/SignalWriter.cs` (task 026; fix-round-1 item 1)
- `src/server/api/Sprk.Bff.Api/Services/Signals/DecisionRecordWriter.cs` (task 026; fix-round-1 item 3a)
- `src/server/api/Sprk.Bff.Api/Services/Workspace/TodoRegardingBuilder.cs` (the shared builder's Signal/DR map)
- `src/dataverse/solutions/SpaarkeMaster/Entities/sprk_Signal/Entity.xml` (task 026; fix-round-1 item 2: `IsSecured`)
- `src/dataverse/solutions/SpaarkeMaster/Entities/sprk_DecisionRecord/Entity.xml`
- `tests/unit/domain/Signals/SignalWriterTests.cs`
- `tests/unit/Sprk.Bff.Api.Tests/Services/Signals/DecisionRecordWriterTests.cs`
- `tests/integration/data-mutation/Signals/SignalPairTypeSchemaAgreementTests.cs`
- `tests/integration/data-mutation/CoreAncestorStamping/OntologyStampedChildRestampTests.cs`

**uac-r2 owned (uac-r2 review requested):**
- `src/server/api/Sprk.Bff.Api/Services/Dataverse/CoreAncestorResolver.cs`: IntermediateRootColumns, OntologySubjectSources, `SingleRootIntermediates`; fix-round-1 `CoreAncestorRefusalKind`.
- `src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts`: INTERMEDIATE_ROOT_COLUMNS.
- `src/server/api/Sprk.Bff.Api/Infrastructure/Dataverse/RecordContainerResolver.cs`: KindByEntity, ByEntity, and the single-root 409.
- `src/server/api/Sprk.Bff.Api/Services/Access/SecureChildLineage.cs`: the Signal/DR line parent.
- `src/server/api/Sprk.Bff.Api/Services/Dataverse/OwnedChildWrite.cs` (fix-round-1 item 3b: the other invoice's entity set; fix-round-2 item 1: the optional app-only line reader).
- `src/server/api/Sprk.Bff.Api/Services/Dataverse/CoreAncestorResolver.cs` (fix-round-2 item 3: `StampFailureOf`, K4).
- `src/server/api/Sprk.Bff.Api/Api/ChildRecordEndpoints.cs` (fix-round-2 item 1: the re-file route passes `IGenericEntityService`).

**AI platform owned (fix-round-2, review requested):**
- `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/ActionCore/UpdateRecordActionCore.cs` (DEF-022: guard before the PATCH).
- `src/server/api/Sprk.Bff.Api/Services/Dataverse/DataverseUpdateHandler.cs` (DEF-022: guard before the write).
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/DataverseUpdateRecordHandler.cs` (D-96: optional `IGenericEntityService` ctor parameter, passed to the re-file core).
- `src/server/api/Sprk.Bff.Api/Api/FieldMappings/FieldMappingEndpoints.cs` (DEF-022: guard before the push write).

**Other:**
- Scripts: the schema script and `scripts/ebilling/Set-EbillingFirmReasonFieldSecurity.ps1`.
- `src/server/api/Sprk.Bff.Api/Services/Dataverse/InvoiceFamilyCoreRecordGuard.cs` (ours, task 013; fix-round-2: D-96 line check, `CheckGenericUpdateAsync`).
- SpaarkeMaster: `Other/Relationships/sprk_BillingEvent.xml`, `Other/Relationships.xml`, `Other/FieldSecurityProfiles.xml`, `Other/Solution.xml`.
- Tests:
  - `tests/unit/domain/Access/SecureChildLineageTests.cs`
  - `tests/unit/Sprk.Bff.Api.Tests/Services/Dataverse/CoreAncestorResolverTests.cs`
  - `tests/unit/Sprk.Bff.Api.Tests/Services/Dataverse/InvoiceFamilyCoreRecordGuardTests.cs`
  - `tests/unit/Sprk.Bff.Api.Tests/Services/Dataverse/InvoiceFamilyCoreRecordGuardLineSplitTests.cs` (fix-round-2)
  - `tests/integration/data-mutation/RecordOwnership/InvoiceFamilyGenericUpdatePathTests.cs` (fix-round-2)
  - `tests/integration/data-mutation/RecordOwnership/SecureChildOwnershipAiToolTests{.cs,.ChildRecordRoutes.cs,.CommunicationFilingSignal.cs}` (fix-round-2: the app-only reader)
  - `tests/unit/Sprk.Bff.Api.Tests/TestInfrastructure/RecordTypeCatalogStub.cs`
  - `tests/integration/data-mutation/CoreAncestorStamping/StampWorld.cs`
  - `tests/integration/data-mutation/CoreAncestorStamping/CoreAncestorStampReconciliationJobTests.cs`
  - `tests/integration/data-mutation/RegardingBuilder/RegardingBuilderHostTableTests.cs`
  - `tests/integration/data-mutation/RecordOwnership/SecureChildOwnershipAiToolTests.InvoiceFamilyGuard.cs`
  - `tests/integration/data-mutation/Signals/FirmReasonFieldSecurityAgreementTests.cs`
  - `tests/integration/auth/UnifiedAccessControl/ChildRecordContainerResolutionTests.cs`
  - `PolymorphicResolverService.coreAncestor.test.ts`

## Solution source

`Export-SpaarkeMasterSource.ps1` was run against a temp folder (`C:\tmp\sm026\out`). Only the four task files were copied; every hunk in them is additive. There was no version bump.

The export stopped at its F12 guard on 7 pre-existing missing dependencies, none of them this task's (DEF-021, #2045):
- `sprk_externaltenantid`, `sprk_homeobjectid` and `sprk_hometenantid`, each needed by type 70 (two each);
- the `sprk_SpaarkePlatform` app module.

The live solution also has about 238 drifted or extra files belonging to other projects. These include app modules and sitemaps, PCF bundles, and Entity.xml for sprk_Budget, sprk_Communication and sprk_Organization. They were not copied, so the next owner's export picks them up.

Fix-round-1 edited the source by hand, to match what the firm reason script applied live: Entity.xml `IsSecured`, the two profile entries and the root component. It did not run an export, because of DEF-021.

## Verification

| Check | Task 026 | Fix-round-1 |
|---|---|---|
| BFF build (-m:1) | Green; no new warnings in changed files | Green; no warnings in changed files |
| Targeted + Signals + Finance + Invoice filter | 4,782 passed, 5 skipped, 0 failed | 4,813 passed, 5 skipped, 0 failed (adds Dataverse, RecordOwnership, UnifiedAccessControl, DecisionRecord, FieldSecurity) |
| Spaarke.ArchTests | 1,056 / 1,056 | 1,056 / 1,056 |
| Jest (PolymorphicResolverService) | 83 / 83 | 83 / 83 |
| ADR-010 registrations | No DI changes (ceiling 165 untouched) | No DI changes |
| Route ledger | No endpoints (131 untouched) | No endpoints |
| Publish size (linux-x64 zip) | 73e912d22 (task 026 base) → task 026 head: 39,223,607 → 39,225,138 B (+1,531 B), total 39.2 MB | 794dd173c → fix-round-1 head: 39,233,041 → 39,235,193 B (+2,152 B), total 39.2 MB |
| Vulnerable packages | None; no package changes | No package changes |

Fail-before proofs:
- Task 026, with the source from 33ad6c984: 34 of the new or changed .NET tests failed, and the jest line test failed. With only the two writer checks disabled, 5 writer tests failed.
- Fix-round-1:
  - item 1: with the MultipleRoots mapping disabled, 2 tests fail;
  - item 3a: with the core check commented out, 3 fail;
  - item 3b: with the line branch disabled, 13 fail; with the stored-line entity-set fallback reverted, 1 fails;
  - item 6: with two TS rows swapped, the lockstep fails;
  - item 2: with Entity.xml `IsSecured` reverted, the agreement test fails.

## Deviations and justifications beyond the contract

- `SingleRootIntermediates` is new surface:
  - Existing: CoreAncestorResolver already fails closed when one table has two different ids, but not on two roots of different types.
  - Extension: it is the smallest extension of that rule.
  - Cost of doing nothing: a line under a matter and a project would be stamped with both, and its Signal would group under the matter (D-37), which is the "pick" FR-05 forbids.
- `SignalWriter.RequireSingleInvoiceFamilyCore` is new:
  - Without it, a Do-lane line or invoice with no core would be written as a no-core item (D-35).
  - An invoice with two cores would be grouped matter-over-project.
- `VerifiedSubjects` gains `sprk_invoice` as well as `sprk_billingevent`, because a billing finding can be about an invoice (FR-15).
- Fix-round-1:
  - `CoreAncestorRefusalKind` is an additive property, with no behaviour change for any existing caller. Without it, the writer cannot tell "two roots" from "subject gone".
  - `LineInvoiceCoreRecordMismatchCode` is a new refusal code inside the existing guard.
  - The firm reason writer profile is a new profile (see Live changes).

## Known limits

- K1, invoice two roots:
  - CoreAncestorResolver still derives both stamps for an invoice under a matter and a project, and the storage resolver does not refuse it. Only the writers refuse it.
  - This is owned by uac-r2 under #2047. uac-r2 confirmed that single-root invoices are theirs to decide there, and not part of 026. Live dev: 0 of 10 invoices name two roots.
- K2, TS preview:
  - The browser derivation does not apply the single-root rule, so a two-root line would preview two stamps where the server refuses.
  - This is unreachable today: no client host can be filed under a line (sprk_todo has no line lookup).
- K3, model-driven re-file: a re-file made outside the BFF reaches Signal and DR rows only through the reconciliation job, which keeps sprk_signal and sprk_decisionrecord report-only by default. This is the existing posture for every ontology subject.
- K4: resolved in fix-round-1 item 1.
- K5, RemoveLink on append-only DRs: deleting or archiving a line clears `sprk_regardinginvoiceline` on its Decision Records, which are append-only, while their pair text (`sprk_regardingrecordid`, name, URL) survives. The DR still says which line it was about, but the lookup is gone. This is the same cascade as `sprk_decisionrecord_reginvoice`.
- K6, a no-root line in storage: a line with no core lookup would resolve storage through the business-unit fallback rather than refusing. This is unreachable, because every BFF create and update path refuses a no-core line (FR-05) and no storage route takes a line as parent.
- K7, FR-05 coverage: resolved in fix-round-2 item 2 (DEF-022). The three generic update paths now run the guard before their write.

## Reviews

| Reviewer | Scope | Verdict |
|---|---|---|
| ours (code-review) | the whole task | fix-now items; folded into fix-round-1 items 1, 3, 4 and 6 |
| ontology-r1 | the ontology-owned files | F1: the Ontology Service role lacked AppendTo on the invoice line and invoice. ontology-r1 applied that live and carries the XML and guard in its own PR (D-95, D-95a). Its condition 2 reading (`:298/:546`) is corrected above. Other items folded into fix-round-1. |
| ontology-r1 re-check | fix-round-1 | CLEAN; the separate firm-reason writer profile is confirmed. K3 / K1 (053 step 5) and the task 130 note are fix-round-2 items 4 and 5. |
| uac-r2 re-check | fix-round-1 | No F-class finding in the changed code. F1 (an invoice re-file splits it from its lines) is fix-round-2 item 1 (owner D-96). K1 is accepted. K4 is fix-round-2 item 3. |
| uac-r2 | `ef3efce73..b5f4a4c78`: the three tables, restamp coverage, tests and the K1 question | No fix-now. All five D-94 conditions met (condition 4 with the deviation above). MultipleRoots → CoreRecordAmbiguous accepted. Single-root invoices are uac-r2's under #2047. uac-r2 will re-check only the CoreAncestorResolver refusal-kind change. |

Owner decision D-95(2) (field security on the firm reason) came from the task 026 review.
