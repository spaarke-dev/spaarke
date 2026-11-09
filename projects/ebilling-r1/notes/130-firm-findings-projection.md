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
    public Task<FirmOutcome?> GetForSubmissionAsync(Guid submissionId, bool invoiceDecided, CancellationToken ct); // null: the submission does not exist (invoiceDecided: fix round 2)
}

public sealed record FirmOutcome(FirmOutcomeKind Kind, string? TrackingNumber, string StatusLabel,
    decimal? BilledTotal, decimal? ApprovedTotal, IReadOnlyList<FirmOutcomeItem> Items, bool Degraded);
public sealed record FirmOutcomeItem(FirmOutcomeDecision Decision, Guid? LineId, FirmOutcomeLine? Line,
    FirmOutcomeAdjustment? Adjustment, string? Reason, IReadOnlyList<string> FirmReasons, // FirmReasons: 130b fix round 2
    IReadOnlyList<FirmOutcomeCitation> Citations);
public sealed record FirmOutcomeLine(DateOnly? Date, string? TimekeeperAsBilled, string? DescriptionExcerpt,
    decimal BilledAmount, decimal? ApprovedAmount);
public sealed record FirmOutcomeAdjustment(string Method, decimal? Value, decimal Amount);
public sealed record FirmOutcomeCitation(string? RuleName, string? Label, int? Version, // FirmReason moved to the item (fix round 2)
    string? SectionReference, string? Quote);
