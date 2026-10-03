# Task 143 — No Access List for INTERNAL users on secure records (#1066)

> Branch `task/uac-r2-143` (from `task/uac-r2-133-b2-r2` + `work/unified-access-control-r2`, base `7128776b7`).
> Server side complete; the form-registered steps are task 154's. Live writes: none (all pending manual gates below).

## 1. Owner answers applied (all escalations answered — none fired as a stop)

| POML trigger | Owner question | Answer (source) | Applied as |
|---|---|---|---|
| (a) | N1 — how is an internal user named | (a) systemuser column + honour linked contact (round 3: accepted as recommended) | `sprk_subjectsystemuser`; the guard also matches every contact that represents the user (141 link `sprk_primarycontact`, and contacts bound to the user's oid) and their organizations |
| (b) | N4 — organization subject binds members' internal users | (a) yes (round 3) | Guard, veto and enforcer all expand organization subjects; the enforce report carries `subjectKind` + `coveredUsers` (blast radius). The explicit "this is the customer's own firm" warning is **067's confirm dialog** (no own-firm marker exists server-side) |
| (c) | N6 — walled creator provisioning | as recommended (round 3b) | Refused BEFORE the owner move (403 `sdap.provision.creator_no_access`), walled colleagues skipped with a per-person entry in `skippedPrincipals`; a resume refuses to share to a walled creator (409 `resume_creator_no_access`) |
| (d) | N2 — team/role/BU access | hide in Teams/SPA (round 3b) — §6.5 path A | Never revoked; reported `NoAccessNotEnforceable` (team-share / team-ownership / role-or-business-unit); the veto removes secure records whole. Recorded in `design.md` §9 |
| (e) | R4 — job posture | enabled, writes on, ≤5 min (round 3 R3/R4) | `NoAccessShareReconciliationJob`, `*/5 * * * *`, enabled, removes |
| (g) | N5 — who may cause a removal | as recommended (round 3b) | A record is enforced only if the entry's `modifiedby` is an enabled person holding Write on it (RetrievePrincipalAccess for that principal, app token); else `author-lacks-write`, nothing removed |
| (h) | N3 — non-secure systemuser veto | (a) remove contact-sourced only (round 3: accepted) | Systemuser plane: secure → remove whole; non-secure → user-subject entry nothing, contact/org entry only the linked contact's contribution. A fault removes the candidate |
| S5 | last person | round 3 S5 | Enforcer keeps the last enabled person's share and reports it; the endpoint answers 409 naming the record |
| O2 | who reads/writes the table | accepted (UX note): access-administrator role | Not this task's to apply (154/064); the enforce route requires Write on the ENTRY, which under O2 is that role |

## 2. Census (step 1)

| Site | Writes | Secure-record site? | Guarded |
|---|---|---|---|
| `InternalShareEndpoints.ShareAsync` (`/share-user`) | Grant/Modify to a systemuser | yes (any root) | ✅ before any share read/write; 403 `subject_no_access`, 500 `no_access_unverifiable` |
| `ProvisionProjectEndpoint` `EnsureCreatorShareAsync` / unverified-move fallback grant | creator / resume person | yes | ✅ creator checked before the first write (forward), resume person checked before the share |
| `ProvisionProjectEndpoint.RestoreCreatorShareAsync` | back to the PRE-CALL mask | compensation only; the creator was already checked | n/a (never widens) |
| `ProvisionProjectEndpoint.ShareToColleaguesAsync` | Collaborate to named colleagues | yes | ✅ walled → skipped + reported |
| `PlaybookSharingService.GrantAccessToTeamAsync` (`:321`) | team share on `sprk_playbook` | **no** — playbooks are not secure roots | not guarded (evidence: entity set `sprk_playbooks`, principal Team) |
| `DirectThreadAccessService` (`:93`, `:195`) | Read on `sprk_communicationthread` / `sprk_communication` | **not yet** — Direct threads carry no regarding anchor; Open/record-anchored messages are children, which become secure children only with C10 part 2 (146/149, not landed) | **HOOK for 146/149**: before `GrantReadAccessToPrincipalsAsync` grants on a message whose thread is anchored to a secure root, call `SecureShareNoAccessGuard.CheckAsync(rootLogicalName, rootId, principal)` and skip refusals. Not an escalation (f): it can consult the guard internally, no API change |
| Task 142 auto-share | — | not landed | **HOOK for 142**: call `CheckAsync` before every Assigned-To POA share; a share this enforcer removed is ledgered `Skipped(no-access)`, so a lifted wall restores it; the "Update Access" command calls `NoAccessShareEnforcer.EnforceForRecordAsync` |
| Task 149 child-share fan-out | — | not landed | **HOOK for 149**: call `CheckAsync` with the ROOT before any child share; extend the enforcer to remove a walled user's child shares (today it removes root shares; a child-record object is reported `not-a-secure-root`) |
| Task 064 add endpoint | — | not landed | **HOOK for 064**: accept `sprk_subjectsystemuser`; after the add, call `EnforceEntryAsync`. ⚠️ N5 reads `modifiedby` — if 064 creates the entry APP-ONLY, `modifiedby` is the app user and nothing is enforced; 064 must create it as the caller (CallerObjectId impersonation) |

Live privilege census (read-only, 2026-10-02): Create/Write on `sprk_noaccessentry` — System Administrator, System
Customizer, Service Writer (Global). Read — those plus Spaarke Core User (Global), Service Reader, Support User (Basic).
0 entries exist in dev.

## 3. What was built (placement + §11 justification)

All in the BFF (CLAUDE.md §10): BFF domain code over BFF-owned tables, BFF identity, low volume; no AI dependency
(ADR-013), no new package, no new `.WithClientSecret`, no plugin (ADR-002), `GrantMembershipAsync` untouched.

| New component | Existing (grep) | Why not extend | Cost of doing nothing |
|---|---|---|---|
| Column `sprk_subjectsystemuser` | only `sprk_subjectcontact` / `sprk_subjectorganization` (`entity-schema.md`); `_sprk_subject` hits only the reader | linked-contact-only fails OPEN for unlinked users (1 of 8 linked) | an internal user cannot be walled directly |
| `SecureShareNoAccessGuard` (scoped) | `IsGranteeDeniedOnRecordAsync` (contacts/orgs only), `ResolveDenyVetoAsync` (read-time, private) | used by 4+ share sites and needs a status-bearing link read that the evaluator's contact path lacks | `/share-user` and provisioning hand a walled user a share |
| `NoAccessEnforcementStore` (typed HttpClient) | `DataverseWebApiClient` cannot call `RetrievePrincipalAccess`; `CallerRecordAccessProbe` is OBO-only by design | N5 and criterion 7 need RPA for a principal other than the caller | no author check, no residual report |
| `NoAccessShareEnforcer` (scoped) | `InternalShareEndpoints.UnshareAsync` removes one share per request | needs entry→records×users, N2/N5/S5, a report, job reuse | an entry added after a share exists changes nothing |
| `POST /api/v1/external-access/no-access/enforce` + `DelegationRuleFilter` case | 064 (pending) cannot see MDA-authored entries | joins the existing group | save-time enforcement (R3 "minutes") impossible |
| `NoAccessShareReconciliationJob` (`AddScheduledJob`, ADR-036/052) | `ExternalAccessReconciliationJob` (row lifecycle, ships disabled) | different reason to change; must ship enabled | OOB MDA shares after an entry stay forever |
| `ExternalParticipationService.FindSecureRootsReferencingOrganizationAsync` / `FindWallMemberContactsAsync` + `BuildOrganizationMembershipFilterForOrganization` | extension of the class that owns the org-lookup registry and junction filter | — (extension) | — |
| `ImpersonatedRootSetSource.DeploymentCacheTenant` / `InvalidateForTenantAsync` | extension | — | the job would invalidate the "anonymous" key nobody reads |
| `sprk_noaccessentry_postsave.js` | `sprk_kpiassessment_quickcreate.js` is KPI-specific | — | no save-time call |

Reader: three-subject `NoAccessSubjects` overload (the contact overload delegates), exactly-one-of-three malformed rule,
`DenyingSubjectKinds` provenance. Reason codes: `sdap.access.user_share.subject_no_access`,
`…user_share.no_access_unverifiable`, `sdap.provision.creator_no_access(_unverifiable)`,
`sdap.provision.resume_creator_no_access`, `sdap.provision.principal_no_access(_unverifiable)`,
`sdap.access.no_access.{entry_required|entry_not_found|entry_inactive|entry_malformed|entry_unreadable|last_person_on_secure_record|enforcement_incomplete}`.

Not built (stated): by-USER enforcement (the job covers links that appear); the own-firm warning (067); the form
registration, ui-tests and live gate 14 (154).

## 4. Tests

New: `SecureShareNoAccessGuardTests` (16), `NoAccessShareEnforcerTests` (19), `NoAccessEnforceEndpointTests` (8, real
filter pipeline + real DI-composed enforcer), `NoAccessShareReconciliationJobTests` (8), `ProvisionNoAccessTests` (5),
reader tests (+9), evaluator tests (4: the N3 split, the user subject, the fault — one of them a rewrite, see below),
`/share-user` tests (+10).
Beyond the contract (one line each): `TheJobShipsEnabled_EveryFiveMinutes` pins the owner's R4 posture (the
identity-link job precedent; not a DI-registration test); `EnforceForRecord_*` pin the 142 hook.

