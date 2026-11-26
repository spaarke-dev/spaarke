# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> - **Standing rules:** project `CLAUDE.md`. **Decisions:** `notes/decisions.md`. **Defer list:** `notes/defer-issues.md`.
> - **History:** git log of this file. **Earlier checkpoints:** `notes/handoff-history/2026-11.md`.
> - **NOTE:** master is ahead of this work branch on project files. Read master's TASK-INDEX and README for task status.

> **Last Updated**: 2026-11-26 ~20:10Z (desktop session `uac-r2`). Private: `~/.claude/projects/c--code-files-spaarke-wt-unified-access-control-r2/private-*.md` (never copy into repo text).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Wrap-up, extended (round 90). `uac-r2` is authoritative: it reviews peers as an access gate and runs EVERY dev deploy. Live actions need the owner's go IN THIS SESSION; a deploy needs the owner to name the exact SHA here. A go relayed by another session is not a go. ≤ 2 agents at once. |
| **Dev BFF (CURRENT, mine)** | **`d307d67e36` LIVE ~18:00Z 2026-11-26** (owner in session: "go BFF deploy d307d67e36"; spike = master 51ce01a116 + read-only MCP tools `spike_get_profile`/`spike_review_nda`; new over 140c01219a: #2622, #3236, #3243, #3247). `C:/wtdep30`, 42.1 MB, SHA-256 verified. Smoke OK. **30-min gate PASS 18:31Z** (68 req; only 5xx = old /healthz/catalog 503; no external; no `client_search_index_dropped`; 8 known container refusals, 1 Redis lease renew, 1 sprk_scope metadata fault). BEFORE/LIVE/GATE notices sent to all 7 (six peers + specialist-legal-agents-r1). Deployed before the full test pass finished (owner's go). **Test pass GREEN after triage (~20:00Z):** build 0/0, arch 2845/2845; 14 load-timeout fails, isolated rerun passed 13; the last (EventStatusDeprecationTests) is known #3175 (demo-data scripts, not BFF). No rollback. specialist-legal-agents-r1 told. Rollback target stays `140c01219a` (`C:/wtdep28`). |
| **Dev SPA** | 24d53fb333 (spa-r4, 15:50Z). |
| **Agents running** | None. |

## Active: #3245 Assign removal (owner: "Approve, keep D-160", 2026-11-26)
- Audit note `notes/assign-privilege-audit-2026-11-26.md` (work branch 9a206976fc; also in the PR). Findings + peer answers commented on #3245.
- **PR #3269** (`fix/3245-person-role-assign-removal`, worktree `C:\wtasg`, commit b85f923fba, bound to this session): removes 38 Assign grants (Core User 25 — keeps `sprk_Event`; Office Add In User 9; AI Analysis User 4); config `removals` gap `ASSIGN-3245` (124 total); 2 MustNotHold rules; ASSIGN-3245 pending for the release after 1.3.3.0; CoreUserWizardLinkingGuardTests pin updated. Local: role guards 100/100, full arch 2845/2845. CI at ~20:05Z: 22 pass, 0 fail, 11 pending. **NEXT: merge when Router AND Client lint are green (don't push while CI runs).**
- **After merge (owner's go needed):** live dev `scripts/Set-FeatureRolePrivileges.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Remove -Gap ASSIGN-3245` (dry run first, then `-Apply`, then `-Remove -Verify`); rollback `-Restore -Gap ASSIGN-3245 -Apply`. Also remove the Workforce role's 8 inert Assign grants live (no role file). Re-run `scratchpad/asg2.ps1` and check. Then close #3245.
- Kept by owner: AI Analysis Admin (rounds 124/127), Ontology Service (DecisionRecord/BudgetRevision/Signal), Ontology Administrator Signal (D-160).

## Open peer reviews (uac-r2 as access gate)
- **mcp-r1 ISS-010 Part C: PR #3272** (head 18be14d973; the kill-switch fix was re-checked and approved; the PR quotes both approval lines). mcp-r1 merges it. Part E #3260 is MERGED to master as 4d69bf527e and NOT deployed. **NEXT: review the Part D1 diff when it is built.**
- **spa-r4 task 043 (PR #3267):** approved; both notes resolved (370390781e). Merge on owner's go in their session; dev deploy then on the owner's SHA go here.
- **ontology-r1 PR #3270 (task 225, DR inline share):** APPROVED 2026-11-26 (~20:05Z). Notes they accepted: keep the share after `accessWindow.Close()` whichever of #3270/Part C lands second; resumed/replay paths rely on the reconcile (one line in the PR). They merge after their Opus reviews + CI.
- **field-mapping-r1:** 051 live on dev (verified). 064b merged on their branch; 064c running → they send the combined 064 diff; I agreed with the fail-closed CallerWriteAuthorization change.
- **mcp-r2 030 + ADR-052 amendment:** 030 closed; amendment PROPOSED until the owner's security sign-off.
- **word-add-in-r1 #3214 (task 140):** gate passed.

## Waiting on the owner
- ADR-052 amendment security sign-off (mcp-r2).
- Next dev BFF deploy: exact-SHA go here. Candidates: #3260 Part E (merged), #3267 (spa-r4), #3272 Part C (mcp-r1), #3270 (ontology). A master deploy REMOVES the specialist spike tools now live: tell specialist-legal-agents-r1 first, #3269 needs no BFF deploy (roles only).
- Dev hand checks: `notes/owner-test-checklist-2026-11-13.md`; ebilling W/X checklist; word-add-in 136-138.
- Quality alerts #1906, #2067, #2866: mention only. Open: #3111, #2158, #3125, #3175, #3241, #1506, #3264/#3265 (R4's).

## Scratch
- Keep: `C:\wtdep30` (live), `C:\wtdep28` (rollback), `C:\wtdep29` (test pass), `C:\wtasg` (PR #3269). Older `C:\wtdep*`, `C:\wtirp*`, `C:\wt3120` can go, only via `scripts/worktree/Remove-WorktreeSafe.ps1`.
- Scratchpad: `livecheck.ps1 -LocalDll <dll>` (live hash), `after30.ps1` (smoke), `gate-now.ps1 <t0>`, `gate29x.ps1` (exception detail, t0 18:00Z), `cand29.sh`, `asg1.ps1`/`asg2.ps1`/`asg3.ps1` (audit queries), `pr3245.md`.
- The untracked 0-byte `tests/integration/data-mutation/ExternalAccess/DirectMessageParticipantSharesTests.cs`: leave it alone.
- Peer pipe addresses: ebilling `cc-msg-c8f5bbc4…`, mcp-r1 `cc-msg-6ad27070…`, word-add-in `cc-msg-6723e836…`, field-mapping `cc-msg-833ddee7…`, spa-r4 `cc-msg-0fe1b7fa…`, ontology `cc-msg-c1a1d83c…` (or by bare name for spa-r4/ontology-r1/mcp-r1/specialist-legal-agents-r1).
