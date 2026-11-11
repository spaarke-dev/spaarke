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
- `RecordOwnershipResolver.cs:1809`, the citation entry `("sprk_ruleset", "sprk_sourcedocument")`, is uac-r2's code, and so are its tests (`SecureChildLineageTests`) and the `SecureChildLineage.cs` comment. They are not changed here and go to uac-r2. (Removed in fix round 2, item 10; restored in round 3.)
- `grep ProcedureLibraryContainerId src/server` also finds `RecordContainerResolver.DocumentPointer.cs:65`, a list of configuration keys, not a read by the import.

## Not done yet

- code-review, adr-check and the adversarial pass on the combined 116 diff (`8770b2c659..HEAD`).
- uac-r2: the review of the two routes, and the `RecordOwnershipResolver.cs:1809` citation entry.
- TASK-INDEX.

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

### Still open for part 3

The UI test on dev after 079 ("RuleReview without create and draft") is not run; the forms script has not been run on dev; `-Delete` waits for the owner's OK.

Part 3 was merged into `task/ebilling-116` on 2026-11-10.

## Fix round 1 (2026-11-10, after the adversarial pass and code-review on `68aea7af88`)

Commits: `4b20045ffd` (code and tests), `d0441c0045` (rate-card tests), `b70e038bd5` (forms spec), and this notes and POML commit. Not deployed; no Dataverse call of any kind.

**Owner ruling (to be logged as EB-D-214 by the main session):** "Reset and allow redraft". A new source file on a drafted Draft version makes it draftable again.

