# Task 085: Rate cards keyed to the Procedure Version (EB-D-148, D-120)

Branch `task/ebilling-085`, base `ecc8a73639` (origin/work/ebilling-r1, 082 complete). Rigor FULL. Written 2026-11-08.

## What changed

A rate card belongs to a Procedure Version. No code reads or writes `sprk_ratecard.sprk_scope` or the card's core-record lookups (`sprk_matter`, `sprk_project`, `sprk_workassignment`).

- **Import** (`RateCardImportService`, `RateCardImportEndpoints`). `POST /api/finance/ratecards/import` takes `procedureVersionId` (required). The version must be a Draft of a Rate-card Procedure; the firm comes from the Procedure. `firmId` is optional: when present it must equal the Procedure's firm, otherwise 400. The retired keys `matterId`, `projectId` and `workAssignmentId` are refused with 400, not ignored. A new refusal code `ratecard.procedure_version_invalid` (400) covers an unknown version, a non-Draft version, and a Procedure that is not a Rate-card Procedure. A firm mismatch is not that code: it is `Invalid` (400 `ratecard.import_invalid`) with an error on column `firmId`. The card owner is resolved with no parents (`CardOwnerContext`).
- **Copy-forward** (`RateCardImportService.PlanCopyForwardAsync`, called from `ProcedureService.CreateVersionAsync`). When a Rate-card Procedure gets a new version, that version starts with a Draft copy of every Published, active card of the previous version and its active rates. The plan is built before the write and sent in the version-create transaction, so a version and its cards are all or nothing. The copy keeps firm, name, dates, rates and timekeepers; it has no source document (a new workbook is not the old one); the previous card is not touched. More cards or rates than one transaction holds is 422 `too_many_rules` (same code as the rule copy). An unresolvable card owner refuses the new version and writes nothing (fail closed).
- **Rate resolution** (`RateCardResolver`, `InvoiceResolutionService`). One `ProcedureResolver.ReadAsync` per invoice, then `RulesOn(date)` per distinct work date, in memory. The versions chosen for a date, with their rank (Level, then Distance, then FirmSpecific), are the only cards that can apply to a line of that date. The rate query asks only for those versions' cards. Tiers: highest rank wins, then the timekeeper basis beats classification inside a rank. Two rates of equal rank and basis with different amounts are Ambiguous; the same amount is not. A resolver fault propagates (the job retries); no rate is judged on a partial set. `RateCardScope` is gone; `RateResolution.Rank` is a `ProcedureRank?`.
- **Context panel** (`InvoiceContextService`). Approved rates come from the versions chosen by `RulesOn` for each distinct line date, else the invoice date; one resolver read per invoice. A resolver fault makes the rate-history section unavailable through the existing guard, with no firm or core-record fallback. The hand-typed core-table list is deleted. `ApprovedRateItem.Scope` is now a rank label from `RateRankLabel.Of` (`Level/dN[/firmSpecific]`, invariant culture, no ids, names or record references). The value set of the TypeScript type in `decisionApi.ts` is unchanged (it is `string`), so no UI file changed.
- **Signal evidence** (`ApprovedRateHandler`). The reference key `approvedRate.scope` is replaced by `approvedRate.rank` (same label). **Reference-key change:** `billing.reference` lines are returned to Signal readers by `SignalReadService`, so a stored Signal written before this change still carries `approvedRate.scope`; new Signals carry `approvedRate.rank`. I searched `src`, `docs` and `projects` for readers of the old key and for any external or firm-facing projection of Signal evidence, and found none. The lead tells ontology-r1 at merge.

## Contract changes

- `RateCardImportRequest(Guid? FirmId, string Name, Guid ProcedureVersionId, Guid? CallerObjectId)`; the core-record fields are gone.
- `POST /api/finance/ratecards/import?procedureVersionId=&name=[&firmId=]`; 201 body unchanged (`RateCardImportResult`).
- `RateRow(…, Guid? ProcedureVersionId, …)` replaces the three core lookups and the scope; `RateCardResolver.Resolve(rows, firmId, chosenVersions, timekeeper, lineDate)`.
- `RateResolution.Rank` (`ProcedureRank?`) replaces `RateResolution.Scope`; `RateRankLabel` is new.
- `ProcedureService.CreateVersionAsync` additionally writes the cards of a Rate-card Procedure (same result type).

## Dataverse

No schema change and no write to dev (read-only): `sprk_ratecard.sprk_procedureversion` was verified present on dev with `describe`, and there is no `sprk_scope` column on the card.

## New authorization surface: `RequireWriteOnTheVersionsProcedure` (section 11)

