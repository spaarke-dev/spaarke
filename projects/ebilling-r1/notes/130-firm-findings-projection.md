# Task 130a: firm findings projection (service only)

Scope: `FirmFindingsProjection` and its DI registration. The external routes, the firm-scope filter, the ArchTest ledger entries and the integration auth tests are 130b. Rigor FULL.

## What was built

- `src/server/api/Sprk.Bff.Api/Services/Finance/FirmFindingsProjection.cs`: the service, the allow-list DTOs (`FirmOutcome`, `FirmOutcomeItem`, `FirmOutcomeLine`, `FirmOutcomeAdjustment`, `FirmOutcomeCitation`, enums `FirmOutcomeKind`, `FirmOutcomeDecision`) and the `FirmClauseReader` delegate.
- `Infrastructure/DI/FinanceModule.cs`: `FirmClauseReader` (factory, resolves `RuleSetService` lazily, as `ExternalInvoiceHiding` does) and `FirmFindingsProjection` (scoped).
- `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/FirmFindingsProjectionTests.cs`.

## Public surface (for 130b, 131, 133)

```csharp
public delegate Task<FirmClauseText> FirmClauseReader(Guid policyVersionId, Guid invoiceId, CancellationToken ct);

public sealed class FirmFindingsProjection
{
    public Task<FirmOutcome?> GetForInvoiceAsync(Guid invoiceId, CancellationToken ct);     // null: the invoice does not exist
    public Task<FirmOutcome?> GetForSubmissionAsync(Guid submissionId, CancellationToken ct); // null: the submission does not exist
}

public sealed record FirmOutcome(FirmOutcomeKind Kind, string? TrackingNumber, string StatusLabel,
    decimal? BilledTotal, decimal? ApprovedTotal, IReadOnlyList<FirmOutcomeItem> Items, bool Degraded);
public sealed record FirmOutcomeItem(FirmOutcomeDecision Decision, Guid? LineId, FirmOutcomeLine? Line,
    FirmOutcomeAdjustment? Adjustment, string? Reason, IReadOnlyList<FirmOutcomeCitation> Citations);
public sealed record FirmOutcomeLine(DateOnly? Date, string? TimekeeperAsBilled, string? DescriptionExcerpt,
    decimal BilledAmount, decimal? ApprovedAmount);
public sealed record FirmOutcomeAdjustment(string Method, decimal? Value, decimal Amount);
public sealed record FirmOutcomeCitation(string? RuleName, string? FirmReason, string? Label, int? Version,
    string? SectionReference, string? Quote);
```

Enums serialize as strings. No caller check: the caller establishes that the invoice may be shown and passes ids it read server-side. No exception other than cancellation leaves the class: a header fault gives `Kind = Unavailable`; a later fault gives the status only with `Degraded = true`. 130b should map `Degraded` / `Unavailable` to a 503, not a 200 with an empty list.

## Rules as built

