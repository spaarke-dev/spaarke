# Task 097: `/api/communications/send` authorization and the archive id

> **Status (2026-10-06)**: RE-SCOPED per §6 and executed; see **§7** for the re-scoped work. The authorization is
> UAC-r2 task 161's (on master via #1312, `d254d7166`). This task fixed the outbound archive pointer, switched the
> Word Email tab on in the deploy workflow, and recorded how 161 treats the Word tab's request. The live checks
> stay open.
>
> **History (2026-10-04)**: blocked before any code edit. `/conflict-check` found another active project
> already fixing the authorization half of this task on the same three files, and editing the archive code
> (§1-§5). The owner picked option 1 (§6).

## 1. The gap is real on this branch and on master

`src/server/api/Sprk.Bff.Api/Api/CommunicationEndpoints.cs:55-72`: `/send` and `/send-bulk` carry only
`.AddEndpointFilter<CommunicationAuthorizationFilter>()`. That filter checks only that the caller is signed in
and has an `oid`. `CommunicationService` then downloads every `AttachmentDocumentIds` entry app-only and writes
`Associations` without checking the caller (task 096 review). HEAD equals `origin/master` plus this project's
commits, and master has the same chain. No failing test was written; see §4 for why.

## 2. The conflict: unified-access-control-r2 task 161 (#1100) already implements the authorization

| Fact | Evidence |
|---|---|
| UAC-r2 task 161, "communications routes authorize the exact record they act on" | commits `7f9159027` (2026-10-03), `0f8cc61ea` (verifier fixes) on `task/uac-r2-161-r1` |
| Merged into the UAC integration branch, pushed | `dc75c60bb` merge into `origin/integ/uac-r2-batch4`; that branch was last committed 2026-10-04 21:50 (`63556192c`), so it is in active verification |
| Also merged into `task/uac-r2-147-r1c` | `a9cf2fd48` (2026-10-04 21:45) |
| Not on master; no open PR touches these BFF files | `gh pr list` (22 open PRs): none include `Services/Communication/**`, `CommunicationEndpoints.cs` or the filter |
| UAC index marks 161 as `🔲 [open]` | `integ/uac-r2-batch4:projects/unified-access-control-r2/tasks/TASK-INDEX.md` |

**What 161 does on `/send` and `/send-bulk`** (`Api/Filters/CommunicationRecordAuthorizationFilter.cs` on that
branch, 833 lines), checked against 097's criteria:

| 097 requirement | 161 |
|---|---|
| Read on every attachment, as the caller | (1) "the decision the document download route makes (operation `read`)", through the existing `AuthorizationService` path, for each distinct id |
| Link rights on every association | (3) AppendTo on every association **and** every regarding record copied from the inherit-from communication, through `CallerRecordAccessProbe` |
| Refuse before any send; one stable code; denied and not-found look the same | `sdap.access.deny.communication.send` / `…send_bulk`, "ONE 403 reasonCode per route, identical for unknown, denied and unparseable ids" |
| Fail closed | every fault denies (`catch (Exception)` → deny, ADR-003); cancellation propagates |
| `/send-bulk` checked too | `AuthorizeSendBulkAsync`: checked "once for the WHOLE request, before the first per-recipient send: a deny is one 403, never a 207" |
| Reuse existing evaluators, no new authorization service | one endpoint filter composing existing seams (CommunicationThreadReadService visibility + ICommunicationAccessFilter, CallerRecordAccessProbe, AuthorizationService `read`). It adds no decision service. |
| Beyond 097 | thread visibility, inherit-from visibility, the 150-attachment cap checked before any rights query, ten other communications routes |
| Tests and seeded controls | `CommunicationRecordAuthorizationContractTests` (real mappers); "36 seeded removals each fail a named test" (commit message) |

The archive bug is **not** fixed by UAC-r2. UAC-r2 task 146 does edit `ArchiveOutboundAttachmentsAsync` and
`CreateAttachmentRecordsAsync`, adding an owner parameter and `ApplyContentOwner`, in hunks that touch the
same lines a fix would change (`@@ -2323` / `@@ -2342` on `integ/uac-r2-batch4`). UAC-r2 changes about 40
hunks across `CommunicationService.cs` in total.

## 3. Findings the owner needs, whichever path is chosen

