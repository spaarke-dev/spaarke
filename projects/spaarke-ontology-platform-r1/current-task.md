# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-11-04 (main session, context-handoff at 85%). State only: history is in git log, task `<completion>` blocks and
> `notes/handoff-history/2026-10.md`. Standing rules: [`CLAUDE.md`](CLAUDE.md) §3 and §6. Decisions: [`notes/decisions.md`](notes/decisions.md)
> (D-1..D-205). Deferred work: one self-contained GitHub issue per item, label `ontology-deferred`, plus a DI-nn row in both tables of
> `notes/defer-issues.md` (next free: **DI-139**). Sub-agents never edit this file.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | Orchestration + peer reviews (no own task in flight). Billing-guideline test (option C) ontology part DONE (#2545). Hold 090 until owner UAT. |
| **Uncommitted** | None. Last commit `bc0eacd901` (pushed). No docs→master PR open. |
| **Open owner questions** | (1) uac-r2 dev BFF deploy of `909754869f`: owner GO given; ontology OK sent with conditions (notices, read-back, Worklist 200, SuppressionPartitionUndetermined count, 30-min gate). (2) D-204 extension: refuse playbook UpdateRecord **updates** of sprk_signal/sprk_decisionrecord/sprk_budgetrevision; fm runs a census first, owner decides via fm. (3) ebilling `sprk_reviewdetail` column (082c-2): owner go for dev column apply (ebilling asked). |
| **Next Action** | Wait for and review, in this order as they arrive: **(a)** ~~082c-1~~ cleared PASS `88b16f8729` (2026-11-04); **(b)** uac-r2 post-deploy read-back (7 project-core Signals' shares expected empty; 4 SR-core DRs owner/BU/shares with any owner change named; SR-core Signal re-query; held pair rows = catalog error); **(c)** ebilling 082c-2 diff (gap hold, review-detail derivation writers per D-205; Signal side: gap joins shared SubmissionIntegrity Signal, ConditionCleared on clear); **(d)** ebilling 095d-1 code (1a/1b/1c) K-3 review per D-201 conditions + amendment; **(e)** fm playbook census result. Then owner UAT on dev (168/173 runs A/B/C, Tony Howe Assign Task CMRCL-479772 → 50395 no bell, BUD- budget, 170 latency, re-checks 158-163/166, 114, 111 row 4; then 105, then 090). |

**Decisions this session (2026-11-04):** D-203 (082b resolver rulings 1-11), D-204 (generic creates of Signal/DR/BudgetRevision refused, owner-accepted; 042c r4 `4b45ba3cc5` PASS; core-pair lineage census + uac-r2 fix #2568 PASS; new core type needs a code change for secure lineage, project CLAUDE.md amended), D-205 (082c brief + 082c-1 owner review PASS with Q1 change; C8; K4 (c); review-detail derivation). Master `909754869f` touches our SignalSuppressionService/tests: **merge master into docs before the next docs→master.**

## Task 168 decisions: D-199 (owner, 2026-10-29)

Q1 = A (an eligible internal user, or an onboarded external contact with a grant on the matter and a portal link); Q2 = the system-mailbox fallback to the same recipients. Built and merged (see the 168 POML completion block).

## Lane facts to carry

- **Dev BFF deploys (2026-11-03):** R3 asked to deploy `d763dc120e` (live `5796774091`; delta #2541 field-mapping 041 index follows content type, #2546 R3 invite refusal). Ontology replied OK with conditions: Router green, the owner's go naming the SHA, before/after notices because the owner is in UAT, smoke afterwards. **Live since 22:26:31Z** (readback: ProductVersion +d763dc120e, healthz 200, healthz/dataverse 200, anonymous /api/me 401; settings unchanged at 225; rollback target 5796774091). No ontology code in the delta. Passed uac-r2's 30-minute gate (0 5xx, ownership and secure state steady); rollback = R3 worktree snapshot `deploy/rollback/wwwroot-20261103T220358Z` or redeploy 5796774091. **R3 has stopped running deploys. uac-r2 (see "Peers") now runs every dev BFF deploy:** send deploy requests, peer OKs and post-deploy results there; the owner gives the go per SHA in uac-r2's session (procedure `docs/procedures/dev-bff-deploy-coordination.md` on master; record: rollout record section 17, PR #2554).
- **Docs now contains master through 194b** (`2cd0f62e2c`, merge `4470c95b86`). 194b moved `ResolveSecureBusinessUnitAsync` to `SecureRecordOwnerTeam`; our call was repointed in `f1133d951b` (uac-r2 approved). ArchTests 1665/1665 on that state.
- **Dev BFF** `136568cf70` (R3, live 02:23:53Z). Smoke PASS (deploy-log `83e3e409cb`). **Pending:** the task-170 commit-path check (before: 28 WhoAmI, 22.4 s per commit) and the `BUD-` number need the owner's next real decision commit. Then query App Insights `spe-insights-dev-67e2xz` (rg `spe-infrastructure-westus2`) for `POST /api/v1/signals/{id}/decision` after 02:23:53Z.
- **SpaarkeMaster 1.2.4.0 stamp check:** customer-provisioning built `Test-RolePrivilegeExpectations.ps1` + `spaarkemaster-1.2.4.0.json` (`b6e94f4499`). Stamps run 1.2.3.1, so the upgrade REMOVES Basic User `prvReadsprk_Policy` (D-189 note `53c01465dc`). They will send ACME's result after its upgrade.
- **uac-r2 dev move:** root-homed humans move to BU1 (owner approval pending, no writes yet; they give notice first). The seed policies likely stay root-owned (`DoNotMoveAllRecords=true`). After the move, BU1-homed Ontology Admins (Deep) can't read root-owned seed policies; uac-r2 is preparing an owner question and will copy us. **Hold the memo-LIKE proof** until they confirm placement.
- **CI:** `ontology-script-tests` is pinned to Pester 5.7.1 (#2428). DI-134 (#2430) asks the CI owner to pin `verdict-script-tests`.
- **Don't push to docs while a docs→master PR is open** (it moves the PR head and restarts CI).
- **Ordinary moves on dev (uac-r2):** both switches on since 2026-10-30 19:53:19Z (owner approved in uac-r2's session). Ontology re-read: **PASS** (9 WA-core Signals root team → BU1, owner only; `notes/ordinary-moves-baseline-2026-10-30.md`).
- **Commits in a fresh worktree:** lint-staged needs prettier: `export PATH="/c/code_files/spaarke-wt-spaarke-ontology-platform-r1/node_modules/.bin:$PATH"` (no junctions). Integration tests under `tests/integration/auth/**` compile into `tests/unit/Sprk.Bff.Api.Tests`; filter there.

## Next docs→master

The last was PR #2463, merged as master `c8bb55d0cd` (2026-10-29): 168 and 173. Before the next: notices to R3, uac-r2 and word-add-in (name under "Peers"); merge master in; build, the filtered Signals tests and the full ArchTests (1858 now).

## Blocked follow-ups (ours)

- `OntologyAdminClassificationService.cs:658/685/705` waits for `sprk_publishstate`. Five stale `DecisionRecordWriter.cs` comments wait for ebilling 074. Close #2212 when ebilling `f0884a175f` reaches master.
- Owner re-checks still open: project create as a Core User (#2394), check 6 (the composer), then close 114, 158–163 and 166.

## Dev state (spaarkedev1 / spaarke-bff-dev)

- **BFF:** `d763dc120e` (R3's last deploy, live 2026-11-03 22:26:31Z; rollback 5796774091). **Console:** `a8314c2e0e`. **SpaarkeMaster:** 1.2.4.0. **POL-MATTER-NOBUDGET:** v3 `…6313`.
- **Worktrees:** none for lanes. Remove only with `Remove-WorktreeSafe.ps1`, one path per call.
- **Stash:** two old `lint-staged automatic backup` entries (20 and 30 h old) are not ours. Leave them.

## Peers (current session names)

As of 2026-11-04 (after the VS Code restart; this session is `spaarke-wt-spaarke-ontology-platform-r1-38`, duplicate `-c8` closed by the owner):
ebilling `spaarke-wt-ebilling-r1-c7` · R3 `spaarke-wt-spa-external-access-platform-1f` (no longer deploys) · uac-r2 `spaarke-wt-unified-access-control-r2-20` (dev BFF deploy owner) ·
word-add-in `spaarke-wt-spaarkeai-word-add-in-r1-7b` · field-mapping `spaarke-wt-field-mapping-server-write-pa-69` ·
customer-provisioning `spaarke-wt-customer-provisioning-orchest-b8` · project-coordination `spaarke-wt-spaarke-project-coordination-75`. Names change on restart: re-check with ListAgents. Send before and after notices to word-add-in, R3 and uac-r2 for any dev BFF,
Console or landing change. word-add-in needs no after-notice for a Console-only change.
