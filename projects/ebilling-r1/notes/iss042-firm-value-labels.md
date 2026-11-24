# ISS-042 (#2933): firm labels for the values a rejection finding read

Task: `tasks/iss042-firm-value-labels.poml`. Rigor FULL. Branch `task/ebilling-iss042` (base `f5ec8b6a20`).

## What changed

- `Services/Finance/FirmValueReadLabel.cs` (new): the closed key → label table, `For(table, name, logger, submissionId)` with the generic fallback `Value`, and a Warning (`EBILLING-FIRM-OUTCOME-VALUE-LABEL-MISSING`, server side, key only) for an unknown name.
- `Services/Finance/FirmFindingsProjection.cs`: `FirmOutcomeValueRead.Name` is now the label (one line at the projection of a rejection finding, plus two doc comments). The value and the wire shape (`{ name, value }`) are unchanged. No SPA change: it renders `name` as received, and its tests already use label-style names.
- The stored findings document keeps the raw key (`valuesRead[].name`); only the firm projection maps it.

## Component justification (root CLAUDE.md section 11)

1. Existing: overlaps `FirmRejectionReasonText` (a closed firm-safe table with a generic fallback and a guard). 2. Extension: not added to that file because its key is a reason code and its entries are sentences; this table's key is a value name and its entries are column labels (a different reason to change). It copies the same shape. 3. Cost of doing nothing: a firm reads `line.rate: 650` in the SPA outcome report, the PDF and the notice (`FirmNoticeComposer.ReasonCell` prints `{Name}: {Value}`).

No new endpoint, service, DI registration, package or column. `FirmNoticeComposer` (firm notice and PDF) reads `FirmOutcomeItem.ValuesRead[].Name` after the projection, so the label now reaches those surfaces too; it prints the name as text and never keys on it.

## Who reads `FirmOutcomeValueRead.Name` (the stop condition)

`grep FirmOutcomeValueRead` over `src`: the record, the projection (the only constructor call), `FirmNoticeComposer.ReasonCell` (prints `{Name}: {Value}`, firm-visible) and the SPA client and report (display). No internal surface keys on it. Internal surfaces (`InvoiceReviewReadService`, `InvoiceFindingsDiff`, `SubmissionOverrideService`, `SubmissionRejectionRule`, `BillingFindingSignalMapper`) read `BillingValueRead.Name` / the stored `valuesRead`, which keep the raw key.

## Key → label table, with the producer of each key

Only an extracted value reaches a firm: `FirmRejectionFindings.ParseEntry` drops a value with no `field` (a reference value: `approvedRate*`, `rateCard.*`, `coreRecord.*`, `duplicateOf.*`, `timekeeper.unmatchedReason`, `approvedRate.status`), a Duplicate's values, a withheld group's values, and a value whose field is AI-assigned (`line.observation`, task, activity and expense codes). So the table lists extracted names only. Derived from the handler and evaluator source (the guard scans it).