**Archive bug, confirmed.** `CommunicationService.cs:2328-2343` (`ArchiveOutboundAttachmentsAsync`):
`var speItemId = attachmentDocumentIds[i];` then `["sprk_graphitemid"] = speItemId`. The values are
`sprk_document` GUIDs. Both send modes reach it: User at `:1269-1271` and SharedMailbox at `:1583-1585`.
`["sprk_graphdriveid"] = _options.ArchiveContainerId` is also wrong. The attachment bytes are never copied
into the archive container; they stay in each source document's own drive. The correct values are the
**source document's** `sprk_graphdriveid` and `sprk_graphitemid`, which the attachment fetch already reads
(`:2201-2222`). So the fix should carry `(driveId, itemId)` per attachment from the fetch to the archive.
Writing an item id into the archive container's drive id would still be wrong. Reachability: the Word tab
sends `archiveToSpe:false`, but the shared engine defaults it to `true` (096 note §119-121).

**Word Email tab (096) compatibility with 161, by inspection.** The attachment is the user's own saved
document, so Read passes. The association is the document's record, and the save already required
**AppendTo** on that record (084: `POST /api/office/save` → `EntityAccessFilter` AppendTo), so 161's AppendTo
check passes for any record the save accepted. An unfiled save sends no association, so nothing is checked
there. Expected verdict: **unaffected**. This was not tested live.

**Other callers of `CommunicationService.SendAsync`**, preliminary verdicts:

| Caller | Attachments / associations | Verdict |
|---|---|---|
| `Api/CommunicationEndpoints.cs` `/send` (`:427`) and `/send-bulk` (`:609`), used by the Spaarke email page / `EmailComposer` and the Word Email tab | yes | **now checked** (161 route filter) |
| `Services/Registration/RegistrationEmailService.cs` (6 calls, `:75`…`:271`) | none | **unaffected, app-only by design.** 161's check sits on the route, not in the service. A service-level check would also be a no-op here (empty lists). |
| `Services/Ai/EmailDispositionSender.cs:107` (`SendMode.User`, OBO) | none | **unaffected** |
| `Services/Communication/Channels/EmailChannelSender.cs` | n/a: it is the Graph sender CommunicationService calls, not a caller | n/a |

No internal caller sends documents on a user's behalf, so escalation trigger 2 does not fire.

## 4. Decision needed (owner)

🔔 **Human Input Required: escalation trigger 3** (`/conflict-check`: another active project is editing these
Communication files)

- **Situation**: 097's authorization half is already built, verified and integrated by unified-access-control-r2
  task 161, on `origin/integ/uac-r2-batch4`. It is not on master yet. Building it again here would add a
  second, competing mechanism. That breaks CLAUDE.md §11 and 097's own "no new authorization service" rule,
  and it would conflict textually in `CommunicationEndpoints.cs`, `CommunicationAuthorizationFilter.cs` and
  `CommunicationService.cs`.
- **Options**:
  1. **(Recommended) Ship order.** Do not deploy the Word Email tab (096) until UAC-r2's integration, which
     carries 161, is on master and deployed. Re-scope 097 to: (a) the archive fix, done on top of master
     **after** UAC-r2 lands, so it rebases on task 146's owner changes instead of conflicting with them;
     (b) the live check that the Word tab still sends and that a foreign document id is refused with
     `sdap.access.deny.communication.send`. 161's tests stand as the contract tests.
  2. Pull `task/uac-r2-161-r1` (or the integ branch) into this branch. This brings a large unrelated UAC
     surface (tasks 146-164) into the Word project's PR. Not recommended.
  3. Implement 097 independently here and resolve the conflict against 161 at merge. This duplicates
     security-critical logic and makes the merge resolution risky. Not recommended.
- **If the Word tab must ship before UAC-r2**: option 3 is the only way, and it would need the UAC-r2 owner to
  agree how 161 rebases on it.

## 5. What was not done (left open)

No source or test edits, no reproduce-first test, no seeded control, no build or test run, no CVE check, no
publish-size measurement. code-review and adr-check were not run because no code changed (task-execute
Step 9.5 skips when nothing changed). Every acceptance criterion is OPEN.

## 6. Owner decision (2026-10-04): option 1

The owner, on the §4 question: *"yes we can follow your recommendation - ensure we have this fully documented"*

**The decision, in full:**

1. **The `/api/communications/send` authorization is UAC-r2's.** Task 161 (#1100) is the one implementation. This
   project does not build a second one. 097's authorization criteria are met when 161 is on master, by 161's own
   contract tests (`CommunicationRecordAuthorizationContractTests`, 36 seeded removals).
