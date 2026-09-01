# Current Task State — email-communication-intelligence-r2

> **Last Updated**: 2026-09-01 (context-handoff — Pillar B add-in deep-fix arc DONE + PR #922 auto-merging; NEXT SESSION = investigate the document-profile playbook bug #919)
> **Recovery**: Read "Quick Recovery" first, then "NEXT SESSION" below.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Status** | **Pillar B add-in is FULLY FUNCTIONAL** (sign-in, real Dataverse "File to" search, save `.eml`, attachment child docs, matter association, indexing — all UAT-verified). 6 pre-existing master bugs + real entity search fixed, tested, deployed to dev. **PR #922 open with auto-merge** (merges when `Router` check passes). |
| **Branch** | `work/email-communication-intelligence-r2` @ `b680cd286` — 0 behind master (merged origin/master cleanly, 66 commits, no conflicts). Working tree CLEAN. |
| **Open PR** | **#922** (Path A auto-merge; `gh pr view 922`) — the Pillar B fixes + entity search. **PR #911** already merged (`3c577bce4`, infinite scroll + ADR-051). master protected (ruleset `21824191`, required check `Router`). |
| **🔴 NEXT SESSION** | **Investigate the document-profile playbook bug — GitHub #919** (see NEXT SESSION section below). This is the ONLY remaining functional issue; everything else works. |
| **Deployed (dev)** | `spaarke-bff-dev` = branch `b680cd286`-equivalent (hash-verified, healthy). Add-in SWA from `cfae9cdc1`. App Insights app id `6a76b012-46d9-412f-b4ab-4905658a9559`. |

## 🔴 NEXT SESSION — document-profile playbook bug (#919), fully root-caused
**Symptom**: every saved Document shows `sprk_filesummarystatus = Failed` — the document-profile AI playbook doesn't complete (no summary / AI documenttype). Save/search/association/indexing all WORK; only profiling fails.

**ROOT CAUSE (definitive, via runtime App Insights capture 2026-09-01)**: the profile playbook's **"Update Record" node** (`sprk_playbooknode 0fa4e8db-b216-f111-8343-7c1e520aa4df`) has `fieldMappings[].value = "{{output_aiAnalysis.output.sprk_filesummary}}"`. At runtime the orchestrator **string-substitutes the multi-line AI summary into the JSON `value` WITHOUT JSON-escaping newlines** → invalid JSON → `UpdateRecordNodeExecutor.ParseConfig` throws `JsonException: '0x0A' is invalid within a JSON string. Path: $.fieldMappings[0].value` → node fails → playbook stops (batch 2) → `filesummarystatus=Failed`.

**Fix location**: `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookOrchestrationService.cs`. The JSON-aware `RenderConfigJsonStructurally` (R7 Wave 11, ~L2299) is meant to prevent this, but for this node it **falls back to flat string substitution at L2284** (catch on JsonException) which corrupts the JSON. **Next step**: add a temp diagnostic in `RenderConfigJsonStructurally` (or the L2277 catch) to capture WHY it throws / falls back for this node's wrapper-format config, then fix (keep on the JSON-aware path / make the flat fallback JSON-safe).
- **The node's config is the Code-Page WRAPPER format**: outer `{__canvasNodeId,__actionType,isConfigured,validationErrors,configJson}` whose **nested `configJson` STRING** holds the real config. `ParseConfig` handles the STORED template fine (proven by `UpdateRecordParseConfigReproTests` — green); only the RENDERED config (AI text w/ newlines) is invalid.
- **Blast radius**: SHARED — affects ANY playbook injecting multi-line/quoted content into a node ConfigJson (Insights, Daily Briefing). Governed by `docs/architecture/SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md`; AI-orchestration area (`spaarke-ai-architecture-redesign-r2`). NOT the Office save path.
- **Verify a fix**: fresh add-in save → query `SELECT TOP 4 sprk_documentname, sprk_filesummarystatus, sprk_documenttype FROM sprk_document ORDER BY createdon DESC` (via Dataverse MCP) → expect `filesummarystatus` != Failed (100000004) + AI-set documenttype. Or App Insights: `traces | where message contains 'Failed to parse update record'`.
- Full record: `notes/pillar-b-uat-findings-2026-08-31.md` (#7 section) + GitHub #919.

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
