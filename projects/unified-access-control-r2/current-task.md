# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint, never prepend; ≤10 KB. Standing rules: project `CLAUDE.md` (§2 Binding rules incl. D-11 team ownership, §3 Owner directives incl. the ADR-028 A9 block, §5 Environment incl. the R3-holds-dev deploy rule, §6 Gotchas). Decisions: `notes/decisions.md` (D-113 amended 2026-10-16). Defer list with the 2026-10-16 release disposition: `notes/defer-issues.md`. Task 186: `notes/186-model1-workforce-design.md` §0. Narrative: checkpoint commit messages.

> **Last Updated**: 2026-10-17 (checkpoint #43).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Final push to release (owner decisions 2026-10-16). Fix-round loop, then merge on a green Router and deploy via R3. A task closes on merge + deploy + server gate. |
| **Fix bar** | The most technically correct, best-practice, code-efficient fix. Never the quick route. No complexity without value. |
| **Order** | 099 (099b after #2025, 099c after #2035) and 186 (ceiling approved in round 101; building as a DRAFT cut from R3's tip (C:/wt186 @ b59e7e2a1, R3 agreed); merges only after R3's interim → master PR, which opens after R3's dev UAT) → 036 (alone, Opus xhigh). Then the remaining wrap-up 090 items, then the **UAT checklist** (scenarios by user type). |

## Open PRs (UAC) and their state
| PR | Issue | Worktree / branch | Head | State |
|---|---|---|---|---|
| #2074 | #2072 (regression from #2025) | — | — | **MERGED 1df4ee12d** on both required checks green (test-only fake fix + must-fire test). #2072 CLOSED. Ontology told. No deploy needed (test-only). |
| #2063 (099b) | #1010 D-11 half | C:/wt099b `feat/uac-099b-grant-team-ownership` | a74a7d159 | Fix round 2 done at 0e78c9b57, all 4 pass-2 findings: the owner-team Read pre-check before onboarding (`TeamHasReadPrivilegeAsync`, 5-min cache, faults never cached), the confirmed 403 classifier, re-owning active grants on Make Secure / Unsecure (`children_incomplete` when the team cannot own), neutral external detail, and the backfill per-row team check. Dev dry run: 62 to write, 0 unresolvable. Master merged to 0fa7c373d. Pass-3 re-check (Opus): 2 F (F4: the grant table is missing from `config/secure-record-owner-role.json` and no census guard catches it; F1: Make Secure moves only ACTIVE grants, so inactive rows leak to the old BU) plus K1–K4 (the root-only grant context, cache positives only with a fresh 403 confirm, the children_incomplete message, backfill counts and dedup, and a read-only live RetrieveTeamPrivileges probe). Suites green (TodoAssignee 34/34). Round 3 fixed at 59e76c88b (filtered 867/867, ArchTests 41/41, -SelfTest 28/28; role script dry run: only prvReadsprk_ExternalRecordAccess missing; K3 probe: SRO team lacks it, the BU1 default team holds it at Deep). Round 4 fixed at be0759e39 (the backfill takes every state: dry run 100 to write, 38 inactive, 2 to SRO; /unsecure-project childrenBlockedBy; filtered 750/750, -SelfTest 31/31). Re-check r3+r4: C# clean; 2 F in the backfill (the candidate filter does not filter, so it crashes on VERIFY after -Apply; the VERIFY "Candidates 0" is unreachable with Unfiled rows). Round 5 fixed at fc7a28232 (873/873, ArchTests 60/60, -SelfTest 39/39; dry run 100 to write, VERIFY NOT MET as expected before -Apply; feed false-renewal filed as #2076, needs a column). Re-check r5: the C# and filter are clean against the dev data shape (no row has both owners; dry-run counts identical to be0759e39); 1 F2 in the backfill (a failed root read becomes Unresolvable and VERIFY says PASS). Round 6 fixed at 24efacc77 (-SelfTest 48/48, 7 fail before; dry run: grant NOT MET 100 to write as expected, document and to-do PASS, 0 READ-FAILED). **REVIEW COMPLETE: the r6 re-check found 0 F**; the nits were applied at a74a7d159 (#Requires 7, the doc header). **Waiting on the OWNER: the Secure Record Owner role -Apply/-Verify on dev plus sign-off.** Then a green CI on the head, merge, R3 deploy, backfill -Apply, VERIFY, and the live gate. Round-6 scope: ReadFailed on non-404 reads; VERIFY = ToWrite 0 AND Unresolvable 0 AND ReadFailed 0 (Unfiled is residue); the fault-twin fresh assertion. Round-5 scope was: those 2 F, plus the reconciler fresh-classify on a refused assign, the per-pass team memo, the privilegeName↔logicalName ArchTest, and the investigate-and-fix of the false "renewed" feed item after a re-own (do not touch the reader at ~:2926). Rollout: the role script -Apply/-Verify per environment before or with the deploy. Then the owner gate: the Secure Record Owner role Read (pre-merge and pre-deploy) plus sign-off, then merge, R3 deploy, backfill -Apply after the deploy, and the live check. |
| #2061 (186, DRAFT) | task 186 | C:/wt186 `feat/uac-186-workforce-ceiling` | f26f0604c | **Review complete:** passes 1 and 2 plus 3 fix rounds and re-checks; no F-class left. Full integration run: BFF IntegrationTests 158/0/8 skipped, Spe.Integration 353/0/23 skipped (all skips need live config). Unit 422, ArchTests 144. K7 (the feed reads the caller's own grant rows without Read on the grant table) is accepted, as pass 2 judged. **Waiting on:** R3's interim → master PR (opens after R3's dev UAT), the owner's Workforce role edit (table in the PR body), and owner sign-off (auth). Then mark ready, merge master, Router, merge. |

## Merged (2026-10-16/17)
- **#2062 `883e415c0`** (2026-10-17; 099c; #961 closed; TrackingFieldTrio 1.0.54 served on dev, sha verified; the interactive check goes on the UAT checklist)
- **#2035 `1d435e695`** (2026-10-17; 099a; #1010 commented: the read half; R3 told, and the owner approved the next BFF deploy)
- **#2025 `bed14dd67`** (2026-10-17; #1576 (b)+(d); #1576 commented and stays open until the dev gate; R3 and provisioning (`-bb`) told)
- #2007 `96d5b16c2` (#2005, live gate PASS)
- #1983 `42aecf064`
- #2013 `0ccc75b4c` (#994)
- #2017 `9b72ea2c8` (#1363 demo expiry; issue stays open until the dev gate; provisioning told)
- #2029 `72b55bb6c` (#2002; dev gate in the next R3 deploy)
- #2024 `77cd69a03` (#1755 create-POST + explicit activate; #1755 commented and stays open until the dev gate; R3 told; the SPE admin session message is HELD (permission mode), so resend when it is released or tell the owner)
- #2021 `b1eaecb49` (share-link retired; task 012 closed; #1350 closed; #1752 link half commented)
- #2016 `2e4ffc654` (#1620 owner-aware unshare; R3 told). The post-save web resource 1.1.2 is **deployed to dev** (16:37Z, verified).
- Tasks 012, 023 and 183 closed; 20 done-task issues closed with evidence.

## Dev BFF (see the first bullet for the current build)
- **Batch 6 gates** (`notes/batch6-live-gates-2026-10-17.md`): #2021, #1620, #994 and #1755 PASS. The #1755 fixture is the secure project `a9e04977-…`, kept with its container.
- #1363: registration PASS. **First run PENDING: after 2026-10-18 00:15Z**, run the KQL in the batch6 notes and `GET /api/admin/jobs/demo-expiration/status`.
- #2002: **FAIL** → #2049. The reconciliation job still resolves with Write intent. Fixed by PR #2052, **merged at 901c7eeed** (R3 told). Next R3 deploy, then re-gate: 0 Warnings in 1 h.
- **Dev BFF = R3 build 9721819ed** (14:30–14:32Z 2026-10-17; sha verified, /healthz 200; contains #2025, #2035, #2062/099c, #2074 and ontology #2073; the only new server code vs 28720f997 is ontology's, so no new UAC gate). Earlier build 28720f997 (06:03Z). Batch 7 gates (`notes/batch7-live-gates-2026-10-17.md`): **#1576 PASS** ((a) and (c); live disabled-caller leg NOT AVAILABLE; issue closed with evidence). **099a PASS**: child-BU testuser1 sees 5 root-owned grants; 403 without Write; TrackingFieldTrio 1.0.50 served after the scoped publish at 06:28Z (the first import skipped the publish; always use `Import-SolutionScoped.ps1`). #2049: 0 Warnings so far; the 1 h query is scheduled for 07:05Z.
- Still to gate on later builds: 099b and 099c (after merge).

## Waiting on others
- **Ontology PR #2075 (102b rule matter scope, D-165)**: the UAC review at 5152cabea found no leak and all five claims hold; it REQUIRES 2 fixes (F3: the job guard at `SignalReevaluationJob.cs:279-289` has no test; F2: Signals stranded outside the scope only Warn, the run stays Success, pinned by the test at :110) plus K1–K4. Sent to ontology. Waiting for the new head, then re-check the fix diff only.
- **Ontology:** RESOLVED: #2039 option (b)/(c) = owner D-151 (both; 102a PR #2054 is UAC-OK at decbc9e6a). Ontology Administrator Append is live. 049 is applied live, including Q6 (Ontology Administrator Signal Write removed); repo PR #2050 is UAC-OK. **OPEN with the owner, through ontology:** remove the Ontology Administrator's prvAssignsprk_Signal and prvDeletesprk_Signal (recommended: remove).
- **ebilling-r1 task 026:** UAC gave technical approval (2026-10-16) for option A: sprk_billingevent becomes an intermediate whose root columns are its own sprk_matter, sprk_project and sprk_regardingworkassignment, never sprk_invoice (their D-75). The edits are IntermediateRootColumns, the TypeScript INTERMEDIATE_ROOT_COLUMNS, and RecordContainerResolver KindByEntity/ByEntity. Conditions: a live column check; restamp on re-file plus reconciler coverage; D-75 must-fire tests; storage-resolver tests for a secure matter; and TypeScript parity. The diff was reviewed 2026-10-16 (ef3efce73..b5f4a4c78): no F-class findings; one cosmetic doc fix sent; #2047 filed (line storage carrier and invoice single-root). Report it to the owner. Fix round 1 (c4c55302d, 7e4fb0c97) re-checked 2026-10-17: no F-class findings in the diff; 812/812; the tests fail under mutation. A pre-existing gap went to ebilling: an invoice re-file is never checked against its lines. Fix (a) is an invoice-side line query (UAC leans app-only); (b) is to file it. The owner decides through ebilling. K4: RefusalKind defaults to Transient on a permanent result. DEF-022 (#2056): the direction is agreed; awaiting their range.
- **Provisioning:** #2027 (org auditing and retention are never set; FR-32's interim audit trail depends on them). Asked whether and when they'll take it. **Retention value = owner decision** (recommended -1, Forever).

## Open owner items (put in the final report)
- **186 ceiling:** approve option (a), and add to the Workforce role on `sprk_servicerequest` (Basic): Create, Read, AppendTo, Delete; plus Create on `sprk_document` if uploads are wanted. See design note §0.
- **#2027** retention value. **#2025** sign-off (auth). **099** sign-off (auth path).
- Live CIAM checks: §12/§11, §14 (135 gate, 136, 037, 039); 140's four-identity gate; 153 §7; 062 NFR-05 at UAT.
- Board #2 views; #1747; #1776; ontology #1876/#1877/#1879; Model 1 `-Apply` restatement; checklist items for 140/153/165/171.

## Remaining wrap-up 090
- 058 docs.
- Keep #1059, #1063 and #1077 open until their owner gates; #961 until 099; #1074 and #1567 until 036; #1014 and #1564 are PARTIAL (owner/peer).
- Then the UAT checklist.
