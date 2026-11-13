# Task 045e — invoice family: the regarding pair agrees with the core lookup (#2772, DI-029)

Owner decision 2026-11-13 ("Refuse it", #2772 option (a); `decisions.md`). POML: `tasks/045e-invoice-family-pair-agrees-with-core.poml`.
FR-23 (the regarding pair stays pushable) is amended for the six invoice-family tables only.

## Rule

1. On the six invoice-family tables (`InvoiceFamilyCoreRecordGuard.CoreRecordLookups`: `sprk_invoice`, `sprk_billingevent`,
   `sprk_invoicesubmission`, `sprk_invoiceapproval`, `sprk_externalidentifier`, `sprk_invoicecorrection`), a write that sends
   the regarding pair (`sprk_regardingrecordid`, `sprk_regardingrecordtype`, either half) must name the row's effective core
   record: the core lookup written in the same request, else the stored one. For an external identifier filed under an
   invoice only, the effective core record is that invoice (D-68).
2. "Agrees" means: the id parses as a GUID equal to the core record's id (any case, braces allowed), and the type is the
   catalog row (`sprk_recordtype_ref`, active, by `sprk_recordlogicalname`) of the core record's table. The catalog is read
   app-only, on every path. An agreeing id is WRITTEN in canonical form (lowercase, "D": `CanonicalPairId`) on every path,
   because the pair is a column of the external identifier's alternate key (D-68) and must not take another spelling of the
   same id (fix round 1, F2). Canonicalising was chosen over refusing: the id names the right record, and every reader
   already parses it, so refusing would reject a correct write over its spelling.
3. A write that disagrees is refused through the path's existing refusal form, with ONE code
   (`invoice_family_regarding_pair_core_record_mismatch`), and nothing is written:
   - create (`RecordCreationService` step 5½, `RecordTableRules.InvoiceFamilyRule`): `PreWriteRefusal.Refused`, 400;
   - child-record PATCH (`sprk_invoice` only — the route re-files no other family table): `ChildRecordEndpoints.InvoiceFamilyRefused`, 400 with the code;
   - chat update tool (`dataverse.update_record`): the tool error with the code;
   - playbook Update Record (`UpdateRecordActionCore`) and output orchestrator (`DataverseUpdateHandler`): the guard's
     code-only `RefusalException`.
4. Only the halves a write sends are checked; a half it does not send is not read. On an update a sent clear (null) of
   either half disagrees: the row would no longer describe its core record. On a create a null half is not checked (the
   045d derive writes the pair).
5. A write that moves the core lookup (sets or clears one so the row names another record) writes only the pair 045d's
   derive gives it, the pair of the record the row will name; the pair it sends is never written (fix round 1, K1):
   - app-only paths: the gate removes every sent pair and display column (`RegardingPairAndResolverColumns`) before the
     derive. If the derive then derives nothing (the core lookup already moved, e.g. a concurrent write), the stored pair is
     left as it is.
   - caller paths (PATCH, chat tool): the derive runs before the check and writes over the sent pair, so the check is asked
     on the pair as derived (`aCoreMoveIsChecked: true`). It agrees by construction; in the concurrent-move case the sent
     pair is refused rather than dropped.
   A write that re-names the record the row already names moves nothing, so the pair it sends is checked.
6. Order: the agreement check is asked after the guard's own decision admits the row (a row the guard refuses keeps its own
   code), on the payload as it will be written:
   - caller update paths: in `OwnedChildWrite.DecideInvoiceFamilyAsync` (inside `RefileAsync`), after 045b's planner
     (`KeepRegardingPairWholeAsync`) and 045d's derive; the agreeing id is canonicalised in `KeepRegardingPairWholeAsync`.
   - app-only paths (playbook update, output orchestrator, push): guard → planner → `KeepRegardingPairAgreeingAsync` (the
     gate, first step of `OwnedChildWrite.KeepInvoiceFamilyResolverFieldsAppOnlyAsync`) → derive → write. A pair the planner
     itself writes is therefore checked too (fix round 1, K2); the guard no longer checks the pair before the planner.
   - create: step 5½, as before; 045d's derive replaces the pair with the canonical one.
