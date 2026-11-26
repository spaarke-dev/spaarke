# 164a-3: the findings diff, extracted from the correction re-evaluation

2026-11-06, branch `task/ebilling-164a3` from `work/ebilling-r1` at `2377cf643a`. One piece of task 164 (`tasks/164-add-line.poml`, brief `notes/add-line-brief.md` section 3 step 3, section 5 "CRE" row, guard G5). Paths are under `src/server/api/Sprk.Bff.Api/Services/Finance/`.

## 1. What moved

The new file `InvoiceFindingsDiff.cs` holds `internal static class InvoiceFindingsDiff`. These moved out of `InvoiceCorrectionReevaluation` (CRE) unchanged apart from visibility (private to internal) and qualified names:

- `RequestBasis` (generalised, section 2);
- `PatchFindings`, `PatchPlan`, `SubjectsOf`;
- their private helpers: `KeyOf` (both), `DecisionOf`, `StoredDecisions`, `StoredKind`, `RecordPlanRefusal`, `RecordRuleChange`;
- the shared helpers `PlanKey`, `Pending`, `Text`, `IntOf`, `DateOf`;
- the records `StoredLine` and `ResolutionFacts`;
- `GapSubjects`.

CRE imports the class with `using static`, so its remaining code compiles unchanged. The one changed call is `new RequestBasis(...)`, which became `RequestBasis.ForCorrection(...)` with the same arguments. The member names `pendingSignalChanges`, `correctionReview` and `resolutionChange` stay on CRE, and the moved code refers to them there.

**Name.** `InvoiceFindingsDiff` says what the code is: the before/after diff of an invoice's stored findings for one change. It is neither correction-specific nor Add-line-specific. A name like "Reevaluation" would have clashed with CRE, which still owns the reads, the step order and the writes.

**Deviation: `GapSubjects` also moved.** It is not one of the four names in the brief. Add line needs it for "a new Procedure gap → 409" (R15, brief section 3 step 3), and the constraint "rules run only through the extracted CRE builder ... never a copy" would otherwise force a copy. It is a 5-line pure function.

## 2. The generalised `RequestBasis`

Before, the basis was built around one `CorrectionApplied`. It now has two factories and one build path:

| Kind | Factory | Build | "before" side | "after" side |
|---|---|---|---|---|
| Field correction (101) | `ForCorrection(applied, invoice, lines, provenance, verified, submissionId, root, core, resolutionBefore)` | `Build(value, preVerified, before)` | the stored lines, the field at its pre-correction value | the same lines, the field at its final value |
| Added line (164) | `ForAddedLine(added, invoice, lines, provenance, verified, submissionId, root, core)` | `Build(before)` | the stored lines | the stored lines plus `added` |

`LinesAfter` holds the "after" lines; pass it to `SubjectsOf` so that the added line is a Signal subject. `Added` is the added line, or null for a correction. Calling the other kind's `Build` throws `InvalidOperationException`.

**The added line is passed as a `StoredLine`.** That is the row the caller is about to create plus its extraction JSON, at `Index = sequence - 1`. The brief describes it as "the sequence, cost type, amounts, codes and timekeeper", and every one of those is a column of that row, so `LineFacts` reads it exactly as it reads a stored line. The reason is consistency: a correction made after an add rebuilds the added line from its stored row through the same `LineFacts`. If the add were evaluated from a separate typed record, any field read differently (confidence, as-billed codes, the timekeeper reason) would show up as a spurious finding change on the next correction.

**`ForAddedLine` refuses** (`ArgumentException`) any of these:
- a cost type other than Fee or Expense;
- an `Index` that is not `sprk_linesequence - 1`;
- an index not after every stored line;
- a row id that is already a stored line;
- a decided line.

**The correction path is unchanged by construction.** The only changes inside the builder are these:
- the primary-constructor parameters became fields;
- `applied.X` became `_applied is { } applied && applied.X`, which is always true on a correction basis;
- the lines passed to the build are the basis's own `_lines`, the same dictionary as before.

A block-by-block diff of the old private code against the new file lists nothing else.

## 3. CLAUDE.md section 11

- **Existing:** CRE's private request builder and its findings and plan patch (task 101). Nothing else rebuilds an evaluation request from stored rows or patches a stored findings JSON.
- **Extension:** this IS the extension. The code is moved, not copied: CRE calls it with a correction basis, and Add line will call it with an added-line basis. No second implementation exists.
- **Cost of doing nothing:** Add line (R6, brief section 3 step 3) would have to copy the builder and the patch. The copy drifts from the code the stored findings were built by, which breaks the "never a copy" constraint and makes later correction diffs disagree with what the add wrote.

## 4. Proof (G5): the correction suites pass unchanged

No test file of the correction suites was edited. The only other test edits are:
- the new `InvoiceFindingsDiffTests.cs`;
- one line in `tests/Spaarke.ArchTests/ProcedureReaderRedactionGuardTests.cs`. It adds `InvoiceFindingsDiff` to `ReaderSurfaces`, because the findings-entry code moved out of a listed surface and the guard would otherwise stop scanning it.

