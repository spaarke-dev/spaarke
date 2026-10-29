# Current Task State — `unified-access-control-r2`

> **Format:** this file holds the CURRENT state only. Rewrite it at each checkpoint (never prepend), and keep it at or under 10 KB.
> - **Standing rules:** the project `CLAUDE.md`.
> - **Decisions:** `notes/decisions.md` (latest: a work assignment's owning unit follows its matter, 2026-10-29) and `notes/session27-owner-decisions-and-research.md` (round 136: customer BU = top level for customer records; 135: Unsecure keeps all access).
> - **Defer list:** `notes/defer-issues.md`.
> - **Delete design:** `notes/core-record-delete-design.md`.
> - **Last deploy + gate:** 136568cf70 on dev 2026-10-29 02:23Z (R3; SpaarkeMaster 1.2.4.0 published from a4b6459da3); gate PASS (a, b, d), filed #2431 (Redis lease, pre-existing). It does NOT contain 194b (2cd0f62e2c).
> - **Previous copy:** `notes/handoff-history/current-task-2026-10-22-cp60.md`.

> **Last Updated**: 2026-10-29 (checkpoint #74, by context-handoff).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Final push, round-90 wrap-up rules. Merge only when Router + "Client lint (type-aware ratchet)" are green; one merge job at a time; `--squash --match-head-commit` (the COMMIT sha, not a merge-tree id), no `--delete-branch`, after `git merge-tree`. Deploy via R3 (the owner's go is given in R3's session). A task closes on merge + deploy + a server-side gate. |
| **Fix bar** | The most technically correct fix; no complexity without value. Every defect found is fixed or filed. Public repo: no live exploit mechanism in issues, PRs, commits or committed notes (neutral wording; owner decides tracking of access defects). |
| **Agents** | Opus for executors and verifiers; Sonnet high for scoped re-checks and mapping. Every model is stated. Helper scripts use unique prefixes in private temp folders (`uacmerge/uacg/`). Cap 4 concurrent. No `jq` on this machine: poll with `python -I`. |
| **Peers** | R3 `spaarke-wt-spa-external-access-platform-ce`, ebilling `spaarke-wt-ebilling-r1-0a`, field-mapping `spaarke-wt-field-mapping-server-write-pa-11`, ontology `spaarke-wt-spaarke-ontology-platform-r1-be`, word-add-in `spaarke-wt-spaarkeai-word-add-in-r1-c4`, provisioning `spaarke-wt-customer-provisioning-orchest-2f` (last two: verify). |
| **Gate tooling** | App Insights `spe-insights-dev-67e2xz` (RG `spe-infrastructure-westus2`). BFF `spaarke-bff-dev.azurewebsites.net`. Script: temp `uacmerge/uacg/uacg_gate.sh`. |
| **Next action** | Wait for the owner's answers (below). Then, when the owner decides BU1 Q1, tell R3 whether the 194b deploy hold is cleared. |

### This session (2026-10-29, done)
- PR #2440 merged (1e409b2ad2): branch sync of project state/notes/tasks 188-195. Project issue #808 commented; board synced (192 tasks / 166 done).
- PR #2451 merged (3d54ac845d): `SecureChildLineage.RootLookupOrder[sprk_workassignment]` derived from `TypedLookupsOf` (matter, project, invoice, event; others after). 2 Opus adversarial passes: no widening. Dev 0/31 rows change. Decision recorded in `notes/decisions.md` (de370a6898, pushed). Worktree C:/wtuacwa removed; remote branch `fix/uac-r2-wa-primary-order` kept (owner to say delete).
- Main checkout `C:/code_files/spaarke` local master is stale at c36907571d: ff-only refused (someone's uncommitted `.claude/agent-memory/researcher/MEMORY.md` there). Not touched.
- Untracked 0-byte `subgridParentRollup.test.ts` at this worktree's root: stray, left alone.

## Next actions (mine)
1. **Task 194b (#2339): MERGED 2cd0f62e2c (PR #2432).** Dev teams live (restricted 16685363, config 2dae7e6e); provisioning takes it as H7c (#2429).
   - **Dev deploy HOLD is OPEN** (told R3): 194b goes to dev only after the dev BU1 move. R3 is asking the owner: (1) wait, or (2) a dev build from a pre-2cd0f62e2c base. Message R3 when the hold clears.
   - **Dev BU1 move:** plan at `uacmerge/uacg/rbu1/rbu1_plan.md` (+ dry-run CSV; owner Q1-Q12). **BLOCKER Q1:** product tables are read at Deep, so a BU1-homed user cannot read root-owned product rows. Analysis output `uacmerge/uacg/rbu1q1/rbu1q1_analysis.md` (status from cp#73: verify). No writes before the go.
   - Then: deploy via R3 → report-only run + ontology memo-LIKE proof → owner go for writes → close. Remove C:/wt194 (safe-remove script).
2. **Task 193 (#2338):** C:/wt193, `fix/uac-193-add-person` (cp#73: G4b running; verify head). Then fix-diff re-check → PR → merge → owner go for `Deploy-ExternalRecordAccessPairedSystemUser.ps1` → deploy via R3 → TrackingFieldTrio (1.0.60) import → gate. After merge: tell ontology (DI-136 #2457 swaps `ContactUserRepresentation` into `AssignTaskRecipientGate`; use `DecideOnLink`).
3. **Task 197:** C:/wt197, `fix/uac-197-stored-match-and-session-owner` (cp#73: D3 running; verify). Then D4, E, F, G → 2 security passes → PR (neutral) → merge → deploy via R3 → owner go for the dev clean-up -Apply.
4. **#2427 (output lookups):** C:/wt2427 `fix/uac-2427-output-lookups` (cp#73: fix round running after pass 1 F1; verify). Then fix-diff re-check + pass 2 → PR; send sha to ebilling.
5. **Task 195** unblocked. **196a** after 193 merges; **196b** after 194b (`notes/task-196-unsecure-keeps-access.md`).
6. **#2382:** ask the owner to retry UAT check 5, then close. #2396 needs a role decision. **#2292** when there is capacity.

## Peer reviews (UAC is the gate)
- **R3 #2450** (9 external grid columns, `sprk_invoicestatus` retired): APPROVED; auto-merge on. No deploy until the hold clears / owner OK naming the SHA.
- **ebilling #1911:** 194b writer change + stored-file fix `task/ebill-storedfile-owner` (e27c849bf3, K pin d3eb47376a): re-check PASS; Q1 ForRow on the invoice's core accepted. #1911 may go ready. They will send the work SHA. `CheckVersionOwnerAsync` moved to a permanent exemption list (D-197).
- **ontology #2418** (Assign Task email): fix round 2 (e8b460a9ea..16f80366ab) PASS (multi-root rule via `CheckLiveParentsAsync`; CIAM compose lift is behaviour-preserving). Their second pass covers the rest. #2439 merged (f07ab2ef1f: 171, 172, 194b repoint).
- **field-mapping:** #2444 filed (shared reads-agnostic primary-order helper). C4 = their design §12.11 (depth cap from the top, owner 10-29). Item 3 resolved by #2451. §13.5 Q1/Q2 owner answers stand (W3 PR comes to UAC). After 194b: RecordFamilyEdgesGuardTests + 037 pins.
- **ebilling 094/098:** READY (cp#73). Standing: no Procedure-table role privilege without UAC review; D-185.
- **word-add-in task 130:** two security passes when the PR arrives.

## Owner items outstanding
1. **New (10-29):** (a) confirm a work assignment's `sprk_regardingcommunication` ranking last; (b) tracking for two pre-existing mismatches from the #2451 reviews (public neutral issues vs notes only): creation vs un-secure cascade for a work assignment filed only under an invoice/event; Decision Record gate writer look-through vs the reconciler for a non-team-owned work assignment; (c) R3 deploy option (1)/(2) — via R3; (d) BU1 Q1-Q12.
2. Task 191: F1 and S4 block T191b onward. Task 190: Apply + the Restricted call on the 91 unmatched archives.
3. Decisions: #2343, #2245, #2250, #2260, K5/#2270 (via R3), #2262, #2396; the dev arrangement for root-homed users.
4. Optional live checks: D7 partner document read; #2177; #2161; T191 MDA/Copilot/grid.
5. Carried: #2163 environment go; 186 intake checks 3/5b/6; 036 gates (ii)–(vi); #2153 sign-off.

## Recently filed
#2423; peers: #2410, #2411, #2413, #2415, #2424-#2426, #2444, #2457.