7. Fail closed: a fault reading the stored row or the catalog writes nothing (`invoice_family_read_failed`, the fault's text
   never in the reason); no active catalog row for the core table is `invoice_family_resolver_fields_incomplete`; a stored
   row that names no single core record keeps the guard's own answer (`no_core_record`, `multiple_core_records`).
   Cancellation is rethrown.
8. The field-mapping push asks the same gate (push order unchanged: source gate → access-column scrub → guard → planner →
   gate + derive), so a pair in a family child's payload would refuse that child only. A push never puts
   a pair in its payload: the mapping engine (`CreateTimeFieldMapping.ApplyRule`, FR-05(e)) skips a rule aimed at the pair or
   the resolver fields on every table. On the push a family child's pair moves only with its core lookup (045d).
9. Not in scope (ebilling-r1, held): a rule that a child's core record equals its invoice's.

## Code decision

No existing guard code says "the regarding record disagrees with the core record":
`multiple_core_records` / `no_core_record` describe the core lookups, `line_invoice_core_record_mismatch` a line and its invoice,
`resolver_fields_incomplete` a missing catalog row. One new code was added,
`InvoiceFamilyCoreRecordGuard.RegardingPairCoreRecordMismatchCode = "invoice_family_regarding_pair_core_record_mismatch"`, with
fixed text that names no record (`RegardingPairCoreRecordMismatchReason`). The code-only paths report the code through the
existing `CodeOnlyRefusalMessage`; `ChildRecordEndpoints.InvoiceFamilyRefused` maps an unlisted guard code to 400 with the
reason, which is this code's intended status. No per-path code.

## §11 — new surface

| Surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| Code `invoice_family_regarding_pair_core_record_mismatch` + reason | The guard's codes (above); none names a pair/core disagreement. | Reusing `multiple_core_records` would tell the caller the core lookups are wrong, which they are not. | A disagreeing pair is refused with no code that says why, or with a wrong one. |
| `InvoiceFamilyCoreRecordGuard.CheckRegardingPairAgreesAsync` | `Check` (core lookups only), 045d's `Derive*` (writes the pair; never compares). | It is the one place every path calls; it extends the guard file, beside `Check` / `CheckGenericUpdateCoreAsync`, as the POML asks. | Each of five paths re-derives the comparison, or a row is left naming one record by its core lookup and another by its pair. |
| `RegardingPairWrite` (record struct) + `RegardingPairWrite.Of` | `OwnedChildWrite.PairChangeOf` (caller-path tuple), 045b's `PairOf(MappedItem)`. | It is a thin public carrier over `PairChangeOf`, so the guard's signature does not take a private tuple; it adds no parsing. | The guard cannot take the pair from the app-only payloads and the create's mapped item through one signature. |
| `OwnedChildWrite.PairWriteOf(MappedItem)` | `PairOf(item)`. | Maps `PairOf` onto `RegardingPairWrite` (`InvalidId` → `Unreadable`); no new parsing. | The create's step 5½ cannot hand its item's pair to the one check. |
| `OwnedChildWrite.DecidePairOnlyUpdateAsync` (private) | `DecideInvoiceFamilyAsync` returned Proceed for an update writing no guarded column. | A private branch of `DecideInvoiceFamilyAsync`, reusing `ReadLookupsAsCallerAsync` and `Check`. | A pair-only PATCH or chat update of a family row is never checked (the core lookup is not written, so the old path read nothing). |
| `recordTypes` parameter on `DecideInvoiceFamilyAsync` (optional) | `PreWriteRow.RecordTypes` (the create's app-only catalog reader, 045d). | Optional, defaults to `AppOnlyRecordTypes(appOnlyEntities)`; the create passes its own reader. | The create cannot read the catalog through the reader it already holds. |
| Fix round 1: `InvoiceFamilyCoreRecordGuard.KeepRegardingPairAgreeingAsync` (the app-only gate) | The guard's early pair check in `CheckGenericUpdateCoreAsync` (removed: it saw the request, not the planned payload). | It replaces that check, moved after the planner into the one function every app-only path already calls (`KeepInvoiceFamilyResolverFieldsAppOnlyAsync`); no call site changes. | A pair the planner writes is never checked (K2), and a sent pair on a core move could be written when the derive derives nothing (K1). |
| Fix round 1: `CanonicalPairId`, `WithCanonicalPairId` (guard), `OwnedChildWrite.WithCanonicalFamilyPairId` | 045d's derive already writes the canonical id on a move; `WithColumnsSet`. | One formatter, applied by the app-only gate and by the caller-path planner entry over the existing `WithColumnsSet`. | Another spelling of the same id is stored in the D-68 alternate key (F2). |
| Fix round 1: `aCoreMoveIsChecked` parameter (optional) on `CheckRegardingPairAgreesAsync` | The move exemption. | Optional flag; the caller paths pass it because their derive runs before the check. | On the caller paths a sent pair on a move would be exempt even when the derive did not write over it. |
| Test-only: `EntitySetNameFixtures` pins `sprk_recordtype_ref` → `sprk_recordtype_refs` | The fixture's pinned set names. | One entry. | The playbook path's tests cannot bind the pair type. |

## What changed

- `InvoiceFamilyCoreRecordGuard.cs`: the code and reason; `CheckRegardingPairAgreesAsync`; `RegardingPairWrite`. Fix
  round 1: the app-only gate `KeepRegardingPairAgreeingAsync` (asked after the planner; `CheckGenericUpdateCoreAsync` no
  longer checks the pair), `CanonicalPairId` / `WithCanonicalPairId`, and `aCoreMoveIsChecked`.
- `OwnedChildWrite.cs` (fix round 1): `KeepInvoiceFamilyResolverFieldsAppOnlyAsync` asks the gate first;
  `KeepRegardingPairWholeAsync` canonicalises an agreeing family pair id (`WithCanonicalFamilyPairId`);
  `DecideInvoiceFamilyAsync` checks a core move too (`aCoreMoveIsChecked: stored is not null`).
- `OwnedChildWrite.cs`: `PairWriteOf`; `DecideInvoiceFamilyAsync` asks the check after an admitted decision (create and
  update) and on a pair-only update (`DecidePairOnlyUpdateAsync`); optional `recordTypes`.
- `RecordTableRules.cs`: `InvoiceFamilyRule` passes `row.RecordTypes` to `DecideInvoiceFamilyAsync`.
- `FieldMappingEndpoints.cs`: unchanged (it already calls `CheckGenericUpdateAsync`).
- Tests (KEEP paths):
  - `tests/unit/domain/Dataverse/InvoiceFamilyRegardingPairAgreementTests.cs` — the check on all six tables and every core
    lookup, and the create rule (`RecordTableRules.InvoiceFamilyRule`) on all six tables.
  - `tests/integration/data-mutation/RecordOwnership/InvoiceFamilyGenericUpdatePathTests.PairAgreement.cs` — playbook
    update and output orchestrator on all six tables; the push.
  - `tests/integration/data-mutation/RecordOwnership/SecureChildOwnershipAiToolTests.InvoiceFamilyPairAgreement.cs` — the
    PATCH (invoice) and the chat tool (all six tables), with scripted metadata for the approval, external identifier and
    correction and an app-only catalog answer in `AppEntities()`.
  - `tests/integration/seam/RecordCreation/RecordCreationServiceInvoiceFamilyRuleSeamTests.cs` — one create through the
    real service and problem map.
  - Existing test changed: `ActionSeamTests.UpdateRecordAsync_AnInvoiceWhosePairPlannerReadFails_AfterTheFamilyGuardPassed_WritesNothing`.
    It sent a pair-only write to a stored invoice with no core record, which the guard now refuses (`no_core_record`) before
    the planner. Its stored invoice now has a matter and its pair names that matter, so the guard still passes and the
    planner's read failure is still what it tests.

## Evidence

New tests (267), all passing with the rule on:

| Set | Tests | Failed with the rule off |
|---|---|---|
| `InvoiceFamilyRegardingPairAgreementTests` (domain: the check, six tables × each core lookup; the create rule, six tables) | 156 | 120 |
| `InvoiceFamilyGenericUpdatePathTests.PairAgreement` (playbook update + output orchestrator × six tables; push × six tables) | 75 | 33 |
| `SecureChildOwnershipAiToolTests.InvoiceFamilyPairAgreement` (PATCH on the invoice; chat tool × six tables) | 35 | 22 |
| `RecordCreationServicePipelineSeamTests.CreateAsync_AnInvoiceWhosePairNamesAnotherMatter_Is400PairMismatch_AndWritesNothing` | 1 | 1 |

- Rule off = `CheckRegardingPairAgreesAsync` answering "no decision" at its first line; restored afterwards. Every must-fire
  (a different id, the right id under the wrong type, a clear, an id that names no record, the D-68 external identifier, a
  re-named core with a wrong pair, a disagreeing create) and every catalog-fault / cancellation test failed with it off. The
  domain must-not-fire `ThePairOfTheCoreRecord_Agrees` (19) also failed with it off, because it asserts the catalog row was
  read (the comparison ran). The rest of the must-not-fire tests (a pair that agrees, no pair, a move with or without a
  pair, a rename, the push's skipped pair) pass both ways, as they must. The two "stored-row read faults with a pair sent"
  tests pass both ways: the guard already reads the stored row before this check.
- Filtered suites (Invoice | OwnedChildWrite | FieldMapping | UpdateRecordAction | DataverseUpdateHandler | RecordCreation |
  ChildRecord | SecureChildOwnership | RegardingPair | RecordTableRule | UpdatePlanner | DataverseUpdateRecordHandler |
  ActionSeam, minus `Api.Office.`): 9160 passed, 0 failed, 3 skipped. Before the new tests: 8861 passed, 1 failed (the
  `ActionSeamTests` case adapted above), 3 skipped.
- ebilling-r1 filters (Finance | ProcedureAssignment | InvoiceFamily, minus `Api.Office.`): 8027 passed, 0 failed, 4 skipped.
- ArchTests (run alone, unfiltered): 2539 passed. (An earlier "183" figure here was wrong.)
- `dotnet build Spaarke.sln`: 0 warnings, 0 errors.
- `dotnet format whitespace --folder --verify-no-changes` on the touched files: clean.

### Fix round 1 evidence

| Set | Tests | Failed before the round |
|---|---|---|
| Domain: the gate (`Gate_*`: canonical id ×19, canonical sent as is, planned pair ×19, core move ×19, no pair, read fault), the derive step (`Derive_*` ×2), the caller-path move check (×19) | 81 | 59 (with the round's rule switched off; the gate's own agreement and fail-closed tests pass both ways, as they must) |
| Integration, on the committed (pre-round) product code: generic canonical id ×12, D-68 ×4, planner clear ×2; chat canonical id ×6; PATCH canonical, PATCH move / same-matter, chat move / same-matter, chat D-68 ×2 | 31 | 24 |

The 7 that pass both ways pin behaviour the round keeps: a core move with a disagreeing pair is written with the derived pair
(PATCH, chat), a re-named core with a disagreeing pair is refused (PATCH, chat), and the D-68 must-fire (chat, generic ×2).

- Filtered suites (as above, plus SearchIndexDefault | RecordOwnership, minus `Api.Office.`): 10081 passed, 0 failed, 3 skipped.
- ebilling-r1 filters: 8113 passed, 0 failed, 4 skipped.
- ArchTests (alone, unfiltered): 2539 passed.
- `dotnet build Spaarke.sln`: 0 warnings, 0 errors. `dotnet format whitespace --verify-no-changes` on the touched files: clean.

### Fix round 2 evidence

- F4: `CheckRegardingPairAgreesAsync` decides a refused after-state (no single core record) before the move exemption, so a
  write that leaves two core records is refused with the guard's answer, never exempted as a move (as its doc says).
  `AWriteThatLeavesTwoCoreRecords_WithAPairSent_IsRefusedByTheGuardsAnswer_NotExemptedAsAMove` (six tables): 6 of 6 failed
  with the old order.
- F3: the caller-path move check is pinned through `DecideInvoiceFamilyAsync`:
  `UpdateRecordTool_AnApprovalMove_WhoseDeriveFoundNothingToMove_WithADisagreeingPair_IsRefusedMismatch`. The chat tool reads
  the core lookups twice, the derive first and the decision second; the test answers the new matter to the derive (it
  derives nothing, so the sent pair stays) and the old matter to the decision (a move). It failed (the write succeeded) with
  the `aCoreMoveIsChecked` argument removed. The opposite order (old, then new) cannot reach the flag: the derive then writes
  the derived pair over the sent one, and the decision sees no move.

## Known limits (K)

- K1: on a create, a type-only pair that step 4½ clears (no agreeing lookup) is not refused; the derive writes the pair.
- K2: an update that sends one half is checked on that half only; the stored other half is not read or re-checked. Example:
  a row under matter M whose stored pair is (M, the project type), left by an earlier writer; a write sending only the id M
  agrees and is written, and the stored type stays as it was. The write does not introduce the disagreement; an id-only
  write can never store an id other than the core record's.
- K2b (fix round 1): on the caller paths, a core move whose derive derives nothing (a concurrent move) refuses a sent
  disagreeing pair rather than dropping it (the app-only paths drop it). Nothing is written either way.
- K3b (fix round 1): on the app-only paths run as a person, the mismatch code (asked on the app's read) versus a later
  Dataverse privilege error tells the person whether a guessed id is the row's core record — the same class as K7.
- K4b (fix round 1): an update that sends a pair to a row that names no single core record is refused with the guard's own
  code (`no_core_record`, `multiple_core_records`), where before this task it was written. An external identifier filed
  under its invoice only names that invoice (D-68) and is not refused; ebilling-r1 confirmed none of its flows sends a pair
  to an unfiled family row.
- K5b (fix round 1): on a family table whose planner reads no agreement lookup (045b `PairAgreementColumns` is empty for the
  approval, correction, submission and external identifier), a type-only pair write is cleared by the planner (rule 2: no
  single agreeing lookup), and the cleared pair is then refused by the gate. Before the round it was written with the pair
  cleared. A writer sends the id with the type (or moves the core lookup, and the derive writes both). On these tables the
  planner cannot otherwise write a pair that names another record: their agreement lookups are their core lookups, and a
  core move is the derive's.
- K5c (fix round 2): on a K5b row, a type-only write that names the right type is refused with the mismatch code
  (`invoice_family_regarding_pair_core_record_mismatch`), whose text speaks of a pair naming another record. The type itself
  agrees; what is refused is the pair the planner clears. The code is kept (one code, fixed text); the wording is imprecise for
  this case. ebilling-r1 confirmed none of its flows sends a type-only pair (K5b accepted).
- K5d (fix round 2): a write that sends only display columns (`sprk_regardingrecordname`, `…url`, `…number`) and no pair is
  written as sent, as before this task; the gate decides a pair only when one is sent (or the core record moves, when the
  display columns are dropped for the derive).
- K6b (fix round 1): extra reads on the app-only paths: one stored-row read (app-only) when the planned payload carries a
  pair or display column; on the caller paths, one app-only catalog read when a core move also sends a type.
- K3: the type agrees only with the catalog row the app reads as active for the core table (top 1). A pair naming another
  row for the same table (an inactive or duplicate row) is refused.
- K4: on the caller update paths the stored core lookups are read as the caller (`ReadLookupsAsCallerAsync`, as the guard
  already reads them there); the catalog is read app-only. A caller who cannot read the stored row is refused as the guard
  refuses it today.
- K5: on the caller update paths 045b's planner runs before the refusal, so its reads (the catalog, a display read) happen
  for a write that is then refused. Nothing is written.
- K6: extra reads: one stored-row read as the caller on a caller-path update that sends only a pair; one catalog read when a
  write sends a type (none when the id alone decides, or no pair is sent).
- K7: on the app-only paths run as a person, the check is asked on the app's read of the row before Dataverse checks the
  person's access to it, as the guard's other questions are.
- K8: the field-mapping push maps no pair on any table (the engine's FR-05(e) skip), although `PushMayWrite` answers true for
  the pair columns. This task does not change either; a family child's pair moves on the push only with its core lookup.
- K9: model-driven form edits of a family row's pair do not pass through these paths and are not covered.