The import also needs the caller's own Write on the Procedure the version belongs to (I-P5, the same rule the Procedure and rule-set routes apply).

1. **Existing.** `BillingAdminAuthorization.RequireWriteOnTheRuleSetsProcedure` asks the same question from a rule set's id.
2. **Extension.** That filter is keyed to the `ruleSetId` route value and the rule-set service; this route carries a version id in the query. The new filter reads the version's Procedure app-only (one query) and then calls the SAME `CallerReadCheck.CheckAsync` with the same entity set (`BillingAdminAuthorization.ProcedureEntitySet`) and the same "write" verb, so the rights question has one implementation. Only the version-to-Procedure lookup is new; no new service, no DI registration.
3. **Cost of doing nothing.** A rate card admin without Write on a Procedure could attach a card to it, which sets the rate its firm's invoices are judged against.

Behaviour: denied is 403 `insufficient_rights`; an unanswered rights question or a fault reading the version is 403 `system_failure` (fail closed); an unknown or malformed version is passed on and the handler or service answers 400. It runs after `AddFinanceAuthorizationFilter` (step 0 and the three table privileges first).

Every route in `RateCardImportEndpoints` that takes a `procedureVersionId` carries it. There is one: `POST /import`. The other two routes (`GET /import-template`, `POST /{rateCardId}/publish`) take no version id. Ledger: `RouteAuthorizationGuardTests.Ledger.cs` gets one `CreditedForm("RequireWriteOnTheVersionsProcedure", …)` entry beside `RequireWriteOnTheRuleSetsProcedure` (approved by the lead).

## Tests and fail-before / seed proofs

- `RateCardResolverTests` rewritten for the rank tiers: unchosen version never used; timekeeper beats classification; equal rank different amounts Ambiguous; fault propagation at the service level.
- `InvoiceResolutionServiceTests`: one resolver read per invoice; per-date version choice (v1 window uses v1's card, v2 window v2's); the rate query asks only for chosen versions and names no core lookup or scope column; no Procedures is NotFound (`NoRateInForce`), not a fault.
- `RateCardImportServiceTests` / `RateCardImportEndpointsTests`: version validation, firm from the Procedure, firm mismatch, retired keys, the Procedure-Write filter must-fire and fail-closed cases.
- `ProcedureServiceTests` (outside the POML test list): copy-forward copies the Published card and active rates as a Draft in one transaction, leaves the previous card alone, copies nothing for a non-Rate-card Procedure, and refuses the version when the card owner is unresolvable. Fail-before: with `ProcedureService.cs` at the base, the two behaviour tests fail (the negative control passes before and after).
- Seed proof: making `RateCardResolver.Applies` accept an unchosen version fails 5 tests (the two "unchosen version" must-fire tests, the version-window test, the precedence test and `WhenNoVersionIsChosen_NothingIsUsed`). Restored and confirmed byte-identical with `cmp`.
- `InvoiceContextServiceTests`: rate query names only the chosen versions and no core lookup; resolver read once per invoice; a resolver fault makes the rate history unavailable and reads no rates; invoice-date fallback.
- `ApprovedRateHandlerTests`: `approvedRate.rank` replaces `approvedRate.scope`; the label names no id, name or record.
- A core record with NO Procedures resolves to no card, not a fault: `TheProcedureResolver_IsReadOncePerInvoice_AndNoRateIsReadWhenNoVersionIsChosen` (resolution service) and the new `InvoiceCorrectionB2cTests.TimekeeperCorrection_OnACoreRecordWithNoProcedures_AppliesWithNoRate_NotAFault` (the correction still applies, writes the roster match and no rate).

## Out-of-list files touched (each approved by the lead)

- Production: `InvoiceContextService.cs`, `ApprovedRateHandler.cs`.
- Tests: `InvoiceContextServiceTests.cs`, `ApprovedRateHandlerTests.cs`, `BillingHandlerKit.cs`, `ProcedureServiceTests.cs`.
- ArchTest ledger: `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs` (one entry).
- Test worlds that build a real `InvoiceResolutionService` over a strict fake that now needs the Procedure rows: `CorrectionWorld.cs` (which also serves `InvoiceLineAddServiceTests.cs` and the other correction tests), plus the new shared builder `RateProcedureWorld.cs` (also used by `InvoiceContextServiceTests`). Only world data changed: one all-records Procedure with one version in force, and the rate rows point at that version. No assertion was changed in these files. `InvoiceCorrectionB2cTests.cs` gets the one new no-Procedures test above.

## BFF hygiene (section 10)

