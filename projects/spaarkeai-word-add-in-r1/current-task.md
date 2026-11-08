# Current Task State — `spaarkeai-word-add-in-r1`

> **Format (2026-10-06):** CURRENT state only, REWRITTEN at each checkpoint. Standing directives + gotchas: project `CLAUDE.md` → "Standing directives & gotchas". History: git log + `notes/handoff-history/` (do not load on recovery).

> **Last Updated**: 2026-11-08 (by context-handoff). This project's session moved from VS Code to the **Claude Desktop session `word-add-in-r1`**. The VS Code session (`spaarke-wt-spaarkeai-word-add-in-r1-7b`) is retiring.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | **133 🔄 Outlook Submit Invoice view** (FULL, opus/high). Live on dev; unified package **1.1.4** uploaded by the owner 2026-11-03 (Invoice pane tab; Submit Invoice / Create To Do ribbon buttons removed). |
| **Status** | Waiting on others; no work in progress. Work branch is clean and pushed; nothing uncommitted. |
| **Open PR** | **#2552 draft, HOLD** (worktree C:\wt185f, `feat/invoice-optional-firm`, head 6af195647d): optional firm (ebilling EB-D-185) + "Submit it again." sentences for the 095c-4 transient Failed codes. Merge only after ebilling sends the master SHA of the dev BFF deploy carrying 095c-2..4. Then: merge master into the branch, `gh pr ready 2552`, wait for every check, `gh pr merge 2552 --auto --merge`, confirm the SWA deploy, owner retest (no-firm submit + correction of it), remove C:\wt185f with Remove-WorktreeSafe. |
| **Live dev BFF** | **22487c6537 since 2026-11-07T20:06:34Z** (uac-r2; gate PASS). Tip of `spike/spaarkeai-mcp-phase0` (draft PR #2615), not on master; owner-approved ride-along (see CLAUDE.md "Deploy"). Carries 049b + MCP spike routes; checked: no add-in concern. Lacks master's 049c (23567b2f93). |
| **Owner checks open** | On the live build: (1) an Office save filed to a matter, (2) an Office save with no related record, (3) one Submit Invoice from an email. Then read back `sprk_document` rows created after 2026-11-07T20:06:34Z (`sprk_searchindexcompletedon`, `sprk_searchindexname`, owner, BU) and the invoice submission, invoice and line rows; send the result to `uac-r2` naming 22487c6537. It stands for the next master build on access and correctness. **After the next master build is live, ask the owner for one Outlook To Do save and read it back** (049c changes the create path); carry no timings across. None made yet. |
| **Last merge to master** | **#2607** (f9ba615b09, 2026-11-06): the To Do form can be saved again after a failed create; checks, gate and SWA deploy all passed. The work branch is 5 commits ahead of master (checkpoints only); they go in with the next merge. |
| **Next Action** | Wait for, and act on: the owner's three checks (read back, report to `uac-r2`); fm's 030b PR (content read before its dev deploy, as for 030a); ebilling's 095c SHA (#2552 steps above); ebilling's 095a/095b/151e PR + SHA (banner, invoice Files section); each `uac-r2` deploy notice (range check, reply). |

## Decisions in force (detail in CLAUDE.md "Standing directives")
- **W3 create paths** (owner 2026-11-06): CLAUDE.md "Create paths follow fm's W3 direction". 030a is live; 030b (Office document save, named exception) is not built yet. Its creates-only guard leaves the version save (`existingDocumentId` + `isNewVersion`) and the re-file (`documentId`) working, with must-not-fire tests (fm confirmed).
- **Roles**: add-in user = Core User + Office Add In User; service and demo accounts never get the add-in role; every add-in role holder has Core User or System Administrator (read back 2026-11-06).
- **ebilling contract** (#2552): `"firm": null` prefill accepted; "Firm: none" kept; §12 reviewer refusals are on no pane route; 082c-3 `warning` is SPA-only.

## Recent deploy checks (newest first; older ones in git log)
- 2026-11-07 22487c6537 (spike tip): no add-in concern; flagged "not on master"; uac-r2 confirmed the owner's go.
- 2026-11-07 cc11294300: 030a To Do through RecordCreationService; #2292 shared alternate-key classifier (exact 0x80060891). No add-in concern.
- 2026-11-06 20669df6a9: POST /api/v1/documents caller check does not touch the pane (it uses GET/PUT there).

## Submit Invoice (task 133): state
- Already-submitted banner (#2533) shows nothing until ebilling 095a is on dev (404 → today's form). Backfill for older submissions is ebilling's, with the owner's OK.
- Invoice form Files section and "Source email" link: ebilling 151e. No approve UI until ebilling 110/111/112 (no interim path).
- Owner retest still open: INV-012.2 / correction UAT.
- **Slice C (needs the owner's explicit go):** remove Invoice from Save and "+ New" in both hosts; tell the owner Word loses Invoice save; then tell ebilling for 096.

## Peers (SendMessage by session name; check `ListAgents` — several moved to Claude Desktop on 2026-11-08)
- `uac-r2` (Desktop): dev BFF deploys, roles. A held message means the owner approves it in that session.
- `spaarke-wt-field-mapping-server-write-pa-69`: field mapping (030b pending).
- `spaarke-wt-ebilling-r1-c7`: ebilling.
- `spaarke-wt-spaarke-ontology-platform-r1-38`: ontology.
- `provisioning-r1` (Desktop): customer provisioning.

## Owner items (open)
- Prod Entra: register the add-in's `brk-…://addins.spaarke.com` redirect on prod `1958aec2` (dev has it; offered via az with a go).
- Teams admin: reject the duplicate "Spaarke portal" submission `5d88b948…` (keep `60055573…`); then tell R3 and legal-ai-accelerator.
- Request Too Long follow-ups on the owner's machine.
- Excel/PowerPoint add-in (no go yet); UAT 042, 080, 121, 127, 078; PCF re-sync; A9 guest sign-in; a named user's retest for R3; 090 wrap-up (`/test-diet`).
- Post-deploy check (UAC #2468): a child-BU user's unfiled Outlook save; read back the `sprk_document` owner and BU.
