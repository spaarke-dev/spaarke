# Task 145 assessment — telling an email's same-name attachments apart

> 2026-11-28. Owner (on 144 Q7): "its own task and ensure you thoroughly research code to ensure no code conflict; evaluate if this requires a more significant change to the architecture or build". Facts from a read-only code map (Sonnet code-mapper) over `C:\wt143` (master + PR #3330), key claims re-checked by the main session. Line numbers approximate; re-locate by name.

## 1. The problem is bigger than the "saved" marks

| # | Defect | Where | Effect with two different `image001.png` in one email |
|---|---|---|---|
| D1 | **Data loss, Office save** (ours, fm-coordinated file) | `UploadFinalizationWorker.cs` ~:1401-1414: child path `{parentDocumentId:N}_{SanitizeFileName(name)}`, `UploadSmallAsync` = path-keyed PUT that overwrites | The second upload overwrites the first file; the second row's create is then refused by `sprk_graphitemid_uk` (worker comment ~:1236). Result: one document named for the first file holding the SECOND file's bytes, and the second file missing. To be proven by a must-fail-before test |
| D2 | **Data loss, inbound mail** (communications code, no active owner session) | `IncomingCommunicationProcessor.cs` ~:1323-1325: path `{communicationId:N}_{SanitizeFileName(name)}`, same PUT | Same overwrite for mailbox-captured mail (same table key, so presumably the same outcome; to be proven by a test) |
| — | Outbound archive already handles it | `CommunicationService.cs` ~:4501 `UniqueArchivePath(communicationId, name, usedPaths)` + `ConflictBehavior.Fail` | Second file becomes `… (2).png` |
| D3 | Selection by name | pane `useSaveFlow.ts` ~:1485-1494, `emailContentCapture.ts` ~:357; server `EmailAttachmentAddPlanner.ApplySelection` ~:632, worker `SelectAttachments` ~:1198-1231 | Ticking one selects both |
| D4 | Skip rule by name | `EmailAttachmentAddPlanner.IsAlreadySaved` / `SavedNamesOf` ~:645-671; worker `AttachmentNamesWithoutAChildAsync` ~:539-572 | Once one is saved the other is skipped, and reported as nothing to add |
| D5 | Saved marks by name | identity `DocumentEmailIdentityFilter.ReadSavedAttachmentNamesAsync` ~:139-229 (`Distinct`); pane `savedEmailAttachments.ts` `nameKey`/`classifySavedAttachments` ~:56-132 | The unsaved one shows as saved and cannot be ticked |
| D6 | Failure counter by name | `useSaveFlow.ts` `attachmentOnlyFailuresRef` | Two same-name files share one retry counter (idempotency key) |

## 2. Identity options

| Option | Verdict | Why |
|---|---|---|
| Name + size | **Rejected** | Inbound and outbound attachment rows store no `sprk_filesize`; item attachments are converted to `.eml`/`.ics` by the pane, so name and size both change; no evidence that Office.js `size` equals the stored decoded length |
| Office.js attachment id | **Rejected as a stored key** | Not persisted anywhere; Office.js documents it as valid only within the session on web and mobile (`office.d.ts` ~:10024). Usable only inside one request |
| Content (bytes) | **Chosen, server-side** | Exact; the server already holds the bytes of every candidate (the request or the stored `.eml`'s MIME parts) and has a byte comparison against a stored SPE item (`OfficeStorageUploader.ItemHoldsContentAsync`, task 047) |
| Count of a name | **Chosen, pane marks** | The pane cannot read every attachment at open just to draw marks. With N files of a name in the email and K saved, it can say saved (K ≥ N), unsaved (K = 0) or "1 of 2 saved" — the server then decides which by content |

## 3. Recommended shape (no new architecture)

- **Storage (D1, D2):** each attachment of one email gets its own stored name that is stable for that attachment (retry-safe) and never equals another attachment's of the same email, including one added later. Reuse the outbound rule (`UniqueArchivePath`) by moving it to the shared `SpeUploadPath` helper rather than writing a third.
- **Selection (D3):** inside one email save the request carries every attachment (with its Office id), so the selection can name attachments by id within that request; the server turns it into the `.eml` part positions the worker uses. An add to an existing `.eml` names the ticked attachments by name plus a content hash the pane computes from the bytes it reads for the ticked files only; the server compares with the MIME parts.
- **Skip rule (D4):** a name present once keeps today's name rule. A name present more than once in the `.eml` is "already saved" only when a child of that name holds the same bytes (byte comparison against the stored child).
- **Marks (D5):** the identity answer reports how many documents of each name are saved (additive field; keep `savedAttachmentNames` for old panes). The pane marks by count; a partial count leaves those attachments tickable with "1 of 2 files named … is already saved".
- **D6:** key the failure counter by attachment id within the session.

Contract changes are additive (new request and response fields; old fields keep their meaning, including ebilling's `SelectedAttachmentFileNames = []` in `FromEmailSubmissionService.cs` ~:276 and `OfficeSubmissionEmailSave.cs` ~:19).

## 4. Does it need a bigger architecture or build change?

**No new service, Dataverse column, package, route or build step.** It is a cross-cutting contract change: about 13 BFF files, 9 pane files, ~25 test files, plus the ADR-008 A1 row and `RouteAuthorizationGuardTests.Ledger.cs` (they describe `savedAttachmentNames`). Not a new hash column: stamping a hash on every attachment row would touch the inbound and outbound create paths (other owners) and turn on tenant-wide content dedup for attachments that today are never deduplicated.

What makes it more than a pane fix: two data-loss defects (D1, D2), one in another owner's code (inbound), and the worker is a field-mapping-r1-coordinated file (fm W3).

## 5. Conflicts

- Open PRs touching these files: only PR #3330 (task 143). 145 starts after it merges.
- Local worktrees with unpushed work on these files: see §6 (scan result).
- Coordination before starting: field-mapping-r1 (worker, W3); D2 in `IncomingCommunicationProcessor`: **owner go 2026-11-28, "Yes include fix here"**; ebilling-r1 informed (additive fields only); task 144 waits on 145 (its "skip a name already saved" rule is replaced by §3).

## 6. Worktree scan

One search over every local and remote branch's commits not on master (last 45 days) for the files in §4:

- task 143 (PR #3330): `OfficeService`, `EmailAttachmentAddPlanner`, `DocumentUrlIdentityResolution`, `DocumentEmailIdentityFilter`, `FileAccessEndpoints`, `SaveRequest`/`SaveResponse`, pane `App`/`SaveFlow`/`useSaveFlow`. 145 starts from master after #3330 merges.
- **spaarkeai-mcp-bff-api-r2** task 023 (`a08889ac93`, "extract preview and open-links steps from the file routes") refactors `Api/FileAccessEndpoints.cs`, where the identity response is built (~:326-328), and ~18 of its agent worktrees touch that file. Not on master yet. 145 adds a field to that response: tell mcp-bff-api-r2 before starting, and rebase on their merge if it lands first.
- No other branch touches the worker, the planner, the inbound processor, `CommunicationService` (in these 45 days), `OfficeStorageUploader` or the pane files.
- Uncommitted work in the 232 worktrees was not scanned (too slow); the per-worktree scan that ran ~5 minutes found only the mcp-bff-api-r2 agent worktrees above.
