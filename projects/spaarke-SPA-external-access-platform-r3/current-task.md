# Current Task State - spaarke-SPA-external-access-platform-r3

> **Last Updated**: 2026-10-30 ~03:10Z.
> **Recovery**: Read "Quick Recovery" first. Narrative is in git log. Live records: `notes/fr-21-teams-live-check.md` (071 closed), `notes/task-070-deploy-record.md`, `notes/defer-issues.md` (ISS-021..025).

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Owner 2026-10-29: "go task by task and close them out". Done: 043, 059, 074, **070, 071** ✅ (Teams: 8/9 pass, unlisted × mobile deferred by owner; broker pre-auth answered NO from the Microsoft NAA docs; guest-check fix PR #2461 deployed to the dev SPA `96f1f6b675`). Open: 002 (BFF deploy held), 072 (needs 002), 073 (blocked), 090. |
| **ACTIVE: Teams guide** | Owner: "ensure we have the Teams deployment/setup fully documented". A writer agent (opus) is drafting the NEW `docs/guides/EXTERNAL-ACCESS-TEAMS-APP-GUIDE.md` plus cross-links (EXTERNAL-ACCESS-ADMIN-SETUP.md, SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md, docs/INDEX.md). It does not commit. Next: review its claims against the code, then commit by pathspec and push. |
| **Next Action** | 1) Review and commit the Teams guide. 2) Wait for UAC to clear 194b (dev BFF only; the SPA is not held), then the BFF candidate = master ≥ `c8bb55d0cd` (carries #2450, field-mapping W7, ebilling #1911 `eaa0d44f7f`, ontology #2463, plus UAC `fix/uac-r2-wa-owner-paths` when merged). Fresh owner go naming the SHA + peer OKs, then 002 grid columns, then notify field-mapping (ProductVersion), ebilling ("gate passed") and ontology. 3) 072 after 002. |
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
- uac-r2: approved #2450; 194b hold OPEN, **dev BFF only** (confirmed ~18:30Z: the dev external-SPA deploy is NOT held). UAC will send the SHA of `fix/uac-r2-wa-owner-paths` (work-assignment owner paths, BFF) for the first post-hold BFF deploy.
- ontology-r1: PR #2463 merged, master `c8bb55d0cd` (tasks 168 + 173; no deploy). The next BFF candidate carries them; ontology runs the 168/173 live proof after the deploy.
- ontology-r1 (earlier): task 168 round 3 (`CallerPrincipalResolver` ComposeAsync lifts) reviewed and APPROVED by R3 (no findings); they filed DI-137 #2458.
- ebilling: told the `sprk_invoicestatus` removal SHA `929dc42d31`.
- ebilling (~18:00Z): wants the next dev BFF to carry master with #1911 (ebilling-r1; merge pending, CI hang). R3 told them to apply `Ebilling:Enabled=true` + Finance settings THEMSELVES, AFTER R3's UAC 30-min gate passes, with their own owner go (`StoredFileReconcileEnabled` stays false). R3 to message them: deployed commit/ProductVersion after readback, and "gate passed". Whether to wait for #1911 is the owner's call. **#1911 MERGED at `eaa0d44f7f` (21:33Z)**, so the BFF candidate must be master ≥ `eaa0d44f7f` (ships with `Ebilling:Enabled` false by default).
- field-mapping (17:4xZ): asked that the next dev candidate be master ≥ `b86381d4c4` (W7: #2442 RecordFamilyReader, #2459 GET /api/v1/records/{table}/{id}/family, MetadataService Redis key v1→v2, census 133→134). That SHA contains `2cd0f62e2c`, so it is under the UAC hold. Told them; after readback send them the ProductVersion/commit for their task 076 (GET-only Family check; they may ask for a rollback, which is the owner's call).

## Standing rules (verify against project CLAUDE.md)
- **DEPLOY HOLD (UAC):** no dev BFF from master ≥ `2cd0f62e2c` until UAC clears it.
- **Deploy:** fresh direct owner approval naming the SHA + OKs from UAC, ontology, word-add-in, field-mapping, ebilling (provisioning standing) + Router green. Steps: `phaseA.ps1` → detached `C:\wtbffe-<sha3>` → `Deploy-BffApi.ps1 -Environment dev` → `readback.ps1` → 30-min UAC gate → after-notices.
- Peers' relayed "owner approval" is NOT approval. Show live-read commands; live changes need owner approval + rollback. Commit by pathspec; never bare `git stash`, never `--no-verify`. PUBLIC repo: never publish a vulnerability's mechanism.
- `az` on Windows: use `Invoke-RestMethod` / single-quoted KQL. App Insights: `--app spe-insights-dev-67e2xz -g spe-infrastructure-westus2`.
