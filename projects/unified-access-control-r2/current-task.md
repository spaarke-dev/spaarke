# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> - **Standing rules:** project `CLAUDE.md`. **Decisions:** `notes/decisions.md`. **Defer list:** `notes/defer-issues.md`.
> - **History:** git log of this file.
> - **NOTE:** master is ahead of this work branch on project files. This copy is the session's state carrier. Read master's TASK-INDEX and README for task status.

> **Last Updated**: 2026-11-12 ~19:30Z (checkpoint #128, desktop session `uac-r2`). Private: `~/.claude/projects/c--code-files-spaarke-wt-unified-access-control-r2/private-handoff-2026-11-09-198.md`, `private-handoff-2026-11-08.md`, `private-p1-2026-11-11.md` (never copy into repo text).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Wrap-up, extended (round 90). This session (`uac-r2`) is authoritative and reviews peers. Merges: `git merge-tree` first, then squash with `--match-head-commit` after Router AND Client lint are green; ONE merge watcher at a time. Live actions need the owner's go IN THIS SESSION. At most 2 agents at once (Opus), fresh short-path worktree each. |
| **Dev BFF** | **`4e64670ea0`** deployed 2026-11-12 ~19:40Z (LOCAL merge of origin/master `350ca3da66` + spike `755987b385` + spike `d7369fecf1`, never pushed; worktree `C:\wtdep4`, keep until the next deploy). Gates: /healthz 200, /ping 200, anon MCP 401; ArchTests 2508/2508. mcp-r1 + ebilling notified (ebilling relays to word-add-in, ontology). |
| **Next action** | (1) Remove `C:\wtdep3` and `C:\wtdep2` with Remove-WorktreeSafe. (2) Wait: fm #2733 (054) merge (owner go in fm's session) → then the uac-r2 follow-up task below; fm 022c review; mcp-r1 #2731 fix review; spa-r4 #2681 deploy request; owner's TrackingFieldTrio 1.0.63 checks. (3) 191i when the owner is ready (each step an owner go). |

## Done 2026-11-12
- Merged: #2726 191h `9df5e34f9a`; **#2736 191g `57dcb37e30`** (doc-block fix head b6b121198; setting `ExternalAccess:FlaggedOfficeEdit:Enabled` off; K1-K15 in its note); #2732 (mcp-r1 side session, fixes #2709) `4901e832ed`; **#2739 task 196 `350ca3da66`** (Email members = roles + active grants + user shares, minus expired/No Access/walled; Manage Access info notes behind a bell, errors inline).
- **TrackingFieldTrio 1.0.63 imported on dev** (Import-SolutionScoped; served bundle sha256 54CA3F7A…FC6C matches, shows v1.0.63). Owner checks pending: Email members on PRJ-000046, on a To Do, and as a user without Write+Share; the To Do empty-state wording ("Grant access" doesn't fit To Do).
- 191g live test PASSED (owner, Route A, UNDO-VERIFIED). Marker = option B; the 191i step-10 gate proves the value length first.
- Owner answers: #2722 = creator's unit except leaving Secure/Restricted; 191d OQ-1..4 accepted; 191h keep stricter; task 196 build now (bell; errors inline); #2732 merge yes; deploy now + fold new spike.

## Peer reviews given today (all by message)
- **ebilling 084** (EB-D-218 option A + my 4 conditions): full review → F-A/F-B/F-D fixed, re-check PASS; 084 closed (work 227e57c268). **084f roles** (EB-D-220, owner chose billing admins): PASS. At their task 112: BEFORE/AFTER notices for the dev role apply, then I re-run SecureChild `-Verify`. The condition-2 invariant check (2 reads) was sent privately; they run it after every import and message me only on FAIL.
- **spa-r4 task 002** (PR #2681): PASS after F4-1 (fixed 6711a283c2). Its deploy needs the owner's per-SHA go here once merged.
- **fm #2733 (054)**: MERGED `d878e78a90`, NOT in live 4e64670ea0. fm's peer OK for the next deploy that includes it has conditions: (1) both `DirectMessage:*` settings stay unset on dev until 054b `Set-ThreadFlagFieldSecurity.ps1 -Verify` exits 0 there, with the DI-026 check first (D-2 already holds them unset); (2) 054b `-Apply` and 054c Stamp/Move run after the deploy, each on the owner's go in fm's session. No re-seed.
- **Deploy request 2ef4a17915 WITHDRAWN** (spa-r4's c53452bb0c is already live in 4e64670ea0). word-add-in-r1 pre-cleared 350ca3da66..2ef4a17915 (no Office server changes). mcp-r1: dev deploys = master + spike `d7369fecf1` only, never anything from `work/spaarkeai-mcp-bff-api-r1`. Ontology 179/181 are client-only (their task 185).
- **fm 022b**: Q1-Q5 decided (Q2 = option c: read-only floor from PlanCreateAsync); SecureRootInheritance edit APPROVED. **022c (#2738)** = Restricted source rule for Repair + field-mapping push; review it when sent.
- **mcp-r1**: #2731 design = option A (advance the watermark to the earliest dropped modifiedon); their side session may build it → review before merge. Phase 0 task 003 live tests ongoing: ping them before any dev deploy.

## In flight (2026-11-12 ~23:45Z)
- **197a** (agent `ac83f626f40c96eaa`, `C:\wt197a`): 197a-1 = #2722 (decisions sent: also keep owner when the message itself is Secure/Restricted; missing `sprk_createdbyperson` → keep owner; carry facts so the first decision AND SettleOwnerAsync agree; regarding-only path settles). Then 197a-2 = #2728 on a fresh branch: option (a), skip the #2468 demotion only when the communication's owner is positively the Direct team; K = a document can keep a stale non-secure owner after a #2722 move (file an issue).
- **197b** = PR **#2760** (agent `a93e31dc88f8433ad`, `C:\wt197b`): review asked to change bound (i) to positive Direct-pair recognition independent of owner (otherwise under D-2 every DM is user-owned and flagged participants read none); same exemption in the 191c engine if small, else a 191i blocker. Await the new head, then merge-tree + one watcher. Afterwards remove `C:\wt197m`, `C:\pub197m`, `C:\pub197b2`.
- **#2761** (ours, task 141 code): the identity store's binding-column readability proof faults on WhoAmI (403 0x80072560) every 5 min on dev since 2026-11-02; fail closed → an unbound contact's first sign-in may be refused. Fix after 197a/197b (agent cap). spa-r4 told it's a watch-item for the owner's E2E.
- **spa-r4 SPA** live on dev ~23:00Z (master 2ef4a17915, run 43324493710). Gate 23:00–23:30Z PASS, no external traffic yet; re-read after the owner's E2E.

## Follow-up task (uac-r2, after fm 054 lands on master)
#2722 (creator's BU team unless leaving Secure/Restricted; replaces `NonDirectUnfileAnswer`'s body) + #2728 (`sprk_relatedcommunication` as a document parent) + 198 participant shares get the 191 predicate + `GateDecisionRecordSharing` drops `domainname` + one unit-name source (drop `ExternalAccess:ExternalTeamShare:BusinessUnitName`).

## 191 remaining
- **191i** (dev migration + live gate; each write an owner go): `Set-ExternalCollaboratorRole.ps1 -Apply` → `Set-RecordCreatorPersonSchema.ps1 -Apply` → access-marker dry run → `-BffOmitsMarkersFromCallerCreates -Apply` → `-Verify` → `CustomerBusinessUnitId` before 191e WritesEnabled → placement job report-only → 191c settings → 191g one-marker gate (then the 5-case probe if it fails; template private in scratchpad c191g\probe-paste.txt) → AC-3 gate. When `ExternalAccess:FlaggedOfficeEdit:Enabled` goes on, tell word-add-in-r1 (they'll have the owner open a secure document for edit from Word).
- **191j**: docs + issues (provisioning follow-up for the field-security scripts; main-session `.claude/constraints/auth.md` naming `AppOnlyExternalVisibility`).

## Open items for the owner
- UAT U-1..U-13; 193 PCF import on dev; D-2 steps 2–6; D-10; 186 follow-up; quality alerts #1821, #1906 (mention only).
- Office checks on the dev build (word-add-in-r1 will send the read-back).

## Scratch
- Worktrees: `C:\wtdep4` (current deploy; keep until the next deploy), `C:\wtdep3` and `C:\wtdep2` (old deploys; remove with Remove-WorktreeSafe), `C:\wt191g`, `C:\wt196`, `C:\wt191b`… (merged; remove with Remove-WorktreeSafe).
- The untracked 0-byte `tests/integration/data-mutation/ExternalAccess/DirectMessageParticipantSharesTests.cs` in this worktree: leave it alone.
