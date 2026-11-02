# Current Task State — `spaarkeai-word-add-in-r1`

> **Format (2026-10-06):** CURRENT state only, REWRITTEN at each checkpoint. Standing directives + gotchas: project `CLAUDE.md` → "Standing directives & gotchas". History: git log + `notes/handoff-history/` (do not load on recovery; the state before 2026-11-01 is archived verbatim in `notes/handoff-history/2026-11.md`).

> **Last Updated**: 2026-11-02 (no BFF deploy needed; corrections PR #2525 open).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | **133 🔄 Outlook Submit Invoice view** (FULL, opus/high). Live on dev in unified package 1.1.3; first owner UATs done. |
| **Status** | ebilling's **#2507** (097b/c/e/f, 191, 192, 0878ca36ac) **is live on dev**: R3 deployed master 3740e1a105 at 2026-11-02 18:52:25Z (UAC gate passed 19:22:25Z). I read it back myself (Kudu DLL ProductVersion `1.0.0+3740e1a105…`; no `src/server` diff to master edb1a784ba; healthz and healthz/dataverse 200, anonymous /api/me 401). So the owner's deploy go (2026-11-02, "yes deploy whatever needs to be deployed") needed **no BFF deploy**; C:\wtbffdep was removed unused. **Corrections are LIVE in Outlook on dev:** PR #2525 (`correctionAvailable` on InvoiceView) merged at 55ef2ab1ab (2026-11-02 22:03:29Z). SWA deploy run 41533905200 succeeded at 22:05:31Z; the live bundle (`icy-desert-0bfdbb61e.6.azurestaticapps.net`) has the flag on. **No package bump**: the package stays at 1.1.3 and nothing was re-uploaded. C:\wt133s is removed. ebilling 7b was told and is testing a correction. Also waiting on the owner's retest (INV-012.2). |
| **Next Action** | Wait for ebilling's correction test result and the owner's correction UAT (email → Submit Invoice → Existing invoice → "Submit a corrected invoice"). Fix any wrong sentence or behaviour from the submission id they report. ebilling #2526 (151c Email to AP; content OK given, touches no add-in route) merged at 5796774091; not on dev yet (SPA-r3 does the next dev BFF deploy). |

## Submit Invoice (task 133): state
- **Dev:** eBilling is ON (since 2026-10-30 15:21Z). The invoice reader is configured (`ContentUnderstanding__Endpoint`, 2026-11-01 02:54:30Z, ebilling, owner go in both sessions; analyzers sprk_invoice_v2p and sprk_invoice_v2 deployed). The owner updated Integrated apps to package **1.1.3** (#2476); the Submit Invoice ribbon button works in Outlook on the web.
- **Merged to master (auto-deploys the add-in):**
  - #2476: package 1.1.3 and Outlook XML 1.0.23.0.
  - #2492 + #2497: the view starts on the record the email is filed to (`invoiceFiledRecord` in `invoiceSubmissionSeam.ts`, from the pane's email identity), never replaces a user pick, names the filed record when another one is picked, and shows the Save picker's suggestion cards for an unfiled email.
  - #2498: sentences for ebilling task 191 (422 `reader_not_configured`; outcomes `ai.invoice.read.not_configured`, `.disabled`, `.binding_missing` ask an administrator; `.completion_failed`, `.output_invalid` say submit again later).
  - #2501: Accepted + `ebilling.submission.replaced_by_correction` is not "held for review"; it names `replacedBy.trackingNumber` when present (ebilling 097f, EB-D-180).
- **UAT results 2026-10-31:**
  - UAT 1: a 409 `document_core_record_mismatch` was correct (communication 99b5f650 "Fw: PAT-942665 …" is filed under PAT-897705; the owner picked PAT-792345).
  - UAT 2: INV-012.1 (submission 24800a9d) Failed with `ai.invoice.read.not_configured` (since fixed on dev); the view didn't prefill (fixed by #2492).
- **Owner retest pending:** the same email "Invoice Submission - PAT-792345", Submit Invoice; expect PAT-792345 prefilled, Morrison Foerster auto-chosen if it is the only linked firm, INV-012 → tracking **INV-012.2** (a new attempt after Failed, §5), then a final status. If it fails, read `sprk_invoicesubmission` (latest by createdon): `sprk_processingstatusname`, `sprk_outcomereasoncode`.
- **Slice C (needs the owner's explicit go):** remove Invoice from Save and "+ New" in both hosts; tell the owner Word loses Invoice save; then tell ebilling for 096.

## ebilling contract items (peer: ebilling, pipe cc-msg-c961e15ec4fa4e62d8bf9706a34c677d)
- **Task 191** (work fdd3c79b7f, d4849acbe6; not on master or dev): intake 422 `reader_not_configured`, §9 `ai.invoice.*` codes. The client side is merged (#2498).
- **Task 192 `filedUnder`** (work 25778a59b7): optional `filedUnder {entityType: Matter|Project|WorkAssignment, entityId, name}` on 409 `document_core_record_mismatch`, only when the caller can Read it. Client side is merged (#2503): the sentence names the record, and the view offers "Use {name}". ebilling has been told.
- **097b/097c Correction** (in #2507). All four client sentence items are merged (#2510): the intake reasons, the stored outcomes, the record_unavailable addition, and `not_available` retired.
- **097e prefill** (work 8b49cb2432): `GET /api/finance/invoices/{invoiceId}/correction-prefill` → `{invoiceId, firm{id,name}, coreRecord{entityType Matter|Project|WorkAssignment, entityId, name}, invoiceStatus, trackingNumber, firmInvoiceNumber, canCorrect, reason, replacedBy{invoiceId,name}|null}`.
  - `reason` ∈ invoice_status, no_submission, submission_in_progress.
  - Errors: uniform 404 `record_unavailable`, 503, 503 `ebilling.disabled`.
  - `invoiceStatus` set: InReview "In Review", Approved, SentToAp "Sent to AP", Paid, Rejected, ReturnedToFirm "Returned to Firm", Cancelled, Superseded (own sentence: "This invoice has already been replaced by a correction. Correct the newer invoice instead." plus a button selecting `replacedBy`); null or unknown → "in its current state".
  - 097b+c+e are on master and dev (3740e1a105); `correctionAvailable` is turned on by PR #2525.
- **097f** (work task/ebill-097f-superseded-view 0666b656ee): the client side is in #2501.

## Peer duties
- Answer cross-session BFF/Console/deploy notices; for a deploy request verify ancestry, OK on content only, record it here, commit and push.
- **Last OK: R3 dev BFF master 5796774091** (2026-11-02; from live 3740e1a105: 12 src/server files, all ebilling #2526; the Communication changes only act on a signed server-send, and the default path is unchanged; no add-in route touched). Waiting for R3's after-notice.
- **Live on dev: 3740e1a105** (R3, owner go, 2026-11-02 18:52:25Z; replaced fcf01480f4; includes ebilling #2507, field-mapping 039, #2519, #2520, uac #2509, #2516). `ContentUnderstanding__Endpoint` is still set; app settings stay at 225. Rollback snapshot for fcf01480f4: `deploy/rollback/wwwroot-20261102T185123Z`.
- R3's dev BFF deploy rules: owner go naming the SHA; OKs from uac-r2, ontology, field-mapping and ebilling; Router green; `scripts/Set-ChildRecordCreatorPersonSchema.ps1 -Verify` exits 0 first (plus ebilling's four `-Verify` scripts, see ebilling 7b's message 2026-11-02); after the deploy, `Ebilling__Enabled=true` and `Ebilling__StoredFileReconcileEnabled=false`; after-notices to every peer, then uac-r2's 30-minute gate. Read the live commit back from the Kudu VFS `Sprk.Bff.Api.dll` ProductVersion.
- Peers: R3 `cc-msg-d998caed4166082c2c443072ac542a83` (name spaarke-wt-spa-external-access-platform-f2); ebilling 7b `cc-msg-a027c144054932864ee427c120b4e2c1` (ignore 9c); UAC session name `spaarke-wt-unified-access-control-r2-db`; ontology `cc-msg-12a5ac7fc59e21f736827730b8d44c8a`.

## Gotchas found this session
- PowerShell `git commit -m` with embedded `"` breaks the argument; use `git commit -F <file>`.
- Local jest: stale gitignored `node_modules` in `src/client/shared/Spaarke.Communication.Components` and `Spaarke.UI.Components` (from 2026-10-08) load a second React and fail view/picker suites with "Invalid hook call". CI is unaffected. Rename them aside for a run (they can be locked by another process); the owner was offered their deletion.
- `App.invoiceRoute.test.tsx` replaces the whole `InvoiceView` module: export pure helpers from a service module, never from the view file.
- When the stale `node_modules` are locked, run jest with a scratchpad config that spreads `jest.config.js` and maps `^react$`, `^react/(.*)$`, `^react-dom$` and `^react-dom/(.*)$` to `office-addins/node_modules` (set `rootDir` to office-addins). This gives one React without renaming anything.
- `Gated jest suites (office-addins)` is NOT a required check, so auto-merge can merge a PR that breaks it. Wait for it before relying on a merge.

## Owner items (open)
- **Outlook desktop sign-in 400 "Request Too Long":** cause = Windows SSO credentials for 4 work accounts, plus a dead Helios IP PRT (last refresh 2026-01-28). Steps given: (1) clear login.microsoftonline.com site data and turn off Edge work SSO, as a test; (2) disconnect RightHub Groups (`rschroeder@righthub.com`); (3) leave Helios IP only after preparation (local admin password, BitLocker key, copy `OneDrive - Helios IP` 47 GB, back up .claude/.ssh/.gitconfig/Code, unlink OneDrive), then disconnect, restart, and re-add Spaarke (join recommended). Re-check with `dsregcmd /status` after.
- Excel/PowerPoint add-in: answered as a moderate project; offered a researcher check of unified-manifest and custom-XML support first. No go yet.
- UAT 042, 080, 121, 127, 078; PCF re-sync; A9 guest sign-in; the Ralph Dewey retest (pass the result to R3); then 090 wrap-up (`/test-diet`).
- Post-deploy check (UAC #2468 fix): a child-BU user's unfiled Outlook save; read back the sprk_document owner and BU (expect the saver's BU team).
- Main checkout `C:\code_files\spaarke`: leave its uncommitted files alone. C:\wtqa1906 is the owner's.
- Cleanup later (Remove-WorktreeSafe): C:\wt2412, C:\wt2412m, C:\wt2416, C:\wt2416c.
