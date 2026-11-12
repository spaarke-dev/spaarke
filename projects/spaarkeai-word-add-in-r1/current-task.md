# Current Task State — `spaarkeai-word-add-in-r1`

> **Format (2026-10-06):** CURRENT state only, REWRITTEN at each checkpoint. Standing directives + gotchas: project `CLAUDE.md` → "Standing directives & gotchas". History: git log + `notes/handoff-history/` (do not load on recovery).

> **Last Updated**: 2026-11-12 (dev BFF 6dbe69b9e2 checked; #2552 released, waiting on the owner's go to merge). This project runs in the **Claude Desktop session `word-add-in-r1`** (the VS Code session retired 2026-11-08).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | **133 🔄 Outlook Submit Invoice view** (FULL, opus/high). Live on dev; unified package **1.1.4** uploaded by the owner 2026-11-03 (Invoice pane tab; Submit Invoice / Create To Do ribbon buttons removed). |
| **Status** | Waiting on the owner's go for #2552 and on the owner's checks. Work branch clean and pushed. |
| **Open PR** | **#2552 draft, RELEASED 2026-11-12** (worktree C:\wt185f, `feat/invoice-optional-firm`, head 6af195647d): optional firm (ebilling EB-D-185) + "Submit it again." sentences for the 095c-4 transient Failed codes. Its gate is met: 095c-2..4 are in master a654da59c4, which is live on dev in 6dbe69b9e2. **Steps once the owner says go:** merge master into the branch, `gh pr ready 2552`, wait for every check, `gh pr merge 2552 --auto --merge`, confirm the SWA deploy, owner retest (no-firm submit + correction of it), remove C:\wt185f with Remove-WorktreeSafe. |
| **Live dev BFF** | **6dbe69b9e2** (uac-r2, 2026-11-12; reported by ebilling). It is a local merge of master **a654da59c4** and mcp-r1's spike **755987b385**, so it is not one master commit. Every later dev deploy must include the spike. It carries **030b, 049c, 095c-2..4, 095a, 095b and 151e**. Range check vs the previous build 22487c6537 (base d53d67f604): see "Recent deploy checks". |
| **Owner checks open** | On 6dbe69b9e2 (the 22487c6537 checks were never made; they move here): (1) an Office save filed to a matter, (2) an Office save with no related record, (3) one Submit Invoice from an email, (4) one Outlook To Do save (049c), (5) one Outlook email save WITH an attachment, filed to a matter (030b: the child row has `sprk_matter`, `sprk_parentdocument` and graph ids from the create), (6) the already-submitted banner: open an email that was already submitted, and the Invoice tab should show the banner, not the form (095a). Then read back the `sprk_document` rows created after the deploy time (`sprk_searchindexcompletedon`, `sprk_searchindexname`, owner, BU) and the invoice submission, invoice and line rows; send the result to `uac-r2` naming 6dbe69b9e2. None made yet. |
| **Last merge to master** | **#2607** (f9ba615b09, 2026-11-06): the To Do form can be saved again after a failed create; checks, gate and SWA deploy all passed. The work branch is 5 commits ahead of master (checkpoints only); they go in with the next merge. |
| **Next Action** | Ask the owner for the go on #2552, then run its steps. Wait for, and act on: the owner's checks (read back, report to `uac-r2`), and each `uac-r2` deploy notice (range check, reply). |

## Decisions in force (detail in CLAUDE.md "Standing directives")
- **W3 create paths** (owner 2026-11-06): CLAUDE.md "Create paths follow fm's W3 direction". 030a is live. **030b is on master** (PR #2629, 57748ebf6d, 2026-11-08 04:43Z, with fm 034 + 052a) and **live on dev in 6dbe69b9e2**. Content read done on 2026-11-08 and sent to fm: OK, no pane change needed. Association, parent link and SPE pointer are now written in the create. The new request members are `[JsonIgnore]`. The id is server-minted, as it already was. The version save (`existingDocumentId` + `isNewVersion`) is outside the diff, and response and refusal bodies are unchanged. When a deploy notice's range includes 57748ebf6d, cite this read.
- **Roles**: add-in user = Core User + Office Add In User; service and demo accounts never get the add-in role; every add-in role holder has Core User or System Administrator (read back 2026-11-06).
- **ebilling contract** (#2552): `"firm": null` prefill accepted; "Firm: none" kept; §12 reviewer refusals are on no pane route; 082c-3 `warning` is SPA-only.

## Recent deploy checks (newest first; older ones in git log)
- 2026-11-12 6dbe69b9e2 (master a654da59c4 + spike 755987b385; range from d53d67f604). Office server changes: 030b (read OK); 022 fix round 3, where quick-create returns 400 `record_creation.mapping_source_secure_filing` with the service's sentence (the pane shows the server detail, task 053); regarding-pair display columns; the uncalled attachment creator was deleted; whitespace only. Add-in client: one comment in EmailView (#2666). **Behaviour change (#2666, ontology D-217):** a Word Email-tab send filed to a record is now archived as an `.eml` even though the pane sends `archiveToSpe: false`. The server decides; no pane change. Finance: the 095a route `GET /api/finance/invoice-submissions?internetMessageId=` now exists; its fields and status values (Received, Processing, Accepted, Rejected, Failed) match the pane's reader. No add-in concern.
- 2026-11-07 22487c6537 (spike tip): no add-in concern; flagged "not on master"; uac-r2 confirmed the owner's go.
- 2026-11-07 cc11294300: 030a To Do through RecordCreationService; #2292 shared alternate-key classifier (exact 0x80060891). No add-in concern.
- 2026-11-06 20669df6a9: POST /api/v1/documents caller check does not touch the pane (it uses GET/PUT there).

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