- No package added; no new service, interface or DI registration.
- Publish size, Release publish of the base tree vs this tree: 137,823,741 bytes vs 137,828,017 bytes (+4.3 KB); `Sprk.Bff.Api.dll` 25,801,216 vs 25,815,552 bytes (+14 KB). Well under the +5 MB threshold.
- No new package, so no new CVE exposure.

## Retained rules

`RuleSetService.cs`, `InvoiceApprovalService.cs`, `ApExportService.cs`, `ApExportEndpoints.cs` and `OwnSubmissionRuleSingleSourceTests.cs` were not touched. `procedureSnapshot` in `sprk_findingsjson` is neither projected nor changed. `TASK-INDEX.md` is not edited.

## Notes for later

- A card published under a version is in force only for work dates the resolver chooses that version for; a copy-forward Draft card keeps the old dates and the admin edits them before publishing.
- Cards orphaned by the retired scope design (DEF-027) no longer exist as a concept: a card with no Procedure Version applies to nothing.

## Fix round 1 (adversarial pass 1)

Branch `task/ebilling-085-fix1`, base `417889cce0`.

### F1-a: publish now also asks Write on the card's Procedure

`POST /api/finance/ratecards/{rateCardId}/publish` now also asks Write on the card's Procedure (`RequireWriteOnTheCardsProcedure`, after the table privileges and the eBilling flag, as the import's filter). The card, its Procedure Version and that version's Procedure are read app-only; the caller's own Write on the Procedure is asked through the same `CallerReadCheck.CheckAsync` body as the import (`CheckCallerWriteOnProcedureAsync`, shared, not copied). Responses: no Write is 403 `insufficient_rights`; a read fault or an unanswered question is 403 `system_failure`; an unknown card, a card with no version and a version with no Procedure are 403 `insufficient_rights` too, so the answer does not say whether a card exists.

Same card, same version (uac-r2 condition): the filter leaves the version id it checked in `HttpContext.Items`; the handler passes it to `RateCardImportService.PublishAsync(rateCardId, expectedVersionId, ct)`, which reads the card again (the read it already made, now with `sprk_procedureversion`) before any write and refuses 409 `ratecard.version_changed` when the card names another version or none. The comparison sits in the service, at the read that decides Draft, so no write can come between the comparison and the first write of the publish; the two-argument `PublishAsync` remains and skips the comparison. No record of the check in `Items` is a 403 `system_failure`.

Ledger: `CreditedForm("RequireWriteOnTheCardsProcedure", ...)` in `RouteAuthorizationGuardTests.Ledger.cs`; the import entry's line citations were refreshed after the shared helpers moved the lines.

Rate-card write routes and the check each carries:

| Route / writer | Writes | Checks |
|---|---|---|
| `POST /api/finance/ratecards/import` | `sprk_ratecard`, `sprk_ratecardrate`, `sprk_timekeeper`, document | step 0; Create on card, rate and timekeeper; eBilling flag; caller's Write on the version's Procedure |
| `POST /api/finance/ratecards/{rateCardId}/publish` | card status, timekeeper approvals | step 0; Write on card and timekeeper; eBilling flag; caller's Write on the card's Procedure (new) |
| `GET /api/finance/ratecards/import-template` | none | step 0; Create on card; eBilling flag |
| `POST /api/finance/procedures/{id}/versions` (copy-forward) | `sprk_ratecard`, `sprk_ratecardrate` for a Rate card Procedure | step 0; Write on the Procedure; billing-admin marker; Create on policy version; eBilling flag; Create on card and on rate for a Rate card Procedure (new, F1-c) |

`ImportAsync` is called only from the import route, `PublishAsync` only from the publish route and `PlanCopyForwardAsync` only from `ProcedureService.CreateVersionAsync`. A search of `src/server` for the card and rate entities found only these writers; `InvoiceResolutionService` and `ProcedureResolver` only read.

### F1-b: a Retired version with a Published card opened its window

The 082 handover is closed. `ProcedureResolver.ReadRowsAsync` now reads, in one chunked query for all Retired versions with an effective-from (skipped when there is none), the Published, active `sprk_ratecard` rows keyed to them. `ProcedureRows` carries the result in a new optional member (`VersionsWithPublishedCards`, default null) and `ProcedureResolution` merges it into the set `ProcedureVersionRow.OpenedAWindow` already tests, so a Retired version with a Published card counts as having opened a window exactly as one with a Published policy version does. `ProcedureService.ScopeLockedAsync` does the same with one more query over all Retired versions (not per version), run only when the rule query found nothing.

