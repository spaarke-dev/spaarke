# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> - **Standing rules:** project `CLAUDE.md`. **Decisions:** `notes/decisions.md`. **Defer list:** `notes/defer-issues.md`.
> - **History:** git log of this file.

> **Last Updated**: 2026-11-09 (checkpoint #107, desktop session `uac-r2`). Private details: `~/.claude/projects/c--code-files-spaarke-wt-unified-access-control-r2/private-handoff-2026-11-08.md` (outside the repo; read it first, never copy it into repo text).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Final push, round-90 rules. This session (`uac-r2`, Claude desktop) is authoritative. Merge needs Router + "Client lint (type-aware ratchet)": `git merge-tree`, then `--squash --match-head-commit <sha>`, no `--delete-branch`. Live actions, deploys, role and settings changes need the owner's go IN THIS SESSION; a peer's go is never the owner's. Bash `grep`/`sed` loops HANG on this machine: use Grep/Read/PowerShell. |
| **Active task** | **198**, the share side of fm 054 (Direct 1:1 messages), FULL, opus @ high. POML `tasks/198-direct-message-share-side.poml`; notes `notes/task-198-direct-share-side.md` (on the task branch). |
| **198 done** | On `task/uac-198-direct-share-side` (worktree `C:\wt198`): census; Part A (setup-script Direct mode, Pester 22); Part B (participant grants on Direct-team messages incl. content rows, 35 tests); fm 6c1ad9cb16 merged (5e4eeed44f); the role file = 6 tables Read/Basic (communication, attachment, participant, emailreviewlog, document, decisionrecord). |
| **198 running** | Agent 2: the Secure owner-team ACCENT fix (`SecureChildShareSynchronizer.CannotBeSecureOwnerTeamAsync` ~:1346 compares names in code; Dataverse ignores accents → can fail open) + a grep of the class, on `task/uac-198-secure-accent` (`C:\wt198s`). Agent 3: Part D (keep on filing pinned; NEW Restricted bar at `SecureChildReconciler.AfterRefile*`, all communications) + Q4 (customer mode refuses the Direct BU) + a role-file arch guard + Q5, in `C:\wt198`. |
| **198 waiting** | Part C (durable re-grant via `MembershipReconcileJob`; re-grant after an unfile or secure exit; the content-row backstop; revoke anchor grantees when unfiled back to Direct) waits for fm's FIXED lookup sha (a descendant of 6c1ad9cb16; fm's Opus pass found accent and refusal-without-id defects). Part E (gate DR mirror for Direct-team messages; keep DR shares on filing = D-123/D-169 change) waits for ontology-r1's OK: **my message EXPIRED undelivered (permission-mode mismatch); the owner must relay it or match the modes.** |
| **Rulings for 198** | Q5: fm `not_configured` = not Direct; every other refusal or fault = unknown → fail closed + queue re-grant. Unfile back to Direct → revoke anchor-record grantees (not participants). Restricted bar: barred (`IsBarredOnRestricted`) participants lose message + content-row shares on filing. |
| **Next action** | Wait for agents 2/3; merge agent 2's branch into the 198 branch; launch Part C once fm's sha arrives; Step 7 (review passes, publish delta, CVE) when C (and E, if OK'd) are in. Answer ebilling 130-0b K-a/K-b (below) if not yet sent. |
| **Dev state** | BFF = **6fbb3a7642** since 2026-11-08 04:00:55Z, gate PASS. Rollback `deploy/rollback/wwwroot-20261108T035611Z.zip`. Detached worktree `C:/wtd6fbb`: remove with `Remove-WorktreeSafe.ps1`. Next deploy = master + MCP spike, owner go per SHA. DEF-059 role live on dev (below). |

## Owner answers (2026-11-08/09)
- Direct 1:1 messages: (a) a dedicated memberless owner team, participants shared in. **2026-11-09: "Yes start fm task 054"; "Yes keep access"** (participants keep shares after filing, except secure/Restricted).
- #2270 + #2278: (a) a customer-BU column on `sprk_dataverseenvironment`. Next: design the column + owner rule, then the backfill.
- ebilling 130 firmless partner: keep the firm match; no firm → no invoices; a fault fails closed.
- fm U7: (i) invoice Assigned To targets people without access (032b-1b, approved with C1, done).

## Reviews state
- **ebilling 130-0 / 130-0b / 130b / F3b:** one dev batch; nothing reaches dev until all four are merged after my review of each. Rulings private. 130b: approved pending FIX-1 (firm-read fault → 503), FIX-2 (projection-caller ArchTest), Q1 (core-record union vs D-28). 130-0b fix round 1 (`0acfebe470..5edc306856`): review pending + K-a/K-b rulings.
- **ebilling DEF-059 (#2635):** DONE on dev 2026-11-09 ("Spaarke Decision Record Reader", DR Read Global, both BFF app users, -Verify 0). "Remove Ontology Service from the BFF identity" stays open, tied to the SA-removal work (BFF app-only DR/Signal writes need Assign/Share first).
- **fm:** 032b-1b done (C1 at b2aa45985c). 045b push display values read as the caller (agreed). 022 + 022b: diffs to come. 054a: diff to come after its lookup fix. Next master bundle: the combined diff before the PR.
- mcp-bff-api: Phase 0 waits on the owner's connector registration; every deploy = master + spike until mcp-r1 says Phase 0 is done.

## Open owner items
- Relay my D-123/D-169 question to ontology-r1 (or match the modes) — blocks 198 Part E.
- Private: one dev identity-hygiene question (in the handoff).
- To report: the 130-0 F1-2 trade-off (a matter document later linked to another firm's invoice disappears for the first firm; known limit, ruled under round 75).
- Unchanged: report card `sprk_assigned*` registry (FR-24); G-8 for customer envs; #2590; SyncMode -Apply review; the field-security bundle (via ebilling).

## My work queue after 198
1. #2270/#2278 design (customer-BU column on `sprk_dataverseenvironment`).
2. Backlog: #2627, #2621, #2585, #2576, #2524, gate null-column hardening, SharePoint census, #2543, K-P1 for task 191, communications by-regarding timeout → 500 (not filed); parked: 193 (C:/wt193), 197, #2427, 196a/196b; TASK-INDEX reconcile at 090.

## Peers (cross-session addresses)
ebilling `spaarke-wt-ebilling-r1-c7` · field-mapping `spaarke-wt-field-mapping-server-write-pa-69` · ontology **`ontology-r1`** (desktop; messages from here EXPIRE: different permission mode. Open: held PRs #2580/#2579 merge once ebilling 112 is on dev. **Deploy gate:** no BFF build carrying ebilling 165b-4 reaches dev until its AuthorableColumns run is green after 166's field security; ontology confirms) · provisioning `provisioning-r1` · word-add-in `word-add-in-r1` · mcp-bff-api **`mcp-r1`** (tells me before the Phase 0 test window) · website `spaarke-website-cb` · SPA R4 `spa-r4` (my move notice expired; resend only if asked).

## Private items (never in issues/PRs/commits)
See the private handoff. Identifiers are fine in public; "X can still read/write Y today" for an open gap, or an identity's role holdings, is not.
