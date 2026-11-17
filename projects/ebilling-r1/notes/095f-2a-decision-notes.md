# 095f-2a: the lifecycle writes the decision note; reject, return and approve take a note

> **Task:** `tasks/095f-2a-lifecycle-reject-return-approve-notes.poml` (FULL) · **Design:** `095f-part2-decision-notes-design.md` §1, §2 and §5.3, as amended by §15 (EB-D-240) · **Base:** `task/ebilling-095f-2a` from 5508b576fe · **Status:** built and tested. Code-review, adr-check and the adversarial pass are for the coordinator.

Every "invoice column" in this task is `sprk_invoice.sprk_decisionnote` (EB-D-240). `sprk_correctionnote` is untouched: its only writer is still the submission persister.

## What was built

| Item | Where |
|---|---|
| `TransitionAsync(invoiceId, target, decisionNote, ct)`. The three-argument member forwards `null`. The note joins the status in the one fields dictionary of the one conditional update, so a version conflict writes neither | `Services/Finance/InvoiceLifecycleService.cs` |
| `DecisionColumn`, the shared codes `ebilling.note_too_long` and `ebilling.note_invalid_characters`, and `DecisionRefusalOf` (maps a `Check` refusal to its code and detail) | `Services/Finance/InvoiceCorrectionNote.cs` |
| Reject and return: the note is checked after the reason and before any lock or read. `Values["note"]` is set only for a non-blank note. The lifecycle gets `note ?? reason` | `Services/Finance/InvoiceRejectReturnService.cs` |
| `InvoiceDecisionRequest(Reason, Note = null)` | `Api/Finance/InvoiceRejectReturnEndpoints.cs` |
| Approve: the note is checked before any lock or read. `Values["note"]` is set only for a non-blank note. The lifecycle gets the note, so no note leaves the column unchanged | `Services/Finance/InvoiceApprovalService.cs` |
| The optional `InvoiceApprovalRequest(Note)` body (nullable: no body binds `null`), plus `ProducesProblem(400)` | `Api/Finance/InvoiceApprovalEndpoints.cs` |
| `note` (TextArea, optional) declared on `reject-invoice` and `return-to-firm` | `Services/Signals/Actions/DecisionActionCatalog.cs` |
| The `InvoiceDecisionNoteWritersGuardTests` ArchTest | `tests/Spaarke.ArchTests/` |

## Rules as built

1. **One validation.** The note goes through `InvoiceCorrectionNote.Check`:
   - 2000 characters are accepted; 2001 are refused with `ebilling.note_too_long`.
   - A control character other than `\n`, `\r` and `\t` (for example `\u0007` or `\u001b`) is refused with `ebilling.note_invalid_characters`.
   - Whitespace-only counts as no note.

   The stored note is the value `Check` returns. It is never truncated or stripped by the lifecycle. A refusal is a 400 before any lock, read, transition or record.
2. **The reason is checked first.** A missing reason with a bad note is still refused as `ebilling.reason_required`.
3. **The hash is unchanged without a note.** No note, `""` and whitespace-only leave no `note` key in Values, never `note: null`. The Decision Record hash then matches the pinned golden values below.
4. **What the column holds:**

   | Decision | With a note | With no note |
   |---|---|---|
   | Reject, return | The note | The reason |
   | Approve | The note | Not written; its current value stands |
5. **K-1 adds no new refusal.** A repeat whose note differs from the first is never refused for that difference. What happens depends on when the repeat arrives:
   - **After the record.** The stored record is found and returned. The edited note is dropped, and the record and column keep the first note.
   - **Before the record.** The invoice is already in its target, so the lifecycle writes nothing and the column keeps the first note. The record is written with the repeat's note.
6. **Logs.** No log line contains note text. Tested on the lifecycle and on the reject, return and approve services.
7. **Firm visibility.** `sprk_decisionnote` was added to no firm-facing reader. `Values["note"]` lives on the Decision Record, which is not on any external allow-list.

## Golden hashes (no note; captured on base 5508b576fe before any source edit)

Fixed subject `09500000-0000-4000-8000-00000000f2a1`, confirmed by the ApprovalWorld's fixed test approver:

