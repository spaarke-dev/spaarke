# Task 130f: Rejected and Returned-to-firm invoices show their line findings (EB-D-259)

Branch `task/ebilling-130f`, from `5fbd606e82` (work/ebilling-r1 tip). Rigor FULL. Model: Sonnet @ high.

## The change

`FirmFindingsProjection.GetForInvoiceAsync` (`src/server/api/Sprk.Bff.Api/Services/Finance/FirmFindingsProjection.cs`). The two early returns for Rejected and ReturnedToFirm (kind, label, billed total, `[]` items) are gone. Both statuses now fall through to the same `BuildItemsAsync` call In Review uses. No copy, no new method, no new DTO field, endpoint or schema change.

| | In Review | Rejected / Returned to firm (new) | Approved / Sent to AP / Paid |
|---|---|---|---|
| Kind and label | In review | Rejected, "Returned to firm" | as before |
| Billed total | yes | yes | yes |
| Approved total | no | no | yes |
| Invoice-level adjustment items | no | no | yes |
| Line items (Reduced, Disallowed, Added) | yes | yes (same code, same rules) | yes |

Mechanics: `decided` now means Approved, Sent to AP or Paid only (it was "anything but In Review"), so `includeAdjustments` and the approved total stay off for the two new cases. The fault path (`statusOnly`) gained a Rejected and a Returned entry: a fault reading the items gives the decided status only, `Degraded`, as the other states do (for the composer that is `Unavailable`, no notice).

## Firm-safety (unchanged rules, proved by the tests)

- The item reasons shown are the line-level decisions' (`reduce-line`, `disallow-line`, `add-line`), exactly as In Review. The whole-invoice decision record (`reject-invoice`, `return-to-firm`: reason and note) is never read: `ReadDecisionRecordsAsync` filters to the three line action codes. An override record is likewise not an item. Test: seeded decision reason, decision note and override reason appear in no part of the projection, and in no part of the notice subject, prose, HTML or the model request, with AI drafting on and off. Controls: the records are in the store and the line-level reasons are in the output.
- Flag-off rule sets keep EB-D-209 behaviour for both kinds (reason and firm reason only; no clause, section, label or rule name).
- Same clause decision (D-66, D-79) and same InternalStandard check, because it is the same code.

## Decision 4 of note 130 is superseded

130's "Decisions taken inside the brief" decision 4 said: "Rejected and Returned to Firm invoices project no items: no reason is in 130's contract (decision 7), so a line list without them would mislead." EB-D-259 (owner, 2026-11-17, 131 review finding 1) supersedes it. Why: EB-D-254 makes the notice and the invoice view the firm's only written record of a whole-invoice Reject or Return, and with no items both were empty, although the firm had seen the same line findings while the invoice was In Review. Dropping them at the moment of the decision removed the reason the firm most needs. The worry in decision 4 (a list without the reviewer's reason would mislead) is answered by keeping the reviewer's whole-invoice reason and note out, and by the line items carrying their own firm-safe reasons. The firm-safe whole-invoice reason is the confirmed notice prose (EB-D-258): task 130g, after 132. The header of 130's note itself is left as written (history); the XML doc on the class now states the new rule.

## Callers (closed set, `FirmFindingsProjectionCallerBoundaryGuardTests`)

- **SPA outcome route** (`FirmOutcomeEndpoints.GetInvoiceOutcomeAsync`): returns the projection as is (`Results.Ok(outcome)`); no filtering by kind. No code in `src/client/external-spa` or `src/client/shared` calls the outcome routes, so there is no client hiding items by kind. **No client change was made**, so spa-r4 has nothing from this task to review. When spa-r4 builds the invoice view it will receive items for Rejected and ReturnedToFirm.
- **FirmNoticeComposer**: `FindingsHtml` and `BuildRequest` render and send `outcome.Items` for any kind, so the post-decision table needs no change. Proved over the real projection: a Reject and a Return notice now render the findings table.

## Tests changed

- `FirmFindingsProjectionTests.RejectedOrReturned_ProjectStatusAndBilledTotalOnly_NoReasonIsRead` asserted `Items` empty and zero reads of `sprk_decisionrecord` for Rejected and Returned: exactly the superseded decision. Replaced by `RejectedOrReturned_ProjectTheSameItemsAsInReview_WithTheDecidedKindAndTheBilledTotal`: the same seeded invoice at In Review, Rejected and Returned; the items are equivalent (strict order) to In Review's; kind, label, billed total as decided; no approved total; no adjustment item (the seed holds an adjustment line, so absence is not vacuous).
- Nothing else asserted empty items for these kinds. `FirmNoticeComposerTests` use a stubbed outcome and are unaffected; `Applies` (kind vs outcome) is unchanged.

New: `RejectedOrReturned_TheDecisionReasonNoteAndOverrideReason_NeverReachTheProjection`, `RejectedOrReturned_FlagOff_ShowsTheReasonOnly_NoClauseSectionOrRuleName`, `RejectedOrReturned_AFaultReadingTheItems_IsTheStatusOnly_Degraded` (projection); `DecidedNotice_RendersTheFindingsTable`, `DecidedNotice_NoDecisionReasonNoteOrOverrideReason_ReachesTheNotice`, `DecidedNotice_AFaultReadingTheItems_IsUnavailable` (notice, over the real projection, in `FirmFindingsProjectionTests.FirmNotice.cs`).

Fail-before (old projection restored, new tests kept): 14 of the 179 `FirmFindingsProjectionTests` cases fail (every new row, both statuses); 179 pass with the change.

## Known limits

- K1. An invoice rejected or returned while a line is still Pending or Flagged shows no item for that line (an item exists only for a line a reviewer decided or added). Same as In Review; the firm sees the decided lines only.
- K2. The firm notice for a Return still carries the fixed sentence ("returned to you and is now closed"); the reviewer's reason in firm-safe words arrives with 130g after 132.
- K3. The `FirmOutcome` doc comment in the 130 note and earlier fix-round notes still describe "status and billed total" for these kinds; they are history and were not edited.

## Gates

See the report in the task hand-off (counts recorded there): BFF build with `--warnaserror`; filtered FirmFindings, FirmNotice, ExternalInvoice and FirmOutcome unit tests; ArchTests filtered to FirmFindings, FirmNotice, External and Finance. External-spa: not run, no client change.
