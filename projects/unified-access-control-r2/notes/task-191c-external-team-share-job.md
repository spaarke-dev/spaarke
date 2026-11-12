# Task 191c: External-team share job

> Branch `task/uac-191c`. Design: `notes/2179-flagged-external-dataverse-design.md` §3.4, §3.5, §3.7, §3.8, §3.14, §4.1 (Q3 = a, Q5 = a, Q7).
> Ships with every write setting OFF. Turning anything on is task 191i, and needs the owner's go in the uac-r2 session.

## What it does

- **Engine.** `ExternalTeamShareSynchronizer` (Services/Access) works over each lineage row: the three roots and every `SecureChildLineage.Children` table.
  - **Team share.** The DEFAULT team of the External Collaborators business unit gets exactly:
    - Read + AppendTo on the 7 filing tables;
    - Read on every other lineage table;
    - nothing on a "never" table: hard-deny, communication content (derived from `SecureChildLineage.NeverExternalTables`), or the invoice family.
    - It holds this share only while two things are true:
      - `ExternalVisibility` says Visible;
      - the row is in no secure family. That means it is not Secure-team-owned, is not a root whose stored flag is anything but `false`, and does not reach a root whose effective flags say Secure or cannot be read. The team share is revoked otherwise.
  - **Flagged users' per-user shares.** These are revoked on a child the predicate does not show. Exceptions:
    - roots are I-14's;
    - Secure-team-owned rows belong to the secure mirror;
    - Direct-team-owned rows belong to task 198 and are reported only.
  - **Re-own.** Rows a flagged user owns are re-owned to their I-6 answer. The re-own:
    - is conditional on the row version and read back;
    - never goes to the External team or the Secure team;
    - never touches an unfiled communication or thread (146 E1).
  - **Who is flagged.** Only a stored `sprk_isexternal = true` counts. It is never inferred.
- **Two-minute job.** The External-team pass of `SecureChildShareReconciliationJob` (ADR-036: no new job) runs, in order:
  1. carried rows;
  2. walk-again starts;
  3. recent changes and their families (the reverse walk);
  4. sweep pages within a time budget. A sweep runs at instance start, then hourly.
- **L1 hooks.** These run with a 100-row inline cap; the two-minute pass finishes larger families:
  - the Restricted remover;
  - `SecureChildReconciler` after a re-file;
  - `SecureChildReconciler` after an Apply-mode secure transition (not the sweep).
- **Secure mirror.** `FilterFlaggedAsync` adds nothing for a flagged or unverifiable user on a child the predicate hides. It revokes a flagged user's held share there when writes are on, and asks the predicate in batches.
- **Resolver.** `RecordOwnershipResolver`: an unfiled create by a flagged acting user is owned by the customer BU's default team (`ExternalAccess:CustomerBusinessUnitId`). With no customer BU configured it is refused (`ActingUserUnresolved`).

## Settings (names only; all default off or unset)

| Setting | Default | Effect |
|---|---|---|
| `ExternalAccess:ExternalTeamShare:WritesEnabled` | false | Team-share grants and revokes, and flagged-user per-user revokes (engine and mirror). Off: report-only, with planned grants and revokes logged per table. |
| `ExternalAccess:ExternalTeamShare:ReownWritesEnabled` | false | Owner writes for rows owned by flagged users. Off: re-owns are reported. |
| `ExternalAccess:ExternalTeamShare:BusinessUnitName` | `External Collaborators` | The unit whose default team is shared with. |
| `ExternalAccess:CustomerBusinessUnitId` | unset | Owner unit for unfiled creates and re-owns by flagged users. **Not gated:** the resolver change is live on deploy. |

## Deviations from the POML (recorded, not silent)

