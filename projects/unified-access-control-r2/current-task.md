# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint, never prepend; ≤10 KB. Standing rules: project `CLAUDE.md` (§2 Binding rules, §3 Owner directives, §5 Environment, §6 Gotchas). Decisions: `notes/session27-owner-decisions-and-research.md` (rounds 1–89) and `notes/decisions.md`. Live records: `notes/batch5-live-gates-2026-10-08.md`. Owner checks: `notes/owner-hands-on-checklist-2026-10-08.md`. Narrative: checkpoint commit messages.

> **Last Updated**: 2026-10-09 (checkpoint #25).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | Session 28. Four PRs are verified and queued as a chained background merge/deploy (scratchpad `mergeNNNN.sh`; each waits for Router with 0 pending): **180** #1519 (`bxbsqfva2`: ribbons -Apply incl. hiding the "New Document" appaction) → **175** #1503 (`beis8njx7`: waits for 180; schema verify → BFF → 2 web resources → form lock → access ribbon -Verify → TFT 1.0.45) → **177** #1517 (`bils0r27l`: waits for 175; BFF) → **176** #1520 (`bab0yymf3`: waits for 177; BFF). The 175 schema step (`Set-AccessInheritanceSchema.ps1 -Apply/-Verify`) is ALREADY DONE on dev (column secured, BFF reads it). |
| **Agents (idle, resumable)** | `exec175` (C:\wt175), `exec176` (C:\wt176), `exec177` (C:\wt177), `exec180` (C:\wt180); older: exec174, exec173, exec153, exec067, fix1410. |
| **Next Action** | (1) As each job reports, check MERGED, the deploy exits, readbacks and healthz. For 175, read the form-lock and ribbon-verify output: if the ribbon verify FAILS, run `Set-AccessRibbon.ps1 -Apply` (after 180's ribbon import has finished). (2) After 175 deploys: watch two `secure-root-inheritance` job runs (`ResultJson.followParents`: notCompleted 0, no access_record_hidden / not_secured), re-run the schema `-Verify` (the read probe now runs), then the 175 live gate via `exec175`: unsecure a matter with a filed work assignment, which follows; Make Secure a work assignment under an ordinary matter, then secure and unsecure the matter, and it stays secure; Remove Secure hidden under a secure floor; the form refuses a looser Access Permission; clean up. (3) After 177 deploys: one-off Send-to-Index for the 141 dev documents (query in PR #1517 body). (4) 180 live gate G180-2: a read-only role sees no custom create buttons; testuser1 (Basic User) still sees them. (5) Close 174 ✅ (done); close 175/176/177/180 after their gates (POML status + TASK-INDEX + drift check + board sync). (6) Then start **179** and **181** (both after 175), then **178** (design note first → owner), then 136 → 099 → 036 → 090. |

## Open with the owner
- **Banner still shows "Access status unavailable"** after #1490 deployed (fixed script 1.0.1 confirmed in Dataverse; the `/no-access` route returns 200 for the owner's matters; CORS and the redirect URI are fine). Waiting on the owner's Network-tab status codes for `no-access|config/client` after Ctrl+Shift+R, plus `[Access Status]`/`[BffAuth]` console lines. Suspects: a cached old script, or MSAL ssoSilent failing in the browser. The Notepad "Failed to load memos [object Object]" error is probably separate.
- Whether to add No Access Entries to the Matter Management app navigation (URL workaround given).
- Policy question: curated Precedents as firm-wide knowledge (option c), for later.
- Checklist §1–§8 (§7 is blocked by the banner); `Remove-TestContainers.ps1`; CIAM identities for 140; 171 users; 165 role grant; round 89 roles (set Share privilege after 179).

## Owner decisions this session (recorded in notes)
- **R87:** the parent sets a FLOOR; a child may be stricter; only inherited values follow the parent down; a re-file never loosens; the lock covers Secure and Access Permission only. "Same as today" for who may act (Access Permission lowering needs Write only).
- **R88:** one SPE container per secure family (top secure record's container; adopt an existing one where possible); un-secure MOVES files to the business unit → task 178.
- **R89:** Manage Access needs the Share privilege (179); Create buttons follow the Create privilege (180); build internal grant notification (181). 174 stand-in accepted → 174 closed.
- Main-session calls: 176 option B (batch read as the caller); index parent = governing record (one secure → it; none → most specific; two secure roots or unreadable → none); SearchDocuments unbound and trimmed; Precedents need every supporting matter readable; 180 hides the modern "New Document" appaction (owner OK).

## Task status
- **Done:** 125 (174 closed today).
- **Verified, merging/deploying:** 175, 176, 177, 180.
- **Waiting on owner checks:** 067 (§6), 153 (§7), 173 (§8), 154, 114, 171, batch 4 (137, 140, 142, 143, 147, 150, 157, 162–166, 168, 169).
- **Not started:** 179, 181 (after 175); 178 (after 175 + 177; design first); 136; 099; 036; 090. Bookkeeping: 013/037/039 (with 136), 058 (090), 066 (067).

## Issues filed today
#1488 (fixed), #1489 KPI/rollup `/api/api`, #1492 SpaarkeMaster source missing 2 web resources (added to the dev solution), #1506 → 178, #1510 → 177, #1511 → 176, #1514 per-document check cost, #1516 bulk indexing sweep broken + "Unknown Matter" labels, #1521 ribbon source drift, #1522 index-name resolver short names, #1533 search counts include unreadable docs, #1534 flaky ContentSafety test.

## Gotchas learned today (also in project CLAUDE.md §6 where durable)
- `sprk_BffApiBaseUrl` ends in `/api`: normalize it (#1488).
- `Deploy-WebResourceInline.ps1` puts new web resources outside SpaarkeMaster (#1492).
- A newly created column needs a table publish before `fieldpermissions` can reference it (175's script now waits).
- Never start a merge job with `&` in a foreground call (no log); always use run_in_background.

## Worktrees
- **Active:** C:\wt175, C:\wt176, C:\wt177, C:\wt180.
- **Deploy:** C:\wtR2, used in sequence by the chained jobs.
- **Removable:** C:\wt174, C:\wt1488, C:\wt064, C:\wt067, C:\wt113, C:\wt153, C:\wt173, C:\wt1410, C:\wt154, C:\wt105, C:\wt101, C:\wt114u. Run `cmd /c rmdir` on any node_modules junction first.
