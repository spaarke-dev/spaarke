# 095f-2b: adjustment Decision Records (add-adjustment, remove-adjustment) and the adjustment note

> **Task:** `tasks/095f-2b-adjustment-decision-records.poml` · **Design:** `095f-part2-decision-notes-design.md` §1, §3 (option A), §8–§11, §14, §15 (EB-D-241) · **Base:** `task/ebilling-095f-2b` from `origin/work/ebilling-r1` (e171823a5b plus the docs checkpoint 017f6d23fc) · **Status:** built and tested. Code-review, adr-check and the adversarial pass are for the coordinator.

An invoice-level adjustment's add and its removal each file ONE Decision Record under the invoice, after the adjustment line and the totals land. The optional note is in the record's Values and, through the totals' own conditional update, in `sprk_invoice.sprk_decisionnote`. An add with no note writes its reason there; a removal with no note leaves the column as it is (a removal has no reason). `sprk_correctionnote` is never written.

## What was built

| Item | Where |
|---|---|
| `add-adjustment` and `remove-adjustment` in `DeclaredBilling` (diff below). Gates as `add-line` (Tier2b, reversible, record-of-truth impact; not externally visible); record class Judgement | `Services/Signals/Actions/DecisionActionCatalog.cs` |
| `IInvoiceTotalsService.RecalculateAsync(invoiceId, decisionNote, ct)`. A non-null note is added to the totals' one conditional invoice update as `sprk_decisionnote`; null leaves the column. The two-argument overload forwards with null, so every other caller is unchanged | `Services/Finance/InvoiceTotalsService.cs` |
| `AddAdjustmentCommand` and `RemoveAdjustmentCommand` gain `Note` (last, default null). The service takes the `BillingDecisionRecorder` | `Services/Finance/InvoiceAdjustmentService.cs` |
| The note is checked with `InvoiceCorrectionNote.Check` / `DecisionRefusalOf` (shared codes `ebilling.note_too_long`, `ebilling.note_invalid_characters`) before the lock and any read, on both acts. The detail never echoes the note | same |
| Step 3 of each act: the record, after the line (or the deactivation), the totals and the re-index enqueue | same |
| Add body gains `note`; the remove route takes an optional JSON body `{ "note": ... }` (`EmptyBodyBehavior.Allow`: no body, `{}` and `{"note":null}` all mean no note) | `Api/Finance/InvoiceAdjustmentEndpoints.cs` |
| ArchTest: the totals path moves from the pending to the built `sprk_decisionnote` writers (its stale check now applies); the pending list is empty | `tests/Spaarke.ArchTests/InvoiceDecisionNoteWritersGuardTests.cs` |

**Record content.** Decision id (as amended in review round 1, below): `InvoiceAdjustmentService.AddDecisionIdFor(invoiceId, adjustmentId)` for an add, `RemoveDecisionIdFor(invoiceId, lineId)` for a removal, both namespaced hashes (first build: the raw client `AdjustmentId`, and `IdOf("remove-{lineId}")`). Subject: the invoice. Values are the declared parameter codes only: add = `method` (`percent` / `fixed`), `value`, `rule` (only when cited), `note` (only when given); remove = `adjustment` (the line id), `note` (only when given). No note means no key, never `note: null` (the hash covers every key). Reason: the add's reason; none for a removal. Facts (outside the hash): invoice id, adjustment line id, amount (add), approved total. Written: the adjustment line. No writer literal was edited (every code outside the four line codes already maps to the invoice).

## Rules as built

1. **Order.** Request shape → note → lock → reads → firm gate → (add) record pre-check, rule check, dry run → record preflight (no I/O) → the first write → totals (+ column) → re-index → record.
2. **Repeats look the record up first** (`BillingDecisionRecorder.FindStoredResolvedSetAsync`, read-only), so an edited-note repeat never overwrites the column:
   - record exists → the totals run without the column, and the writer's hash decides: same content → applied, `alreadyRecorded` inside the writer, one record; another note (edited, dropped or added) → the writer's other-content refusal → **409 `ebilling.adjustment.id_reused`**, no second record, the column keeps the first note.
   - record missing (a strand) → the repeat's own note goes to both the column and the record (one record).