| Code | Reason / lines | `reviewHash` |
|---|---|---|
| `reject-invoice` | "Rate card not followed" | `1E91C99B7444B0C67B664ACBB030DAB3B34BAE56A7945B8B7A4FCDA41B48043D` |
| `return-to-firm` | "Please correct the task codes" | `832DF98E3F4C689526E02C315F79EE1790ED5EA909622490449274DE0362F198` |
| `approve-invoice` | Accepted 400/400, Reduced 600/450 | `A3AB0EB79298A7E6779A6A9AEE92B3A14769C2C7461BEDCCD2AB78E0F9679B45` |

These are pinned in `InvoiceDecisionNoteTests` (`NoNote_HashesAsBefore_AndTheColumnTakesTheReason`, `Approve_NoNote_IsUnchanged`).

## The writers guard

`InvoiceDecisionNoteWritersGuardTests` scans IL string loads and, since review round 1, custom attribute arguments (see below).

- **Decision-note writers.** `sprk_decisionnote` may be named only by:
  - `InvoiceLifecycleService.TransitionAsync`;
  - `SubmissionOverrideService.BuildReleaseTransaction`;
  - `InvoiceTotalsService.RecalculateAsync`.

  The last two are the release transaction and the totals path the design allows. They are listed as *pending*: allowed, but not checked for staleness, because they do not write the column yet.
- **Correction-note writer.** `sprk_correctionnote` may be named only by `InvoiceSubmissionPersister.FillInvoice`.
- **Controls.** Each guard has a must-fire fixture (a planted extra decision-note writer; a planted correction-note writer on a decision path) and a must-not-fire fixture (a type that names neither column).

## Tests

| Criterion | Test |
|---|---|
| The note is in the same conditional update as the status | `InvoiceLifecycleDecisionNoteTests.Note_LandsInTheSameUpdateAsTheStatus` (approve, reject, return) |
| A version conflict writes neither | `ConcurrentChange_WritesNeitherTheStatusNorTheNote`, `KeepsChanging_GivesUp_WithNoNote` |
| The three-argument call and a null note write no note column (must-not-fire) | `ThreeArgumentCall_AndNullNote_WriteNoNoteColumn` |
| K-1 at the lifecycle | `AlreadyInTarget_WritesNoNote` |
| The note is in Values and in the column (reject, return) | `InvoiceDecisionNoteTests.Note_IsInValuesAndInTheColumn` |
| No note hashes as before; the column takes the reason | `NoNote_HashesAsBefore_AndTheColumnTakesTheReason` (null, `""`, whitespace) |
| The note is inside the hash | `Note_IsInsideTheHash` |
| The 2000-character and 1-character limits | `NoteUpToTheLimit_IsAccepted` |
| 2001 characters, `\u0007` and `\u001b` are a 400 before any lock or read | `RefusedNote_Is400_BeforeAnyLockOrRead`, `Approve_RefusedNote_Is400_BeforeAnyLockOrRead` |
| The reason is checked before the note | `MissingReason_IsRefusedBeforeTheNote` |
| K-1 (both cases) | `K1_RepeatAfterTheRecord_DropsTheEditedNote`, `K1_RepeatBeforeTheRecord_RecordsTheEditedNote_ColumnKeepsTheFirst`; approve: `Approve_K1_RepeatAfterTheRecord_DropsTheEditedNote`, `Approve_K1_RepeatBeforeTheRecord_RecordsTheEditedNote_ColumnKeepsTheFirst` (review round 1) |
| No log line carries the note | `NoLogLine_CarriesTheNote` (lifecycle and services) |
| Approve with no note is unchanged | `Approve_NoNote_IsUnchanged` (null, `""`, whitespace; golden; column unchanged) |
| An approve note is recorded | `Approve_Note_IsInValuesAndInTheColumn` |
| The bulk accept carries no note (must-not-fire) | `Approve_BulkAccept_CarriesNoNote` |
| HTTP: reject and return | `InvoiceRejectReturnEndpointsAuthorizationContractTests.Note_IsRecorded`, `RefusedNote_Is400_AndWritesNothing` |
| HTTP: approve with no body, `{}`, `{"note":null}` or whitespace behaves as before | `InvoiceApprovalEndpointsAuthorizationContractTests.Approve_WithoutANote_BehavesAsBefore` |
| HTTP: approve with a note, and a refused note | `Approve_WithANote_RecordsIt`, `Approve_RefusedNote_Is400_AndWritesNothing` |
| Catalog | `DecisionActionCatalogTests.InvoiceDecisions_DeclareNoteAsAnOptionalTextAreaParameter` |
| Writers guard | `InvoiceDecisionNoteWritersGuardTests` (4 facts) |