| Suite | Before (2377cf643a) | After |
|---|---|---|
| `InvoiceCorrectionReevaluationTests` | 17/17 | 17/17 |
| `InvoiceCorrectionSignalsTests` | 13/13 | 13/13 |
| `InvoiceCorrectionB2cTests` | 39/39 | 39/39 |
| `InvoiceCorrectionDurabilityTests` | 10/10 | 10/10 |
| `InvoiceCorrectionEndToEndTests` | 16/16 | 16/16 |
| `InvoiceCorrectionServiceTests` | 84/84 | 84/84 |
| `Submission.InvoiceCorrectionIntakeTests` | 48/48 | 48/48 |
| `Api.Finance.InvoiceCorrectionEndpointsAuthorizationContractTests` | 75/75 | 75/75 |
| `Api.Finance.InvoiceCorrectionPrefillContractTests` | 35/35 | 35/35 |
| `Submission.InvoiceSubmissionPersisterTests` (incl. `RerunFacts`) | 268/268 | 268/268 |
| Filtered unit set (Finance, Ebilling, Invoice, Correction) | 7991 passed, 6 skipped, 0 failed (7997) | 7999 passed, 6 skipped, 0 failed (8005 = 7997 + the 8 new) |
| `Spaarke.ArchTests` | 2105/2105 | 2105/2105 |

**New tests:** `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/InvoiceFindingsDiffTests.cs`, 8 tests over 072's real in-memory evaluator:
- the added line is a new subject, on the "after" side only;
- a rule on the added line adds its finding, its plan entry and a raise on the added line;
- must-not-fire: an added line no rule judges leaves the stored findings JSON byte-identical;
- an added line with its own finding leaves the stored lines' findings and plan entries as stored;
- DailyHoursCap (the rule that sums one timekeeper's hours per day across lines) counts the added line, so the stored first line of the day takes the over-cap finding;
- control: an added line on another day changes nothing;
- `ForAddedLine` refusals;
- each basis kind is built its own way.

**Fail-before evidence:** before this change there was no added-line basis, so the tests do not compile against `2377cf643a`. As a mutation, an "after" side without the added line (`LinesAfter = lines`) failed the 4 must-fire tests; the must-not-fire and refusal tests stayed green, as they should.

## 5. The surface the Add line service (164a) calls

```csharp
using static Sprk.Bff.Api.Services.Finance.InvoiceFindingsDiff;

var added = new StoredLine(sequence - 1, row, extraction, Decided: false);   // the row and extraction JSON it will create
var basis = RequestBasis.ForAddedLine(added, invoice, lines, ExtractionProvenance.ParseSubmission(json), verified, submission.Id, root,
    InvoiceLineDecisionService.SingleCoreOf(invoice) ?? /* refuse */);
var before = basis.Build(before: true);
var after = basis.Build(before: false);
var loaded = await load(after, ct);                                           // BillingFactLoader.LoadAsync, once
var beforeResult = evaluate(before, loaded);                                  // InvoiceComplianceEvaluator.Evaluate (pure)
var afterResult = evaluate(after, loaded);
var gapsBefore = GapSubjects(beforeResult);
if (GapSubjects(afterResult).Any(g => !gapsBefore.Contains(g))) { /* 409 procedure_gap */ }
var decided = lines.Values.Where(l => l.Decided).Select(l => l.Index).ToHashSet();
var touched = new HashSet<int>();
var storedFindings = (root["findings"] as JsonArray)?.DeepClone() as JsonArray ?? [];
PatchFindings(root, beforeResult, afterResult, decided, touched);
PatchPlan(root, beforeResult, afterResult, storedFindings, SubjectsOf(invoiceId, root, basis.LinesAfter), decided, touched);
```

`RequestBasis.Build` throws `CorrectionFirmMissingException` when the invoice has no firm (as for a correction).

## 6. Open points for the service piece (not decided here)

1. **As-billed codes (T6c).** `ExpenseRuleHandler` and `AddedFeeHandler` judge the BILLED code (D-117), which is read from `sprk_*codeasbilled`. For a reviewer-typed expense code to meet the same rules as an imported one, the added row needs its as-billed codes set; the recommendation is to set them to the typed codes. The resolved codes are read from `extraction.coding.{task,activity,expense}.code`.
2. **Field confidence.** It is read from the extraction's `fields.{name}.combined`. A row without them reads no confidence. Whatever 164a decides (for example 1.0 for reviewer-typed fields) must be in the stored extraction JSON, so that later rebuilds match.
3. **Timekeeper reason.** A Fee line with no roster match reads its unmatched reason only from `extraction.resolutionChange.after`, as for every stored line. Without it the reason is `None`.
4. **Where the Signal changes land.** `PatchPlan` records rule changes in `pendingSignalChanges.rule`, the correction member that only CRE's Signals step reads. The brief (section 3 steps 6 and 9) names `addLine.signals`. The service either moves the entries into its own member in the same write, or reconciles from `pendingSignalChanges`. If entries stay there, a later correction's Signals step would reconcile them.
5. **The reads.** `ReadLinesAsync` (lines by `sequence - 1`, decided flag, extraction parse) and `ReadCorrectionsAsync` (the source of the `verified` set) are still private to CRE; this piece's scope was the four names. To avoid a copy, extract them as statics in 164a.
6. **Touched lines.** `touched` includes the added index. The added row is created in the commit already Flagged (R10), so a status step must not try to update it as a stored row.
