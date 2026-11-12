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
| **In flight** | Dev BFF deploy running (background), from `C:\wtdep4` commit `4e64670ea0` = origin/master `350ca3da66` + spike `755987b385` + NEW spike `d7369fecf1` (owner go: "Fold into this deploy"). Ledger conflict resolved to count 153. Build 0 errors, ArchTests 2508/2508. |
| **Next action** | (1) When the deploy finishes: gates `/healthz` 200, `/ping` 200, anonymous POST `/mcp/legal-operations` 401. (2) Send mcp-r1 one line with the deployed SHA (they asked); notify ebilling, ontology, word-add-in. (3) Update this file: Dev BFF = `4e64670ea0`, keep `C:\wtdep4`, remove `C:\wtdep3` + `C:\wtdep2` with Remove-WorktreeSafe. |

## Done 2026-11-12
- Merged: #2726 191h `9df5e34f9a`; **#2736 191g `57dcb37e30`** (doc-block fix head b6b121198; setting `ExternalAccess:FlaggedOfficeEdit:Enabled` off; K1-K15 in its note); #2732 (mcp-r1 side session, fixes #2709) `4901e832ed`; **#2739 task 196 `350ca3da66`** (Email members = roles + active grants + user shares, minus expired/No Access/walled; Manage Access info notes behind a bell, errors inline).
- **TrackingFieldTrio 1.0.63 imported on dev** (Import-SolutionScoped; served bundle sha256 54CA3F7A…FC6C matches, shows v1.0.63). Owner checks pending: Email members on PRJ-000046, on a To Do, and as a user without Write+Share; the To Do empty-state wording ("Grant access" doesn't fit To Do).
- 191g live test PASSED (owner, Route A, UNDO-VERIFIED). Marker = option B; the 191i step-10 gate proves the value length first.
- Owner answers: #2722 = creator's unit except leaving Secure/Restricted; 191d OQ-1..4 accepted; 191h keep stricter; task 196 build now (bell; errors inline); #2732 merge yes; deploy now + fold new spike.

## Peer reviews given today (all by message)
- **ebilling 084** (EB-D-218 option A + my 4 conditions): full review → F-A/F-B/F-D fixed, re-check PASS; 084 closed (work 227e57c268). **084f roles** (EB-D-220, owner chose billing admins): PASS. At their task 112: BEFORE/AFTER notices for the dev role apply, then I re-run SecureChild `-Verify`. The condition-2 invariant check (2 reads) was sent privately; they run it after every import and message me only on FAIL.
- **spa-r4 task 002** (PR #2681): PASS after F4-1 (fixed 6711a283c2). Its deploy needs the owner's per-SHA go here once merged.
- **fm #2733 (054)**: guard entries for our 191c code accurate; merge needs the owner's go in fm's session. NOT merged yet.
- **fm 022b**: Q1-Q5 decided (Q2 = option c: read-only floor from PlanCreateAsync); SecureRootInheritance edit APPROVED. **022c (#2738)** = Restricted source rule for Repair + field-mapping push; review it when sent.
- **mcp-r1**: #2731 design = option A (advance the watermark to the earliest dropped modifiedon); their side session may build it → review before merge. Phase 0 task 003 live tests ongoing: ping them before any dev deploy.

## Follow-up task (uac-r2, after fm 054 lands on master)
#2722 (creator's BU team unless leaving Secure/Restricted; replaces `NonDirectUnfileAnswer`'s body) + #2728 (`sprk_relatedcommunication` as a document parent) + 198 participant shares get the 191 predicate + `GateDecisionRecordSharing` drops `domainname` + one unit-name source (drop `ExternalAccess:ExternalTeamShare:BusinessUnitName`).

## 191 remaining
- **191i** (dev migration + live gate; each write an owner go): `Set-ExternalCollaboratorRole.ps1 -Apply` → `Set-RecordCreatorPersonSchema.ps1 -Apply` → access-marker dry run → `-BffOmitsMarkersFromCallerCreates -Apply` → `-Verify` → `CustomerBusinessUnitId` before 191e WritesEnabled → placement job report-only → 191c settings → 191g one-marker gate (then the 5-case probe if it fails; template private in scratchpad c191g\probe-paste.txt) → AC-3 gate.
- **191j**: docs + issues (provisioning follow-up for the field-security scripts; main-session `.claude/constraints/auth.md` naming `AppOnlyExternalVisibility`).

## Open items for the owner
- UAT U-1..U-13; 193 PCF import on dev; D-2 steps 2–6; D-10; 186 follow-up; quality alerts #1821, #1906 (mention only).
- Office checks on the dev build (word-add-in-r1 will send the read-back).

## Scratch
- Worktrees: `C:\wtdep4` (current deploy; keep until the next deploy), `C:\wtdep3` and `C:\wtdep2` (old deploys; remove with Remove-WorktreeSafe), `C:\wt191g`, `C:\wt196`, `C:\wt191b`… (merged; remove with Remove-WorktreeSafe).
- The untracked 0-byte `tests/integration/data-mutation/ExternalAccess/DirectMessageParticipantSharesTests.cs` in this worktree: leave it alone.
