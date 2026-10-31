# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-10-29 (main session, context-handoff at 85%). State only: history is in git log, task `<completion>` blocks and
> `notes/handoff-history/2026-10.md`. Standing rules: [`CLAUDE.md`](CLAUDE.md) §3 and §6. Decisions: [`notes/decisions.md`](notes/decisions.md)
> (D-1..D-199). Deferred work: one self-contained GitHub issue per item, label `ontology-deferred`, plus a DI-nn row in both tables of
> `notes/defer-issues.md` (next free: **DI-138**). Sub-agents never edit this file.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | No lane is running. Docs = master `04c03ef814` (#2488, notes only). Dev BFF `9f4e3820b2` (uac-r2, 19:22:50Z 2026-10-30; contains everything of ours). Next is the owner's UAT check. |
| **Status** | No owner question is open. ebilling #1911 is merged (`eaa0d44f7f`). Their next PR (104, 101) brings our two asks: the principal-change known limit in 105-override-server.md and the double RecordRefusal log; review it when it arrives. The main checkout `C:\code_files\spaarke` refuses fast-forwards because of another session's uncommitted `.claude/agent-memory/researcher/MEMORY.md`; leave it alone. |
| **Next Action** | 1) Owner UAT: the 168/173 runs A/B/C (`notes/168-173-live-proof.md`; main session runs the read-only checks and fills section 6), one Create budget (`BUD-`), the task-170 latency read, the 🔄 re-checks (158-163, 166), 114 and 111 row 4 (external SPA on dev is master `b083d62183` since 19:12:41Z 2026-10-31; same wizard code as `96f1f6b675`, verified). Then 105 v4 UAT rounds; 090 after UAT (owner hold). 2) ebilling: b1054ada6a finding sent (RefusalScopes flags lines the client default covered, D-39); e205c93419 OK; their next PR into master (097b+097c together, after the sprk_resubmissionof field-security change and 097e) needs our review; ebilling accepted the finding as a defect: fix on `task/ebill-101-refusal-scope-fix` (option a, D-39 precedence key; unreadable type stays flagged); review its commit when it arrives. 115 confirmed done (review = our PASS `12040e2a74`; dev run = deploy-log `6edfdb6d6b`; asked them to confirm SubmissionIntegrity 100000014 is on dev). 3) **#2491 (R3, bug in our 168 gate):** `AssignTaskRecipientGate` reads the contact with `ContactSelect`, which lacks `sprk_externaltenantid`, so every WorkforceHome assignee is Unverified (fails closed; misses D-199). Proposed: R3 fixes both call sites (with #2458) in one PR on a shared one-contact read, if the owner agrees; ontology reviews our call site and re-checks on dev with Tony Howe (WorkforceHome, grant on CMRCL-479772). 4) #2486 (field-mapping 017) ontology files reviewed OK (PR comment 6537746257). |

## Task 168 decisions: D-199 (owner, 2026-10-29)

Q1 = A (an eligible internal user, or an onboarded external contact with a grant on the matter and a portal link); Q2 = the system-mailbox fallback to the same recipients. Built and merged (see the 168 POML completion block).

## Lane facts to carry

- **Docs now contains master through 194b** (`2cd0f62e2c`, merge `4470c95b86`). 194b moved `ResolveSecureBusinessUnitAsync` to `SecureRecordOwnerTeam`; our call was repointed in `f1133d951b` (uac-r2 approved). ArchTests 1665/1665 on that state.
- **Dev BFF** `136568cf70` (R3, live 02:23:53Z). Smoke PASS (deploy-log `83e3e409cb`). **Pending:** the task-170 commit-path check (before: 28 WhoAmI, 22.4 s per commit) and the `BUD-` number need the owner's next real decision commit. Then query App Insights `spe-insights-dev-67e2xz` (rg `spe-infrastructure-westus2`) for `POST /api/v1/signals/{id}/decision` after 02:23:53Z.
- **SpaarkeMaster 1.2.4.0 stamp check:** customer-provisioning built `Test-RolePrivilegeExpectations.ps1` + `spaarkemaster-1.2.4.0.json` (`b6e94f4499`). Stamps run 1.2.3.1, so the upgrade REMOVES Basic User `prvReadsprk_Policy` (D-189 note `53c01465dc`). They will send ACME's result after its upgrade.
- **uac-r2 dev move:** root-homed humans move to BU1 (owner approval pending, no writes yet; they give notice first). The seed policies likely stay root-owned (`DoNotMoveAllRecords=true`). After the move, BU1-homed Ontology Admins (Deep) can't read root-owned seed policies; uac-r2 is preparing an owner question and will copy us. **Hold the memo-LIKE proof** until they confirm placement.
- **CI:** `ontology-script-tests` is pinned to Pester 5.7.1 (#2428). DI-134 (#2430) asks the CI owner to pin `verdict-script-tests`.
- **Don't push to docs while a docs→master PR is open** (it moves the PR head and restarts CI).
- **Ordinary moves on dev (uac-r2):** both switches on since 2026-10-30 19:53:19Z (owner approved in uac-r2's session). Ontology re-read: **PASS** (9 WA-core Signals root team → BU1, owner only; `notes/ordinary-moves-baseline-2026-10-30.md`).
- **Commits in a fresh worktree:** lint-staged needs prettier: `export PATH="/c/code_files/spaarke-wt-spaarke-ontology-platform-r1/node_modules/.bin:$PATH"` (no junctions). Integration tests under `tests/integration/auth/**` compile into `tests/unit/Sprk.Bff.Api.Tests`; filter there.

## Next docs→master

The last was PR #2463, merged as master `c8bb55d0cd` (2026-10-29): 168 and 173. Before the next: notices to R3, uac-r2 and word-add-in (current name `spaarke-wt-spaarkeai-word-add-in-r1-8a`); merge master in; build, the filtered Signals tests and the full ArchTests (1858 now).

## Blocked follow-ups (ours)

- `OntologyAdminClassificationService.cs:658/685/705` waits for `sprk_publishstate`. Five stale `DecisionRecordWriter.cs` comments wait for ebilling 074. Close #2212 when ebilling `f0884a175f` reaches master.
- Owner re-checks still open: project create as a Core User (#2394), check 6 (the composer), then close 114, 158–163 and 166.

## Dev state (spaarkedev1 / spaarke-bff-dev)

- **BFF:** `136568cf70` (R3, 2026-10-29). **Console:** `a8314c2e0e`. **SpaarkeMaster:** 1.2.4.0. **POL-MATTER-NOBUDGET:** v3 `…6313`.
- **Worktrees:** none for lanes. Remove only with `Remove-WorktreeSafe.ps1`, one path per call.
- **Stash:** two old `lint-staged automatic backup` entries (20 and 30 h old) are not ours. Leave them.

## Peers (current session names)

ebilling `spaarke-wt-ebilling-r1-5b` · R3 `spaarke-wt-spa-external-access-platform-08` · uac-r2 `spaarke-wt-unified-access-control-r2-ac` ·
word-add-in `spaarke-wt-spaarkeai-word-add-in-r1-c4` · field-mapping `spaarke-wt-field-mapping-server-write-pa-be` ·
customer-provisioning `spaarke-wt-customer-provisioning-orchest-2f`. Send before and after notices to word-add-in, R3 and uac-r2 for any dev BFF,
Console or landing change. word-add-in needs no after-notice for a Console-only change.
