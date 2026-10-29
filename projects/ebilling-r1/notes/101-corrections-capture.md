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

rework: none