Rewritten, not deleted: `ComposeAsync_SystemUserRecordThatWouldSurviveRestricted_IsStillRemovedWhenAlsoDenyListed`
asserted the pre-N3 behaviour (any record removed whole). It is now the SECURE half
(`…OnASecureRecord_ALinkedContactEntryRemovesTheWholeRecord_MembershipIncluded`) plus the non-secure half.

Fixture changes: the seam/grant-policy deny-reader doubles now populate the SUBJECT column a real row carries (the new
malformed rule would otherwise reject their subject-less rows); the provisioning fixture registers the production guard
over its own deny list; `GrantPolicyContractTests` seeds the share target's link row.

Bite proof: ten violations seeded at once (share guard, malformed rule, secure split, S5, N5, creator guard,
colleague skip, filter case, job cache key → "anonymous", resume guard) — 27 tests failed, each seed reddening its
own tests; restored and touched.

## 5. Pending manual gates (main session; live writes)

| Gate | Command | Order |
|---|---|---|
| G-1 schema | `pwsh scripts/Set-NoAccessSystemUserSubjectSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Apply` then `… -Verify` (exit 0) and `mcp__dataverse__describe('tables/sprk_noaccessentry')` | **BEFORE any BFF deploy carrying this branch** — the reader selects `_sprk_subjectsystemuser_value`; without the column every No Access read fails closed (deny-all, both planes). Dry run done read-only 2026-10-02 |
| G-2 deploy | BFF deploy (bff-deploy skill) | after G-1 |
| G-3 form steps | task 154: field on the entry form; register `sprk_/scripts/bff_auth.js` then `sprk_/scripts/noaccessentry_postsave.js` (`Spaarke.NoAccessEntry.onLoad`) | after G-2 |
| G-4 live gate 14 (i)–(v) | spaarkedev1, existing non-admin users in their BUs, an author who can create entries (O2 role) AND holds Write on the project | after G-3; ⚠️ O2 is answered (accepted) but not yet APPLIED (Core User still holds Global Read — 154). The POML's O2 trigger asks to stop before sign-off while every core user reads every Reason |

