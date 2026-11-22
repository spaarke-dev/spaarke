# Current Task State

> **Last Updated**: 2026-11-22 (task 023 split into 023a/b/c; 023a+023b coded, reviewed, fix round running; nothing committed)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file. Target ≤ 10 KB.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **023a** (shared DataGrid: rowSource/rowSourceKey, rowActions wiring onRecordAction, opt-in truncate-tooltip) + **023b** (config `286be8b4-f96f-4d5a-b1d5-82fd83ca9693` `config/external-record-documents.gridconfiguration.json`, `scripts/Apply-ExternalRecordDocumentsGridConfiguration.ps1`, BFF id admission in `OutsideCounselGridConfigurationIds`, test `ExternalRecordDocumentsGridConfigurationTests`). Design APPROVED 2026-11-22 ("Yes, Dataverse record"; grid-owner props approved). 023 = 🚫 split. 023c (SPA Documents on shared grid, all roots + D-23) after these |
| **Step** | Code done by agents; adr-check clean; ONE adversarial review done (opus). Findings: F2 x3 (More-actions Space toggles row selection; tooltip not on keyboard focus; truncated notice `1 rows` + fixed noun) + K items → **fix agent RUNNING** (shared DataGrid only). F4 allow-list rule exception → DONE by main session (`ExternalAccessModule.cs` comment); STILL TODO: same note in `LiveDerivedAllowLists` comment of `tests/integration/auth/UnifiedAccessControl/ExternalModuleColumnAllowListTests.cs` ~:757. F4 publish-size: MUST measure (fresh master vs branch, short paths, Compress-Archive Optimal, PDBs incl., file counts match) — not done. F6 filed: D-24 #2939 (FetchXML page error shows empty list), board Type=Bug, in defer-issues.md. K: add useHostRowSource to CAPABILITY-INDEX row 31 needs `.claude` edit (owner approval) — not done |
| **Status** | Uncommitted in this worktree: 023a/023b code+tests+docs, POMLs 023a/b/c, TASK-INDEX (023 🚫, 023a/b/c 🔲; drift clean), design + notes files, D-24 entry. Not yet: POML/index status → ✅ for 023a/023b. Earlier: 016 🔄 (owner retest 3–7); 020/022 ✅ merged `4c2d26939d`; dev SPA `2963d1565b` |
| **Side** | Quality alerts: worktree `C:\code_files\spaarke-wt-quality-alert-fixes`, branch `fix/quality-alerts-1906-2934`, **PR #2937** (bound to this session; nightly 5-shard sync + `TestShardFilterSyncGuardTests`; #1906a version tests via `helpers/scriptVersion.ts`). Not merged — owner merge go needed. #1906b → ebilling-r1; #2866 later |
| **Next Action** | When fix agent reports: verify (`npx jest src/components/DataGrid`, tsc, build), finish the two TODOs above (test comment; publish-size measurement), set 023a/023b ✅ (POML+index+drift), commit on the branch (message file, `git commit -F`), push, open PR for 023a+023b → ask uac-r2 to review the BFF piece (peer msg ≠ owner approval); owner merge go. Then 023c → 024 → 028 (028: run apply script -WhatIf/apply/-Verify with owner go + rollback = delete row; grid PATCH 3ff4102c; BFF deploy via uac-r2 first). Open owner wording Q: show "The client is reviewing this invoice." beyond Information field? |

## Task 015 outcome (for 016's E2E)
- Add event: Event Type required (options = `creatableEventTypes`), Due Date required by type, Priority, Assigned To ("Me" first), Description. Add event hidden when no type is offered.
- Event record: row opens a child record from row data; deep link `?record=event:{id}` (token amended from `sprk_event:`).
- Edit / Change status per `canEdit` / `canChangeStatus` / `allowedStatuses`; Delete (creator only).
- Known limits: Edit can't mark Due Date required (server 400 shown); multi-parent events: one parent's assignees (400 shown), Copy link may open not-found; truncated parent list → not-found on re-read; 403 then failed re-read replaces the page.

## Live state
- **Dev BFF** `5ae2804884` (uac-r2 deploy, live since ~2026-11-19T02:01Z; owner go in uac-r2): live `326781c50f` + master `fe51619cfc` (#2904 ebilling 131/132/135, ISS-040); mcp spike kept. Rollback = `326781c50f`. `/healthz/catalog` 503 until ebilling seeds EB-D-256 (not a regression). uac-r2 asks spa-r4 before any restart. uac-r2 30-min gate (02:01–02:35Z): PASS, but it saw no signed-in external traffic (1 anonymous `/api/external/me` 401), so the SPA routes incl. billing-guidelines are not yet exercised; they show up in the next gate after use.
- **Dev SPA** master `2963d1565b` (run 44441594640, success, live 2026-11-19T02:05:53Z; owner go 2026-11-18 "and then also deploy portal"). Agreed target was `fe51619cfc`; the 8 later master commits don't touch `src/client/external-spa`, and the only shared file the SPA imports that changed (`themeStorage.ts`) is type-only. Live bundle: Billing Guidelines widget + `ebilling/billing-guidelines` route present, both plane titles present, welcome band absent, `assignable-people` present. Rollback = redeploy master `ea7ba661d6` (portal chrome build), then `ed3a5c2752`. Roll back to `04dbd8df05` only together with BFF `5a9b687a0b`.
- **Portal chrome (#2900):** title by plane (partner "Outside Counsel Collaboration", internal "Legal Service Request Portal"); nav strip and welcome band removed; no Quick Start tab on the CIAM plane (`hasQuickStartFor`), workforce/admin keep it; assistant starts collapsed. The partners-only reading of "remove Quick Start" was not separately confirmed by the owner.
- **ebilling-r1 task 135** (Billing Guidelines widget, `task/ebilling-135` `f054d8a00e`): SPA review PASS with notes N1–N4 (2026-11-18). Per-record view (`coreRecordId`) has no caller; if the owner wants it on records, it is an R4 follow-up in the record template.

## Coordination
- uac-r2 (session `uac-r2`) reviews BFF PRs as a gate and deploys the dev BFF. The owner gives the merge go each time. Peer messages are not owner approval.

## Open items
- D-13 #2800, D-14 #2809, D-15 #2810, D-16 #2823; D-19 #2854, D-20 #2860.
- Older owner questions: D-1 #2686, D-2 #2687, D-4 #2693, #2786 backfill.
- Phase 2 (020/022/023/024/028) still open.
- CI: office-scope legs sit at their 20-min cap (known #1474/#1718/#2767). Re-run on a cancel; not ours.
