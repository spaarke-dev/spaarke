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
| #2025 | #1576 (disabled users + per-request token) | C:/wt1576 `fix/uac-1576-disabled-users-per-request-token` | 689f217ff | **READY: awaiting owner sign-off (auth).** 2 full passes + 2 scoped re-checks; no F-class. Known limits closed (fail-closed JIT sweep and Decision Record owner, guard shapes, per-site ratchet). Filed #2033 (disabled sender) and #2034 (guest writer, provisioning). After merge: close #1576 parts (b) 61b0aa77a and (d) 633cce424; tell provisioning about the baseline marker. |
| #2024 | #1755, #1735 create-POST | C:/wt1755 `fix/uac-1755-bounded-provisioning-token` | 428c7219a | **Round 3 running:** a failed compensating delete is reported honestly; connect-refused is classed as not sent; state-machine test; wording; and an **explicit `/activate`** after create (Learn: inactive containers auto-delete after 24h). Then a scoped re-check, then merge, then ping sdap-SPE-admin-app-r2 (#2032: their create, activate, lock and unlock). |
| #2035 (099a) | task 099 (amendment 5) + #1010 read half | C:/wt099a `feat/uac-099a-record-grants-read` | 882eae925 | **Adversarial pass 1 running.** Then pass 2 and owner sign-off. After merge and an R3 deploy, run the live check in the PR body (child-BU Core User sees root-owned grant G; TrackingFieldTrio 1.0.48 import). Next: **099b** (after #2025 merges), **099c** (after #2035). #1010 closes after 099a+099b; #961 closes with 099c. |

## Merged today (2026-10-16)
- #2007 `96d5b16c2` (#2005, live gate PASS)
- #1983 `42aecf064`
- #2013 `0ccc75b4c` (#994)
- #2017 `9b72ea2c8` (#1363 demo expiry; issue stays open until the dev gate; provisioning told)
- #2029 `72b55bb6c` (#2002; dev gate in the next R3 deploy)
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
