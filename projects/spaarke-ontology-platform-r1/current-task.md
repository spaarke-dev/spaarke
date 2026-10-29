# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-10-28 ~22:30Z (main session, context-handoff). State only: history is in git log, task `<completion>` blocks and
> `notes/handoff-history/2026-10.md`. Standing rules: [`CLAUDE.md`](CLAUDE.md) §3 and §6. Decisions: [`notes/decisions.md`](notes/decisions.md)
> (D-1..D-198). Deferred work: one self-contained GitHub issue per item, label `ontology-deferred`, plus a DI-nn row in both tables of
> `notes/defer-issues.md` (next free: **DI-135**). Sub-agents never edit this file.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | **UAT round 2** (`notes/uat-v4-log.md` "Round 2 feedback"). Done: ✅165, ✅167 (#2403), ✅169 (#2402), ✅170 (#2404), ✅167c (#2422, D-198), plus the D-193 follow-up #2414 (owning BU read as the caller). 167/169/170 are on **master `a8314c2e0e`** (#2409); the Console is deployed. #2414 and #2422 are on docs only. **Open: 168** (#2418), **171** (blocked on R4b-2c). |
| **Status** | **#2418 (task 168, D-194 Assign Task email + bell) is BLOCKED: not merged.** uac-r2 found F-1..F-4. The Opus pass found F1 (record name/link to any assignee), F1b (`IsInternalOnly` not set, so the row is partner-visible), F3 (no DI wiring test), K2 (no assigner identity in the fallback; a 4xx job retries to dead-letter). uac-r2 agreed the whole fix plan; they re-check only the fix diff. |
| **Next Action** | **1)** Answer R3's two OK requests (see "R3 requests"). **2)** When the owner answers Q1/Q2 below, record **D-199**. Launch the 168 fix round in `C:\wts-168` (branch `task/168-assign-task-notify`, head `8064311c54`):<br>• a gate copied from master `GrantAccessNotifier.cs:222-260`: one eligible internal user per contact; `SecureShareNoAccessGuard.CheckRecordAndSecureParentsAsync(core,user,AsFlagged)` = NotSecure/NotWalled; Read on the task; fail closed. It runs in both `NotifyAsync` and `SendFromSystemMailboxAsync`.<br>• then `InquiryRecordFacts.ResolveAsync` for the name and link lines (narrow only).<br>• the fallback only after the gate.<br>• the job re-reads `sprk_assignedto` + `statecode` + the gate at send time.<br>• an inactive contact gets no email.<br>• `IsInternalOnly=true`, with a read-back test and a mutation.<br>• the `DecisionCommitFollowUps` DI test.<br>• "Assigned by" + `sprk_sentby`.<br>• Poisoned on 4xx.<br>• log the exception type and Graph code, not the exception object.<br>Must-not-fire tests: walled; secure non-member; Restricted + flagged-external; unverifiable.<br>Then a scoped re-check, send the SHA to uac-r2, CI, merge. **3)** Then docs→master (#2414, #2422, 168): before notices, master merged in, full ArchTests. |

## Open owner questions (task 168, asked 2026-10-28)

1. **The assignee is not an eligible internal user** (external, several users, none). **A (rec.):** send nothing unless the contact holds an active onboarded grant on the core record. B: a notice naming no record. C: never.
2. **Fallback reach.** **A (rec.):** system-mailbox fallback only for recipients who pass the gate; otherwise `assignee_email_not_sent`. B: everyone.

## R3 requests (2026-10-28, not yet answered)

