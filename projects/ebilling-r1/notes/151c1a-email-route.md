# Task 151c-1a: Email to AP and Mark Sent to AP, server side

Branch `task/ebill-151c1a-email-route`, from `work/ebilling-r1` at `d566e5f7fd`. Rigor FULL (bff-api, auth, communication). Model: Sonnet @ high. Decisions EB-D-181, EB-D-182. The UI (151c-1b) calls the two routes below; the regarding link from the sent communication to the batch is task 151c-2 and is **not** in this task (see "No regarding link").

## Schema (scripts written, not run live)

| Column | Table | Type | Script |
|---|---|---|---|
| `sprk_apexportemail` "AP export email" | `sprk_ebillingsetting` | single-line text, Email format, max 320 | `scripts/ebilling/Deploy-EbillingApExportEmailColumn.ps1` |
| `sprk_senttoapdate` "Sent to AP Date" | `sprk_billingexport` | Date and Time | `scripts/ebilling/Deploy-EbillingSentToApDateColumn.ps1` |

Both scripts follow `Deploy-EbillingInvoiceTrackingNumberSchema.ps1` (EbillingSchema.psm1 helpers): idempotent, `-WhatIf`, a conflicting existing column (wrong Email format or max length under 320; wrong DateTime format) is an escalation (exit 2), nothing is ever changed or deleted. Each column is also in the SpaarkeMaster `Entity.xml`, on the main form (plain and `_managed`), in the `Deploy-EbillingFormsViews.ps1` spec and in `docs/data-model`.

`-WhatIf` against https://spaarkedev1.crm.dynamics.com (read-only):

```
Deploy-EbillingApExportEmailColumn.ps1 -WhatIf
  WOULD CREATE sprk_apexportemail (String)
  WhatIf: publish skipped
  created : 1   changed : 0   skipped : 0
  WhatIf run: nothing was written.          exit 0

Deploy-EbillingSentToApDateColumn.ps1 -WhatIf
  WOULD CREATE sprk_senttoapdate (DateTime)
  WhatIf: publish skipped
  created : 1   changed : 0   skipped : 0
  WhatIf run: nothing was written.          exit 0
```

### Deploy order

1. `Deploy-EbillingApExportEmailColumn.ps1` and `Deploy-EbillingSentToApDateColumn.ps1` (each with `-WhatIf` first), then `Deploy-EbillingFormsViews.ps1` (adds both to the main forms).
2. Then the BFF.

The BFF reads each new column in isolation, so deploying the BFF first breaks nothing else: the AP address is read by its own one-column query (not the shared `IEbillingSettingsReader`, whose select would fail on a missing column and break invoice submissions), and the batch read (`GET export-batches/{id}`) does not select the date. Until the columns exist, Email to AP and Mark Sent to AP answer 503; every other route is unaffected.

## Routes (both in `Api/Finance/ApExportEndpoints.cs`, group `/api/finance`, step 0, then `AddFinanceAuthorizationFilter(BatchReadTargets, UniformNotFound)`, then (email only) `CommunicationAuthorizationFilter`, then `RequireEbillingEnabled`)

### `POST /api/finance/export-batches/{batchId}/email`

Request body (optional; an empty body is the same as `{}`):

```json
{ "cc": ["a@example.com"], "message": "Please pay by Friday." }
```

`cc`: at most 10, each a bare valid address (no display name, no two in one string, no blank), de-duplicated case-insensitively. `message`: at most 2000 characters.

200:

```json
{ "communicationId": "<guid>", "sentTo": "ap@example.com", "cc": ["a@example.com"],
  "sentToApDate": "2026-11-02T09:30:00+00:00", "sentToApDateSet": true }
```

`sentToApDateSet` is true only when this call set the date. A re-send returns the date already held and `false`. If the date write fails after the send, the answer is still 200 with `sentToApDateSet: false` and `sentToApDate: null`; the email went and must not be sent again.

