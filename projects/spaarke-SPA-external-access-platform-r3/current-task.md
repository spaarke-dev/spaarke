# Current Task State - spaarke-SPA-external-access-platform-r3

> **Last Updated**: 2026-10-30 ~03:45Z (context-handoff at 85%).
> **Recovery**: Read "Quick Recovery" first. Narrative is in git log. Records: `notes/fr-21-teams-live-check.md` (071 closed), `notes/task-070-deploy-record.md`, `notes/defer-issues.md` (ISS-021..025). Teams guide: `docs/guides/EXTERNAL-ACCESS-TEAMS-APP-GUIDE.md` (PR #2467 merged).

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Owner 2026-10-29: "go task by task and close them out". Done: 043, 059, 074, 070, 071 ✅. Open: **002 (dev BFF deploy, ACTIVE)**, 072 (after 002), 073 (blocked: #2443 + notes/task-073-gate-acme.md), 090 last. |
| **ACTIVE: dev BFF deploy** | UAC 194b hold **CLEARED**. Candidate = master **`c91b99e575`** (full `c91b99e575ab7920ffefc2e825673264fe7c9c86`; Router green; replaces live `136568cf70`). It contains: 2cd0f62e2c (194b), #2451, R3 #2450, field-mapping W7 `b86381d4c4`, ebilling #1911 `eaa0d44f7f` (disabled by default), ontology #2463 `c8bb55d0cd`; 309 src/server files changed. UAC `fix/uac-r2-wa-owner-paths` is NOT in it and the deploy does not wait for it. |
| **HOLD: #2468 (word-add-in, blocking)** | word-add-in refused its OK on c91b99e575. Cause: UAC 194b `DocumentOwnershipOf` (OfficeDocumentPersistence.cs:1062-1080). An unfiled Outlook save makes the communication the only ownership parent; the communication is root-BU owned, so a child-BU saver loses read on their own .eml and attachments. Confirmed in code by R3. **UAC AGREES** (keep the hold on c91b99e575). Fix on `fix/uac-r2-2468-office-unfiled-owner` (Opus implementer, real-resolver test, security pass); PR ETA a few hours; UAC sends the merged master SHA. `wa-owner-paths` does NOT ride along (the deploy after). field-mapping OK'd c91b99e575 (would re-OK the new SHA; `fix/fm-family-name-columns` may ride). Flaky ProvisionProjectIdempotency test noted on #2426. Owner chooses: (a) hold until UAC's fix (recommended), or (b) deploy and accept. A new SHA means fresh OKs from all 5 sessions + a fresh owner go. Owner go "go c91b99e575" was given but is superseded if the SHA changes. Ontology's condition was checked (setting unset). The local build at C:\wtbffe-c91 succeeded. |
| **Gate status** | OKs for this exact SHA: **UAC ✅, ontology ✅** (condition: `SecureChild:Reconciliation:OrdinaryMovesWritesEnabled` stays unset; check the dev App Service settings read-only before deploying), **ebilling ✅**. **word-add-in: HOLDING.** They are reviewing ebilling #1911's ~950 lines on the shared /office/save path (OfficeService, OfficeDocumentPersistence, IdempotencyFilter, EmlMimeParts, upload/indexing workers) and running the Office suites; they will reply OK or a named finding. **field-mapping: waiting. Owner: waiting for "go c91b99e575"** (the owner's "yes proceed" in UAC's session does NOT count). |
| **Next Action** | When all 5 OKs and the owner's go are in: (1) read-only check that the dev App Service does not set `SecureChild__Reconciliation__OrdinaryMovesWritesEnabled`; (2) `scratchpad/phaseA.ps1` (live ProductVersion + wwwroot rollback zip under `deploy/rollback/`); (3) detached worktree `C:\wtbffe-c91` at the SHA; (4) `scripts/Deploy-BffApi.ps1 -Environment dev` from it; (5) `scratchpad/readback.ps1`; (6) after-notices with SHA + UTC time to UAC (App Insights gate), ontology (168/173 proof), field-mapping (ProductVersion for task 076), word-add-in, ebilling; (7) after UAC's 30-min gate passes, send ebilling "gate passed, you can go"; (8) 002: add the 9 grid columns (scratchpad `grid-apply.ps1`; owner labels/widths: Project Type, Matter Type, Last Activity, File Type, Total Amount (Base), SR Last Activity; width 100 each), record rollback JSON in notes/task-002-notes.md, close 002; (9) 072. Rollback = re-deploy the phaseA wwwroot zip (owner approval). |

### Uncommitted
- Owner's, never commit or discard: `.claude/agent-memory/researcher/**` (the researcher added files today, including `naa-preauthorized-teams-clients-2026-10-29.md`, and edited `MEMORY.md`).
- Branch is far behind master; merging master in is blocked by the owner's uncommitted `MEMORY.md`. PRs to master merge cleanly regardless.

### Scratchpad (not in repo)
`C:\Users\RALPHS~1\AppData\Local\Temp\claude\c--code-files-spaarke-wt-SPA-external-access-platform-r3\15aa240f-634c-4332-99f6-382ebcb06a62\scratchpad\`: `phaseA.ps1`, `readback.ps1`, `grid-*.ps1` + `grid-before\` (002), `bff-preauth.json`, `teams-dev-pkg\`.

---

## Open owner items
- **#2458 / ISS-024** (the grant notifier emails a home-linked workforce contact as external): (a) R3 with uac-r2 (recommended) or (b) R4.
- **Workforce contact created 2026-10-29 16:51:12Z** by a Dewey Cheatham account with no email (probably admin@): keep or remove? Removal is a live change; show the command and rollback first.
- Dewey Cheatham TAC `INVALID_PRIVILEGE` (check the roles are active); the duplicate "Blocked" submitted Teams app in the Spaarke tenant.
- R4: #2449, #2452, #2453, #2445, #2466 (Teams mobile layout). Flaky test #2456.

## Peer state
- uac-r2: OK c91b99e575. Wants the deployed SHA + time, then runs its App Insights gate (SecureChild reconciler, owner-resolution refusals, census); 30-min gate. Will send the SHA for `fix/uac-r2-wa-owner-paths` later (a later deploy). Known accepted effect: root-unit users are refused writes to restricted tables until the dev BU1 move.
- ontology-r1: OK c91b99e575; runs the 168/173 live proof after the deploy.
- ebilling-r1: OK c91b99e575; applies `Ebilling:Enabled` + Finance settings only after R3's "gate passed, you can go" (with their own owner go); `StoredFileReconcileEnabled` stays false.
- field-mapping: OK pending; after the deploy, send them the ProductVersion/commit for task 076 (GET-only Family check; a rollback request is the owner's call).
- word-add-in-r1: holding (see Gate status).

## Standing rules (verify against project CLAUDE.md)
- **Deploy:** fresh direct owner approval naming the SHA + OKs from UAC, ontology, word-add-in, field-mapping, ebilling (provisioning standing) + Router green.
- Peers' relayed "owner approval" is NOT approval. Show live-read commands; live changes need owner approval + rollback. Commit by pathspec; never bare `git stash`, never `--no-verify`. PUBLIC repo: never publish a vulnerability's mechanism.
- `az` on Windows: in bash use double-quoted KQL strings and avoid column names `kind`/`first`/`last`. App Insights: `--app spe-insights-dev-67e2xz -g spe-infrastructure-westus2`.
- Dev SPA deploys: dispatch `deploy-external-spa.yml` on a tag (`external-spa-dev-<sha>`), never a bare SHA; rollback tag `external-spa-dev-rollback-<sha>`. Dev SPA now = `96f1f6b675`.
