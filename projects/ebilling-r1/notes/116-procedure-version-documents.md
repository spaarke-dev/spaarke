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
