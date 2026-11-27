# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> - **Standing rules:** project `CLAUDE.md`. **Decisions:** `notes/decisions.md`. **Defer list:** `notes/defer-issues.md`.
> - **History:** git log of this file. **Earlier checkpoints:** `notes/handoff-history/2026-11.md`.
> - **NOTE:** master is ahead of this work branch on project files. Read master's TASK-INDEX and README for task status.

> **Last Updated**: 2026-11-27 ~04:50Z (desktop session `uac-r2`). Private: `~/.claude/projects/c--code-files-spaarke-wt-unified-access-control-r2/private-*.md` (never copy into repo text).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Wrap-up, extended (round 90). `uac-r2` is authoritative: it reviews peers as an access gate and runs EVERY dev deploy. Live actions need the owner's go IN THIS SESSION; a deploy needs the owner to name the exact SHA here. A go relayed by another session is not a go. ≤ 2 agents at once. |
| **Dev BFF (CURRENT, mine)** | **`d307d67e36` LIVE ~18:00Z 2026-11-26** (owner in session: "go BFF deploy d307d67e36"; spike = master 51ce01a116 + read-only MCP tools `spike_get_profile`/`spike_review_nda`; new over 140c01219a: #2622, #3236, #3243, #3247). `C:/wtdep30`, 42.1 MB, SHA-256 verified. Smoke OK. **30-min gate PASS 18:31Z** (68 req; only 5xx = old /healthz/catalog 503; no external; no `client_search_index_dropped`; 8 known container refusals, 1 Redis lease renew, 1 sprk_scope metadata fault). BEFORE/LIVE/GATE notices sent to all 7 (six peers + specialist-legal-agents-r1). Deployed before the full test pass finished (owner's go). **Test pass GREEN after triage (~20:00Z):** build 0/0, arch 2845/2845; 14 load-timeout fails, isolated rerun passed 13; the last (EventStatusDeprecationTests) is known #3175 (demo-data scripts, not BFF). No rollback. specialist-legal-agents-r1 told. Rollback target stays `140c01219a` (`C:/wtdep28`). |
| **Dev SPA** | 24d53fb333 (spa-r4, 15:50Z). |
| **Agents running** | None. |

## Active: #3245 Assign removal (owner: "Approve, keep D-160", 2026-11-26)
- Audit note `notes/assign-privilege-audit-2026-11-26.md` (work branch 9a206976fc; also in the PR). Findings + peer answers commented on #3245.
- **PR #3269** (`fix/3245-person-role-assign-removal`, worktree `C:\wtasg`, commit b85f923fba, bound to this session): removes 38 Assign grants (Core User 25 — keeps `sprk_Event`; Office Add In User 9; AI Analysis User 4); config `removals` gap `ASSIGN-3245` (124 total); 2 MustNotHold rules; ASSIGN-3245 pending for the release after 1.3.3.0; CoreUserWizardLinkingGuardTests pin updated. Local: role guards 100/100, full arch 2845/2845. **MERGED ~22:20Z as 50bdad697c** (Router + Client lint green; only the advisory api-insights-integration timed out).
- **NEXT (owner's go needed in this session):** live dev `scripts/Set-FeatureRolePrivileges.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Remove -Gap ASSIGN-3245` (dry run first, then `-Apply`, then `-Remove -Verify`); rollback `-Restore -Gap ASSIGN-3245 -Apply`. Also remove the Workforce role's 8 inert Assign grants live (no role file). Re-run `scratchpad/asg2.ps1` and check. Then close #3245.
- Kept by owner: AI Analysis Admin (rounds 124/127), Ontology Service (DecisionRecord/BudgetRevision/Signal), Ontology Administrator Signal (D-160).

## Open peer reviews (uac-r2 as access gate)
- **mcp-r1 ISS-010 Part C: PR #3272** (head 18be14d973; the kill-switch fix was re-checked and approved; the PR quotes both approval lines). mcp-r1 merges it. Part E #3260 is MERGED to master as 4d69bf527e and NOT deployed. **NEXT: review the Part D1 diff when it is built.**
- **spa-r4 task 043 (PR #3267):** approved; both notes resolved (370390781e). Merge on owner's go in their session; dev deploy then on the owner's SHA go here.
- **ontology-r1 PR #3270 (task 225, DR inline share):** APPROVED 2026-11-26 (~20:05Z). Notes they accepted: keep the share after `accessWindow.Close()` whichever of #3270/Part C lands second; resumed/replay paths rely on the reconcile (one line in the PR). They merge after their Opus reviews + CI.
- **field-mapping-r1:** 051 is live on dev (verified). **Combined 064 (064+b+c+d, `3e0268349a..fd8172ea32`): APPROVED 2026-11-26 ~20:30Z.** Pre-deploy condition MET: all 18 create tables on dev return a boolean IsSecured (task-064-notes §11; repeat per environment before a deploy carrying 064). Target-side field-security skip = owner-decided known limit. Nit filed as DI-052 #3280. **MERGED as PR #3282 → 8975e13034** (~22:40Z); verified: after fd8172ea32 only 2 docs commits, master-merge 3c0ac94429 has no src/server resolution hunks. Re-run the IsSecured check just before the deploy.
- **ebilling-r1 PR #3290 (#3276, inline share of billing/procedure Decision Records):** APPROVED ~22:55Z (head c47eca7c2d), no findings. Not in candidate 8975e13034.
- **field-mapping-r1 next:** owner decided (their session) field-secured source values copy; separate PR to my gate. Check: other 064 boundaries kept + tested; decision record states the target-readability consequence.
- **mcp-r2 030 + ADR-052 amendment:** 030 closed; amendment PROPOSED until the owner's security sign-off.
- **ontology-r1 PR #3278 (task 230, read-only system playbooks), test file only:** APPROVED. Nit: pin the guard-after-filters ordering with a must-not-fire case.
- **word-add-in-r1 #3214 (task 140):** gate passed. **Task 141 (`fix/word-addin-141` 2a66132dc0, no PR yet): APPROVED 2026-11-26 ~20:45Z**. The named-.eml attachments add requires Write on the named .eml (version-save filter) plus EntityAccessFilter, IsFiledTo, the NotThisEmail check and the container guard. resolve-email-identity's savedAttachmentNames checks Read on each child via InternalPlaneDocumentNarrowing (ADR-008 A1). Nits accepted: replay confirmed per caller (IdempotencyFilter keys on userId); child id will be logged; batch or cap comes in their next fix round. **MERGED as PR #3289 → d2991fd743** (word-add-in-r1, ~22:15Z); they want the live SHA + gate result after the deploy.

## Waiting on the owner
- ADR-052 amendment security sign-off (mcp-r2).
- Next dev BFF deploy: exact-SHA go here. **Recommended: spike `5cd40984b0`** (specialist-legal-agents-r1 request 2026-11-27 ~04:40Z; branch `spike/specialist-agents-phase0`, never merged) = master 765a199922 (8975e13034 + H12b control-plane seeds only, no BFF) + spike tools incl. new G5 stubs `spike_review_agreement`/`spike_review_legal_hold`: access-reviewed here, no findings ([Authorize], closed arg, fail-closed caller, fixed text, no data access). Rollback d307d67e36. Post-deploy: tools/list on `/mcp/legal-operations` shows 4 spike tools; reply live SHA + time to them. Alternative: **`8975e13034`** (master, ~22:40Z) = d2991fd743 + 50bdad697c (roles only) + #3282 (051/063/064, approved). 51ce01a116 is its ancestor; it drops only the 3 spike commits (spike tools go away: tell specialist-legal-agents-r1 first). Server work over live: #3260 Part E, #3267, #3270, #3278, 141 (#3289), and un-gated 226 (#3277), 227, 228 (#3273), 232, #1697 notification priority, provisioning seeds (control plane, not the BFF). 226 + 228 checked here ~22:25Z: access-safe (226 reads only through the caller's client; its app reads are config/policy/record-type metadata it already had; 228 adds a refusal only, after the caller's own read). NOT in it: #3272 Part C.
- Dev hand checks: `notes/owner-test-checklist-2026-11-13.md`; ebilling W/X checklist; word-add-in 136-138.
- Quality alerts #1906, #2067, #2866: mention only. Open: #3111, #2158, #3125, #3175, #3241, #1506, #3264/#3265 (R4's).

## Scratch
- Keep: `C:\wtdep30` (live), `C:\wtdep28` (rollback), `C:\wtdep29` (test pass), `C:\wtasg` (PR #3269). Older `C:\wtdep*`, `C:\wtirp*`, `C:\wt3120` can go, only via `scripts/worktree/Remove-WorktreeSafe.ps1`.
- Scratchpad: `livecheck.ps1 -LocalDll <dll>` (live hash), `after30.ps1` (smoke), `gate-now.ps1 <t0>`, `gate29x.ps1` (exception detail, t0 18:00Z), `cand29.sh`, `asg1.ps1`/`asg2.ps1`/`asg3.ps1` (audit queries), `pr3245.md`.
- The untracked 0-byte `tests/integration/data-mutation/ExternalAccess/DirectMessageParticipantSharesTests.cs`: leave it alone.
- Peer pipe addresses: ebilling `cc-msg-c8f5bbc4…`, mcp-r1 `cc-msg-6ad27070…`, word-add-in `cc-msg-6723e836…`, field-mapping `cc-msg-833ddee7…`, spa-r4 `cc-msg-0fe1b7fa…`, ontology `cc-msg-c1a1d83c…` (or by bare name for spa-r4/ontology-r1/mcp-r1/specialist-legal-agents-r1).
