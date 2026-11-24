# Current Task State

> **Last Updated**: 2026-11-24 ~01:30Z (wave 8 committed: 031, 032, 034; PR next, then the owner's merge go)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file. Target ≤ 10 KB.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Wave 8** | Tasks 031 (`5fed198b33`), 034 (`b976ebca35`) and 032 (next commit) are committed on `work/spaarke-SPA-external-access-platform-r4`. Review is done: one adversarial pass, fix rounds 1 and 2, and the main session's scoped re-checks. The owner's four decisions (2026-11-23) are applied. POML status, TASK-INDEX and the board are NOT updated yet: close the three tasks after the merge. |
| **Next action** | Push → one PR (031+032+034) → owner merge go → `gh pr merge N --squash` → send uac-r2 the merge SHA → close #3010 → close 031/032/034 (POML, TASK-INDEX ✅, `pwsh scripts/check-task-status-drift.ps1`, board Tasks Completed 25→28) → reset this file. |
| **Checks run** | BFF `ExternalModuleColumnAllowListTests`: 173 passed. SPA vitest: RecordMessages/GridSettings/RecordTemplateAccess/GridWidgetBody.hostCommands/RecordTemplateSections, 317+ passed. ui-components jest (DataGrid + RecordCommandBar): 229 passed. tsc clean on both packages. external-spa `npm run build` OK. Publish-size delta (031): +57 B. |

## 🔒 HARD DEPLOY ORDER (wave-8 review F1, 2026-11-23)
Once 031 is merged, **no SPA deploy may run until uac-r2 has deployed a BFF that carries 031's ReadableColumns to dev.** Every SPA deploy builds master HEAD. Without that BFF, every matter, project and work-assignment record read answers 400 `DV_FETCHXML_COLUMN_NOT_PERMITTED`. uac-r2 has agreed. The same rule is in task 038's `<gate>`. The current dev BFF `22c42efad6` does NOT carry 031.

## Owner decisions (2026-11-23)
1. 032's DataGrid prop API is approved on behalf of the dataset-grid owners (recorded in `notes/task-032-notes.md`).
2. "Reset columns" became **"Clear all"**. It uses the new icon-map entry `reset` (ArrowReset20Regular) and clears widths, sort AND column filters. A filtered header is announced as "filtered" as well as showing the filter icon.
3. Information also shows matter `sprk_mattertype` and `modifiedon`, and work-assignment `sprk_mattertype`. These were already allow-listed.
4. 034's background check (≤ 1 GET/min) is accepted. The manual Refresh the owner asked for already exists (Messages header icon and "Show new messages").

## Known limits (for the PR)
- "Open date" is the formatted `createdon`, which is a date-time.
- Contact lookups also return the contact GUID (a serialised EntityReference). uac-r2 says this is fine.
- A parent record's Messages keeps polling while a child modal is open.
- Found and fixed in scope: a `HeaderCellContent` hook-order bug (`useRef` after an early return). It has a regression test.

## UAC
All 12 columns are approved: 11 by uac-r2, and `sprk_regardingmatter` by the owner in uac-r2's session. Both uac-r2 conditions are met by existing tests (`ExternalModuleColumnAllowListTests.cs` ~:1287 drops an unlisted lookup's display value; ~:1318 refuses a contact read).

## Live on dev
- SPA: master `9d08576e63` (rollback `2dcbe35e34`).
- BFF: `22c42efad6`, live since ~23:49Z with MCP on (rollback `a48398746d`); deployed by uac-r2.

## Owner questions open (older)
- Should a docx preview be filed as an Idea?
- Show "The client is reviewing this invoice." beyond the Information field?
- Add a `useHostRowSource` row to `.claude/patterns/CAPABILITY-INDEX.md` row 31? This is a `.claude` edit and needs owner approval.

## Filed
- D-24 #2939 … D-30 #2975.
- D-31 #3008: narrow DataGrid header minimums.
- D-32 #3009: empty band above the Documents grid ("Clear all" now sits in that toolbar).
- D-33 #3010: folded into 031; close it after the merge.

## Merge trap (seen 2026-11-22)
When merging master after a squash of our own work, watch for a line that one task changed and a later task changed back. It looks unchanged from the merge base, so git takes master's text. For files where `origin/master` equals the pre-squash branch tip, restore `HEAD^1`'s copy.
