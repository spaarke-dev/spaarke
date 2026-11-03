# Current Task State — `spaarkeai-word-add-in-r1`

> **Format (2026-10-06):** CURRENT state only, REWRITTEN at each checkpoint. Standing directives + gotchas: project `CLAUDE.md` → "Standing directives & gotchas". History: git log + `notes/handoff-history/` (do not load on recovery; the state before 2026-11-01 is archived verbatim in `notes/handoff-history/2026-11.md`).

> **Last Updated**: 2026-11-03 (Invoice tab + ribbon cleanup #2535 merged and live; package 1.1.4 ready for the owner's upload).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | **133 🔄 Outlook Submit Invoice view** (FULL, opus/high). Live on dev in unified package 1.1.3; corrections on (#2525). Side fix in flight: **PR #2531** (NAA sign-in for new Outlook / OWA). |
| **Status** | **PR #2531 MERGED** at 21730d5d65 (2026-11-03; 35 pass, 10 skipping, 0 failed incl. the gated jest check). SWA deploy run 41661250220 succeeded; the live `89.bundle.js` (shared by the Outlook and Word panes) carries the new strategy. No package bump (JS only). C:\wt133t removed. |
| **Banner** | **PR #2533 MERGED** (8438fc93f5): already-submitted banner against ebilling contract §11; review + re-check fixes in. Shows nothing until ebilling 095a is on dev (404 → today's form). ebilling: column `sprk_sourceemaildocument` is on dev; their 095a+095b master PR after their review round; then owner deploy go per SHA; backfill for older submissions (INV-792345.3/.4) is put to the owner with the deploy request. |
| **Tab + ribbon** | **PR #2535 MERGED** at e9004d25f3 (2026-11-03): owner UAT items 1+3 — Invoice pane tab (Save · To Do · Invoice · Find, `canGetAttachments`), Submit Invoice + Create To Do ribbon buttons and `?action=` runtimes removed. SWA run 41683650638 succeeded (pane tab live now). Package **1.1.4** = that run's artifact `spaarke-addin-unified-package`, saved to `%USERPROFILE%\Downloads\spaarke-addin-1.1.4\spaarke-addin-1.1.4.zip` (checked: version 1.1.4, prod id e68f3cb1, no CreateTodo/SubmitInvoice/`?action=`). **Owner uploaded 1.1.4 in Integrated apps 2026-11-03.** The old buttons can linger up to 72 h on cached manifests and still open the right tab. C:\wt133u and C:\wt114pkg removed. |
| **Firm optional** | Owner UAT item 2 (Firm "business required", warn but don't block): conflicts with owner decisions D-91/D-106; ebilling is putting the scope to the owner in their session (proposal: In Review + "firm needed" hold, approve refused until a reviewer sets it). Pane change waits for their contract delta, then goes behind a flag. |
| **Owner result 2026-11-03** | New Outlook desktop: no Request Too Long, add-in works with the NAA fix. OWA / Word desktop retest optional. Owner asked where to add the prod redirect: steps given (Entra → App registrations → `1958aec2` → Authentication → Single-page application → Add URI); offered to do it via az with a go. |
| **UAT 1.1.4 (owner, 2026-11-03)** | Ribbon OK. Submit OK (sub 0d67bcc9…, "INV #2025023029.1" Accepted, invoice 7c200dd1…). Sent to ebilling-r1-7b: (b) no banner = 095a not on master and sprk_sourceemaildocument null on this sub (backfill must include it); (e) invoice form shows no file/Document: stored file 12d57bb9… exists, no sprk_document linked to the invoice, submission doc lookups null; (h) no approve UI: ebilling 110/111/112 open; firm still blocks. **ebilling replied:** one master PR (095a + 095b + 151e) after their combined gate → PR # + SHA to us → SPA-r3 deploys on owner go; backfill covers 0d67bcc9 (-WhatIf, owner OK). 151e = invoice-form Files section (PDF + supporting files, Open/Download, "Source email" link), owner OK to deploy web resource + form. No approve UI until 110/111/112 (no interim path). Firm: owner decided hold-until-firm-set; 095c/095d after this PR; contract delta (sentence + code) to us; keep our flag off. |
| **Optional firm (EB-D-185)** | ebilling advance notice 2026-11-03 (not live; 095c-1 on their work branch, route in 095c-2..4): `firmId` optional for New (null/absent = no firm, idempotency binds it empty); correction of a no-firm invoice sends no firm (else 409 `correction_not_allowed` / `different_firm`); new Accepted code `ebilling.submission.firm_missing` → "Submitted without a firm. A reviewer will add the firm before approval." Pane work, not built yet: exact firm_missing entry in `describeAcceptedOutcome`; Firm not blocking in New (warning, empty list allowed, send null); `readCorrectionPrefill` accepts a firm-less §10 body; correction sends no firmId. ebilling answered: §10 no-firm = `"firm": null` (accept absent too); send `firmId: null`; firms route unchanged; they send the SHA after the dev BFF deploy carrying 095c-2..4. **Built:** C:\wt185f, branch `feat/invoice-optional-firm`, commit 8a3aa70c61 → **draft PR #2552** (HOLD: merge only after ebilling's 095c-2..4 is on dev BFF). Gated jest 112/112; typecheck 69 = master. Loading state still blocks Submit (deliberate). Opus adversarial review running. |
| **Deploy OKs** | 2026-11-03: content OK to R3 for dev BFF **d763dc120e** (from live 5796774091; #2541 field-mapping 041 Office indexing worker scope/stamp change + #2546 R3; #2530 no server change). Conditions: Router green; post-deploy Office save smoke (filed + unfiled, index stamped). **LIVE since 22:26:31Z** (R3 readback OK). Smoke: the owner does 2 saves (one filed to a matter, one unfiled); I read sprk_document rows created after 22:26Z for `sprk_searchindexcompletedon` + `sprk_searchindexname`, then tell R3 (f2). None yet. R3's last deploy; next ones are coordinated by uac-r2 (`docs/procedures/dev-bff-deploy-coordination.md`). |
| **Next Action** | Owner: (1.1.4 uploaded) retest Submit Invoice from the pane's Invoice tab on a received email. Waiting on ebilling: the firm-optional contract delta (after the owner's decision) and the 095a/095b PR + SHA (banner). NAA live test (done on new Outlook; others optional) on dev: new Outlook (their machine), OWA, classic Outlook, Word desktop. In the console (F12 / Edge DevTools for the pane), look for `[OfficeNaaStrategy] host=… platform=… NestedAppAuth1.1=…` and "initialized via NAA broker" (or "NAA bridge not available; initialized via standard popup client"). If new Outlook still shows Request Too Long, ask for that console output. Rollback = revert 21730d5d65 on master. |

## PR #2531 — what it does (for the live test and the PR notes)
- `OfficeNaaStrategy.detectNaaSupport()`: gate = `isSetSupported('NestedAppAuth','1.1')` on every host (old version thresholds removed); Word on the web stays on the popup.
- NAA config `redirectUri` = https `/auth-callback.html` (never `brk-`); MSAL's Info log line "Nested App Auth Bridge available: …" sets `isNaaActive` (a guard test checks the line still exists in msal-browser).
- Falls back to the standard popup only on `nested_app_auth_bridge_disabled` or a ServerError carrying an AADSTS code. Never on a cancel (`user_canceled` / `user_cancelled`), no network, no account or interaction required.
- Under NAA, sign-out and clearCache clear the MSAL sessionStorage keys (`logoutPopup` and `clearCache` are unsupported there).
- Known limits: concurrent calls share instance state; a failed fallback creation is memoised; any AADSTS code switches (consent declined included); a host-coded NestedAppAuthUnavailable does not switch; the desktop popup in the WebView is untested.
- Owner Entra item before production: register `brk-9199bf20-a13f-4107-85dc-02114787ef48://addins.spaarke.com` on prod `1958aec2` (dev has it).
- The 400 "Request Too Long" the owner still gets after deleting all cookies: the likely cause is per-account Windows SSO headers (`x-ms-RefreshTokenCredential*`). Diagnostics given: F12 request header names only; Edge "Allow single sign-on for work or school sites" off as a test.

## Submit Invoice (task 133): state
- **Dev:** eBilling on; invoice reader configured; package **1.1.3** in Integrated apps. Corrections live (#2525, 55ef2ab1ab, SWA run 41533905200).
- **Owner OWA retest passed:** INV-792345.3 Accepted (about 21 s).
- **Already-submitted banner (mine, after ebilling 095a is on dev; owner go 2026-11-03):** a MessageBar at the top like Save's "already filed to": "Already submitted on {date} as {tracking} by {name}" plus status. Processing → resume polling; Accepted → offer a correction; Failed → the sentence plus "Submit again"; 404 → today's form. ebilling 095a route: `GET /api/finance/invoice-submissions?internetMessageId=…` → the by-id body plus `submittedOn` (UTC), `submittedBy {name}|null` (sprk_createdbyperson), `isCorrection`; uniform 404, 503, 503 `ebilling.disabled`. ebilling sends the PR number and deploy SHA.
- **095b** (ebilling): in-app bell notification on Accepted / Failed / refused. Nothing client-side.
- "Record Created On" = OOB `overriddencreatedon` (import-only, blank by design); there is no `sprk_recordcreatedon`. Not a defect.
- `…%3E/suggestions` 404 = an encoded Internet Message-ID for an email not yet captured. Expected.
- Owner retest still open: INV-012.2 / correction UAT (email → Submit Invoice → Existing invoice → "Submit a corrected invoice").
- **Slice C (needs the owner's explicit go):** remove Invoice from Save and "+ New" in both hosts; tell the owner Word loses Invoice save; then tell ebilling for 096.

## ebilling contract items (peer: ebilling 7b)
- Client sides merged: 191 sentences (#2498), 192 `filedUnder` (#2503), 097b/c correction sentences (#2510), 097e prefill + `correctionAvailable` (#2525), 097f superseded (#2501).
- #2530 (Entity `sprk_trackingnumber` plus six Read privileges on the add-in user role): content OK given; Deep reads pointed out for the owner to decide.

## Peer duties
- Answer cross-session BFF/Console/deploy notices; for a deploy request verify ancestry, OK on content only, record it here, commit and push.
- **Live on dev: 5796774091** (R3, 2026-11-03 13:29:19Z; read back: Kudu `1.0.0+5796774091…`). Rollback 3740e1a105. Last OK given: this SHA.
- R3's dev BFF deploy rules: owner go naming the SHA; OKs from uac-r2, ontology, field-mapping and ebilling; Router green; `scripts/Set-ChildRecordCreatorPersonSchema.ps1 -Verify` plus ebilling's four `-Verify` scripts exit 0; after the deploy `Ebilling__Enabled=true`, `Ebilling__StoredFileReconcileEnabled=false` (225 settings); after-notices, then uac-r2's 30-minute gate.
- Peers (SendMessage by session name; the old cc-msg pipes no longer resolve): R3 `spaarke-wt-spa-external-access-platform-f2`; ebilling `spaarke-wt-ebilling-r1-7b` (ignore 9c); UAC `spaarke-wt-unified-access-control-r2-db`; ontology `cc-msg-12a5ac7fc59e21f736827730b8d44c8a`.

## Gotchas (this session)
- Use `git commit -F <file>`; git under the Bash tool hangs, so use PowerShell.
- `Gated jest suites (office-addins)` is NOT required: wait for it before relying on a merge.
- `App.invoiceRoute.test.tsx` replaces the whole `InvoiceView` module: export pure helpers from a service module.
- A fresh worktree needs `npm run build` in `src/client/shared/Spaarke.Auth` before office-addins tsc (gitignored `dist`); the baseline is 68 errors.
- Stale `node_modules` in Spaarke.Communication.Components / Spaarke.UI.Components load a second React locally; map react/react-dom to office-addins in a scratchpad jest config.

## Owner items (open)
- Teams admin: reject the "Spaarke portal" submission `5d88b948…` (a Developer Portal duplicate; keep `60055573…`). Once it's done, tell R3 and legal-ai-accelerator.
- Request Too Long: header-name diagnostics; remove the RightHub work account; Helios IP only after its preparation steps (local admin password, BitLocker key, OneDrive copy, backups), then `dsregcmd /status`.
- Excel/PowerPoint add-in (no go yet); UAT 042, 080, 121, 127, 078; PCF re-sync; A9 guest sign-in; the Ralph Dewey retest (pass the result to R3); 090 wrap-up (`/test-diet`).
- Post-deploy check (UAC #2468): a child-BU user's unfiled Outlook save; read back the sprk_document owner and BU.
- Main checkout `C:\code_files\spaarke`: leave its uncommitted files alone. C:\wtqa1906 is the owner's.
- Cleanup later (Remove-WorktreeSafe): C:\wt2412, C:\wt2412m, C:\wt2416, C:\wt2416c.
