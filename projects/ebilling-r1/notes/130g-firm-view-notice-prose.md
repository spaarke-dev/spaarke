# Task 130g: the firm outcome carries the reviewer-confirmed notice prose (EB-D-259)

Branch `task/ebilling-130g`, from `ff496f2aa0` (`work/ebilling-r1`). Rigor FULL. Model: Sonnet @ high. BFF only: no schema change, no SPA change, no `.claude/` edit, no deploy, no push to `work/ebilling-r1` or master.

## What changed

| Path | Change |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Finance/FirmFindingsProjection.cs` | `FirmOutcome` gets one optional member, `NoticeProse` (`JsonIgnore WhenWritingNull`, so the JSON key is `noticeProse` and is absent when null). `GetForInvoiceAsync` fills it for `Rejected` and `ReturnedToFirm` only, through the new `ReadNoticeProseAsync` and the pure `ScreenStoredNotice`. Doc comments updated (record parameter, class remarks). |
| `tests/integration/tenant/Finance/FirmOutcomeNoticeProseTests.cs` | New (KEEP path `tenant`): the projection over the real class and 130's Dataverse double, plus the composer echo proof. |
| `tests/integration/contract/Api/ExternalAccess/FirmOutcomeScopeTests.NoticeProse.cs` | New (KEEP path `contract`): the route shape. A partial of the 130b route rig (`FirmOutcomeScopeTests` became `partial`; one word changed in `tests/integration/auth/Finance/FirmOutcomeScopeTests.cs`). |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/FirmNoticeComposerTests.cs` | `TheNoticeConsumes_OnlyTheAllowListFieldsOfTheFirmOutcome` is the review gate for a new `FirmOutcome` member; `NoticeProse` was added to its list with the reason (the notice does not consume it, proved by AC6). |
| `tests/Spaarke.ArchTests/ExternalInvoiceVisibilityGuardTests.cs` | The (a') guard flagged `FirmFindingsProjection::ReadNoticeProseAsync` as a new invoice-family reader; it is listed in `NotInvoiceSurfaces` with its reason, the same way the projection's other private readers are. |

No new route, service, DI registration, package, Dataverse column or file surface (root section 11): the only new surface is one member on an existing record, read by the projection that already reads the invoice's own rows.

- **Existing:** `FirmFindingsProjection` (the one firm-safe projection), `InvoiceBlockedHold.ReadOwnSubmissionAsync` (the one own-submission rule), `FirmNoticeProse.Check` (the one screen), `EbillingNotificationJobHandler.FirmNoticeTextColumn` (the one column name).
- **Extension:** extended all four; nothing is forked or re-derived.
- **Cost of doing nothing:** task 134's firm page has no firm-safe reason to show for a Rejected or Returned invoice.

## How it works

`GetForInvoiceAsync`, after the items are built (so a fault there still returns the 130f status-only Degraded outcome and never reads the notice), for `Rejected` and `ReturnedToFirm` only:

1. `InvoiceBlockedHold.ReadOwnSubmissionAsync(_entities, invoiceId, [FirmNoticeTextColumn])`: app-only, the column alone, the submission whose id derives the invoice's. It queries the submissions linked to the invoice (with the notice column in the ColumnSet) and keeps only its OWN (id-derived) one; no other row's text leaves the method.
2. `ScreenStoredNotice(stored, expectedKind)`, pure: JSON object, `v` is the number `EbillingNotificationJobHandler.SentNoticeVersion` (the writer's own constant, so the two cannot drift), `kind` is exactly `InvoiceRejected` for a Rejected invoice or `ReturnedToFirm` for a Returned one, `prose` is a string, and `FirmNoticeProse.Check` passes it on this read. Any other shape gives null with a stable reason (`malformed`, `unsupported-version`, `kind-mismatch`, `submission-notice`, `no-prose`, `refused:<code>`). Ill-formed text (a lone surrogate: `ArgumentException` from the parse, `InvalidOperationException` from `GetString`) is `malformed` like bad JSON.
3. Only `prose` is copied out. `subject`, `html`, `htmlOmitted` and `source` are never read into the outcome.

**Screen call.** `FirmNoticeProse.Check(subject, body)` refuses a blank subject. Only the prose is shown, so it is screened beside the fixed neutral subject `"Invoice outcome"`. The stored subject (which ends in the tracking number, a figure) is never read into the screen, so a good prose is not refused because of the subject, and nothing of the subject can reach the firm. `FirmNoticeProse` itself is not changed.

**Source is not filtered.** A stored notice from the Reviewer, the Action or the Template is shown when it passes the screen. Each was screened (or is fixed text) before the email was sent, the firm already received it, and the AC does not name a source. If the owner wants Reviewer-only, it is a one-line `source` check in `ScreenStoredNotice`.

**Logging.** A not-shown notice logs `EBILLING-FIRM-OUTCOME-NOTICE-PROSE-NOT-SHOWN` with the invoice id, the submission id and the reason code; never the text. "No own submission", "nothing stored" and `submission-notice` (a stored `SubmissionRejected` notice on a Rejected or Returned invoice's own submission, left by an automatic rejection or by a held submission later decided without a confirmed notice: routine, on every read) are Information; malformed, version, any other kind mismatch and a refusal are Warnings.

## The fail-safe choice

**A fault reading the stored text omits only `noticeProse` and keeps the rest of the outcome (items, totals, kind), with a Warning carrying the invoice id and the exception type.** The outcome is not marked Degraded and the route stays 200, not 503. Reasons: the items are already firm-safe and complete; the notice is supplementary; and Degraded would make the route answer 503 and hide the whole decided outcome from the firm for a field it can live without. No partial or unscreened text is possible: the text is either screened in full or absent. A cancelled request is never swallowed (`IsFault` rethrows it; tested).

## Callers checked

- `FirmNoticeComposer` renders only items, tracking number, totals and kind (`RenderHtml`, `BuildRequest`); it does not read `NoticeProse`. Proved through the REAL projection: a Return or Reject notice composed after the decision (template, model, and reviewer-confirmed) contains neither the stored prose nor its marker (AC6).
- `EbillingFeedSource` reads `Kind`, `Degraded` and `Unavailable` only, and only reports `ApprovedWithAdjustments`, which never carries the field. No change.
- `InvoiceApprovalEndpoints` does not use `FirmOutcome` (its `AssignFirmOutcome` is a different type). No change.
- `FirmFindingsProjectionCallerBoundaryGuardTests` (closed caller list) is unchanged and passes: no new caller.
- Both routes carry the field (fix round 1, N2): the invoice route, and `GET /api/v1/external/ebilling/submissions/{id}/outcome` for an Accepted submission whose invoice is decided (`GetForSubmissionAsync` returns `GetForInvoiceAsync` of the derived invoice). Intended; pinned by two contract cases on the submission route (key present for a Rejected or Returned invoice with a matching stored notice; key absent when the invoice is not decided). The same note is on the `FirmOutcome.NoticeProse` doc comment.
- NFR-10 / D-70: the field is read after the route's filter admitted the invoice, in the same call as the items; no new route or identity. A refused request reads nothing (route test).

## Acceptance criterion to test map

| AC | Tests |
|---|---|
| 1 Rejected and Returned project the prose | `FirmOutcomeNoticeProseTests.RejectedOrReturned_WithItsMatchingStoredNotice_ProjectsThePlainProse` (2 cases); `TheStoredSource_IsNotRead_AndNeverExposed` (3); `ANoticeOnAnotherSubmissionOfTheInvoice_IsNeverShown` |
| 2 must-not-fire: other outcomes, kind mismatch | `OtherOutcomes_NeverCarryTheProse` (In Review, Approved, Approved with adjustments, Sent to AP, Paid; also asserts the column is not read); `ARejectedSubmission_NeverCarriesTheProse` (SubmissionRejected); `AKindMismatch_GivesNoProse` (6 cases) |
| 3 screen refusal, malformed, version, blank, missing | `ProseTheScreenRefuses_GivesNoProse_AndTheOutcomeStands` (10: digit, `$`, euro sign, amount word, 2 links, address, bare domain, numbered reference, control character); `ProseOverTheCap_GivesNoProse`; `AnUnreadableStoredNotice_GivesNoProse_AndTheOutcomeStands` (16); `TheMinimalValidObject_IsShown` (control); `NoStoredNotice_GivesNoProse` (no own submission, blank, whitespace) |
| 4 nothing else of the stored notice, nothing internal | `TheSerializedOutcome_CarriesOnlyTheProse_OfTheStoredNotice` (subject and html markers, decision reason and note; the allow-listed key set); `TheSubmissionRead_AsksForTheNoticeColumnOnly`; route: `Invoice_RejectedOrReturned_SerializesTheNoticeProse` |
| 5 a read fault | `AFaultReadingTheNotice_OmitsOnlyTheProse` (the outcome equals the no-notice outcome; Warning has the id and no text); `ACancellationWhileReadingTheNotice_Propagates`; route: `Invoice_ANoticeReadFault_Is200WithoutTheKey` |
| 6 composer does not echo | `TheComposer_NeverEchoesTheStoredProse` (Reject and Return, AI on and off, drafted and confirmed); `FirmNoticeComposerTests.TheNoticeConsumes_OnlyTheAllowListFieldsOfTheFirmOutcome` |
| 7 route contract | `FirmOutcomeScopeTests.NoticeProse`: `Invoice_RejectedOrReturned_SerializesTheNoticeProse`, `Invoice_WithNoProseToShow_OmitsTheKey`, `Invoice_ARefusedNotice_IsOmitted`, `Invoice_NotDecided_OmitsTheKey`, `Invoice_ANoticeReadFault_Is200WithoutTheKey`, `Invoice_OfAnotherFirm_IsRefused_AndNoNoticeIsRead` |
| 8 filtered suites, ArchTests, `-warnaserror` build | results below |

## Before and after

Behaviour seeded off, same tests (the member exists, the read is disabled with `if (false && ...)`; restored after):

- **AC1:** both `RejectedOrReturned_WithItsMatchingStoredNotice_ProjectsThePlainProse` cases, the 3 source cases, the composer control, and both route serialization cases FAIL before (no prose); pass after.
- **AC3:** with only the screen disabled (`refusal = null`), 14 tests fail: all 10 refused-prose cases, the over-cap case, the 2 blank-prose cases of the unreadable-notice test (whitespace and empty) and the route's refused-notice case; they pass with the screen.
- With the read disabled, 38 of the 169 tests in the two new files and the route rig fail; with the real code, 169 of 169 pass. (The refusal cases also fail before through their log assertions, which pin the reason code and the absence of the text.)

## Results

- `dotnet build src/server/api/Sprk.Bff.Api/ -warnaserror`: 0 warnings, 0 errors.
- Filtered `Sprk.Bff.Api.Tests` (`FirmFindings|FirmNotice|FirmOutcome|ExternalInvoice|EbillingFeed|EbillingNotification`): 1205 passed, 0 failed. (First run had 1 failure, `TheNoticeConsumes_OnlyTheAllowListFieldsOfTheFirmOutcome`, the intended gate on a new member; fixed as above.)
- Filtered `Spaarke.ArchTests` (`Route|Finance|External|FirmNotice|FirmFindings|InvoiceMoney|FieldSecur`): 629 passed, 0 failed. (First run had 1 failure, the task 098 (a') guard listing the new private reader; listed with its reason.)
- No guard lists readers of `sprk_firmnoticetext` as such: `FirmNoticeTextFieldSecuredGuardTests` checks the field-security declaration only (unchanged and passing), and the `FindingsJson`/`InvoiceFirmHold` app-only guards are about other columns.
- Packages: none added or changed, so no CVE delta (`dotnet list package --vulnerable` not re-run).
- Publish size (BFF hygiene, root section 10): see below.

### Publish size (BFF hygiene section 10 and NFR-01)

Convention: `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/` (framework-dependent, default RID linux-x64 from the project), zipped with `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`, PDBs INCLUDED (4.81 MiB raw), each tree published from a fresh worktree at a short path. MB below is MiB (PowerShell `1MB`).

| Tree | Commit | Files | Raw | Zip (bytes) | Zip |
|---|---|---|---|---|---|
| fresh master (`C:\wt130m`) | `02bb05ba94` | 191 | 134.57 MiB | 42,575,596 | 40.60 MiB |
| base, `work/ebilling-r1` (`C:\wt130a`) | `ff496f2aa0` | 191 | 134.52 MiB | 42,562,080 | 40.59 MiB |
| this branch (`C:\wt130b`) | `31e3fffe0e` | 191 | 134.52 MiB | 42,563,005 | 40.59 MiB |

- **Delta against fresh master: -12,591 bytes (-0.012 MiB).** The branch is smaller than master because `work/ebilling-r1` carries other differences from master; that is not this task.
- **This task's own delta (branch against its base): +925 bytes.**
- File counts match (191 each), so the publishes are complete and comparable. Far under the +5 MB trigger, the 55 MB review line and the 60 MB hard stop. No package added or changed, so no CVE delta.
- The three temporary worktrees were removed with `Remove-WorktreeSafe.ps1`.

## Known limits

- **Timing.** The job writes the stored notice after it sends the email, after the decision. Until then the invoice is Rejected or Returned with `noticeProse` absent; a later read shows it. The page (task 134) must treat the field as optional, as the contract says.
- **No stored notice, no prose.** A Return or Reject decided without a notice (a path that skips the email, a failed store: the job only logs a Warning) shows no prose; there is no template substitute on the outcome. The FirmNotice template exists only in the email.
- **A tightened screen later hides a previously good notice.** That is the intent of re-screening; the Warning code names it.
- **One extra Dataverse read** per Rejected or Returned outcome read (including the notification job's own compose, which does not use the field).
- **Source is shown as is** (Reviewer, Action, Template); a Reviewer-only rule would be a one-line change.
- The SPA does not render the field until task 134.

## Review: fix round 1

### Fixed

| Item | Finding | Fix | Proof |
|---|---|---|---|
| F2-a | The stored shape is defined twice (the writer `EbillingNotificationJobHandler.SentNoticeJson`, the reader `FirmFindingsProjection.ScreenStoredNotice`) and no test pushed the writer's real output through the reader. | New `EbillingNotificationJobHandler.SentNoticeVersion` (internal const) beside `SentNoticeJson`; the writer and the reader both use it (the reader's own `StoredNoticeVersion` is gone). Tenant tests seed the own submission with `SentNoticeJson(<confirmed Reviewer draft>)` built through `FirmNoticeComposer.ComposeConfirmedForInvoiceAsync`: InvoiceRejected, ReturnedToFirm, and the html-omitted variant (4 cases). | Mutation: renaming the writer's `prose` key to `proseX` fails all 4; writing `"v" = 2` as a literal fails all 4; both restored. (Bumping the constant itself passes, by design: both sides move together.) |
| F2-b | A routine kind mismatch was a Warning on every read: an automatic rejection, or a held submission later decided without a confirmed notice, stores a `SubmissionRejected` notice on the invoice's own submission. | `ScreenStoredNotice` answers reason `submission-notice` for a stored `SubmissionRejected` (only Rejected and Returned invoices are read, so it is always the routine case); `ReadNoticeProseAsync` logs that reason at Information. Every other not-shown reason stays a Warning. No prose in both cases. | `ASubmissionNotice_IsRoutine_InformationNotWarning` (2) fails before, passes after; `AnyOtherMismatch_StaysAWarning` is the control. The old `AKindMismatch_GivesNoProse` case for `SubmissionRejected` (which pinned the Warning) is removed. |
| K1 | `ScreenStoredNotice` caught only `JsonException`: a lone surrogate throws `ArgumentException` from `JsonDocument.Parse` and `InvalidOperationException` from `GetString()` (escaped `\ud800`), outside the read's try block (a 500 on the route). | Catches `JsonException or InvalidOperationException or ArgumentException` and returns `malformed`. | 3 stored texts (escaped in the prose, escaped in the kind, raw in the text), through the projection and through the pure screen: 6 cases fail before, pass after. |
| N1 | Wording: other linked submissions were described as "never read". | `ReadOwnSubmissionAsync` queries every submission linked to the invoice, with the notice column in its ColumnSet, and keeps the own (id-derived) one in memory. Reworded in the `ReadNoticeProseAsync` comment, the `NotInvoiceSurfaces` reason in `ExternalInvoiceVisibilityGuardTests`, and this note: "queries the submissions linked to the invoice and keeps only its OWN (id-derived) one; no other row's text leaves the method". | Comment and text only. |
| N2 | The submission route also returns `noticeProse` (an Accepted submission with a decided invoice returns that invoice's outcome). Intended, but unpinned and undocumented. | Two contract cases on `GET /api/v1/external/ebilling/submissions/{id}/outcome`: key present for a Rejected or Returned invoice with a matching stored notice; key absent when the invoice is not decided. One line under "Callers checked" and on the `FirmOutcome.NoticeProse` doc comment. | The route cases are pins of existing behaviour: they pass before and after. |

### Known limits (no code change)

| Finding | Class | Decision |
|---|---|---|
| HTML-shaped prose passes the screen (for example `&lt;script&gt;`). | K | Safe today: the value is a JSON string, and the firm already received the same text by email. Task 134 MUST render `noticeProse` as plain text only, never with `dangerouslySetInnerHTML`. |
| The job's `ComposeConfirmedForInvoiceAsync` does a notice read whose result the composer discards, and `ReadOwnSubmissionAsync` pulls the notice column for every linked submission. | K3 | One query per email or outcome read; accepted. |
| The stored notice's `source` is not filtered. | K | For InvoiceRejected and ReturnedToFirm the job stores only Reviewer notices, so there is no real path to another source. Accepted. |

### Results (fix round 1)

- `dotnet build src/server/api/Sprk.Bff.Api/ -warnaserror`: 0 warnings, 0 errors.
- Filtered `Sprk.Bff.Api.Tests` (`FirmFindings|FirmNotice|FirmOutcome|ExternalInvoice|EbillingFeed|EbillingNotification`): 1221 passed, 0 failed (was 1205).
- Filtered `Spaarke.ArchTests` (`Route|Finance|External|FirmNotice|FirmFindings|FieldSecur|OwnSubmission`): 638 passed, 0 failed.
- Publish size, same convention as above, tip against its predecessor in the same checkout path: `3bb804d8de` 42,563,564 bytes, this round 42,563,741 bytes: +177 bytes. The earlier recorded figure for `3bb804d8de` (42,563,005, from `C:\wt130b`) differs from the in-place figure by 559 bytes (the checkout path), so the corrected delta against base `ff496f2aa0` (42,562,080) is +925 + 177 = **+1,102 bytes**; the raw figure (42,563,741 less 42,562,080) is +1,661, which includes the path offset. 191 files in both. No package change.