1. **Writers not hooked.** 189's two writers (`ChildAccessPermissionReconciler`, `CoreAncestorResolver`), `LowerWhenSafeAsync` and BFF lineage creates get no L1 hook. The two-minute pass picks up their rows through `modifiedon`, so the window is up to about 2 minutes plus the run time, as the owner accepted for Q4.
2. **Where the reverse walk lives.** It is in the share job's External pass, not in `SecureChildReconciliationJob`. Same ADR-036 job family; one place lists and walks.
3. **ArchTest name.** The POML's `ExternalShareGrantAllowListTests.cs` is `tests/Spaarke.ArchTests/ExternalTeamShareGuardTests.cs`. It holds the grant/modify caller allow-list with controls, plus the role-file parity checks.
4. **Direct-message writers.** `DirectMessageParticipantShares` and `DirectThreadAccessService` are NOT filtered by the predicate. The task brief forbids changing 198's Direct-message code. They are listed as KNOWN GAP in the allow-list, and their rows are reported by the engine (`directOwnedFlaggedShares`), never fought. This is an owner question.
5. **Undecided keeps the constraint.** A transient Undecided still revokes the team share and flagged users' shares, as the task constraint requires; the review proposed carrying instead. The bisecting decision limits the damage to the faulting row itself.

## §10 BFF hygiene

- **Placement.** Inside the BFF, in Services/Access next to `SecureChildShareSynchronizer`:
  - it uses the same POA seam (`IDataverseRecordShareService`) and the same two-minute job;
  - no new job, endpoint, package or `BackgroundService`;
  - DI adds one factory registration of the engine in `ExternalAccessModule`.
- **Publish size.** Compared against a fresh master build (08abdfe151; master has changed no BFF source since), zipped: the branch is 41,915,612 bytes against master 41,867,152, a delta of +48,460 bytes (about +47 KB), about 41.9 MB in total. That is under +5 MB and under 55 MB.
- **CVE.** `dotnet list package --vulnerable --include-transitive` finds no vulnerable packages.

## §11 component justification

| New surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `ExternalTeamShareSynchronizer` | `SecureChildShareSynchronizer` (secure children only), `RestrictedExternalShareRemover` (per-user root shares) | It reuses both of their seams and the lineage registry. Folding it into the secure synchronizer would mix a whole-environment principal into a secure-only mirror with different scope and fail-closed rules. | Once flagged users move into the External BU, they see only their per-user roots; and without the revoke side, hidden or internal-only rows stay shared. |
| External pass partial of `SecureChildShareReconciliationJob` | The job's secure and Direct passes | It extends the existing job (ADR-036), with its own watermark, carried rows and sweep. | Without it, nothing sweeps or carries the team share. |
| Engine DI registration | — | One factory registration; consumers take it as optional. | The job and hooks could not reach the engine. |
| `ExternalVisibilityResult.RootsReached` / `EffectivelySecureRoots` / `Merge` | 191b's result | Extends the existing type. | Without it, inherited Secure is missed (adversarial F1). |
| Resolver flagged-acting-user rule | `UnfiledOwnership.ActingUserTeam` | Extends the existing rule. | A flagged user's unfiled create would be owned by the External BU's team, which must never own a row. |

## Adversarial reviews (Opus)

### Pass 1: all fixed or recorded as limits

| Finding | Disposition |
|---|---|
| Only the stored root flag was read; inherited Secure was missed. | **Fixed.** Uses `EffectivelySecureRoots`. |
| One predicate fault revoked across the whole batch. | **Fixed.** Bisecting decision, 16-call cap, systemic stop. |
| Watermark stalled when 500 or more rows share one `modifiedon`. | **Fixed.** The listing is a (modifiedon, id) key set. |
| A truncated walk carried only its start rows; the sweep was slow. | **Fixed.** Walk-again starts; sweep time budget. |
| Permanently Undecided rows failed the job. | **Fixed.** `Failures` counts fault-carried rows only; carried rows drop to the sweep after 5 runs. |
| Inline latency. | **Fixed.** 100-row inline cap. |
| L1 hooks untested. | **Fixed.** Tests added. |
| Real-predicate root data untested. | **Fixed.** Tests added. |
| The test double ignored `ColumnSet`. | **Fixed.** |
| A root-facts read fault did not carry the row. | **Fixed.** It is carried. |
| The mirror decided per child. | **Fixed.** Batched. |
| Refile hook cancellation. | **Fixed.** The hook never throws. |
| A missing engine was logged at Information. | **Fixed.** Warning, once. |
| Re-own hardening. | **Fixed.** Secure answer refused; conditional write; census entries. |