## 6. Residuals (stated, not hidden)

- **Performance**: the residual check is one RetrievePrincipalAccess per (covered user × covered secure record) — bounded
  by 200 × 500 per entry, every 5 minutes. Fine for dev (0 entries); a large organization subject should be watched.
- ~~The job scans the newest 500 active entries; beyond that it reports TRUNCATED (Success=false) every run.~~ Superseded in r1 (§7 item 7): every active id is read (ceiling 50,000); 500 are enforced per run, rotating.
- An entry whose org subject has >200 members reports Truncated every run (not clean) until the ceiling is raised.
- ~~The read-time veto takes the linked contact from `IIdentityNormalizationService` (a faulted link read is "no contact"
  there — task 141's contract), while the guard reads the link status-first; the systemuser subject is unaffected.~~
  Withdrawn in r1 (§7 item 2): on secure records the veto now reads the link status-first through the guard's resolver.
- **S5 lock, server side (r2, §8 item 3).** The removal lease is renewed just before the revoke and lives 5 minutes,
  longer than the client's own bound on the revoke (100 s HTTP timeout + up to 30 s token wait). Dataverse may still
  apply a revoke the client gave up on; if it lands more than 5 minutes after the renewal, a second enforcement could
  judge S5 from a share about to disappear. Not closable without fencing, which Dataverse does not offer.
- **A dead lock holder** blocks that record's removals (`record-busy`) for up to 5 minutes (r2: was 2); the job retries.
- **A next-page link outside the configured Web API root** (another host or version than `Dataverse:ServiceUrl`)
  is never followed; the scan fails and the run records `error`. With more than 500 active entries, no entry is
  enforced by that run (deliberate: never a silent prefix). The save-time endpoint and Update Access are unaffected.
- **The lease store is the scheduler's** (r2, §8 item 2): a PROPOSED §6.5 path-A exception, awaiting the reviewer.

## 7. Round r1 (2026-10-03) — adversarial-verifier findings closed

Branch `task/uac-r2-143-r1` (from `task/uac-r2-143` @ `d248dff11`). Live writes: none.

| # | Finding | Disposition |
|---|---|---|
| 2 | MEDIUM, fail-open: the systemuser-plane veto took the linked contact from `principal.ContactId`, which `IIdentityNormalizationService` derives under a "never an exception" contract — a transient link-read fault read as "no contact", so contact- and organization-subject walls on SECURE records were never consulted (residual W4, not a §6.5 path) | **FIXED (path C — comply).** For every SECURE candidate the veto now resolves its subjects through the write-time guard's own static `SecureShareNoAccessGuard.ResolveSubjectsAsync` over `IContactIdentityStore` (status-first): the systemuser, its `sprk_primarycontact` link, every contact bound to its oid, their wall organizations — plus the principal's derived contact. A faulted link, binding or membership read removes **every secure candidate whole** (ADR-003 "link unreadable → removed"). Non-secure candidates keep the N3 rule over the derived contact only: there the wall removes only that contact's grant contribution, and a contact the normalizer could not derive contributed no grant — so the normalizer's fault-swallowing cannot open anything there. `AccessibleRecordSetService` gains one ctor dependency (`IContactIdentityStore`, already a registered singleton) — no new registration. Residual W4 is withdrawn. |
| 3 | LOW-MEDIUM: the guard's remark claimed the veto asked "the same question through the same reads"; it did not | **FIXED** by item 2 (the secure-record subjects ARE the guard's), and the remark rewritten to say exactly who asks what: the guard and the veto (secure records) call the one resolver; the enforcer asks the reverse question over the same two links; the veto's non-secure branch deliberately checks only the derived contact (why stated). |
| 4 | MEDIUM: seven fail-closed guards never proven to bite (S1, S18, S2, S9, S11, S20; S15/S22 mutually covered) | **CLOSED** — one test per seed, each driven by the PRODUCTION fault shape: S1 guard + S18 `EnforceForRecordAsync` with `ReferencedOrganizations.Unresolved` returned (never thrown; new `FlagStubParticipationService.UnreadableReferencedOrganizations`); S2 flags `IsUnreadable` with `IsSecure:false` (the factory `RootRecordFlags.Unreadable` also sets `IsSecure`, which is why the verifier's seed changed nothing against production values — the test pins the veto's own rule, and a second record uses the factory value); S9 the linked contact's membership read faulting, on a NON-secure record so the branch bites alone; S11 the owner read throwing (`FakeEnforcementStore.FailOwnerRead`); S20 a colleague whose link read fails (`ProvisionProjectTestFixture.UnreadableLinkUsers`) → skipped with `principal_no_access_unverifiable`, others shared. |
| 5 | LOW: `ReadActiveEntryIdsCoveringAsync` cut the organization list at 50 with no signal | **FIXED.** Every organization is asked about, in OR-chunks of 50 (record clause in the first), answers unioned, `Truncated` past `max`. Wire seam `QueryActiveEntryIdsAsync`; filters built by the pure `CoveringObjectFilters`. |
| 6 | LOW: S5 race — nothing serialized the enforce endpoint, concurrent endpoint calls and the job | **FIXED.** The enforcer removes a direct share only under a per-RECORD lease — the existing atomic `IScheduledJobLease` (Redis `SET NX PX` across instances/slots; process-local when Redis is off), keyed `no-access-enforce:{table}:{id}`, 2-minute expiry — and RE-READS the shares under it before the S5 check, the revoke and the read-back. A held lease → failure `record-busy` (nothing removed; the job retries in ≤5 min); an unreachable lease store → `record-lock-unavailable` (fail closed). The job's remark now says why chunk claims are still not needed: the S5 cross-check is serialized where it is decided. **Scope stated:** the lease serializes the enforcer with itself; a user's own unshare (task 139) and the MDA Share dialog do not take it (the latter cannot be serialized by the BFF at all). |
| 7 | LOW: the job always scanned the newest 500 active entries; the rest were never enforced | **FIXED.** The scan reads every active entry id (paged via `@odata.nextLink`, `Prefer: odata.maxpagesize=500`, ordered by id; ceiling 50,000 ids → TRUNCATED). At most 500 are enforced per run; past that the window ROTATES from an in-instance cursor (the last entry id reached; robust to adds/deactivations), so every entry is enforced within ceil(N/500) runs. Such a run is reported `ROTATING` (status partial, `Success=false`, `activeEntries`/`rotating` in the result). Residual: past 50,000 active entries the excess is not read (reported TRUNCATED every run). The next-link is followed only when it is under the configured Web API root (the app token is never sent elsewhere). |
| 8 | LOW / observation: `TheJobShipsEnabled_EveryFiveMinutes` | **No change — proved not a ban-B3 test.** It asserts the shipped VALUES (cron `*/5 * * * *`, enabled) that encode owner R4, not that a type is registered: perturbing either reddens it; it follows the `IdentityLinkReconciliation` precedent. |
| 9 | Verified independently | Accepted (build, full suite, ArchTests, +0.05 MB publish). Re-run after r1 below. |
| 10 | Criterion 11 partial | **Now met**: link-read fault → secure candidates removed (test); veto fault branches (organizations unreadable, flags unreadable) tested and bite. |
| 11 | Criterion 13 partial | **Now met** for every production fault shape the verifier listed (item 4). |
| 12 | Criterion 9 partial | **Now met**: the unverifiable-colleague skip is tested and bites (S20). |
| 13 | Criterion 1 — live gate G-1 | **NOT CLOSABLE HERE** (read-only rule). Unchanged pending gate: `pwsh scripts/Set-NoAccessSystemUserSubjectSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Apply`, then `-Verify` (exit 0) and `describe('tables/sprk_noaccessentry')` — BEFORE any BFF deploy of this branch. |
| 14 | Criterion 14 — manual live gate | **NOT CLOSABLE HERE.** After G-1, G-2 (BFF deploy), task 154's form registration and O2's application. |

