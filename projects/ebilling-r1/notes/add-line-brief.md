# Add line brief (EB-D-188)

2026-11-05, base `c4d6c3b07c`. Task `tasks/164-add-line.poml` (160 is taken). Paths are under `src/server/api/Sprk.Bff.Api/`.

Short names, all under `Services/Finance/`: LDS `InvoiceLineDecisionService.cs`, ADJ `InvoiceAdjustmentService.cs`, CRE `InvoiceCorrectionReevaluation.cs`, TC `InvoiceTotalsCalculator.cs`. FG is `InvoiceFirmGate` (branch `task/ebilling-095d0`, not merged). uac A1-A5 and ont O1-O3 are the conditions relayed on 2026-11-05.

## 1. Goal

A reviewer adds a line the firm did not bill, or fixes another error, through **Add line** on Invoice Review: one BFF route, In Review with a firm only. The line's rules, code checks and the invoice-level total rules run; the app writes the line, reviewer-added and Flagged, outside the billed total; one Decision Record names the reviewer and the reason. A script hides "New Billing Event" (live run: separate owner go).

## 2. Requirements (closed set)

| Id | Requirement | Source |
|---|---|---|
| R1 | `POST /api/finance/invoices/{invoiceId}/lines` in the `/api/finance` group (step 0, internal only), with `AddFinanceAuthorizationFilter("ebilling.invoice.decide")` (Write on the INVOICE, asked as the caller) and `RequireEbillingEnabled` | `InvoiceLineReviewEndpoints.cs:42-68`; D-77; uac A2, A4 |
| R2 | The app writes the line (roles have no Create). The decider is the caller's `systemuserid` from their token | EB-D-188; D-90; ont O1 |
| R3 | 409 when not In Review (any closed status) and for NoFirm, HeldForFirm and RerunPending, before any write, SignalWriter or recorder call | EB-D-185; FG; uac A3 |
| R4 | Inputs: `requestId`, cost type, date, timekeeper (Fee), hours+rate or amount, codes, description, and a **required reason** (at most 2000 characters) | EB-D-188, EB-D-176; uac A5 |
| R5 | Codes only from the resolved set (out-of-set refused). The Fee timekeeper and rate are resolved as a timekeeper correction does | FR-22; `InvoiceCorrectionService.cs:568-660` |
| R6 | The line's rules run, plus the invoice-level rules over totals or several lines (AddedFee, Discount, DailyHoursCap, Duplicate), through 101's whole-invoice before/after diff (no per-line evaluator exists) | ont O2; CRE:456-560 |
| R7 | Signals via `SignalWriter` (D-13 key). Changed reason set → Superseded (no record) + n+1; unchanged → refresh `sprk_lastevaluated` only | ont O2 |
| R8 | One record, only via `BillingDecisionRecorder` → `WriteBillingDecisionAsync`, after the line write: the caller, the reason, empty `ResolvedSignalIds`, facts = ids and codes | D-100; ont O3; uac A5 |
| R9 | Only a hold whose LAST reason this cleared closes ConditionCleared, linked by `LinkConditionClearedRecordAsync` (`resolvedBy` = the caller) | ont O3 |
| R10 | Reviewer-added marker (Q2); Flagged with the line finding `line.reviewer_added` | EB-D-188 |
| R11 | I-2 counts it like any line; Accept all Clean skips it | `InvoiceApprovalService.cs:582` |
| R12 | The billed total never changes; the approved total follows Q1 | TC:85-103 |
| R13 | Line and decision ids are derived from (invoice, `requestId`). A repeat → `alreadyApplied`; other content → 409 | ADJ:136, :243 |
| R14 | `LockKeyFor`, plus a commit conditioned on the submission AND invoice row versions | LDS:150; CRE:394; `InvoiceAssignFirmService.cs:1011` |
| R15 | No hold is released; Blocked entries and the override stamp are kept. A new Procedure gap → 409 | CRE:91, :515 |
| R16 | 409 while a correction is unapplied | LDS:722 |
| R17 | Route-ledger entry; uniform 403/404; `errorCode` shape | `RouteAuthorizationGuardTests.Ledger.cs:394`; uac A1, A3 |
| R18 | Hide New (homepage grid, subgrid AddNewStandard, form New and Save & New); the script has `-Apply` and `-Verify` | `Deploy-EbillingBillingExportRibbon.ps1`; ribbon-edit |
| R19 | Fluent v9 `FormModal` (ADR-050); refusals show in the form | `InvoiceReview/src/components/Dialogs.tsx:1-11` |

