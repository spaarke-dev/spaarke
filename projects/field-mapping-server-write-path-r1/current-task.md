# Current Task — field-mapping-server-write-path-r1

> **Last updated**: 2026-11-06 ~19:30Z (main session checkpoint). Recovery: read Quick Recovery first.
> **Done on master**: 001–006, 010–018, 037–039, 042c, 043, 046, 046b (#2592), 048, 049 (#2599, 20669df6a9), 070–074.
> **Live on dev:** BFF + Console bundle at master 20669df6a9 (deployed by UAC-r2 16:44Z, gate PASS; no organic traffic on the new routes yet). 046b roles applied. G-8 on. Core User given to 5 add-on users (17:30Z). Console User removed from root team "Spaarke" (18:11Z).
> **W3 direction (owner + UAC-r2, decisions.md 5e4f4be3aa):** named exceptions plus guards; no core redesign in R1; no given-id input in RecordCreationService.

## Quick Recovery

| Field | Value |
|---|---|
| **Branches (none merged yet)** | 046c → task/fm-046c-fix ad88f1f9b7 (review pass 2 running). 030a → task/fm-030 c2f99fa55b, fix round on task/fm-030a-fix running. 044 → task/fm-044-build 6d98e001af; fix round on task/fm-044-fix running, LIMITED to the BFF null-pair fix + caller-read inventory (script items HELD pending owner A/B). 040 → task/fm-040 6e7ac9cae4, Opus review running. 032 → task/fm-032 96769b68bf (notes only). 034 → task/fm-034 bb887aadcd (notes only). |
| **Owner questions open** | (1) 044: A = drop field security on the core pair (keep fail-closed re-read, column auditing confirmed on both pair columns, guard no non-admin Write on Signal/DR, 052 reports half pairs AND pair changed after create) vs B = keep field security. Ontology-r1 + UAC-r2 accept A with those conditions. Revises 042c ruling. (2) Go for live checks on dev: Project→Matter profile + 021 re-measure (FMSWP- rows). (3) 032: FR-20 mapping for extracted invoices in R1 (mapping step in persister) or deferred (recorded: deferred). (4) Office To Do no assignee: mapped Assigned To beats caller contact (A, new order) or caller wins (B, old). |
| **Next actions** | On each review/fix report: fix round or scoped re-check → merge into work branch serially (030a and 046c both edit FeatureRolePrivilegeGuardTests.cs) → verify-suites → publish-delta → PR. 046c: PR then dev apply from THIS session with owner go (order: G17 changes -Apply/-Verify, then -Remove -Gap R3 -Apply/-Verify; rollback -Restore -Gap R3). 044 fix diff → ontology-r1 + UAC-r2. 040: UAC-r2 sign-off on #2188 caller-mask guard exemption; 075 ships with 040. 030a/030b PRs → word-add-in for a content read. Re-scope POMLs 030b, 032, 033, 034 under the W3 direction. File the 044 H7b provisioning issue (neutral). Sweep notes for per-identity privilege detail before the next master PR. |
| **Private (message only)** | UAC-r2 and ontology-r1 get access findings by message; never in repo text. |
| **Issues** | #2597 (flaky OntologyAdmin Rules test), #2583, #2574, #2590, #2444, #2413, #2429, #2469, #1948. Open PR #2387 (075, HOLD, with 040). |
| **Worktrees** | Many agent worktrees under C:/code_files/spaarke/.claude/worktrees (046b, 049b, 049c, 043, 046, 075, 039, plus today's 030/032/034/040/044/046c and fix rounds). Remove at project end via Remove-WorktreeSafe. |

## How wave-2 agents are run (lessons)

- `isolation: worktree` cuts from **origin/master**. Every brief starts with `git switch -c task/fm-NNN <tip>`.
- Launch adversarial-reviewer passes from the main session (one Opus per change set, two for security; a scoped Sonnet re-check per fix round). After an idle pause, start a fresh implementer.
- **After ANY master merge, run the full ArchTests AND the related suites.** Rebuild ArchTests first; a stale DLL gives false failures.
- **Never run the unfiltered `Sprk.Bff.Api.Tests`.** Use `scripts/verify-suites.sh <repo> <log-dir>`. Publish delta + CVE: `scripts/publish-delta.ps1 -wt <worktree> -MasterSha <sha> -BranchSha <sha>` (main session only).
- A reviewer must not build in a worktree where verify-suites is running (file locks). Give it read-only scope or its own detached worktree.
- Public repo: notes, POMLs, commits and PR bodies state rules and gaps only. A description of an open access condition on a pushed branch needs a history rewrite (uac-r2 2026-11-06).
- Call `Remove-WorktreeSafe.ps1` through PowerShell after `cd`-ing every shell out of the worktree; then `git worktree prune`.
- CI: the required checks are `Router` + `Client lint (type-aware ratchet)`; Router reports after the Tier 2 shards (~25 min).

## Cross-project obligations

- **UAC-r2:** reviews and runs every live role or app-setting change and every dev deploy, with the owner's go in its session.
- **ebilling-r1:** 164a makes sprk_billingevent create-only refused (not full writer-only; D-96 updates must keep working). It edits RecordTableRules, ChildRecordEndpoints (CreateTables) and the ribbon JS. Whoever merges second of 164a and 049b resolves ChildRecordEndpoints.
- **ontology-r1:** 174 touches Signals files only (no overlap). 033 needs a design-note review first; 052 is hard-blocked on 033.
- **R3:** deploys only batched candidates from UAC-r2.

## Next tasks

049 → merge (#2599), then 046c, then 021's deploy. Then 019, 033 (before 052), 040, 050.