For ontology-r1 review: the hunk in `Services/Signals/Billing/ProcedureResolver.cs` is the `ProcedureResolution` constructor line, the new `ReadRetiredVersionsWithPublishedCardsAsync` read and its call in `ReadRowsAsync`, the optional `ProcedureRows` member, and the `OpenedAWindow` remark. No other resolver logic changed.

Reading of "no lower-ranked card prices it": a gap raised on the Procedure means none of its versions is chosen for that date, so none of its cards (v1's withdrawn card, v2's not-yet-in-force card) prices the line. Rate resolution still lets a card of another Procedure decide when that Procedure is chosen for the date (see K3); no rate-resolution behaviour changed.

Known limit: a card that was Published and was later made inactive is not seen, so a version whose only evidence of having been in force was that card is read as never opened.

### F1-c: version creation and the card tables

`POST /api/finance/procedures/{procedureId}/versions` for a Rate card Procedure now also requires Create on `sprk_ratecard` and `sprk_ratecardrate` (`RequireRateCardCreateOnARateCardProcedure`, after the declared checks). The existing declaration is synchronous and cannot read the Procedure's type, so the filter reads the type app-only and asks the same table-privilege question (the probe's three-state answer, as the caller). Missing privilege is 403 `insufficient_privilege`; a fault, an unanswered question or an unreadable type is 403 `system_failure`; a Guideline Procedure asks nothing extra; an unknown Procedure is passed on (the earlier Write check has answered, the service answers 404). The refusal is before the service, so nothing is written. It is a filter, not a service check: the service has no caller privilege seam.

### F4

The firm-mismatch wording (What changed, Import bullet) is corrected.

### Known limits and a design decision

- K1: an unknown version on import is 400 `ratecard.procedure_version_invalid`, not 403 (not aligned: the import filter passes an unknown version on, as before; the publish route answers 403 for an unknown card).
- K2: the evaluation pipeline reads the resolver twice per invoice (`InvoiceResolutionService.cs:123` and `BillingFactLoader.cs:312`), so a publish landing in between can split the two snapshots. Not fixed here.
- K3: lower-ranked cards decide while the higher Procedure's copied card is still a Draft; there are no service-level tests of the whole chain; the context panel's id guard skips resolver reads.
- Design decision (formerly K4, uac-r2 approved, 2026-11-08): the Invoice Review context panel shows the rates that resolution applies to the invoice's work dates to any reviewer with core Read. It takes only the invoice id, chooses no Procedure or card from the client, and prints only the rank label. If it ever gains a parameter that picks a Procedure or card, it must ask Procedure Read (`RequireReadOnTheRuleSetsProcedure` parity). Pinned by `InvoiceContextServiceTests.TheContext_CarriesNoProcedureIdentifier_OnlyTheRankLabel` (new: no Procedure id or name, no version id, only the rank label). Only resolver-chosen versions appear: `EveryCrossInvoiceQuery_IsScopedToTheInvoicesFirmAndCoreRecord_AndExcludesTheCurrentInvoice` and `TheApprovedRates_AreJudgedOnTheLineDates_AndOnTheInvoiceDateWhenNoLineHasOne` (existing). A core Read failure gives Unavailable: `ACoreRecordTheCallerCannotRead_GivesNoDetailsNoBudgetAndNoCoreScopedRates` and `AFailedCoreRecordAccessCheck_MakesTheCoreSectionsUnavailable_NeverTheData` (existing).
- Dev count (read-only, 2026-11-08): Draft `sprk_ratecard` rows with no `sprk_procedureversion`: 0.

### Tests added

- Endpoint (`RateCardImportEndpointsTests`): no Write on the card's Procedure is 403 and nothing is published; with Write it is published (control); a rights fault, a read fault on the card and on the version, an unknown card, a card with no version and a version with no Procedure are 403; the service's 409 `ratecard.version_changed` maps to 409. The three existing publish tests now seed the card's chain.
- Service (`RateCardImportServiceTests`): a card that names another version, or none, is refused 409 with nothing written; a card that still names the checked version is published.
- Resolver (`ProcedureResolverTests`): v1 retired with a Published card and v2 Active later gives a gap in the v1-only window (before: not applicable); no card, a Draft card and an inactive card behave as before; the card read is one query.
- Service (`ProcedureServiceTests`): a Rate card Procedure whose only version is Retired with a Published card has a locked scope; a Draft or inactive card does not lock.
- Version creation (`ProcedureEndpointsTests`): a Rate card Procedure without either card privilege is refused; a Guideline Procedure with the same caller is allowed; a caller with both privileges is allowed; a read fault and an unanswered question are `system_failure`.
