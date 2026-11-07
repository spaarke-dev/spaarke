# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> - **Standing rules:** project `CLAUDE.md`. **Decisions:** `notes/decisions.md`. **Defer list:** `notes/defer-issues.md`.
> - **History:** git log of this file.

> **Last Updated**: 2026-11-07 ~15:00Z (checkpoint #101: batch deployed, cc11294300 on dev, gate PASS; membership back-fill 0 → 1699; spike held for CI).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Final push, round-90 rules. Merge needs Router + "Client lint (type-aware ratchet)": `git merge-tree`, then `--squash --match-head-commit <sha>`, no `--delete-branch`. Live actions, deploys, role and settings changes need the owner's go IN THIS SESSION; a peer's go is never the owner's. |
| **Dev state** | BFF = master **cc11294300** (since 2026-11-07 14:22:09Z). `sprk_recordfamily` NEW (in spaarke_core); `sprk_fieldmapping_push` v1.3.0; `sprk_spaarkeai` at cc11294300 (CI). G-8 ON. 226 settings. 046c applied 11-07 03:33/03:36Z (watch PASS). Junction 1699 rows. Gate record: `notes/deploy-gate-2026-11-07-cc11294300.md`. Rollback: `deploy/rollback/wwwroot-20261107T142009Z.zip` (20669df6a9). Detached deploy worktree `C:/wtd1129`: remove with `Remove-WorktreeSafe.ps1` when no longer needed. |
| **Next action** | 1. Wait for fm's live UI check times (a) to (d), then read the server side for those windows. 2. mcp-bff-api sends the spike SHA when PR #2615 is green (their Task 167 scanner fix, option a, with controls): check the diff = 16 spike files + ArchTests. 3. Next dev window = master ≥ d53d67f604 (049b) + the spike: put it to the owner. 4. Reviews as they arrive (below). |

## Next dev window (needs the owner's go here)
- Content: master from d53d67f604 (049b via #2619; 049c if merged) + spike HEAD (green). Spike gate: anonymous `/mcp/legal-operations` and `/mcp/spike-area-b` → 401, signed-in → 2xx. Send mcp-bff-api the SHA and read-back.
- ebilling's branch batch (164a + 166 + 095a + ...) comes later as one checklist. Its gates: creator-person -Verify, `sprk_sourceemaildocument` IsSecured read true by me, 166 field security applied with the owner's go, reconcile off.
- Standing pre-checks: 174/175 absent, no 095a code unless gated, -Verify exit 0, `Ebilling__StoredFileReconcileEnabled` false, census, 2h baseline.

## Decisions this session (owner, here)
- 2026-11-06: W3 = named exceptions plus guards; 046c Core User for all five add-on holders (done); root team Console User removed (done); EB-D-200 option D.
- Partners (SPA) SEE reviewer-added lines and reasons while In Review (EB-D-202; reasons via the typed invoice-module projection in task 130).
- #2292: fixed, deployed, back-filled.
- MCP spike on dev: no time limit; rides with each deploy once its CI is green.
- 2026-11-07: "Deploy the batch now" → done as plain master (spike CI red).

## Reviews state
- **fm (`-69`):** 044, 040+075, 049b fix2 approved and on master. 049c: (A) OK with conditions (no value logging; step-2 refusal shape unchanged, with a test; audit noise is a K), (B) OK with cancellation; merges after its fix2 (A2 in-flight-probe test). 033 dropped by the owner (Repair reader → 052). Coming: 045A, 030b, 034, **032 design first** (fill-only mapping in the invoice persister), 052.
- **ebilling (`-c7`):** 164a + follow-up `647ad48a8e` APPROVED (merged to work as eeac33b1c2 + 682dc4a661; the 166 guard got InvoiceLineAddService as Writer and InvoiceFindingsDiff as Reader, to look at in the batch diff). Coming: 165a, then **165c** (the link counts only if it equals `InvoiceIdFor(id)`), 096b, 2b-2, then the 130 projection.
- **ontology (`-38`):** told them the "Signal writer access team" proposal is not mine (they ask fm). Roles alone cover Signal writers. 174/175 held until ebilling 112.
- **mcp-bff-api (`-68`):** spike scanner fix in progress. A10, B-4 and production task 021 (FR-02) still to come.

## Owner items outstanding
- Report card: should its 8 `sprk_assigned*` lookups confer access (registry FR-24)? Today no.
- Dev app users with prod-like names hold System Administrator.
- G-8 for customer environments; #2590 rule; `Set-FieldMappingRuleSyncMode -Apply` review (fm).
- Older: column auditing on writer-only lookups, AP diagnostics, VS Code agent-dir cleanup, word-add-in Office saves, click-test 195, #2343, #2245, #2250, #2260, #2270, #2262, #2396, 191 F1/S4, 190 Apply, #2382, #2505; tasks 140/165/153.

## Private items (never in issues/PRs/commits)
- Open gaps stay in peer messages only: the ebilling hold-column item (165a), the submission-link readers (165c). The line: identifiers are fine in public; "X can still write Y today" for an open gap is not.

## My own backlog
#2621 (filed today), #2585, #2576, #2524, gate null-column hardening, SharePoint census, #2543, K-P1 for task 191, communications by-regarding timeout → 500 (maybe 503; not filed); parked: 193 (C:/wt193), 197, #2427, 196a/196b; TASK-INDEX reconcile at 090.

## Peers
uac-r2 `-20` (this session) · ebilling `-c7` · field-mapping `-69` · ontology `-38` · provisioning `-b8` · word-add-in `-7b` · mcp-bff-api `spaarke-wt-spaarkeai-mcp-bff-api-r1-68` · website `spaarke-website-cb` · SPA R4 `spaarke-wt-spa-external-access-platform-ad`.
