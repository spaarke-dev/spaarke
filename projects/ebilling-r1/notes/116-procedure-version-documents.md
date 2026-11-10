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

Code and script references still to clear (task 116's remaining work): ~~`RateCardImportService` (writes it)~~ (done in part 2), `RecordOwnershipResolver.cs:1809` (a citation entry `("sprk_ruleset", "sprk_sourcedocument")`, uac-r2's code), RuleReview `ruleSetApi.ts` / `testKit.ts` (`sourceDocumentId`), `Deploy-EbillingFormsViews.ps1` (both forms), and the tests that seed it. Creation scripts `Deploy-EbillingPolicyStoreSchema.ps1` and `Deploy-EbillingTimekeeperRateCardSchema.ps1` add the lookups and stay as history. SpaarkeMaster XML is 079's to re-sync.

## Part 2: tests, guards, the rate-card file step (2026-11-10)

| Commit | What |
|---|---|
| `c47fefc492` | Drafting, compensation, job and seam tests re-keyed per Procedure Version (EB-D-211: memo first, Yes last; AlreadyDrafted reads the Yes/No; `RuleSetDetail.NotConverted` is the highest-numbered version's memo; compensation per version keeps copy-forward drafts; source states Pending (retried), Malicious (Poisoned), Missing (`ruleset.source_missing`), exactly one Servable file else 422). Job: payload, subject, idempotency and lock key are the version id; a payload with no version id is Poisoned. Retired rule-set routes: a test that they are gone. |
| `9f1d82ec37` | `StoredFileService.ReadRowsForProcedureVersionAsync` and `ReplaceForCallerAsync` (new Servable before the earlier is Removed; a failed withdraw rethrows, the new row Removed and the earlier one still Servable; a refused or failed upload leaves the earlier file alone; `WithdrawForUndoAsync` still one conditional write). `ProcedureService.GetDraftVersionForFileAsync` accepts only a Draft version of this live Procedure (a missing status is not a Draft). |
| `b58b9a9039` | **Fix**: the file route stored the client's claimed Content-Type although `RuleSetService.MimeTypeOf` (until then unused) documents a type derived from the name; the route now uses it. Route tests for `/draft` and `/file` (privileges must-fire and must-not-fire, 202 keyed by the version, refusals answered now with no job, 409 already drafted, 503 queue down, 413 by Content-Length and by file length, refused versions store nothing, replace, failed withdraw 500 with the earlier file Servable, no Write on the version 403, no file 400). |
| `ee35443df5` | Rate-card import: the workbook is the version's source file through `ReplaceForCallerAsync`, AFTER the card, rates and timekeepers; a refused or faulted file write removes the rows. No `SpeFileStore`, `sprk_document`, container read or card source lookup remains. `RateCardImportResult.SourceDocumentId` becomes `SourceFileId`. A store that cannot stage refuses 503 `stored_file.store_not_configured` before any write (replaces `ratecard.container_not_configured`; no client reads that code). |
| `58a969074d` | ArchTests ledgers (below). |

**Refused rate-card file write, HTTP mapping.** Copied from `ProcedureEndpoints.PutVersionFile`, the codebase's existing mapping of a refused caller write to the file store: `CallerNotInternal` 403 `system_failure`; `NoWriteOnParent` 403 `insufficient_rights` ("You need Write on this Procedure Version."); `OwnerUnresolved` 409 with the owner code; `NotConfigured` 503 `stored_file.store_not_configured`; `InvalidRequest` 400 `ratecard.import_invalid`; anything else 500 `ratecard.import_failed`. The import carries the store's refusal out as `RateCardOutcomeKind.FileRefused`, and the route maps it.

**ArchTests.** The pre-116 base `698fd222cc` passes 2200/2200, so the six failures were all 116's. Each ledger now matches the code; no guard's rule changed:
- SPE write-sink provenance (2 tests): the four `RateCardImportService` and `RuleSetService` `UploadSmallAsync`/`DeleteFileAsync` sites are gone; their entries are deleted.
- Writer-only delete: `RuleSetService.CompensateAsync` (the retired create's rollback) is gone; its entry is deleted.
- Child-create census: `RuleSetService.cs` no longer creates `sprk_ruleset`; the entry is deleted and the 089a non-vacuity check expects the two waived tables left. A new create there fails the census.
- InvoicePdf writer and app-only update writer: the status-only withdraw moved from `WithdrawForUndoAsync` into the shared `WithdrawRowsAsync` (undo and replace); the entries name it, with the same constraint.

**Fail-before.**
- The media-type test failed on the old route ("text/html" stored).
- With the rate-card change reverted (no `FileRefused` arm, no row compensation on a refusal), 8 of 8 new refusal tests failed.
- The drafting, job, store and route tests were written against the pre-116 contract's absence: the old code has no version-keyed drafting, `ReplaceForCallerAsync` or the two routes, so the tests do not compile against it.

**Verification (HEAD `58a969074d`).**
- BFF build: 0 errors, 0 warnings.
- Full ArchTests: 2200/2200.
- Filtered unit tests (Finance, Procedure, Jobs, StoredFile, RuleSet, RateCard, BillingRuleDraft, Ebilling): 7914 passed, 4 skipped, 0 failed, on the first run.
- Publish size, measured from fresh worktrees at short paths: `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/` (the project's framework-dependent linux-x64), then `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`, PDBs included. Master `26d135bc8f` is 39.27 MB; this branch `58a969074d` is 39.58 MB, a delta of +0.31 MB. The branch figure also carries work-branch tasks not yet on master. It is far under the +5 MB and 55 MB thresholds.

**Known limits and open items.**
- Two concurrent uploads to one version can leave two Servable files. Drafting and publish then refuse (422 / 400) until the next upload.
- A PUT to `/file` that is not multipart is answered 415 by routing, before authentication. That is ASP.NET's `Accepts` matcher; it reveals nothing beyond the route's existence.
- `RecordOwnershipResolver.cs:1809`, the citation entry `("sprk_ruleset", "sprk_sourcedocument")`, is uac-r2's code, and so are its tests (`SecureChildLineageTests`) and the `SecureChildLineage.cs` comment. They are not changed here and go to uac-r2.
- `grep ProcedureLibraryContainerId src/server` also finds `RecordContainerResolver.DocumentPointer.cs:65`, a list of configuration keys, not a read by the import.

## Not done yet

- code-review and adr-check on the 116 diff.
- RuleReview and the two scripts (another agent).
- TASK-INDEX.
