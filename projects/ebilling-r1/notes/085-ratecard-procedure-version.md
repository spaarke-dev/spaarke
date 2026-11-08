# Task 085: Rate cards keyed to the Procedure Version (EB-D-148, D-120)

Branch `task/ebilling-085`, base `ecc8a73639` (origin/work/ebilling-r1, 082 complete). Rigor FULL. Written 2026-11-08.

## What changed

A rate card belongs to a Procedure Version. No code reads or writes `sprk_ratecard.sprk_scope` or the card's core-record lookups (`sprk_matter`, `sprk_project`, `sprk_workassignment`).

- **Import** (`RateCardImportService`, `RateCardImportEndpoints`). `POST /api/finance/ratecards/import` takes `procedureVersionId` (required). The version must be a Draft of a Rate-card Procedure; the firm comes from the Procedure. `firmId` is optional: when present it must equal the Procedure's firm, otherwise 400. The retired keys `matterId`, `projectId` and `workAssignmentId` are refused with 400, not ignored. A new refusal code `ratecard.procedure_version_invalid` covers an unknown version, a non-Draft version, a Procedure that is not a Rate-card Procedure, and a firm mismatch. The card owner is resolved with no parents (`CardOwnerContext`).
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