| Status | `errorCode` | When |
|---|---|---|
| 400 | `ebilling.export.invalid_request` | Bad CC, more than 10 CCs, message over 2000 characters. Nothing is read or sent |
| 401 | | Not authenticated |
| 403 | `ebilling.export.caller_unresolved` | The caller's systemuser could not be established |
| 404 | (uniform body, `reasonCode` `sdap.access.deny.record_unavailable`) | No Read on the batch, unknown batch, step-0 refusal, rights-read fault |
| 409 | `ebilling.export.ap_email_not_configured` | No active settings row, an empty column, or a value that is not a valid address |
| 409 | `ebilling.export.file_not_available` | Batch not Completed, a requested format has no servable file, a file row is missing/removed or its scan verdict is pending |
| 409 | `ebilling.export.attachments_too_large` | Files over the send path's 35 MB total (or its own limit refused them) |
| 503 | `ebilling.export.email_send_failed` | The send faulted. No date is set |
| 503 | `ebilling.export.unavailable` | A Dataverse or file-store fault before the send |
| 503 | `ebilling.disabled` | Kill switch (an unauthorized caller still gets the 404) |

What is sent: To = the settings AP address; Cc; subject `AP export {batch name}: {N invoices}, {total} {currency}`; a plain-text body with the batch name, invoice count, total and currency (several currencies read "mixed currencies: CAD, USD"), the files attached, and the optional message; both stored files attached as `ap-export-{batchId}.csv` (`text/csv`) and `ap-export-{batchId}.json` (`application/json`), byte for byte as stored; sent as the signed-in user; internal only; not archived.

### `POST /api/finance/export-batches/{batchId}/mark-sent-to-ap`

No body. 200 `{ "sentToApDate": "<iso>", "alreadySet": false }` when this call set the date; 200 with `alreadySet: true` and the existing date when it was already set (no write; also when a rival set it between this call's read and its write: the write is version-conditional and the rival's value is returned). 409 `ebilling.export.batch_not_completed`; 404 uniform; 401; 503 `ebilling.export.unavailable` / `ebilling.disabled`.

### Date rule (both routes)

`sprk_senttoapdate` is written only when empty, with `UpdateIfRowVersionMatchesAsync` on the version just read (one re-read on a version clash), so a date is never overwritten. The clock is `TimeProvider`.

## No regarding link

The sent communication carries **no** link to the batch: no `Associations`, no `sprk_regarding*`, no new column on `sprk_communication`. Task 151c-2 adds a plain `sprk_billingexport` reference later. Until then, find a batch's emails by the subject (which names the batch).

## Access (for uac-r2's review)

**Owner.** What 150 sets today: `ApExportService.CreateBatchAsync` asks the ownership resolver with no parent (`RecordOwnershipContext { CallerSystemUserId, RequestedBy }`), so `WhenUnfiled` is the default `ActingUserTeam`: the batch is owned by the exporting caller's business-unit default team (or the Secure team when the resolver says so, or a refusal that stops the export). The owner is being asked which team should own batches, and that may change 150. Email to AP does not copy that choice: it asks the resolver again, as a **content row of the batch** (`RecordOwnershipContext.ContentOf("sprk_billingexport", batchId)`, `KeepCreatorUnlessTargetIsTeamOwned`), so the communication follows whatever team the resolver names for the batch at send time, and so follows any later change to how batches are owned. An answer that is not a team (refused, or "keep the creator") refuses before anything is sent or written (409). The default owner (the sender's unit team) is never kept.

**Category routing.** `CommunicationEnrichmentService` (which calls `AssignOwningTeamAsync`, the category routing that re-owns a communication) is not run for this send (`ServerSend.SkipEnrichment`). That also means no AI indexing, triage or proposals for the AP email. A normal send still enriches (a regression test pins it).

**Copies.** None. The bytes go from the stored files to the mail channel only. No `sprk_document`, `sprk_communicationattachment` or other row holds them; `sprk_hasattachments` and `sprk_attachmentcount` are not set (no attachment rows exist to count); `ArchiveToSpe` is false and the option is refused if combined with an archive, a shared mailbox, a message or a system-mail send. The only stored copies of the files are the batch's own Stored Files (parent = the batch, so they keep the stored-file hard-deny and Read on the batch).

**Archive.** Not made. The send path's archive step is skipped because `ArchiveToSpe` is false; the guard throws if the server option is combined with it.