2. **The Word Email tab (096) is held until 161 is on master AND deployed to the environment.** The code stays (it
   is merged with round 4); the tab is switched OFF by a build setting, so 095 and 083 can ship now.
   - **Mechanism (next action):** a build setting `ADDIN_EMAIL_TAB_ENABLED`, injected like `ORG_URL`
     (`webpack.config.js` + `deploy-office-addins.yml`), default **off**; the adapter's `canEmailFromPane`
     capability is `true` only in Word **and** with the setting on. With it off, Word shows Save · To Do · Find and
     no Email tab, and no Send Email row (096 removed task 086's row, whose sharing link always failed on SPE — so
     nothing working is taken away). Outlook is unaffected (native compose).
   - **Release:** flip the setting on in `deploy-office-addins.yml` in the same change that records 161's merge +
     deploy; no code change.
3. **097 is re-scoped** to: (a) the archive fix — `ArchiveOutboundAttachmentsAsync` writes `sprk_document` ids into
   `sprk_graphitemid` and the archive container into `sprk_graphdriveid`; it must carry each source document's drive
   and item ids from the attachment fetch — done on master **after** UAC-r2 lands (UAC-r2 task 146 edits the same
   lines); (b) the live checks: the Word tab sends the user's own document, and a foreign document id is refused with
   `sdap.access.deny.communication.send`.
4. **Coordination:** UAC-r2 is told that this project depends on 161 for the Word Email tab and owns the archive fix
   after 146 (so the two do not collide). **Done 2026-10-04**: the owner passed this note to the UAC-r2 session, which
   noted both dependencies and will report back when 161 and 146 are done.

**Order:**

| Step | Owner of the step | Gate |
|---|---|---|
| Email tab switch (default off) | this project, now | — |
| Round-4 PR (095 + 096-held) → merge → ONE dev BFF deploy (083 + round 4) → add-in site | this project | owner go for the BFF deploy (given: "once, with round 4 + 097") |
| 161 (+146) to master and deployed | UAC-r2 | UAC-r2's own verification |
| Flip `ADDIN_EMAIL_TAB_ENABLED` on; archive fix; live checks | this project (097) | 161 on master + deployed |

## 7. Re-scoped execution (2026-10-06)