**Seeding.** Three seeds were applied together, the note tests run, and the files restored. The run gave 10 failed of 42:

- Dropping the lifecycle's note line failed `Note_LandsInTheSameUpdateAsTheStatus` ×3.
- Passing `note` instead of `note ?? reason` to the lifecycle failed `NoNote_HashesAsBefore_AndTheColumnTakesTheReason` ×5.
- Never adding `Values["note"]` on approve failed `Approve_Note_IsInValuesAndInTheColumn` and `Approve_BulkAccept_CarriesNoNote`.

The goldens were captured on base before any source edit.

## Gates

| Gate | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/ --warnaserror` | Succeeded, 0 warnings, 0 errors |
| `dotnet test tests/unit/Sprk.Bff.Api.Tests --filter "FullyQualifiedName~Finance\|FullyQualifiedName~Signals\|FullyQualifiedName~Invoice"` | 16635 passed, 0 failed, 12 skipped (16647) |
| The new note tests, contract and catalog tests (same project, filtered by class) | 294 passed, 0 failed |
| `dotnet test tests/Spaarke.ArchTests` | 2660 passed, 0 failed |
| Publish size (`dotnet publish -c Release`). Base is 5508b576fe: the same tree with the seven changed source files checked out at base | Branch 140,440,261 B; base 140,436,149 B; delta **+4,112 B** |
| CVE (`dotnet list package --vulnerable --include-transitive`) | No vulnerable packages. No `.csproj` or props file changed |

## Deviations and known limits

1. **Approve K-1 (fixed in review round 1, F1).** Suppose the first approve fails after its record is written, at "signals" or "link". The claimant's repeat now completes whatever note it carries:
   - Completion finds the writer-created record by its key and finishes it as it is. It closes the stored set and writes the portion link. It never calls `RecordAsync` again.
   - The record and the column keep the first note. An edited or removed note is dropped, as K-1 specifies for reject and return.
   - Before the record exists, the record takes the repeat's note. The column keeps what the first transition wrote.
2. **Approve body binding.** Approve now binds an optional JSON body. No body, `{}` and `{"note":null}` behave exactly as before (tested). Two cases now fail where the body used to be ignored:
   - A malformed JSON body gets 400.
   - A non-JSON content type with a body gets 415.

   No client sends either today.
3. **Pending decision-note writers in the guard.** The release transaction and the totals path are allowed by the design but do not name the column yet. The guard lists them as allowed without a staleness check, so the later tasks that wire them need no guard edit.
4. **`reject-invoice` is `ExternallyVisible`.** The firm reads only `sprk_reason`, never the note; this task does not change that. The catalog note parameter is internal input to the record.

## Review round 1

An independent review confirmed these findings. Each is fixed on `task/ebilling-095f-2a`.

### F1: an approve stranded after its record could never complete with another note

**Before.** The first approve (note "A") failed at "signals" or "link" after the record was written. `CompleteOrNoOpAsync` counted a record as existing only when the portion linked to it. So a repeat with no note, or with "B", called `RecordAsync` with a different hash, and the writer refused it (ReviewInvalid). Every repeat answered 503 Incomplete at "record":

- the invoice was Approved;
- its Signals stayed open;
- the portion was never linked.

Only a repeat that retyped "A" exactly finished it.

**Fix** (`Services/Finance/InvoiceApprovalService.cs`), the same shape as reject and return:

- When the writer-created record exists (`recorded.Count > 0`) and the link is absent, completion passes `recorded[0]` to `FinishAsync` as the existing record. No second record is written.
- `FinishAsync`'s existing-record branch closes the stored set, then writes the portion link. That is the new `LinkAsync`, shared with the record-write path.
- The repeat keeps the FIRST record. An edited or removed note is dropped (K-1).

**Tests** (`tests/integration/data-mutation/Ebilling/InvoiceDecisionNoteTests.cs`):

| Test | Cases | On a29e59aa38 | After the fix |
|---|---|---|---|
| `Approve_K1_RepeatAfterTheRecord_DropsTheEditedNote` | "signals", "link" | 2 failed: Incomplete, not Approved | pass |
| `Approve_StrandedAfterTheRecord_CompletesOnARepeatWithAnotherNote` | "signals" and "link", each with a different note and with no note | 4 failed: Incomplete at "record" | pass. Each case checks Approved, one record (the first), the Signal closed by that record, the portion linked and Approved, and that a further repeat answers AlreadyApproved |
| `Approve_K1_RepeatBeforeTheRecord_RecordsTheEditedNote_ColumnKeepsTheFirst` | — | passed | pass |

The before-the-record case was already correct. Its test pins the K-1 behaviour and was not expected to fail.

Fail-before was shown by running the new tests on a29e59aa38's source before the service edit: 6 failed, 35 passed of 41.

**Existing test restated.** `InvoiceApprovalServiceTests.ApprovedAmount_IsCoveredByTheHash` (103 K3 SEEDED) asserted the old stranding: a repeat after "link" with a tampered portion amount answered Incomplete at "record". With the fix, that repeat finishes the stored record:

- the answer is Approved with the first record's id;
- no second record is written;
- the record's `approvedAmount` is still the frozen one, never 999;
- the portion is linked.

So the tampered amount is still never recorded. ~~That the amount is inside the record's `Values`, and so inside the hash, stays proven by `Approve_Note_IsInValuesAndInTheColumn`.~~

**Corrected in review round 2.** That statement was wrong. The amount is still in the record's `Values`, but once the record exists `RecordAsync` is no longer asked, so the hash never compares it. A repeat with a tampered portion amount answered Approved, and the portion kept the tampered value while the record held the original. The restated test asserted something true by construction. Round 2 F2 below restores the refusal.

### F2: the writers guard missed names in attributes

**Before.** `tests/Spaarke.ArchTests/InvoiceDecisionNoteWritersGuardTests.cs` read IL string loads only. A column passed as a custom attribute argument has no `ldstr`, so it was invisible to the guard. That covers `sprk_decisionnote` or `sprk_correctionnote` in, for example, `[JsonPropertyName("sprk_decisionnote")]` on a DTO.

**Fix.** The guard also reads custom attribute arguments (`CustomAttributeData`) on every scanned type and on its declared members, parameters and return values. Constructor and named arguments are both read, with arrays flattened. A hit is attributed to its place:

- a member of the outermost type: (outermost type, member name);
- a parameter or return value: (outermost type, its method's name);
- a type-level attribute: `(type)`;
- anything on a nested type: `"{nested}.{member}"`, so it can never match an allowed method name.

The "Not covered" list in the file now names what remains uncovered (limit (c) below).

**Test.** The new fact `AttributeControls` covers both columns:

- **Must fire.** One fixture per column names it only in attributes, in three places: the type (`[Description]`, a constructor argument), a DTO property (`[JsonPropertyName]`) and a parameter (a named array argument). Each place is reported.
- **Must not fire.** The lifecycle and the persister report only their reviewed entries. Each fixture is silent on the other column. An attribute naming an unrelated column is not a hit.

Fail-before was shown by commenting out the attribute scan (`naming.UnionWith(AttributesNaming(...))`) and running the guard. `AttributeControls` failed and the other 4 facts passed. The line was then restored. The real BFF and `Spaarke.Dataverse` scans stay green, so no attribute in either names a note column today.

### F2: the note rule and the reason (documentation)

`InvoiceCorrectionNote`'s summary said the type owns "the text rule, so the writers cannot drift apart". That is not the whole story. A reject or return with no note writes its REASON into `sprk_decisionnote`, and the reason is checked only by its own rule: required, trimmed, at most 2000 characters, and no control-character check.

The summary now says that the text rule applies to notes, and that with no note the column takes the reason under the reason's own rule. Reason validation is unchanged, for contract stability. This is known limit (e) below.

### F2: stale comment

`tests/Spaarke.ArchTests/InvoiceNoteColumnsGuardTests.cs` said "the BFF names neither column today". It now says:

- the BFF names `sprk_invoice.sprk_decisionnote`, through 095f-2a's lifecycle write;
- it does not yet name `sprk_invoicecorrection.sprk_note`;
- the pending list covers both.

The client file `src/solutions/InvoiceReview/src/services/decisionApi.ts` is left to task 095f-2ui.

### Gates (review round 1)

| Gate | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/ --warnaserror` | Succeeded, 0 warnings, 0 errors |
| `dotnet test tests/unit/Sprk.Bff.Api.Tests --filter "FullyQualifiedName~Finance\|FullyQualifiedName~Signals\|FullyQualifiedName~Invoice\|FullyQualifiedName~Ebilling"` | 16832 passed, 0 failed, 12 skipped (16844) |
| `dotnet test tests/Spaarke.ArchTests` | 2661 passed, 0 failed |