| State | Projected |
|---|---|
| Submission Received / Processing | status, tracking number, billed total (from the submission's own derived invoice once it exists, else null); no reads of lines, records or Signals |
| Submission Accepted | the invoice's outcome |
| Submission Rejected | status only (see open question 1) |
| Submission Failed, unknown | `Unavailable` |
| Invoice In Review | status, billed total, reviewer-added lines, Reduced and Disallowed lines with the reviewer's reason; no approved total; no invoice-level adjustment |
| Invoice Approved / Sent to AP / Paid | the same items plus invoice-level adjustments, and the approved total; Approved with a Reduced, Disallowed or Adjustment item is `ApprovedWithAdjustments` |
| Invoice Rejected / Returned to Firm | status and billed total only (reject and return reasons are not in 130's contract) |
| Invoice Cancelled / Superseded / unknown code | `Unavailable` |

Never projected: Pending, Flagged, Clean and Accepted lines, overridden findings, AI observations, a forged reviewer marker, a line or record of another invoice.

Where each part is read (all app-only, through `IGenericEntityService`):

- Lines: `InvoiceReviewReadService.LineQuery` + `ReadPagedAsync` (plus the `sprk_adjustmentrule` column, which the review column set omits). A row whose `sprk_invoice` is not the invoice is dropped.
- Reviewer-added: `ExtractionProvenance.IsReviewerAdded`.
- Reason: the `reduce-line` / `disallow-line` / `add-line` Decision Record whose `sprk_regardinginvoiceline` is one of this invoice's decided lines (and whose `sprk_regardinginvoice`, if set, is this invoice) with the action code the line's current status needs; the newest wins. A record with neither link of this invoice is never projected, whatever the query returns. `sprk_confirmedby`, `sprk_steps`, `sprk_factsnapshot` are not selected.
- Citations (line items): the Signals the chosen record resolved (`sprk_signal.sprk_decisionrecord`), one citation per distinct policy version, with `sprk_firmreason` (first non-blank in creation order; null when none, never substituted; the Signal message is not selected). Citations (adjustments): `sprk_adjustmentrule`, no firm reason.
- Rule name: `sprk_policy.sprk_name` of a Billing policy. A version whose Billing policy cannot be read is not cited. The rule set is never read.
- Clause: `FirmClauseReader` (= `RuleSetService.GetFirmClauseAsync`) once per distinct policy version per invoice. Text returned: label, version number, section reference, quote. None (flag off, version not applied, any fault inside the reader): rule name and firm reason only.

## Decisions taken inside the brief (for review)

1. `ApprovedWithAdjustments` = Approved with at least one Reduced, Disallowed or Adjustment item (a reviewer-added line alone does not make it so). The brief does not define it.
2. A decided line with no Decision Record carrying the line's link is still listed (its status says it was decided), with a null reason. The alternative, dropping it, hides a decision from the firm.
3. Invoice-level adjustments are excluded from the In Review view because EB-D-202 lists lines only; they appear from Approved on.
4. Rejected and Returned to Firm invoices project no items: no reason is in 130's contract (decision 7), so a line list without them would mislead.
5. A reviewer-added line is `Added` even if it was later reduced.

## Component justification (CLAUDE.md section 11)

`FirmFindingsProjection` (service) and its DI registration:

1. Existing: `InvoiceReviewReadService` is the internal review read model (carries Signal sentences, confidence, reviewer identity, clause quotes, values read); `RuleSetService.GetFirmClauseAsync` is the clause decision. No firm projection exists (`grep "FirmOutcome|clausequote"` finds none).
2. Extension: the review model cannot be narrowed to the firm view without a second rule set inside it, and `RuleSetService` has nine constructor dependencies, so the projection takes a `FirmClauseReader` delegate. It reuses the review's line query and paging, `ExtractionProvenance.IsReviewerAdded` and the clause reader unchanged.
3. Cost of doing nothing: FR-38 fails (a firm cannot see why a line was reduced), and the notice (131), PDF (133) and SPA (134) would each re-derive what a firm may see, risking a clause or reviewer note reaching the wrong reader (D-66).

`FirmClauseReader` registration: Existing: `ExternalInvoiceHiding` is the same shape (a lazily resolved delegate). Extension: none can supply the clause; it is a method group over `RuleSetService`. Cost of doing nothing: the projection would hold `RuleSetService` and its nine dependencies, and tests would need all of them.

## BFF hygiene (section 10)

- Placement: BFF, `Services/Finance` beside the review read model (B1 caller identity is the route's, B4 live request). No endpoint in 130a.
- No package added; no HIGH CVE introduced (`dotnet list package --vulnerable` not re-run: no package reference changed).
- Publish-size delta: see "Results".

## Open questions

1. Rejected submissions (FR-18, acceptance "lists each rejection finding with line, values read, rule and citation"): the projection returns status only. The firm-visible reason of a rejection finding has no stored source this task could name (the stored findings carry reason codes and the Signal plan's internal sentence; `sprk_firmreason` is on Signals, and a rejected submission's findings are not Signals the decision resolved). Needs a decision on where that text comes from before it is built.
2. `sprk_signal.sprk_firmreason` has no writer yet (decision 3). Until one exists a citation's `FirmReason` is always null in production.
3. Decision Record authenticity: `InvoiceLineOrigin` accepts an `add-line` record only when the writer identity created it (`WriterCreatedRecord`). The projection applies the link check only, as EB-D-202 states. If a non-writer can create a Decision Record with these action codes and a line link, its reason would reach the firm. A writer-identity filter on the record query would close it; it needs the writer user id (another dependency).
4. The policy name is shown with the flag off (decision 2). If a record rule's policy name can carry internal wording, a name filter is needed (the review DTO's `PolicyCode` already carries a note about `ProcedureRulesOnDate.ForReaders()`).

## Results

- Filtered unit tests (`FirmFindingsProjectionTests` + `InvoiceReviewReadServiceTests`, the one existing class whose file gained a static helper): 99 passed, 0 failed (39 new).
- Seeding proof 1 (visibility gate: the citation always carries label, version, section and quote): `FlagOff_ACitationCarriesTheRuleNameAndTheFirmReasonOnly` and `Approved_ProjectsTheApprovedTotal_TheLinesAndTheAdjustmentWithItsReasonAndRule` failed (2 failed, 38 passed); restored with `git checkout`, tree clean.
- Seeding proof 2 (the Decision Record's invoice-link check dropped): `ARecordWithoutThisInvoicesLineLink_IsNeverProjected` failed (1 failed, 39 passed); restored with `git checkout`, tree clean. The brief asked for one proof "for the visibility filter only"; both are recorded because the task POML names the link check as its seed.
- ArchTests (full, once at the end): 2181 total, 2180 passed, 1 failed: `Task 098: every external invoice-family surface reaches THE (a') predicate (or is listed)` names `FirmFindingsProjection` (a `NamedExternalTypes` and `FamilyReaderTypes` entry placed there for task 130 before the class existed). Its fix is the ledger entry or the predicate on the routes, which is 130b's. At tip `13a8c63292` the suite was 2181/0. Two other failures the first run showed were mine and are fixed: `ADR-038 B12` (a serializer-literal assertion, removed) and `166 K4 C1` (the projection named the field-secured totals; the header read now lives in `InvoiceReviewReadService.ReadHeaderFactsAsync`, a reviewed reader, so no new type names them).
- Publish size: `dotnet publish -c Release` of this branch tip against the same tree with the three changed files reverted to `13a8c63292` (built in this worktree; machine under load): 137,913,793 bytes against 137,858,881 bytes, a delta of +54,912 bytes (+54 KB, all of it `Sprk.Bff.Api.dll`: 25,888,256 against 25,842,176). No package changed. The figure is the publish folder's raw size with symbols, not the compressed artifact the architecture thresholds may use; `scripts/Capture-BffBaseline.ps1` is an HTTP latency probe, not a size tool, and was not used.
- Vulnerable-package check: not re-run; no package reference changed.
