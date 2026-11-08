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

## 130b: routes, firm-scope authorization, writer-created reasons

Branch `task/ebilling-130b` (from `task/ebilling-130a` head `60a2b64dc0`).

### What was built

- `Api/ExternalAccess/FirmOutcomeEndpoints.cs` (new): `MapFirmOutcomeEndpoints`, mounted in `ExternalAccessEndpoints.cs` on the external collaboration group (one line, after the invoice-submission mount). Two GET routes:
  - `/api/v1/external/ebilling/invoices/{invoiceId:guid}/outcome`
  - `/api/v1/external/ebilling/submissions/{submissionId:guid}/outcome`
  - Each chain: `AddFirmOutcomeAuthorizationFilter(subject)`, then `RequireWorkforceCeiling(ReadInvoiceSubmissions)`, then `RequireEbillingEnabled()` (the flag never replaces the authorization decision, so it runs after it).
- `Api/ExternalAccess/FirmOutcomeAuthorizationFilter.cs` (new): the decision. The handlers read the projection only for the id the filter admitted (stored in the request's items); a handler reached without that decision answers 403.
- `Services/Finance/FirmFindingsProjection.cs`: the writer-created hardening (below). `Infrastructure/DI/FinanceModule.cs`: the registration became a factory that passes `BillingDecisionRecorder.WriterUserIdAsync`.

### Status codes (closed set)

| Case | Status | Body |
|---|---|---|
| Unauthenticated | 401 | the host's challenge |
| Group filter cannot resolve the caller | 403 | the resolver's |
| Internal systemuser, no principal, caller with no active firm | 403 | `ebilling.firm-outcome.forbidden` |
| Firm read faults, is unreadable, or returns null | 403 (logged as a system failure) | same |
| Item's firm is not one of the caller's active firms (a shared matter changes nothing); no Read on the item's core record; hidden by the (a') predicate; no (a') predicate registered | 403 | same |
| Item does not exist | 403 | same body as another firm's |
| Invoice/submission read faults, or the visibility decision cannot be made | 503 | `ebilling.firm-outcome.unavailable` |
| eBilling flag off, caller authorized | 503 | `ebilling.disabled` |
| Projection `Degraded` or `Kind == Unavailable` | 503 | `ebilling.firm-outcome.unavailable` |
| Otherwise | 200, `Cache-Control: no-store`, the `FirmOutcome` JSON (the kind is a string) | |

A multi-firm caller sees the items of each of its firms.

Why a missing id is 403 and not 404: the rule is "404 only if the firm check could pass". A missing item has no firm, so the check can never pass for it, and the answer is 403 for a missing and a foreign item alike. This is deliberate: a 404 for a missing id would let any caller probe which ids exist.

### Writer-created reasons (the 130a open question 3, closed)

The Decision Record query now carries `WriterCreatedRecord.AddConditions` (created by the BFF writer, not on behalf of anyone), so a reason can come only from a record the writer created. The rule has one source (the existing `WriterCreatedRecordSingleSourceGuardTests` ArchTest forbids hand-built conditions). The writer id is a `Func<CancellationToken, Task<Guid>>`, called lazily and only when there are decided lines (no decided lines, no call). A non-writer record's line stays, with a null reason. If the id cannot be obtained (a fault or an empty id) the outcome is `Degraded` with the status only, never "no record". `BillingDecisionKey` matching was not added: reduce and disallow lines store no decision id.

### ArchTests

- `AuthenticationSchemeCensusTests`: both routes in `PinnedExternalCollaborationRoutes`.
- `RouteAuthorizationGuardTests.Ledger.cs`: a `GovernedFile` entry (GroupGated) for `FirmOutcomeEndpoints.cs` citing the filter attachment at `:26`, `:37` and the kill switch at `:28`, `:39`; a `CreditedForm` for `AddFirmOutcomeAuthorizationFilter` (a credited form must exist for a GroupGated attachment to be classified), citing the filter's decision lines; `ExpectedEndpointFileCount` 151 to 152 with a history line.
- `ExternalInvoiceVisibilityGuardTests` ("Task 098: every external invoice-family surface reaches THE (a') predicate (or is listed)"): resolved by **reviewed `NotInvoiceSurfaces` entries, not by calling the predicate in the handlers.** Why: the filter is the single place that asks the predicate for the one item, and it precedes every handler on the route; asking again in the handler would repeat the same reads for the same answer. The two handlers and the 12 `FirmFindingsProjection` members the scan flags (the projection is the reader behind the handlers, and was already a `NamedExternalTypes` entry) are listed, each with a reason. The filter itself is seen reaching the predicate.
- `InvoiceMoneyColumnsAppOnlyWriteGuardTests` ("166 K4 C1"): the filter reads `sprk_vendororg` (one of the seven field-secured columns) to compare the invoice's firm with the caller's firms, so it is added to `Readers`. This file was not named in the brief; the guard fails without it.

### Decisions taken inside the brief (for review)

1. The filter does every check, including the (a') decision, and the handlers do none. The alternative (the handler re-asking the predicate) doubles the reads.
2. A submission whose invoice exists is projected by the submission route only for an Accepted submission; the submission seam (`VisibleSubmissionsAsync`) decides the submission's own invoice as well.
3. The refusal body of every 403 is identical and carries no item data (asserted in the tests), so the status says nothing about why.
4. The same `ReadCallerFirmsAsync` / `IsSystemUser` helpers of the invoice-submission filter are reused, not copied.

### Component justification (CLAUDE.md section 11)

`FirmOutcomeEndpoints` (two routes), `FirmOutcomeAuthorizationFilter`, and the mount line in `ExternalAccessEndpoints.cs`:

1. Existing: `ExternalInvoiceSubmissionEndpoints` / `ExternalInvoiceSubmissionAuthorizationFilter` (submit, read and list a submission; they return the submission, not the decided lines), and the 130a projection (no routes). No existing route returns the firm's outcome view.
2. Extension: the submission filter's rules differ (a read is a uniform 404, a submit needs Create); the outcome needs the firm check on the invoice (not only the submission), the (a') decision for an invoice, and a 403 that hides existence. It reuses the submission filter's firm read and systemuser test, and the same (a') seams, unchanged. The routes could not be added to `ExternalInvoiceSubmissionEndpoints` without mixing two authorization shapes in one chain table.
3. Cost of doing nothing: FR-38 fails (a firm cannot see why a line was reduced, and the notice 131, PDF 133 and SPA 134 have no route to read), and any later route would re-derive who may see an outcome.