**Test.** `ApExportEmailAccessTests`: end to end through the real delivery service, the real `CommunicationService` send-as-user path (Graph channel and Dataverse writes are the only doubles) and a read model that applies the team-owned-row rule. It asserts the communication is owned by the batch's team (the resolver answers a different team for any other question), that it is the only row created and carries no bytes, that no document or attachment row exists, that the stored files are still only the batch's own, that a finance-team member reads the communication and a same-unit user outside the team reads neither it nor any file row, and (control) that the same read model does let a unit-team member read a unit-team-owned row, so the test can fail. The model is a test stand-in for Dataverse's rule; it is not a Dataverse run. Real-environment confirmation belongs to the UI test step.

## Component justification (CLAUDE.md section 11)

**Column `sprk_apexportemail`**
1. Existing: `sprk_ebillingsetting` already holds the AP export mapping; no column holds a recipient.
2. Extension: a column on the existing settings row; no new table.
3. Cost of doing nothing: Email to AP has nowhere to send (EB-D-181).

**Column `sprk_senttoapdate`**
1. Existing: `sprk_billingexport.sprk_exportstatus` and the invoices' Sent to AP status; nothing records that the files reached AP.
2. Extension: invoice status already means "in the batch", not "delivered"; per-invoice copies were rejected (EB-D-182 (3)).
3. Cost of doing nothing: finance cannot tell a batch's files were delivered, or when.

**Route `POST .../email`**
1. Existing: `POST /api/communications/send` sends mail but takes document ids, not stored export files; 151a's download serves one file to a browser.
2. Extension: a route in `ApExportEndpoints` and one service (`ApExportDeliveryService`) over the existing send path; no second mail pipeline.
3. Cost of doing nothing: the AP email is sent by hand from a download, with no record on the batch.

**Route `POST .../mark-sent-to-ap`**
1. Existing: Email to AP sets the same date, but only when it sends.
2. Extension: same service and date writer; a small handler.
3. Cost of doing nothing: a batch delivered another way (portal, USB, a phone call) can never be marked delivered.

**Server-only send option (`ServerSendOptions`)**
1. Existing: `SendCommunicationRequest.AttachmentDocumentIds` (needs a `sprk_document`, which is exactly the unit-readable copy the review forbade) and `SystemMailOptions` (task 185, the pattern copied).
2. Extension: a second internal option on the same request, merged where document attachments are merged; the public JSON contract is unchanged. A public in-memory attachment field was rejected (any caller could attach arbitrary bytes).
3. Cost of doing nothing: the files cannot be attached without a copy row, or the owner cannot be set to the batch's team.

## Placement, publish size, CVE (CLAUDE.md section 10)

- **Placement:** the BFF, `Api/Finance` and `Services/Finance`: both routes answer a user action as the caller (bff-extensions B1 caller identity, B4 live request); the send is the existing OBO path. ADRs: 001, 008 (endpoint filters), 019 (stable codes), 002 (no plugin; one server owner of the date), 018 (kill switch after authorization). No background work, no AI, no package. DI: `ApExportMailer` (a thin seam so the route tests do not need Graph) and `ApExportDeliveryService`, both scoped in `FinanceModule`.
- **Publish size.** `dotnet publish -c Release -r linux-x64 --self-contained false src/server/api/Sprk.Bff.Api/ -o deploy/api-publish`, `Compress-Archive -CompressionLevel Optimal`, fresh worktrees at short paths: `origin/master` at `0e3962069b` (`C:\w15cm`) and this branch at `4d348792fd` (`C:\w15cb`); file counts match (192 and 192). Master 40,995,840 bytes (39.10 MB); branch 41,126,325 bytes (39.22 MB). **Delta +130,485 bytes (+0.12 MB)**, against master, so it also carries the rest of `work/ebilling-r1` that master does not have yet. Far under the +5 MB trigger. Both worktrees removed with `Remove-WorktreeSafe.ps1`.
- **CVE.** `dotnet list package --vulnerable --include-transitive` on `Sprk.Bff.Api`: no vulnerable packages. No package added or changed.

## Tests

