# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> - **Standing rules:** project `CLAUDE.md`. **Decisions:** `notes/decisions.md` (latest 2026-10-31: task 195 owner decisions).
> - **Defer list:** `notes/defer-issues.md`. **Previous copy:** `notes/handoff-history/current-task-2026-10-22-cp60.md`.

> **Last Updated**: 2026-11-01 ~03:22Z (checkpoint #84: task 195 deployed to dev with the owner's go; backfill applied; ribbon import and gate running).

## Task 195 live on dev (owner go "Deploy all + backfill", 2026-10-31)

R3's OK was for d0c1c5dc3c only. Ebilling was notified. Schema Verify PASS.

**BFF = d0c1c5dc3c**
- Deploy started 02:56:14Z from C:/wt2468 (detached at d0c1c5dc3c).
- The first try hit the platform's 10-minute deploy-status timeout and recovered automatically.
- The new build started at 03:09:00Z. 4 hashes match; healthz 200.
- All settings intact, including ebilling's new `ContentUnderstanding__Endpoint` (set at 02:54:30Z; the 02:55Z restart was theirs).
- **Gate window starts 03:09Z.**

**Web resources** (03:12Z, read back by SHA-256)
- `sprk_wizard_commands`, `sprk_createprojectwizard`, `sprk_creatematterwizard`.
- Live `sprk_wizard_commands` was byte-identical to b083 before the overwrite.
- Pages built with vite in C:/wt2468. Not `Deploy-WizardCodePages.ps1`, which republishes about 12 pages.

**Backfill `-Apply`** (03:14:07Z → 03:14:58Z)
- Filed 7, unverified 0, skipped 0, failed 0 (`$TEMP/uacmerge/uacg/bf195_apply.txt`).
- Root-owner moves already done: PRJT.10006.01 BU1→root; PRJ-000034 root→BU1; PRJ-000038 root→BU1.
- **PRJT.10001.01 inherited Restricted** from REAL-2026-123456.02 (round 84). It has no active project-level external grants, so no partner loss. Tell the owner.
- None of the 7 matters is secure.
- 0 No Access entries on the 14 records (R3's parent-rule check, answered).
- External grants on the 7 projects and their matters were sent to R3.

**Running**
- **Ribbon import agent (Opus).**
  - Dedicated ribbon-only solution for sprk_matter and sprk_project. Fresh export, `Test-RibbonExportCurrent`, platform-drift check, then merge only the 195 delta.
  - Then `Import-SolutionScoped` and verify with RetrieveEntityRibbon.
  - It STOPS without importing if the platform command definitions have drifted.
- **Gate job bb9ihq6me** runs at about 03:30Z: `gate9f4e.sh 03:09:00Z → now` into `$TEMP/uacmerge/uacg/gate_d0c1.txt`.
  - Split the external routes.
  - Ebilling's column delete (`sprk_invoice.sprk_invoicestatus`, 03:14:51Z) and its field-security change (about 03:05Z) fall in the window.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Final push, round-90 rules. Merge only when Router + "Client lint (type-aware ratchet)" are green; `--squash --match-head-commit <sha>`, no `--delete-branch`, after `git merge-tree`. Live actions, deploys and settings changes need the owner's go; shared-dev deploys go through the R3 protocol. |
| **Task** | **195** (#2352) **MERGED** as d0c1c5dc3c (#2496, 2026-10-31). **DEPLOYED to dev** (see the section above); the ribbon import and the gate are running. The design note `notes/task-195-project-filing-root.md` §4 has the implementation. Field-mapping has the SHA. |
| **What shipped** | **Core.** `ProjectMatterFiling` is the one core that writes the project pair. It does the caller checks, the secure gate, a version-conditioned app-only write and a read-back. Its two callers are `PATCH /api/v1/child-records/sprk_project/{id}` and the chat tool. **Rules.** `?move=true` is required to re-file a project that is already filed (otherwise 409 `project_filing.already_filed`; chat always sends move). A secure target is refused with 409 `secure_matter_unsupported` (#1506 interim). A project leaving a secure matter is filed and stays secure. **Census.** 3 report counts. **Wizards.** Create Matter offers Keep/Move. Create Project holds filing back while secure is blocked. The hosts lock on a partner-record launch. **Ribbon.** On the `sprk_Project_Matter_nn` subgrids, New opens the wizard and the platform New / Add Existing are hidden. **Backfill.** `scripts/Set-ProjectFilingFromMatterLink.ps1`, report-only by default. **Reviews.** Two Opus adversarial passes plus a Sonnet re-check; all F findings fixed; integration suites 199/8 and 353/23. **Filed.** #2493, #2494, #2495. |
| **Next action** | (1) Read the ribbon agent's report. On drift or failure, fix or escalate. (2) Read `gate_d0c1.txt`: 5xx, [OWNERSHIP-REFUSED], [ROOT-OWNER]/[ORDINARY-REOWN] moves for the 3 projects and their children, the new census counts (expect projectsWithMatterLinkButNoPair = 0), and the external split. (3) Send after-notices: R3 and ebilling (gate result), field-mapping (deploy time 03:09Z; their task 040 can proceed). (4) Owner browser click-test on the checklist (it doesn't hold the task open, round 90): N:N subgrid New opens the wizard; Add Existing hidden; other subgrids unchanged; the Related tab. (5) Close task 195: POML status, TASK-INDEX ✅, design note §5 live result; commit to work/unified-access-control-r2. (6) Clean up worktrees C:/wt195 and C:/wt2486 with Remove-WorktreeSafe. |

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