### Peer report #1081 (owner, 2026-10-02) — effect on this task

Root-BU membership is a dev artifact (round 5): in dev every root-team member reads every secure record through Basic
User at the root (Secure Record BU included), and the "Spaarke Demo" team holds System Administrator. For task 143 this
means, **in dev only**: a walled root-team member's access to a secure record is `role-or-business-unit` — reported
`NoAccessNotEnforceable` and hidden on Teams/SPA by the veto (N2), never revoked; a System-Administrator team member
is likewise reported, never revoked. Nothing is coded for the root team. Live gate 14 must therefore use users placed
in a child BU (as round 4 item 1 already says: ask the owner for test users at specific levels).

### r1 tests

New (each bites its own seed, below): evaluator +4, guard +1, enforcer +5 (incl. a deterministic interleaving of two
enforcements on a record whose only two readers are both walled), store +5 (new `NoAccessEnforcementStoreTests`), job +2,
provisioning +1. Doubles extended at module boundaries only: `FlagStubParticipationService.UnreadableReferencedOrganizations`,
`FakeEnforcementStore.{EntryReads, FailOwnerRead}`, `Harness.Lease`, `ProvisionProjectTestFixture.UnreadableLinkUsers`,
`AccessibleRecordSetTestFactory.UnlinkedIdentityStore()` (the inert default for the 8 pre-existing construction sites).

