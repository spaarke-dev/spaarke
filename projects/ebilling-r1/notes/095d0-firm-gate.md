# 095d-0: no invoice decision without a firm (EB-D-185, with EB-D-190 and EB-D-197)

Branch `task/ebilling-095d0` (base `origin/work/ebilling-r1` `438c311867`). Owner rule EB-D-185 (2026-11-05; first written as "D-148" in this slice's code and notes by mistake: EB-D-148 is the Procedure and file model), with EB-D-190 (whole-invoice Reject is gated) and EB-D-197 (adjustments and corrections are gated): "There always needs to be a firm: we have to know what billing guidelines apply, who to pay, etc."

## 1. The rule

While an invoice

- (a) has no firm (`sprk_invoice.sprk_vendororg` empty),
- (b) is held for its firm (`InvoiceFirmHold`: the invoice's own `sprk_firmhold`, or a linked submission storing `firm_missing`; the same read that backs approve's 409), or
- (c) has the Set-firm re-run pending: `sprk_invoice.sprk_firmhold` = `ebilling.submission.rerun_pending` (the marker Set firm writes, section 8), or a linked submission whose stored `assignFirm` member has `rulesRerun` other than `completed`,

these decisions are refused with 409 and nothing is written.

## 2. Surfaces

| Surface | Gated | Where the gate sits | Code |
|---|---|---|---|
| Line decisions: `POST .../invoices/{id}/lines/{lineId}/decision`, `POST .../lines/decisions`, and Accept all Clean (`POST .../lines/accept-clean`, the same service path) | yes | `InvoiceLineDecisionService.ApplyAsync`, after `AuthorizeCloseAsync` (the rights, FR-51), before the Decision Record preflight and every write | `ebilling.line_decision.firm_missing` |
| Override (`SubmissionOverrideService`, Override verb only): every kind, the ordinary blocked-submission release and the 114 Procedure-hold release | yes | `DecideLockedAsync`, after `AuthorizeCloseAsync` (114b order: rights, then the gate), before the holds derivation, the Signal set's store (`BulkUpdateIfRowVersionsMatchAsync`), the Decision Record and the closes | `ebilling.override.firm_missing` |
| Approve | (a) and (b) already refused; (c) added | `InvoiceApprovalService`, the existing 095c-4 firm check now asks `InvoiceFirmGate` | `ebilling.approve.firm_missing` (kept) |
| Return (submission Return and invoice Return to Firm) | no (R1: Return to Firm uses `InvoiceFirmGate.CheckForReturnAsync`, so it passes after Set firm) | Return to Firm keeps its own 095c-4 firm check (it needs a firm to return the invoice to); after Set firm the firm exists and the rerun-pending marker no longer blocks it | - |
| Set firm | no | it is the way out | - |
| Adjustments (`InvoiceAdjustmentService.ApplyAddAsync` and `ApplyRemoveAsync`; rule-less and rule-cited) | **yes (EB-D-197)** | after the 403 (the route filter) and the InReview check, before any write | `ebilling.adjustment.firm_missing` (`AdjustmentStatus.FirmMissing`, 409; the family's existing code), sentences `AdjustmentCodes.AdjustFirmDetailFor(gate)` |
| Corrections (`InvoiceCorrectionService.CorrectLineAsync` and `CorrectHeaderAsync`) | **yes (EB-D-197)**; now asks `InvoiceFirmGate.CheckAsync` (was `InvoiceFirmHold`: no firm / held only), so the re-run pending is refused too | after the 403, before any write | `ebilling.correction.firm_missing` (existing), sentences `InvoiceCorrectionService.FirmDetailFor(InvoiceFirmGateResult)` |
| Reject (whole invoice; `InvoiceRejectReturnService.RejectAsync`, the only human Reject path) | **yes (scope change EB-D-190, owner)** | after the 403s (eligible approver, claim) and the FR-20 hold, before the Signals read, the claim and every write | `ebilling.invoice.firm_missing` (the route family's existing firm code, which Return to Firm already emits; the approve code is approve-specific) |

The route-level authorization filters (`ebilling.invoice.decide`, the Override privilege) run before the service, so a 403 from them always comes first. Inside the services the 403s (`NotAuthorized`, `NotPermitted`) also come before the 409.

**Reject (EB-D-190).** The first cut of this slice left Reject available (it applies no guideline and pays no one). The owner reversed that: Reject is gated exactly like approve, so only Return and Set firm stay available. Return to Firm keeps its own 095c-4 check (it needs a firm to return the invoice to), so with no firm the reviewer's only way out is Set firm. The pipeline's automatic rejection at intake (`AutoRejected`, no human decision) is not a Reject decision and is not gated.

Reject sentences (`InvoiceRejectReturnCodes.RejectFirmDetailFor`): no firm "This invoice has no firm. A reviewer must add the firm before the invoice can be rejected."; held "This invoice is waiting for its firm to be assigned. It can be rejected once the firm is assigned."; re-run pending, the shared re-run sentence. Return to Firm's two sentences no longer say "or reject the invoice" (that advice is now wrong).

**UI (Invoice Review decision wizard).** The approval state's existing `firmNeeded` flag now follows the gate (so `canApprove` is also false while the re-run is pending, which closes the gap a first cut noted). The wizard's Outcome step disables Reject, and says why for Approve and Reject, when `firmNeeded` is true (`decisionModel.firmNeededBlocker`, `steps.tsx`, `InvoiceDecisionWizard` `canAdvance`). Return to firm is left to the server's own check. No new response field: `firmNeeded` was already in the DTO; the client type gains it as optional.

## 3. The helper

`Services/Finance/Submission/InvoiceFirmGate.cs`: `CheckAsync(IGenericEntityService, Entity invoice, ct)` returns `InvoiceFirmGateResult` { `Open`, `NoFirm`, `HeldForFirm`, `RerunPending` }.

- Order (fix round R2/R3): `HasNoFirm` gives `NoFirm`; then the invoice's own `sprk_firmhold` (`InvoiceFirmHold.InvoiceHoldStateAsync`): exactly the marker is `RerunPending` (wins even with no linked submission), any other non-empty value is `HeldForFirm` (fail closed), empty continues; then ONE app-only read of the linked submissions: unreadable findings `HeldForFirm`, a pending `assignFirm` re-run `RerunPending`, a stored `firm_missing` hold `HeldForFirm`, else `Open`. The pending re-run is checked before the held test, so a reviewer who just set the firm sees the re-run sentence.
- `InvoiceAssignFirmService.RulesRerunCompleted = "completed"` was added beside `RulesRerunPending`. 095d-2 writes it.
- **Fail closed.** A read fault throws; every caller answers 503 / refuses with nothing written (line decisions: `Unavailable`; Override: `Unavailable`; approve: its existing `read` failure). An `assignFirm` member that is present but cannot be read (`FormatException`, JSON errors) is `RerunPending`. Findings that cannot be parsed at all are `HeldForFirm` (that is `InvoiceFirmHold`'s existing rule, applied first).
- **Legacy rows.** A submission with blank findings, or findings with no `assignFirm` member (an invoice whose firm came with the intake, or a row from before Set firm), carries no re-run and is `Open`. The only legacy row the gate can newly block is one whose findings JSON is unparseable or whose `assignFirm` member is malformed; the first was already refused by approve (`InvoiceFirmHold.IsHeld` fails closed), the second cannot exist before 095d-1b wrote it.
- **App-only and `sprk_findingsjson`.** It reads the column through the `IGenericEntityService` its callers pass (app identity), never as the caller (the column is field-secured, task 122). It is added to the reviewed readers of `FindingsJsonAppOnlyReadGuardTests` (C1/C2/C3) and to the task 122 inventory (row 13). It does not name `sprk_firmhold` itself (it calls `InvoiceFirmHold`), so `InvoiceFirmHoldAppOnlyGuardTests` is unchanged.
- The line-decision service now reads `sprk_vendororg` with the invoice (`InvoiceColumns`); the Override service's invoice read already had it.

## 4. Codes and sentences (copy of the 409 shape of each route; no new response fields)

| Surface | Code | NoFirm | HeldForFirm | RerunPending |
|---|---|---|---|---|
| Line decisions | `ebilling.line_decision.firm_missing` (`LineDecisionCodes.FirmMissing`, status `LineDecisionStatus.FirmRequired` -> 409 with `errorCode` and `detail`, like `NotInReview`) | "This invoice has no firm. A reviewer must add the firm before its lines can be decided." | "This invoice is waiting for its firm to be assigned. Its lines can be decided once the firm is assigned." | "This invoice's firm was just assigned and its billing rules are being checked again. Try again when the check finishes." |
| Override | `ebilling.override.firm_missing` (`BlockedSubmissionErrors.FirmMissing`, `BlockedDecisionResult.Refuse` 409, like `InvoiceNotHeld`) | "This invoice has no firm. A reviewer must add the firm before the submission can be overridden." | "This invoice is waiting for its firm to be assigned. The submission can be overridden once the firm is assigned." | the same re-run sentence |
| Approve | `ebilling.approve.firm_missing` (kept) | `FirmMissingDetail` (unchanged) | `FirmHeldDetail` (unchanged) | `InvoiceApprovalCodes.FirmRerunPendingDetail` (the same re-run sentence) |

`FirmDetailFor(InvoiceFirmGateResult)` is added beside the existing `FirmDetailFor(InvoiceFirmNeed)` on `InvoiceApprovalCodes`, and the same-named method on `LineDecisionCodes` and `BlockedSubmissionErrors`. The approval's bulk-accept path maps `LineDecisionStatus.FirmRequired` to its `Conflict` outcome (it cannot happen there: approve has just passed the same gate, but the switch is closed).

## 5. CLAUDE.md §11 justification

**`InvoiceFirmGate`**
1. *Existing:* `InvoiceFirmHold` / `InvoiceFirmNeed` answer "no firm" and "held" (approve, export, Return to Firm, correction, adjustment rule options); `AssignFirmStoredDecision.Read` parses the stored Set-firm decision. Nothing answers "is the re-run still pending", and line decisions and Override ask nothing about a firm.
2. *Extension:* `InvoiceFirmHold`'s contract is "why the invoice needs a firm" with seven callers; widening it to the re-run would change what Return to Firm, export and correction refuse. So the gate composes the two readers, adds the one new fact, and duplicates neither.
3. *Cost of doing nothing:* a reviewer can accept, reduce or disallow lines, or Override a blocked submission, on an invoice whose billing guidelines and payee are not yet known (the owner's EB-D-185 reason); a decision made before the re-run is lost or contradicted by it.

**The two new codes (`ebilling.line_decision.firm_missing`, `ebilling.override.firm_missing`) and the status `LineDecisionStatus.FirmRequired`**
1. *Existing:* `ebilling.approve.firm_missing`, `ebilling.invoice.not_in_review`, `ebilling.blocked.invoice_not_held`.
2. *Extension:* reusing the approve code on the line or Override routes would make a client that branches on codes show "approve" wording on a lines screen, and the existing sentences say "before it can be approved". One code per surface is the family's pattern (`ebilling.export.firm_missing`, `ebilling.adjustment.firm_missing`).
3. *Cost of doing nothing:* the client cannot tell a firm refusal on a line decision or an Override from the other 409s of those routes (a changed line, a busy lock), so it cannot send the reviewer to Set firm.

No endpoint, DI registration, package, project file or background work was added; the BFF hygiene obligations (§10) reduce to the placement (existing `Services/Finance` files) and the unit tests below. The change adds two small reads (a gate on three routes), no publish-size-relevant surface.

## 6. Tests (closed set)

New files: `InvoiceFirmGateTests.cs`, `InvoiceLineDecisionServiceTests.FirmGate.cs`, `SubmissionOverrideServiceTests.FirmGate.cs` (partial classes of the existing test classes, so they use the existing in-memory worlds), and additions to `InvoiceApprovalFirmHoldTests.cs`.

- **Must-fire, per surface** (line decision single / several / Accept all Clean; Override ordinary / Procedure hold Internal / Procedure hold SPA; approve): NoFirm, HeldForFirm (the invoice's own column; a submission's stored hold), RerunPending (`pending`, another word, an unreadable `assignFirm` member) each give 409 with the code and sentence, nothing written, no Decision Record, no Signal close. Line decisions assert no line write, no totals, no correction rows, no record, no close, the lines' statuses unchanged, no re-index queued. Override asserts no record, no close, **nothing in the stored findings (no Signal set)**, no release transaction, no review-detail write, every Signal still Open and unlinked (ontology-r1 condition 2: the 409 comes before the set store). A fault reading the gate: 503, nothing written (line decisions, Override).
- **Must-not-fire:** firm and `rulesRerun` = `completed` proceeds (line decisions x3 routes, Override ordinary and Procedure hold, approve); a firm with no `assignFirm` member proceeds (the whole existing suite, plus an explicit case per surface); Return proceeds on NoFirm / held / re-run pending (`Return_IsNotGated`; the existing `Return_IsUnchanged_ByTheHoldFilter` also holds the firm hold); Set firm proceeds on a firmless invoice (the whole `InvoiceAssignFirmServiceTests` suite, which runs on firmless invoices and is unchanged).
- **Reject (EB-D-190), `InvoiceApprovalFirmHoldTests`:** must-fire for no firm, held by the invoice's own column, held by a submission's stored hold, re-run pending and re-run unreadable: 409 `ebilling.invoice.firm_missing`, the matching sentence, no write, no claim, no Decision Record, the Signal still Open. Must-not-fire: a firm with the re-run `completed` rejects; a plain firm invoice rejects; Return to Firm proceeds while the re-run is pending. Order: an ineligible approver on a firmless invoice gets `NotEligible`, not the 409. The approval state's `firmNeeded` is true and `canApprove` false while the re-run is pending. Front end (jest, InvoiceReview): Approve and Reject disabled with the reason when `firmNeeded`; Reject enabled otherwise; `firmNeededBlocker` and `approveBlocker`.
- **Order:** an unauthorised caller on a firmless / held / re-run-pending invoice gets 403, not 409: Override without the privilege (`not_permitted`, the gate not read), Override with the privilege but without the rights to close (`not_authorized`), line decisions with no rights and with read-only rights on both routes (`ebilling.decision.not_authorized`). A refused Override followed by the firm being set decides on the retry with exactly one record and the full set.
- **The flipped 114b tests** (ontology-r1 condition 1): not deleted. `Release_WhileTheFirmHoldRemains_IsRefused_AndLeavesEverySignalOpen` (was "an Internal gap released while the firm hold remains leaves the shared Signal OPEN") and `OrdinaryOverride_WhileTheFirmHoldStands_IsRefused_AndClosesNothing` (was the firm-hold case of "an ordinary override ... leaves the shared Signal open") now assert 409 `ebilling.override.firm_missing`, no Decision Record, no Signal closed. The D-201 filter and its other tests stay (two linked submissions; another Procedure Signal; the "another linked submission's gap" ordinary-override case).
- **Seed (once).** `InvoiceFirmGate.CheckAsync` made to return `Open` always: 82 tests failed in the filtered suite (75 before Reject was gated; the new Reject must-fire cases are among the 82) (the new must-fire tests of all four surfaces and the gate unit tests, and the existing 095c-4 / 095c-5 / 082c-2 approve tests that rest on the same check); restored (file identical to the pre-seed copy), all pass.

### Existing tests changed, and why each still proves its point

| Test | Change | Why it still proves its point |
|---|---|---|
| `InvoiceLineDecisionServiceTests` `World` | the default invoice has a firm (`sprk_vendororg`); new `Submissions` list answers the linked-submission query | every line-decision test now decides on an invoice that has a firm, as the rule requires; none of them is about the firm |
| `100: with the lock defeated, the row-version-conditional line write ...` | its private invoice answer (built inline) gets a firm | it proves the conditional write is exactly-once; the invoice was firmless only because it was built inline |
| `SubmissionOverrideServiceTests` `World` | `HoldReadFaultAfter` / `FirmGateHoldReads` (new test knob) | three fail-closed tests aim at the holds derivation's own fault; the gate now reads the same rows first, so they skip the gate's 3 reads and still prove the derivation's 503-before-any-write |
| `114b must-fire (a)` and the firm-hold case of `114b: an ordinary override ...` | flipped to assert the 409 (above) | the original behaviour is the one EB-D-185 removed |
| `114 must-fire: the release re-derives the detail ...` | the two data rows "own firm hold still set" and "invoice names no firm" removed (the other four hold reasons stay) | an Override can no longer leave a firm hold standing; the derivation of those two reasons is still pinned by `InvoiceOpenHoldsTests`, and the refusal by the new must-fire tests |
| `114b retry (d)`, `114b F1 must-fire` (rights asked on the invoice and the core), `114b F1 must-fire` (403, nothing written), `114b F1 replay` | the "hold that remains" is another linked submission's gap instead of the firm hold | each proves the filtered/empty set, the rights and the replay; they need *a* hold to remain, and the firm hold is no longer one an Override can pass |
| `095c-4: Return to Firm of an invoice with no firm ... Reject is allowed` (`ReturnToFirm_NeedsAFirm_RejectDoesNot`, now `..._AndSoDoesReject`) | the Reject leg asserts `Held` / `ebilling.invoice.firm_missing`, invoice still In Review, no write (it asserted `Decided` / Rejected) | the Return legs are unchanged; the Reject leg is the behaviour EB-D-190 reverses |
| `114b F1 order (K2)`, `114b: a Return closes the set as before` | unchanged (they still pass; the first now also shows 403 before the gate; the second is the Return must-not-fire) | - |

## 7. Known limits

- A retry of an Override whose first attempt began before EB-D-185 (record written, state not finished) on a still-firmless invoice is refused by the gate like any Override; the reviewer sets the firm first. Such a row can only exist from before this change.
- (Closed by the fix round, R6.) The lines grid, the By-rule suggestions, the corrections and the wizard's Adjust step now read `firmNeeded` and are disabled with the words; a 409 `firm_missing` that still arrives (the state changed under the page) keeps the dialog open with the typed reason and shows the server sentence, with no Reload.
- Return to Firm on an invoice with no firm is still refused by its own 095c-4 check, so such an invoice can only take Set firm until the firm exists.
- Until 095d-2 clears the marker (and writes `completed`), an invoice after Set firm stays refused: `sprk_invoice.sprk_firmhold` = `ebilling.submission.rerun_pending` (section 8). That is the owner's rule and the order of the slices, so 095d-2 must ship before a customer can decide an invoice whose firm was set through Set firm.
- The Set-firm apply still leaves the submission's `firm_missing` reason code as it is (082c-2 fix round 3), so `InvoiceFirmHold` keeps answering "held" for the submission; the gate answers `RerunPending` first from the invoice-level marker, so the reviewer sees the re-run sentence. 095d-2 should clear the marker, rewrite the reason and write `completed` in one transaction.

- **K1 (known limit).** Line decisions left Incomplete (a 503 "incomplete" the reviewer did not repeat) before EB-D-185 cannot be finished by Repeat on an invoice that now has no firm, is held, or has its re-run pending: the repeat is a line decision and is refused 409 `ebilling.line_decision.firm_missing`, nothing written. They finish once the re-run completes; 095d-2's reset of the review covers them. Such rows can only exist from before this change.
- **K3 (known limit).** Accept all Clean on an invoice with zero Clean lines returns 200 and writes nothing. Nothing is decided and nothing is written (no decision, no Decision Record), so it is harmless; the client does not offer the button with no Clean lines.

## 8. Fix round (adversarial review R1-R8; uac-r2 approved R3 with conditions)

**R1.** Return to Firm passed after Set firm: it asks `InvoiceFirmGate.CheckForReturnAsync` (no firm still refuses; the rerun-pending marker and the pending re-run do not). **R2.** The gate checks a pending re-run before the held test. **R4.** The `HoldReadFaultAfter` tests now prove the holds derivation's own 503 (the gate's two reads are skipped by `FirmGateHoldReads = 2`).

**R3: the rerun-pending marker.** Set firm used to clear `sprk_invoice.sprk_firmhold`, so once a reviewer set the firm the invoice looked open to anything that read only that column. It now writes `sprk_firmhold` = `ebilling.submission.rerun_pending` (`InvoiceFirmHold.InvoiceRerunPendingValue`) in the same transaction (`InvoiceAssignFirmService`, the apply builder). The gate maps exactly that value to `RerunPending` (the invoice-level marker wins even when no submission is linked); `firm_missing` is `HeldForFirm`; any other non-empty value is held (fail closed); only an empty column is open. A removed or re-pointed lookup (`sprk_vendororg`) cannot release it: the marker is on the invoice, not derived from the lookup.

- **Obligation on 095d-2 / 2a-2 (TODO in code, `InvoiceAssignFirmService` apply builder):** only the re-run clears the marker, in the same transaction that writes `rulesRerun` = `completed`. Nothing else may write the column.
- **Field security (uac-r2 condition, confirmed from the repo).** `sprk_firmhold` is field-secured: only the Writers profile can Create/Update it (`FieldSecurityProfiles.xml` about lines 897-904); the Readers profile is read only. The Writers profile's membership is BFF application users only (enforced by `Set-EbillingInvoiceFirmHoldColumn.ps1` p2 and step (c)). **No human field-security profile can Update the column**, so no person can clear the marker by hand.
- **Existing rows:** none to back-fill (the main session checked dev: 0 invoices need a backfill).
- **Review detail (judgment call, reported).** The review-detail recompute after Set firm now sees the invoice hold set (`invoiceHoldSet: true`), so the partner label stays "Waiting for the firm to be confirmed" (`WaitingForFirm`, 659490002) until the re-run clears the marker. Before: it cleared as soon as the firm was set. Four review-detail tests were updated for this.
- **`Set-EbillingInvoiceFirmHoldColumn.ps1`:** the column Description and header text say the column also carries the marker (text only; the column is not re-created).

**`sprk_firmhold` readers (all app-only; `InvoiceFirmHoldAppOnlyGuardTests` governs the types that name it):**

| Reader | file:line |
|---|---|
| `InvoiceFirmHold.HeldInvoicesAsync` (any non-blank holds; approve, export, Return to Firm, corrections, adjustment rule options) | `Services/Finance/Submission/InvoiceFirmHold.cs:137` |
| `InvoiceFirmHold.InvoiceHoldStateAsync` (None / RerunPending / Held; the gate) | `Services/Finance/Submission/InvoiceFirmHold.cs:176` (state decided at `:208`) |
| `InvoiceFirmHold.IsInvoiceHeld` (pure, over a row already read) | `Services/Finance/Submission/InvoiceFirmHold.cs:217` |
| `InvoiceOpenHolds.ReasonsAsync` (review detail, supersede undo, Set firm) | `Services/Finance/Submission/InvoiceOpenHolds.cs:106` |
| `ParentLineage` (the columns a generic writer must never write; a refusal list, not a read) | `Services/Dataverse/ParentLineage.cs:1173` |
| `Deploy-EbillingInvoiceReviewDetailColumn.ps1` (-Verify reverse invariant; any non-blank value counts as a firm hold) | `scripts/ebilling/Deploy-EbillingInvoiceReviewDetailColumn.ps1:243` |

Writers: `InvoiceSubmissionPersister.cs:1512` (the held outcome's create) and `InvoiceAssignFirmService.cs:1013` (the marker). `InvoiceFirmGate` does not name the column; it goes through `InvoiceFirmHold`.

**R5 (EB-D-197): adjustments and corrections.** See the surface table (section 2). Add and Remove adjustment are both gated (a judgment call: the owner text says "adjustments", and a Remove changes the totals the re-run will re-derive). Both rule-less and rule-cited adds are gated; the 403 comes first (the route filters), the 409 before any write, a read fault is `Unavailable`. UI: the wizard's Adjust step disables Add and Remove and shows the words (`adjustment-firm-first`), and the header and line corrections are disabled.

**R6 (lines grid).** `InvoiceReviewPage` takes `firmNeeded` (fed from the approval state in `InvoiceReviewWithDecisions`): Accept/Reduce/Disallow (pane Confirm), the ticked-row Accept and Disallow/Reduce, Accept all Clean, the By-rule apply and the corrections are disabled with the firm-first words. A 409 `ebilling.line_decision.firm_missing` (`isFirmFirst`) keeps the dialog open with the typed reason, shows the server sentence, and offers no Reload (nothing changed, so nothing to reload). Any other 409 still closes the dialog and offers Reload. Correction and adjustment 409s still offer Reload if the UI state was stale (judgment call; not changed).

**R7.** The firm rule is EB-D-185 (+190, +197) everywhere in code, tests and these notes; "D-148" in this slice was a mis-numbering (EB-D-148 is the Procedure and file model and is left alone where it means that). **R8.** Known limits K1 and K3 are in section 7.

**Tests (fix round).** BFF: `InvoiceFirmGateTests`, `InvoiceApprovalFirmHoldTests`, `InvoiceAssignFirmServiceTests`, `InvoiceLineDecisionServiceTests.FirmGate`, `SubmissionOverrideServiceTests.FirmGate`, `InvoiceAdjustmentServiceTests`, `InvoiceCorrectionServiceTests` (marker set gives RerunPending; unknown value held; marker wins with no linked submission; re-pointed or removed lookup cannot release; adjustments and corrections hit the gate; must-not-fire: firm, no marker and re-run completed is open). Seeds (once each): adjustment and correction gates replaced by `Open`: 27 failed in the adjustment/correction suites; restored, 501 pass. UI (jest): five `firmNeeded` page tests and one Adjust step test; seeds: the firm-first branch of `runWrite` and the Accept-all-Clean disable removed gave 3 failures, the Adjust step blocker nulled gave 1; restored.
