# Task 045g — a filed invoice is never moved; rows filed with an invoice regard its core record

POML: `tasks/045g-invoice-family-never-moved-children-match-invoice.poml`. Owner decision EB-D-231 (#2794): a wrongly filed
invoice is returned to the firm and resubmitted on the right record. It is never moved.

## Rules

1. **Rule 1.** An invoice's core record (`sprk_matter`, `sprk_project`, `sprk_regardingworkassignment`) cannot change after
   create, on any path.
   - The refusal is the new code `invoice_family_invoice_core_record_fixed`. Its fixed text names no record.
   - The hint shown with this code and with the line mismatch code is replaced.
   - Old hint: detach the lines, move the invoice, move the lines, re-attach them.
   - New hint (`InvoiceFamilyCoreRecordGuard.ReturnAndResubmitHint`): "A wrongly filed invoice is not moved: it is returned
     to the firm and resubmitted on the right matter, project or work assignment."
2. **Rule 2.** These rows must regard their invoice's core record, on create and on update:
   - an approval, a submission or a correction (through `sprk_invoice`);
   - an external identifier filed under a core record (through `sprk_regardinginvoice`).

   A mismatch reuses D-96's code, `invoice_family_line_invoice_core_record_mismatch`, with the same hint.

   An external identifier filed under its invoice only (no core lookup) keeps D-68: it regards the invoice and is not
   compared.
3. **Rule 3.** A line's invoice (`sprk_billingevent.sprk_invoice`) cannot be cleared after create.
   - The refusal is the new code `invoice_family_line_invoice_cleared`, with fixed text.
   - A line may still be pointed at another invoice of its own core record. Rule 2's comparison, the existing line rule,
     decides that.
4. **Order.** Rules 1 and 3 are decided right after the guard's own `Check` admits the row, on the stored row, before any
   other read: lines, the superseded invoice, the work assignment, the catalog.
5. **Person runs.** On the playbook update and the output orchestrator run as a person, a refusal of rules 1–3 is decided
   again on the stored row and the other invoice as that person reads them (`PersonRowReader`).
   - A row the person cannot read gives `invoice_family_read_failed`.
   - A read fault also gives `invoice_family_read_failed`.
   - So no refusal turns on a row the person cannot read.
   - On the caller paths, the stored row and the other invoice are already read as the caller.
6. **Fail closed.** A stored-row or invoice read that fails writes nothing. The caller's cancellation is rethrown.

## Inventory (who writes these columns)

- **Conflicting flow found.** The Create Matter wizard's "Associate To → Invoice" option re-filed an existing invoice under
  the new matter (`PATCH /api/v1/child-records/sprk_invoice/{id}` with a matter bind).
  - Reported as a STOP. The owner decided: "Build 045g, remove Invoice here".
  - The option, its `associateToRecord` branch and the BFF re-file wrap are removed in
    `src/client/shared/Spaarke.UI.Components/src/components/CreateMatterWizard/CreateMatterWizard.tsx`. Other options are
    unchanged.
  - No consumer outside the library depends on the option:
    - `src/solutions/CreateMatterWizard/src/main.tsx` launches only from `sprk_project`;
    - `sprk_wizard_commands.js` opens it only from a project;
    - the AssociateToStep `types.ts` invoice entries serve other wizards and are unchanged.
- **Finance writers (ebilling-r1 flows 1–7, verified in code).** They write through `IGenericEntityService` and
  transactions, app-only. They use only the pure `Check`, so 045g does not reach them. None would be refused anyway:
  1. Commit undo clears the submission's invoice (`SubmissionCommitGate.cs` 425–446). Rule 3 is a line's only, and a
     must-not-fire test drives the same shape through the generic paths.
  2. `BuildSubmissionUpdate` (1965) sets the submission's invoice to one of the same core (`outcome.CoreRecord`, 459–462).
  3. Rerun `LineUpdate` writes only derived and decision columns (196–207).
  4. The supersedes link (EB-D-206) is found by `snapshot.CoreRecord`, and the rerun refuses a core mismatch (424–428).
  5. `BuildRenumberUpdate` (600–622) and its caller (`InvoiceAssignFirmService` 1085–1150) write the firm, numbers, names
     and findings only.
  6. Add line and adjustment (`InvoiceLineAddService` 1179–1220, 657–687) create rows with the invoice's own core record.
  7. No flow updates an existing external identifier.

  Also checked: return to firm (`InvoiceRejectReturnService` 595/655/702) writes status, claim and record link only. The
  repair pass (`RecordCreationService.Repair.cs` 935–947) writes no family row.

## Component justification (§11)

- **`InvoiceCoreRecordFixedCode`, `LineInvoiceClearedCode` (new codes, with fixed reasons).**
  - Existing: the guard's code set (D-96's mismatch, `MultipleCoreRecordsCode`).
  - Extension: neither existing code says what was refused. A move with every line following is not a mismatch, and a
    cleared invoice names no other record. D-96's code is reused for rule 2.
  - Cost of doing nothing: the refusal would carry a code that names a different rule, and its hint would describe a move
    the owner has ruled out.