**Trigger**: 161 is on master (#1312, `d254d7166`; it is an ancestor of this branch's HEAD) and 146 is on master.
Owner, 2026-10-06: *"uac-r2 task 161 is working so you can proceed with completing the dependent work"*.

### 7.1 Archive pointer fix: implemented and tested, but NOT safe to ship as is (🔔 decision needed)

**The change** (`src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationService.cs`):

- `DownloadAndBuildAttachmentsAsync` (`:2636`) now returns `PreparedAttachments`, which holds the channel attachments
  plus, index-aligned, one `OutboundAttachmentSource(DocumentId, DriveId, ItemId)` per attachment (`:2621`,
  recorded at `:2875`). These are the source document's own `sprk_graphdriveid` / `sprk_graphitemid`, after task
  166's pointer check (`:2743`) has passed them.
- Both send paths carry the sources (SharedMailbox `:1144`, User `:1509`) to the archive (`:1336`, `:1658`). The
  `_options.ArchiveContainerId` drive and the raw id array are no longer passed.
- `ArchiveOutboundAttachmentsAsync` (`:2507`) writes `sprk_graphitemid = source.ItemId` and `sprk_graphdriveid =
  source.DriveId` (`:2541`). Task 146's `ResolveContentOwnerAsync`, its refusal branch and `ApplyContentOwner` are
  unchanged.

**Regression test**: `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/OutboundAttachmentArchivePointerTests.cs`.
It is a theory over both send modes. It runs the real `SendAsync` and two source documents in two different
drives. File metadata comes from the real `GraphMetadataCache` over an in-memory cache, so there is no
`HttpMessageHandler`. The real document-pointer check runs. The test asserts each archived attachment's
(drive, item) and that its owner is the team.

- **Red on HEAD** (the old file restored with `git restore --source=HEAD`): 2/2 failed with exactly the bug, `Expected
  … to be "drive-bu-north" … but "drive-archive"` and `Expected … to be "item-contract" … but
  "8be08e48-7cef-4365-8ec2-6f5a875dd8d7"` (the `sprk_document` GUID), for both attachments and in both modes.
- **Green on the fix**: 2/2 passed.

**🔔 Why it must not ship as is: the fix makes two `sprk_document` rows share ONE SharePoint Embedded file, and
two existing BFF paths delete the file a row points at.**

| Path | What it does to the shared file |
|---|---|
| `DELETE /api/documents/{id}` → `DocumentCheckoutService.DeleteAsync` (`Services/DocumentCheckoutService.cs:794-799`) | Deletes `document.DriveId/ItemId` from SPE first. Deleting the **archived copy** would delete the **original document's** file. |
| `DocumentContainerRelocator` (Make Secure moves and the legacy migration; `Services/Documents/DocumentContainerRelocator.cs:1808`) | After the copy is verified, deletes the source item. Relocating the archived copy (for example when the communication's record is made secure) would remove the original document's file. |

Today's broken pointer names no file, so both paths miss (`not found`) and nothing is lost. After the fix, both
paths can destroy a user's original document. This is the kind of data-integrity change that needs a human
decision (CLAUDE.md §6).

Two smaller interactions with task 166's pointer check (`RecordContainerResolver.DocumentPointer.cs`):

- Under the **STRICT** rule, the archived copy's derived container is the communication's container, not the source
  record's. App-only reads of the copy are refused, including the Document Profile job this method enqueues.
- Under the **INTERIM** rule, a read is served only when the item's creator is the archived row's creator, that is,
  the sender. So the copy works only for documents the sender uploaded.

**Options (owner)**:

| # | Option | Effect |
|---|---|---|
| A | **Copy the bytes** into the communication's container, the one `CommunicationContainerResolver` gives the `.eml` (`ArchiveToSpeAsync`), named `{communicationId:N}_{file}`, and point the archived document at the copy. | No shared item. Matches the inbound archive (`MessageAttachmentMaterializer`) and task 166's ARCHIVE-PATH rule (b), so it reads under both rules. Delete and relocate are safe. Cost: the bytes (already in memory for the send) are stored twice, and files over 4 MB need the upload-session path, not `UploadSmallAsync`. |
| B | **Do not create a duplicate document** when the attachment is already a `sprk_document`. The `sprk_communicationattachment` row already links the source document (`CreateAttachmentRecordsAsync`, `sprk_document`). | No duplicate rows, no shared items, no second copy of the file. It is a product change: the copy's `sprk_relatedcommunication` listing and its Document Profile job go away, and the source document is already profiled. |
| C | Ship the shared pointer as implemented. | Not recommended: it adds the data-loss path above. |

**Recommendation: B if the product agrees that "documents of this communication" may be read through the
attachment rows; otherwise A.** Either one is a new, small task. The pointer change and its test stay in the
working tree, uncommitted, so the main session can keep them as the basis for A or drop them for B.

**Reachability today**: only sends with `archiveToSpe: true` reach this code, which means the Spaarke email page
with archiving on. The **Word Email tab sends `archiveToSpe: false`** (096), so the tab does not depend on this
decision.

### 7.2 Word Email tab switched on (deploy workflow only)

- `.github/workflows/deploy-office-addins.yml`: `ADDIN_EMAIL_TAB_ENABLED: "true"`, with a comment citing 161 on master
  (#1312, `d254d7166`) and the owner's go of 2026-10-06.
- `src/client/office-addins/webpack.config.js`: the code default stays **OFF** (only the exact string `"true"` turns
  it on). The comment now says the deploy workflow turns it on.
- `src/client/office-addins/.env.example`: "held off" wording replaced. The commented example is now `=true` for
  local builds.
- `WordAdapter.ts:727` and `capabilities.test.ts` are unchanged (the capability follows the setting).

### 7.3 How 161's filter treats the Word Email tab's request (read from the code)

Request: `POST /api/communications/send`, `sendMode: "User"`, `attachmentDocumentIds: [<the open document's
sprk_document id>]`, `associations: [{ entityType: <logical name>, entityId }]` (or none when unfiled),
`archiveToSpe: false`, and the pane's bearer token.

The route (`Api/CommunicationEndpoints.cs:62-64`) runs `CommunicationAuthorizationFilter` (signed in, has `oid`),
then `CommunicationRecordAuthorizationFilter(Send)` (`Api/Filters/CommunicationRecordAuthorizationFilter.cs`):

1. `CheckRequestShape` (`:309`): no shape rule for Send, so it passes.
2. `CheckCallerPreconditionsAsync` (`:368`): a bearer token plus a resolvable Dataverse `systemuser`. Every add-in user
   holds at least Basic User, so this passes.
3. `AuthorizeSendAsync` → `AuthorizeSendBodyAsync` (`:582`, `:610`):
   - attachment count is at most 150 (one).
   - **(1) Read on each attachment** (`:621-629`) through `AuthorizationService` with operation `read` (`:753-769`). This
     is the same decision the document download route makes, asked as the caller. The user's own saved document is
     one they can already open and download from the pane (Find / Open), so **it passes**. A foreign or unknown id
     fails with `sdap.access.deny.communication.send`.
   - **(2) Thread**: the tab sends no `threadId`, so skipped. **Inherit-from**: none, so skipped.
   - **(3) AppendTo on each association** (`:635-666`) through `CallerRecordAccessProbe.GetCallerRightsForRecordsAsync`.
     The Word document's related record comes from the four direct slots the Office save writes (`sprk_matter`,
     `sprk_project`, `sprk_invoice`, `sprk_workassignment`; `OfficeDocumentPersistence.DirectAssociationAttributes`).
     All four are in `RegardingNameFields.EntitySetName`. The save itself required **AppendTo** on that record
     through the **same probe** (`EntityAccessFilter`, task 084), so a record the save accepted **passes**. An unfiled
     document sends no association, so the target list is empty and it passes (`:743-744`).
   - Every fault denies (`Safely`, fail closed).

**Verdict: unaffected.** The user's own document plus its filed record passes all three checks. A document id the
user cannot read gets 403 `sdap.access.deny.communication.send` before any mail is sent. **One edge to know:**
`sprk_todo` is not in `RegardingNameFields.EntitySetName`, and `sprk_communication` has no regarding column for it.
If the related-record card ever surfaced a To Do (today it does not; it reads only the four slots above), that send
would be refused with the same 403.

### 7.4 Gates

| Gate | Result |
|---|---|
| `/conflict-check` | Soft warns only. PR #1314 (`fix/uac-r2-deploy-script-fixes`) edits `CommunicationService.cs` around `:446` (template read) and `CommunicationServiceArchiveEmbedTests.cs`. This task's hunks are elsewhere, and its test is a new file. Dependabot PR #909 touches only the workflow's setup-node line. |
| `dotnet build src/server/api/Sprk.Bff.Api/ --no-incremental` | 0 warnings, 0 errors |
| Unit project, filter `Communication\|Office` (includes the linked `tests/integration/contract/**` and `auth/**`, among them 161's `CommunicationRecordAuthorizationContractTests`) | 1904 passed, 16 skipped, 0 failed |
| `tests/Spaarke.ArchTests` | 806 passed, 0 failed |
| `dotnet list package --vulnerable --include-transitive` | no vulnerable packages |
| Publish size: fresh worktrees `C:\code_files\wt097m` (origin/master `dc469d4a2`) and `C:\code_files\wt097b` (HEAD `a5ec128a4` + the changed `CommunicationService.cs`), `dotnet publish -c Release`, `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*` | master **37,886,369 B (36.13 MB)**, branch **37,887,104 B (36.13 MB)**, delta **+735 B**. 192 files on each side. Both worktrees removed. |
| Inline code-review + adr-check | see the findings in §7.1. ADR-007: SPE only through `SpeFileStore`, no new Graph call. ADR-010: no new DI registration. ADR-002: no plugin. ADR-028: no auth change. ADR-038: no `Mock<HttpMessageHandler>`. The test sits in `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/` beside the other CommunicationService tests, as instructed; ADR-038 would place a bug regression under `tests/integration/regression/` (low). CLAUDE.md §10 placement: an existing service is modified; no new endpoint, service, package or registration. |

### 7.5 Open

- 🔔 **Owner decision on §7.1** (A / B / C). Until then, do not commit the `CommunicationService.cs` change and its test
  as a fix.
- **Live (main session, after the add-in deploy)**: the Word Email tab sends the user's own document; a foreign
  document id is refused with `sdap.access.deny.communication.send`.

## 8. Main session (2026-10-06): what ships now, what waits

- **Ships now**: the Word Email tab switch (`ADDIN_EMAIL_TAB_ENABLED: "true"` in the deploy workflow) — 161 is on master
  and live on dev since ~04:47 UTC (UAC-r2's confirmation), and the tab sends `archiveToSpe: false`, so it does not
  touch the archive path.
- **Waits for the owner**: the archive fix. The drafted change (carry the source drive/item ids into the archived copy)
  would make two `sprk_document` rows share one SPE file, and `DELETE /api/documents/{id}` / the container relocator
  delete the file a row points at — deleting the copy would delete the original. Options A (copy the bytes into the
  communication's container, like inbound archiving) / B (no duplicate; the attachment row already links the source) /
  C (ship as is — no). The draft code + its red/green regression test are kept at
  `notes/097-archive-pointer-fix-draft.patch`, NOT in the tree.
