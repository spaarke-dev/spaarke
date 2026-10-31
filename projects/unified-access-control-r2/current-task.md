# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> - **Standing rules:** project `CLAUDE.md`. **Decisions:** `notes/decisions.md` (latest 2026-10-31: task 195 owner decisions).
> - **Defer list:** `notes/defer-issues.md`. **Previous copy:** `notes/handoff-history/current-task-2026-10-22-cp60.md`.

> **Last Updated**: 2026-10-31 (checkpoint #83: task 195 merged as d0c1c5dc3c; deploy awaits owner go).

## Deployed and gated
- **Dev BFF = b083d62183** (master, including #2486, field-mapping 017).
  - I deployed it at 19:52:25Z on 2026-10-31, with the owner's go in this session and R3's OK for that SHA. Router passed.
  - Schema Verify passed; the package was 39.07 MB; the 4 critical-file hashes matched; healthz passed.
  - App settings unchanged: ebilling's keys, and both UAC switches (RootOwnerWritesEnabled, OrdinaryMovesWritesEnabled) still true.
  - R3 and ebilling were told the deploy is done.
- **Gate PASS** (19:52:25Z → about 20:12Z; `$TEMP/uacmerge/uacg/gate_b083.txt`):
  - 23 requests, 0 failed, 0 5xx, 0 [OWNERSHIP-REFUSED];
  - the reconciler wrote nothing;
  - external routes had 0 traffic;
  - only the pre-existing warnings: SECURE-CENSUS, and acme ID-LINK #2163.

  After-notices sent to field-mapping (the re-seed is theirs, with the owner's OK in their session), R3 and ebilling.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Final push, round-90 rules. Merge only when Router + "Client lint (type-aware ratchet)" are green; `--squash --match-head-commit <sha>`, no `--delete-branch`, after `git merge-tree`. Live actions, deploys and settings changes need the owner's go; shared-dev deploys go through the R3 protocol. |
| **Task** | **195** (#2352) **MERGED** as d0c1c5dc3c (#2496, 2026-10-31). Not deployed. The design note `notes/task-195-project-filing-root.md` §4 has the implementation. Field-mapping has the SHA. |
| **What shipped** | **Core.** `ProjectMatterFiling` is the one core that writes the project pair. It does the caller checks, the secure gate, a version-conditioned app-only write and a read-back. Its two callers are `PATCH /api/v1/child-records/sprk_project/{id}` and the chat tool. **Rules.** `?move=true` is required to re-file a project that is already filed (otherwise 409 `project_filing.already_filed`; chat always sends move). A secure target is refused with 409 `secure_matter_unsupported` (#1506 interim). A project leaving a secure matter is filed and stays secure. **Census.** 3 report counts. **Wizards.** Create Matter offers Keep/Move. Create Project holds filing back while secure is blocked. The hosts lock on a partner-record launch. **Ribbon.** On the `sprk_Project_Matter_nn` subgrids, New opens the wizard and the platform New / Add Existing are hidden. **Backfill.** `scripts/Set-ProjectFilingFromMatterLink.ps1`, report-only by default. **Reviews.** Two Opus adversarial passes plus a Sonnet re-check; all F findings fixed; integration suites 199/8 and 353/23. **Filed.** #2493, #2494, #2495. |
| **Next action** | **Owner go needed** for the dev deploy of master ≥ d0c1c5dc3c. Run the R3 protocol first: R3's OK on the SHA, notify ebilling, schema Verify. Order: (1) BFF via `Deploy-BffApi.ps1` from a fresh short-path worktree; (2) `Deploy-WizardCommandsJs.ps1`; (3) `Deploy-WizardCodePages.ps1`; (4) the ribbon import with `Import-SolutionScoped.ps1`, then a live click-test: N:N subgrid New opens the wizard and Add Existing is hidden; other matter and project subgrids keep their buttons; check the associated "Related" tab; (5) the gate; (6) the backfill report, then `-Apply` (accepted: FILE 7, 3 business-unit moves), then a gate on the root-owner moves. Then send field-mapping the deploy time and close task 195 (TASK-INDEX, POML). |

## Owner decisions for 195 (2026-10-31)
1. Re-filing to another matter is allowed. Nothing is ever released from isolation.
2. Adding a matter to a project's matter list goes only through the wizard. The model-driven app's out-of-the-box subgrid New/quick-create and Add Existing are replaced.
3. The backfill is accepted with all 3 unit moves, applied after deploy, report first.
   - Project 6 e42aa40d: BU1 → root.
   - PRJ-000034 a8dbfb72: root → BU1.
   - PRJ-000038 479c5a63: root → BU1.

## Next (mine, in order)
1. Gate for b083 → after-notices (above).
2. Task 195 (above).
3. BU1 Q2–Q12 recommendations for the owner (`$TEMP/uacmerge/uacg/rbu1/rbu1_plan.md` §6), including K4.
4. Review field-mapping's #2483 follow-up and its 039 diff when they arrive.
5. Clean up these worktrees with `Remove-WorktreeSafe.ps1`, all merged: C:/wt2468 (deploy checkout, at b083d62183), C:/wtuacq1, C:/wtuacwo, C:/wtuacrf, C:/wt2471f, C:/wt2486, C:/wt194.
6. Parked (verify state):
   - task 193 (C:/wt193), task 197 (C:/wt197);
   - #2427 output lookups (C:/wt2427; ebilling waits for its sha);
   - 196a/196b.

## Owner items outstanding
- The owner-private items in `scratchpad/q1_owner_private.md`.
- Whether chat should send invites, as its own change, through field-mapping.
- BU1 Q2–Q12.
- Older:
  - decisions #2343, #2245, #2250, #2260, K5/#2270 (via R3), #2262, #2396;
  - task 191 F1/S4;
  - task 190 Apply;
  - #2382 UAT check 5.

## Filed this stretch
- #2478: a filing is left behind after a refused Office save. Not an access issue.
- #2487: the membership junction owner row stays stale until the nightly cleanup after a reconciler re-own.

## Peers
R3 `spaarke-wt-spa-external-access-platform-ce`; ebilling `spaarke-wt-ebilling-r1-0a`; field-mapping `spaarke-wt-field-mapping-server-write-pa-11`; ontology `spaarke-wt-spaarke-ontology-platform-r1-be`; word-add-in; provisioning.
