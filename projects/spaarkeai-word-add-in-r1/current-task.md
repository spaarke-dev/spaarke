# Current Task State — `spaarkeai-word-add-in-r1`

> **Format (2026-10-06):** CURRENT state only, REWRITTEN at each checkpoint. Standing directives + gotchas: project `CLAUDE.md` → "Standing directives & gotchas". History: git log + `notes/handoff-history/` (do not load on recovery; the state before 2026-11-01 is archived verbatim in `notes/handoff-history/2026-11.md`).

> **Last Updated**: 2026-11-06 (work branch merged with master and records reviewed before /merge-to-master; dev BFF 20669df6a9 live; #2552 held).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | **133 🔄 Outlook Submit Invoice view** (FULL, opus/high). Live on dev; unified package **1.1.4** uploaded by the owner 2026-11-03 (Invoice pane tab; Submit Invoice / Create To Do ribbon buttons removed). |
| **Open PR** | **#2552 draft, HOLD** (C:\wt185f, `feat/invoice-optional-firm`, head 6af195647d): optional firm (ebilling EB-D-185) + "Submit it again." sentences for the 095c-4 transient Failed codes. Merge only after ebilling sends the master SHA of the dev BFF deploy carrying 095c-2..4. Then: merge master into the branch, `gh pr ready 2552`, wait for every check, `gh pr merge 2552 --auto --merge`, confirm the SWA deploy, owner retest (no-firm submit + correction of it), remove C:\wt185f with Remove-WorktreeSafe. |
| **Live dev BFF** | **20669df6a9 since 2026-11-06T16:44:51Z** (deployed by uac-r2; rollback 0f53e053bf; server gate PASS to 17:14:51Z). No Office or Finance server change since d763dc120e. |
| **Owner checks open** | On whatever build is live: (1) an Office save filed to a matter, (2) an Office save with no related record, (3) one Submit Invoice from an email. Then read `sprk_document` rows created after the live time (`sprk_searchindexcompletedon`, `sprk_searchindexname`) and the invoice submission, invoice and line rows; send the result to uac-r2. None made yet (checked after 16:44:51Z). |
| **Last merge to master** | **PR #2604 MERGED** 2026-11-06 as bcdc0e0069 (project records only; Router, client lint and every other check pass; Tier 1/2 skipped as docs-only). Main checkout fast-forwarded to it. Issue #945 commented; board Tasks Completed corrected 127 → 126 (042 is 🔄; the old count read the ✅ in its dependency cell). |
| **Next Action** | Wait for: the owner's three checks; ebilling's 095c SHA (#2552); fm's 030a and 030b PRs for a content read before their dev deploy; ebilling's 095a/095b/151e PR + SHA (banner, invoice Files section). |

## Decisions and alignment (2026-11-04 to 11-06)
- **W3 create paths** (owner 2026-11-06, fm decisions.md, UAC-r2 reviewed): see project `CLAUDE.md` "Create paths follow fm's W3 direction" and the spec ADR Tensions row. fm confirmed the 030b "existing id refused" guard covers creates only; the version save (`existingDocumentId` + `isNewVersion`) and the re-file (`documentId`) get must-not-fire tests.
- **fm 030a** (Office To Do through the service): built on `task/fm-030`, in fm review. Its To Do refusal is **403** OFFICE_009 `source_record_inaccessible` (unchanged body); new: an assignee contact needs AppendTo (Core User has it). The pane shows the server's detail sentence, so no pane change. Suggested to fm (optional): add "or choose a different person" to the sentence.
- **Roles** (owner): Office Add In User needs Core User and is for licensed core users only; Basic User becomes Read-only on `sprk_*` (fm 046c, live apply by uac-r2). The four service and demo accounts do not get the add-in role (2026-11-05). The five add-in holders who lacked Core User were given it (2026-11-06, read back on dev); every add-in role holder now has Core User or System Administrator.
- **ebilling contract** (#2552): `"firm": null` prefill accepted; "Firm: none" kept (ebilling confirmed); §12 reviewer refusals not on any pane route; 082c-3 `warning` is SPA-only, no pane impact.

## Deploy OKs given (newest first; history in git log)
- 2026-11-06 20669df6a9: checked after uac-r2's before-notice; POST /api/v1/documents caller check does not touch the pane (it uses GET/PUT there).
- 2026-11-05 0f6618094b: content OK; the owner held it and it shipped inside 20669df6a9.
- 2026-11-05 0f53e053bf, 2026-11-04 909754869f, 2026-11-03 d763dc120e: content OK; each carried the owner-checks condition above.
- Dev BFF deploys run through uac-r2 (`spaarke-wt-unified-access-control-r2-20`): requests, peer OKs and after-notices go there; the owner gives the go per build in its session (`docs/procedures/dev-bff-deploy-coordination.md`).

## Submit Invoice (task 133): state
- Already-submitted banner (#2533) shows nothing until ebilling 095a is on dev (404 → today's form). Backfill for older submissions (incl. 0d67bcc9 and INV-792345.3/.4) is ebilling's, with the owner's OK.
- Invoice form Files section and "Source email" link: ebilling 151e. No approve UI until ebilling 110/111/112 (no interim path).
- Owner retest still open: INV-012.2 / correction UAT.
- **Slice C (needs the owner's explicit go):** remove Invoice from Save and "+ New" in both hosts; tell the owner Word loses Invoice save; then tell ebilling for 096.

## Peers (SendMessage by session name)
- uac-r2 `spaarke-wt-unified-access-control-r2-20` (dev BFF deploys, roles); ebilling `spaarke-wt-ebilling-r1-7b` / `-c7`; field-mapping `spaarke-wt-field-mapping-server-write-pa-69`; ontology `spaarke-wt-spaarke-ontology-platform-r1-38`.

## Gotchas (this session)
- Use `git commit -F <file>`; git under the Bash tool hangs, so use PowerShell.
- Remote refs are shared by every worktree: another session's fetch can move `origin/master` mid-task. Re-check `git rev-list --count HEAD..origin/master` right before a merge or PR.
- `Gated jest suites (office-addins)` is NOT required: wait for it before relying on a merge.
- `App.invoiceRoute.test.tsx` replaces the whole `InvoiceView` module: export pure helpers from a service module.
- A fresh worktree needs `npm run build` in `src/client/shared/Spaarke.Auth` before office-addins tsc (gitignored `dist`).

## Owner items (open)
- Prod Entra: register the add-in's `brk-…://addins.spaarke.com` redirect on prod `1958aec2` (dev has it; offered via az with a go).
- Teams admin: reject the duplicate "Spaarke portal" submission `5d88b948…` (keep `60055573…`); then tell R3 and legal-ai-accelerator.
- Request Too Long follow-ups on the owner's machine.
- Excel/PowerPoint add-in (no go yet); UAT 042, 080, 121, 127, 078; PCF re-sync; A9 guest sign-in; a named user's retest for R3; 090 wrap-up (`/test-diet`).
- Post-deploy check (UAC #2468): a child-BU user's unfiled Outlook save; read back the `sprk_document` owner and BU.
- Main checkout `C:\code_files\spaarke`: leave its uncommitted files alone. C:\wtqa1906 is the owner's.
- Cleanup later (Remove-WorktreeSafe): C:\wt2412, C:\wt2412m, C:\wt2416, C:\wt2416c.
