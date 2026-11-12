# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> - **Standing rules:** project `CLAUDE.md`. **Decisions:** `notes/decisions.md`. **Defer list:** `notes/defer-issues.md`.
> - **History:** git log of this file.
> - **NOTE:** master is ahead of this work branch on project files (#2685, #2697, #2698, #2700, #2702, 191 PRs). This copy is the session's state carrier. Read master's TASK-INDEX and README for task status.

> **Last Updated**: 2026-11-11 (checkpoint #126, desktop session `uac-r2`). Private: `~/.claude/projects/c--code-files-spaarke-wt-unified-access-control-r2/private-handoff-2026-11-09-198.md`, `private-handoff-2026-11-08.md`, `private-p1-2026-11-11.md` (never copy into repo text).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Wrap-up, extended. Owner: "Build in uac-r2 now" for **191** (D-13) and **193** (D-14). Round-90 rules. This session (`uac-r2`) is authoritative. Merges: `git merge-tree` first, then squash with `--match-head-commit` after Router AND Client lint are green; ONE merge watcher at a time. Live actions need the owner's go IN THIS SESSION. Agents: Opus, a fresh short-path worktree each, own scratch folder `scratchpad\c<task>\`, at most 2 at once, never merge. |
| **Next action** | (1) Watcher `bp2tzsgxy` merges #2711 (191b) at head `c90d6a0a4a`. (2) When it merges, start Group B agents: 191c (after b+d), 191g (after b), 191h (after b), keeping at most 2 running (191e is running). (3) The 191f agent: wire the Pester file into CI on #2712 and push, then open a separate R1 BFF PR (`fix/uac-191f-r1-draft-marker`, `C:\wt191r1`): `EmailDraftToolHandler` must omit the marker columns on caller-written creates under `OwnedChildWrite.AppliesTo`. Review and merge both. (4) Review fm piece 3a, then 3b (named edit to `SecureChildReconciler`: a content row follows the RESOLVED Direct team only; unknown means no owner write). (5) After ebilling's 116 merges: next dev deploy = local merge of origin/master + spike `755987b385` (ledger conflict: master count + 1); include ontology #2695 and ebilling #2708. (6) Ask the owner the batched questions below before 191i. |

## Done today (2026-11-11)
- Merged: D-5 #2690, D-3 #2691, 090 housekeeping #2685, #2697 (194 closed), #2698 (191 design), **#2700 (193, `b4130cc5ea`)**, **#2702 (191d, `29b1d5b74b`)**.
- Dev BFF = **`b3ca99cad5`** (LOCAL merge: origin/master `ddae82fa90` incl. #2689 + spike `755987b385`; never pushed; worktree `C:\wtdep2`, kept until the next deploy). All gates passed; ebilling, mcp-r1 and ontology-r1 notified.
- fm: 054b and 054c approved and merged into the fm branch (K3 = #2701). Piece 3: Q1 agreed (`PartiallyConfigured` in the lookup; 198 reads it as unknown, fail closed; fm adds tests to the 198 test files, no 198 production change without asking me). Q2: a named edit by fm to `SecureChildReconciler`. Owner (in fm's session): 409 only on a 1:1 Message send; a transient lookup fault → 503.
- ebilling: the 164 ribbon was applied on dev. My SecureChild `-Verify` showed 10 failures, all from 096b's invoice live steps, which have not run; ebilling is asking the owner. #2708 (pin fix + new CI shard) was approved by me.
- 191b: the second adversarial pass gave F1–F4; all fixed. HardDeny marker added; AI guard unchanged.

## Owner decisions (2026-11-11)
- D-1..D-12 "Accept all"; D-13 191 and D-14 193: "Build in uac-r2 now". 193 B-F5: "Expired doesn't block".
- 191 Q1 (a) field-lock 3 columns; Q2 (b) item-level Office edit; Q3 (a) Read + AppendTo; Q4 (a) Restricted = internal only, Secure = share externally; Q5 (a) whole environment; Q6 (a) invoices SPA-only; Q7 accept all four.
- P-1: "Not yet" (leave the dev app user alone).

## Batched owner questions (ask before 191i; recommendation first)
1. **191d OQ-1, reader profile:** keep the External Collaborators default team with NO reader profile (secure flag, created-by person and document links show blank in forms for flagged users; BFF unaffected) + extend `scripts/common/OntologyWriterUnit.ps1` into a reserved-units list, so `Set-RecordCreatorPersonSchema.ps1 -Apply` won't add the team. (Rec.)
2. **191d OQ-2:** accept the 28 Global metadata reads + 5 org-owned catalogs (no customer rows). (Rec.)
3. **191d OQ-3:** leave `sprk_eventtype_ref` out of the role (it is user-owned). (Rec.)
4. **191d OQ-4 (mine):** `prvReadUser` / `prvReadTeam` floor is Local (`canbebasic=false`, read on dev), and all flagged users share one unit, so partners at different firms can see each other's user rows. Accept as a known limit and document it (Rec.), versus per-firm units (against Q5).
5. **191b → 191h:** documents become STRICTER for externals than 188 alone (the parent walk; `NoRoot` hides). Accept the stricter result (Rec.); count affected dev documents before 191h switches surfaces.

## Running agents / jobs
- **Watcher `bp2tzsgxy`:** merges #2711 at `c90d6a0a4a`.
- **191e agent** (`C:\wt191e`, `task/uac-191e`): placement job; reads the stored flag only, never `#EXT#`.
- **191f agent** (`C:\wt191f`, PR #2712 head `06be45b61b`): CI wiring, then the R1 PR (`C:\wt191r1`).

## 191 build order and live steps
- Done: b (#2711 merging), d (#2702), f (#2712 open). Group B: c (after b, d), e (running), g (after b; live test with the BFF identity first, owner go), h (after b). Then i (dev migration + live gate; each write an owner go), then j (docs + issues, incl. the provisioning follow-up for the field-security scripts, R3/K2; `.claude/constraints/auth.md` edit by the main session).
- 191i order (from 191f): deploy the BFF with the R1 fix → `Set-ExternalCollaboratorRole.ps1 -Apply` → `Set-RecordCreatorPersonSchema.ps1 -Apply` → access-marker dry run → `-BffOmitsMarkersFromCallerCreates -Apply` → `-Verify` → placement job → AC-3 gate. Run the marker steps after the release's last SpaarkeMaster import, and `-Verify` after every import.
- 191c handoff (from 191b): bind `ExternalVisibility.Readers`; Undecided → no grant, revoke, retry (design §6); a single-record caller maps Hidden and Undecided to one response; `DirectThreadAccessService` still grants `sprk_emailreviewlog` to participants.

## Peers
- **ebilling-r1:** 116 merged into their work branch; #2708 pending merge; 096b invoice live steps waiting on the owner (BEFORE/AFTER notices to me, then I re-run SecureChild `-Verify`).
- **field-mapping-r1:** piece 3a running; diff to me after its 2 adversarial passes; 3b after.
- **ontology-r1:** wants #2695 in the next deploy; asked the owner for a 177 re-send; 176 still blocked (no Revise-budget Work Item).
- **mcp-r1:** include the spike tip `755987b385` on every dev deploy until they say Phase 0 is done.
- Names: `ebilling-r1`, `field-mapping-r1`, `ontology-r1`, `provisioning-r1`, `mcp-r1`, `spa-r4`.

## Open items for the owner
- UAT U-1..U-13 (`notes/wrap-up-uat-and-decisions-2026-11-11.md` on master); 193 PCF import on dev (needs a go).
- D-2 steps 2–6 (fm 054b verified → Direct setup + `DirectMessage:*` settings, owner go HERE → 054c Stamp → piece 3 → 054c Move, the first Move scoped to one FMSWP- message).
- D-10 (142 setting, after U-3); 186 follow-up (Workforce role export); quality alerts #1821, #1906 (mention only).

## Scratch
- Worktrees: `C:\wtdep2` (live dev merge), `C:\wt191b`, `C:\wt191d`, `C:\wt191e`, `C:\wt191f`, `C:\wt193`, `C:\wt090`, `C:\wt191`, `C:\wtd3`, `C:\wtd5`, `C:\wt198`. Remove only with `scripts/worktree/Remove-WorktreeSafe.ps1` once their PRs merge.
- The untracked 0-byte `tests/integration/data-mutation/ExternalAccess/DirectMessageParticipantSharesTests.cs` in this worktree: leave it alone.