- **`ReturnAndResubmitHint`.** This replaces `DetachMoveReattachHint` (renamed, new text). It is not new surface.
- **`InvoiceFamilyCoreRecordGuard.PersonRowLookup` (delegate) and `OwnedChildWrite.PersonRowReader`.**
  - Existing: 045f's `PersonStoredRowReader`, built on the same `IImpersonatedCommunicationQuery.QueryAsync`.
  - Extension: that reader reads one column set for 045f's URL rule. The guard needs the guarded lookups of any family row
    and of the other invoice, as references. The reader keeps the same query and the same 403/404 handling.
  - Cost of doing nothing: on a person run, a rule-2 refusal could turn on an invoice the person cannot read, which would
    tell them how that invoice is filed.
- **`InvoiceCoreRecordChanges` and `RefuseChangeAfterCreate` (pure helpers).**
  - Existing: `NamesTheSameRecord`, which these use.
  - Extension: they are the one decision, asked by the guard, by 045d's caller-path derive skip and by the app-only derive.
  - Cost of doing nothing: three copies of rule 1.
- **The `stored` parameter of `CheckWriteAsync` and the `asPerson` parameter of `CheckGenericUpdateAsync`.**
  - These are optional parameters on existing methods. Every create passes no `stored`.

## Before → after

| Write | Before | After |
|---|---|---|
| Invoice core lookup changed, any update path (PATCH, chat tool, playbook, orchestrator, push, decision route) | written when every line followed (D-96), with 045d's derived fields | 400 / refused `invoice_family_invoice_core_record_fixed`, return-and-resubmit hint; nothing read after the stored row |
| Invoice moved under a work assignment | cycle check (UAC-r2) then written | refused `..._core_record_fixed`; the work assignment is not read (the cycle stays on the create) |
| Invoice naming the core record it already names | written, nothing derived | unchanged |
| Line's invoice cleared (with or without a move) | written (the old hint's first step) | refused `invoice_family_line_invoice_cleared` |
| Line pointed at another invoice of its own core record | written | unchanged |
| Approval / submission / correction / external identifier (under a core record) given an invoice of another core record, or moved away from its invoice's | written | refused `..._line_invoice_core_record_mismatch` |
| The same rows given an invoice of their own core record; an external identifier under its invoice only (D-68); a submission's invoice cleared | written | unchanged |
| Rule refusal on a person's run, row unreadable as the person | n/a | `invoice_family_read_failed` |
| Create Matter wizard "Associate To" | offered Project, Account, Invoice | offers Project, Account |

## 045d derive paths that become unreachable

- Every derive of an invoice's resolver fields for a core move, on every update path:
  - On the caller paths, `KeepInvoiceFamilyResolverFieldsAsync` skips the derive when `InvoiceCoreRecordChanges`, and the
    guard refuses the write.
  - On the app-only paths, the guard refuses before `KeepInvoiceFamilyResolverFieldsAppOnlyAsync`.
  - A change seen only by the derive's own read (the row changed in between) is refused with the same code
    (`DeriveForGenericUpdateAsync`).