### Known limits

(a) **Deploy order.** Reject and return now always write `sprk_decisionnote`. This BFF must never reach an environment before `scripts/ebilling/Deploy-EbillingDecisionNoteColumns.ps1 -Apply` has created the column there:

   - **On dev:** before the work branch merges to master. Several projects deploy dev BFFs from master.
   - **In every other environment:** in SpaarkeMaster, through task 079.

   Without the column, every reject and return fails 503 at "transition", with the claim held.

(b) **The approve body binding.** Both cases below are answered by the framework before the endpoint filters run. No current caller sends either.

   - Malformed JSON gets the framework's 400, with no `errorCode`.
   - A non-JSON content type with a body gets 415.

(c) **Guard scope.** The guard does not cover:

   - **The pending writers' behaviour.** The release transaction and the totals path are allowed but not verified until their tasks move them to the built list.
   - **Overloads.** The allow-lists are keyed by method NAME, so an overload that shares an allowed method's name is allowed too.
   - **`Spaarke.Core`.** It is not scanned; only the BFF and `Spaarke.Dataverse` are.
   - **Other metadata.** Assembly- and module-level attributes, const field values, default parameter values, embedded resources and constant data are not read.
   - **Run-time names.** A name built at run time is not seen.

(d) **Publish delta.** The +4,112 B above was measured against the branch base (5508b576fe), not master, because the work branch carries unmerged code. Review round 1 changes one source file (`InvoiceApprovalService.cs`, a small refactor into `LinkAsync`) plus a doc comment, so it adds no package.

