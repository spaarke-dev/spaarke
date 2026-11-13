# Current Task State — `spaarkeai-word-add-in-r1`

> **Format (2026-10-06):** CURRENT state only, REWRITTEN at each checkpoint. Standing directives + gotchas: project `CLAUDE.md` → "Standing directives & gotchas". History: git log + `notes/handoff-history/` (do not load on recovery).

> **Last Updated**: 2026-11-12 (#2552 merged and deployed; dev BFF 4e64670ea0 checked; D-220 split agreed with ontology). This project runs in the **Claude Desktop session `word-add-in-r1`** (the VS Code session retired 2026-11-08).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | **133 🔄 Outlook Submit Invoice view** (FULL, opus/high). Live on dev; unified package **1.1.4** uploaded by the owner 2026-11-03 (Invoice pane tab; Submit Invoice / Create To Do ribbon buttons removed). |
| **Status** | Waiting on the owner's checks. No open PR. Work branch clean and pushed. |
| **Open PR** | None. |
| **Live dev BFF** | **9411f604bb** (uac-r2, read-back green 2026-11-13T02:49:36Z; ProductVersion 1.0.0+9411f604bb): a local merge of master **e9208abc5e** with the mcp-r1 spike **d7369fecf1** (which contains 755987b385), so it is not one master commit. Every later dev deploy must include the spike. It carries **030b, 049c, 095c-2..4, 095a, 095b, 151e and fm 054**. Peer OK given: no add-in concern. `FlaggedOfficeEdit` is off; uac-r2 will say when it is on. Before it: 4e64670ea0, then 6dbe69b9e2 (both 2026-11-12). Range checks: see "Recent deploy checks". |
| **Owner checks open** | On the live build (the checks planned for 22487c6537 and 6dbe69b9e2 were never made; they run on **9411f604bb**; uac-r2 confirmed the Office checks can run against it): (1) an Office save filed to a matter, (2) an Office save with no related record, (3) one Submit Invoice from an email, (4) one Outlook To Do save (049c), (5) one Outlook email save WITH an attachment, filed to a matter (030b: the child row has `sprk_matter`, `sprk_parentdocument` and graph ids from the create), (6) the already-submitted banner: open an email that was already submitted, and the Invoice tab should show the banner, not the form (095a). (7) #2552: one Submit Invoice with no firm, then a correction of it. Then read back the `sprk_document` rows created after the deploy time (`sprk_searchindexcompletedon`, `sprk_searchindexname`, owner, BU) and the invoice submission, invoice and line rows; send the result to `uac-r2` naming the live SHA. None made yet. |
| **Last merge to master** | **#2552** (14b600192f, 2026-11-12T20:05:50Z, owner's go): optional firm (EB-D-185) + "Submit it again." for the 095c-4 transient Failed codes. All 45 PR checks passed. Auto-merge did not fire although the PR was clean and `Router` passed on the head, so it was merged directly with `--merge`. The SWA deploy (run 43296860255) and the post-merge `Office Add-ins Tests (Gate)` (run 43296860521) both passed. C:\wt185f removed. The work branch still has checkpoint-only commits ahead of master; they go in with the next merge. |
| **Next Action** | Wait for, and act on: the owner's checks (read back, report to `uac-r2` naming the live SHA); each `uac-r2` deploy notice (range check, reply); ontology's master SHA + handler contract for task 189's templates route, which starts this project's Word Email tab task (section "Word Email tab: templates + AI draft"). Nothing in progress, nothing uncommitted. |

## Decisions in force (detail in CLAUDE.md "Standing directives")
- **W3 create paths** (owner 2026-11-06): CLAUDE.md "Create paths follow fm's W3 direction". 030a is live. **030b is on master** (PR #2629, 57748ebf6d, 2026-11-08 04:43Z, with fm 034 + 052a) and **live on dev in 6dbe69b9e2**. Content read done on 2026-11-08 and sent to fm: OK, no pane change needed. Association, parent link and SPE pointer are now written in the create. The new request members are `[JsonIgnore]`. The id is server-minted, as it already was. The version save (`existingDocumentId` + `isNewVersion`) is outside the diff, and response and refusal bodies are unchanged. When a deploy notice's range includes 57748ebf6d, cite this read.
- **Roles**: add-in user = Core User + Office Add In User; service and demo accounts never get the add-in role; every add-in role holder has Core User or System Administrator (read back 2026-11-06).
- **ebilling contract** (#2552): `"firm": null` prefill accepted; "Firm: none" kept; §12 reviewer refusals are on no pane route; 082c-3 `warning` is SPA-only.

## Recent deploy checks (newest first; older ones in git log)
- 2026-11-12 peer OK for master e9208abc5e + spike (fm 054 deploy, owner's go pending in uac-r2). 2ef4a17915..e9208abc5e has 0 server and 0 add-in files, so the pre-check below covers it. Deployed as 9411f604bb (parents e9208abc5e and d7369fecf1, verified); after-notice received 2026-11-13.
- 2026-11-12 pre-check of master 2ef4a17915 (+ spike) for uac-r2; the request was withdrawn (no deploy). Range 350ca3da66..2ef4a17915: 27 server commits, all fm 054/054b communication-ownership work. No Office server change. `CommunicationEndpoints` re-file/filing routes now declare 409/503; the pane calls neither. The only add-in commits are #2552's own. No add-in concern; reuse this if that SHA is deployed later.
- 2026-11-12 4e64670ea0 (master 350ca3da66 + spikes 755987b385, d7369fecf1; uac-r2 via ebilling): range a654da59c4..350ca3da66 has no Office server or add-in commit. 191g (#2736, off by default): `GET /api/documents/{id}/open-links`, which the pane's Open Document uses, now passes the file's item id to the edit-access step; nothing changes while the setting is off. 191h (#2726) applies the external visibility predicate to SPA lists, AI tools and retrieval trim for external callers; the pane's licensed users are unaffected. #2732 is a share-job watermark fix. No add-in concern. The pane's own #2552 is not in this build: it is client-only and deploys through the SWA.
- 2026-11-12 6dbe69b9e2 (master a654da59c4 + spike 755987b385; range from d53d67f604). Office server changes: 030b (read OK); 022 fix round 3, where quick-create returns 400 `record_creation.mapping_source_secure_filing` with the service's sentence (the pane shows the server detail, task 053); regarding-pair display columns; the uncalled attachment creator was deleted; whitespace only. Add-in client: one comment in EmailView (#2666). **Behaviour change (#2666, ontology D-217):** a Word Email-tab send filed to a record is now archived as an `.eml` even though the pane sends `archiveToSpe: false`. The server decides; no pane change. Finance: the 095a route `GET /api/finance/invoice-submissions?internetMessageId=` now exists; its fields and status values (Received, Processing, Accepted, Rejected, Failed) match the pane's reader. No add-in concern.
- 2026-11-07 22487c6537 (spike tip): no add-in concern; flagged "not on master"; uac-r2 confirmed the owner's go.
- 2026-11-07 cc11294300: 030a To Do through RecordCreationService; #2292 shared alternate-key classifier (exact 0x80060891). No add-in concern.
- 2026-11-06 20669df6a9: POST /api/v1/documents caller check does not touch the pane (it uses GET/PUT there).

## Word Email tab: templates + AI draft (ontology D-220, owner ruling 2026-11-12)
- The owner ruled that the Word Email tab (`EmailView.tsx` → `SendEmailPane`) gets the template picker and AI drafting, like the Console's Send Email (#2744), coordinated with this project and uac-r2. ontology's task 189 tracks it; nothing is built yet.
- Answered ontology on 2026-11-12. This project owns `EmailView.tsx` and `shared/types/spaarke-send-email-pane.d.ts`; nothing in flight. Draft and render have no add-in limit (same NAA audience and CORS as `/send`). Recommended a BFF `GET /api/communications/templates` used by both hosts (Console drops its Xrm listing; ADR-012 host-agnostic decision 2026-10-05; compose-r8 can swap the source server-side), owned by the communications owner and checked by uac-r2. `NO_ADDITIONAL_ATTACHMENT_SOURCES` stays. **Agreed with ontology (2026-11-12):** ontology task 189 builds `GET /api/communications/templates` as an impersonated caller read (uac-r2's condition; its contract is not bound to OOB `template` columns) and switches the Console's `onListEmailTemplates` to it; uac-r2 reviews the route before merge. **This project then does the pane side:** EmailView passes `onDraftWithAi`, `onListEmailTemplates` and `onRenderEmailTemplate`; add those props (and `aiDraftActions` if used) to the `.d.ts` subset; EmailView tests; owner narrow-width check. Trigger: ontology sends the master SHA and the handler contract. Run it as a project task (task-execute) and add it to TASK-INDEX at that point.

## Submit Invoice (task 133): state
- Already-submitted banner (#2533): **active since 6dbe69b9e2** (095a on dev); owner check (6) above. Backfill for older submissions is ebilling's, with the owner's OK; an older submission still shows today's form until then.
- Invoice form Files section and "Source email" link: ebilling 151e, on dev in 6dbe69b9e2 (the invoice form, not the pane). No approve UI until ebilling 110/111/112 (no interim path).
- Owner retest still open: INV-012.2 / correction UAT.
- **Slice C (needs the owner's explicit go):** remove Invoice from Save and "+ New" in both hosts; tell the owner Word loses Invoice save; then tell ebilling for 096.

## Peers (SendMessage by session name; check `ListAgents` — several moved to Claude Desktop on 2026-11-08)
- `uac-r2` (Desktop): dev BFF deploys, roles. A held message means the owner approves it in that session.
- `field-mapping-r1` (Desktop; the VS Code session `-69` is retiring): field mapping. Nothing open with them (030b read OK, acknowledged).
- `ebilling-r1` (Desktop since 2026-11-11; VS Code `-c7` retired): ebilling. Owner approved their next work→master merge on 2026-11-11, which carries 095c-2..4 and 095a/095b/151e. It is waiting on a master sync and a test fix. They will send the master SHA, then the dev SHA.
- `ontology-r1` (Desktop, since 2026-11-08; the VS Code session `-38` is retiring): ontology, including its dev BFF before/after notices.
- `provisioning-r1` (Desktop): customer provisioning.

## Owner items (open)
- Prod Entra: register the add-in's `brk-…://addins.spaarke.com` redirect on prod `1958aec2` (dev has it; offered via az with a go).
- Teams admin: reject the duplicate "Spaarke portal" submission `5d88b948…` (keep `60055573…`); then tell R3 and legal-ai-accelerator.
- Request Too Long follow-ups on the owner's machine.
- Excel/PowerPoint add-in (no go yet); UAT 042, 080, 121, 127, 078; PCF re-sync; A9 guest sign-in; a named user's retest for R3; 090 wrap-up (`/test-diet`).
- Post-deploy check (UAC #2468): a child-BU user's unfiled Outlook save; read back the `sprk_document` owner and BU.