The `FirmFindingsProjection` DI factory registration: Existing: `BillingDecisionRecorder.WriterUserIdAsync` is reused; no new abstraction. Cost of doing nothing: the writer filter cannot be applied.

### BFF hygiene (section 10)

- Placement: BFF, `Api/ExternalAccess` beside the invoice-submission routes (B1 caller identity from the principal, B4 live request).
- No package added, no HIGH CVE introduced (no package reference changed; `dotnet list package --vulnerable` not re-run).
- Publish-size delta: see "Results (130b)".

### Results (130b)

- Integration auth tests `FirmOutcomeScopeTests` (33, in `tests/integration/auth/Finance`, compiled into the unit project): 33 passed.
- Unit: `FirmFindingsProjectionTests` 45 passed (6 new writer-filter tests: a non-writer reason is never shown, a record created on the writer's behalf is not shown, a writer reason is shown, a writer-id fault is Degraded, an empty id is Degraded, no decided lines means no writer-id call).
- Broader unit run (`Finance`, `ExternalAccess`, `Module`, `DependencyInjection` name filters): 12235 total, 12231 passed, 0 failed, 4 skipped.
- Seeding proof 1 (firm scope): the `firms.Contains(...)` term removed from both firm checks in the filter. 6 of 33 failed: the another-firm invoice and submission tests (both marked "SEEDING PROOF"), the own-firm-versus-other test, the multi-firm test, the "kill switch runs after authorization" test and the "missing is the same 403 as another firm's" test. The file was restored from a copy; its SHA-256 matched the original (`317a772a...e8879`).
- Seeding proof 2 (writer-created filter): the `WriterCreatedRecord.AddConditions` call replaced by `_ = writer;`. 3 of 78 failed: the unit must-fire test (a record the writer did not create is never projected), the empty-id Degraded test, and the route-level forged-reason test. Restored from a copy; SHA-256 matched (`4a1c7aec...40240`).
- ArchTests (full, once): 2181 total; the first full run had 2180 passed and 1 failed (`166 K4 C1`, the filter naming `sprk_vendororg`, fixed by listing it as a reader). The four affected classes were re-run after the fix (121 passed). At the 130a tip the suite was 2180 of 2181 (the 098 test, which this task resolves).
- Publish size: `dotnet publish -c Release` of this branch tip against the same tree with the 130b source changes reverted to `60a2b64dc0`: 137,934,313 bytes against 137,913,813 bytes, a delta of +20,500 bytes (+20 KB; `Sprk.Bff.Api.dll` 25,905,664 against 25,888,256, +17 KB). No package changed. Raw publish folder size with symbols, not the compressed artifact. Against master the 130a delta (+54 KB) adds to this one.
