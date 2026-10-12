# Current Task State - spaarke-SPA-external-access-platform-r3

> **Last Updated**: 2026-10-12 (owner-decision wave; Option B finishing; by context-handoff)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Owner-decision work of 2026-10-12. Option B (multi-root, secure roots only, EFFECTIVE secure status) is being finished in worktree `C:\code_files\spaarke\.claude\worktrees\agent-ae8f4415f1670de13` (branch `worktree-agent-ae8f4415f1670de13`, commits `d29fe0342`, `d375c597d` + an effective-flag follow-up; not merged) |
| **Step** | Merged and pushed: closed SR cannot be deleted + renewal bell items (`72b0b6520`), their review fixes incl. SPA (`4a0fd6cd8`), intake department + partner Invention flag + `/me` intakeRequestTypes (merge after `4a0fd6cd8`), ledger prose (`b4f1a4610`). Verified: build 0/0, filtered unit 3218, arch 941; SPA 849 |
| **Status** | Main worktree clean. An empty locked folder `C:\code_files\spaarke\.claude\worktrees\agent-af842f88050a4b5b9` remains (worktree unregistered, branch deleted); remove later |
| **Next Action** | Option B done -> TWO fable reviews (tenant isolation) -> fix -> merge `--no-ff` -> build + filtered unit + arch. Then: grid views task (after owner confirms a/b/c below), 052/053 intake wizards, 038 BFF deploy to dev (owner said ok; show commands + rollback first) |

### Critical Context
- **Parallel pattern:** BFF tasks run in isolated worktrees under `C:\code_files\spaarke\.claude\worktrees\agent-*` (`git reset --hard <R3 sha>` first), commit there, main session merges `--no-ff`. SPA tasks run in this worktree, uncommitted; the main session commits by explicit path. Agents never edit TASK-INDEX, this file, POML status or `.claude/`.
- **Review policy:** auth/security/tenant-isolation changes get two fable passes; other FULL tasks one. Fix rounds sonnet; a fix that changes approach gets a scoped re-check. Max 4 agents.
- **Quota:** the opus weekly limit was hit on 2026-10-12 (resets 9pm America/New_York); use sonnet for implementers until then.
- **Bookkeeping edits:** python script files in the scratchpad (sed breaks on the status emoji). Check with `pwsh -NoProfile -File scripts/check-task-status-drift.ps1`.
- **Do not run the full BFF unit suite under load**; use filtered runs. Bash can be slow under load: prefer Grep/Read.

## Owner answers 2026-10-12 (recorded in project CLAUDE.md §3)
Option B; no delete after close; invoice upload = invoice rights only; team-access and renewal bell items wanted; contact owned by the customer's primary BU; intake (department from contact, Invention partners behind a flag, incidents simple, fixed lists, NDA review + issue); grid views from Dataverse views; 060 shared service OK if Model 1 allows; dev dispatch "ok"; 060 tenant-wide group permission approved if it cannot be scoped. Deferred: DEF-001 #1736 NDA generation (separate project), DEF-002 #1737 incident alerting.

## Open owner questions
1. **Grid views:** (a) "assigned resolver" = the shared membership resolver? (b) views per entity = External Active / My / Inactive? (c) log the internal "My" views (`ownerid eq-userid`) as an issue? Design: external-marked system views, server-side membership narrowing for My views, picker on for registered views only, `statecode` allow-listed.
2. **Bell items:** team access A (none) / B (snapshot diff in cache); renewals A (accept modifiedon noise) / B (`sprk_renewedon` column).
3. **ISS-005:** option A, `ExternalAccess:CustomerBusinessUnitId` written by provisioning H4b from H10's BU id (coordination note to provisioning).
4. **060:** approve (1) the D-12 line "dedicated per customer covers the customer stamp, not platform services holding no customer content", (2) the ADR-028 A7 carve-out for the `/organizations` registration client, (3) 60-day log retention. Analysis: `notes/task-060-model1-shared-service-analysis.md`.

## Next actions (in order)
1. Land Option B (reviews, merge, verify).
2. SPA follow-ups: use `/me` `intakeRequestTypes` (undefined = fallback, not "none"/"all") in 052/053.
3. Grid views task once question 1 is answered (replaces 002's fixed columns).
4. 052/053 intake wizards (NDA review/issue with `ndaAction`).
5. 038 BFF deploy to dev, then 070/071/072; 040 step 5 after 071.
6. Before the BFF PR: `/conflict-check` + actionlint.

## Dependencies tracked elsewhere
- ISS-001 (#1623), ISS-002 (#1633), ISS-003 (#1636), ISS-004, ISS-005 (#1673).
- Copilot agent still uses `access_as_user` on `1e40baad`.
- Operator: `mailbox-central@spaarke.com` + Exchange RBAC scoping for the stamp UAMI (H14a); BFF app user needs `prvDelete` on `sprk_servicerequest` (verify live).
