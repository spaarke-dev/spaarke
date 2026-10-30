# Task 101 part B1 - Corrections capture (FR-31, FR-22, D-64)

Branch `task/ebill-101-corrections` (base `b66b3b5230`). Rigor FULL. Part B2 (re-evaluation of the billing rules after a correction) is a later agent.

## 1. What was built

| Piece | File |
|---|---|
| Service | `Services/Finance/InvoiceCorrectionService.cs` (concrete, scoped, ADR-010) |
| Routes | `Api/Finance/InvoiceCorrectionEndpoints.cs`: `POST /api/finance/invoices/{invoiceId}/lines/{lineId}/corrections`, `POST .../invoices/{invoiceId}/header/corrections`, `GET .../invoices/{invoiceId}/corrections?sourceCode=&sourceVersion=` |
| Code-set read | `CodeSetResolver.LoadRecordedAsync` (the line's recorded code set, not a second resolution order) |
| Registration | `FinanceModule` (`AddScoped<InvoiceCorrectionService>`), `EndpointMappingExtensions` |
| Guards | route ledger 146 -> 147 (the caller reconciles at merge), `RecordOwnerAssignmentCensusTests` unscanned-writer entry, `FindingsJsonAppOnlyReadGuardTests` reviewed type (header provenance read, app-only; task 122 notes row 7) |

Auth shape is `InvoiceAdjustmentEndpoints`: group with `RequireAuthorization` and `RequireInternalFinanceCaller` (step 0), `AddFinanceAuthorizationFilter` on the route's invoice
(`ebilling.invoice.decide` for the two writes, `finance.read` for the list), then `RequireEbillingEnabled`. Nothing is trusted from the body: it names a field and a value (or
`confirm`), never an outcome, role, status or reviewer. The reviewer is the caller's `systemuserid` from their own token.

## 2. Behaviour

- Correctable fields: Hours, Rate, Amount, Narrative, TaskCode, ActivityCode (fee lines), ExpenseCode (expense lines); header FirmInvoiceNumber, BillingPeriodStart, BillingPeriodEnd,
  Currency, TotalDue. Only while the invoice is In Review and the line Clean or Flagged.
- A correction (or a confirm) writes ONE `sprk_invoicecorrection` row (Corrected or Confirmed individually, both labels) and updates the value, in ONE Dataverse transaction: the row
  upsert, then the line / invoice update conditioned on the row version read in the request. A stale version answers 409 `ebilling.line.changed` / `ebilling.invoice.changed` and
  rolls the row back (D-64 rows are append-only, so no label may outlive a value that was not applied). The row id is deterministic (field, version read, final value).
- AI value = the earliest prior correction row's `sprk_aivalue` for the field, else the current stored value (a header field: the header as read, from the stored header provenance).
  Outcome = Corrected when the final value differs from the AI value, else Confirmed. A repeat that finds the latest row already holding the final value writes no second row.
- Code corrections are stored in the line's `coding.<kind>.code` (the extraction JSON is round-tripped whole; `mismatch` and `firmCodeMismatch` are recomputed). The code lookups are
  not written. A code must be in the line's recorded code set: 400 `ebilling.code.not_in_set`; a line with no recorded set answers 409 `ebilling.correction.code_set_unavailable`.
- Timekeeper, line date and invoice date are refused with 400 `ebilling.correction.field_not_supported_yet`. B2 item: they need rate re-resolution.
- Reviewer role: `eligible-approver` when the caller's own contact (`IIdentityNormalizationService`, EB-D-171) is in the eligible set of the invoice's single core
  (`SingleCoreOf`, #2455), otherwise `invoice-reviewer`. A failure to resolve the set falls to `invoice-reviewer`.
- B1 changes no line status, approved amount, decision or Signal, and recomputes no totals.

## 3. Deviations and open points (for the operator)

1. **EB-D-170 versus "a legacy line is corrected normally".** A line stored before Part A has no `provenance`. The service answers 409 `ebilling.correction.no_provenance` for any
   field (an extracted value or an AI-assigned code) rather than write a correction row with a blank source version. A line with no stored value for the field answers the same.
   If the contract is that a legacy line is corrected with an empty source, drop the `null` returns in `LineSource` (one place) and the two tests that name it.
2. **Uncoded line**: a code correction on a line with no coding answers 409 (`code_set_unavailable`), since there is no set to check against.
3. **B1 leaves derived state to B2** (declared, K2): `TotalDue` rewrites the invoice's billed total, `Amount` rewrites the line amount, `Currency` leaves each line's currency as read;
   none of these recomputes the totals or re-evaluates a rule.
4. **No per-invoice lock**: two reviewers correcting the same field at the same version conflict through the row-version check (one gets 409), not through a lock.
5. Narrative is capped at 2000 (the `sprk_description` column), Hours is rounded to 2 decimals (the `sprk_quantity` precision), both so the request is a 400 or rounded before any write.

## 4. The B2 seam

`InvoiceCorrectionService.OnValueWrittenAsync(CorrectionApplied, CancellationToken)` (internal, a no-op in B1) is called once after a correction is saved (including a repeat
that finished it), with the invoice, line, field, outcome and whether the value changed. B2 re-evaluates the billing rules, reconciles Signals, patches `findings[]` and
`signalPlan[]` in `sprk_findingsjson`, and follows through to status and totals there.

## 5. Component justification (CLAUDE.md section 11)

- Existing: `InvoiceLineDecisionService` writes the line decision and the label rows of Reduce / Accept (grep `sprk_invoicecorrection`); nothing writes a value correction or a Confirmed row.
- Extension: the decision service is keyed on a status change and a Decision Record; a correction changes a value, never a status. Folding it in would give one method two reasons to change.
- Cost of doing nothing: FR-31's Edit and Confirm controls have no endpoint; a reviewer cannot fix a misread value, and task 161's calibration has no Corrected / Confirmed labels.
- Placement (BFF hygiene): the BFF, in the Finance feature module (`FinanceModule`), next to the adjustment and line-decision routes it mirrors (ADR-001, ADR-008, ADR-010). No package,
  no background work, no AI dependency: the code set is read through `CodeSetResolver`.

## 6. Publish size (section 10)

`dotnet publish -c Release -r linux-x64 --self-contained false`, folder bytes (fresh builds from `git archive` of each ref in a short path):

| Build | Bytes | `Sprk.Bff.Api.dll` |
|---|---|---|
| `origin/master` | 132,727,804 | 21,213,696 |
| branch base `b66b3b5230` | 136,041,012 | 24,042,496 |
| this branch tip | 136,132,864 | 24,113,664 |

B1 adds 91,852 bytes (0.09 MB) over the base; the 3.4 MB over master is the branch's earlier ebilling-r1 work. `dotnet list package --vulnerable --include-transitive`: none.

## 7. Seeded proofs (revert a behaviour, watch the tests fail, restore)

Reverted one at a time and each caught: the AI value taken from the current value instead of the earliest row; the outcome rule inverted; a second row written on a repeat; the
row-version check dropped; the code-set check dropped; the role always `invoice-reviewer`; the provenance requirement dropped; the row and the value sent as separate transactions
(the stale-version tests fail: a label is left for a value never applied).

## 8. Review (step 9.5)

Adversarial review of the change set. Fixed: the row and the value were two transactions (a stale version left a permanent label for a value never applied) - now one transaction;
the Narrative cap exceeded the column (2000); Hours precision. Open for the operator: item 1 above; B2's derived state (item 3).

## 9. EB-D-175 / EB-D-176 (separate commit, owner decisions 2026-10-29)

`InvoiceSubmissionPersister`: a coded line the Action did not return (`NotReturned`) and a line with a blank narrative (`NoNarrative`, or any blank narrative on an uncoded
invoice) are written **Flagged**, with a finding in `sprk_findingsjson` `lineFindings[]` (`coding.not_returned`, `line.no_description`; no value). The firm's codes are kept. An
uncoded invoice's other lines stay Pending (D-126); coded lines with no finding stay Clean. A Flagged line is decided by status alone in `InvoiceLineDecisionService` (it needs
no Signal and no finding entry), so no new Signal kind or schema change was needed. Not done: the review read model does not yet surface `lineFindings` (the reviewer sees the
Flagged status, `CodingStatus` and the blank narrative), and no Signal is raised for these lines; both are follow-ups.

## 10. B2a - seam plumbing (refactor and wire; no new business behaviour)

Branch `task/ebill-101-corrections`, on top of `33eba7f446`.

| Piece | Where |
|---|---|
| Shared Signal lifecycle | `Services/Finance/Submission/SignalEpisodeLifecycle.cs` (internal sealed). The option-a lifecycle moved out of `InvoiceSubmissionAfterSteps` (`WriteWithLifecycleAsync`, `LatestEpisodeAsync`, `FingerprintOf`, `PermanentSkipCodes`, `SignalStep`, the two reason codes). |
| Per-entry builders | `InvoiceSubmissionPersister.FindingEntryJson(int index, BillingFinding, FindingDecision?, bool scrub)`, `SignalPlanEntryJson(BillingSignalMapping)`, `LineFindingEntryJson(int lineIndex, string code)` |
| Seam on repeat | `InvoiceCorrectionService.ApplyAsync` -> `OnValueWrittenAsync`; a failure is `CorrectionStatus.Incomplete` -> 503 `ebilling.correction.incomplete`, `failedStep` `reevaluation` |

**The helper B2b calls**

```csharp
internal sealed class SignalEpisodeLifecycle
{
    public SignalEpisodeLifecycle(IGenericEntityService entities,
        Func<SignalWriteRequest, CancellationToken, Task<SignalWriteResult>> writeSignal,
        Func<Guid, string, string, CancellationToken, Task<SignalCloseOutcome>> supersedeSignal, ILogger logger);

    internal Task<SignalStep> WriteAsync(SignalWriteRequest request, string fingerprint, string contextLabel, Guid contextId,
        ResolvedEpisodeRule resolvedRule, CancellationToken ct);
}
internal enum ResolvedEpisodeRule { SkipWhenSameFingerprint /* 049 */, ReopenAsNextEpisode /* B2b: latest resolved -> episode n+1 */ }
```

049 passes `SkipWhenSameFingerprint`; behaviour is unchanged and its suite is untouched. The writer calls are functions (the shape the after-steps already use), so no DI registration was added. The log
lines now take `{Context} {ContextId}` instead of the fixed words "Invoice submission {SubmissionId}"; the wording is otherwise the same. `PermanentWriteRefusal` (the exception classifier) stays in
`InvoiceSubmissionAfterSteps`; B2b can call it (internal static) if it catches writer faults.

**Seam on repeat.** `ApplyAsync` already reached `OnValueWrittenAsync` on a repeat that wrote no second row (the call sits after the optional transaction). B2a adds the test that pins it, and the
failure path: a thrown seam is now `Incomplete` (503, `errorCode`, `failedStep`, the retry sentence "Repeat the same request to complete it."), never `Applied`. The service takes the seam body as an
optional constructor argument (`onValueWritten`, null = the B1 no-op) so B2b wires its re-evaluation there and tests can fail it.

**Tests.** `FindingsJson_OfAMixedInvoice_IsPinned` (golden captured from the persister BEFORE the extraction, run against it first); `EachStoredEntry_EqualsItsPerEntryBuilder`; the seam-on-repeat,
seam-failure and 503-mapping tests in `InvoiceCorrectionServiceTests`; `SignalEpisodeLifecycleTests` (one-line justification: the reopen rule is the one new input to the extracted helper, and no 049 test reaches it).
Seeded proof: the seam call guarded by `!rowExists || valueChanged` made the repeat test and the failure/repeat test fail; restored.

### 10.1 Part A review fixes F2 and F3 (separate commit, `InvoiceSubmissionPersister`)

- **F2, a refused rule version left lines Clean (fail-open).** A version the gate or its handler refused produced no finding, so the lines it should have checked were written Clean and the invoice could be
  approved unseen. Now a line such a version applies to is written **Flagged** with a `lineFindings[]` entry `rules.not_checked` whose message names the rule type ("The ApprovedRate rule could not be checked
  on this line."), no rule id and no value. The refusal is tied to lines through a new additive member `BillingEvaluationResult.RefusalScopes` (`BillingRuleRefusalScope`: version id, rule type, whether the
  invoice subject selected it, the line indexes); it is a separate member so `BillingRuleRefusal` and its equality, which 072's tests pin, are untouched. When the invoice subject selected the version, or a
  refusal has no scope, every line is flagged. Known over-flag: a refused client default whose type a valid firm version already covers still flags the lines (the evaluator does not say which refusals were
  displaced); it fails safe and the reviewer accepts the lines. The stale comment "every check that can flag a line has run" now says a refused rule is flagged. With no refusal nothing changes
  (must-not-fire test; the byte-identical pin is unaffected).
- **F3, provenance named the coding Action for a line it never saw.** The line's `provenance` now carries the Action pair only when `coding is { Status: not NoNarrative }`; a `NotReturned` line was sent to the
  Action and keeps it. The submission-level pair is unchanged (the invoice was coded).
- Seeded proof: with the F2 flag condition removed and the F3 condition reverted, three tests failed (both F2 tests and the F3 test); restored.

rework: none

## B2d. `lineFindings[]` in the review read model

`InvoiceReviewReadService` now reads the `lineFindings` member of `sprk_findingsjson` (written by the persister, section 9) and returns each entry on its line.

Contract change (additive): `ReviewLineDto` gains a last member `LineFindings` (wire `lineFindings`), a list of `ReviewLineFindingDto(Code, Message)`:

- `code` is `coding.not_returned` or `line.no_description`; `message` is the stored sentence (null when the entry has none).
- Entries are matched to the line by index (stored `line` = the line's sequence - 1), the same key the policy findings use.
- It is a separate list from `findings`: a `ReviewFindingDto` requires a policy, rule type and clause, which these notes do not have. `findingCount` and `highestSeverity` still count the policy findings only.
- A row stored before the member existed, a `null` member, or no stored submission reads as `[]`. An entry that is not an object, or lacks an integer `line` (>= 0) or a non-empty string `code`, is skipped; a member that is not an array reads as empty. Skips are logged as one warning per read with the count only (no content, ADR-015), following the existing tolerance for a damaged findings column.
- The read stays inside `InvoiceReviewReadService` (the `FindingsJsonAppOnlyReadGuardTests` allow-list is unchanged).

Consumers: no SPA or shared client type mirrors this read model (searched `src/` and `tests/` for the model's member names in `.ts`/`.tsx`/`.json`; only server code and notes). The review endpoint's contract tests (`InvoiceLineReviewEndpointsAuthorizationContractTests`) assert authorization only, not the body shape, so there is no field list to extend; the shape is covered by the unit tests.

Tests (`InvoiceReviewReadServiceTests`): each code shown on its line and not on others; old row (no member) reads empty with no log; no submission reads empty; five kinds of malformed entry skipped with one warning and the good entry kept; non-array member reads empty with one warning.

UI follow-up (not built): the review page should show `lineFindings[].message` as the reason on a Flagged line that has no policy findings, and may count them in its line badge. The server counts do not include them.

## B2b-1. Re-evaluation, findings patch, line status

Branch `task/ebill-101-corrections`, on top of `6d54c3fcd5`.

| Piece | Where |
|---|---|
| Follow-through | `Services/Finance/InvoiceCorrectionReevaluation.cs` (public sealed, concrete, scoped, ADR-010). `ApplyAsync(CorrectionApplied, ct)` is the service's `onValueWritten`. |
| Wiring | `InvoiceCorrectionService`'s public constructor takes it and passes `ApplyAsync` as the seam; `FinanceModule` registers it next to the service. |
| Step names | `CorrectionCodes.FindingsStep` (`findings`), `CorrectionCodes.StatusStep` (`status`); a failure is `CorrectionStepException(step)`, mapped to the 503 Incomplete `failedStep`. Any other seam failure stays `reevaluation`. |
| Uncoded re-code (D-126) | `InvoiceCorrectionService.CorrectLineAsync` / `CheckCodeAsync` / `ResolveForUncodedAsync` |
| Guards | `FindingsJsonAppOnlyReadGuardTests` reviewed type (row 8 in the task 122 notes: app-only read and row-version-conditional write). |

**Order.** After the value write: (1) findings step: read the invoice, its fee and expense lines, every correction row, the latest submission; rebuild the request; load the facts once; evaluate twice; patch; one row-version-conditional update of `sprk_findingsjson` (skipped when the patch changes nothing). (2) Status step: one transaction of row-version-conditional line updates (skipped when no status changes).

**The request, rebuilt.** Header: a field with a Corrected / Confirmed row from its column, any other from the header provenance's read value (the invoice number column holds the typed number), else its column. Lines: the columns; codes, observations and confidences from the line's extraction JSON; the resolved timekeeper and approved rate as Found / matched or not. The corrected field takes its pre-correction value on the "before" side (the previous Corrected / Confirmed row's `sprk_finalvalue`, else the first row's `sprk_aivalue`) and the latest row's final value on the "after" side. Confidence 1.0 for every field with a Corrected / Confirmed row, and for the corrected field on the after side (on the before side only when an earlier row exists). Facts the rows do not keep (the timekeeper's unmatched reason, an observation's quoted text, a rate's not-found reason) are rebuilt the same on both sides, so they cannot make a difference.

**The patch.**
- `findings[]`, keyed (policy version, subject, line, reason code): a key whose entries differ between the two evaluations has its stored entries replaced in place (same `index`, 073's `consequence` / `rejects` / `heldBecause` kept), the extra ones removed, new ones appended at an index above every stored index and every `decision.deciding` index (a removed index is never reused). A new entry carries the rule version's consequence and `rejects: false` (073 is not re-run on an In Review invoice). D-112 scrub follows the after evaluation's withheld groups.
- `signalPlan[]`, keyed (policy code, subject entity, subject id): a group whose mapped entry differs is replaced, added or removed. An entry the writer's preflight would refuse is left out (removed).
- Code correction (any code field, allowed by construction): that line's `codingFindings[]` entry loses the refused codes of the corrected kind and that kind's `mismatch` flag; the entry goes when no refusal and no mismatch is left. A `coding.not_returned` entry of the line goes.
- Narrative correction to a non-blank value: the line's `line.no_description` goes.
- `rules.not_checked` is never removed.
- A decided line (Accepted, Reduced, Disallowed): no entry naming it is touched (findings, plan, clears) and its status is not written.

**`pendingSignalChanges` (exactly what is stored).** A new root member `{ "rule": [...], "system": [...] }`:
- `rule[]`: one entry per (policy, subject) whose plan entry a correction changed since B2b-2 last reconciled: `{ policyCode, subjectEntity, subjectId, closeFingerprint, raiseFingerprint }`. `closeFingerprint` is the stored plan entry's fingerprint before the FIRST correction that changed the group (null when none was stored); later corrections keep it. `raiseFingerprint` is the fingerprint now in the plan (null when the group no longer fires). An entry with both null is dropped.
- `system[]`: `{ line, kind, reason }` per system finding removed from `codingFindings[]` (`kind` Task / Activity / Expense, `reason` `refused` or `firmCodeMismatch`); de-duplicated.
- No value is stored. `StoredFindings.Parse` ignores the member.

**Line status.** For each line the diff or a clear touched (the corrected line always): `InvoiceSubmissionPersister.LineStatusFor(flagged, coded)` where flagged = any `findings[]` (subject Line), `lineFindings[]` or `codingFindings[]` entry names the line in the PATCHED JSON, and coded = the line's coding records a `codeSetId`. A line of an uncoded invoice that is not re-coded therefore re-derives Pending when its last finding clears (the persister's rule).

**Uncoded invoices (D-126).** A code correction of a Pending line whose coding says `NotCoded` is allowed; any other Pending line is still 409 `ebilling.line.pending`. The set is resolved by `CodeSetResolver.ResolveAsync` for the invoice's single core record over the invoice's lines as stored (the FR-22 order, including step 4's suggestion provider). No usable set (`LineCodingException.NoCodeSet`) is 409 `code_set_unavailable` and nothing is written. On success `codeSetId`, `codeSetName`, `codeSetSource`, `needsReview` are recorded in the line's `coding` block with the code; `status` stays `NotCoded` (no AI coded the line, so task 100's bulk label rows still skip it), and a later correction reads the set back with `LoadRecordedAsync`. The correction row has no source code / version (the AI offered no code, B1's rule).

**Not here.** Currency and TotalDue corrections are not re-evaluated (B2c). Timekeeper and date corrections stay refused (B2c). No Signal is written or closed, the D-125 hold stays (B2b-2).

**Known limits (K-class).** (1) A replaced plan entry is mapped without 073's decisions, so a group stored as Held / Blocked loses that presentation when a correction changes it. (2) Corrections never re-run 073. (3) A refused plan entry is dropped without a `signalPlanRefusals` record. (4) A replaced AiObservation finding carries an empty quoted text.

**Tests** (`InvoiceCorrectionReevaluationTests`, 12, over `CorrectionWorld` and 072's real evaluator): the diff adds and removes (hours 25 to 2.5: daily cap removed, whole-hour increment added, plan and pending entries); the last finding cleared makes the line Clean; task code clears the task kind's refused and mismatch entries but keeps an activity refusal; not-returned plus the emptied coding entry cleared, line Clean; no-description cleared; `rules.not_checked` kept; a decided line untouched; uncoded Pending line re-coded from the settings default set; no resolvable set 409; non-code correction of a Pending line still refused; retry after a findings failure; retry after a status failure (no second patch).

Changed 101 test files: `CorrectionWorld` (firm, submission version, a transaction fault hook, `In` conditions, the resolver's core read, a settings default set); `InvoiceCorrectionServiceTests.ALineThatWasNotCoded_HasNoCodeSet_Conflict` clears the code sets (D-126 makes an uncoded line re-codable; the test keeps its 409 by leaving no set to resolve). 049's suites are unchanged.

**Seeded proofs.** The decided-line flag forced false: the decided-line test failed. The re-evaluation skipped when `CorrectionApplied.ValueChanged` is false: both retry tests failed. Restored.

**Gate.** BFF and tests build; unit filter `Finance|Ebilling|Billing|Signals|Approval|Invoice` 8993 passed, 7 skipped, 0 failed; ArchTests 1858 passed. Publish-size delta not measured in this part (one source file, no package).

### For B2b-2

1. Read `pendingSignalChanges`: for each `rule[]` entry, close the open Signal of (policyCode, subject) whose fingerprint is `closeFingerprint` with `SignalWriter.CloseAsync(..., ResolutionConditionCleared, ...)` (record-free), and when `raiseFingerprint` is set write the current `signalPlan[]` entry of that key through `SignalEpisodeLifecycle.WriteAsync(..., ResolvedEpisodeRule.ReopenAsNextEpisode)`. An entry whose two fingerprints are equal is a refresh (the facts changed, not the reasons).
2. For `system[]`: re-map the system findings from the patched JSON (`StoredFindings.SystemFindings`) per line and close or refresh that line's SubmissionIntegrity Signal.
3. Clear the member (or the entries it reconciled) in the same row-version-conditional write, so a repeat converges. The member accumulates across corrections until then.
4. The D-125 coding hold: when the last uncoded line is re-coded, close the hold Signal and decide whether `pipelineFindings[]`'s hold entry is removed (this part leaves it).
5. The end-to-end approval test (a corrected invoice becomes approvable).

## B2b-2. Signals, the coding hold, the end-to-end path

Branch `task/ebill-101-corrections`, on top of `472565fd26`.

| Piece | Where |
|---|---|
| Signals step | `InvoiceCorrectionReevaluation.ReconcileSignalsAsync`, run after the status step; a failure is `CorrectionStepException(CorrectionCodes.SignalsStep)` (`failedStep` `signals`) |
| Writer wiring | the public constructor takes the singleton `SignalWriter` (write, and the record-free system close); the unit-test constructor takes the calls and an optional system-policy read. No DI registration added. |
| Shared helpers (behaviour unchanged) | `SignalEpisodeLifecycle.LatestEpisodeAsync(policy, entity, id)` (internal; the private request overload delegates); `InvoiceSubmissionAfterSteps.ReadSystemPolicyAsync(IGenericEntityService, ...)` static (049's instance method delegates); `StoredFindings.ParsePlanEntry` internal; `InvoiceSubmissionPersister.SignalPlanRefusalEntryJson` (the persister's inline builder, extracted) |

**Rule Signals.** Per `pendingSignalChanges.rule[]` entry: when `closeFingerprint` is set and differs from `raiseFingerprint`, the latest episode of (policy, subject) is closed with `SignalWriter.CloseAsync(id, rowVersion, ResolutionConditionCleared, policyCode)` (record-free, the `SignalReevaluationJob` precedent; no Decision Record), and only when it is open AND carries `closeFingerprint`; a resolved, absent or differently fingerprinted episode is left (no-op). When `raiseFingerprint` is set, the current `signalPlan[]` entry of that key is parsed (`StoredFindings.ParsePlanEntry`) and written through `SignalEpisodeLifecycle.WriteAsync(..., ReopenAsNextEpisode)`: after a close, episode n+1; with equal fingerprints there is no close and the open episode is refreshed in place. A plan entry that does not match the change is logged and not raised.

**System Signals.** For the lines named in `system[]`: `StoredFindings.Parse` of the patched JSON, then `SystemFindings` under the system policy (049's read), then the line's findings. None left: its latest open system Signal is closed (ConditionCleared, any fingerprint). Some left: mapped (`BillingFindingSignalMapper.MapAll`) and written through the lifecycle (open with another fingerprint: Superseded, then n+1). An unusable system policy (missing, disabled, ...) wrote no system Signal in 049, so nothing is reconciled (logged).

**Clearing.** After every Signal call, ONE row-version-conditional update removes `pendingSignalChanges` (and the hold entry, below) and adds any permanent writer refusal to 049's `signalWriteRefusals` (same shape, de-duplicated). A failure part-way throws before that write, so the member stays and the repeat converges: an already closed Signal is left, one already at the new episode is refreshed (no n+2). A concurrent write fails the update (Incomplete) and the repeat re-reads.

**D-125 coding hold.** Shape (persister / after-steps): a `pipelineFindings[]` entry `ebilling.submission.coding_no_code_set` or `ebilling.submission.coding_unavailable`, which 049 maps to the INVOICE's `BILL-SYSTEM-SUBMISSION` Signal (reasons `coding.no_code_set` / `coding.unavailable`, grouped with any other invoice-level system reason). It clears when no UNDECIDED fee or expense line lacks a `coding.codeSetId`. Two readings to confirm: a decided line can no longer be re-coded, so it does not keep the hold; and B2b-1 keeps `status: NotCoded` on a re-coded line, so the recorded code set is the test, not the status. On clearing, the entry is removed in the clearing write and the invoice's system Signal is closed ConditionCleared, or refreshed when another invoice-level system finding remains. No new decision path was needed. After the clearing write, every undecided line still Pending with a recorded code set takes `StatusOf` (the persister's rule); this is idempotent, so a status failure after the clear is finished by the repeat.

**073 presentation (B2b-1 K1, fixed).** Option (a), reusing 073's stored output and the mapper rather than copying either: both sides of the plan diff are mapped with 073's STORED decisions and the stored kind. `StoredDecisions` gives the n-th finding of a key the n-th stored `findings[]` entry of that key (`consequence`, `rejects`, `heldBecause`); the before side reads the pre-patch copy, the after side the patched one. A Held or Blocked group is therefore presented as 049 stored it. A guard makes sure a correction can never drop a hold: a stored entry presented `Blocked` that the after side would present otherwise (its deciding reason corrected away, or the group gone) is KEPT as stored and marked `correctionReview: true`, and no Signal change is recorded for it. Its Signal stays open until 105's override / return resolves every Signal (D-122). A root with no `decision` maps without decisions, as before.

**What the hold checks read.** `InvoiceApprovalService.IsHeldAsync` and 104's `InvoiceRejectReturnService.IsHeldAsync` (on `work/ebilling-r1`, not on this branch) both read `FinanceBaseValues.HeldSubmissionConditions` on the SUBMISSION (Processing, or Rejected with the Blocked reason); neither reads the plan. A correction never writes the submission status, so a held invoice stays refused. Test: `HeldInvoice_AfterACorrectionThatChangesTheHeldGroup_IsStillRefusedApproval`. 104 is not merged into this branch, so the reject / return refusal is not exercised here (it uses the same predicate); add the twin test when the branches meet.

**Plan refusals (B2b-1 K3, fixed).** A replacement or new entry that `SignalWriter.PreflightRefusal` refuses is left out and recorded in `signalPlanRefusals` with the persister's own entry builder, once.

**Tests.** `InvoiceCorrectionSignalsTests` (11 cases, over `CorrectionWorld` with an in-memory Signal store and `BeginsWith`):
- coded clear then re-raise (ConditionCleared, then episode 2);
- refresh;
- line system Signal closed / refreshed (theory, 2 cases);
- hold kept while a line is uncoded, then closed and the lines re-derived after the last re-code;
- Blocked group changed keeps the held presentation;
- Blocked group whose reason is removed is kept and marked, Signal open;
- refused replacement recorded;
- failure at the signals step, then repeat;
- failed clear, then repeat (no episode 3);
- half-cleared member.

`InvoiceCorrectionEndToEndTests` (2, over `ApprovalWorld` with the real resolver, line-decision, approval and recorder): an uncoded invoice re-coded line by line, the hold closes, the Flagged line is accepted, then the invoice is approved; a held invoice is refused approval after a correction.

Changed B2b-1 tests: the two that read `pendingSignalChanges` now read the findings step's write (the Signals step clears the member); the status-failure retry test counts two submission writes (the patch, then the clear). `ApprovalWorld` gains `BeginsWith`. 049's suites are unchanged.

**Seeded proofs** (each reverted, watched fail, restored):
1. The Blocked guard disabled: `BlockedGroup_WhoseReasonACorrectionRemoves_IsKeptAndMarked` and the end-to-end held test failed. The latter fails on its plan assertion; its approval assertion reads the submission, which the guard does not touch.
2. The stored decisions dropped from the mapping: `BlockedGroup_ChangedByACorrection_KeepsTheHeldPresentation` failed.
3. The open-episode check dropped from the close (retry idempotency): `AFailedSignalsStep_IsIncomplete_AndTheRepeatConverges` and `AHalfClearedPendingMember_Converges` failed.

**Known limits (K-class).**
1. A refused plan entry, or a Signal write refused permanently in the Signals step, is recorded in the findings JSON but does not set `sprk_adminattention` (049 sets it from its own run).
2. A stored Blocked entry kept by the guard keeps its pre-correction facts until override / return.
3. A Signal a person resolved, whose group a later correction re-raises with the same fingerprint, reopens as episode n+1 (`ReopenAsNextEpisode`, the B2a rule).
4. Reject / return (104) is not on this branch; only the approval half of item 5 is tested.
5. The hold-refresh case (another invoice-level system reason left when the hold clears) has no test of its own; it is the same lifecycle write the line-refresh test covers.

## B2c. Timekeeper and date corrections, totals, currency, re-coded read model, admin attention

**Behaviour (before -> after).**
1. Timekeeper-as-billed, line-date and invoice-date corrections: were 400 `field_not_supported_yet` -> enabled. The correction service builds a single-line `InvoiceResolutionInput` and calls the registered `IInvoiceResolutionService` (046 unchanged; its private `ReadRatesAsync` is not called). A timekeeper correction names the roster entry the reviewer picked; if the resolution matches a different timekeeper (not approved and active on the line date, or the name is ambiguous) the answer is 400 `ebilling.correction.timekeeper_mismatch` and nothing is written. On a match, the resolved timekeeper, the approved rate and the line's `resolutionChange` member (before pair; after reasons) are written in the same conditional transaction as the correction row. B2b's re-evaluation then diffs the line with the new facts; an invoice-date correction re-evaluates date-dependent rules through the same seam.
2. Totals: an Amount, Hours, Rate or TotalDue correction recomputes the totals through `InvoiceTotalsService` (016) as its own idempotent seam step after status; a failure is the 503 Incomplete with `failedStep` `totals`, and the repeat converges.
3. Currency: a Currency correction updates the invoice currency only. Lines whose currency disagrees get a `lineFindings[]` entry with code `currency.mismatch` (Flagged like the other line findings; no Signal, no new Signal kind); it is cleared when the currencies agree.
4. Re-coded lines: the line's coding status is now `Coded` (was `NotCoded`) with a `recoded` marker and no `hold`, so the review read model shows it as coded. The hold rule is kept (it clears when no undecided line lacks a code set). Two follow-ups the new status needed: `LineSource` treats a re-coded line like an uncoded one (a retry of its correction had returned 409 `no_provenance`), and `InvoiceLineDecisionService.CorrectionRowsFor` skips `recoded` lines so the approval's bulk rows do not record a reviewer's code as an AI code accepted in bulk.
5. Admin attention: a refused plan entry, or a permanently refused Signal write, during a correction sets `sprk_adminattention` through `AdminAttentionText.Merge` / `FromFindings` (the persister's and 049's helper and text). This closes B2b-2 known limit 1.

**Files.** `Services/Finance/InvoiceCorrectionService.cs`, `InvoiceCorrectionReevaluation.cs`, `InvoiceLineDecisionService.cs`, `Submission/InvoiceSubmissionPersister.cs` (`LineFindingCurrencyMismatch` const), `Api/Finance/InvoiceCorrectionEndpoints.cs` (400 mapping, docs). No schema change, no new Signal kind, no new DI registration (the correction and re-evaluation services take two already-registered scoped services).

**Placement / ADRs (section 10, section 11).** Existing: extends the correction service and its seam; Extension: nothing new is added. Cost of doing nothing: the 400 `field_not_supported_yet` stays and the totals/currency/read model stay wrong after a correction. ADR-002 (server-side owner of each invariant; no plugin), ADR-010 (no new interface), ADR-013 (the BFF finance code uses 046's service, not an AI client), ADR-015 (logs carry ids, field names and exception type only). adr-check: no violations.

**Tests.** New `InvoiceCorrectionB2cTests` (28 cases): matching and mismatching timekeeper (pending, same-name-twice, other firm, repeat, stored name), date corrections (fee, still unmatched, expense, resolution fault), invoice date, totals (theory over Amount/Hours/Rate/TotalDue/Narrative, failure then repeat, refused calculation, order after status), currency (flag, clear, decided line, agreeing), re-coded line in the read model, status-step retry, re-correction of a re-coded code, admin attention (plan refusal, permanent write refusal, none). `CorrectionWorld` gains the roster, rate rows and a real `InvoiceResolutionService`. The end-to-end re-code test now also asserts no AI-accepted-in-bulk `TaskCode` row exists for the re-coded lines (and that bulk rows exist at all). Changed existing tests: two `field_not_supported_yet` tests removed; the re-code test expects `Coded` + `recoded`; the endpoint contract theories use value_invalid cases. The 046 and 049 suites are unchanged and green (inside the unit filter below).

**Seeded proofs** (reverted, watched fail, restored):
1. Mismatch check disabled (`if (false && ...)`): both `timekeeper_mismatch` scenarios (pending, same-name-twice) failed (2 failed, 26 passed in the B2c file).
2. Totals step made a no-op: 7 failed (refused calculation, the four totals-theory rows, the failure/repeat test, the order test).

**Gate (on the commit).** Builds clean (0 warnings, 0 errors): Sprk.Bff.Api.Tests, Spaarke.ArchTests, Sprk.Bff.Api.IntegrationTests, Spe.Integration.Tests, Sprk.Provisioning.ControlPlane.LoadTests. ArchTests 1858 passed, 0 failed. Unit filter `Finance|Ebilling|Billing|Signals|Approval|Invoice`: 9030 passed, 7 skipped, 0 failed (`InvoiceCorrection*` alone: 204 passed). Integration project: 199 passed, 8 skipped, 0 failed. `dotnet list package --vulnerable --include-transitive`: none; no package change.

**Publish size** (`-c Release -r linux-x64 --self-contained false`, `git archive` of the commit, short path): 136,246,608 bytes. Master 132,727,804 -> whole 101 branch +3,518,804 (under the +5 MB bar); B1 tip 136,132,864 -> B2c +113,744. The absolute total is above 55 MB but master itself already is (132.7 MB), so the delta rule is the one that applies; no new package.

**Known limits (K-class).**
1. A date correction re-resolves without the invoice's timekeeper summary names, so it resolves with the resolved roster entry's name (the line's stored billed name when none).
2. A Currency correction does not re-run the money rules (D-20); only the `currency.mismatch` finding is patched.
3. A refused totals calculation (for example TotalDue lowered below the adjustments) leaves the correction Incomplete at `totals` until corrected again.
4. `resolutionChange` holds only the latest timekeeper/date correction of a line; a retry of an earlier one has an imperfect "before" side.
5. Reject / return (104) is still not on this branch.

## Fix round, part 2: the value write path (F-1, F-3, K-f)

**Behaviour (before -> after).**
1. F-1, a refused totals calculation: was a saved value plus a 503 `incomplete` (`failedStep` `totals`) that no repeat could clear -> a field that feeds the totals (TotalDue, Amount, Hours, Rate) dry-runs `InvoiceTotalsCalculator.Calculate` BEFORE the transaction, with the proposed value over the stored lines (TotalDue: the billed total; Amount: the corrected line's amount; Hours and Rate: the stored inputs), the way `InvoiceLineDecisionService` and `InvoiceAdjustmentService` do. A refusal is 409 `ebilling.invoice.totals_refused` (the existing code; detail is the calculator's refusal code, for example `invoice-totals.approved-total-negative`) and nothing is written: no correction row, no value. A refusal after the write (a concurrent adjustment) is the same 409: the totals step throws `CorrectionTotalsRefusedException`, which is not wrapped as a failed step. The dry-run runs only when this request will write (a new row or a value); a repeat that finds both already written skips it and runs the follow-through, so a re-evaluation that failed earlier still completes.
2. F-3, a Currency correction: was every undecided line given a false `currency.mismatch` (the persister copies the header currency onto every line, so lines never carry their own) -> the correction's transaction also updates `sprk_currency` on the undecided fee and expense lines (version-conditional; only those whose currency differs, so a repeat finds nothing left; a decided line keeps what it was decided with; a confirm writes nothing; beyond 1,000 requests the rest follows in further transactions and a failure there is a 503 `unavailable` whose repeat finishes it). Currency joined the re-evaluated fields, so the rules re-run through the diff (RequiredData's not-USD finding, and the D-20 / D-115 skip of the rate and cap rules) and the Signals reconcile like any other header field. `currency.mismatch` and `PatchCurrencyAsync` are removed: no line can carry its own currency. The "rules cannot re-run" case needs no fallback: a failure of the re-evaluation is the 503 `findings` step and the repeat re-runs it.
3. K-f, extraction JSON size: a correction whose new extraction JSON would exceed `ExtractionConfidenceMaxLength` (100,000) is 409 `ebilling.correction.too_large` with nothing written (the code, timekeeper and date corrections rewrite that JSON). A fee or expense line (Pending, Clean or Flagged) with NO stored extraction JSON (the persister leaves it out when it was over the limit, so it reads as uncoded) is 409 `ebilling.correction.extraction_unavailable`, before the Pending, provenance and code-set checks, instead of a misleading `line_pending` / `no_provenance` / `code_set_unavailable`.

**New error codes.** `ebilling.correction.too_large` (409), `ebilling.correction.extraction_unavailable` (409). `ebilling.invoice.totals_refused` is the existing `LineDecisionCodes.TotalsRefused`, now also on the correction routes (409). New `CorrectionStatus` values `TotalsRefused`, `TooLarge`, `ExtractionUnavailable`, all mapped to 409 in `InvoiceCorrectionEndpoints`.

**Files.** `Services/Finance/InvoiceCorrectionService.cs`, `InvoiceCorrectionReevaluation.cs`, `InvoiceAdjustmentService.cs` (`ToInput` private -> internal, reused for the dry-run), `Submission/InvoiceSubmissionPersister.cs` (`LineFindingCurrencyMismatch` removed), `Api/Finance/InvoiceCorrectionEndpoints.cs` (409 mapping, docs). No schema change, no new DI registration, no package.

**Tests.** `InvoiceCorrectionB2cTests`: the refused-calculation test is now "a refusal after the write is the same 409"; new: TotalDue below a fixed adjustment (refused before the write, every repeat the same 409, a valid value then goes through), an Amount raise under a 100% adjustment, Hours and Rate under an already-refused total, the currency tests rewritten with lines shaped as the persister writes them (the lines move to the new currency, no `currency.mismatch`, RequiredData's not-USD finding appears and clears, a decided line is left, one transaction with a stale line version rolling the label back, a repeat finishes a line left behind), too large (refused; the same correction with room is applied), and a line with no stored extraction (three cases). Two existing header-field assertions (`EachHeaderField_IsCorrectedThroughItsColumn` and its endpoint twin) now count the invoice write and, for Currency, the two line writes.

**Seeded proofs** (reverted, watched fail, restored): the pre-write dry-run disabled -> 5 failed in the B2c file (the TotalDue, Amount and Hours / Rate refusals); the size check disabled -> the too-large test failed.

**Known limits.** (a) A refusal after the write leaves the value saved and answers 409; the detail names only the refusal code. (b) The status step (`DeriveStatusAsync` / `StatusOf`) still reads "coded" from the extraction JSON, so a OTHER touched line with no stored extraction would be re-derived as Pending by a diff that touches it; the corrected line itself is refused above. That code belongs to the Signals / status part and is not changed here.

## Fix round, part 1: the Signals step and logging (F-5, K-a, K-e, F-4)

**F-5, a Signal left open after its group is gone (fixed).** `ReconcileRuleAsync` matched the close on the stored `closeFingerprint` even when `raiseFingerprint` was null. After a failed clearing write (close fp0 / raise fp1 done, member kept), a later correction that removes the group turns the member into close fp0 / raise null while the open episode already carries fp1; the fp0 match found nothing, the member was cleared, and episode 2 stayed open with no plan entry. Now a null `raiseFingerprint` closes the latest open episode of that (policy, subject) whatever its fingerprint, through the same record-free `CloseAsync(..., ResolutionConditionCleared, ...)`. With a raise set, the close is still of the stored fingerprint only. Test: `InvoiceCorrectionSignalsTests.RemovedGroup_AfterAFailedClear_ClosesTheOpenEpisodeWhateverItsFingerprint` (failed before: episode 2 stayed open; passes after).

**K-a, cross-line read order (fixed).** `PatchFindingsAsync` now reads the submission (and so its version) first, then the invoice, lines and correction rows. A concurrent correction whose findings patch lands after the version read bumps it, so our conditional write fails and is retried; the old order could pair lines older than the version with a version that already included the other patch. No separate test: the in-memory store is single-threaded, so the interleaving cannot be driven; the change is the read order and the existing conditional-write retry tests still pass.

**K-e, log shape (fixed).** The lifecycle extraction replaced task 049's `Invoice submission {SubmissionId}: ...` lines with `{Context} {ContextId}`. `SignalEpisodeLifecycle.WriteAsync` now takes a `SignalLogContext` (`Submission` / `Correction`) instead of a label string and keeps a literal template per context: the two lifecycle log lines (`was skipped`, `read without a row version`) are byte-for-byte 049's for `Submission`, and a correction logs `Invoice correction {InvoiceId}: ...`. Tests (`SignalEpisodeLifecycleTests`) capture the template and properties: the Submission template equals 049's and carries `SubmissionId`; the Correction line carries `InvoiceId` and no `SubmissionId`. 049's after-steps suite is unchanged and green. The remaining 049 log lines in `InvoiceSubmissionAfterSteps` were never touched.

**F-4, no behaviour change.** A code correction clears `coding.not_returned`. The owner's decision EB-D-175 says such a line must be "accepted or corrected" by a reviewer, so a correction resolves it. A code comment citing EB-D-175 now sits at the `RemoveLineFinding(... LineFindingNotReturned)` call.

**Known limits (K-class, notes only).**
- **K-b:** rule versions can change after a submission; the stored findings are keyed by the old version id. It fails safe (a group that no longer matches is closed or raised, never silently kept).
- **K-c:** the D-112 scrub covers only the entries a correction replaces; the review read model re-scrubs what it returns.
- **K-d:** `sprk_reviewedon` has seconds resolution, so two corrections of one field in the same second can swap order when the latest is picked by that column.
- **K-g:** a re-code calls `CodeSetResolver.ResolveAsync` again, with AI cost per correction, and can leave mixed code sets on one invoice (the corrected line on the newer resolution, others on the original).

## Fix round, part 3: durability (F-2, the fail-open)

**The defect.** A correction's value commits in its own transaction; the follow-through (findings, status, Signals, totals) runs after it. A findings step that failed, with no repeat, left nothing behind saying the correction was not followed through: a Clean line corrected from 2.5 to 25 hours stayed Clean with no daily-cap finding, and the approval accepted it in bulk (D-72). The same root cause broke chained corrections: the "before" side was the previous row's final value (`labels[^2]`), and a timekeeper then date correction on one line lost the first pair because `resolutionChange` was overwritten.

**Behaviour (before -> after).**
1. **Applied rows are recorded.** The findings step adds every Corrected / Confirmed row of the corrected field (on its line) to a new findings JSON member `appliedCorrections` (`[ "<row id>", ... ]`), in the same row-version-conditional write as the patch, also when the patch is empty. A repeat adds nothing and writes nothing.
2. **The diff starts from the last applied value.** The "before" side is the final value of the last row of the field listed in `appliedCorrections` (else the first row's AI value), so a row saved without its findings step is diffed by the next correction of the field, or by its own repeat. Its confidence counts as verified only when such a row exists.
3. **Timekeeper and date: the last applied pair.** The correction service now also writes `resolutionChange.original` (the roster match and rate before the line's FIRST timekeeper or date correction, carried over every later one). The findings step of a timekeeper or date correction of a fee line records the pair it patched with in a new member `appliedResolutions` (`{ "<line index>": { timekeeperId, approvedRate, timekeeperReason, rateStatus, rateReason } }`), in the same write. The "before" pair of such a correction is `appliedResolutions[line]`, else `original` (else `before`, for a line written before `original` existed) with the reasons the first evaluation stored, else the columns (the pair never changed). This also closes B2c known limit 4.
4. **Approval guard.** `InvoiceApprovalService` approve, and `canApprove` on the approval GET, refuse while a Corrected or Confirmed row of a re-evaluated field is not in `appliedCorrections`: 409 `ebilling.approval.corrections_pending`, detail `These corrections were saved but did not finish: Hours (line 3); InvoiceDate (invoice). Repeat each correction, then approve again.` (field names and line numbers; no ids). The check sits with the other pre-claim checks (after the hold check, before the I-2 line check and the earlier-bulk-accept completion), so nothing is claimed or written when it refuses. Its read (`InvoiceCorrectionReevaluation.ReadUnappliedAsync`, static, app-only, inside the reviewed type so the task 122 guard is unchanged) fails closed: a fault is 503 `ebilling.approval.unavailable` at step `read` on approve, and the GET throws (as it does for the hold read). Reject and return (104) are not on this branch and are not guarded.
5. **A line with no stored extraction.** The status step leaves a touched line that has no extraction JSON (or an unreadable one) as it is, never re-deriving it as Pending, and logs one warning with the count. This closes part 2 known limit (b).

**Decision taken here (for the operator).** `ReadUnappliedAsync` skips the rows of a line a reviewer has since DECIDED (Accepted, Reduced, Disallowed). Two reasons: 103's D-72 test (`CleanLinesAcceptedInBulk_DecidedFieldsUnreviewed`) seeds a reviewer's Corrected row on an Accepted line with no findings JSON, and the brief requires 103's suite unchanged. Also, the correction service refuses a decided line, so such a row could never be repeated, and approval would be blocked with no way out but reject / return. Cost: the narrow case below (known limit 1). Reversing it means also letting a repeat run on a decided line (a correction-service change) and changing that 103 test.

**Files.** `Services/Finance/InvoiceCorrectionReevaluation.cs`, `InvoiceCorrectionService.cs` (`original`), `InvoiceApprovalService.cs` (code, check, `canApprove`), `Api/Finance/InvoiceApprovalEndpoints.cs` (docs). No schema change (two new members in an existing JSON column), no new DI registration, interface, endpoint or package.

**Component justification (section 11).** Existing: the findings JSON already carries the follow-through's own state (`pendingSignalChanges`), and the approval already refuses on pre-claim reads (held, I-2). Extension: both new members ride in the existing findings write; the guard is one more pre-claim read through a static method in the type that already owns the findings JSON read. Cost of doing nothing: an unfinished correction is approved past, and its line is accepted in bulk without the finding it should carry (F-2).

**Tests.**
- New `InvoiceCorrectionDurabilityTests` (6): `appliedCorrections` written on an empty patch, and the repeat adds nothing; a failed findings step leaves the row unlisted until its repeat; rate 150 -> 100 unapplied, then 105: the stale over-approved finding is removed (SEEDED); rate 100 -> 150 unapplied, then 160: the finding is added and both rows are listed; timekeeper (unapplied) then date on one line: the date diff starts from the original pair, then the timekeeper repeat clears the guard; a touched line with no extraction keeps its status, with one warning.
- `InvoiceCorrectionEndToEndTests` (+3, over `ApprovalWorld`): an unfinished correction gives `canApprove` false and approve 409 `corrections_pending` with nothing written (no claim, no transition, the Clean line not accepted), then the repeat makes it approvable and it is approved (SEEDED); the 2.5 -> 25 hours case is not bulk-accepted, and the repeat flags the line (approve then answers lines open); a fault on the guard's read refuses approve (503, nothing written), and the GET throws.
- Changed: `InvoiceCorrectionReevaluationTests.ALineAReviewerDecided_...` compares the stored JSON without `appliedCorrections` (the empty patch now records the row). The end-to-end file's `Corrections` helper gained an optional Signal writer. 103's approval suite and 049's suites are unchanged and green.

**Seeded proofs** (each reverted, watched fail, restored):
1. Guard off (the refusal skipped and `canApprove` ignoring it): both approval tests failed.
2. "Before" back to `labels[^2]`: the rate 150 -> 100 -> 105 test failed. A first version of the test (100 -> 120 -> 130) stayed green under this seed: the over-approved finding carries the billed rate, so 120 and 130 differ and the entry is replaced either way. The seeded test uses the direction where both sides agree.
3. Resolution "before" back to `resolutionChange.before` only: the timekeeper-then-date test failed.
4. `MarkApplied` removed: 10 tests failed (the tests that read `appliedCorrections` or approve after a correction).
5. The no-extraction skip removed: its test failed.

**Gate.** Builds clean (0 warnings, 0 errors): Sprk.Bff.Api.Tests, Spaarke.ArchTests, Sprk.Bff.Api.IntegrationTests, Spe.Integration.Tests, Sprk.Provisioning.ControlPlane.LoadTests. ArchTests 1858 passed, 0 failed. Unit filter `Finance|Ebilling|Billing|Signals|Approval|Invoice`: 9052 passed, 7 skipped, 0 failed. Integration project: 199 passed, 8 skipped, 0 failed. `dotnet list package --vulnerable --include-transitive`: none; no package change.

**Publish size** (`-c Release -r linux-x64 --self-contained false`, `git archive` of each commit, short path): master `c8bb55d0cd` 136,144,596 bytes; this branch 136,266,820; the whole 101 branch is +122,224 against current master (under the +5 MB bar; master already holds the rest of ebilling-r1). The absolute total is above 55 MB, as master's is.

**code-review / adr-check.** No violations. ADR-002 (one server-side owner, fails closed), ADR-010 (no new interface or DI), ADR-013 (no AI), ADR-015 (logs carry ids, field names and counts only), ADR-019 (stable code), ADR-028 / task 122 (the findings JSON read stays in the reviewed app-only type; ArchTests green), ADR-044 (ids stored in `D` form, parsed tolerantly). ADR-038 warning: the new test file goes to test-diet at project close. Two class remarks that still described the old "before" were corrected.

**Known limits (K-class).**
1. **A row on a line decided since** is not guarded (the decision above). If its findings step never ran, a finding it would have placed on ANOTHER undecided line (a cross-line rule such as the daily cap) is missing, and that line can be accepted in bulk. Follow-up: have task 100's line decision refuse a line with an unapplied correction, which removes both this case and the reason to skip.
2. **"Applied" means the findings step finished.** A status, Signals or totals step that failed after it, with no repeat, is not guarded: the line status, a Signal or the totals can stay stale while approval passes (this is the brief's closed set). `pendingSignalChanges` still marks an unfinished Signals step, if a stronger guard is wanted.
3. **TotalDue** has no findings step, so its rows are never listed and never guarded (a failed totals step is the 503 the client repeats).
4. **The diff is per field.** A correction diffs its own field from its last applied value with every other field at its current value. While another field's correction is unapplied, a finding that depends on both fields can be patched against the other field's unapplied value. The guard holds approval until that correction is repeated.
5. **A concurrent correction** whose value commits after the approve's guard read is not seen by that approve (corrections do not take the invoice-decision lock).
6. **Rows saved before this change** (no `appliedCorrections` yet) read as unapplied on undecided lines: the reviewer repeats them once. Only dev data exists on this branch.
7. **The approval GET** shows `canApprove: false` without a reason; the reason is in the approve's 409. A UI follow-up can call approve or add a member.
8. `appliedCorrections` grows by about 40 characters per row; the column limit (1,048,576) allows about 26,000 rows.

## Fix round, part 3, round 2: applied means the whole follow-through (K1, K2, K3, K7 of part 3)

The coordinator accepted the decided-line exclusion and asked for three changes. This round makes them.

**1. "Applied" now means the WHOLE follow-through finished (part 3 known limits 2 and 3).**

| | Before | After |
|---|---|---|
| When a row is recorded | `appliedCorrections` was written in the findings patch. A status, Signals or totals step that failed after it left the row listed. The line stayed Clean with a new finding on it, or the approved total stayed stale, and approve passed (bulk-accepting the Clean line). | A final row-version-conditional write after the last step (findings, status, Signals, totals, as the field has them) adds the field's Corrected / Confirmed rows to `appliedCorrections`. A failure in that write is 503 `incomplete` with `failedStep` `reevaluation`, which the endpoint contract already defines as "any other step". A failure at any earlier step leaves the row unlisted, so approval refuses with 409 `corrections_pending` until the repeat. |
| TotalDue | Not guarded: TotalDue has no findings step. | Guarded: its rows are read before the totals step, recorded after it, and `ReadUnappliedAsync` now covers every field that feeds the totals. |

- **Diff base (a deviation from the brief, for the operator).** The "before" side of the diff does NOT move to "the last fully applied value". It stays at the value whose findings patch LANDED, now tracked in its own member, `patchedCorrections`, which is written with the patch as `appliedCorrections` was in part 3.
  - Counterexample: rate 100 → 150 (the patch lands and the over-approved finding is added; the status step fails) → 100. Diffed from the last fully applied value (100), both sides agree, so the 150 finding is never removed. Diffed from 150, it is removed.
  - Test `ARateChain_WhoseMiddlePatchLandedButWhoseStatusFailed_ConvergesWhenCorrectedBack` goes red when the base is switched to `appliedCorrections` (seed 3).
  - `appliedResolutions` (the timekeeper/date pair the findings were patched with) stays with the patch for the same reason: it says what the stored findings reflect. It is not a done-marker.
- **Status coverage.** `pendingStatusLines` holds the lines a landed patch touched whose status has not been re-derived yet.
  - The patch adds its touched lines only when it changed the findings, so a repeat whose patch is a no-op writes nothing.
  - Every status step re-derives its touched lines plus the waiting ones.
  - The final write removes the lines its status step considered.
  - A later correction of ANY field therefore completes the status work a failed one left behind. The unfinished row itself stays unapplied until it is repeated (or followed by a later correction of the same field).
- **Idempotency.** Two parts make the patch idempotent:
  - A repeat re-runs the same diff (from the last patched row before the latest), which rewrites the same entries: same index, same decision members.
  - New in this round: a plan entry that already equals the after side records no Signal change again. Previously a repeat after a completed Signals step re-recorded `pendingSignalChanges` (close = raise), and the Signals step wrote the same entry once more.
  - Proven by `ARepeatAfterAFailedTotalsStep_ConvergesToTheUninterruptedState`: the repeat's only submission write is the final write, no Signal is written twice, and the converged findings JSON equals an uninterrupted run's byte for byte (apart from the row-id lists). Seed 5 makes it red.

**2. The decided-line gap is closed at the source (part 3 known limit 1).**
- `InvoiceLineDecisionService` refuses a decision on a line that has a Corrected / Confirmed row not in `appliedCorrections`:
  - code: 409 `ebilling.line.corrections_pending` (`LineDecisionStatus.LineNotDecidable`, already 409 on the endpoint and in the approval's mapping);
  - detail: `These corrections were saved but did not finish: Narrative (line 2). Repeat each correction, then decide the line again.`
  - nothing is written.
- **Accept all Clean** refuses while ANY correction of the invoice is unapplied, and names every one: `... then accept the Clean lines again.`
  - This is stricter than "the lines it would accept" (a decision for the operator): an unfinished header correction, or one on another line, can still place a finding on a Clean line (cross-line rules such as the daily cap, header rules on every line).
- The read fails closed (503 `ebilling.decision.unavailable`, nothing written).
- The approval's held paths (`AcceptCleanHeldAsync`, `CompleteBulkAcceptHeldAsync`) skip the check: the approval's own guard has just refused on the same read.
- The refusal text is now one helper, `InvoiceCorrectionReevaluation.UnappliedText`, shared by approval and line decisions.
- With this, a line can be decided only when every correction of it is applied, and the correction service refuses decided lines. So the guard's decided-line exclusion stays sound. 103's D-72 seed (a Corrected row on an Accepted line, with no findings JSON) stays valid, and 103's suite is unchanged and green.

**3. Reason on the approval GET (part 3 known limit 7): skipped.** `InvoiceApprovalStateDto` carries no reason codes. It carries one flag or count per block (`Held`, `OpenLines`, `NoEligibleApprover`, `EligibilityUnavailable`), so the brief's condition is not met. Following that pattern, a `CorrectionsPending` flag would be a contract addition for the UI to decide on. The reason is in the approve's 409 detail.

**Files.**
- `Services/Finance/InvoiceCorrectionReevaluation.cs`: `PatchedCorrectionsMember`, `PendingStatusLinesMember`, the final write, the status step's waiting lines, the plan no-op, `ReadUnappliedAsync` covering the totals fields, `UnappliedText`.
- `Services/Finance/InvoiceLineDecisionService.cs`: `CorrectionsPending`, `RefuseUnappliedAsync`.
- `Services/Finance/InvoiceApprovalService.cs`: the shared text.
- `Api/Finance/InvoiceLineReviewEndpoints.cs`: the 409 in the docs.
- Tests:
  - `InvoiceCorrectionDurabilityTests` (+4, 1 changed);
  - `InvoiceCorrectionEndToEndTests` (+5);
  - `InvoiceCorrectionReevaluationTests` (2 expectations: the final write is a third submission write, and the decided-line comparison drops `patchedCorrections` too);
  - `ApprovalWorld` (an additive `TransactionFault` hook).
- No schema change (two more members in the same JSON column), no new endpoint, interface, DI registration or package.

**Component justification (section 11).**
- `RefuseUnappliedAsync` is new surface in an existing service.
  - Existing: the approval's guard, which uses the same read and the same text.
  - Extension: the line decision is a separate owner that the approval does not run on its public routes, so the check has to sit in it. It reuses the read and the text.
  - Cost of doing nothing: a reviewer can decide a line past an unfinished correction. The line then never gets the finding (decided lines are frozen), and the approval guard has to skip it.
- `pendingStatusLines` and `patchedCorrections` extend the existing findings JSON and its existing write. Cost of doing nothing: a chained correction either leaves a stale finding (diff from the fully-applied value) or leaves a line's status underived.

**Tests.**
- New, durability:
  - a repeat after a totals failure converges to the uninterrupted state;
  - a rate chain whose middle patch landed but whose status failed converges when corrected back (SEEDED);
  - a waiting line is re-derived by the next correction of any field;
  - TotalDue is recorded after its totals step, and is unapplied while that step is unfinished.
- New, end to end over `ApprovalWorld`:
  - a failed status step blocks approval (409 with exact detail, nothing written, `canApprove` false); the repeat flags the line; it is decided, and the invoice approved (SEEDED);
  - the same for a failed Signals step (the coding hold's close), with the invoice approved after the repeat;
  - the same for a failed totals step;
  - a line with an unfinished correction cannot be decided, Accept all Clean is refused while any correction is unfinished, a line with no unfinished correction can still be decided, and after the repeat the line is decided (SEEDED);
  - the line decision's read fails closed.

**Seeded proofs** (each change reverted, the tests watched fail, then restored):

| Seed | Failed under the seed |
|---|---|
| 1. Record `appliedCorrections` in the findings patch again | The status, Signals and totals approval tests |
| 2. Line decisions skip the check | The line-decision test |
| 3. Diff base = `appliedCorrections` | The rate chain-back test |
| 4. The status step ignores `pendingStatusLines` | The waiting-line test |
| 5. The plan re-records a change that already landed | The convergence test |

**Known limits after this round.**
1. A single-line decision checks only that line's own corrections. A decision on line Y while an unfinished correction of line X would place a cross-line finding on Y is allowed. Approval still refuses until X is repeated, but X's repeat does not patch Y, because Y is now decided. Accept all Clean, the bulk path, refuses in that case.
2. A correction committed between a decision's (or the approval's) read and its write is not seen; corrections take no invoice lock (unchanged).
3. Rows saved before this round are listed in `appliedCorrections` if their findings patch landed under part 3; dev data only.
4. The per-field diff (part 3 known limit 4) and TotalDue having no findings step are unchanged. TotalDue is now guarded.
5. `appliedCorrections` and `patchedCorrections` together grow about 80 characters per row.

**Gate (this round).** Builds clean (0 warnings, 0 errors): Sprk.Bff.Api.Tests, Spaarke.ArchTests (an MSBuild child-node crash, MSB4166, in the first build; rebuilt on one node, clean), Sprk.Bff.Api.IntegrationTests, Spe.Integration.Tests, Sprk.Provisioning.ControlPlane.LoadTests. ArchTests 1858 passed, 0 failed (the task 122 findings-JSON guard is green: the line-decision service reads through the reviewed type). Unit filter `Finance|Ebilling|Billing|Signals|Approval|Invoice`: 9061 passed, 7 skipped, 0 failed (part 3: 9052; +9 new tests). Integration project: 199 passed, 8 skipped, 0 failed. No vulnerable packages; no package change. Each commit's Finance suite was run on its own tree before it was committed. Publish size was not re-measured this round (no package or project change; part 3's whole branch measured +122,224 bytes against master).

## Review status (F-1 .. F-5)

| Finding | Status | Where |
|---|---|---|
| F-1 refused totals left a saved value no repeat could clear | Fixed: dry-run before the write, 409 `totals_refused` | Fix round, part 2 |
| F-2 fail-open: an unfinished correction approved past | Fixed: `appliedCorrections` written after the LAST step (TotalDue included), the diff from the last landed patch, approval guard 409 `corrections_pending`, line decisions refuse past an unfinished correction (round 2 known limit 1 remains) | Fix round, part 3 and round 2 |
| F-3 false `currency.mismatch` on every line | Fixed: the lines move with the header currency; the rules re-run through the diff | Fix round, part 2 |
| F-4 `coding.not_returned` cleared by a correction | No behaviour change: EB-D-175 allows it; code comment added | Fix round, part 1 |
| F-5 Signal left open after its group is gone | Fixed: a null raise closes the latest open episode | Fix round, part 1 |
