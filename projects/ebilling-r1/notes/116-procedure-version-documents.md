# Task 116: Procedure Version documents (in progress)

Status: **in progress** on `task/ebilling-116`. Rigor FULL. Not merged, not deployed, nothing applied to any environment.

## Done so far

| Commit | What |
|---|---|
| merge of `698fd222cc` | DEF-061 brought in (`sprk_publishedon`, the shared published predicate); 116 builds on it. |
| `861008c591` | 080's `Deploy-EbillingProcedureSchema.ps1`: `sprk_procedureversion.sprk_notconvertedlist` (memo 100000, field-secured with `sprk_notconverted`'s grants), the EB-D-211 Description on `sprk_notconverted`, and the verify step reads both back. Pester 85 → 95. **Not applied**: the dev apply needs the owner's go, with uac-r2 notices before and after. |
| `9df60e9d20` | EB-D-211 in `RuleSetService` (list first, marker Yes last; AlreadyDrafted reads the Yes/No; `RuleSetDetail.NotConverted` reads the highest-numbered version's list); the two `ProcedureEndpoints` routes; `StoredFileService.ReadRowsForProcedureVersionAsync` and `ReplaceForCallerAsync`; the two `RuleSetEndpoints` routes retired; ArchTests route ledger. The BFF builds. |

## The two new routes (for uac-r2's review)

Both are on 081's group (`RequireAuthorization`, then `RequireInternalFinanceCaller` step 0), then a route-level `AddFinanceAuthorizationFilter`, then `RequireEbillingEnabled`.

| Method and path | Authorization | Answers |
|---|---|---|
| `POST /api/finance/procedures/{procedureId}/versions/{versionId}/draft` | `BillingAdminAuthorization.DraftProcedureVersion`: the caller's Write on the Procedure (Record path, `sprk_procedures`) plus `DraftRules()` (Write on `sprk_ruleset`, Create on `sprk_policy` and `sprk_policyversion`). No route id means no check, which the filter denies. | 202 + Location (the rule set read); 404 `procedure.version_not_found` for a version of another Procedure (checked first); 400 `procedure.not_billing`; 409 `procedure_version.not_draft`, `ruleset.already_drafted`, `procedure.retired`, `procedure.ruleset_missing`, `ruleset.retired`; 422 `ruleset.source_missing`; 503 `ruleset.draft_not_queued`. |
| `PUT /api/finance/procedures/{procedureId}/versions/{versionId}/file` (multipart) | `BillingAdminAuthorization.WriteProcedure` (081's write check: Write on the Procedure plus the billing-admin marker); then inside the file store, step 0 again and the caller's own Write on the version (`UploadForCallerAsync`). | 200 `{procedureVersionId, fileId}`; 413 `procedure_version.file_too_large` (Content-Length before buffering, then the file length); 404 `procedure.version_not_found`; 409 `procedure.retired`, `procedure_version.not_draft` (a missing status is not a Draft); 400 `procedure_version.file_missing`; 403 on a store refusal for rights; 409 owner code; 503 `stored_file.store_not_configured`; 500 `procedure_version.file_write_failed`. |

Replace rule: the new file is Servable first; only then are the earlier Servable rows set Removed (the shared withdraw, conditioned on the row version read). A refused upload or a failed flip leaves the earlier row Servable. If the withdraw fails, the new row is withdrawn (best effort) and the route answers 500. Known limit: two concurrent uploads can both read the same earlier row and leave two Servable files; drafting and publish then refuse (422 / 400) until the next upload.

## `sprk_sourcedocument` census (dev, read-only GETs, 2026-11-10)

| Table | Rows | Non-null `sprk_sourcedocument` | Forms | Views | `RetrieveDependenciesForDelete` |
|---|---|---|---|---|---|
| `sprk_ruleset` | 0 | 0 | main form "Information" (`bafad1a6-…`) | none of 7 system views; no personal view visible | 1: that form (type 60) |
| `sprk_ratecard` | 0 | 0 | main form "Information" (`da801593-…`) | none of 7 system views; no personal view visible | 1: that form (type 60) |

Relationships: `sprk_document_ruleset_sourcedocument`, `sprk_document_ratecard_sourcedocument`. Neither column is field-secured (the two `FieldSecurityProfiles.xml` entries named `sprk_sourcedocument` are `sprk_storedfile`'s, 088's, which stays).

Code and script references still to clear (task 116's remaining work): `RateCardImportService` (writes it), `RecordOwnershipResolver.cs:1809` (a citation entry `("sprk_ruleset", "sprk_sourcedocument")`, uac-r2's code), RuleReview `ruleSetApi.ts` / `testKit.ts` (`sourceDocumentId`), `Deploy-EbillingFormsViews.ps1` (both forms), and the tests that seed it. Creation scripts `Deploy-EbillingPolicyStoreSchema.ps1` and `Deploy-EbillingTimekeeperRateCardSchema.ps1` add the lookups and stay as history. SpaarkeMaster XML is 079's to re-sync.

## Not done yet

See the task report: unit and seam tests, the rate-card import file step, RuleReview, the two scripts, verification, reviews, TASK-INDEX.

## Part 3: RuleReview, the forms script and the column-removal script (branch `task/ebilling-116-ui`, from `15053b7161`)

Not merged, not deployed, nothing applied to any environment. The only dev contact was read-only GETs (the census below).

### RuleReview (`src/solutions/RuleReview`)

| Removed | Where |
|---|---|
| "New rule set" action and `NewRuleSetModal.tsx` (deleted); its state, `create` handler and render | `App.tsx`, `RuleSetListView.tsx` (the `canWrite` / `onNew` props go too; the empty-list text no longer says "Create one") |
| "Draft rules" button, the drafting spinner, `handleDraft`, the `drafting` guard on Publish / assignment / detail pane | `RuleSetDetailView.tsx` |
| `runDraft`, `draftFinished`, `DraftOutcome`, `PollOptions` | `services/reviewActions.ts` |
| `api.create`, `api.draft`, `RuleSetCreated`, `RuleDraftAccepted`, `CreateRuleSetInput`, `RuleSetSourceKind`, `CODE_ALREADY_DRAFTED`, `RuleSetDetail.sourceDocumentId` | `services/ruleSetApi.ts`, `__tests__/testKit.ts` |

Kept: the read of `notConverted` (the server still returns the highest-numbered version's list) and its accordion; the empty-grid text is now "No rules yet." (never drafted) or "The document produced no rules." No new UI for the two new routes: the POML does not ask for it (084 Part B owns the Procedure Version UI). `grep -rniE "sourceDocument|NewRuleSet|runDraft|draftFinished" src` finds nothing.

Tests: the `runDraft` block and the `create` route tests are removed; a guard test asserts the client exposes exactly `accept, assign, edit, get, list, publish, reject, setVisibility`. Jest 60 pass (3 suites). `npm run build` passes (`tsc-surface-gate`: surface-owned errors 0; the 99 shared-lib errors are the known deferred ones). `npm install --legacy-peer-deps --no-audit --no-fund` only; no `npm ci`. `eslint` has no config for this surface (ESLint 10 wants `eslint.config.*`), so no lint result.

### Forms script

`Deploy-EbillingFormsViews.ps1`: `sprk_sourcedocument` removed from the `sprk_ruleset` and `sprk_ratecard` main-form specs ("Information"). No view spec carried it. Not run. New `tests/scripts/Deploy-EbillingFormsViews.Tests.ps1` (5 Pester): the spec has no `sprk_sourcedocument`, and both forms keep their other columns, the ruleset's not-converted section and the rate card's rates subgrid. Against the old script it fails 5/5; against the new one it passes 5/5.

### `Remove-EbillingSourceDocumentColumns.ps1` (new, read-only in this task)

Copied from `Remove-EbillingInvoiceLineItem.ps1` (module `EbillingSchema.psm1`, `az` token, dev only, exit 0 / 1 / 2, `EscalationException`). Without `-Delete` it only GETs (a `Read-Dv` wrapper with no method parameter) and prints, per column: rows, rows holding a value, forms and views (system and personal) that mention the column, the relationship, `RetrieveDependenciesForDelete(ComponentType=2)` with each dependent named, then READY / BLOCKED / ALREADY REMOVED. `sprk_storedfile`'s own `sprk_sourcedocument` is ignored.

`-Delete` refuses (exit 2, nothing changed) unless `-OwnerApproval '<date and who>'` and `-Confirm` are both given and every column has no value, no form, no view, no dependent and exactly the expected relationship. Reading of the brief: the table's total row count is reported but is not a blocker (a row whose lookup is empty loses nothing); the non-null count is. The removal is the relationship delete (as 011 did for the timekeeper lookup), each write behind `ShouldProcess`, then an entity-scoped `PublishXml`. **`-Delete` was not run.** Run it only after the forms script has run on the target and this BFF is deployed (079).

Pester `tests/scripts/Remove-EbillingSourceDocumentColumns.Tests.ps1`, 25 pass: the census against a Dataverse test double that throws on any non-GET; the verdicts (clean, already gone, value held, count unknown, form / view / personal view / other table / dependent, wrong or duplicate relationship); the refusals (no approval, blank approval, no `-Confirm`, any blocker, missing census, all reasons at once); and AST checks that the census has no `-Method`, that the only `-Method` uses are the `DELETE` and the `PublishXml` POST, each after `ShouldProcess`, and that the removal is reached only after `Get-DeleteRefusals` in the `-Delete` path. The script is new, so there is no "before" run for it.

### Read-only census run on dev (2026-11-10, `pwsh scripts/ebilling/Remove-EbillingSourceDocumentColumns.ps1`, exit 0, nothing written)

| Column | Rows | Non-null | Forms mentioning it | Views (system / personal) | Other tables | Relationship | `RetrieveDependenciesForDelete` | Verdict |
|---|---|---|---|---|---|---|---|---|
| `sprk_ruleset.sprk_sourcedocument` | 0 | 0 | 1: main form "Information" (`bafad1a6-ace0-42ad-bc71-b9df4ecbd8eb`) | 0 / 0 | 0 | `sprk_document_ruleset_sourcedocument` | 1: that form | BLOCKED (the form) |
| `sprk_ratecard.sprk_sourcedocument` | 0 | 0 | 1: main form "Information" (`da801593-8779-4642-b813-b79a5c7b5f5d`) | 0 / 0 | 0 | `sprk_document_ratecard_sourcedocument` | 1: that form | BLOCKED (the form) |

This matches the earlier census in this file. The only blocker on each column is the main form; it clears when `Deploy-EbillingFormsViews.ps1` (with this change) is run on dev and the forms are published. Then re-run this script: expected READY on both.

### Still open for 116

Not in this branch (the other agent's `task/ebilling-116`): server unit and seam tests, the rate-card import file step, `RecordOwnershipResolver.cs:1809` citation entry, verification, reviews, `TASK-INDEX.md`. In this branch: the UI test on dev after 079 ("RuleReview without create and draft") is not run; the forms script has not been run on dev; `-Delete` waits for the owner's OK.