R3 asked for the ontology OK. Neither request runs without the owner's direct go.
- **(1) SpaarkeMaster 1.2.4.0 publish** from master `136568cf70`. Customer stamps get it on their next provisioning run. Dry run 40803358901. Our commits in range: `3c6b316899` (167 BUD-/currency), `accfc2d2f7` (policy columns; Basic/Console User lose Read on policy tables), `4e9e295a30` (159), `7d6f7044bd` (102b), `423fac833d` `5098b423bb` `0140c1cbc6` `cf6e09a393` (049/DI-107/101 roles). Before answering, verify those are release-ready for customers.
- **(2) Dev BFF deploy of master `136568cf70`** (#2409 + word-add-in #2417/#2419 + field-mapping #2392). Rollback point: snapshot `e56a3a51d4`. After the deploy: the decide-lane smoke, the task-170 App Insights check (≤2 WhoAmI per commit, record the wall time), and the 168-independent budget check (`BUD-` number on a new budget; currency prefilled).

## Lane facts to carry

- **D-193..D-198** are in `notes/decisions.md`. `BUD-{SEQNUM:6}` is live on spaarkedev1 (SpaarkeMaster, entity-only publish).
- **The budget currency default** (`DecisionParameterSources`):
  - the owning BU is read as the caller;
  - a 403/404/empty/no caller falls back to the base currency; a fault throws (503);
  - for a secure matter, the currency comes from `ExternalAccess:CustomerBusinessUnitId` (uac-r2 resolver + predicate);
  - currencies, BUs and the org are read app-only (configuration).
  - On dev only root and Spaarke BU1 have a currency (USD).
- **170:** the WhoAmI memo is in `HttpContext.Items`, keyed SHA-256(env URL + "\n" + token). The entity-set cache has a TTL of ≤15 min. ADR-009 path C (its "Allowed L1 Exceptions" table). uac-r2 declined P2/P3; P4 is OK if bounded.
- **UAC task 193** changes `SingleEnabledUserAsync` to `ContactUserRepresentation`. If 193 merges first, 168 switches to it; uac-r2 will tell us.
- **Don't push to docs while a docs→master PR is open:** it moves the PR head and restarts CI (#2409 lost a green run).
- **160 rules:** link by recipient (D-187). The footer label only when the matter is Standard and every recipient is an enabled internal user (#2314).
- **D-185 is enforced by** `PolicyReadRoleGuardTests`. The `RemovePrivilegeRole` payload uses `privilegeid` inside `Privilege`.

## Blocked follow-ups (ours)

- **171** (D-197, seeded policies owned by the customer-BU team): waits for uac-r2 R4b-2c on master. `OrdinaryMovesWritesEnabled` stays OFF until R4b-2c F2/F3 are re-checked, uac-r2's live LIKE proof is in, and the signal/DR/budgetrevision lines are sent.
- `OntologyAdminClassificationService.cs:658/685/705`: waits for `sprk_publishstate`. Five stale `DecisionRecordWriter.cs` comments wait for ebilling 074. Close #2212 when ebilling `f0884a175f` reaches master.
- Owner re-checks still open: project create as a Core User (#2394 fixed it), check 6 (the composer), then close 114, 158–163, 166.

## Dev state (spaarkedev1 / spaarke-bff-dev)

- **BFF:** `e56a3a51d4` (R3, 2026-10-28). Master `a8314c2e0e`+ is not deployed yet.
- **Console:** `a8314c2e0e` (run 40768831264, 21:14Z): the 169 lanes and the 167 wizard fields.
- **POL-MATTER-NOBUDGET:** current v3 `…6313`.
- **zz smoke row:** `57e37569-…` deleted, owner OK, confirmed by query.
- **Worktrees:** `C:\wts-168` (168 fix round). Remove only with `Remove-WorktreeSafe.ps1`.
- **Stash:** two old `lint-staged automatic backup` entries (11 and 21 h old) are not ours. Leave them.

## Peers (current session names)

ebilling `spaarke-wt-ebilling-r1-5b` · R3 `spaarke-wt-spa-external-access-platform-08` · uac-r2 `spaarke-wt-unified-access-control-r2-ac` ·
word-add-in `spaarke-wt-spaarkeai-word-add-in-r1-c4` · field-mapping `spaarke-wt-field-mapping-server-write-pa-be`. Send before and after notices to
word-add-in, R3 and uac-r2 for any dev BFF, Console or landing change. word-add-in needs no after-notice for a Console-only change.
