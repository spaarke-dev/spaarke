# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-10-29 (main session, context-handoff at 85%). State only: history is in git log, task `<completion>` blocks and
> `notes/handoff-history/2026-10.md`. Standing rules: [`CLAUDE.md`](CLAUDE.md) §3 and §6. Decisions: [`notes/decisions.md`](notes/decisions.md)
> (D-1..D-199). Deferred work: one self-contained GitHub issue per item, label `ontology-deferred`, plus a DI-nn row in both tables of
> `notes/defer-issues.md` (next free: **DI-136**). Sub-agents never edit this file.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | **168** (#2418): the fix round under D-199 is RUNNING (Opus implementer in `C:\wts-168`; review findings saved in the session scratchpad as `r168-0.txt`). When it reports: re-check the fix diff, send it to uac-r2, wait for CI, merge. 171 and 172 are DONE (171: #2433 merged `3452d3017e` after fix round 1; 172: #2434 merged `fab8c91531`). |
| **Status** | No lane is running. The worktrees `C:\wts-171` and `C:\wts-172` are removed. uac-r2 was sent the 171 fix-diff SHA `e025152e3b` after the merge; if they raise anything, handle it as a follow-up before docs→master. DI-124 (#2300) has the `-CustomerBusinessUnit` / `-WhatIf` comment. |
| **Next Action** | 1) Re-check the 168 fix round (D-199) in `C:\wts-168` (plan below). 2) After the owner's next real decision commit: the task-170 App Insights check and the `BUD-` number (Lane facts). 4) Read any uac-r2 reply on the 171 fix diff. |

## Task 168 decisions: D-199 (owner, 2026-10-29)

Q1 = A: an eligible internal user, or an onboarded external contact with an active grant on the matter (portal link); nobody else, fail closed. The owner also requires that the record shows its related tasks to whoever has access, including in the portal. Checked: the portal lists every event on the matter to a contact with Read, but shows no Task type, no assignee and has no task deep link. That is DI-135 (#2445, R3); the 168 email links to the matter. Q2 = yes: the system-mailbox fallback, to the same recipients only.

## 168 fix plan (after Q1/Q2; uac-r2 agreed the plan and re-checks only the fix diff)

Branch `task/168-assign-task-notify`, head `8064311c54`. First merge docs (it now contains master + 194b).
- A gate copied from master `GrantAccessNotifier.cs:222-260`: one eligible internal user per contact; `SecureShareNoAccessGuard.CheckRecordAndSecureParentsAsync(core,user,AsFlagged)` = NotSecure/NotWalled; Read on the task; fail closed. It runs in both `NotifyAsync` and `SendFromSystemMailboxAsync`.
- `InquiryRecordFacts.ResolveAsync` for the name and link lines; the fallback only after the gate.
- The job re-reads `sprk_assignedto` + `statecode` + the gate at send time. An inactive contact gets no email.
- `IsInternalOnly=true` (read-back test + mutation); the `DecisionCommitFollowUps` DI test; "Assigned by" + `sprk_sentby`; Poisoned on 4xx; log the exception type and Graph code, not the exception object.
- Must-not-fire: walled; secure non-member; Restricted + flagged-external; unverifiable. Watch UAC 193 `ContactUserRepresentation` (K-1).

## Lane facts to carry

- **Docs now contains master through 194b** (`2cd0f62e2c`, merge `4470c95b86`). 194b moved `ResolveSecureBusinessUnitAsync` to `SecureRecordOwnerTeam`; our call was repointed in `f1133d951b` (uac-r2 approved). ArchTests 1665/1665 on that state.
- **Dev BFF** `136568cf70` (R3, live 02:23:53Z). Smoke PASS (deploy-log `83e3e409cb`). **Pending:** the task-170 commit-path check (before: 28 WhoAmI, 22.4 s per commit) and the `BUD-` number need the owner's next real decision commit. Then query App Insights `spe-insights-dev-67e2xz` (rg `spe-infrastructure-westus2`) for `POST /api/v1/signals/{id}/decision` after 02:23:53Z.
- **SpaarkeMaster 1.2.4.0 stamp check:** customer-provisioning built `Test-RolePrivilegeExpectations.ps1` + `spaarkemaster-1.2.4.0.json` (`b6e94f4499`). Stamps run 1.2.3.1, so the upgrade REMOVES Basic User `prvReadsprk_Policy` (D-189 note `53c01465dc`). They will send ACME's result after its upgrade.
- **uac-r2 dev move:** root-homed humans move to BU1 (owner approval pending, no writes yet; they give notice first). The seed policies likely stay root-owned (`DoNotMoveAllRecords=true`). After the move, BU1-homed Ontology Admins (Deep) can't read root-owned seed policies; uac-r2 is preparing an owner question and will copy us. **Hold the memo-LIKE proof** until they confirm placement.
- **CI:** `ontology-script-tests` is pinned to Pester 5.7.1 (#2428). DI-134 (#2430) asks the CI owner to pin `verdict-script-tests`.
- **Don't push to docs while a docs→master PR is open** (it moves the PR head and restarts CI).
- `OrdinaryMovesWritesEnabled` stays OFF.
- **Commits in a fresh worktree:** lint-staged needs prettier: `export PATH="/c/code_files/spaarke-wt-spaarke-ontology-platform-r1/node_modules/.bin:$PATH"` (no junctions). Integration tests under `tests/integration/auth/**` compile into `tests/unit/Sprk.Bff.Api.Tests`; filter there.

## Next docs→master

The last one was PR #2439, merged as master `f07ab2ef1f` (2026-10-29): 167b, 167c, 171, 172 and the 194b repoint; all checks passed. Docs was fast-forwarded to it. The next one carries 168 when it is ready. Before it: notices to word-add-in, R3 and uac-r2; merge master in; full ArchTests. The word-add-in session refuses held messages while the two sessions run in different permission modes.

## Blocked follow-ups (ours)

- `OntologyAdminClassificationService.cs:658/685/705` waits for `sprk_publishstate`. Five stale `DecisionRecordWriter.cs` comments wait for ebilling 074. Close #2212 when ebilling `f0884a175f` reaches master.
- Owner re-checks still open: project create as a Core User (#2394), check 6 (the composer), then close 114, 158–163 and 166.

## Dev state (spaarkedev1 / spaarke-bff-dev)

- **BFF:** `136568cf70` (R3, 2026-10-29). **Console:** `a8314c2e0e`. **SpaarkeMaster:** 1.2.4.0. **POL-MATTER-NOBUDGET:** v3 `…6313`.
- **Worktrees:** `C:\wts-168`. Remove only with `Remove-WorktreeSafe.ps1`, one path per call.
- **Stash:** two old `lint-staged automatic backup` entries (20 and 30 h old) are not ours. Leave them.

## Peers (current session names)

ebilling `spaarke-wt-ebilling-r1-5b` · R3 `spaarke-wt-spa-external-access-platform-08` · uac-r2 `spaarke-wt-unified-access-control-r2-ac` ·
word-add-in `spaarke-wt-spaarkeai-word-add-in-r1-c4` · field-mapping `spaarke-wt-field-mapping-server-write-pa-be` ·
customer-provisioning `spaarke-wt-customer-provisioning-orchest-2f`. Send before and after notices to word-add-in, R3 and uac-r2 for any dev BFF,
Console or landing change. word-add-in needs no after-notice for a Console-only change.