(e) **The column's text with no note is the reason.** That text is checked by the reason's rule (length only), not the note's control-character rule. Changing it would change the reject and return contract, so it is left as is.

## Review round 2

An independent re-check of round 1 (a29e59aa38..55efd48441) confirmed two regressions, each with a test. Both are fixed on `task/ebilling-095f-2a`. Reject and return are unchanged.

### F1: the repeater became the decider of a stranded approve

**Before.** Alice approves and the call stops at "signals" or "link" after her record is written. Bob, named on the portion's `sprk_decidedby` (a column a person can edit), repeats. Round 1 finished Alice's record, but `FinishAsync` closed the Signals with `decision.Decider.SystemUserId`, which is Bob. Approve's `ReplaySignalsAsync` dropped the stored record's `ConfirmedByUserId`.

**Fix** (`Services/Finance/InvoiceApprovalService.cs`), parity with reject and return (#2507 K1):

- `ReplaySignalsAsync` carries the stored record's `ConfirmedByUserId` out in `SignalRead.StoredDeciderUserId`.
- `FinishAsync` takes it as `storedDeciderUserId` and names it as the decider for every Signal close. A first attempt (no stored record) is still the caller's own decision.
- The repeater may finish the recorded decision but is never recorded as its decider. `AuthorizeCloseAsync` still runs as the caller, unchanged.

### F2: a tampered approved amount was no longer detected

**Before.** Round 1 stopped asking `RecordAsync` once the record existed, so the hash (which covers `approvedAmount`) no longer refused a repeat with a changed portion amount. `CheckCompletionAmountMismatch` runs only when no record exists. A tampered repeat answered Approved.

**Fix** (`Services/Finance/InvoiceApprovalService.cs`):

- `WriterApproveRecordRowsAsync` is the same writer-created record read as `WriterApproveRecordsAsync` (which now delegates to it), and it also returns each record's stored `approvedAmount`, read from `sprk_steps` (the taken `approve-invoice` step's `values.approvedAmount`).
- In completion, once a record exists, the stored amount of the record being finished is compared with the portion's current approved amount, after the signal read and the close-rights check and before `FinishAsync`. On a mismatch, or when the stored amount cannot be read (fail closed), the answer is what the writer's refusal used to be: Incomplete at "record" (detail `ReviewInvalid`). Nothing is written, closed or linked.
- One Warning is logged with the invoice id and the record id only, never the amounts.

