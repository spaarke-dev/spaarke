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
| #2063 (099b) | #1010 D-11 half | C:/wt099b `feat/uac-099b-grant-team-ownership` | aef835fa2 | Pass 1: F1 the secure-owner role prerequisite (**owner OK asked**: probe → config → Set-SecureRecordOwnerRolePrivileges; merge AND deploy prerequisite); F2 invite-and-grant onboards before resolving the owner; F3 the backfill skips 5 human-owned grants; the script's secure mapping for docs/todos fixed in scope; K2 the materializer renewal refusal. **Fix round 1 running** (plus a specific `owner_privilege_missing` code, and reconciling the dry run against secure project 31e232ae's grants). Filed pre-existing: #2064 (AI create_record bypasses the grant core), #2065 (UpdateAsync with no If-Match can create a row). Then pass 2 and owner sign-off; after the deploy: -Apply, then the dry-run verify. |
| #2061 (186, DRAFT) | task 186 | C:/wt186 `feat/uac-186-workforce-ceiling` (base b59e7e2a1 = master + R3 61e74ef64) | e6de930ae | Round 1 fixed: `WorkforceCeilingEndpointFilter` plus a declaration on every `/api/v1/external` route, the coverage ArchTest, every module at the seam, feed filtering, and /me/entitlements intersected. Suites 1964, ArchTests 97, +12 KB. R3 files: two exemption lines (registration, link) sent to R3 for review. **Adversarial pass 2 running (full set).** Owner role edit (dev lacks): sprk_servicerequest C/R/AppendTo/D; sprk_document C, D; sprk_todo C, W, D; sprk_event C, D; sprk_externalrecordaccess C, W. Still owed: a full integration run, and R3's interim PR merging first. |
| #2062 (099c) | #961 | C:/wt099c `feat/uac-099c-expiration-picker` | 163bbaf6f | Round 1 built (picker and Apply; 229/229 tests). **Round 2 running:** every add sends the applied record date (option A, main-session decision 2026-10-17, see notes/session27); editable with no shares; TrackingFieldTrio 1.0.52. Then one adversarial pass; import on dev after merge, once the BFF with 1d435e695 is live. |

## Merged (2026-10-16/17)
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

## Dev BFF = R3 build d5401cf2d (deployed 2026-10-17 02:27–02:29Z; contains master e702c3401)
- **Batch 6 gates** (`notes/batch6-live-gates-2026-10-17.md`): #2021, #1620, #994 and #1755 PASS. The #1755 fixture is the secure project `a9e04977-…`, kept with its container.
- #1363: registration PASS. **First run PENDING: after 2026-10-18 00:15Z**, run the KQL in the batch6 notes and `GET /api/admin/jobs/demo-expiration/status`.
- #2002: **FAIL** → #2049. The reconciliation job still resolves with Write intent. Fixed by PR #2052, **merged at 901c7eeed** (R3 told). Next R3 deploy, then re-gate: 0 Warnings in 1 h.
- **Dev BFF = R3 build 28720f997** (06:01–06:03Z 2026-10-17; contains 901c7eeed, bed14dd67, 1d435e695). Batch 7 gates (`notes/batch7-live-gates-2026-10-17.md`): **#1576 PASS** ((a) and (c); live disabled-caller leg NOT AVAILABLE; issue closed with evidence). **099a PASS**: child-BU testuser1 sees 5 root-owned grants; 403 without Write; TrackingFieldTrio 1.0.50 served after the scoped publish at 06:28Z (the first import skipped the publish; always use `Import-SolutionScoped.ps1`). #2049: 0 Warnings so far; the 1 h query is scheduled for 07:05Z.
- Still to gate on later builds: 099b and 099c (after merge).

## Waiting on others
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