## 3. Design

**`InvoiceLineAddService`** (scoped, in the request: a 101 correction's size, so no job) composes ADJ's row write, CRE's diff, LDS's lock and record order, and 016. Route `AddInvoiceLine` in `InvoiceLineReviewEndpoints`: 200 `{requestId, lineId, decisionId, status, alreadyApplied, totals}`; 400; uniform 403/404; 409; 500 `owner_unresolved`; 503 `unavailable` or `incomplete` + `failedStep`. New codes `ebilling.line_add.{firm_missing, invalid, amount_mismatch, code_not_allowed, code_set_unavailable, procedure_gap, id_reused, changed}`; the rest reuse existing `ebilling.*` codes.

**Flow.** Steps 0-5 write nothing.

0. **400 before any read:** reason and description required; a Fee has hours+rate or an amount (all three: `round(h*r,2)` = amount); an Expense has an amount; non-zero, in range; codes fit the cost type.
1. **Lock (busy → 409), then app-only reads:** the invoice and its version, the lines, the newest submission and its version, the derived-id row. Missing → uniform 404; not In Review → 409; no single core → refused; FG not Open → 409 `firm_missing` (fault → 503); an unapplied correction → 409. An existing row with the same content → step 7; other content → `id_reused`.
2. **Resolve:** codes via `CodeSetResolver.ResolveAsync(core)`; the Fee timekeeper via the one-line `IInvoiceResolutionService` input (`RederiveAsync`; unmatched → flagged by the rules). Build the row as ADJ:546-571 does (core and resolver fields, firm, currency, sequence = max over ALL lines + 1, `LineNameFor`, Flagged, extraction `provenance{source:"reviewer", decisionId}`). Then `ResolveOwnerAsync(ForChild(row))` + `ApplyTo`, `RecordCreatorPerson.Stamp`, `InvoiceFamilyCoreRecordGuard.Check`.
3. **Evaluate:** before = stored rows; after = before + the line (CRE's `RequestBasis`, extracted; the correction suites pass unchanged). Load once, run the pure `Evaluate` twice. New gap → 409. CRE's `PatchFindings`/`PatchPlan` (decided lines and Blocked entries kept) plus `line.reviewer_added` (and `rules.not_checked`). No 073.
4. **Checks:** dry-run TC with Q1 (refused → 409); the `BillingDecision` (`add-line`, subject = the line, `Human(caller, "invoice-reviewer")`, the reason, facts = ids, cost type, codes, timekeeper id, rule reason codes); `Preflight`.
5. **R9:** if `InvoiceOpenHolds` goes from non-empty to empty, plan the link and ask FR-51 as the caller now. Unreachable in R1 (Q7).
6. **Commit, one transaction:** create the line; update the submission findings (`IfRowVersionMatches`) with the patch and `addLine{decisionId, signals[], state}`; a version-only conditional update of the invoice. A conflict → 409 `changed`, nothing written; another fault → 503, and a repeat is safe. A test proves Dataverse honours the version-only guard; if it does not, STOP.
7. **Totals:** `RecalculateAsync`, then the index enqueue (LDS:653).
8. **Record:** `RecordAsync`, idempotent by decision id.
9. **Signals:** each pending entry goes through `SignalEpisodeLifecycle` (new → episode 1; same fingerprint → refresh; different → Superseded + n+1). A group that is gone → ConditionCleared, record-free, unless step 5 linked it. Then clear `addLine.signals` (conditional). A failure → 503 `signals`, and the repeat finishes it.

From step 6 on, `CancellationToken.None` (ADJ:576). Every later step is idempotent.

## 4. Open questions (record each answer in DL before 164a)

- **Q1 (owner): totals.** TC's approved = billed − adjustments, so an Accepted added line pays nothing and a Disallowed one subtracts money that was never billed. **Recommend:**
  - approved = billed + Σ added contributions − firm-line adjustments − invoice adjustments;
  - the contribution is the amount (undecided or Accepted), the approved amount (Reduced), or 0 (Disallowed);
  - outside billed, fees, expenses and the percent base.
  This changes I-1; tell ontology-r1 (spend, D-130).
- **Q2 (owner, §11): the marker.** No typed column fits (`sprk_createdbyperson` is on intake lines too; `sprk_visibilitystate` is spend state; `sprk_reviewedby` means decided). **Recommend:** `provenance.source = "reviewer"` in the existing `sprk_extractionconfidence` (EB-D-170's home; a flag and an id meet uac's 095d-2a condition 1). A typed column → escalate, never in 164a.
- **Q3 (ontology-r1): the code.** None of the nine fits (`DecisionActionCatalog.cs:379-449`). **Recommend:** `add-line` (required reason, Tier2b, Judgement, Reversible, RecordOfTruthImpact), subject = the line (D-75): add it to the writer's line codes (`Services/Signals/DecisionRecordWriter.cs:1256`, ontology code) and `Billing_C2`. FR-30 gains Add line. **ANSWERED (ontology): OK**, with conditions: same-commit `Billing_C2` update; BillingActions only (commit route refuses it, tested); idempotent on the decision id (one line, one record on retry); excludes empty.
- **Q4 (owner): AI coding.** **Recommend:** none. The reviewer types the codes; FR-22 and the rules check them. That keeps the service out of `Services/Ai` (bff-extensions A4).
- **Q5 (R4 + owner): partners.** Firms read no lines today (no `sprk_billingevent` module in `ExternalAccessModule`; 130/134 open). **Recommend:** nothing while In Review (FR-38); once decided, 130 lists it as its own item (date, description excerpt, approved amount, fixed label "Added by the client"); the reason stays internal. 164 adds no external surface.
- **Q6 (uac-r2): other create paths.** Whether `sprk_billingevent` should stay in scope of the generic child-record route and uac 147's SecureChild "New Billing Event" ribbon (not deployed). Sent to uac-r2. **Recommend:** remove the table from both, so Add line is the one server-side owner of a new line (ADR-002). Until then, 164c's dry run refuses.
- **Q7 (ontology-r1). ANSWERED:** no link code in R1 (code nothing calls proves nothing). Add line cannot clear the last hold reason (EB-D-185/190; only Override or the re-run releases a Procedure hold). Must-not-fire test instead: never `LinkConditionClearedRecordAsync`, never ConditionCleared on a held invoice. Trigger: if a later change gives Add line a hold-clearing path, O3 applies then.

## 5. Files

| File | Change | Copy |
|---|---|---|
| `Services/Finance/InvoiceLineAddService.cs` | new | ADJ `ApplyAddAsync` :451-582; LDS :274-318, :617-704 |
| `Api/Finance/InvoiceLineReviewEndpoints.cs` | route, `AddLineRequest`, mapping | `DecideLine` :143, `ToResult` :215 |
| CRE | extract `RequestBasis`, `PatchFindings`, `PatchPlan`, `SubjectsOf` | — |
| TC, `InvoiceTotalsService.cs` | Q1: `InvoiceLineInput.ReviewerAdded` | TC:114-200 |
| `Submission/ExtractionProvenance.cs`, `InvoiceReviewReadService.cs` | `source`, `IsReviewerAdded`; `ReviewLineDto.ReviewerAdded` | — |
| `Signals/Actions/DecisionActionCatalog.cs`, `Signals/DecisionRecordWriter.cs:1256` | Q3 | `assign-firm` :434 |
| `SignalEpisodeLifecycle.cs`, `FinanceModule.cs:151`, `Dataverse/ParentLineage.cs` | `SignalLogContext.AddLine`; `AddScoped`; the findings writer text | 095d-2 R18 |
| `src/solutions/InvoiceReview/src/` `Dialogs.tsx`, `InvoiceReviewPage.tsx`, `LinesGrid.tsx`, `invoiceReviewApi.ts`, `reviewModel.ts` | `AddLineModal`, button, badge, `addLine` | `CorrectValueModal` :70; `runWrite` :399 |
| `scripts/ebilling/Deploy-EbillingBillingEventRibbon.ps1`, `infrastructure/dataverse/ribbon/BillingEventRibbons/`, `sprk_BillingEvent/RibbonDiff.xml` | new / hides | the BillingExport script and shell |

Contract tests copy `tests/integration/contract/Api/Finance/InvoiceLineReviewEndpointsAuthorizationContractTests.cs`. The persister is NOT edited (082d and 095d-2a overlap).

## 6. §11 (existing / extension / cost of doing nothing)

- **Service:** ADJ, LDS, CRE, persister; none adds a line after intake, and extending one gives it a second invariant (ADR-002); EB-D-188 fails.
- **Route:** same file; no write path.
- **`add-line`:** no code means "added"; wrong or no record.
- **`provenance.source`, `ReviewerAdded`:** extend a member and a DTO; the line cannot be told apart.
- **Ribbon shell, script:** one per table; admins create unchecked lines.
- **`AddLineModal`:** `CorrectValueModal` edits one value; no UI.

## 7. ADRs and constraints

ADR-001, 002, 008, 010, 015, 018, 019, 024, 028 (`constraints/auth.md`), 034, 038, 050, 052. `bff-extensions.md` §A (the BFF is the invariant owner's write path; no package; publish delta against a fresh master build). `DATAVERSE-WRITE-PATH-ARCHITECTURE.md`, `MODAL-DECISION-CRITERIA.md`, ribbon-edit.

## 8. Test scope (closed)

**Must-fire**
- T1 Fee line: the row, `sprk_createdbyperson` = the caller, Flagged, the marker, Q1 totals, one `add-line` record (the line, the reason, the caller, empty resolved set, ids and codes). T2 the same for an Expense line.
- T3 a rule on the line → a Signal keyed `…|sprk_billingevent|{lineId}|1`.
- T4 a changed invoice-level set → Superseded with no record, then n+1. *Must-not-fire:* an unchanged set only refreshes `sprk_lastevaluated`.
- T5 a cleared non-hold group → ConditionCleared, no link.
- T6 Q7 must-not-fire: no hold link and no ConditionCleared on a held invoice; Blocked entries and the override stamp stay byte-identical. T6b Q3: the commit route refuses `add-line`; a retry gives one line and one record. T6c a reviewer-typed invalid code raises the same finding as an imported one.
- T7 I-2 refuses approve until accept-line; Accept all Clean skips the line.
- T8 Q1 for accept, reduce and disallow; billed, fees and expenses unchanged.

**Negatives**
- N1 no Write → 403, nothing read or written (a right on another invoice does not count; a failed check is denied).
- N2 `FlagTrue`, `NoSystemUserRow` and the `CiamContact` plane are refused before any read.
- N3 the three EB-D-185 states → 409, with no recorder or SignalWriter call (strict mocks).
- N4 the six closed statuses → 409.
- N5 a missing or too-long reason → 400 before any read.
- N6 the other 400s and `code_set_unavailable`.
- N7 a repeat → `alreadyApplied` (one row, one record, no new episode); other content → `id_reused`.
- N8 a changed submission or invoice version → 409, nothing written; a held lock → busy.
- N9 a new gap or an unapplied correction → 409.
- N10 a fault at totals, record or signals → 503 naming the step; the repeat leaves one record.
- N11 an unresolved owner → 500.

**Seeds** (access and fail-open only): S1 remove the filter → N1 fails; S2 remove the gate → N3 fails; S3 drop the version conditions → N8 fails; S4 remove `RequireInternalFinanceCaller` from a test group → N2 fails.

**Guards:** G1 ledger; G2 `FindingsJsonAppOnlyReadGuardTests` and a notes/122 row; G3 `RecordOwnerAssignmentCensusTests`; G4 `DecisionActionCatalogTests` and `DecisionRecordWriterTests.Billing` C2; G5 the correction suites pass unchanged.

**Client:** C1 one `requestId` across Repeat; C2 a refusal shows in the form; C3 the button is hidden without decide rights or outside In Review; C4 the badge.

**Script:** P1 the dry run lists the hides and refuses while non-live `sprk.SecureChild.*` ids are in the diff; P2 `-Verify` exits 0 or 1.

## 9. Known limits

K1 no AI provenance, so correcting an added line's extracted fields → 409 (disallow and re-add). K2 budget Signals keep their own cycle. K3 the AP export has no "source" column. K4 no timekeeper or code pickers (the server checks). K5 "Add Existing" stays (it fails for person roles). K6 the O3 link is unreachable in R1.

## 10. Rigor and model

FULL. 164a: opus, effort high, two adversarial passes, uac-r2's route review, and ontology on R7-R9. 164b and 164c: sonnet, effort high, STANDARD.

## 11. Split

- **164a BFF.** Gate: Q1-Q4 answered and 095d-0 merged.
- **164b Client**, after 164a.
- **164c Ribbon**, independent; Q6 is needed before `-Apply`.