| Key | Label | Producer (under `Services/Signals/Billing`) |
|---|---|---|
| `line.rate` | Rate | `ApprovedRateHandler` (found and not-found paths) |
| `line.hours` | Hours | `HandlerContext.LineHours` (ApprovedRate, Duplicate) and `HandlerContext.WithCreditTestReads` (credit-test reads on ApprovedRate, ExpenseRule, InvoiceAge, CoreRecordWindow) |
| `line.amount` | Amount | `ExpenseRuleHandler`, `HandlerContext.WithCreditTestReads`, `DuplicateHandler` |
| `line.date` | Line date | `HandlerContext.LineDate` (ApprovedRate, ApprovedTimekeeper, ExpenseRule, InvoiceAge, CoreRecordWindow, Duplicate) |
| `line.lineDate` | Line date | `RequiredDataHandler` (`"line." + field`, `LineOrder`) |
| `line.timekeeperName` | Timekeeper | `HandlerContext.LineTimekeeper` (ApprovedRate, ApprovedTimekeeper, Duplicate) |
| `line.timekeeper` | Timekeeper | `RequiredDataHandler` (`LineOrder`) |
| `line.narrative` | Description | `DuplicateHandler`, `RequiredDataHandler` |
| `line.expenseCode` | Expense code | `ExpenseRuleHandler`, `RequiredDataHandler` |
| `line.taskCode` | Task code | `RequiredDataHandler` (the firm-billed code; field `BilledTaskCode`, not AI-assigned) |
| `line.activityCode` | Activity code | `RequiredDataHandler` (firm-billed) |
| `invoice.date` | Invoice date | `InvoiceAgeHandler` |
| `invoice.invoiceDate` | Invoice date | `RequiredDataHandler` (`HeaderOrder`) |
| `invoice.firmInvoiceNumber` | Invoice number | `DuplicateHandler`, `RequiredDataHandler` (`HeaderOrder`) |
| `invoice.billingPeriodStart` | Billing period start | `RequiredDataHandler` (`HeaderOrder`) |
| `invoice.billingPeriodEnd` | Billing period end | `RequiredDataHandler` (`HeaderOrder`) |
| `invoice.currency` | Currency | `RequiredDataHandler` (currency not USD), `InvoiceComplianceEvaluator.RatesNotChecked` |

Unknown name: `Value` (never the key). The coverage guard excludes two producer files whose rule types can never reject (asserted against the catalogue): `AiObservationHandler` (I-5) and `BillingSystemFindings` (SubmissionIntegrity; its `invoice.invoiceNumber` never reaches a firm).