| Finding | Fix | Tests (fail-before) |
|---|---|---|
| A1 (F1) replace on a version that stopped being a Draft mid-request | `ReplaceForCallerAsync` takes a `parentStillAccepts` re-check, asked once the new row is Servable and before the earlier rows are withdrawn. False withdraws the new row and refuses `stored_file.parent_no_longer_accepts`; the route answers 409 `procedure_version.not_draft`. The rate-card import passes the same method (`ProcedureService.ResetDraftedStateIfDraftAsync`), maps the refusal through `FileRefused` to the same 409, and its row compensation runs. A fault in the re-check is handled as a failed withdraw. | Store: must-fire, must-not-fire, fault. Route: status flips to Published during the upload. Import: version published during the file write (rows removed, earlier file Servable). Route mapping for the import. All must-fire failed before. |
| A2 (F1) redraft after a replace | `ResetDraftedStateIfDraftAsync`: re-reads the version; when it is a Draft marked Yes, writes `sprk_notconverted` = No, then `sprk_notconvertedlist` cleared; a never-drafted version is not written. Per the coordinator's order change, the route calls it after the rights and state checks and BEFORE anything is staged (a failed reset answers 500 and stores nothing), and again as the A1 re-check once the new file is Servable (which also catches a run that marked the version meanwhile). Job race: `AddDraftsAsync(version, draftedFromFileId, ...)` re-reads the version's one Servable source file before the list and again after the marker. Changed: it removes this run's untouched drafts, writes no list or marker (or takes them back: No first, then the list) and refuses 503 `ruleset.source_changed`, which the handler's existing transient path retries. | Reset order (No then list); never-drafted, marker No, published and no-status versions are not written; draft, replace, draft again (202 path at the service: not drafted, the new file id, no duplicates); race before the list and race after the marker (no marker, no list, drafts removed, 503); file unchanged writes the marker; route: reset before staging and again once Servable; a failed reset stores nothing. |
| A3 (F2) cancellation skipped the rollback | After the new row is Servable, the re-check, the withdraw and the rollback run on `CancellationToken.None`; the catch no longer excludes cancellation. The import observes no token after the file is Servable, so its file outcome and row compensation agree. | Store and import: the client cancels right after the new file is Servable; one Servable file, rows kept. Both failed before. |
| A4 (F1) unfinished list shown | `LatestVersionNotConvertedAsync` selects `sprk_notconverted` and returns the list only under Yes. | Marker No and absent give null (failed before); Yes returns it. |
| A5 (F1) capped, unfiltered prior-row read | `ReadRowsForProcedureVersionAsync` filters to Servable rows (query and row re-check) and throws when the read reports more rows than it returned, before anything is staged. | 105 Removed rows plus one Servable (Dataverse-shaped fake: status condition, TopCount, MoreRecords): the Servable one is withdrawn; 101 Servable rows refuse; the read test now expects Servable only. All failed before. |
| A6 (F1) stale form section | The `sprk_ruleset` form spec has no not-converted section. Script not run. | Pester 5.7.1: 6 pass; the old script fails the new case (1 of 6). |
| F4 / K1 body size | `PutVersionFile` and the rate-card import carry `RequestSizeLimitAttribute(InvoiceSubmissionHttp.MaxRequestBodyBytes)`; each handler lowers the server's cap to the configured limit where it can; a `BadHttpRequestException` with 413 maps to `file_too_large` on both. | A body the server stops at its cap: 413 on both routes (400 before); the route metadata carries the cap (30 MB before, as a mutation). |
| K4 already-drafted before the source check | `GetDraftSourceAsync` answers already-drafted before reading the source files. No conflict with A2: the reset makes a re-filed version undrafted, so this never hides a redraft. The job also benefits: a drafted version with no single file is a no-op, not Poisoned. | Drafted version with 0 and with 2 Servable files: already drafted (422 before). |
| Route answer order (coordinator, after the access-control review) | Both version routes resolve the version under the route's Procedure first (unknown-id 404), then check the caller's own rights on the version as the user (the file store's check, `GetCallerRightsOnParentAsync`): no Read is the same 404, Read without Write 403; then the 409s and the size 413. `/draft` keeps its Procedure and DraftRules() checks; the upload keeps its own check as a backstop. POML criteria refined. | No Read: same status and body as an unknown id, for a non-Draft version and an oversize body, on PUT; same on `/draft`. Read without Write: 403 on both (before the 409). The other-Procedure 404 tests stay. All failed before. |

Fail-before was proved by reverting each fix in place (the new API kept, its behaviour undone) and running the 36 new tests: the 28 must-fire tests failed, the 8 must-not-fire tests passed both ways.

**What a redraft does to each draft of the version (point 4).** The compensation runs on every drafting run, with the current source file's `createdon`:
- Untouched draft of this version (created by the service, never modified, Proposed, Off, no current version, Draft, not in force): removed, as before.
- Draft of this version made from an EARLIER file (policy and every version created by the service before the current file was created), edited, accepted or rejected but still Draft-state, never in force, policy with no current version: removed. This is the new rule; the redraft therefore produces no duplicates.
- Draft a reviewer worked on that was made from the CURRENT file (a retry after a run died): kept, as before; the model's second wording of a clause it covers is listed, not stored.
- Copy-forward draft (its policy has a current version): kept. A published policy or a version in force: never touched.
- A rule another identity created, or a draft of another Procedure Version: kept.
- When the current file's creation time cannot be read, only untouched drafts are removed (reviewed work is kept).

**§11.** The three "Servable source files of a version" reads were not merged: `ProcedureService` and `RuleSetService` do not hold the file store, so the swap needs a new constructor dependency on both (and on every test that builds them), and they need "exactly one", not the rows with their row versions. The remark is on `ReadRowsForProcedureVersionAsync`.

**Known limits (fix round 1).**
- K3 (accepted): a second rate-card import on the same version replaces the first card's workbook as the version's source file.
- A reset is written before the file is staged, so an upload that fails after the reset leaves the version undrafted with its old file (safe; drafting again works).
- Draft age is compared on Dataverse `createdon`; a reviewed draft created in the same second as its replacement file would be kept.
- Two concurrent uploads can still leave two Servable files (unchanged from part 2).

**Verification (fix round 1, HEAD before this notes commit `b70e038bd5`).** BFF build: 0 errors, 0 warnings. Full ArchTests: 2200/2200 (no ledger change). Filtered unit tests (Finance, Procedure, Jobs, StoredFile, RuleSet, RateCard, BillingRuleDraft, Ebilling): 7949 passed, 4 skipped, 0 failed, first run. Pester (forms script, 5.7.1): 6/6. RuleReview not touched (A4 makes the server match `ruleSetApi.ts`'s existing contract), so no Jest run.

## Fix round 2 (2026-11-10, after the re-check and the access-control review of fix round 1)

Not deployed; no Dataverse call of any kind. Commits are listed in the hand-off report.

**What "drafted" means now (replaces the fix round 1 description).** `sprk_notconvertedlist` holds an envelope, `{"draftedFromFileId": "<stored file id>", "notConverted": [...]}`, written by the drafting run (server-internal; the rule set read still returns the bare list, so the client contract is unchanged). A version is drafted when `sprk_notconverted` is Yes AND the envelope's file is the version's one Servable source file. A list without a file id (the bare-list shape written before this round) under Yes is drafted as before. The re-check's clear writes an envelope naming no file, which is never drafted.

| State | `sprk_notconverted` | `sprk_notconvertedlist` | Drafted? |
|---|---|---|---|
| Never drafted | No / empty | empty | no |
| Run died after the list | No / empty | envelope (file F) | no; the retry compensates and overwrites |
| Drafted from F, F still the one Servable file | Yes | envelope (file F) | yes: `/draft` 409, the job is a no-op |
| Drafted from F, F replaced (take-back never landed) | Yes | envelope (file F) | no: redraftable; the rule set read hides the list |
| Replace confirmed (re-check) | No | envelope (no file) | no |
| Earlier shape | Yes | bare list | yes, as before |

| Item | Fix | Tests (fail-before) |
|---|---|---|
| 1 F2 stale marker | The list carries the drafted-from file (`RuleSetService.StoredNotConvertedOf` / `ParseStoredNotConverted`); `IsDraftedAsync` applies the rule above in the draft target (`LoadDraftTargetAsync`), the draft source (`GetDraftSourceAsync`, so `/draft`'s 409 and the job's redelivery check) and the rule set read (A4). The marker is written on `CancellationToken.None`; a fault in the re-read after it is handled as changed (marker taken back on `None`, 503 `ruleset.source_changed`). | Fault points: the re-read after the marker throws; the take-back fails; the host stops after the marker. Each ends redraftable and the retry drafts the new file and is marked for it. The marker lands although the run was cancelled after its list. A cleared envelope under Yes is not drafted. Must-not-fire: a bare list under Yes is drafted as before; the run writes the envelope and the read returns the bare list. 5 must-fire failed before (the stale-Yes rule, the `None` marker and the fault handling reverted in place). |
| 2 R1 /draft and Delete | `BillingAdminAuthorization.DraftProcedureVersion` also asks the caller's `prvDeletesprk_Policy` and `prvDeletesprk_PolicyVersion` (same probe, as the caller), right after the Create checks. POML criterion refined. | Without either Delete: 403, nothing read, removed or queued (two theory cases). With both: 202 and the four questions asked in order. 3 failed before. |
| 3 R2 validate before the reset | `StoredFileService.PrecheckUpload` (the row's name and type rules with the server-derived media type, the size column, a configured store; no read or write). `PutVersionFile` calls it after the size checks and before the reset; its refusals map as the store's (400, 413, 503). | A name the store refuses (`folder/`): 400 and the reset never called. An unconfigured store: 503 and the reset never called. Both failed before (the reset ran first). |
| 4 First reset keeps the list | `ResetDraftedStateIfDraftAsync` writes only the marker No. `RecheckDraftAfterNewFileAsync` (the `parentStillAccepts` re-check of both the version route and the rate-card import) writes No if needed and clears the list only when the version is still a Draft. | The first reset writes only No. A version published between the reset and the re-check keeps its list; the re-check refuses and writes nothing. The re-check of a still-Draft version writes No, then the cleared envelope. 2 failed before. |
| 5 Handler wiring | Tests only. | The run hands the source file id to `RemoveUntouchedDraftsAsync` and `AddDraftsAsync` (failed with `Guid.Empty` at the two calls). A 503 `ruleset.source_changed` on a non-final delivery is `Failed` (retried), not `Poisoned` (failed with the transient threshold moved above 503). 2 failed before. |
| 6 Timing parity | Both version routes ask the caller's rights on the version (the id as given) before they read the version, and act on the 404 after, like the other callers of the equalized probe. | An unknown version on PUT `/file` and on `/draft`: one rights question each, then 404. Failed before (no question for an unknown id). |
| 7 Flip on `None` | `FlipToServableAsync`'s status update runs on `CancellationToken.None`. | The client cancels as the flip commits: the replace still withdraws the earlier file, one Servable file. Failed before (the cancelled flip threw and left two Servable files). |
| 8 Superseded discriminator | `sprk_storedfile` records no time it became Servable (its columns are `createdon` and `versionnumber`; `modifiedon` moves on every later status write), so nothing to compare against without a new column. Left as a known limit (below). | None (no change). |
| 9 Rate-card import order | Before: a caller with the table privileges and Write on the version's Procedure but no Read or Write on the version was answered by the workbook validation, the firm check, the roster, the owner and the row writes before the first version-rights question, which was inside the file store's upload. Now `RequireWriteOnTheVersionsProcedure` asks the caller's rights on the version as the user for every id, then reads the version's Procedure: an unknown version, one with no Procedure and one the caller cannot read are the same 404 `procedure.version_not_found`; Read without Write 403; then the Procedure Write check and the handler. | Unknown and unreadable versions: same status and body, both asked. Read without Write: 403, the service never reached. Must-not-fire: a readable version the service refuses still gets its 400. 2 failed before. Three existing tests were adjusted to the test host's new admin default (the version now has a Procedure the admin can write): the two "unknown rights" tests clear that grant before faulting it, and the publish "version with no Procedure" test names an unknown version. |
| 10 Citation clean-up (restored in round 3) | `("sprk_ruleset", "sprk_sourcedocument")` removed from `RecordOwnershipResolver.CitationLookups`; the `SecureChildLineage` comment and the legacy-set test cases removed; `ParentLineage.cs` untouched. | The citation set has no rule set entry (failed before). |
| 11 | Notes only (below). | — |

Fail-before was proved by putting the previous code back in place (or reverting the single change for the handler and the marker) and running the new tests: 11 failed in the first group (items 1, 2, 4, 7) and 8 in the second (items 3, 5, 6, 9, 10); the must-not-fire tests passed both ways.

**Known limits (fix round 2).**
- Item 3 residual: the store can still fault after the reset (a staging or Dataverse fault). The version is then undrafted with its old file; drafting again works.
- Item 8: the superseded-draft rule compares against the current file's `createdon`, not the time it became Servable. A file created before a draft but made Servable after it would make that draft read as made from the current file and be kept. No column records the Servable time; adding one is out of scope.
- Item 11: a `source_changed` retry that lands while the version briefly holds two Servable files (between a replace's flip and its withdraw of the earlier file) gets 422 `ruleset.source_missing` and is Poisoned. The admin can trigger drafting again.
- `/draft` now needs Delete on the two policy tables; a caller without it is refused 403. Whether the shipped security configuration grants it is an owner item (not changed here).
- `grep sprk_sourcedocument src/server tests` still finds non-Stored-File uses outside this task's code: `RecordRepairModeTests.cs:1184` and `:1620` (a matter field in a repair fixture) and a guard fixture string in `SubmissionSourceEmailLinkNotFilingGuardTests.cs:272`. They are not this task's and are left as they are.
- The route ledger's prose for `/draft` (`RouteAuthorizationGuardTests.Ledger.cs`) still lists DraftRules() only; it was not changed (the ledger follows moved code only). The main session decides whether to add the two Delete checks there.

**Verification (fix round 2, `f4c3b538cc`).** BFF build: 0 errors, 0 warnings. Full ArchTests: 2200/2200 (no ledger change). Filtered unit tests (Finance, Procedure, Jobs, StoredFile, RuleSet, RateCard, BillingRuleDraft, Ebilling, SecureChildLineage, RecordOwnership): 9452 passed, 4 skipped, 0 failed, first run. RuleReview not touched (the rule set read still returns the bare list), so no Jest run.

## Fix round 3 (2026-11-10, after the scoped re-check of fix round 2)

Not deployed; no Dataverse call of any kind. Out of scope and unchanged: how a redraft treats the drafts it supersedes (the removal, `IsSupersededDraft`, the Delete checks on `/draft`); that is an open owner decision.

**What "drafted" means now (replaces the fix round 2 table).** Unchanged rule: Yes AND the envelope's file is the version's one Servable source file; a list with no file id under Yes is drafted. Changed writers: the first reset of PUT `/file` writes No only for a list with no file id (item 2), and the drafting run's take-back writes the cleared envelope instead of an empty column (item 4), so no write since round 2 produces a list with no file id under Yes.

| State | `sprk_notconverted` | `sprk_notconvertedlist` | Drafted? |
|---|---|---|---|
| Never drafted | No / empty | empty | no |
| Run died after the list | No / empty | envelope (file F) | no; the retry compensates and overwrites |
| Drafted from F, F still the one Servable file | Yes | envelope (file F) | yes: `/draft` 409, the job is a no-op |
| Drafted from F, first reset ran, upload never landed (published meanwhile, refused, store fault) | Yes | envelope (file F) | yes, list shown: the first reset writes nothing for an envelope |
| Drafted from F, F replaced (re-check never landed) | Yes | envelope (file F) | no: redraftable; the rule set read hides the list |
| Replace confirmed (re-check) | No | cleared envelope (no file) | no |
| Run's take-back after its file changed | No | cleared envelope (no file) | no |
| Take-back, then an overlapping run's marker lands | Yes | cleared envelope (no file) | no: redraftable (one extra run, compensated) |
| Earlier shape | Yes | bare list | yes, as before |
| Earlier shape, first reset ran | No | bare list | no (list hidden); the first reset still writes No for a list with no file id |

| Item | Fix | Tests (fail-before) |
|---|---|---|
| 1 F4 citation removed too early | `("sprk_ruleset", "sprk_sourcedocument")` is back in `RecordOwnershipResolver.CitationLookups`, with a comment: the entry is removed in the same change as `Remove-EbillingSourceDocumentColumns.ps1 -Delete`, not before (the column exists and rows can hold it until then). The `SecureChildLineage` comment and the legacy-set test cases round 2 removed are restored; round 2's "no rule set citation" reflection pin is dropped (inverted it would repeat the restored `IsCitation` assertion). **Removing this entry is part of the column's `-Delete` change.** | The legacy set with only `sprk_sourcedocument` has no parent; with a matter it is filed under the matter only. 1 failed before (round 2's resolver: the set was filed under the document). |
| 2 F1 first reset hides "drafted" | `ResetDraftedStateIfDraftAsync` (the first reset) writes No only when the stored list names no file (bare list or no list); for an envelope it writes nothing. The re-check once the new file is Servable is unchanged. `StoredFileService.PrecheckUploadAsync` asks `CanStageAsync` (configured and the environment confirmed), so a store that cannot stage is refused 503 before the reset; a fault of that check is 500 and nothing changes. | Scenario A (drafted from F, reset, G Servable, publish, re-check refuses, G withdrawn): drafted, list shown. Scenario B (upload refused after the reset): drafted, list shown. `Recheck_AVersionPublishedAfterTheFirstReset_KeepsItsList` now seeds an envelope and asserts `GetAsync` shows the list. The first reset of an envelope writes nothing. Route: a store whose environment check fails is 503 before the reset; a faulted check is 500 before the reset; must-not-fire: a confirmed environment stores the file. 6 failed before; the must-not-fire passed both ways. |
| 3 F4 `/draft` timing | The no-Read 404 from the rights probe (which answers no rights for an unknown id too) is returned before `GetDraftSourceAsync`. Read without Write (403) and a probe fault still wait for the version read, so a version under another Procedure answers 404 as before. | The handler over the real rule set service and file store: an unknown id and an unreadable existing version give the same status and body and the same reads in number and order (rights questions, Dataverse reads, file-store calls). Must-not-fire: Read without Write on another Procedure's version still reads the version and answers 404. 1 failed before (1 read against 4). |
| 4 K2 take-back writes an empty list | `SourceChangedAsync` writes `ClearedNotConverted` instead of `DBNull`. | Overlapping runs (run 1 writes its list, run 2's take-back writes, then run 1's marker lands): the list is the cleared envelope, not drafted, and not drafted for a later file either. The existing take-back test now expects the cleared envelope. 2 failed before. |

Fail-before was proved per item by putting that item's previous behaviour back in place (item 1: the round-2 resolver; item 2: the unconditional marker write and the `IsConfigured`-only precheck; item 3: no early return; item 4: the round-2 `RuleSetService`), running the item's tests, then restoring.

**Known limits (fix round 3).**
- Item 3: an existing version whose Procedure row is missing, asked by a caller with no Read, now answers the version 404 (`procedure.version_not_found`) instead of the Procedure 404 the version read gave. Both are 404; the new body is the unknown-id body, which is the point of the item.
- Item 2: an earlier-shape version (bare list, no file id) still has its marker written No by the first reset, so an upload that never lands leaves it undrafted with its list hidden (safe; drafting again works). New runs always write an envelope.
- Item 4: a Yes over the cleared envelope after overlapping runs reads as not drafted although a run did draft the current file; `/draft` then runs once more and its compensation removes the earlier run's untouched drafts.
- Round 2's "Item 3 residual" now covers only faults the precheck cannot see (a Dataverse fault after the reset); for an envelope version the reset writes nothing, so it is unaffected.

**Verification (fix round 3, `0854ddff14`).** BFF build: 0 errors, 0 warnings. Full ArchTests: 2200/2200 (no ledger change). Filtered unit tests (Finance, Procedure, Jobs, StoredFile, RuleSet, RateCard, BillingRuleDraft, Ebilling, SecureChildLineage, RecordOwnership): 9461 passed, 4 skipped, 0 failed, first run (9452 + 9 new). RuleReview not touched (the rule set read still returns the bare list), so no Jest run.

## Fix round 4 (EB-D-216, 2026-11-10): a redraft deactivates the drafts it supersedes

Branch `task/ebilling-116-c` from `cf323d251f`. Not deployed; no Dataverse call of any kind. Owner ruling EB-D-216 under uac-r2's five conditions, plus round 3's re-check K1 and K2 and the peer reviewer's two added checks.

**The draft-state table above is unchanged:** round 4 changes no write of `sprk_notconverted` or `sprk_notconvertedlist`, and EB-D-214 stands (a replacement upload still resets a drafted version; only the supersede step changed). What changed is the fate of the draft ROWS:

| Draft row of the version | Before round 4 | Now |
|---|---|---|
| Untouched or reviewed draft made from an earlier file, at `/draft` | deleted by the job | deactivated in the `/draft` request (`statecode` 1, `statuscode` 2), after the caller's per-row Write checks |
| Untouched draft of a run that died (same file), at the job's retry | deleted (075) | deleted (075, unchanged: the job's own never-modified output; a deactivated row is never read, so never deleted) |
| Reviewed draft of an earlier file still active when the job runs (no request deactivated it) | deleted by the job | job refuses 409 `ruleset.superseded_drafts_remain` (Poisoned), nothing removed; the admin runs `/draft` |
| Copy-forward draft, published version, another version's draft | kept | kept, never planned, never asked about |
| Deactivated draft | n/a | invisible to every draft reader; asked for by id: 409 `ruleset.draft_inactive`; a citation still resolves it by id |

`sprk_policyversion` and `sprk_policy` (SpaarkeMaster `Entity.xml`): `statecode` 0 Active / 1 Inactive; `statuscode` 1 Active / 2 Inactive (and `sprk_policy` 100000000 Retired, under Inactive). Deactivation writes 1 / 2 on both.

| Condition | Before → after | Tests (fail-before) |
|---|---|---|
| 1 per-row Write, all or nothing | `/draft` asked table Delete on both policy tables (round 2) and the job deleted the superseded drafts. Now `/draft` asks no Delete; after the source checks it plans the rows (`RuleSetService.PlanRedraftAsync`: exactly the set the delete path removed, untouched plus superseded, whole rules: every version and its policy), asks the caller's own Write on every one of them as the user in one question (`StoredFileService.GetCallerRightsOnRowsOrUnknownAsync`, the probe the route already asks the version through), and only then deactivates as the application. Any row without Write: 403 `insufficient_rights`; any unanswered row or a fault: 503 `ruleset.redraft_unverified`; nothing written, no job. Create checks unchanged. A first draft plans nothing and asks nothing. | Deactivates every superseded row, deletes nothing, asks each row as the user; no Write on the second of several rows: 403, nothing at all written; an unanswered row: 503; no Delete privilege still 202. Must-not-fire: a first draft asks only the version; copy-forward and published rules are neither asked nor touched. 4 failed before (route step removed; the Delete checks put back). |
| 2 only drafts still in Draft | `DeactivateSupersededDraftsAsync` re-reads every planned row (no `statecode` filter, so an already-inactive row is skipped), checks the rule is still what the plan saw (no current version, exactly the planned versions, each active one a Draft of this Procedure Version, never in force), and writes NOTHING until every row passed; then one transaction deactivates every version and then its policy, each conditioned on the row version just read. A publish in between, before or after the re-read, is 409 `ruleset.draft_changed` with nothing written (no row of an earlier rule stays deactivated: the transaction rolls back). A published version is never deactivated. | Published after the plan: 409, nothing deactivated; published between the re-read and the write: the transaction rolls back, 409, the published version still active; the same through the route, no job. 3 failed before (re-check and row-version condition removed). |
| 3 every draft reader excludes inactive drafts | Draft readers read policies through `PolicyQuery` (already `statecode` 0) and now also name `statecode` 0 on their version reads: `LoadRulesAsync` (rule set GET, edit, decide, publish plan, copy-forward), `LoadDraftedFingerprintsAsync` (the run's repeat check), `FindDraftsAsync` (plan and job compensation). `ListAsync` counts through `PolicyQuery`. An edit, accept or reject of a deactivated draft by id: 409 `ruleset.draft_inactive` (was 404). Publish (`PlanProcedureVersionPublishAsync`) never offers one; it publishes a version, so there is no by-id publish of a single draft: the by-id refusal is the decide/edit path. Client contract unchanged (no RuleReview change, no Jest run). Guard: `DraftPolicyVersionStatecodeGuardTests`. | A deactivated draft is not in the rule set read, not in the publish plan, refused 409 by id, not re-planned; the next run stores a rule equal to a deactivated one; the job refuses a reviewed superseded draft no request deactivated. 2 failed before (by-id 409, the policy read's `statecode` and the job's refusal removed); the run test failed with the route step removed. Guard: fires when `LoadRulesAsync`'s `statecode` is removed. |
| 4 citations still resolve | Nothing filters a by-id read of a cited version on `statecode`: `DecisionRecordWriter` (the version-exists check, by id), `ProcedureResolver` and `BillingFactLoader` (Published only; never deactivated), `RuleSetService.GetFirmClauseAsync` (the version by id; its policy read goes through `PolicyQuery`, but it serves only versions an invoice applied, which are published and never deactivated). The guard exempts by-id reads by rule. | Guard must-not-fire control (a by-id read). |
| 5 no role change | `RuleSetRolePrivilegeGuardTests` unchanged; no role file touched. | — |

**Guard (`tests/Spaarke.ArchTests/DraftPolicyVersionStatecodeGuardTests.cs`).** Scope `Services/Finance/**` and `Api/Finance/**`. Every `new QueryExpression(...)` over `sprk_policyversion` must carry a condition (`AddCondition` / `ConditionExpression`) on `"statecode"` before it is sent, unless it is a by-id read or a Published-only read (each also a condition: naming the id or `statecode` only in a column set or an order does not count), or annotated `// all versions: <reason>`. Billing decides "draft" in memory, not in the query, so no query filters on the Draft status: a by-policy or by-Procedure-Version read returns drafts. Annotated: `NextVersionNumberAsync`, `VersionNumberExistsAsync` (numbering and the key need inactive rows), the deactivation's own re-read, and `ProcedureAssignmentService`'s record-rule version read. Controls: must-fire (a by-policy read without `statecode`, a literal-table read by Procedure Version, `statecode` named only after the send, `statecode` and the id named only in a column set and an order); must-not-fire (`statecode` named, by id, Published only, the `ReadVersionsAsync` shape annotated, `ProcedureService`'s `VersionEntity` which is `sprk_procedureversion`); Ontology and Signals out of scope. No violation in scope, so no ratchet baseline. No draft-status query exists outside Finance and Procedures.

**K1 (round 3 re-check).** A FAULT of `/draft`'s version rights probe now answers 403 `system_failure` at once, before the version is read (`ProcedureEndpoints.CallerVersionRightsAsync`); only Read without Write still reads the version first. Test: unknown id and existing version under a faulting probe give the same status, body and reads (none). Failed before (1).

**K2.** The two "removed in fix round 2" notes now say it was restored in round 3.

**Route ledger.** `RouteAuthorizationGuardTests.Ledger.cs`: the `/draft` entry names the per-row Write checks and "no Delete check" (round 2 never added Delete there).

**DEF-060.** `notes/def-060-firm-description-design.md` is on `work/ebilling-r1`, not on this branch; the line to add there: "Draft source selection (the replay and the mapper source) must exclude inactive drafts (`statecode` 0 on both policy and version reads; EB-D-216)."

**Known limits (fix round 4).**
- The job's retry compensation still deletes its OWN untouched drafts of a run that died (075). These are never-modified rows the application created in the same request; a deactivated row is never read there, so never deleted. Say if they should be deactivated too.
- A queue failure after the deactivation (503 `ruleset.draft_not_queued`) leaves the earlier drafts deactivated and no new run; `/draft` again plans nothing and queues (safe).
- A job that drafts a file nobody asked to draft (a `source_changed` retry after an upload) with a reviewed draft of the earlier file still active refuses 409 and is Poisoned; the administrator runs `/draft`.
- Rules are deactivated whole (policy and every version); a rule with any other version (a copy-forward chain) is never planned, as before.

**Verification (fix round 4).** See the hand-off report for the numbers of this round's run.
