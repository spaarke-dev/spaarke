# Current Task State — email-communication-intelligence-r2

> **Last Updated**: 2026-08-31 (task 090 wrap-up — automatable close-out artifacts DONE; project gated on 044 live UAT)
> **Recovery**: Read "Quick Recovery" first. Wrap-up ran; ONE task (044) is operator-gated on an Office host.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Status** | **WRAP-UP DONE except the 044 gate.** 090 close-out artifacts all written (test-diet, doc-drift, publish-size, CVE, lessons-learned, r5 coordination, INDEX). Project NOT yet flipped to Complete — blocked on **044** (Pillar B add-in live UAT, operator-gated). |
| **Branch** | `work/email-communication-intelligence-r2` · has **uncommitted** wrap-up notes + INDEX/TASK-INDEX/r5-coord/current-task edits (need commit + push + PR). |
| **Last merge** | PR **#911** MERGED 2026-08-31 → `3c577bce4` (infinite scroll + ADR-051). master protected (ruleset `21824191`, required check `Router`; use `/merge-to-master` Path A). |
| **090 artifacts (this session)** | `notes/test-diet-report.md` (clean, 0 deletes) · `notes/drift-audit-2026-08-31.md` (clean) · `notes/lessons-learned.md` · `notes/044-addin-deploy-runbook.md` · publish ~**44 MB** compressed (≤60), **0 HIGH CVE** · r5 coord §10 close-out stamp · `projects/INDEX.md` row → WRAP-UP. |
| **Next Action** | (1) Commit + push these wrap-up edits (PR via Path A). (2) **044**: operator runs `deploy-office-addins.yml` + live NAA smoke at an Office host (see `notes/044-addin-deploy-runbook.md`), then flip 044→✅. (3) Then flip README→Complete + all-✅ + reset current-task→none. |

## Pillar B add-in UAT — deep-fix arc (2026-08-31, deployed to dev)
Live UAT surfaced 6 issues (full record: `notes/pillar-b-uat-findings-2026-08-31.md`; GitHub #919). All deployed to dev (`spaarke-bff-dev` hash-verified + healthy; add-in SWA from `cfae9cdc1`):
- **#1 auth** — added NAA broker SPA redirect (`brk-9199bf20-…`/`brk-multihub://icy-desert-…`) to add-in Entra reg `c1258e2d-…` via `az rest`. Fold into task 004 / auth-deployment-setup.
- **#3 priority mapping** (`UploadFinalizationWorker` `192350xxx`→`100000xxx`) — **PRE-EXISTING MASTER BUG**; aborted every email save. `c0bd37fdc`.
- **#4 attachment gate** (`OfficeJobQueue` HasAttachments from SelectedAttachmentFileNames). `cfae9cdc1`.
- **#6 contacts-sync 400** (`RecordSyncJob` `parentcustomerid`→`_parentcustomerid_value`) — **PRE-EXISTING MASTER BUG**. `35b12c560`.
- **#2 real entity search** (`OfficeService.SearchEntitiesAsync` real Dataverse queries, replaces stub; +4 tests). `f5f4362ee`.
- **#5 email archives not AI-indexed** — OPEN, not root-caused (downstream analysis/index job). Tracked #919.
- **⚠️ #3 + #6 are pre-existing master bugs** — deployed to dev but MUST reach master via PR. Branch HEAD `f5f4362ee` (6 commits past the wrap-up docs). **Awaiting operator re-test** of File-to search + save-with-attachments (verify via Dataverse).

---

## What's DONE + MERGED (do not redo)
- **UAT rounds 4 + 5** (14 reconcile-UI fixes) — merged.
- **Item 1 — true infinite scroll**: `useLazyLoad` `hasMore` = `moreRecords === true || page-was-full` (MDA `Xrm.WebApi` strips the FetchXML `morerecords`/paging-cookie → the "shows only 25" trap). Reconciliation grid pages at 50. **UAT-confirmed working by owner.**
- **Canonical thin scrollbar**: DataGrid `gridScroll` uses `thinScrollbarStyle` (drift converged).
- **ADR-051** (`.claude/adr/`) + pattern `.claude/patterns/ui/infinite-scroll-list.md` + `thin-scrollbar.md` update + shared-lib `CLAUDE.md` section + CHANGELOG — **infinite lazy-scroll + thin scrollbar is now the repo standard; NO pagination (no down-arrow / prev-next / numbered pages / "Load more").**

## 3 REMAINING ITEMS (operator-gated)
1. **2d / 2e — archive `.eml` + attachments** (owner is UAT'ing). **Code is correct; these are DATA gaps**: only 6/126 email archives carry `sprk_relatedcommunication`; needs-review emails have no `sprk_communicationattachment` rows. Options: (a) backfill dev data — REQUIRES operator approval (never silently mutate dev rows), or (b) treat as ingestion concern (project item 064). Test with an attachment/archive-bearing email via the "Email Review All" view (e.g. "Fw: LITG-119896 Monte Rosa…" `d0d3f282-…`, 4 attachments).
2. **Task 044** — Deploy Pillar B Outlook add-in to Azure SWA (🔲 — deploys were paused; needs a live Office host).
3. **Task 090** — Project wrap-up: `/test-diet` (reconcile tests added this project vs ADR-038 build-vs-maintain) + `doc-drift-audit` + publish-size check. Terminal close-out task.

## Build / deploy / test reference
- Libs (order): `Spaarke.UI.Components` → `Spaarke.Auth` → `Spaarke.Communication.Components` (`npm run build` in Comm runs build:deps).
- Code page: `cd src/solutions/CommunicationReconciliation && rm -rf dist node_modules/.vite .vite && npm run build` → `dist/sprk_communicationreconciliation.html`.
- SpaarkeAi: `cd src/solutions/SpaarkeAi && rm -rf dist node_modules/.vite .vite && npm run build` → `dist/spaarkeai.html`.
- Deploy: `pwsh scripts/Deploy-WebResourceInline.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com -WebResourceName {sprk_communicationreconciliation|sprk_spaarkeai} -FilePath …/dist/*.html` (needs `az login` = ralph.schroeder@spaarke.com / Spaarke Dev).
- Tests: `npx jest EmailAssociationsAndTracking ReconciliationWorkspace ReconciliationGrid ReconcileTabs useLazyLoad` in the shared-lib dir. **Known pre-existing failures (unrelated, on clean base too): `EmailTrackingPanel` ×3 + `triageColumnRenderers` sort ×1.**

## Merge / protection notes (IMPORTANT for next merge)
- **master is PROTECTED** (ruleset `21824191`, added 2026-08-29): PR required, required check = **`Router`** (literal name, NOT "CI / Router"), `strict: false`, force-push blocked. **Direct `git push origin HEAD:master` is REFUSED** — use `/merge-to-master` Path A (auto-merge PR). Classic `/branches/master/protection` returns a misleading 404 — check **rulesets**, not classic protection.
- master is VERY active (~900+ commits/day from large PRs). Always `/worktree-sync` (or fetch + merge origin/master) before building/merging.

## Key IDs
- Needs-review count (2026-08-19): ~100 (`sprk_communicationtype=100000000` AND `sprk_associationstatus IN (100000001,100000003,100000004)`).
- Attachment-bearing test email: "Fw: LITG-119896 Monte Rosa…" `d0d3f282-938c-f111-8076-000d3a98755b` (4 attachments, status Resolved).