Judgement call for the owner: `line.date` is labelled "Line date" (not "Date") so it cannot be confused with "Invoice date" in a finding that shows both; `line.narrative` is "Description" (the firm's word; the outcome line excerpt calls it the description).

## Tests

New: `FirmValueReadLabelGuardTests` (tests/unit/Sprk.Bff.Api.Tests/Services/Finance): scans the producers for every name given to an extracted value (call sites of `LineField`, `HeaderField`, `Extracted`, the `ExtractedField` initialisers, and `RequiredDataHandler`'s concatenated names expanded from its own ordered lists); fails on a name with no label, a label no producer emits, a name the scan cannot read, a non-sentence-case label, or a label that is a reason code or rule-type name; with must-fire and must-not-fire controls.
New in `FirmFindingsProjectionTests`: ISS-042 must-fire `Rejected_ApprovedRateValues_ShowFirmLabels_NotInternalKeys`, `Rejected_AnUnknownValueName_ShowsTheGenericLabel_NeverTheKey`, `Rejected_EveryLabelledName_ProjectsItsTableLabel`.
Updated (raw key replaced by label, or an arbitrary stored name replaced by a real key / the generic label): 13 tests in `FirmFindingsProjectionTests` and 1 in `FirmOutcomeScopeTests.cs` (the HTTP happy path now asserts "Rate" and "Hours" and that `line.rate` / `line.hours` are absent from the body).

## Fail-before evidence

1. Projection line reverted to `new FirmOutcomeValueRead(v.Name, v.Value)` with the new and updated tests in place: 16 failed of 303 (`FirmFindingsProjectionTests` + `FirmOutcomeScopeTests`), including all 3 ISS-042 projection tests. Restored: 303 pass.
2. Guard: the `["line.narrative"] = "Description"` row deleted from the table: `FirmValueReadLabelGuardTests` 1 failed ("line.narrative: a producer emits it, but it has no non-blank label"). Restored: 7 pass.

## Not done / noted

- Not run: the whole `Sprk.Bff.Api.Tests` or ArchTests (by instruction). Run: the filtered classes listed in the task report, plus `FirmFindingsProjectionCallerBoundaryGuardTests` and `FindingsJsonAppOnlyReadGuardTests` (ArchTests, 12 pass).
- The SPA was not touched; no SPA test run was needed.

## Fix round 1

Review findings at b7c5fc8393. Test and guard changes only; `FirmValueReadLabel`'s table and runtime behaviour are unchanged (one doc-comment sentence in `FirmValueReadLabel.cs` now says what the guard does).

### F2 (1): `FirmValueReadLabelGuardTests` failed open on ordinary call shapes

Change, all in `Scan` (`FirmValueReadLabelGuardTests.cs`):

- (a) Every non-definition call matching `\b(?:LineField|HeaderField|Extracted)\s*\(` is counted per file and must equal the calls the scan parsed; a mismatch fails (`N LineField/HeaderField/Extracted call(s) but M the scan can read`). A definition is a call whose list starts with a typed parameter (`string name`, `BillingLineFacts line`, `FieldRef field`), so `Extracted(string.Format(...))` is not mistaken for one. A reference to a producer that is not a call (`var pick = context.LineField;`) fails. The call patterns now allow whitespace before `(`, so a spaced call is read, not missed.
- (b) `with\s*\{[^}]*\bName\s*=` fails.
- (c) A `name` argument is allowed only in `HandlerContext.cs`; the `"line." + field` and `"invoice." + field` expansion only in `RequiredDataHandler.cs`. Either shape elsewhere fails. A local `name = ...` declaration in `HandlerContext.cs` fails too (elsewhere a forwarded `name` already fails; a blanket ban hit two unrelated files, `ProcedureResolver.cs` and `DailyHoursCapHandler.cs`).
- The class remarks and the `FirmValueReadLabel.cs` remark now list exactly what the guard catches and what it does not read.

New cases (all in `FirmValueReadLabelGuardTests`):

| Case | Test |
|---|---|
| indexer first argument, method-call first argument, named arguments, spaced `LineField (`, spaced `HeaderField (`, bare method-group reference | `MustFire_AnUnreadableCallShape_Fails` (Theory, 6 rows) |
| `context.LineHours(line) with { Name = ... }` | `MustFire_AWithExpressionThatSetsName_Fails` |
| forwarding wrapper outside `HandlerContext.cs` | `MustFire_AForwardingWrapper_OutsideHandlerContext_Fails` |
| local `name` with a computed value, passed to a producer | `MustFire_ALocalNameWithAComputedValue_Fails` |
| local `name` inside `HandlerContext.cs` | `MustFire_ALocalNameWithAComputedValue_InHandlerContext_Fails` |
| `"line." + field`, `"invoice." + field` outside `RequiredDataHandler.cs` | `MustFire_AConcatenatedName_OutsideRequiredData_Fails` (Theory, 2 rows) |

Must-not-fire: the real producer tree (`TheTable_CoversExactlyTheNamesTheProducersEmit`) is clean; `MustNotFire_ReadableProducers_ReportNothing` now puts the helper definitions in `HandlerContext.cs` and adds a `with { Value = ... }` (a `with` that does not rename).

Must-fire evidence: the 12 new test cases above were spliced into the round-0 guard (b7c5fc8393 `Scan` and `Violations`) and run: 12 failed, 7 passed (the 7 are the round-0 tests that were already there). With the new guard restored, all pass.

### F2 (2): `Rejected_EveryExtractedFieldConstant_StillShows_IncludingCaseAndPaddingVariants`

It now collects `expectedValues` (`SHOWN-{c}-{i}`, in order) next to `expectedNames` (all `GenericLabel`) and asserts both `Select(v => v.Name).Should().Equal(expectedNames)` and `Select(v => v.Value).Should().Equal(expectedValues)`. A projection that dropped or re-ordered a value, or showed another value, now fails (the count of names alone passed).

### Nit: `FirmNoticeComposerTests`

The label `"Billed rate"` is now `"Rate"` (the projection's label for `line.rate`), in the value read and in the HTML assertion.

### Run

Filtered `FirmValueReadLabel*`, `FirmFindingsProjectionTests`, `FirmNoticeComposerTests`: 323 passed, 0 failed. `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors.
