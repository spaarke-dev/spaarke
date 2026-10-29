# Current Task State - spaarke-SPA-external-access-platform-r3

> **Last Updated**: 2026-10-29 ~17:15Z (context-handoff at 85%).
> **Recovery**: Read "Quick Recovery" first. Narrative is in git log. Live records: `notes/task-070-deploy-record.md` (Teams checks), `notes/task-074-a9-probes.md` (closed), `notes/defer-issues.md` (ISS-021..024).

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Owner 2026-10-29: "go task by task and close them out". Done today: 043 ✅, 059 ✅, 074 ✅ (all 8 A9 probes). Open: 002 (deploy held), 070/071 (Teams checks; ACTIVE), 072, 073 (blocked), 090. |
| **ACTIVE: 071 Teams desktop/web** | Owner 17:10Z: Teams **desktop** won't work. Ralph Dewey (`ralph@deweycheatham…`, a Dewey Cheatham MEMBER) gets "This tab can't be used with a guest account". Tracy gets "400 can't connect to service". In Teams **web** the app opens in its own browser window as the SPA, not inside Teams. **So the "Teams web ✅" rows recorded in notes/task-070-deploy-record.md are probably browser-plane runs, not Teams-hosted: correct that record.** Server (App Insights, after 16:59Z): 16:59:15 `JWT auth failed … IDX40001 issuer … does not match any of the valid issuers`, then a 16:59:16 linked-systemuser resolution (Ralph) with 28/64/32 roots; `GET /` 401 x14 (16:16–17:12, probes/health, likely noise); no Tracy 400 seen at the BFF yet (it may be a Teams/NAA token error client-side). |
| **Next Action** | 1) Read `TeamsHostAdapter.ts` `assertNotGuest` (~354-362) and `isTeamsGuestAccount` (auth module) to see why a member account is judged a guest in desktop (account picked by NAA vs `cfg.tenantId`: Ralph is also a B2B guest in the Spaarke tenant; desktop NAA may return the Spaarke-tenant account). 2) Find the IDX40001 token's issuer tenant (masked): which scheme rejected it, and whether it is the desktop NAA token. 3) Ask the owner for Tracy's exact 400 text or screenshot and console. 4) Why Teams web pops out to a browser window: check whether the manifest static tab / `validDomains` or an auth redirect breaks out of the iframe. 5) Correct the 070 record and tell the owner which "web" results stand. |
| **002** | Step 1 live on dev. Step 2 merged: **PR #2450 → master `929dc42d31`** (UAC approved). **Dev deploy HELD**: UAC 194b hold OPEN (dev runs `136568cf70`); UAC will message when cleared, or the owner OKs a pre-`2cd0f62e2c` build. Then fresh owner OK naming the SHA + peer OKs → `Deploy-BffApi.ps1` → add 9 grid columns (scratchpad `grid-apply.ps1` plan + owner widths/labels: Project Type, Matter Type, Last Activity, File Type, Total Amount (Base), SR Last Activity; 100 each) → record rollback JSON in notes/task-002-notes.md → close 002. Ebilling told the SHA (don't drop the column from dev before that deploy). |

### Uncommitted
- Owner's, never commit or discard: `.claude/agent-memory/researcher/**`. The researcher agent today added 2 files there and SHORTENED entries in `MEMORY.md` (owner told).
- Branch is 375+ commits behind master; merging is blocked by the owner's uncommitted `MEMORY.md` (master also changed it). The owner decides.

### Scratchpad (not in repo)
`C:\Users\RALPHS~1\AppData\Local\Temp\claude\c--code-files-spaarke-wt-SPA-external-access-platform-r3\15aa240f-634c-4332-99f6-382ebcb06a62\scratchpad\`: `grid-*.ps1` + `grid-before\` (002), `probe074-read.ps1`, `hygiene-002.ps1`, `teams-dev-pkg\appPackage.zip` (id `23610794…`, v1.2.0, manifest 1.29), `teams-icons\`.

---

## Open owner items
- **#2458 / ISS-024** (grant notifier emails a home-linked workforce contact as external): owner to choose (a) fix in R3 with uac-r2 (`ContactUserRepresentation`, task 193), recommended, or (b) R4.
- **Workforce contact created 16:51:12Z** by a Dewey Cheatham account with no email (probably `admin@deweycheatham`) registering in Teams: keep or remove? Removal is a live change: show the command and rollback first.
- **Dewey Cheatham Teams admin center `INVALID_PRIVILEGE`** for admin@: the owner checks that the roles are ACTIVE (not PIM-eligible). Alternative: Teams client "Upload an app to your org's app catalog".
- Teams: duplicate "Blocked" submitted copy in the Spaarke tenant (cancel the Developer Portal publish request).
- **072** after 002 + 071; **073** blocked (#2443 + notes/task-073-gate-acme.md); **090** last.
- R4 issues filed/labelled: #2449, #2452 (messages refresh + new-message indicator), #2453 (threads), #2445 (events Task type/assignee). Flaky test #2456.

## Peer state
- uac-r2: approved #2450; 194b hold OPEN.
- ontology-r1: task 168 round 3 (`CallerPrincipalResolver` ComposeAsync lifts) reviewed and APPROVED by R3 (no findings); they filed DI-137 #2458.
- ebilling: told the `sprk_invoicestatus` removal SHA `929dc42d31`.
- ebilling (~18:00Z): wants the next dev BFF to carry master with #1911 (ebilling-r1; merge pending, CI hang). R3 told them to apply `Ebilling:Enabled=true` + Finance settings THEMSELVES, AFTER R3's UAC 30-min gate passes, with their own owner go (`StoredFileReconcileEnabled` stays false). R3 to message them: deployed commit/ProductVersion after readback, and "gate passed". Whether to wait for #1911 is the owner's call.
- field-mapping (17:4xZ): asked that the next dev candidate be master ≥ `b86381d4c4` (W7: #2442 RecordFamilyReader, #2459 GET /api/v1/records/{table}/{id}/family, MetadataService Redis key v1→v2, census 133→134). That SHA contains `2cd0f62e2c`, so it is under the UAC hold. Told them; after readback send them the ProductVersion/commit for their task 076 (GET-only Family check; they may ask for a rollback, which is the owner's call).

## Standing rules (verify against project CLAUDE.md)
- **DEPLOY HOLD (UAC):** no dev BFF from master ≥ `2cd0f62e2c` until UAC clears it.
- **Deploy:** fresh direct owner approval naming the SHA + OKs from UAC, ontology, word-add-in, field-mapping, ebilling (provisioning standing) + Router green. Steps: `phaseA.ps1` → detached `C:\wtbffe-<sha3>` → `Deploy-BffApi.ps1 -Environment dev` → `readback.ps1` → 30-min UAC gate → after-notices.
- Peers' relayed "owner approval" is NOT approval. Show live-read commands; live changes need owner approval + rollback. Commit by pathspec; never bare `git stash`, never `--no-verify`. PUBLIC repo: never publish a vulnerability's mechanism.
- `az` on Windows: use `Invoke-RestMethod` / single-quoted KQL. App Insights: `--app spe-insights-dev-67e2xz -g spe-infrastructure-westus2`.