3. **A repeat by another reviewer** (add or removal) is 409 `id_reused` before the totals or the writer: the record names its decider, and a repeat never makes someone else the decider (the add-line precedent and 2a's "repeater became decider" finding). **Behaviour change:** before 095f-2b such a repeat answered 200.
4. **A first add whose adjustment id already has an `add-adjustment` record** is 409 `id_reused` before anything is written (otherwise it would file a line whose record could never be written: a 503 no repeat could finish).
5. **An edited method, value, reason or rule on a repeat** is 409 `id_reused` before the totals and the writer (the existing same-adjustment check), also after a strand: never a 503 (ISS-029).
6. **503-then-repeat.** A totals failure is 503 `ebilling.adjustment.incomplete` (as before); a record failure other than other content is 503 `incomplete` at the record step, and the same request repeated finishes it with exactly one record. The removal's record-step message does not say "saved".
7. **Preflight.** The writer's no-I/O refusals run before the first write (400 `DecisionInvalid`, nothing written).
8. **Logs** carry ids, the step and an exception type or refusal reason only, never the note.

## Tests

New file `tests/integration/data-mutation/Ebilling/InvoiceAdjustmentServiceTests.DecisionRecords.cs` (a partial of the adjustment test class, over its World: the REAL adjustment service, totals service, recorder and writer harness). The World now carries the writer harness, an identity double, capturing loggers, every totals write's fields, and an optional fixed invoice id. The contract host (`InvoiceAdjustmentEndpointsAuthorizationContractTests`) gains the real recorder and the three-argument totals setup, plus HTTP tests.

| POML acceptance criterion | Test |
|---|---|
| Repeated AdjustmentId, same content → one record (AlreadyRecorded, one create) | `Add_ARepeatWithTheSameContent_FilesOneRecord`, `Remove_ARepeatWithTheSameContent_FilesOneRecord` |
| Remove then add of a new adjustment → two records | `ARemovalThenANewAdd_FilesTwoRecords` |
| Repeated AdjustmentId changing only the note → 409 `id_reused`, no second record | `Add_ARepeatThatChangesOnlyTheNote_Is409IdReused_NoSecondRecord` (edited, dropped, added) |
| Repeated remove with an edited note → 409, no second record | `Remove_ARepeatWithAnEditedNote_Is409IdReused_NoSecondRecord` (edited, dropped, added) |
| Each new entry has a Gate | `DecisionActionCatalogTests.EachBillingAction_HasItsRecordClassAndGateTier` (+2 rows), `BillingActions_AreExactlyTheStatedSet_…` |
| Writer refuses `add-adjustment` without a reason, accepts `remove-adjustment` with a null reason | `TheWriter_RefusesAnAddWithoutAReason`, `TheWriter_AcceptsARemovalWithANullReason` |
| No note → no key; an add puts the reason in the column | `Add_WithoutANote_HasNoNoteKey_HashesAsTheGolden_AndTheReasonIsTheColumn`, `Remove_WithoutANote_…_AndLeavesTheColumn` (null, empty, whitespace) |
| The 2a validation set applies | `Add_ARefusedNote_Is400_BeforeAnyLockOrRead`, `Remove_ARefusedNote_Is400_BeforeAnyLockOrRead` (2001 chars, `\u0007`, `\u001b`); must-not-fire `ANoteAtTheLimit_OrWithALineBreakOrTab_IsAccepted` (2000 chars, `\n`, `\t`); HTTP `Add_/Remove_ABadNote_Returns400_WithTheSharedCode_BeforeAnyRead` |
| Amended exact-set tests; BillingActions gains exactly the two codes | `BillingActions_AreExactlyTheStatedSet_…`, `Billing_C2_TheBillingEntrysSubjects_…`, `ABillingCode_IsRefusedByTheCommitRoute_…` (+2 rows) |
| Note in Values and in the column | `Add_FilesOneRecord_WithTheDeclaredValues_AndTheNoteInTheColumn`, `Remove_FilesOneRecord_…`, `Add_AFixedAdjustmentCitingARule_CarriesTheRule`; HTTP `Add_/Remove_WithANote_Succeeds_…` |
| Brief extras | strand → repeat → one record (`Add_/Remove_StrandedAtTheRecordStep_…`, `Add_StrandedAtTheTotals_ARepeatWithAnotherNote_…`); ISS-029 (`Add_ARepeatThatEditsOtherContent_Is409_…`, `Add_AfterAStrand_ARepeatThatEditsOtherContent_Is409`); reviewer (`Add_/Remove_ARepeatByAnotherReviewer_Is409`); first add recorded (`Add_AFirstAttemptWhoseIdIsAlreadyRecorded_Is409_NothingWritten`); no log line carries the note (`NoLogLine_CarriesTheNote`, four loggers); must-not-fire `sprk_correctionnote` (`TheNote_IsOnlyOnTheRecordAndTheDecisionColumn`); remove body binding (`Remove_WithoutANote_Succeeds_AndWritesNoNote`: no body, `{}`, `{"note":null}`) |

**Golden hashes.** Before 095f-2b no adjustment had a record, so no pre-change hash exists. The two no-note hashes were pinned from the first 095f-2b build on a fixed world (fixed invoice, adjustment and line ids): add `B05596B1…F79531`, remove `4B6F19E4…B5AEE6`. Null, empty and whitespace notes hash the same. They guard the no-note content from here on.

**Fail-before evidence.** The old service cannot compile against the new tests (new constructor parameter, new command members), so the evidence is (a) the old catalog against the new code and tests, and (b) one mutation at a time of the committed code, each restored with `git checkout`:

| Change | Tests that fail |
|---|---|
| Base `DecisionActionCatalog.cs` | 100 of 1,074 (catalog exact set, gate, parameter and note rows; writer subject set; commit-route rows; every adjustment test that reaches the record) |
| Add repeat skips the reviewer check | 1 (`Add_ARepeatByAnotherReviewer_Is409`) |
| Removal repeat skips the reviewer check | 1 (`Remove_ARepeatByAnotherReviewer_Is409`) |
| Other-content refusal answered 503 | 6 (both edited-note theories) |
| First-add record pre-check dropped | 1 |
| A recorded repeat still writes the column | 3 |
| Note key always present (null when none) | 10 (goldens, no-key, HTTP no-body rows, rule test) |
| Add with no note does not write its reason | 3 |
| Removal ignores its note | 15 |
| The service logs the note | 1 (`NoLogLine_CarriesTheNote`) |
| Repeats skip the same-adjustment check | 6 (both ISS-029 theories) |
| Repeats skip the record step (the pre-change behaviour) | 13 |
| Totals path stops writing the column | 9 adjustment tests + the ArchTest `OnlyTheReviewedWritersNameTheDecisionNote` (stale check; the old guard does not fire, the totals path was only "pending" there) |

## Gates

| Gate | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/ --warnaserror --no-incremental` | 0 warnings, 0 errors |
| BFF tests, filter `Adjustment\|InvoiceTotals\|DecisionRecordWriter\|DecisionActionCatalog\|DecisionNote\|InvoiceLineDecision\|InvoiceApproval\|SubmissionOverride` | 1,819 passed, 0 failed (2,079 with `DecisionCommitService` added) |
| ArchTests, filter `Invoice\|Decision\|Catalog` | 151 passed, 0 failed |
| Publish size (Release, clean folders, base e171823a5b vs tip, from `git archive` trees at short paths) | 140,470,713 → 140,484,925 bytes (+14,212 B, +0.01 MB); 191 files each; `Sprk.Bff.Api.dll` +12,288 B. A fresh `origin/master` build was not practical (another worktree is outside this agent's isolation), so the base sha was used, as the brief allows |
| `dotnet list package --vulnerable --include-transitive` | no vulnerable packages; no package or project file changed |

## Ontology-r1 review items

1. **Catalog entries** (`DecisionActionCatalog.cs`, `DeclaredBilling`, appended after `add-line`):

```diff
+        // ---- Invoice-level adjustments (ebilling-r1 095f-2b, EB-D-241; design 3 option A; subject: the invoice) --------------------------
+        // Recorded only by the adjustment service, after the adjustment line and the totals land, never through the Signals commit route
+        // (BillingActions only). The gates follow add-line: a reviewer-made change to the invoice's totals that a later decision can undo; the
+        // firm sees the resulting totals, not the record. Each parameter code is its Values key. remove-adjustment declares no reason: the
+        // remove route takes none.
+        new("add-adjustment", "Add adjustment", DecisionLane.Decide, IsNextStep: false, WorkType: null,
+            [
+                new("method", "Method", DecisionParameterKind.Choice, Required: true,
+                    Options: [new("percent", "Percent of fees after line adjustments"), new("fixed", "Fixed amount")]),
+                new("value", "Value", DecisionParameterKind.Money, Required: true),
+                new("rule", "Billing rule", DecisionParameterKind.Lookup, Required: false, LookupEntity: "sprk_policyversion"),
+                new("reason", "Reason for the adjustment", DecisionParameterKind.TextArea, Required: true, Hint: ReasonHint),
+                NoteParameter(),
+            ],
+            ["Adds an invoice-level adjustment (a percent of fees or a fixed amount)", "Recomputes the invoice totals"],
+            Excludes: [],
+            DecisionRecordClass.Judgement)
+        { Gate = new DecisionGateFactors(GateRiskTier.Tier2b, Reversible: true, RecordOfTruthImpact: true) },
+
+        new("remove-adjustment", "Remove adjustment", DecisionLane.Decide, IsNextStep: false, WorkType: null,
+            [
+                new("adjustment", "Adjustment", DecisionParameterKind.Lookup, Required: true, LookupEntity: "sprk_billingevent"),
+                NoteParameter(),
+            ],
+            ["Removes an invoice-level adjustment (the line is kept, inactive)", "Recomputes the invoice totals"],
+            Excludes: [],
+            DecisionRecordClass.Judgement)
+        { Gate = new DecisionGateFactors(GateRiskTier.Tier2b, Reversible: true, RecordOfTruthImpact: true) },
```

2. **Exact-set amendment** (D-100 "additive only" is amended, not just added to):

```diff
 // tests/unit/domain/Signals/DecisionActionCatalogTests.cs
-        ["accept-line", "reduce-line", "disallow-line", "approve-invoice", "reject-invoice", "return-to-firm", "override-submission", "assign-firm", "add-line"];
+    [
+        "accept-line", "reduce-line", "disallow-line", "approve-invoice", "reject-invoice", "return-to-firm", "override-submission", "assign-firm", "add-line",
+        "add-adjustment", "remove-adjustment",
+    ];
+    [InlineData("add-adjustment", DecisionRecordClass.Judgement, GateRiskTier.Tier2b)]
+    [InlineData("remove-adjustment", DecisionRecordClass.Judgement, GateRiskTier.Tier2b)]
+    [InlineData("add-adjustment", "method", "value", "reason")]
+    [InlineData("remove-adjustment", "adjustment")]
+    [InlineData("add-adjustment")]     // note theory
+    [InlineData("remove-adjustment")]  // note theory

 // tests/unit/Sprk.Bff.Api.Tests/Services/Signals/DecisionRecordWriterTests.Billing.cs (Billing_C2 expected subjects)
+            ("add-adjustment", "sprk_invoice"), ("remove-adjustment", "sprk_invoice"),

 // tests/integration/data-mutation/Signals/DecisionCommitServiceTests.cs (ABillingCode_IsRefusedByTheCommitRoute_…)
+    [InlineData("add-adjustment")]
+    [InlineData("remove-adjustment")]
```

## Known limits (K-class, none is a defect on a real path)

- **K1 (503 that a repeat may not finish).** A stored record whose content cannot be read, or another billing action filed under the same decision id, is not proof that the caller changed its request, so it stays 503 `incomplete` at the record step (as add-line). Only the writer's other-content refusal is 409.
- **K2 (column on a strand repeat).** If an add or removal strands before its record and a later decision then writes `sprk_decisionnote`, a repeat of the stranded request writes its own note (or reason) to the column again, so the column can show the older decision's note. The records are correct; the column is "latest written", as in 2a.
- **K3 (deploy order).** As 2a: the `sprk_decisionnote` column script must run before this BFF is deployed, or every adjustment's totals update fails (503 `incomplete`, line written; a repeat after the column exists finishes it).
- **K4 (503 shape).** The adjustment 503 keeps its existing body (`errorCode` + detail); it states no `failedStep` field. The detail sentence names the step.
- **K5 (reviewer repeat).** A repeat by another reviewer used to answer 200; it is now 409 `id_reused` (rule 3).

## Noticed, not changed (pre-existing)

- When a removal's totals recalculation is refused after the row was deactivated, the row stays inactive and the stored totals are stale until the next recalculation (the add path undoes its row; the remove path has no undo). Out of scope for 095f-2b; report to the coordinator for filing.
- The pre-commit hook's lint-staged backs up the index with `git stash` (its own entry, dropped on completion). It touches the stash stack shared across worktrees (FAILURE-MODES G-18).

## Review round 1

Two reviews (Opus, ontology-r1) independently found F1. The fix is on `task/ebilling-095f-2b`, after `22d4c29d36`.

### F1: the add's decision id is derived on the server

- **Defect.** A client-supplied id could coincide with another decision's id on the invoice. The writer keys records by subject and decision id (no action code), and every other billing decision id is a deterministic namespaced hash, so the raw `AdjustmentId` used as the add's decision id could block a later decision on the same invoice.
- **Fix.** `InvoiceAdjustmentService.AddDecisionIdFor(invoiceId, adjustmentId) = InvoiceLineDecisionService.IdOf("ebilling-add-adjustment-{invoiceId:D}-{adjustmentId:D}")`, following add-line's `DecisionIdFor`. It is used for the record, `IsRecordedAsync` on a repeat and the first-add pre-check. Idempotency per `AdjustmentId` is unchanged (the same invoice and id give the same decision id; the row and the id-reuse checks are as before).
- **Removal id.** Renamed for namespace consistency: `RemoveDecisionIdFor(invoiceId, lineId) = IdOf("ebilling-remove-adjustment-{invoiceId:D}-{lineId:D}")` (was `IdOf("remove-{lineId:D}")`). Nothing is deployed, so there are no records under the old ids and nothing is migrated.
- **Golden hashes.** Unchanged (`AddNoNoteHash`, `RemoveNoNoteHash`): the review hash covers the content (values, reason), not the decision id, so nothing was re-captured. The golden tests pass untouched.
- **Tests** (`InvoiceAdjustmentServiceTests.DecisionRecords.cs`, region "Review round 1", 15 new test cases; the three existing tests that named the old ids now use the derived ones):
  - the decision id is never the raw `AdjustmentId` (pure test, pinned to the hash input), and the record's key is the derived id;
  - the same `AdjustmentId` or line id on two invoices gives different ids, and an add and a removal never share one;
  - an add whose `AdjustmentId` equals approve's, reject's or return's decision id does not block that decision, in both orders (add then decision; decision then add);
  - a decision filed under a removal's earlier id (`remove-{lineId}`) no longer blocks the removal (three decision kinds);
  - same-content repeats still file one record, and a changed note is still 409 `id_reused`.
- **Fail-before.** With only the two id functions set back to the first-build values (raw `adjustmentId`, `IdOf("remove-{lineId}")`) and the lookup wording below set back, 14 of the 15 new cases fail: the add-then-decision, decision-then-add and removal cases for all three decision kinds, the derived-key test, the two-invoice test, the pure id test and the two lookup-wording tests. The 15th (same-content repeats, 409 on a changed note) passes before and after by design, as it guards that idempotency did not change. With the fix restored, 15 of 15 pass. The set-back functions are the `22d4c29d36` behaviour for the ids and the wording; the file was restored from a copy afterwards.

### F1 (low): a faulted stored-record lookup named the wrong step

- **Defect.** When the stored-record lookup (`IsRecordedAsync`) faulted on a repeat, the answer was `Incomplete(RecordStep)`, whose text says the totals were updated. After a strand at the totals step they may not have been.
- **Fix.** A new `LookupFailed` answers 503 `incomplete` with neutral wording: "The adjustment is not finished. Repeat the same request to complete it." (removal: "The removal is not finished. ..."). It names neither the totals nor the record step. The next repeat, with the lookup healed, finishes whatever is left. The path where `RecordAsync` itself faults keeps its record-step wording (the totals did land there).
- **Tests.** An add and a removal each stranded at the totals step, then repeated with the stored-record lookup faulting: 503, the neutral wording, no claim that the totals were updated, no totals write by the faulted repeat; then a healed repeat is applied with one record. Both fail before, pass after.

### Counts and gates

- Filtered `Sprk.Bff.Api.Tests` (Adjustment, InvoiceTotals, DecisionRecordWriter, DecisionActionCatalog, DecisionNote, InvoiceLineDecision, InvoiceApproval, InvoiceRejectReturn, SubmissionOverride, DecisionCommit): 2236 passed, 0 failed (includes the 15 new). `Spaarke.ArchTests` (Invoice, Decision, Catalog): 151 passed. `dotnet build src/server/api/Sprk.Bff.Api/ --warnaserror`: 0 warnings, 0 errors.
- Publish size: two small functions added; not re-measured this round (limit d).

### Known limits (this round)

- **(a)** A strand repeat can rewrite an older adjustment's note into `sprk_decisionnote` over a newer one. Narrow: a record-step fault, a second reviewer and a repeat. The column has no reader today; revisit if one is added (see K2).
- **(b)** An unreadable stored record on a repeat is a 503 that no repeat finishes. This matches the add-line precedent (see K1).
- **(c)** Behaviour change: a repeat by a different reviewer is 409 `id_reused` (see K5). On a removal this is reachable from a stale page; the 095f-2ui client should reload the list on that 409.
- **(d)** Publish size was measured against the base sha, not a fresh master build; it will be re-measured at the master merge.
- **(e)** ISS-030 (filed separately): a refused recalculation after a removal leaves the line deactivated with stale totals. Pre-existing; this task means its record is also never written.
