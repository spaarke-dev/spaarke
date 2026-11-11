# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> - **Standing rules:** project `CLAUDE.md`. **Decisions:** `notes/decisions.md`. **Defer list:** `notes/defer-issues.md`.
> - **History:** git log of this file.

> **Last Updated**: 2026-11-08 (checkpoint #106, desktop session `uac-r2`, peer-review stretch). Private details: `~/.claude/projects/c--code-files-spaarke-wt-unified-access-control-r2/private-handoff-2026-11-08.md` (outside the repo; read it first, never copy it into repo text).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Final push, round-90 rules. This session (`uac-r2`, Claude desktop) is authoritative; the VS Code `-20` session is retired. Merge needs Router + "Client lint (type-aware ratchet)": `git merge-tree`, then `--squash --match-head-commit <sha>`, no `--delete-branch`. Live actions, deploys, role and settings changes need the owner's go IN THIS SESSION; a peer's go is never the owner's. |
| **Running** | Nothing of mine. Worktree clean and pushed after this checkpoint. Peer work in flight: ebilling 130-0 fix round 2 + 130-0b steps 1-3 + 130b fix round; fm 022 fix round 2, 032b-1b, 045b review. |
| **Dev state** | BFF = **6fbb3a7642** since 2026-11-08 04:00:55Z, gate PASS (`notes/deploy-gate-2026-11-08-6fbb3a7642.md`); two boot-window 500s unexplained: check the next deploy's boot window. 226 settings, reconcile false. Rollback `deploy/rollback/wwwroot-20261108T035611Z.zip`. Detached worktree `C:/wtd6fbb`: remove with `Remove-WorktreeSafe.ps1`. Master since: #2629 (57748ebf6d), not on dev. Next deploy = master + MCP spike, owner go per SHA. |
| **Next action** | Wait for peer diffs (none in hand). Then my own share-side work for fm task 054 (below). Relay the owner's answer on "participants keep shares after filing" to fm when it arrives. |

## Owner answers this stretch (2026-11-08)
- fm 060 Direct 1:1 messages: option (a), a dedicated owner team outside the BU tree, with participants shared in. Record-anchored messages are unchanged. Unfiled inbound email: not ruled (treated as unchanged).
- #2270 + #2278: owner-team source = (a), a customer-BU column on `sprk_dataverseenvironment`. Next: design the column + owner rule, then the backfill (#2278).
- ebilling 130 firmless partner: keep the firm match; no firm → no invoices; a fault fails closed.
- fm U7 (invoice Assigned To): (i), targeting without access (fm builds 032b-1b).

## Open owner question
- **Direct message filed to a record: do participants keep their shares?** Recommended: keep, except on secure/Restricted targets (`ClassifyEligibility`/`IsBarredOnRestricted` decide). Asked in this session; not yet answered. fm designs it as one switch.
- Also outstanding (unchanged): #2635 (BFF Decision Record read grant, recommended); report card `sprk_assigned*` registry (FR-24); G-8 for customer envs; #2590; SyncMode -Apply review; the field-security bundle (via ebilling); older list in git history of this file (checkpoint #105).
- To report to the owner: the 130-0 F1-2 trade-off. A matter document later linked to another firm's invoice disappears for the first firm. It's a known limit, ruled under round 75.

## Reviews state
- **ebilling 085:** APPROVED (rounds 1+2) and merged into ebilling work `052307dc0b`; the K note (native admin-role writes bypass the BFF-only Procedure check) at `8bd27cab66`. Done.
- **ebilling 130-0 / 130-0b / 130a / 130b:** rulings given (detail private): invoice-linked children follow their invoice on both planes (reads and writes); roots are decided by their grant with the invoice link redacted; the shared `InvoiceLinks` helper; the work assignment joins invoice rights (D-28) incl. the `ExternalAccessModule` ScopeDimension (+ RootLinks/LiveParents); F4/F4b workforce ceiling sets in `WorkforceRoleCeilingGate.cs` (my file, ebilling edits it; one shared constant across the two branches). **Next:** review the 130-0 + 130-0b diff, and the 130b diff incl. the gate hunks together.
- **fm 032b-1:** APPROVED on access. **032b-1b** (74e74104db): APPROVED on access, with condition C1 (rule: content about a record selected only through a targeting column is read as its recipient; plus a dev playbook census) before the master PR.
- **ebilling DEF-059 (#2635):** option C (a dedicated Decision Record Reader role for the BFF identity), approved by the owner in ebilling's session. No objection from me; I review the step-1 diff before any import.
- **fm 022:** the secure-source mapping gate is being built (detail private); the leftover census comes to me privately.
- **fm 054 (Direct owner team):** POML approved with three edits (054c precondition = 054b -Verify exit 0; AC2 recovery via 054c -Verify/-Apply; failed grant never rolls the owner back). fm commits it, then code; the diff comes to me. 054a waits for 045b + my share side.
- **fm next master PR bundle** (076c, 060, 045a, 021, 045c, 032b-1, 045b, 022): the combined diff before they open it.
- mcp-bff-api: Phase 0 waits on the owner's connector registration; every deploy = master + spike until they say Phase 0 is done.

## My own work queue
1. **Share side for fm 054:** extend the Decision Record share mirror to team-owned messages (`DirectThreadAccessService.cs:219-220`); a Direct-BU mode in `Set-CustomerOwnerTeams.ps1` (not loosening its customer-BU refusal); failed-share re-grant; the keep-shares switch. Must land before or with 054a's deploy.
2. #2270/#2278 design (customer-BU column on `sprk_dataverseenvironment`).
3. Backlog: #2627, #2621, #2585, #2576, #2524, gate null-column hardening, SharePoint census, #2543, K-P1 for task 191, communications by-regarding timeout → 500 (not filed); parked: 193 (C:/wt193), 197, #2427, 196a/196b; TASK-INDEX reconcile at 090.

## Private items (never in issues/PRs/commits)
See the private handoff. The line: identifiers are fine in public; "X can still write/read Y today" for an open gap is not.

## Peers (cross-session addresses)
ebilling `-c7` · field-mapping `-69` · ontology **`ontology-r1`** (desktop, since 2026-11-09; `-38` is retiring. Open with them: held PRs #2580/#2579 (D-207) merge once ebilling 112 is on dev, then they send me the SHAs. **Deploy gate:** no BFF build carrying ebilling 165b-4 reaches dev until its AuthorableColumns run is green after 166's field security is applied; ontology confirms it) · provisioning **`provisioning-r1`** · word-add-in **`word-add-in-r1`** (open with them: read-back of the owner's three checks on 22487c6537 + one To Do save after the next master build) · mcp-bff-api **`mcp-r1`** (desktop, since 2026-11-09; the connector Entra app is created, the Dev Portal OAuth registration is with the owner, and it tells me before the Phase 0 (003) test window) · website `spaarke-website-cb` · SPA R4 **`spa-r4`**: my move notice EXPIRED undelivered (different permission mode). It doesn't know the `uac-r2` address; tell the owner or resend if asked. Send dev BFF before/after notices and gate results there.