**Bite proof (r1).** 13 violations seeded ONE AT A TIME, each followed by a build and the 537 task-related tests, then
restored and touched; every seed reddened its own new test(s) and nothing else:

| Seed | Failed |
|---|---|
| B1 veto ignores a faulted link read | 1 (link-fault test) |
| B2 veto back to the derived contact only (pre-r1 subjects) | 2 (oid-bound contact; link fault) |
| S1 guard ignores `Unresolved` referenced organizations | 1 |
| S18 `EnforceForRecordAsync` ignores `Unresolved` | 1 |
| S2 unreadable flags no longer secure to the veto | 1 |
| S9 unreadable linked-contact organizations no longer remove every candidate | 1 |
| S11 unreadable owner no longer a residual failure | 1 |
| S20 provisioning shares to an Unverifiable colleague | 1 |
| L1 removal without the per-record lock | 3 (held lock; unreachable store; the interleaving S5 test) |
| L2 removal proceeds when the lease store is unreachable | 1 |
| C1 covering read asks the first 50 organizations only | 1 |
| P1 active scan reads one page only | 2 |
| J1 the job enforces the same first window every run | 1 |

**Verification (r1).** BFF build 0 errors / 0 warnings. Full BFF unit suite **14423 passed, 0 failed, 54 skipped, 14477
total** (20m48s — the verifier's 14405 plus the 18 new). `Spaarke.ArchTests` **346/346**. Publish size not measured here
(the main session measures; r1 adds no package, no csproj change).

## 8. Round r2 (2026-10-03) — adversarial-verifier findings closed

Branch `task/uac-r2-143-r2` (from `task/uac-r2-143-r1` @ `a4ade61a8`). Live writes: none.

| # | Finding | Disposition |
|---|---|---|
| 1 | MEDIUM: the S5 re-read under the per-record lease was never proven to bite. Seeding "lock taken, stale pre-lock shares used" passed all 345 task tests; the r1 interleaving test proves only the `record-busy` refusal, and the r1 bite row L1 (removal without the lock) proves the LOCK, not the re-read | **CLOSED.** New `AnEnforcementWhoseFirstShareReadPredatesAnotherEnforcementsRemoval_ReReadsUnderTheLock_AndKeepsTheLastReader`: B reads {Walled, walledToo}, A then runs start to finish (takes the lock, removes Walled, releases), and only then does B take the now-free lock. B must report no failure (the lock did not decide), remove nothing, and report `last-person-on-secure-record` for walledToo, whose share stays. The verifier's exact seed (bite row L3 below) reddens this test and only this test. The r1 bite table's L1 row is corrected below: it covers the lock, L3 covers the re-read. |
| 2 | LOW-MEDIUM: the enforcer uses `IScheduledJobLease` (the scheduler's dispatch-lease store) as a per-record mutex; `NoAccessShareReconciliationJob` reaches it transitively, against ADR-036 A1-7 / ADR-052 §5, and the tension was not surfaced (§6.5); the keys land in the `{prefix}scheduler:lease:` family, catalogued as `SchedulerLease` only | **SURFACED as a §6.5 path-A exception, PROPOSED, NOT YET ACCEPTED.** Recorded in `design.md` §9 (new row "ADR-036 A1-7 / ADR-052 §5"). Why not C: the only other cross-instance lock in the BFF, `IIdempotencyService.TryAcquireProcessingLockAsync`, is check-then-set (not atomic) and fails OPEN on a cache fault, so it cannot protect S5; complying means a new lock type with Redis and process-local implementations that duplicates `RedisScheduledJobLease` (§11). Why not B: the rule's intent (a job never touches the host's dispatch of ITSELF) holds; only the letter (the type) is crossed. Bounded and catalogued: the keys appear as the second site of `SystemCacheKeys.SchedulerLease`, with raw key `{InstanceName}scheduler:lease:no-access-enforce:{table}:{recordId}`. No job id starts with `no-access-enforce:`. `occurrenceUtc` is always null. Every lock problem fails closed. The enforcer's ctor doc names the exception. **🔔 The reviewer must accept or decline it explicitly** (§6.5: "code-review approves explicitly"); the task-143 PR must cite the §9 row. If declined, path C is a new lock type independent of `Spaarke.Scheduling`. |
| 3 | LOW: the 2-minute lease is never renewed; slow reads (throttling) could outlive it, and a second enforcement could take it mid-removal; undocumented | **FIXED + residual stated.** The lease is RENEWED right before the revoke (`RenewRecordLockAsync`). A renewal answering false (the lease expired, or someone else holds it) removes nothing and reports `record-lock-lost`. A renewal that cannot be made removes nothing and reports `record-lock-unavailable`. A successful renewal proves the lease was held throughout, because Redis `LockExtend` compares the token and fails on an expired key. `RecordLockDuration` 2 → **5 min**, so after the renewal it outlasts the revoke: the Dataverse Web API client's default 100 s timeout, plus its token-refresh wait of up to 30 s. The cost: a holder that dies blocks that record's removals (`record-busy`) for up to 5 min, which the 5-minute job absorbs. **Residual (§6):** Dataverse may still apply a revoke after the client has given up on it. If that happens later than 5 min after the renewal, a second enforcement could judge S5 from a share that is about to disappear. The window is server-side processing of one request; it is not closable without fencing, which Dataverse has no form of. |
| 4 | LOW: the absolute-URL branch of `NoAccessEnforcementStore.GetAsync` (origin check) was never exercised; a foreign next link fails the whole scan | **CLOSED.** The URL choice is extracted unchanged into `internal static ResolveRequestUrl(apiUrl, relativeOrNextLink)`, which `GetAsync` calls. Eight new cases: a relative path, a link under the root (as written, and upper-case host), and five that are refused (another host, a look-alike host, another version, the root as a bare prefix `v9.2.evil`, an `http` downgrade). The scan-level consequence is unchanged and deliberate: the refused link throws, the job records `error` and names the problem (`ARun_WhoseScanFaults_RecordsSuccessFalse_NeverNothingToReconcile`), and the entries already read are NOT enforced as a silent prefix. Stated in the method's doc and in §6. |
| 5 | TRIVIAL: `NeverDeniesReader`'s `<summary>` sat above `UnlinkedIdentityStore` | **FIXED.** The summary moved back to its method. |
| 6–8 | Verified OK | Accepted. |
| 9 | Observation: no completion markers in the job | Accepted as reasoned. No change. |
| 10 | Observation: a row carrying both contact and organization subjects is malformed | Accepted. Criterion 1 mandates it. No change. |
| 11/12 | Criterion 13 not met (S5 re-read; publish size) | **Test half met** (item 1). Publish size remains the main session's. |
| 13 | Criterion 1: live gate G-1 | **NOT CLOSABLE HERE** (read-only). Unchanged: `pwsh scripts/Set-NoAccessSystemUserSubjectSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Apply`, then `-Verify` (exit 0) and `describe('tables/sprk_noaccessentry')`, BEFORE any BFF deploy of this branch. |
| 14 | Criterion 14: manual live gate | **NOT CLOSABLE HERE.** It needs G-1, G-2 (the BFF deploy), task 154's form registration (`bff_auth.js` before `noaccessentry_postsave.js`) and O2 applied. Per #1081 and round 5, it uses **child-BU users**, never root-BU or "Spaarke Demo" team members: their secure-record access is `role-or-business-unit` in dev, reported, never revoked. |

### r2 tests

New: 11 tests. The enforcer gains 3: the stale-first-read interleaving, the lease lost before the revoke, and the renewal unreachable. The store gains 8 cases in 3 methods. `InterleavingShares` gains a one-shot `AfterFirstRead` hook. The lease double `ExpiringLease` is a module-boundary double over `ProcessLocalScheduledJobLease`. No new double of production internals. The task-related set: **356/356**.

**Bite proof (r2).** Five violations were seeded one at a time. Each was followed by a build and the 356 task-related tests, then restored from a backup copy and touched:

| Seed | Failed |
|---|---|
| L3 the lock is taken, but the STALE pre-lock shares decide (the verifier's seed) | 1 (the new stale-first-read test) |
| R1 no renewal before the revoke | 2 (lease lost; renewal unreachable) |
| R2 a failed renewal is treated as held | 1 (lease lost) |
| U1 the origin check is dropped | 5 (every refused link) |
| U2 the check has no trailing `/` (a bare prefix) | 1 (`v9.2.evil`) |

Correction to the r1 bite table: **L1** ("removal without the per-record lock") proves the lock. It does not prove the re-read, which only L3 proves.

**Verification (r2).** BFF unit-test build 0 errors / 0 warnings. Full BFF unit suite **14434 passed, 0 failed, 54
skipped, 14488 total** (19m47s — r1's 14423 plus the 11 new). `Spaarke.ArchTests` **346/346**. Publish size not measured here
(the main session measures; r2 adds no package and no csproj change).