### Tests (`tests/unit/Sprk.Bff.Api.Tests/Services/Finance/InvoiceApprovalServiceTests.cs`)

| Test | Cases | On 55efd48441 | After the fix |
|---|---|---|---|
| `StrandedApprove_RepeatedByAnother_ClosesForTheStoredDecider` (F1) | "signals", "link" | 2 failed: `CloseDeciders` held Bob's user twice | pass. Approved with the first record's id, `CloseDeciders` non-empty and only Alice's user, one record, the link written, both Signals resolved |
| `ApprovedAmount_IsCoveredByTheHash` (103 K3 SEEDED, F2, replaces round 1's restated test) | "signals", "link" | 2 failed: Approved, not Incomplete | pass. Incomplete at "record"; no create, no write, no close asked, nothing linked; the record keeps its amount; the one Warning carries ids only |
| `ApprovedAmount_Unchanged_StrandedApproveCompletes` (F2 must-not-fire) | "signals", "link" | passed | pass. The frozen amount written back with two decimals completes: Approved, linked, Signal resolved |

Fail-before was shown by running the new tests on 55efd48441's source before any service edit: 4 failed, 2 passed (the must-not-fire pair) of 6. In that first run the must-not-fire pair wrote a fixed 100.00 back to the portion, which is not the amount the record freezes in this fixture. With the F2 fix that pair then failed as tampered, so it was corrected to write back the record's own frozen amount, which is a true must-not-fire. The F1 fix was committed with its test alone; the F2 tests then ran green with both fixes. The round-1 tests (`Approve_StrandedAfterTheRecord_CompletesOnARepeatWithAnotherNote`, `Approve_K1_RepeatAfterTheRecord_DropsTheEditedNote`, `Approve_K1_RepeatBeforeTheRecord_RecordsTheEditedNote_ColumnKeepsTheFirst`) and the reject and return suite stay green.

### Gates (review round 2)

| Gate | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/ --warnaserror` | Succeeded, 0 warnings, 0 errors |
| `dotnet test tests/unit/Sprk.Bff.Api.Tests --filter "FullyQualifiedName~Finance\|FullyQualifiedName~Signals\|FullyQualifiedName~Invoice\|FullyQualifiedName~Ebilling"` | 16837 passed, 0 failed, 12 skipped (16849) |
| `dotnet test tests/Spaarke.ArchTests` | 2661 passed, 0 failed |

Round 2 changes one source file (`InvoiceApprovalService.cs`) and adds no package.

### Known limits (review round 2)

(f) **Writers guard attribution.** A custom attribute on a parameter or return value is attributed to its method's name. So such an attribute naming a note column on a method whose name is allowed (for example `TransitionAsync`) passes the writers guard. The guard documents this attribution rule.

(g) **The stored decider when the stored set cannot be found.** As in reject and return, the closes fall back to the caller's user only when the stored-set lookup returns no record while the writer-created record read found one. Both reads look for the writer-created record under the same key, so this is not expected on a real path; it was not made to fail closed, to keep parity with reject and return.