```

Enums serialize as strings. No caller check: the caller establishes that the invoice may be shown and passes ids it read server-side. No exception other than cancellation leaves the class: a header fault gives `Kind = Unavailable`; a later fault gives the status only with `Degraded = true`. 130b should map `Degraded` / `Unavailable` to a 503, not a 200 with an empty list.

## Rules as built

| State | Projected |
|---|---|
| Submission Received / Processing | status, tracking number, billed total (from the submission's own derived invoice once it exists AND the route's filter decided it, else null; 130b fix round 2); no reads of lines, records or Signals |
| Submission Accepted | the invoice's outcome |
| Submission Rejected | status only (see open question 1) |
| Submission Failed, unknown | `Closed`, status only, not degraded (130b fix round 1; was `Unavailable`) |
| Invoice In Review | status, billed total, reviewer-added lines, Reduced and Disallowed lines with the reviewer's reason; no approved total; no invoice-level adjustment |
| Invoice Approved / Sent to AP / Paid | the same items plus invoice-level adjustments, and the approved total; Approved with a Reduced, Disallowed or Adjustment item is `ApprovedWithAdjustments` |
| Invoice Rejected / Returned to Firm | status and billed total only (reject and return reasons are not in 130's contract) |
| Invoice Cancelled / Superseded / unknown code | `Closed`, status only, not degraded (130b fix round 1; was `Unavailable`) |

Never projected: Pending, Flagged, Clean and Accepted lines, overridden findings, AI observations, a forged reviewer marker, a line or record of another invoice.

Where each part is read (all app-only, through `IGenericEntityService`):

- Lines: `InvoiceReviewReadService.LineQuery` + `ReadPagedAsync` (plus the `sprk_adjustmentrule` column, which the review column set omits). A row whose `sprk_invoice` is not the invoice is dropped.
- Reviewer-added: `ExtractionProvenance.IsReviewerAdded`.
- Reason: the `reduce-line` / `disallow-line` / `add-line` Decision Record whose `sprk_regardinginvoiceline` is one of this invoice's decided lines (and whose `sprk_regardinginvoice`, if set, is this invoice) with the action code the line's current status needs; the newest wins. A record with neither link of this invoice is never projected, whatever the query returns. `sprk_confirmedby`, `sprk_steps`, `sprk_factsnapshot` are not selected.
- Citations (line items): the Signals the chosen record resolved (`sprk_signal.sprk_decisionrecord`), one citation per distinct policy version, with `sprk_firmreason` (first non-blank in creation order; null when none, never substituted; the Signal message is not selected). Citations (adjustments): `sprk_adjustmentrule`, no firm reason.
- Rule name: `sprk_policy.sprk_name` of a Billing policy. A version whose Billing policy cannot be read is not cited. The rule set's name is never read (since 130b fix round 2 its Procedure link is, for the InternalStandard check).
- Clause: `FirmClauseReader` (= `RuleSetService.GetFirmClauseAsync`) once per distinct policy version per invoice. Text returned: label, version number, section reference, quote. None (flag off, version not applied, any fault inside the reader): ~~rule name and firm reason only~~ no citation at all since 130b fix round 2 (see there).

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
  - Each chain: `AddFirmOutcomeAuthorizationFilter(subject)`, then `RequireEbillingEnabled()` (the flag never replaces the authorization decision, so it runs after it). Corrected in fix round 1: `RequireWorkforceCeiling(...)` is endpoint metadata, not a filter in that chain. The workforce ceiling is the external GROUP's filter (`WorkforceCeilingEndpointFilter`, after the caller-principal filter), so it runs FIRST, before the route's filter, and its answer does not depend on the id. The sets are `ReadInvoiceOutcome` and `ReadSubmissionOutcome` since fix round 1 (see below).
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
| Projection `Degraded` or `Kind == Unavailable` (a read FAULTED, the invoice has no status, or an Accepted submission's invoice was not decided by the filter: fix round 2) | 503 | `ebilling.firm-outcome.unavailable` |
| Projection `Kind == Closed` (a closed state at the time of the request; fix round 1) | 200 | status label and tracking number only |
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
2. A submission whose invoice exists is projected by the submission route only for an Accepted submission; the submission seam (`VisibleSubmissionsAsync`) decides the submission's own invoice as well. Superseded by fix round 1 (F1): the seam alone was not enough, because the projection reads the DERIVED invoice id. The filter now decides that invoice exactly as the invoice route does.
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

## 130b fix round 1 (adversarial pass 1)

Branch `task/ebilling-130b-fix1` from `task/ebilling-130b` at `34f6188cb5`. The rule throughout: a firm sees only its own firms' invoice outcomes.

### Fixed

- **F1, the submission route decides the submission's invoice too.** `FirmOutcomeAuthorizationFilter.DecideSubmissionAsync`, after the submission's own firm, core record and seam checks, runs `DecideInvoiceAsync` on `InvoiceSubmissionPersister.InvoiceIdFor(submissionId)`. That is the id the projection reads. The invoice gets the same firm, core-record and (a') checks as on the invoice route. No invoice yet (`RecordNotFoundException`) passes; a refusal is the same 403 and a fault the same 503. A submission is shown only when it is the caller's AND its invoice is visible to the caller (uac-r2's rule). Its firm and core record are the submission's own columns, which say nothing about the invoice.
  - Tests: `ASubmission_WhoseInvoiceIsAnotherFirms_IsTheSame403AsAForeignSubmission` (the probe: byte-identical body, nothing projected), `ASubmission_WhoseInvoiceIsNotVisible_Is403` (another firm, an unreadable record, hidden by (a')), `AReceivedSubmission_WhoseExistingInvoiceIsAnotherFirms_Is403`.
  - Must-not-fire: `AnOwnSubmission_WithAnOwnInvoice_Is200` and `AReceivedSubmission_WithNoInvoiceYet_Is200_StatusOnly`.
- **F1, closed states are not faults (a contract change).** (Fix round 2: "permanent" was wrong. A closed state is closed at the time of the request; undoing a correction returns a superseded invoice to In Review.) A new `FirmOutcomeKind.Closed`. A Failed submission, a Cancelled invoice, a Superseded invoice (also reached through an Accepted submission whose invoice a correction replaced) and an unknown status code are `Closed`: status label and tracking number only, no items, no totals, `Degraded = false`, so the route answers **200**. An unknown code is logged as a warning. `Unavailable` / `Degraded` (503) now mean a FAULT only. Authorization has already passed, so the 200 tells the caller nothing new about the item.
  - Labels: `Failed`, `Cancelled`, `Replaced by a correction`, and `Closed` for an unknown code.
  - An Accepted submission whose invoice cannot be found is still 503 (not a closed state the brief names: the data is inconsistent, not closed).
  - Tests changed to the new contract: `SubmissionStates_MapToTheirStatus` (Failed: 200 Closed) and `AnUnavailableProjection_Is503` (header fault only; Cancelled moved to `APermanentInvoiceState_Is200_StatusOnly`). Unit: `PermanentStates_AndUnknownCodes_AreClosed_StatusOnly_NotDegraded`, `RejectedSubmission_ProjectsStatusOnly_FailedIsClosed_AndMissingProjectsNothing`, `AnAcceptedSubmission_WhoseInvoiceWasSuperseded_IsClosed`.
  - SPA (134): one new `kind` value, `Closed`; no other field changed. No client code reads the outcome yet (no match in `src/client` or `src/solutions`).
- **F2, a failed billed-total read is not swallowed.** A Received or Processing submission whose invoice total read faults is status-only `Degraded` (503 at the route), never a 200 with a null total. Tests: `ASubmissionWhoseBilledTotalReadFaults_IsDegraded_NeverANullTotal` (unit) and `AReceivedSubmission_WhoseBilledTotalReadFaults_Is503` (route).
- **F3 (i), two firms, one clause.** `ACallerInTwoFirms_GetsTheClauseOfTheInvoicesFirmOnly`: a caller in firms A and B views firm A's invoice through a clause reader keyed by the invoice's firm. It gets A's clause and label only, and the reader is asked only about A's invoice.
- **F3 (ii), a submission-seam fault.** `ASubmissionDecisionThatFaults_Is503_AfterTheRecordChecks` covers a failed-read result (the seam's contract) and a thrown exception: both give 503. Another firm's submission is still 403 before the seam is asked. The filter now also turns an exception the seam throws into a 503; before, it was an unhandled 500.
- **F4, the workforce ceiling declares what each route returns** (owner-approved edit to `WorkforceRoleCeilingGate.cs`, in the file's own pattern). Changes:
  - `BillingEventTable = "sprk_billingevent"`.
  - `ReadInvoiceOutcome` = Read on `sprk_invoice` + `sprk_billingevent` (the invoice route).
  - `ReadSubmissionOutcome` = Read on `sprk_invoicesubmission` + `sprk_invoice` + `sprk_billingevent` (the submission route).

  Tests in `FirmOutcomeScopeTests`, whose test group now runs the real `AddWorkforceCeilingFilter` after the principal filter, as the production group does:
  - must-fire: `AWorkforceContact_WithoutReadOnAReturnedTable_IsRefusedByTheCeiling`, five cases: invoice route without invoice or line Read; submission route without submission, invoice or line Read. Each is the ceiling's 403, with no membership read, no record read and no projection read.
  - must-not-fire: `AWorkforceContact_WithEveryDeclaredRead_IsServed`, 200 on both routes.
  - the declared sets are pinned in `TheRoutes_DeclareTheReadsTheyReturn`.

  The owner-table test double (`WorkforceCeilingDouble.OwnerPrerequisite`, `tests/integration/auth/UnifiedAccessControl/ExternalCollaborationTestFixture.cs`) gains `sprk_billingevent` Read. Without it `TheFixtureDefault_IsEveryRoutesPrivilege` fails, since the owner table must equal the union of the declared sets. The dev Workforce role needs that privilege for workforce contacts to keep these routes (being checked on dev by the lead, not here).
- **K3, the projection test double.** `FirmFindingsProjectionTests.World` now applies the line query's nested filters (`ExcludeInactiveAdjustments`: not an Adjustment, no cost type, or `statecode` Active). An operator it cannot evaluate throws, so no criterion is silently ignored. Top-level conditions stay unevaluated, so the "another invoice's line" test still proves the projection's own check. New test: `ARemovedAdjustment_AndItsReason_NeverReachTheFirm`.
- **K4, stale text.**
  - `ExternalInvoiceVisibilityGuardTests`: `NamedExternalTypes` now names `Sprk.Bff.Api.Api.ExternalAccess.FirmOutcomeEndpoints` (it said `Api.Finance`).
  - The `PendingSurfaces["130"]` entry is removed, since 130b landed. Only `TheLaterSurfaces_AreCoveredByThePrimitives` depended on it: it now expects `{093, 152}` and checks that every named reader and named external type exists, the projection included.
  - The `Excerpt` reason now says it is an internal static helper trimming the line description.
  - The two submission-route reasons name the new invoice decision.
  - The filter-order claim above is corrected.

### Workforce-ceiling census (F4): external routes returning `sprk_invoice` or `sprk_billingevent` content

| Route | Declared | Status |
|---|---|---|
| `GET /ebilling/invoices/{id}/outcome` | `ReadInvoiceOutcome` | fixed here |
| `GET /ebilling/submissions/{id}/outcome` | `ReadSubmissionOutcome` | fixed here |
| `GET /invoice-submissions/{id}` (`ExternalInvoiceSubmissionEndpoints.cs:64-66`; reads the invoice's status at :152 for the hold warning and the replaced-by-correction note) | `ReadInvoiceSubmissions` | handled in 130-0 fix1 (it will declare `sprk_invoice` Read) |
| `GET /invoice-submissions` (`:75-77`; same read at :298) | `ReadInvoiceSubmissions` | handled in 130-0 fix1 |
| `GET /invoices/{id}/documents`, `/content`, `/versions`, upload (`ExternalProjectDataEndpoints.cs:386-420`) | `ReadInvoiceDocuments` / `UploadInvoiceDocument` (Read on `sprk_invoice`) | already declared |
| `GET/POST /invoices/{id}/messages` | `ReadMessagesOn` / `PostMessageOn("sprk_invoice")` | already declared |
| Module-data seam (`invoices` module) | Read on the module's own table, in the handler | already declared |
| Notification feed | filtered per table (`CanRead`) | already declared |

No other external route returns `sprk_billingevent` content.

### Known limits (no fix this round)

- K1: Signals and the line status, amount and adjustment columns are trusted without a writer filter. This is the same native-write limit as 085. `WriterCreatedRecord` on Signals waits for a single Signal writer.
- K2: since the 130-0 merge, the invoice route reads and compares the caller's firms twice (the filter, then the seam). A fault on the second read gives 503 where a fault on the first gives 403.
- (Removed in fix round 2: the window in which an invoice created after the filter had its total read without the invoice decision is closed by the `InvoiceDecided` rule; the missing route-family rows were added.)

### Seeding proof (F1 submission fix)

See "Results (fix round 1)".

### Results (fix round 1)

- Filtered unit run (`FirmOutcome`, `FirmFindingsProjection`, `ExternalInvoiceVisibility`, `InvoiceSubmission`, `WorkforceCeiling`, `ExternalNotificationFeed`): 2177 passed, 0 failed. `FirmOutcomeScopeTests` and `FirmFindingsProjectionTests` together: 104 (78 before; 26 new).
- Seeding proof (F1, the submission's invoice decision): the `DecideInvoiceAsync` call at the end of `DecideSubmissionAsync` was replaced by `return null;`. 6 of 55 `FirmOutcomeScopeTests` failed: the probe (`ASubmission_WhoseInvoiceIsAnotherFirms_IsTheSame403AsAForeignSubmission`), the three `ASubmission_WhoseInvoiceIsNotVisible_Is403` cases, `AReceivedSubmission_WhoseExistingInvoiceIsAnotherFirms_Is403`, and the must-not-fire `AnOwnSubmission_WithAnOwnInvoice_Is200` (it asserts that the invoice decision was asked). The file was restored from a copy; its SHA-256 matched (`41406900...be83408`).
- Fail-before check for the other changes, in one mutated build:
  - `Closed` built as the old `Unavailable`;
  - the billed-total fault swallowed again;
  - both routes back on `ReadInvoiceSubmissions`;
  - the filter's catch of a thrown seam exception removed;
  - the test double ignoring nested criteria.

  19 of 104 failed: every permanent-state, F2, K3 and seam-throw test, and the four ceiling must-fire cases the old declaration did not cover. The submission-table case passed, as expected (it was always declared). All four files were restored; their SHA-256 matched.
- ArchTests (full, once): 2181 total, 2181 passed, 0 failed.
- Broader unit run (`ExternalAccess`, `UnifiedAccessControl` name filters, for the owner-table double): 5022 passed, 0 failed.

## 130b fix round 2 (adversarial pass 2, and the F4b owner ruling)

Branch `task/ebilling-130b-fix2` from `task/ebilling-130b-fix1` at `7a962ffd9d`, pushed to `task/ebilling-130b-fix1` (fast-forward).

### Fixed

- **F1, the `InvoiceDecided` rule.** `FirmOutcomeScope` now records whether the filter decided, by the invoice rule, the invoice the outcome would show (`FirmOutcomeAuthorizationFilter.cs`: `DecideInvoiceAsync` returns the refusal and the flag; a submission's invoice that does not exist yet passes UNDECIDED; the flag is stored with the admitted id). The submission handler passes it to `FirmFindingsProjection.GetForSubmissionAsync(submissionId, invoiceDecided, ct)`. With it false nothing is read from that invoice: Received or Processing is status only with a null total (not degraded, 200); Accepted is `Unavailable` (503), so the retry re-enters the filter, which then decides the invoice. The invoice route always decides its invoice (true).
  - Must-fire (route, the reviewer's probe turned into tests): `AnUndecidedInvoice_InProgress_Is200_WithNoBilledTotal_AndIsNeverRead` (Received and Processing; the invoice exists only in the projection's store, as another firm's on an unreadable matter: no billed total in the body, no `sprk_invoice` read, no invoice decision asked) and `AnUndecidedInvoice_Accepted_Is503_AndIsNeverRead`.
  - Must-fire (the window itself): `AnInvoiceCreatedBetweenTheFilterAndTheProjection_IsNotShown` (Processing and Accepted). The filter's invoice read misses, and the invoice (own firm, readable matter) is created right after it. No total and no content; Accepted is 503. The retry is decided and shows the content.
  - Must-not-fire: `ADecidedInvoice_InProgress_ShowsItsBilledTotal` (route) and `ADecidedInvoice_ShowsTheBilledTotal` (unit); `AnOwnSubmission_WithAnOwnInvoice_Is200` unchanged.
  - Unit: `AnUndecidedInvoice_IsNeverRead_InProgressIsStatusOnly`, `AnUndecidedInvoice_OfAnAcceptedSubmission_IsUnavailable`.
  - Changed premise: `AReceivedSubmission_WhoseBilledTotalReadFaults_Is503` now seeds the invoice where the filter can see it (a total is read only for a decided invoice).
- **K1, a null `statuscode` is not Closed.** `InvoiceReviewReadService.ReadHeaderFactsAsync` turned a null status into 0, which read as an unknown code and so as Closed (200). Its only caller is the projection (checked: no other reference to `ReadHeaderFactsAsync` or `InvoiceHeaderFacts`). So `InvoiceHeaderFacts.StatusCode` is now `int?` (no `?? 0`), and the projection maps a null status to `Unavailable` (503). Test: `AnInvoiceWithNoStatusCode_IsUnavailable_NotClosed` (directly and through an Accepted submission).
- **K1 (an Accepted submission whose invoice is missing).** Still 503 (the data is inconsistent). Corrected in fix round 3: on the real route the filter passes a missing invoice undecided (`invoiceDecided == false`), so the common path is a Warning, not an Error. The stable code `EBILLING-FIRM-OUTCOME-ACCEPTED-WITHOUT-INVOICE` (`FirmFindingsProjection.AcceptedWithoutInvoiceCode`) is carried by both: a Warning on the undecided path (a retry may decide it, or the data stays inconsistent), and an Error in the rare race where the invoice was decided but is gone by the read. Tests: `AnAcceptedSubmission_WithNoInvoice_IsUnavailable_AndLoggedWithAStableCode` (Error, decided) and `AnAcceptedSubmission_WithNoInvoice_Undecided_IsUnavailable_AndWarnedWithTheStableCode` (Warning, undecided). Known limit: this is a permanent 503 until the data is repaired.
- **F4b, the ceiling (owner ruling).** `ReadInvoiceOutcome` and `ReadSubmissionOutcome` also declare Read on `sprk_decisionrecord` (the reviewer's reasons) and `sprk_signal` (a cited rule's firm reason). `sprk_policy`, `sprk_policyversion`, `sprk_procedure` and `sprk_ruleset` are NOT declared; see the citation rule below. `WorkforceCeilingDouble.OwnerPrerequisite` gains both, so the dev Workforce role needs Read on them for workforce contacts to keep these routes.
  - Must-fire rows: `AWorkforceContact_WithoutReadOnAReturnedTable_IsRefusedByTheCeiling` gains one row per new table per route (4).
  - Pins: `TheRoutes_DeclareTheReadsTheyReturn` (it also asserts the four rule tables are absent).
- **Rule citations are published by the firm flag (owner ruling, the hard rule).** Design statement: rule citations on a firm's line are shown only as the policy's firm flag publishes them; the workforce ceiling covers the line, decision and signal tables.
  - A citation exists only when the clause reader returned text for the version on this invoice (`RuleSetService.GetFirmClauseAsync`: the rule set's visible-to-firms flag on AND the invoice's snapshot applied the version) AND the rule is not an InternalStandard Procedure's.
  - Otherwise the citation is omitted entirely: no rule name, no label, no version, no clause (the four citation fields of the ruling). The line, the reviewer's reason and the firm reason stay. Before this round, the rule name was shown with the flag off.
  - A fault reading the flag (a clause reader that throws), the rule set or the Procedure omits the citation; it no longer degrades the whole outcome.
  - InternalStandard: the BFF already keeps an InternalStandard Procedure's flag off:
    - `ProcedureService.cs:1221-1222`: `InitialVisibleToFirms` is off for InternalStandard;
    - `ProcedureService.cs:439`: a type change resets the flag;
    - `RuleSetService.cs:2090`: the rule set's visibility route refuses a Procedure-owned set.
  - A native write to the flag is not blocked by those, so the projection also reads the policy's rule set (only `sprk_procedure`) and the Procedure (only `sprk_proceduretype`). An InternalStandard rule is never cited, whatever its flag.
  - Fail closed on the citation: a policy with no rule set, a rule set or Procedure not returned, a Procedure with no type, or a fault reading either, cites nothing. A rule set with no Procedure (a legacy or record rule set) is decided by its flag alone. Names of rule sets and Procedures are never read.
  - Tests (unit):
    - must-fire, flag off: `FlagOff_TheRuleIsNotCited_NoNameNoLabelNoClause`;
    - must-fire, InternalStandard with the flag on: `AnInternalStandardProceduresRule_IsNeverCited_WhateverItsFlag`;
    - must-not-fire, flag on: `FlagOn_ACitationCarriesTheLabelVersionSectionAndQuote_AndTheRuleNameIsThePolicysNotTheRuleSets`, `FlagOn_ALegacyRuleSetHasNoLabel_ButStillShowsVersionSectionAndQuote`;
    - fault: `AFaultDecidingACitation_OmitsIt` (clause reader, rule set, Procedure); undecidable type: `AnUndecidableProcedureType_CitesNothing` (4 cases);
    - no ids: `NoPolicyVersionRuleSetOrProcedureId_IsInTheSerializedBody` (the serialized body carries no policy, version, rule set or Procedure id, in D or N form). At the route: the two-firm clause test also asserts that the version id is absent.
  - Changed to the new rule: `Approved_ProjectsTheApprovedTotal_TheLinesAndTheAdjustmentWithItsReasonAndRule` (version B unpublished: no citation), and four tests that relied on the old flag-off citation now publish it (`ABlankFirmReason_IsOmitted`, `ARuleCitedByTwoSignalsOfOneDecision_IsOneCitation`, `ASignalNotResolvedByTheLinkedRecord_IsNotACitation`, the fault theory). `AFaultInTheClauseReader_ProjectsTheStatusOnly` is replaced by the fault theory above.
  - The firm reason is not a citation (lead correction, Q1). `sprk_signal.sprk_firmreason` is the explanation addressed to the firm, and the ceiling now declares `sprk_signal`, so it is shown whenever present, whatever the flag, a fault deciding a citation, or an InternalStandard rule. It comes from the same source as before this round: the first non-blank reason per Billing rule version the decision resolved.
  - Contract change this required: the reason moved from `FirmOutcomeCitation.FirmReason` (string, one per citation) to `FirmOutcomeItem.FirmReasons` (a list of strings: one per rule version that has one, duplicates removed, in Signal creation order; empty for an adjustment and when none). `FirmOutcomeCitation` is now `(RuleName, Label, Version, SectionReference, Quote)`. No other field changed. In JSON: `items[].firmReasons` replaces `items[].citations[].firmReason`. 131, 133 and 134 have no client code reading the outcome yet; they see a firm reason whenever the old shape had one.
  - Tests:
    - must-not-fire (firm reason), flag off: `FlagOff_TheRuleIsNotCited_NoNameNoLabelNoClause_ButTheFirmReasonIsShown`;
    - flag on, both shown: `FlagOn_ACitationCarriesTheLabelVersionSectionAndQuote_AndTheRuleNameIsThePolicysNotTheRuleSets`;
    - citation fault, reason still shown: `AFaultDecidingACitation_OmitsIt` (3 cases);
    - InternalStandard: `AnInternalStandardProceduresRule_IsNeverCited_WhateverItsFlag` also asserts the reason;
    - independence: `TheFirmReasons_AreIndependentOfTheCitationGate` (one published rule, one not: one citation and both reasons; a repeated reason once).
  - Fail-before check: the firm reason gated on the citation again (`citable.Contains`). 6 of 64 `FirmFindingsProjectionTests` failed (the 3 fault cases, flag off, independence, InternalStandard). Restored; SHA-256 matched (`770d7eb3...50505e18`).
- **K3.** `WorkforceCeilingRouteFamilyTests.Routes` lists both outcome routes, one row per declared table (4 on the invoice route, 5 on the submission route), so the shared must-fire, unavailable, must-not-fire and systemuser theories run on them.
- **K4, text.** "Retrying would never change it" was false for Superseded (undoing a correction returns the invoice to In Review). It is now "a closed state at the time of the request" in `FirmOutcomeEndpoints.cs` (`ToResult`), the `FirmOutcomeKind.Closed` comment and these notes. The ledger's `AddFirmOutcomeAuthorizationFilter` credited form names the submission's invoice step and the `InvoiceDecided` rule; every line citation in it and in the `FirmOutcomeEndpoints.cs` governed-file entry was re-checked against the edited files. The two reviewed reasons in `ExternalInvoiceVisibilityGuardTests` (the submission handler and `GetForSubmissionAsync`) now state the `InvoiceDecided` rule.

### Workforce-ceiling census (F4b): external routes returning `sprk_decisionrecord` or `sprk_signal` content

| Route | Declared | Status |
|---|---|---|
| `GET /ebilling/invoices/{id}/outcome` | `ReadInvoiceOutcome` | declared here |
| `GET /ebilling/submissions/{id}/outcome` | `ReadSubmissionOutcome` | declared here |

No other external route returns either table's content. The service-request delete (`ExternalDataService.ServiceRequestRelations`) probes both for existence and returns none of their content; the assistant tools (model 9.1 (d)) are not workforce-ceiling routes.

### Known limits

- An Accepted submission whose invoice is missing is a permanent 503 (inconsistent data), logged with `EBILLING-FIRM-OUTCOME-ACCEPTED-WITHOUT-INVOICE`: at Warning on the undecided path (the usual route case) and at Error in the decided-then-gone race.
- K1 of fix round 1 stands: the Signal, line status and adjustment columns are trusted without a writer filter. The Procedure-type check reads `sprk_proceduretype` and `sprk_procedure` the same way.

### Results (fix round 2)

- Filtered unit run (`FirmOutcome`, `FirmFindingsProjection`, `WorkforceCeiling`, `ExternalInvoiceVisibility`, `InvoiceSubmission`): 2143 passed, 0 failed (final run, after the seeds were restored).
- Seeding proof 1 (F1, the filter): `FirmOutcomeScope` built with `InvoiceDecided` forced to true. 5 of 65 `FirmOutcomeScopeTests` failed: both `AnUndecidedInvoice_InProgress_Is200_WithNoBilledTotal_AndIsNeverRead` cases, `AnUndecidedInvoice_Accepted_Is503_AndIsNeverRead`, and both `AnInvoiceCreatedBetweenTheFilterAndTheProjection_IsNotShown` cases. Restored from a copy; SHA-256 matched (`41ad93c7...a83facb6e`).
- Seeding proof 2 (F1, the projection): both `!invoiceDecided` checks in `GetForSubmissionAsync` disabled. 8 of 128 (`FirmOutcomeScopeTests` + `FirmFindingsProjectionTests`) failed: the 5 above and the 3 unit must-fire tests. Restored from a copy; SHA-256 matched (`7f93f72b...d81e58b`).
- Fail-before check (citations): the citation filter put back to "every readable Billing rule" (`rules.ContainsKey`). 10 of 63 `FirmFindingsProjectionTests` failed: flag off, InternalStandard, the 3 fault cases, the 4 undecidable-type cases and the Approved adjustment. Restored; SHA-256 matched.
- ArchTests (full, once): 2181 total, 2180 passed, 1 failed: `Task 098` flagged the three new projection members (`ReadClauseAsync`, `CitableVersionsAsync`, `ReadByIdsAsync`). They are now listed with reasons; the four affected classes (`ExternalInvoiceVisibilityGuardTests`, `RouteAuthorizationGuard*`, `InvoiceMoneyColumns*`, `AuthenticationSchemeCensus*`) were re-run: 121 passed, 0 failed.
- Publish size: not re-measured. No package changed; the source delta is one projection method group and a tuple in the filter.

## 130b fix round 4 (review follow-up)

Branch `task/ebilling-130b-fix4` from `task/ebilling-130b-fix1` at `fc68d0b44a`.

- **FIX-1, an unknown firm set is a 503, not a 403.** `FirmOutcomeAuthorizationFilter`: a firm read that faults or comes back unreadable now answers `FirmOutcomeErrors.Unavailable()` (the existing retryable shape, code `ebilling.firm-outcome.unavailable`); only a DECIDED empty firm set (the contact confers no organization) stays the uniform 403. The decision is still made before any invoice or submission is read. This supersedes the "Firm read faults, is unreadable, or returns null: 403" row of the status table above.
  - Tests (`FirmOutcomeScopeTests`): must-fire `AFirmReadThatCannotBeCompleted_Is503_AndNoSubjectIsRead` (3 cases: membership fault, membership unreadable, contact unreadable; 503, no projection read, no invoice read) and `Submission_ForACallerWithNoFirm_Is403_AndAnUnreadableFirm_Is503` (also no submission decision asked); must-not-fire `ACallerWithNoFirm_Is403_AndNothingIsRead` and the same submission test's no-firm half.
  - The ledger's `AddFirmOutcomeAuthorizationFilter` credited form was re-cited to the edited line numbers.
- **FIX-2, the projection's callers are pinned.** `FirmFindingsProjection` has no caller check; ArchTest `FirmFindingsProjectionCallerBoundaryGuardTests` (compiled IL scan, `IlCallScan`) fails unless the only types referencing it are `FirmOutcomeEndpoints` and `FinanceModule` (its DI registration). Closed list, no baseline. Controls: a caller by call, by field and by generic resolution (must fire) and a type using only the output records (must not fire). The class remark on `FirmFindingsProjection` names the test and drops the earlier "or an internal job" wording.
- **Q1, one core record per invoice.** Outcome: the filter's core-record check stays as the union (Read on the matter, the project or the work assignment). Findings:
  - The "exactly one core record" rule (FR-05, D-28) is owned by `InvoiceFamilyCoreRecordGuard` (`Check` refuses none and several, `MultipleCoreRecordsCode`). It is called on the BFF create and update paths: the submission persister and service (`Check`, `RequireFiled`), `RecordCreationService`'s pre-write step and `OwnedChildWrite.RefileAsync` (`CheckWriteAsync`), and the three generic update paths (`CheckGenericUpdateAsync`: the playbook update, the output orchestrator, the field-mapping push). Behaviour tests: `InvoiceFamilyCoreRecordGuardTests`, `RecordCreationServiceInvoiceFamilyRuleSeamTests`, `InvoiceFamilyGenericUpdatePathTests`, `SecureChildOwnershipAiToolTests.InvoiceFamilyGuard`.
  - No ArchTest pins that every writer of the three lookups asks the guard (the completeness of the writer set is not pinned), so the answer is partial.
  - The external `invoices` module has no precedence to copy: it ORs its scope dimensions (`ExternalAccessModule.cs` invoices registration, `ExternalModuleRegistry.ScopeRows`, "OR across dimensions", owner decision D2 option A) and adds a Read requirement on secure roots. The filter's union matches the module's row scope. Choosing a precedence (or requiring Read on every set core) is a design decision for the owner; no filter change was made.

## 130c: a rejected submission's findings (FR-18)

Branch `task/ebilling-130c` from `task/ebilling-130b-fix1` at `ecf694a8d1`. Closes 130a open question 1 (where a rejection finding's content comes from): the submission's OWN stored findings. No new route, no new caller of `FirmFindingsProjection`.

### What was built

- `Services/Finance/FirmRejectionFindings.cs` (new, internal): the allow-list parse of `sprk_invoicesubmission.sprk_findingsjson` for the firm view. Stable log codes `EBILLING-FIRM-OUTCOME-REJECTION-FINDINGS-DROPPED` (counts only: malformed / unknown rule type / over the cap) and `EBILLING-FIRM-OUTCOME-REJECTION-FINDINGS-UNREADABLE` (a document that cannot be read as a whole).
- `Services/Finance/FirmFindingsProjection.cs`: `GetForSubmissionAsync` on a Rejected submission now calls `ProjectRejectionAsync` (it returned status only). New contract members: `FirmOutcomeDecision.RejectionFinding`; `FirmOutcomeItem.LineNumber` (int?, one-based) and `FirmOutcomeItem.ValuesRead` (list of `FirmOutcomeValueRead(Name, Value)`), both omitted from the JSON when null, so every existing item serializes as before.
- Tests: new cases in `FirmFindingsProjectionTests` and `FirmOutcomeScopeTests` (listed under Results). ArchTests: `FindingsJsonAppOnlyReadGuardTests` (the projection is now a reviewed reader of the column; row 18 of `notes/122-findings-json-field-secure.md`) and `ExternalInvoiceVisibilityGuardTests` (four reviewed `NotInvoiceSurfaces` entries for the new private members).

### The JSON shape consumed (field names only)

Root: `kind`, `findings[]`. Per finding: `ruleType`, `rejects`, `policyVersionId`, `subject`, `line`, `reasonCode` (required, never shown), `detailWithheld`, `valuesRead[]` with `name` and `value`. Never read: `clause`, `clauseLocation`, `policyId`, `policyCode`, `consequence`, `heldBecause`, `suggestedAdjustment`, `confidence`, `belowReliabilityThreshold`, the per-value `line`, `signalPlan`, `pipelineFindings`, `codingFindings`, `lineFindings`, `procedureSnapshot`, `invoiceId`, `trackingNumber`.

### Rules as built

| Rule | Behaviour |
|---|---|
| Which submissions | A Rejected submission whose stored `kind` is `AutoRejected`. A `Blocked` outcome (also Rejected in status) is an internal hold awaiting a reviewer, not a decision a firm was given (FR-38): no finding. Any other kind: no finding. |
| Which findings | `rejects` true only. A finding that did not reject is skipped silently (not a defect). |
| Unknown rule type (FR-24) | Refused whatever else the entry says, even when it does not reject: the name must be exactly a defined `RuleType` member (not a number, not a comma-joined list whose OR is another member), in the Billing domain, and not the system marker `SubmissionIntegrity`. |
| Malformed | A non-object entry, a wrong-typed member, a missing or empty policy version, a missing reason code, a line subject without a line (or an invoice subject with one), a `rejects` that is not a boolean, a rejection by a type that can never reject (including an AI observation, I-5), a non-string value: dropped. |
| Logging | One Warning per read: stable code plus counts. No content, no ids of the finding. |
| Bounds | 200 findings (the outcome's `notShownCount` says how many more valid rejection findings were left out, 130c fix round 2), 20 values per finding, 200 characters per value (trimmed with "..."; a cut never splits a surrogate pair). |
| Line | `LineNumber` = stored zero-based index + 1, i.e. the Nth line the submission read from the invoice (not a number printed on the invoice); null for an invoice-level finding. Never a line id; no live line is read. |
| Values read | Name and value, trimmed and truncated (not "as stored"), nothing else (no confidence). Only values extracted from the invoice (see "130c fix round 1"). A Duplicate finding and a `detailWithheld` finding project none (a Duplicate quotes an EARLIER invoice the caller may not see: D-112, D-176). |
| Citation (the rule and the clause) | Exactly as an invoice item's (130b fix round 2): only when `RuleSetService.GetFirmClauseAsync` publishes text for that version on the submission's derived invoice AND the rule set's Procedure is not InternalStandard; otherwise none (no rule name, label, version, section or quote). A fault deciding it omits it; it is never a guess. No ids. |
| Firm reason | From a `sprk_signal` the BFF writer created (`WriterCreatedRecord`), for the derived invoice, the finding's policy version and its line (the derived line id; none for an invoice-level finding), first non-blank `sprk_firmreason` in creation order. Independent of the citation gate. A fault, or an unknown writer id, makes the outcome Degraded (503), as for a decided line. |
| Invoice not decided | `invoiceDecided` false (the derived invoice did not exist when the route's filter ran): the findings keep their line and values (the submission's own), but there is no citation, no firm reason, and nothing of the invoice is read (no clause call, no Signal, no rule row, no writer id). |
| Faults | Reading the stored findings, the rules or the Signals: status only, `Degraded` (route 503). A fault reading the Procedure type (or the firm flag) omits the citation and is not a fault of the outcome (`CitableVersionsAsync` catches it). A stored document that cannot be read, or has no usable rejection finding: status only, not degraded (route 200), with the stable-coded Warning when it was unreadable. |

### Decisions taken inside the brief (for review)

1. Flag off shows NO citation at all (no rule name, no rule set label), following 130b fix round 2 (the owner ruling that supersedes the earlier "rule name and reason" reading of D-79). The brief's D-79 line says "reason + rule set name only"; a rule set name cannot be shown because the POML forbids reading `sprk_ruleset.sprk_name` and the clause reader returns no label with the flag off. Open question 1.
2. The rule type is validated but not projected: it is a category of an internal rule, and the owner ruling publishes the rule only as the firm flag allows.
3. `rejects` true is the definition of a rejection finding (073's stored decision), not "every stored finding". A firm is not shown flagged findings that did not reject.
4. A Blocked outcome projects no finding even though its submission status is Rejected.
5. The firm reason is read from Signals (the only stored home of `sprk_firmreason`), not composed from the reason code; `reasonCode` is validated and never shown, because no firm-text composer for it exists.

### Component justification (CLAUDE.md section 11)

`FirmRejectionFindings` (internal parser) and the new projection members:

1. Existing: `InvoiceReviewReadService.ParsedFindings` (private; the INTERNAL reader: confidence, stored clause, suggestion, Signal plan) and `InvoiceSubmissionAfterSteps.StoredFindings` (the writers' reader). The projection's own citation, clause, rule-name and Signal-read helpers are reused unchanged.
2. Extension: neither existing parser can be narrowed to an allow-list without a second rule set inside it, and neither refuses an unknown rule type (FR-24). A small second parser with a closed member list is the smaller surface.
3. Cost of doing nothing: FR-18's on-screen report (and the PDF, FR-40) cannot be built, or is built by copying the internal reader's members (confidence, the internal clause).

### BFF hygiene (section 10)

Placement: BFF, `Services/Finance` beside the projection (B4 live request). No package, no endpoint, no DI change. No HIGH CVE introduced (no package reference changed). Publish size: not re-measured (one small internal class and one projection method group; no package change).

### Known limits

- `sprk_signal.sprk_firmreason` still has no writer (130a decision 3): until one exists the firm reason of a rejection finding is empty in production, as an invoice item's is.
- Which Signal belongs to a finding is decided by (invoice, policy version, derived line id). If the Signal writer subjects a line finding differently, the reason is absent (fail closed), never wrong.
- A rejected submission whose derived invoice was never created shows its findings without citation or reason.

### 130c fix round 1

Changes after the first adversarial review pass (closed set of items):

- **F1-a, which values are shown.** The projection shows a value only when the stored entry names the invoice field it was extracted from (the persister writes `field` from the finding's extracted-field reference; a value read from the system's own tables has none). Every other value is dropped. As a backstop, a value whose text parses as an identifier is dropped. `field` is now read, but only for whether it is present. No per-type reference-value list was added.
- **F1-b, a missing field is named.** A value with no text is kept as an empty value (name kept) for a finding that is not withheld and not a Duplicate, so a RequiredData rejection tells the firm which field is missing. RequiredData stores that value through the extracted-field helpers, so its `field` is present and the F1-a rule keeps it. The same applies to any extracted value that was read as empty. (Corrected in fix round 2: this does NOT include the credit-test reads of an ApprovedRate finding. `WithCreditTestReads` creates those with null confidence, and `SubmissionRejectionRule` holds any finding that lists a read with unknown confidence, so such a finding never rejects and is never projected.)
- **F3, tests use the writer's real shape.** The stored entries in the new tests come from `InvoiceSubmissionPersister.FindingEntryJson` over findings produced by the real `ApprovedRateHandler` and `RequiredDataHandler` (through the existing handler test kit). The hand-built fixtures use real reject-capable reason codes instead of a made-up one.
- **K1, parse faults.** The call to the parser in `ProjectRejectionAsync` is inside fault handling: any exception other than the caller's own cancellation is treated as an unreadable document (status only, `Degraded` false, the stable UNREADABLE Warning, no content). The parser is replaceable through an internal init-only member so the test can inject a fault.
- **K2, reject-capable reasons only.** An entry is malformed (dropped, counted in the DROPPED Warning) unless `BillingRuleCatalogue.IsRejectCapableReason(type, reasonCode)` holds and the type is not hold-only.
- **K3, value name cap.** A value name over 100 characters drops the value.
- **K4, notes corrected.** A fault reading the Procedure type omits the citation; it does not make the outcome Degraded. Values are trimmed and truncated, not "as stored".

Results (fix round 1): filtered run (`FirmFindingsProjectionTests` + the `FirmOutcome*` classes) 177 passed, 0 failed (8 new tests). Fail-before: with the F1-a, F1-b, K1, K2 and K3 changes temporarily reverted, 13 of the 177 fail, including every new test (the real-shape ApprovedRate test, the extracted-field test, the two-RequiredData test, the parser-fault test, the reason test and the name-cap test); the files were restored and their SHA-256 verified. Full `Spaarke.ArchTests`: 2183 passed, 0 failed. The existing integration fixture in `FirmOutcomeScopeTests` now stores real reason codes and the writer's `field` member.

### Results (130c)

- Filtered run (`FirmFindingsProjectionTests` + the `FirmOutcome*` classes, including `FirmOutcomeScopeTests`): 169 passed, 0 failed.
- Fail-before proofs (the real code temporarily replaced, the suite run, the real code restored and its SHA-256 verified):
  - Old behaviour (a rejected outcome returns the status only): 27 of the 165 tests in the two classes fail, among them the must-fire two-findings test, the route happy path and the route 503 test.
  - FR-24 (security-class): the unknown-rule-type refusal replaced by a plain enum parse: the seeded unknown-type test fails (1 of 94 in the projection class).
  - EB-D-208: the writer-created conditions dropped from the Signal read: 3 tests fail (the firm-reason test, the writer-id test, the route happy path).
- ArchTests: the full `Spaarke.ArchTests` run on the final tree, 2183 passed, 0 failed (includes the reviewed-reader guard for the field-secured column, the invoice-visibility guard's new reviewed entries, the caller-boundary guard and the writer-created single-source guard).

### 130c fix round 2

Branch `task/ebilling-130c-f2` from `task/ebilling-130c` at `adb765caa3`, pushed to `task/ebilling-130c` (fast-forward).

- **F1, the findings cap is no longer silent.** `FirmRejectionFindings.Parse` now returns `ParseResult(Findings, NotShown)`; `NotShown` is the number of valid rejecting findings past `MaxFindings` (200, unchanged). `FirmOutcome` gains an optional last member `NotShownCount` (int?, default null, `JsonIgnore(WhenWritingNull)`, the same convention as `FirmOutcomeItem.LineNumber`), set by `ProjectRejectionAsync` when `NotShown > 0`. Malformed and refused entries are not counted in it: they are not findings this build could show (they stay in the DROPPED Warning). Every other outcome, and a rejection at or under the cap, serializes exactly as before. No client reads the outcome contract (grep of `src/**` for the routes, `FirmOutcome` and `rejectionFinding` found nothing), so there is no consumer to update; 131/133/134 will read `notShownCount`.
- **K1, AI-assigned fields.** A value whose stored `field` satisfies `BillingFieldNames.IsAiAssigned` (the coding Action's codes and every `Observation.*` field) is not projected: it is model output, not the firm's invoice. The firm-billed codes (`BilledTaskCode` and the others) are extracted fields and are still shown.
- **K2, a merge-fragile test.** `Rejected_ARealApprovedRateFinding_ProjectsOnlyTheValuesExtractedFromTheInvoice` no longer names the handler's reference values. Its precondition is generic: the real finding's `ValuesRead` has entries with no extracted field (and at least one is an identifier), none of those names appears in the output, and every projected name is an extracted one.
- **K4, wording.** `LineNumber` is the one-based position of the Nth line the submission read from the invoice, not a number printed on the invoice. Fixed in `FirmOutcomeItem`'s doc comment and in the "Rules as built" Line row above. (The contract's other XML docs and the endpoint had no matching wording.)
- **K5, the notes, and reachability proven through the real decision.** The fix-round-1 F1-b bullet is corrected in place. Credit-test reads (`WithCreditTestReads`, `HandlerContext.cs`) are created with null confidence, and `SubmissionRejectionRule.DecideFinding` holds any finding whose read field has unknown confidence (`read_field_confidence_unknown`), so they cannot appear on a rejecting finding. The new tests run `SubmissionRejectionRule.Decide` over findings from the handler kit instead of forcing `Rejects = true`, and store them with the persister's own `FindingEntryJson`:
  - an ApprovedRate finding on a fee line with neither hours nor amount lists both as null-valued reads; the decision holds it (`ReadFieldConfidenceUnknown`, outcome Accepted); stored beside a rejecting finding it is not projected;
  - a RequiredData missing-field finding (`firmInvoiceNumber` null, confidence known and at or above the threshold) **is reachable**: the decision rejects it (outcome Rejected) and the projection shows the field name with an empty value. The confidence comes from the facts' field-confidence map, as the kit supplies it.
- **K6, robustness.**
  1. The 200-character cut no longer splits a surrogate pair: if the last kept character is a high surrogate the cut steps back one character.
  2. A fault while reading one entry (for example a string whose escape cannot be decoded, which makes `GetString()` throw) drops and counts only that entry as malformed. The document stays readable and the other entries are shown. The fault is caught per entry in `Parse` (`InvalidOperationException`, `ArgumentException`, `FormatException`, `JsonException`); a value that fails makes its entry malformed, the same as a wrong-typed value.
  3. A document that cannot be parsed as a whole still takes the UNREADABLE path (status only, stable code, nothing shown).

Tests (`FirmFindingsProjectionTests`; the replaced `Rejected_FindingsOverTheCap_AreCountedNotShown` pinned the silent behaviour):

- must-fire: 250 rejecting findings give 200 shown, `NotShownCount` 50, `"notShownCount":50` in the body and "50 over the cap" in the Warning; an AI-assigned value is not projected; a 201-character value ending in an emoji truncates cleanly (and serializes to valid UTF-8); one undecodable entry among good ones drops only that entry;
- must-not-fire: 1, 199 and 200 findings, and every other outcome, have no `notShownCount`; only valid rejecting findings past the cap count; an emoji inside the cut is kept; a whole-document parse fault is still UNREADABLE with no DROPPED Warning;
- reachability (K5): the two tests above.

### Results (130c fix round 2)

- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 errors, 0 warnings.
- `FirmFindingsProjectionTests`: 114 passed, 0 failed (102 before; 12 net new).
- Filtered unit run (`FirmOutcome`, `FirmFindingsProjection`, `Billing`, `SubmissionRejectionRule`, `FirmRejection`): 2403 passed, 0 failed.
- Fail-before:
  - the two production files put back to `adb765caa3`: the test project does not compile (`FirmRejectionFindings.ParseResult` and `FirmOutcome.NotShownCount` do not exist);
  - the new behaviours mutated (NotShownCount always null, AI check removed, surrogate step-back removed, per-entry catch removed): 5 of 114 fail (the 250-finding cap test, the cap-count test, the AI-assigned test, the emoji test, the undecodable-entry test). The K5 reachability tests and the K2 rewrite pass before and after by design: they pin existing behaviour. Both production files were restored from copies; their SHA-256 matched (`CE96D5AB...D6D0EF7`, `AF2F136C...A985CD`).
- ArchTests (full): 2183 total, 2183 passed, 0 failed (run from the worktree's own path).

### 130c fix round 3

Branch `task/ebilling-130c-f3` from `task/ebilling-130c` at `9a84a1f346`, pushed to `task/ebilling-130c` (fast-forward). A hardening round; no new service, package or registration.

- **G, a catalogue-driven guard on the value boundary.** The projection shows a value only when its stored entry has a non-empty `field` (extracted from the firm's own invoice) that the AI did not assign. That holds because each handler leaves `field` off its reference values (approved rate, rate card, core-record dates and status, prior-invoice data). `StoredFindingValueBoundaryTests` (`tests/unit/domain/Signals/Billing/Handlers`, beside `BillingHandlerKit`) now guards it. It iterates `BillingRuleCatalogue.RejectCapableReasons` over every Billing type, and for each (type, reason) runs the REAL handler through `BillingHandlerKit.Run` with reference data present and set up so the handler fires (rate card below the billed rate; core-record window dates, a closed record and a closed-to-billing status; a prior invoice with the same number; an aged invoice date; an expense code and an expense over the cap; missing required fields). Each finding is stored through `InvoiceSubmissionPersister.FindingEntryJson` and the stored shape is checked:
  - every value entry with a non-empty `field` names a `BillingFieldNames` extracted-field constant (the constants are found by reflection: not the observation prefix, not AI-assigned);
  - every reference value (no extracted field on the finding) is stored with no `field`, and the reference names each fixture lists (for example `approvedRate`, `rateCard.id`, `coreRecord.openDate`, `duplicateOf.trackingNumber`) are present and stored with no `field`, so a handler that attaches an extracted field to one fails even if it flags it consistently;
  - the reason's finding is produced (the test cannot pass vacuously), and the projection's own parse of the stored document shows no reference value (a Duplicate shows none at all).
  A dictionary of fixtures keyed by (type, reason) must equal the catalogue's reject-capable set, so a new reject-capable reason without a fixture fails, and the theory has one case per catalogue entry. All ten reasons (ApprovedRate 1, ExpenseRule 2, InvoiceAge 1, CoreRecordWindow 4, Duplicate 1, RequiredData 1) were driven to a finding through the kit; none was left out.
- **K-a, the AI-assigned skip ignores case.** `FirmRejectionFindings` uses a local `IsAiAssignedField` (the trimmed name, `OrdinalIgnoreCase`, same set and the same `Observation.` prefix as `BillingFieldNames.IsAiAssigned`). `BillingFieldNames` and `SubmissionRejectionRule` are unchanged. Before: `taskcode` and `observation.x` were projected. After: both are skipped; `observationx` (no dot) and `BilledTaskCode` are still projected.
- **K-b, a list is not presented as complete when rejecting entries were dropped.** `ParseResult.NotShown` and `FirmOutcome.NotShownCount` now count rejecting findings that could not be shown: those past the cap (as before) plus entries with `rejects` true that were dropped as malformed, undecodable, of an unknown or refused rule type, or of a type or reason that cannot reject. Entries with `rejects` false or absent, a `rejects` that is not a boolean, entries that are not objects, and every entry of a non-AutoRejected document (a Blocked hold) are not counted. A rejected submission where every rejecting entry was dropped now returns no items with `NotShownCount` set (previously it looked like a rejection with nothing to say). The XML doc on `NotShownCount` reads "rejecting findings that could not be shown". The DROPPED Warning is unchanged (codes and counts only).
- **K-c.** In the K5 test whose shown rejecting entry is invoice-level, the vacuous `LineNumber.Should().NotBe(3)` is now `LineNumber.Should().BeNull()`.

Tests (`FirmFindingsProjectionTests`, 114 to 119; `StoredFindingValueBoundaryTests`, 12 new):

- must-fire: casing variants of AI-assigned fields are skipped; 3 valid and 1 malformed rejecting entry give 3 shown and `NotShownCount` 1; every kind of dropped rejecting entry is counted; all-malformed gives no items and `NotShownCount` N; the catalogue guard, one case per reject-capable reason;
- must-not-fire: `observationx` and `BilledTaskCode` are projected; held, non-rejecting, non-boolean-flag, non-object and Blocked-document entries are not counted;
- three existing tests gained the matching `NotShownCount` assertion (malformed 9, unknown-type 6, undecodable 1).

### Results (130c fix round 3)

- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 errors, 0 warnings.
- Filtered unit run (`FirmOutcome`, `FirmFindingsProjection`, `FirmRejection`, `Billing`, `SubmissionRejectionRule`, `StoredFindingValueBoundary`): 2420 passed, 0 failed (2403 before; 17 new).
- Fail-before:
  - K-a and K-b mutated off (the AI check back to the ordinal `BillingFieldNames.IsAiAssigned`, the rejecting-entry count removed, the empty-list outcome without the count): 7 of 119 `FirmFindingsProjectionTests` fail (the K-a test, the three new K-b must-fire tests, and the three existing tests that gained the count assertion); the must-not-fire test passes. Both production files were restored from copies and their SHA-256 matched (`78900960...232872`, `1dd83fce...cc4f233`).
  - G: one reference value in `ApprovedRateHandler` temporarily given an extracted field: the ApprovedRate case fails ("approvedRate is reference data"). One fixture key (Duplicate) removed: the Duplicate case and the fixtures-equal-the-catalogue test fail. The handler (SHA-256 `6941eb94...2106ca`) and the test file (`942d6c86...cfa8`) were restored and verified.
- ArchTests (full, from the worktree's own path): 2183 total, 2183 passed, 0 failed.

### 130c fix round 4

Branch `task/ebilling-130c-f4` from `task/ebilling-130c` at `a357a5c7e1`, pushed to `task/ebilling-130c` (fast-forward). A hardening round; no new service, package or registration, and `BillingFieldNames` is unchanged.

- **The value filter is an allow-list.** Before: a value with a non-empty `field` was shown unless the field was AI-assigned, so a field name the projection did not recognise was shown. After: a value is shown only when its trimmed `field` equals, `OrdinalIgnoreCase`, a `BillingFieldNames` constant that names a field of the firm's invoice (not `ObservationPrefix`, not AI-assigned). The AI-assigned skip stays as well (defence in depth). The empty-value rule is unchanged: an allowed field with no text shows with an empty value.
- **Where the allowed set comes from.** `BillingFieldNames` exposes no set of extracted names, so `FirmRejectionFindings` builds one static read-only set once from the `public const string` fields of `BillingFieldNames` (reflection, the approach `StoredFindingValueBoundaryTests` already uses), minus the observation prefix and the AI-assigned names. A constant added to `BillingFieldNames` is allowed with no second list to keep in step. `StoredFindingValueBoundaryTests` keeps its own independent derivation and is unchanged.
- **Tests (`FirmFindingsProjectionTests`, 119 to 121).**
  - must-fire: an entry whose values carry fields `line.internalScore`, a truncated `Rat` and the bare observation prefix still shows (its line number), while those values do not; the allowed `Rate` value beside them does.
  - must-not-fire: every extracted-field constant, as named, upper-cased and lower-cased with padding, still shows its value.
  - The fix round 3 K-a test now also expects a name that only resembles an AI-assigned one (`observationx`) not to show, because it is not an extracted field.

### Results (130c fix round 4)

- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 errors, 0 warnings.
- `FirmFindingsProjectionTests`: 121 passed, 0 failed (119 before; 2 new).
- Filtered unit run (`FirmOutcome`, `FirmFindingsProjection`, `FirmRejection`, `Billing`, `SubmissionRejectionRule`, `StoredFindingValueBoundary`): 2422 passed, 0 failed (2420 before; 2 new). `StoredFindingValueBoundaryTests` is unchanged.
- Fail-before: with the allow-list mutated off (the check reduced to the AI-assigned skip alone; a non-constant mutation), 2 of 121 `FirmFindingsProjectionTests` fail (the new must-fire test and the updated K-a test); the must-not-fire test passes before and after by design. The production file was restored from a copy; its SHA-256 matched (`d56b2f11368e1d69...`).
- ArchTests (full, from the worktree's own path): 2183 total, 2183 passed, 0 failed.