| File | Covers |
|---|---|
| `tests/unit/.../Communication/CommunicationServiceServerSendTests.cs` (15) | Bytes reach the channel with name, content type and content; owner follows the batch through the resolver and never the default; a non-team owner answer refuses before send and write; enrichment skipped (and a normal send still enriches); nothing persisted as a document, attachment row or byte attribute; 35 MB total and 150-count limits on the combined list (exactly at the limit sends); the option is refused with shared mailbox, message, archive and system mail; a JSON body carrying `serverSend` / `ServerSend` / `attachments` leaves it null; a send without the option is unchanged |
| `tests/integration/auth/Finance/ApExportDeliveryTests.cs` (39) | Email: success shape, request content, files equal stored bytes, CSV-only batch, mixed currencies, date set once, re-send unchanged, after Mark Sent unchanged, send fault 503 with no date, date-write failure 200 with Error logged, no recipient (no row, empty column, invalid) 409, not Completed 409, scan pending 409, JSON removed 409 with no partial send, oversize 409, send-path limit 409, six bad bodies 400, exactly 10 CCs and 2000 characters accepted, 401, no Read 404 with no Dataverse call, unknown batch 404, four step-0 kinds, kill switch. Mark: sets, idempotent, rival wins, not Completed 409, fault 503, 401, no Read 404, unknown 404 |
| `tests/integration/auth/Finance/ApExportEmailAccessTests.cs` (1) | The access test above |
| `tests/integration/auth/Finance/ApExportRoutesHost.cs`, `ApExportAuthorizationTests.cs` | Host wiring: the recording mailer, a fixed clock, the captured log; the 150 host registers the new service (its route tests failed without it: the routes take it as a handler parameter) |

### Fail-before proofs (production line changed, tests red, restored, restored file `cmp`-identical)

| Mutation | Red |
|---|---|
| `MergeServerAttachments` call removed | 4: bytes reach the channel, 35 MB total, 150 count, access test |
| `OwnerFollows` ignored (default `ForChild` question) | 3: owner follows the batch, "unchanged" answer refuses, access test |
| `SkipEnrichment` ignored | 1: enrichment skip |
| `ServerSend` made public and not `JsonIgnore` | 1: the JSON body must-not-fire test |
| "date already set" short-circuit removed (overwrite) | 4: re-send, after Mark Sent, idempotent Mark, rival wins |

### Seed proof (authorization gate, once per route; filter removed, red, restored, `cmp`-identical)

| Mutation | Red |
|---|---|
| `AddFinanceAuthorizationFilter` removed from `/email` | 6: no-Read 404, the four step-0 kinds, the kill-switch case |
| `AddFinanceAuthorizationFilter` removed from `/mark-sent-to-ap` | 5: no-Read 404 and the four step-0 kinds |

## Gate

| Check | Result |
|---|---|
| 5 test projects build | 0 errors |
| Full `Spaarke.ArchTests` | 1,869 passed, 0 failed (first run: 1 failed, `Task 188 r4e` generic-writer list; fixed by listing `ApExportDeliveryService::SetSentToApDateIfEmptyAsync`, which writes one literal key on `sprk_billingexport`) |
| Unit filter `ApExport / Finance / Communication` | 7,029 passed, 0 failed, 5 skipped |
| `Sprk.Bff.Api.IntegrationTests` | 199 passed, 0 failed, 8 skipped |
| `Spe.Integration.Tests` | 353 passed, 0 failed, 23 skipped |
| `Sprk.Provisioning.ControlPlane.LoadTests` | builds (not run) |

## Known limits and things noticed

- **Post-send window.** If the process dies between the send and the date write, the email went and the date is empty. The caller can use Mark Sent to AP. Making the send idempotent (a sent marker before the send) would need a new column; not done.
- **Unchanged send-path behaviour.** Thread resolution, the arrived-producer, the participant index and the link sweep run for this email as for any send-as-user email. They were not changed and are mocked in the unit rig, so uac-r2 may want to look at what they write for a team-owned communication (they write metadata about the message, not the files).
- **`sprk_hasattachments` is not set**, on purpose: no attachment rows exist, and the flag would claim rows that are not there. The Communication form will not show a paperclip for this email.
- **Graph payload.** The files are sent inline in the Graph `sendMail` body; the existing 35 MB cap (the send path's own) is the only limit applied.
- **Unresolved caller is 403** (`ebilling.export.caller_unresolved`), like the export route, after the uniform 404 check has passed.
- **Settings read.** The AP address uses its own query, not `IEbillingSettingsReader`, for the deploy-order reason above. Once the column is deployed everywhere the reader could carry it; left for a later cleanup.
- **Owner question open.** Which team should own batches is with the owner; this task needs no change if that changes (see "Owner").