- On the child-record PATCH route, the 045d derive is now unreachable for every family table. The invoice is the route's
  only family table.
- The derive stays for:
  - creates (all six tables);
  - an approval, submission, correction or external identifier whose core move is admitted (no invoice, or moving with
    an invoice of the new core record);
  - the D-68 external identifier.

  Its tests now drive an approval.

## Evidence

- **Filtered Sprk.Bff.Api.Tests.** Filter: InvoiceFamily, RecordOwnership, RecordCreation, Finance, ProcedureAssignment,
  SecureChildOwnership, UpdateRecord, DataverseUpdateHandler, FieldMapping, `&FullyQualifiedName!~Api.Office.`.
  - Before the test changes: 96 failed.
  - After: **11776 passed, 0 failed, 4 skipped (11780)**.
- **Each must-fire set with its rule switched off.** The switch was a temporary local `false &&`, never committed, and the
  guard was restored byte-identical.
  - Rule 1 (`InvoiceCoreRecordChanges` false): **60 of 60 must-fire tests fail**. The set's 2 must-not-fire cases pass.
  - Rule 2 (`IsInvoiceChild` false and the external-identifier branch off): **41 of 45 fail**. The 4 that pass are the
    submission cells on the playbook update and the orchestrator. There the handlers' access-column rule refuses the write
    first. The tests accept either refusal, and nothing is written either way.
  - Rule 3 (the line branch off): **7 of 7 fail**.
  - Person re-decision (the `asPerson` block off): **6 of 6 fail**. The 2 must-not-fire cases pass.
- **Client.**
  - Spaarke.UI.Components `jest src/components/CreateMatterWizard`: **42 passed, 7 suites**. The linked `@spaarke/sdap-client`
    and `@spaarke/auth` packages were built locally to run them.
  - With the old wizard file restored: **3 failed** (the new invoice tests).
  - `tsc --noEmit`: 0 errors.
  - eslint: 0 errors. One warning, at line 780, which this task did not touch.
- **Gates:**
  - `dotnet build Spaarke.sln`: 0 warnings, 0 errors.
  - ArchTests alone: 2544 passed, 0 failed.
  - Filtered Sprk.Bff.Api.Tests (excluding `Api.Office.`): 11776 passed, 0 failed, 4 skipped. Before the test changes,
    96 failed.
  - ebilling filters (Finance, ProcedureAssignment, InvoiceFamily; excluding `Api.Office.`): 8351 passed, 0 failed,
    4 skipped.
  - `dotnet format whitespace --verify-no-changes` on the 24 touched .cs files: no changes.

## Changed existing expectations (each with its reason)

All of the following are rule 1 unless stated: an invoice's core record no longer moves.

- **`InvoiceFamilyGenericUpdatePathTests.OrphanAndD96`**
  - `D96_AnInvoiceMovedToAnotherMatter_WhileALineStaysOnTheOldOne…` and `D96_…WhoseLinesAreAllOnIt_IsWritten` are merged into
    `AnInvoiceMovedToAnotherMatter_IsRefusedCoreRecordFixed_WithTheHint_WhereverItsLinesAre`. It also asserts that no line or
    catalog query runs.
  - `D96_ALineMovedToAnotherMatter…` keeps the mismatch code; the hint text is the new one.
  - `D96_ALineDetachedFromItsInvoice_AndMovedInOneWrite_IsWritten…` becomes `ALineWhoseInvoiceIsCleared_IsRefusedLineInvoiceCleared…`
    (rule 3; with and without a move).
  - `TheMismatchRefusalOnTheThrowingPaths_PinsTheExactMessage` now drives a line mismatch. The new
    `TheCoreRecordFixedRefusal…` pins the invoice text.
  - The re-attach test's comment no longer describes the detach sequence.