### Pass 2: two fix-now findings, both fixed

| Finding | Disposition |
|---|---|
| A truncated walk was re-walked identically, and one family starved the others. | **Fixed.** The walk collects up to 50,000 ids and syncs in 5,000-row passes; walk-again runs one start at a time. |
| Incomplete counted as success, which hid write failures. | **Fixed.** The `Failures` count gates `Ok`. |
| Minor: stale log message and comment. | **Fixed.** |
| Systemic-fault load (K2). | **Fixed.** Splitting stops after the first split. |
| Mirror batch scope (K3). | **Fixed.** Lazy batches of up to 200. |

## Known limits

- **K1.** The Restricted remover's team-share hook runs only when that call removed per-user shares. Otherwise the two-minute pass handles it, because the root's `modifiedon` changed.
- **K2.** Held listing positions have no overlap, so a late NoLock commit at or before the held instant can be skipped. The hourly sweep covers it. Check live that `modifiedon` comes back at stored precision.
- **K3.** A parentless email artifact, attachment artifact, review log, attachment or participant owned by a flagged user would be re-owned. No real path to such a row was found; these rows are service-created.
- **K4.** Rows owned by the External team itself only raise an alarm (`BU OWNS ROWS`). Placement is another task.
- **K5.** Idle cost while writes are off: the pass still lists, decides and reports every two minutes.
- **K6.** NoLock reads can race a concurrent writer. The next run converges, and the re-own write is version-conditional.
- **K7.** The guard checks who grants, not that each grantor asks the predicate. Reviewers check the reason given for each entry.
- **K8.** The resolver change is not setting-gated. `ExternalAccess:CustomerBusinessUnitId` must be set on every slot before flagged users create unfiled rows, or those creates are refused.
- **K9.** L1 hooks discard `WalkAgain`; the two-minute pass relists the changed row.

## Owner-go list for 191i (run in order, each with its own go)

1. **Deploy** the BFF carrying this task, with all settings off. Watch the `[EXTERNAL-TEAM-SHARE] heartbeat` lines: `status`, `examined`, `undecided`, `teamGrants`, `teamRevokes`, `flaggedUserRevokes`, `reowns`, `failures`, `externalTeamOwned`, `directOwnedFlaggedShares`.
2. **Read the report-only plan** for one full sweep pass. The per-table lines say `report-only` and carry `grantRows` / `revokeRows`. STOP and escalate if:
   - any internal user's share is planned for revoke;
   - any Restricted, secure or never table is planned for a grant;
   - `externalTeamOwned` > 0.
3. **Set `ExternalAccess:CustomerBusinessUnitId`** on every slot, if it is not already set.
4. **Set `ExternalAccess:ExternalTeamShare:WritesEnabled = true`.** Watch:
   - `failures` stays 0;
   - `written` matches the planned counts;
   - `carried` falls;
   - `[SECURE-CHILD-SHARES]` stays complete.
5. **Set `ExternalAccess:ExternalTeamShare:ReownWritesEnabled = true`.** Watch:
   - `reowned` matches `reowns`;
   - no `BU OWNS ROWS` errors.
6. **Rollback:** set the setting back to false. Grants already written stay until the predicate hides the row. Per-user revokes are not restored by this engine.

## Owner questions

1. The Direct-message writers (`DirectMessageParticipantShares`, `DirectThreadAccessService`) grant participants without the predicate. Should 198's owner add the flagged-principal filter?
2. `GateDecisionRecordSharing`'s guest test also reads `domainname`, which is inference beyond the stored flag. It is pre-existing; keep it or remove it?
3. Unfiled communications and threads owned by flagged users keep their creator (146 E1). Is that acceptable once flagged users sit in the External BU?
4. Confirm that a refused unfiled create (no customer BU configured) is the wanted fail-closed behaviour.
