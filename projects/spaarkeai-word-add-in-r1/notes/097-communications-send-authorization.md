# Task 097: `/api/communications/send` authorization and the archive id. BLOCKED (escalation trigger 3)

> **Date**: 2026-10-04 · **Status**: blocked before any code edit · **Rigor**: FULL (opus @ high)
> **Outcome**: `/conflict-check` found another active project already fixing the authorization half of this
> task on the same three files, and editing the archive code. The POML says to coordinate before editing,
> so no source or test file was changed. The owner needs to pick a path (§4).

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