- **`…ResolverFields`**
  - The invoice-move derive tests (resolver fields, blank name or number, derive failures, person reads, push caller reads,
    403, pushed pair, cancellation, second core record after the guard) now drive an approval with no invoice.
  - The invoice case is `AnInvoiceMovedToAnotherMatter_IsRefusedCoreRecordFixed_AndNothingIsDerived`.
  - New: `AnInvoiceWhoseCoreRecordChangesAfterTheGuard_IsRefusedCoreRecordFixedByTheDerive…`.
- **`…WorkAssignmentCycle`**: both move tests become one refusal theory (wherever the work assignment is filed). The cycle
  stays on the create.
- **`…PairAgreement`**: `D96_AnInvoiceMovedWhileALineStaysBehind_WithAPairSent…` becomes
  `AnInvoiceMovedWithAPairSent_IsRefusedCoreRecordFixed_NotForThePair`.
- **`…AllFamilyTables`**: `AnExternalIdentifierUnderAMatter_GivenAnInvoice_IsWritten…` gives the invoice its own matter.
  Rule 2 now compares the two.
- **`…FixRound2`**: the push re-file race test writes the matter the invoice already names (the re-file still wraps the
  write).
- **Unit `InvoiceFamilyCoreRecordGuardLineSplitTests`**
  - The hint test pins the new codes and text.
  - `GenericUpdate_AnInvoiceMovedToAProject…` now expects the fixed code in all three line states.
- **Unit `InvoiceFamilyCoreRecordGuardTests`**: `CheckWrite_TheOtherFamilyTables_AreNeverComparedWithAnInvoice` is replaced
  by rule 2's must-fire, must-not-fire and fail-closed tests, plus D-68.
- **Unit `InvoiceFamilyCoreRecordGuardWorkAssignmentCycleTests`**: the generic-update move tests become one refusal theory.
- **`SecureChildOwnershipAiToolTests`**
  - The invoice re-file and chat-tool move tests (`InvoiceFamilyGuard`, `InvoiceFamilyResolverFields`,
    `InvoiceFamilyPairAgreement`, `DecisionRouteInvoiceFamily`) now expect `..._core_record_fixed` and no patch.
  - The census theories drop `sprk_invoice` and get `ChildRefile_AnInvoice_IsNeverMovedUnderAnotherMatter_AndKeepsItsOwnerAndShares`.
    `TheRefileTheory_CoversEveryRefileTable` still covers every route table.
  - The 045d chat-tool derive tests now drive an approval.
  - The scripted metadata gains `sprk_invoice` on the approval, the correction and the submission, as on the live tables
    (the writers in Finance set it).

## Known limits

- **K1.** D-96's app-only line count still runs for an invoice update that writes its core lookup with the record it
  already names. A stray line from older data is then refused with the mismatch code. This is not a new refusal.
- **K2.** The field-mapping push runs with no person, so its rule refusals are read app-only, as its guard reads today.
- **K3.** On the two caller paths a non-invoice row's derive (045d) still runs before the guard. A derive failure is
  answered before a rule-2 refusal of the same write. This is the existing K3 order, and nothing is written either way.
- **K4.** A caller create of an approval, submission, correction or external identifier filed with an invoice needs Read on
  that invoice. An unreadable invoice is the uniform not-found, never a mismatch.
- **K5.** The create seam fixture has no invoice row for the ownership step. The must-not-fire create test asserts that the
  registered rule admits the row, not the whole create. The guard's admit is pinned in unit and update-path tests.
- **K6.** The client change ships with the next shared-library and Create Matter wizard deploy. Not deployed here.
- **K7.** Comments that still describe the old detach sequence, outside this task's files (left as found):
  - `tests/integration/Sprk.Bff.Api.IntegrationTests/Signals/SignalReadRouteTests.cs:1464`;
  - `tests/integration/seam/RecordCreation/RecordCreationServicePipelineSeamTests.WriterOnlyTables.cs:93`;
  - `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/InvoiceApprovalLineOriginTests.cs:182-191` (an ebilling-r1 test that
    builds the rows directly; it still passes).
