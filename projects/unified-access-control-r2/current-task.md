# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint, never prepend; ≤10 KB. Standing rules: project `CLAUDE.md` (§2 Binding rules incl. D-11 team ownership, §3 Owner directives incl. the ADR-028 A9 block, §5 Environment incl. the R3-holds-dev deploy rule, §6 Gotchas). Decisions: `notes/decisions.md` (D-113 amended 2026-10-16). Defer list with the 2026-10-16 release disposition: `notes/defer-issues.md`. Task 186: `notes/186-model1-workforce-design.md` §0. Narrative: checkpoint commit messages.

> **Last Updated**: 2026-10-16 ~17:30Z (checkpoint #38).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Final push to release (owner decisions 2026-10-16). Fix-round loop, then merge on a green Router and deploy via R3. A task closes on merge + deploy + server gate. |
| **Fix bar** | The most technically correct, best-practice, code-efficient fix. Never the quick route. No complexity without value. |
| **Order** | 099 (running) → 186 (waits on the owner's ceiling answer) → 036 (alone, Opus xhigh). Then the remaining wrap-up 090 items, then the **UAT checklist** (scenarios by user type). |

## Open PRs (UAC) and their state
| PR | Issue | Worktree / branch | Head | State |
|---|---|---|---|---|
| #2025 | #1576 (disabled users + per-request token) | C:/wt1576 `fix/uac-1576-disabled-users-per-request-token` | 3c777073c | Pass-1 fixes done (NotificationService, materializer disabled → Unresolved, guard shapes, comments, K3 tests; master merged). **Pass 2 running** (Opus). One baseline entry left: `DataverseWebApiGuestUserWriter.cs:282` (provisioning file, active project) — pass 2 judges; file/report to provisioning. Then **owner sign-off** (auth). After merge close #1576 parts (b) 61b0aa77a, (d) 633cce424. |
| #2024 | #1755, #1735 create-POST | C:/wt1755 `fix/uac-1755-bounded-provisioning-token` | 63543aae3 → fix round running | The diff is CLEAN. Adjacent F1s being fixed: per-request Polly opt-out for the create POST, the Step 6 "may exist" message, Step 7 read-back, K4 test hygiene. General case filed as #2028. Then a scoped re-check, then merge. |
| #2017 | #1363 (demo expiry) | C:/wt1363 `fix/uac-1363-demo-spe-marker` | 90bd3879a | Re-check: no F1–F3. **Fix round running**: F4 next-link scheme+port check, K3 repeated-link guard, K2 404-only-with-GUID. Then merge; tell provisioning the head SHA. |
| #2029 | #2002 (no-root Warning noise) | C:/wt2002 `fix/uac-2002-ancestor-warning` | bd53f762f | I reviewed it CLEAN. **Merge watcher running** (background job). Close #2002 after the merge. |
| (099a) | task 099 (amendment 5) + #1010 read half | C:/wt099a `feat/uac-099a-record-grants-read` | — | **Executor running** (Opus high): gated `/record-grants` route + TrackingFieldTrio via BFF. Two adversarial passes, owner sign-off. Then **099b** (D-11 grant ownership + extend `Backfill-RecordOwnership.ps1`; after #2025 merges) and **099c** (picker; after 099a). #1010 closes after 099a+099b; #961 with 099c. |

## Merged today (2026-10-16)
- #2007 `96d5b16c2` (#2005, live gate PASS)
- #1983 `42aecf064`
- #2013 `0ccc75b4c` (#994)
- #2021 `b1eaecb49` (share-link retired; task 012 closed; #1350 closed; #1752 link half commented)
- #2016 `2e4ffc654` (#1620 owner-aware unshare; R3 told). The post-save web resource 1.1.2 is **deployed to dev** (16:37Z, verified).
- Tasks 012, 023 and 183 closed; 20 done-task issues closed with evidence.

## Next dev BFF build (via R3 only; R3 session `spaarke-wt-spa-external-access-platform-*`)
- Must include master ≥ 2e4ffc654: #994, #2016, #2021, ontology #2020, then #2017, #2024, #2025, #2029 and 099 as they merge.
- Run the ancestry check, then the server gates: #1620, #994, #1576, #1755, #1363, #2002.

## Waiting on others
- **Ontology:** PR #2022 (task 049) is UAC-OK at 2dd6a67ad; no live -Apply until their second pass and the owner's go. PR #2019 (task 104): UAC has no objection. Q1 (Ontology Administrator Signal Write) is with the owner, through ontology.
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
