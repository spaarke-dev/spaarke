# Task 132 (#1056) — C12: access caches stop storing faults, BFF writes evict, TTLs cut to 2 minutes

**Status:** code complete on `task/uac-r2-132` (parent `task/uac-r2-137-b2`, merged with `work/unified-access-control-r2`
at `3a6b38cb1`); verifier rounds **r1** (§15), **f1** (§16), **f1-v1** (§16.6) and **f1-v1c** (§16.7) closed. POML status
**completed**: escalation 7 (§8) was answered by owner round 10 item 3 (§16); the dev deploy and the manual live gate
(criterion 21) are pending manual gates, now a dry-run / `-Apply` / `-Verify` script (§12 item 3). Rigor FULL, Opus 5.5 @ xhigh.

**Owner sign-off (merge gate, criterion 18):** owner rounds 3 **R3/R4** (2026-09-30, BINDING) — *"Access changes must
take effect in MINUTES, never hourly … the background job is only a safety net, running at ≤ 5 min"* — relayed by the
orchestrator as this task's sign-off on the shortened identity/membership TTLs. Every bound in the residual table (§9)
is ≤ 4 minutes; the stacked worst case (identity + membership) is 4 minutes, inside the ≤ 5-minute rule.

---

## 0. Step 0 — dependencies, anchors, corrections

- **131** (grant cache carries `DirectAccessLevel`, CacheVersion 5) and **109** (one junction read, `Unreadable`) are
  ✅ completed and present on this branch. **137** (the parent) consolidated grant-cache invalidation into
  `InvalidateGrantSetsAsync`; the single grant-cache WRITE path (`GetGrantSetAsync` → `CacheGrantSetAsync`) is where this
  task's gate sits, as the POML's 137 dependency line asks.
- **Anchor drift (code won again):**
  | POML claim | Reality at `3a6b38cb1` |
  |---|---|
  | `IdentityNormalizationService` sub-path catches at `:226/:564/:695/:735/:784`, cache helpers `:818/:851`; contact cross-ref at `:568-576` | the file is 579 lines after task 141: systemuser `:229`, binding `:311` (already guarded), teams `:365`, account `:405`, orgs `:454`, cache helpers `:488/:521`. The "contact cross-ref" is now task 141's oid BINDING lookup |
  | deny-veto subject = "the identity's contact, or else an email-resolved contact (`AccessibleRecordSetService.cs:1017-1034`)" | task 141 DELETED the email fallback. The subject is `principal.ContactId` = `identity.ContactId` (`WorkforcePrincipalResolver.cs:135`) |
  | `MembershipResolverService` CacheVersion 4, TTL `:112` | unchanged anchors held |
  | `ExternalParticipationService` grant catch-all `:989`, sub-queries `:1055/:1127` | drifted to `:1382`, `:1474`, junction `:1609-1624` (131/109/137 added lines) |
  | `CachedAccessDataSource` dead keys `:136-139`, `:246-298` | held |
  | `Membership:CacheInvalidator:Enabled` in no Bicep / no template | held — and see §6.1: the flag could not have helped |

## 1. Step 1 — the fault-vs-answer classification (the specification the tests implement)

The rule, once per cache: **a successful read that returns nothing is an ANSWER (cached); a read that could not be
completed is a FAULT (returned to this request unchanged — fail closed — and never cached).** Only the CALLER's
cancellation propagates (`catch (OperationCanceledException) when (ct.IsCancellationRequested)`); an HttpClient timeout
(TaskCanceledException, caller token NOT cancelled) is a fault.

| Cache | ANSWER (cached) | FAULT (never cached) |
|---|---|---|
| (a) grant set — `ExternalParticipationService` | 2xx with a `value` array, zero rows included | grant query non-2xx (429 / 5xx / 403…), a 2xx without `value`, a parse failure, any exception, a timeout; the org-grant read non-2xx / exception / no `value`; task 109's junction `Unreadable` (consumed, no second read). A partial set (org term faulted) RETURNS the direct grants and caches nothing |
| (b) identity — `IdentityNormalizationService` | each sub-read completed (no teams, no link, no binding, no account, no orgs are all answers) | the systemuser row, teams, oid-binding lookup, account, or an organization resolver threw / timed out. Recorded as `PersonIdentity.Faults` (internal, not serialized) |
| (c) membership — `MembershipResolverService` | a response over a clean identity (zero matches included) | a response over a faulted identity, or (people-targeting surface) over an unreadable human/app-user check. The contact path takes no identity read — §3 |
| (d) snapshots — `DataverseAccessDataSource` → `CachedAccessDataSource` | RPA answered (an empty rights string included); probe 403/404; user lookup 2xx with zero rows | OBO failure, user lookup non-2xx / no `value` / parse failure / exception / timeout; RPA unanswered → probe readable (DEGRADED Read); probe any other status / exception / timeout; team or role sub-read failed; any exception |
| (e) impersonated root sets — `ImpersonatedRootSetSource` | unchanged: positive-only by construction (no catch on the read) | — |

`AccessSnapshot.Faulted` (additive, default `false`) carries (d)'s classification; `ExternalGrantSet.Faulted`
(`internal init`, `[JsonIgnore]`) carries (a)'s. Probe classification is ONE rule
(`DataverseAccessDataSource.ClassifyProbeStatus`) shared by the record and document probes.

## 2. (a) Grant set

- `QueryGrantSetAsync`: non-2xx / no `value` → `ExternalGrantSet.Unreadable`; catch-all → `Unreadable`; caller
  cancellation rethrown. `QueryOrganizationGrantRowsAsync` now returns `(rows, faulted)`; the set is `Faulted` when the
  org term faulted, with the direct grants kept.
- **r1 (verifier item 6, criterion 3):** the junction query (`QueryOrganizationMembershipsAsync`) now RETHROWS the
  caller's cancellation too. Task 109 had it report `Failed` because `QueryGrantSetAsync`'s catch-all used to turn an
  escaping cancellation into an empty, cached grant set (109's W3); this task made that catch rethrow and gated the
  write, so the reason was gone and the old behaviour left one window — a client abort DURING the junction read made
  `GetGrantSetAsync` return a faulted set instead of propagating. The justifying comment (now false) is rewritten, and
  109's W3 test is rewritten to the corrected contract (§15).
- `GetGrantSetAsync` (the ONE write path 137 left): a faulted set is logged and not written. Its cache-read catch is
  guarded for caller cancellation.
- **No `CacheVersion` bump**: `CachedGrantSet` is unchanged (the flag is never cached). A pre-fix fault-derived grant
  entry lives ≤ 60 s after deploy. 131's shape guard names `Faulted` in `NotCarriedByDesign` with the reason, and
  `GetGrantSetAsync_ACacheHit_NeverReportsFaulted` pins the other half (constraint "task 131").
- **109 residual #1 (a grant set built over a faulted junction read is cached for one TTL): CLOSED** — the junction's
  `Unreadable` now marks the set faulted.
- **109 residual #2 (a grant-set cache MISS performs a second junction read): ACCEPTED, with reason.** The two reads are
  the same query, filter and projection; they can differ only by a change in the milliseconds between them. On a cache
  HIT the grant set's organization view is already up to 60 s older than the evaluator's fresh read — the identical
  divergence, for the whole TTL — so unifying the reads on the miss path would not tighten the worst case at all, while
  it would add a per-request memo (or change `GetGrantSetAsync`'s signature — six overriding doubles, the CIAM `/me`
  surface). The divergence is bounded by the documented 60 s grant TTL (§9 row 1).

## 3. (b) Identity and (c) membership

- `IdentityNormalizationService`: every sub-read returns `(value, faulted)`; the merged identity carries `Faults`; it is
  cached only when `Faults == None`. Every catch this task touched is guarded (`when (ct.IsCancellationRequested)`),
  including the two cache helpers and `InvalidateAsync`.
- **The organization-resolver path was unreachable before this task**: `OrganizationMembershipResolver` swallowed its
  own query fault (fail-soft "empty + Warning"), so the identity layer's resolver catch never fired and a failed
  organization read was cached as "member of no organization". The `IIdentityOrganizationResolver` adapter now lets the
  fault reach the identity layer (`ResolveCoreAsync(failSoft: false)`); the canonical `GetOrganizationIdsAsync` keeps
  its fail-soft contract (its tests unchanged). Request behaviour unchanged (orgs empty); only caching changes.
- `MembershipResolverService`: one gate (`TrySetCacheUnlessFaultedAsync`) on the systemuser path's three writes.
  People-targeting: an unreadable human/app-user check (`IsApplicationUserAsync == null`) is also a fault.
- **Contact path (criterion 8):** `ResolveByContactAsync` takes no identity read (it builds the identity from its
  inputs), and its key encodes every input — `contact:{contactId}:{entity}:{hash}` with `OrganizationIds` inside the
  options hash (`HashOptions`, task 043). Its only reads are discovery and the FetchXml; both THROW on failure (no
  fail-soft), so no fault-derived response can be produced, let alone cached, and an entry built for one organization
  list can never answer another. Pinned by
  `Membership_ContactPath_AnEmptyOrganizationListDoesNotSatisfyALaterResolveWithTheRealIds`.
- **Versions bumped**: identity 1 → 2, membership 4 → 5 (constraint "stale fault entries at deploy").

## 4. Step 4 — the deny-veto subject trace (criterion 16): CONFIRMED, closed

Trace at `3a6b38cb1`:
1. `WorkforcePrincipalResolver.ResolveAsync` (`:134-135`) → `identity.ContactId` → `ForSystemUser(..., derivedContactId)`.
2. `IdentityNormalizationService`: a faulted systemuser row → `SystemUserData.Empty` → `PrimaryContactId` null AND
   `AzureAdObjectId` null → the binding lookup is skipped → `ContactId` null. A faulted binding lookup → null too.
3. `AccessibleRecordSetService.ComposeForSystemUserAsync`: `grantContactId = principal.ContactId` (null) →
   `ReadActiveOrgMembershipsAsync(null)` returns `ActiveOrgMemberships.None` → `ResolveDenyVetoAsync`: no contact subject,
   no org subject → returns `EmptyDeniedSet` (`:781-785`).
4. `ApplyVetoPipeline` slot 1 then removes nothing, so a No Access entry naming that person's contact (owner Q4: the
   No Access List applies to internal users on secure records) stopped applying to their membership-term access — for
   the request, and, because the faulted identity was cached, for 10 minutes.

**Fix:** `PersonIdentity.ContactUnreadable` (ContactId null AND the systemuser-row or binding read faulted) →
`WorkforcePrincipal.ContactUnreadable` (also set when the identity resolution itself threw) → on the systemuser plane
the evaluator denies every candidate, mirroring `ActiveOrgMemberships.Failed`. A user READ as having no linked contact
keeps today's path — the successful-read signal exists, so escalation trigger 4 does not fire (7 of 8 dev systemusers
lose nothing). The inline link (task 141) still runs when no contact was derived; if it links one, the flag drops.

## 5. (d) Access snapshots + ADR-009

- `DataverseAccessDataSource` classified per §1; every catch guarded for caller cancellation. One new internal seam:
  `GetDataverseTokenViaOBOAsync` became `internal virtual` (behaviour unchanged) — the MSAL exchange is the one step a
  test cannot drive offline; every Dataverse read after it runs as production code over the in-memory server.
- `CachedAccessDataSource`: ONE gate (`CacheUnlessFaulted`) on both methods; the write-only `sdap:` roles/teams keys,
  their TTL fields and writers are deleted.
- **ADR-009 tension — path C (pivot to comply), as the POML proposed.** Keys moved onto `ITenantCache` under the
  caller's `tid`: `tenant:{tid}:auth-access:{authMode}:{oid}:{documentId}:v1` and
  `tenant:{tid}:auth-record-access:{entitySet}:{oid}:{recordId}:v1` — subject-discriminated (oid + record) per the
  2026-09-28 ADR-009 §5 amendment. The new shape orphans the old `sdap:` entries (≤ 60 s).
  **Escalation trigger 3 does not fire:** every caller of the decorator runs inside an HTTP request carrying a validated
  Entra token — `AuthorizationService` denies with no caller token before reaching the data source
  (`Spaarke.Core/Auth/AuthorizationService.cs:54-72`, `:207-221`), `AiAuthorizationService` takes the `HttpContext` and
  denies when the bearer cannot be extracted (`:75-86`) — and every Entra token carries `tid`. The "sp" (app-only) branch
  is unreachable from both. Defensive: a request with no `tid` bypasses the cache entirely (no sentinel tenant, no
  allow-list entry).
- **Consumers of `IAccessDataSource` / `AccessSnapshot` (criterion 10 grep, 2026-10-03):** `AuthorizationService`,
  `IAuthorizationRule` / `OperationAccessRule` / `OperationAccessPolicy` (Spaarke.Core), `AiAuthorizationService`,
  `PermissionsEndpoints`, `CachedAccessDataSource`, `DataverseAccessDataSource`. **No behaviour change** for any of them
  from the additive `Faulted` property (default false; none reads it except the cache gate). The caller-cancellation
  rethrow is the one behaviour change: a client abort now propagates as `OperationCanceledException` instead of a None
  snapshot; `AuthorizationService.AuthorizeAsync`'s catch-all still turns it into a deny, and the request is gone
  anyway. `Spaarke.Dataverse` is referenced only by the BFF, Spaarke.Core (itself BFF-only) and test projects —
  escalation trigger 5 does not fire.

## 6. Invalidation (criteria 13-15)

### 6.1 A latent defect found: the real invalidator could never be registered

`MembershipModule` selected the real `MembershipCacheInvalidator` only when `Membership:CacheInvalidator:Enabled` AND
`services.Any(IConnectionMultiplexer)`. **`Program.cs` calls `AddMembership` (`:113`) BEFORE `AddCacheModule` (`:140`)**,
so no multiplexer was ever registered at that point: the Null peer was selected in EVERY environment, flag or no flag.
(After CacheModule, a multiplexer — real or `NullConnectionMultiplexer` — is always registered, so the check could not
have distinguished anything either.) Setting the flag in the three Bicep stacks would have changed nothing.

### 6.2 ADR-032 tension — decided: correctness does not depend on a kill switch

- Registration re-keyed on **`Redis:Enabled`** (the value `CacheModule` itself decides by, same configuration):
  Redis on → the real invalidator (the BFF write-path evictions ALWAYS active; the junction pub/sub publish and its
  subscriber still follow `Membership:CacheInvalidator:Enabled`); Redis off (in-memory — Development/Testing only,
  `CacheModule` refuses it elsewhere) → the Null peer. Symmetric, one implementation bound in every state.
- Every deployed environment runs Redis: `customer.bicep:606`, `stacks/model1-shared.bicep:520`,
  `stacks/model2-full.bicep:255` set `Redis__Enabled: 'true'`. **No Bicep change is needed or made** (criterion 15's
  first branch: "it does not depend on a kill-switch").
- Inert halves are visible: the real invalidator logs ONE Warning at construction when the junction channel is off
  ("junction pub/sub publication is INERT … BFF write-path access eviction is ACTIVE"); the Null peer logs ONE Warning
  that every invalidation is inert. The Null peer is never registered where Redis is enabled.

### 6.3 The hook (extension of `IMembershipCacheInvalidator`, no new service)

- `InvalidateUserAccessAsync(systemUserId)` — the user's identity entry, every membership entry, every impersonated
  root-set entry, under EVERY tenant segment.
- `InvalidateRecordOwnerChangeAsync(entityLogicalName, entitySetName, recordId)` — every user's membership entries for
  the entity type, every user's root-set entry for it, every user's access snapshot of the record.
- Mechanism: SCAN + DEL in the shared Redis directly (the subscriber's existing mechanism), so every instance sees it at
  once without pub/sub. Patterns are built ONLY from the readers' own builders — `TenantCache.BuildKey` (tenant = `*`)
  over `IdentityNormalizationService.CacheId`, `MembershipResolverService.ComposeCacheId`,
  `ImpersonatedRootSetSource.CacheId(string, string)`, `CachedAccessDataSource.RecordAccessCacheId` — prefixed with
  `RedisOptions.InstanceName` via `TenantCache.OnWire`, exactly as `StackExchangeRedisCache` prefixes. Current versions
  only (older versions are orphaned by the bumps).
- Never throws; one pattern's failure is logged and the next still runs; not bound to the request's token.
- XML docs on both methods name them as THE hook every team/BU or owner-changing writer must call, and name the C10
  writers (144, 146, 148, 149) that must wire it.

### 6.4 Callers

| Writer | Hook | Notes |
|---|---|---|
| `RegistrationDataverseService.AddUserToTeamAsync` / `RemoveUserFromTeamAsync` | user | in `finally` after the send (a timeout after Dataverse committed may still have applied it). No HttpContext needed (`DemoExpirationService`). Skipped when the write's environment (`targetDataverseUrl`, else `DATAVERSE_URL`) is not this BFF's `Dataverse:ServiceUrl` (D-13) — recorded next to the call; unknown on either side → evict |
| `RegistrationDataverseService.CreateSystemUserAsync` | user | after the BU bind + contact link (`finally`) — matters when Dataverse hands back an existing user's id |
| `ProvisionProjectEndpoint` | owner change | once the re-own is verified, in a `finally` around the share step (the shares are access changes too) — runs on success and on a share failure; never fails provisioning |
| `UnsecureProjectEndpoint` | owner change | in a `finally` around the share sweep (re-own + revocations), and on the "could not verify the re-own" path |

Not wired (recorded, not deferred work of this task): `InternalShareEndpoints` share/unshare already invalidate the
sharee's impersonated root set (task 063); their record-access snapshots of the record lapse within 60 s (§9 row 4).

### 6.5 SCAN cost (escalation trigger 8 — does not fire)

Dev Redis `spaarke-bff-redis-dev` (Basic), Azure Monitor `totalkeys`, hourly max over the 7 days to 2026-10-03:
**178 keys** (read-only `az monitor metrics list`). A full SCAN of that keyspace is one or two round trips; it runs only
on the rare team / BU / re-own writes, never on a read path.

## 7. TTLs (criterion 12) and read volume (escalation trigger 2)

| Cache | Before | After |
|---|---|---|
| identity | 10 min | **2 min** |
| membership | 5 min | **2 min** |
| impersonated root sets | 5 min | **2 min** |
| grant set, access snapshots | 60 s | 60 s (unchanged) |

All three now satisfy `caching-architecture.md`'s "MUST: authorization cache TTLs ≤ 2 minutes" (true of the code at
last). Every comment/doc in step 7's list was updated (IMembershipCacheInvalidator, the subscriber, root-set source,
SpaarkeCore, MembershipJunctionUpdater, IMembershipResolverService, MembershipResponse, MembershipEndpoints' header,
WorkforcePrincipalResolver's link-attempt TTL note, AccessibleRecordSetService, both architecture docs).

**Read-volume estimate (per active systemuser, R requests/hour, E root types composed per request, u ≥ 1 uncached reads
per composition — the batched flag read at minimum, plus junction / referenced-orgs / No Access reads when a contact is
linked; k ≤ 3 reads per identity resolve — systemuser row + teams + binding-or-account; organization resolvers are
unconfigured in dev):**

- identity reads/h: before `k·min(R, 6)`, after `k·min(R, 30)`; membership reads/h: before `E·min(R, 12)`, after `E·min(R, 30)`.
- Below R = 6/h nothing changes (both TTLs expire between requests anyway).
- **Worst ratio** at R = 30/h (one request every 2 minutes), k = 3, E = 3, u = 1: before 18 + 36 + 90 = 144, after
  90 + 90 + 90 = 270 → **1.88×**. At R = 60/h: 234 → 360 (1.54×); with u = 3 (a linked contact): 1.39×.
- Context: dev Redis served at most **1,251 GETs in any hour** over 7 days (all users).
- **Escalation trigger 2 does not fire** (no point exceeds 2×), but the margin at the worst point is thin and is stated
  so the owner sees it.

## 8. Throttling and the degraded answer (constraint "owner round 2"; escalation trigger 7)

- **No negative TTL** for faults. A fault costs one Dataverse call per request — the same as having no cache — never a
  retry loop; a 429's own Retry-After governs the client. Nothing to justify a 10 s negative entry.
- 🔔 **Escalation trigger 7 FIRES — recorded, not resolved by this task.** The DEGRADED answer (RPA unanswered →
  probe-derived Read) is uncached per the binding classification rule. During an RPA outage every authorization CHECK
  then runs RPA + probe, where before the degraded answer was cached 60 s per (user, record). The amplification equals
  the checks per (user, record) per 60 s — a document page load authorizes the same document several times (preview,
  metadata, versions), so it is plausibly 3-10× RPA volume, plus one probe each: **more than double** during an outage.
  Options for the owner:
  - **(a) keep it uncached (implemented — the classification rule's default; recommended).** Correctness: a Write
    holder is never pinned at Read once RPA recovers. Cost: during an outage, load = one RPA + one probe per check (the
    no-cache baseline); the outage is visible as `[UAC-DIAG] RPA-FALLBACK` warnings.
  - **(b) a ≤ 10 s negative TTL for the degraded answer only** — bounds the amplification at ≤ 6 RPA + probe per
    (user, record) per minute; a Write holder stays capped at Read for ≤ 10 s after RPA recovers. ~5 lines in
    `CachedAccessDataSource.CacheUnlessFaulted` (cache `Faulted && AccessRights == Read` for 10 s).
  It was NOT cached for 60 s (the trigger's forbidden choice).

## 9. Residual staleness (final, criterion 18) — signed off under owner R3/R4

After this task, staleness remains only for changes the BFF cannot observe (no plugins, ADR-002): the Dataverse admin
UI, MDA Assign / Change BU, flows, imports.

| # | Cache | TTL | Invalidated by (BFF) | Outside-BFF bound | Direction |
|---|---|---|---|---|---|
| 1 | External grant set (`external-access-grant`) | 60 s | grant / revoke / close / expiry writes (task 137, every tenant, org members fanned out) | 60 s | removal → old access ≤ 60 s (over-grant); addition → new access ≤ 60 s |
| 2 | Identity (`membership-identity`) | **2 min** | team add / remove, BU bind (`InvalidateUserAccessAsync`) | 2 min | same, for BU / team / contact link changes |
| 3 | Membership (`membership-resolved`) | **2 min** | the user's team / BU writes (per user); every re-own of the entity type (per entity) | identity + membership = **4 min** worst case (stacking) | same |
| 4 | Access snapshots (`auth-access`, `auth-record-access`) | 60 s | every re-own of the record, all users | 60 s (incl. BFF share/unshare and a user's team change — keyed by oid, not evicted) | same |
| 5 | Impersonated root sets (`impersonated-root-set`) | **2 min** | per user on a POA share change (task 063), per user on team add / remove, all users of the entity type on a re-own | 2 min | same |
| 6 | Fault-derived results | never cached | — | — | a fault denies one request, never the TTL |

Owner bound: R3/R4 "minutes, never hourly" with the safety-net cadence ≤ 5 min → every row ≤ 4 min. Recorded with the
sign-off in `docs/architecture/caching-architecture.md` § Access cache residual staleness.

Race (inherent to cache-aside eviction): a read that STARTED before an eviction and writes after it can re-cache a
pre-change answer; it lives at most one TTL. Not closed here (closing it needs versioned keys — escalation trigger 8's
alternative, not warranted at this keyspace size).

## 10. Tests (ADR-038 KEEP path `tests/integration/auth/UnifiedAccessControl/`)

New:
- `AccessCacheFaultCachingTests.cs` — criteria 1-9, 12, 16, 24 (49 cases).
- `AccessCacheInvalidationTests.cs` — criteria 13, 14, 23 (9 cases).
- `InMemoryRedisKeyspace.cs` — one store seen as the `IDistributedCache` (InstanceName applied like
  `StackExchangeRedisCache`) and as the `IConnectionMultiplexer` (SCAN/DEL); Redis glob modelled for `*` only, throwing
  for `?`, `[`, `\`.

Updated: `AccessCacheCharacterizationTests` (re-keyed onto `TenantCache` + `tid`, every property unchanged);
`GrantCacheRoundTripSeamTests` (`Faulted` in `NotCarriedByDesign` + `GetGrantSetAsync_ACacheHit_NeverReportsFaulted`);
constructor call sites (`RegistrationSecureRecordPlacementTests`, `RegistrationContactLinkTests`,
`IdentityLinkReconciliationTests`, `MembershipCacheInvalidatorTests`).

Instruments: grant set and snapshots over an in-memory ASP.NET Core server (ADR-038 §7's named replacement for B1, the
109 instrument) — the production classifier runs, so mapping a 429 to an answer reddens the tests. **Deviation from the
POML's ADR-038 tension text:** the POML proposed per-read `internal virtual` seams for `ExternalParticipationService`
and a path-A hand-written handler for `DataverseAccessDataSource`; neither was needed — the in-memory server reaches
every branch with no seam and no transport double (no path-A exception taken). Identity/membership over a substituted
`IDataverseService`; the deny veto over the production evaluator with its participation reads quieted. No
`Mock<HttpMessageHandler>`, no DI-registration or ctor-null tests, no reflection; timeouts are a 400 ms client timeout
against a server that never answers (no sleeps).

Beyond the closed set, justified: `Identity_…(teams-timeout)` (the constraint's timeout rule on the identity cache);
`WorkforceResolver_MarksTheContactUnreadable_OnlyWhenItsReadsFailed` (the link from identity to evaluator in criterion
16's chain); the TTL pin (criterion 12 is testable, the stacking check is the owner bound).

**Results (2026-10-03):** affected classes **739 / 0 / 0**; full BFF unit suite **14,318 passed / 1 failed / 54 skipped
(14,373)** — the one failure, `AttachmentFilterServiceTests.Filter_LogoImage_Excluded("logo_company.jpg")` (an e-mail
attachment filter this task does not touch), took 4 s under a 25-minute concurrent run and passes in isolation
(**48 / 48** for its class): contention, reported both ways; NetArchTest **346 / 0 / 0** (re-run after the last doc
edit); `Sprk.Bff.Api.IntegrationTests` **104 / 0 / 0**; `Spe.Integration.Tests` **403 passed / 0 failed / 25 skipped
(428)**. Builds 0 warnings / 0 errors. No package change (no CVE delta); publish size skipped by instruction.

**Publish size + CVE (criterion 20, CLAUDE.md §10 — measured by the r1 verifier, recorded in r1):** both sides built in
FRESH short-path detached worktrees and zipped with the SAME tool, PowerShell `Compress-Archive` (Optimal):
`origin/master` @ `818840ac6` = **45.66 MB, 212 files**; this task @ `4ed160714` = **45.67 MB, 212 files**;
**delta +0.01 MB**, equal file counts, well under the 60 MB ceiling and the +5 MB escalation threshold. (The verifier's
report does not state the PDB convention; both sides used the same publish, so the delta stands either way.) **CVE:** no
`.csproj` or package change anywhere in the task (or in r1), so `dotnet list package --vulnerable --include-transitive`
can report nothing new. r1 adds no package and changes a handful of source lines; its publish size was not re-measured
(run instruction).

**A test-harness trap worth carrying:** the framework `HttpContextAccessor` keeps the context in an AsyncLocal, which
does not flow back out of an async world-builder — the first run's "IS cached" assertions failed (no `tid`, so nothing
cached) while every "is NOT cached" assertion PASSED vacuously. Fixed with a field-backed accessor; the perturbations
(§11) prove the negatives now bite.

## 11. Perturbations (criterion 19) — each seeded, seen red, restored byte-identical, file touched

Each seed applied to the production file, BFF + tests rebuilt, the named tests run, the file restored from a byte copy
(asserted identical) and touched. 15 seeds, none zero.

| # | Seed (criterion 19 wording where it has one) | Red |
|---|---|---|
| P1 | re-enable the cache write for a faulted grant set (`if (false && grantSet.Faulted)`) | **8** — every grant fault case (non-2xx ×4, malformed, timeout, org-grant, junction) |
| P2 | map a 429 to an ANSWER inside the grant-query classifier (`ExternalGrantSet.Empty` for 429) | **1** — `GrantSet_GrantQueryAnswersNon2xx_…(429)` — the test reaches the classifier, not just the gate |
| P3a | remove the cancellation guard (arm made unmatchable: `when (ct.IsCancellationRequested && !ct.CanBeCanceled)`; a bare `when (false)` is CS8359 under warnaserror) | **1** — `GrantSet_CallerCancelsDuringTheGrantQuery_…` (criterion 3) |
| P3b | un-guard it (`catch (OperationCanceledException)` — a timeout then propagates) | **1** — `GrantSet_GrantQueryThrowsOrTimesOut_…(timeout)` (criterion 2) |
| P4 | cache a faulted identity (`if (true \|\| faults == None)`) | **6** — every sub-path case (criterion 6) |
| P5a | cache a faulted or degraded snapshot (gate disabled) | **14** — every record- and document-path fault case (criterion 9) |
| P5b | classify the probe-derived Read as an answer (`faulted = probe == Faulted`) | **1** — the degraded record case (the classifier, not the gate) |
| P6a | remove the provisioning re-own invalidation call | **2** — `ReOwn_…(provision)` + the eviction-failure test (no scan attempted) |
| P6b | remove only the impersonated-root-set part of the owner-change patterns | **2** — `ReOwn_…(provision)` and `(unsecure)` (root-set re-read) |
| P7 | change the identity eviction pattern's resource segment by one character (`"membership-identit"`) | **4** — criterion 23 user eviction + every registration write |
| P8 | remove the deny-veto unreadable branch | **1** — `DenyVeto_…(contact-unreadable)` (the traced fail-open returns) |
| P9a | revert the identity version to 1 | **1** — `Identity_AnEntryCachedUnderThePreFixVersion_IsNotServed` |
| P9b | revert the membership version to 4 | **1** — `Membership_AnEntryCachedUnderThePreFixVersion_IsNotServed` |
| P10 | remove the team-remove eviction (the over-grant direction) | **1** — `RegistrationWrite_…(team-remove)` |
| P11 | remove the other-environment rule (evict for every environment) | **1** — `RegistrationWrite_ToAnotherEnvironment_EvictsNothing_…` |

After restoring: affected classes 739 / 739 green.

**Count correction (r1, verifier item 13):** the original run's structured report said "17 perturbations"; the record
above — the authoritative one — lists **15** seeds (P1, P2, P3a, P3b, P4, P5a, P5b, P6a, P6b, P7, P8, P9a, P9b, P10,
P11). The r1 seeds R1-R8 are in §15.

## 12. Pending manual gates (no live writes made)

1. **Dev deploy** (bff-deploy skill), then **criterion 21** (manual, existing non-admin test users in their current BU,
   RECORD changes only — owner 2026-09-10/09-30):
   - (a) MDA: reassign a team-owned test matter away from the test user's team; poll the Teams/SPA matter list; record
     when it disappears — must be within 4 min (§9 row 3).
   - (b) a BU colleague loads the project list (warm cache), then provision a secure test project (or unsecure →
     re-secure the existing dev secure test project); the colleague's FIRST request after provisioning returns must
     not list it, and a direct read must be denied.
   - (c) record `ExternalAccess:ImpersonatedRootSets:Enabled` (036 not merged → flag off).
   - Evidence of eviction: App Insights `[ACCESS-EVICT] Evicted {Count} cache entries for sprk_project {id} (owner
     change)`.
   - **Deploy-order precondition (r1, verifier item 11):** a systemuser-row read failure now denies the whole
     systemuser plane on the grant-supported root types (criterion 16), and the identity's systemuser read selects task
     141's `sprk_primarycontact`. Before deploying this code to ANY environment, confirm the column exists there
     (read-only): `GET {env}/api/data/v9.2/EntityDefinitions(LogicalName='systemuser')/Attributes(LogicalName='sprk_primarycontact')?$select=LogicalName`
     → 200. Dev has it (141 G-1). A 404 means deploy 141's schema first, or Teams/SPA shows internal users nothing,
     logged as `[WF-AUTHZ] Deny-veto subject … UNREADABLE`.
2. **Publish size / CVE** — ~~skipped by run instruction~~ **CLOSED in r1**: measured by the verifier, recorded in §10
   (+0.01 MB, 212 / 212 files, no package change).
3. **G-1 / G-2 as a script (f1-v1c-v1, §16.7)** — `projects/unified-access-control-r2/notes/task-132-live-gate.ps1`.
   Every live write is behind `-Apply`; every result is checkable with `-Verify`; everything else is read-only. Run from
   the repository root of the branch being verified, signed in with `az login` as an operator (not a service principal).
   The script records the deploy start and the matter's original owner in
   `%TEMP%\task-132-live-gate.state.json` (or `-StatePath`). **The original owner is recorded ONCE per matter** (owner
   round 48 (d), §16.8): a second `ReassignMatter -Apply` before `RestoreMatter` keeps the first one and says so; a
   verified `RestoreMatter -Apply` marks it restored, after which the next reassign records afresh. That bookkeeping is
   tested with no live call: `Invoke-Pester projects/unified-access-control-r2/notes/task-132-live-gate.Tests.ps1`
   (7 / 7). Commands, in order:

   ```powershell
   $g = 'projects/unified-access-control-r2/notes/task-132-live-gate.ps1'
   # G-1 — deploy (the precondition and the settings are printed first; -Apply refuses if sprk_primarycontact is missing)
   & $g -Step Preflight
   & $g -Step Deploy                     # dry run: prints the exact Deploy-BffApi.ps1 command
   & $g -Step Deploy -Apply              # LIVE WRITE: deploys this branch's BFF to spaarke-bff-dev
   & $g -Step Deploy -Verify             # /healthz 200 and the site modified after the recorded start
   # G-2 (a) — a team-owned TEST matter the test user sees through their team; another team as the target
   & $g -Step ReassignMatter -MatterId <matter> -ToTeamId <team> -TestUserId <user>           # dry run
   & $g -Step ReassignMatter -MatterId <matter> -ToTeamId <team> -TestUserId <user> -Apply    # LIVE WRITE (record change)
   #   now poll the Teams/SPA matter list AS THE TEST USER; record the minute it disappears (bound: <= 4 min)
   & $g -Step ReassignMatter -MatterId <matter> -TestUserId <user> -Verify
   & $g -Step RestoreMatter -MatterId <matter> -Apply                                         # LIVE WRITE (restore)
   & $g -Step RestoreMatter -MatterId <matter> -Verify
   # G-2 (b) — by hand: the BU colleague loads the project list (warm cache); provision a secure TEST project from the
   #   wizard (or unsecure -> re-secure the dev secure test project); record the colleague's FIRST list response after
   #   provisioning returns (must not list it) and a direct read (must be denied). Then, read-only:
   & $g -Step VerifyProvision -ProjectId <project> -ColleagueUserId <colleague>
   # G-2 (c) — the ExternalAccess__ImpersonatedRootSets__Enabled line of Preflight (record it; run (b) with it on too if
   #   task 036 has merged)
   ```

   **Preflight, run read-only 2026-10-05 against dev:** `systemuser.sprk_primarycontact` PRESENT; `spaarke-bff-dev`
   `Redis__Enabled = true`; `Membership__CacheInvalidator__Enabled` and `ExternalAccess__ImpersonatedRootSets__Enabled`
   not set (the appsettings default applies; no code on this branch reads the latter — 036 is not merged here);
   `/healthz` 200.

## 13. Placement + justification (CLAUDE.md §10 / §11)

**Placement: in the BFF**, in place — existing caches, the existing shared access data source, the existing
invalidator. No new endpoint, service, interface, DI registration, option, job, column or package. The DI change
re-keys an EXISTING symmetric registration (§6.2).

- **Fault indicator on existing result types** (`ExternalGrantSet.Faulted`, `PersonIdentity.Faults` / `IdentityReadFaults`,
  `AccessSnapshot.Faulted`, `WorkforcePrincipal.ContactUnreadable`) — *Existing:* `ActiveOrgMemberships.Unreadable`,
  `ReferencedOrganizations.Unreadable`, `RootRecordFlags.Unreadable` (the pattern followed). *Extension:* yes — additive
  fields, no wrapper type; `PersonIdentity`'s flag is internal and not serialized, so `GET /api/users/me/memberships`
  is unchanged. *Cost of doing nothing:* a 429 cached as "no grants" for 60 s; a team-read fault hiding every
  team-owned matter for ~15 min; an unreadable contact skipping the ethical wall.
- **Eviction hook** (`InvalidateUserAccessAsync`, `InvalidateRecordOwnerChangeAsync`) — *Existing:*
  `IMembershipCacheInvalidator` + the subscriber's SCAN eviction; `IdentityNormalizationService.InvalidateAsync`
  (current-tenant only). *Extension:* yes — two members on the existing interface and its Null peer; no new service.
  *Cost of doing nothing:* a secured project stays visible to BU colleagues for the TTLs; a removed team member keeps
  team-owned records.
- `TenantCache.BuildKey` widened to `internal` (+ `AnyTenant`, `OnWire`), and the readers' id builders made `internal`
  — so patterns are built by the readers' own code (constraint "invalidation caller context").
- `DataverseAccessDataSource.GetDataverseTokenViaOBOAsync` → `internal virtual` (test seam, §5).
- Complexity (§11.5): `MembershipCacheInvalidator` grows to ~300 lines with one reason to change (cache invalidation);
  `DataverseAccessDataSource` grows by the classification only.

## 13a. Step 9.5 — code-review + adr-check

**Fixed:** SCAN page size 250 → 1,000 (`MembershipCacheInvalidator.ScanPageSize`: a quarter of the round trips on a
large keyspace); an unused parameter in the invalidation test world.

**Accepted / recorded (no change):**
- ⚠️ **A SYSTEMATIC systemuser-row read failure now denies the whole systemuser plane** (every candidate, every
  request) where before it degraded (no BU, no contact, membership via owner/team still composed). That is criterion
  16's fail-closed rule applied to an unknown veto subject. It depends on task 141's schema (`sprk_primarycontact`)
  existing — live in dev (141 G-1). A deploy to an environment missing that column would surface as Teams/SPA showing
  internal users nothing, logged as `[WF-AUTHZ] Deny-veto subject … UNREADABLE`.
- The per-entity membership eviction also drops CONTACT-plane membership entries for that entity type (their ids carry
  the entity segment too) — harmless, one re-read.
- Every provisioning evicts every user's project membership — at most the load of the 2-minute TTL expiring.
- The environment comparison in `RegistrationDataverseService.IsThisBffsEnvironment` is string-based (trimmed,
  case-insensitive); unknown on either side evicts.
- During a rolling deploy, old instances read pre-fix keys (identity v1, membership v4, `sdap:` snapshots) that the new
  instances neither read nor evict; each lives at most its old TTL (≤ 10 min) on the old instances only.

**ADR check:** ADR-001/052 (no Functions, no new background work) ✅ · ADR-002 (no plugins; outside-BFF changes
TTL-bounded) ✅ · ADR-003 (fail closed; data not decisions; faults not cached) ✅ · ADR-007/008 (no Graph types, no new
endpoint) ✅ · ADR-009 path C ✅ — eviction deletes tenant-scoped keys through `IConnectionMultiplexer` exactly as the
existing subscriber does; no `IDistributedCache` call, no non-tenant key written (⚠️ low-confidence note: the eviction
is a cross-tenant DELETE by design, because the writer that evicts does not share the reader's tenant) · ADR-010 (no new
interface; the extended one has real + Null + test spy) ✅ · ADR-013 (the membership types are not AI-internal — the
ArchTest forbids `IOpenAiClient` / `IPlaybookService`; `AccessibleRecordSetService` and `WorkforcePrincipalResolver`
already consume this namespace) ✅ · ADR-032 (symmetric, Null peer logs inert) ✅ · ADR-038 (no transport mock, no
path-A handler, no DI/ctor/reflection tests) ✅. **No violation.**

## 14. For the main session

- `.claude/constraints/auth.md:58-59` — **already corrected** (verified 2026-10-03: "a short explicit cross-request TTL
  is permitted (A1 retired 'per-request only')" and "MUST NOT cache a fault-derived result … task 132"). Nothing left.
- `.claude/adr/ADR-003-authorization-seams.md:71` ("already caches in `IDistributedCache` at 2 min / 60 s") is
  historical; optionally annotate that the 2-min roles/teams keys were removed and the snapshot keys moved onto
  `ITenantCache` by task 132 (sub-agents cannot write `.claude/`).
- `TASK-INDEX.md` → `✅` for 132 (not edited here, per run rules).
- Escalation trigger 7 (§8) → owner choice (a) / (b).
- GitHub: none existed for C12 at authoring; #1056 is this task's issue.

## 15. Verifier round r1 (2026-10-03, `task/uac-r2-132-r1` from `task/uac-r2-132` @ `4ed160714`)

Owner decisions re-read from `work/unified-access-control-r2` (rounds 1-9 + the #1081 peer report): none answers
escalation 7, so it stays a first-class stop (status `completed-with-escalation`, as task 137 carried its open
escalation). No live write was made.

| # | Verifier item | Outcome |
|---|---|---|
| 1, 2, 4, 9, 10, 12 | scope, suite re-runs, the 16 perturbations, verified-correct classifications, the DI defect, 109 residuals | Nothing to change. |
| 3 / 16 | publish size + CVE not recorded (criterion 20) | **Closed:** the verifier's measurement is in §10 and §12 (+0.01 MB, 212 / 212 files, Compress-Archive, no package change). |
| 5 | four seeds stayed green (S10, S11, S14, S15) | **Closed:** each now has a test and the seed reddens it — R2, R3, R6, R7 below. |
| 6 / 15 | caller cancellation during the junction read returned a faulted set (criterion 3) | **Closed:** the junction query rethrows the caller's cancellation (§2 r1); the false comment is rewritten (the catch, the method remarks, `ReadOrganizationMembershipsAsync`'s note). Task 109's W3 test asserted the opposite and is rewritten to the corrected contract (R1). |
| 7 | comments / docs contradicting the code | **Closed:** `MEMBERSHIP-RESOLUTION-GUIDE.md` :620 / :637 / :641 and `IMembershipResolverService.cs:25` (its claimed "Phase 2 extends TTL" exists nowhere in code and was dropped). The same sweep found and corrected five more this task had made false: `OrganizationMembershipResolver`'s header ("all failure modes fail soft" — not through the identity seam), `ExternalParticipationService`'s contact-state remarks ("10-minute identity cache"), `UserOrgContextReader` ×2 ("mirrors the identity cache's 10-minute TTL" — it stays 10, the identity cache is 2), `SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md:244` ("5-min/user" membership), and the task-109 note's two references to the rewritten W3 test. Not edited: `docs/adr/ADR-034-user-record-membership.md:89` (historical ADR text, criterion 11 keeps `docs/adr/` out of scope). |
| 8 | ambiguous re-owns evicted inconsistently | **Closed — rule: evict whenever the ownership PATCH may have applied.** `ProvisionProjectEndpoint` now evicts on `OwnerAssignmentOutcome.Failed` (a PATCH that timed out after Dataverse committed, or an accepted PATCH whose read-back failed); `NotApplied` (a read-back that SAW the old owner) evicts nothing. `UnsecureProjectEndpoint` also evicts when its PATCH throws (the same timeout-after-commit case), beside its existing "could not verify" eviction. Responses unchanged (500, same reason codes). |
| 11 | the unreadable-contact veto denied every candidate on EVERY entity type | **Closed:** the deny-every-candidate branch now applies only on the grant-supported root types (project / matter / work assignment) — the only types where the linked contact IS the veto subject. On any other type `grantContactId` stays null even for a KNOWN contact, so an unknown one cannot change the answer; denying on it refused what a known contact keeps. Fail-closed is unchanged wherever the contact is consulted. The deploy-order dependency (task 141's `sprk_primarycontact`) is now a G-1 precondition with a read-only check (§12). |
| 13 | bookkeeping | **Closed:** POML status `completed` → `completed-with-escalation` (escalation 7 open; G-1 / G-2 pending manual gates); the "17 perturbations" report vs 15 recorded is corrected in §11. |
| 14 | smallest fix set + optionals | (a) item 6, (b) item 3, (c) item 7 — all done; optionals done too: S10 / S11 / S14 tests and the provisioning `Failed` eviction (plus S15 and the unsecure PATCH-threw eviction, for consistency). |
| 17 | criterion 21 (manual live gate) | **Not closable from code** — G-2 after the G-1 dev deploy, unchanged (§12). |
| 18 | criterion 17 (PR part) | **Due at PR time** — the paragraph below is ready to paste into the PR description; the reviewer's approval is recorded there by the reviewer. |

### 15.1 r1 perturbations — each seeded in the production file, rebuilt, seen red, restored byte-identical (asserted) and touched

| # | Seed | Red |
|---|---|---|
| R1 | the junction query reports the caller's cancellation as `Failed` again (item 6) | **1** — `OrganizationMembershipReadTests.GetGrantSetAsync_CallerCancelsDuringTheJunctionRead_PropagatesTheCancellation_AndCachesNothing` |
| R2 | the identity seam back to `failSoft: true` (S10) | **1** — `Identity_AFaultOnEachSubPath_…(organizations-real-resolver)` |
| R3 | no eviction on unsecure's "could not verify" path (S11) | **1** — `ReOwn_AnAmbiguousOutcome_…(unsecure, read-back-failed)` |
| R4 | provisioning evicts on `NotApplied` instead of `Failed` (item 8) | **2** — `ReOwn_AnAmbiguousOutcome_…(provision, read-back-failed)` and `(provision, patch-timed-out-after-commit)` |
| R5 | no eviction when unsecure's PATCH throws (item 8) | **1** — `ReOwn_AnAmbiguousOutcome_…(unsecure, patch-timed-out-after-commit)` |
| R6 | a sentinel tenant (`?? "no-tenant"`) when `tid` is missing (S14) | **4** — `AccessCacheCharacterizationTests.ARequestWithoutATid_…` (document / record × no tid claim / no HttpContext) |
| R7 | an unreadable people-targeting app-user check treated as a non-fault (S15) | **1** — `Membership_PeopleTargeting_…(checkFaulted: True)` |
| R8 | the unreadable-contact veto on every entity type again (item 11) | **1** — `DenyVeto_SystemUserPlane_…(contact-unreadable, sprk_event)` |

### 15.2 r1 tests (ADR-038 KEEP paths; no transport mock, no DI / ctor / reflection test, no sleep)

- **Rewritten:** `OrganizationMembershipReadTests.GetGrantSetAsync_CallerCancelsDuringTheJunctionRead_KeepsTheDirectGrants`
  → `…_PropagatesTheCancellation_AndCachesNothing`: cancel fired once the junction request is IN FLIGHT (a
  `JunctionRequestSeen` signal on the fake, replacing a 300 ms timer that could fire during the grant query and pass
  for the wrong reason); asserts the OCE, no cache entry, and that the next request reads the direct grants (W3's real
  concern, kept).
- **Added cases:** `Identity_AFaultOnEachSubPath_…(organizations-real-resolver)` — the PRODUCTION
  `OrganizationMembershipResolver` over the substituted Dataverse, its own FetchXml failing;
  `Membership_PeopleTargeting_CachesOnlyAResponseBuiltOnAReadApplicationUserCheck` ×2 (fault / control);
  `DenyVeto_…` +2 on `sprk_event` (unreadable and known contact compose alike there);
  `ReOwn_AnAmbiguousOutcome_StillEvicts_AndTheFailureIsReportedAsBefore` ×4 (provision / unsecure × PATCH timed out
  after commit / read-back failed) over the real endpoints + production invalidator;
  `AccessCacheCharacterizationTests.ARequestWithoutATid_IsNeitherReadFromNorWrittenToTheCache` ×4.
- **Fixture:** `ProvisionProjectTestFixture` gains `OwnershipPatchTimesOutAfterApplying` and `OwnerReadBackFails`
  (default off, cleared by `Reset`).
- Beyond the closed set, justified: each added case is the verifier's named gap or the edge of an item-8 / item-11
  change; no happy-path variant was added.

**r1 results (2026-10-03):** build 0 warnings / 0 errors; affected + adjacent classes **470 / 0 / 0** after the seeds were restored; full BFF unit suite **14,332 passed / 0 failed / 54 skipped (14,386** = 14,373 + 13 new cases; 24 m 39 s, no contention failure this run); NetArchTest **346 / 0 / 0**; `Sprk.Bff.Api.IntegrationTests` **104 / 0 / 0**; `Spe.Integration.Tests` **403 passed / 0 failed / 25 skipped (428)**.

### 15.3 PR description paragraph for criterion 17 (paste at PR time)

> **ADR-009 tension (task 132, CLAUDE.md §6.5) — path C, pivot to comply.** `CachedAccessDataSource` used
> `IDistributedCache` directly with non-tenant `sdap:auth:*` keys and was not allow-listed in `SystemCacheKeys.cs`. Its
> two keys now go through `ITenantCache` under the caller's `tid`, subject-discriminated:
> `tenant:{tid}:auth-access:{authMode}:{oid}:{documentId}:v1` and
> `tenant:{tid}:auth-record-access:{entitySet}:{oid}:{recordId}:v1`. A request with no `tid` is not cached at all (no
> sentinel tenant, no allow-list entry) — pinned by `ARequestWithoutATid_IsNeitherReadFromNorWrittenToTheCache`.
> Escalation trigger 3 did not fire: every caller of the decorator runs inside an HTTP request with a validated Entra
> token (notes §5). `docs/architecture/caching-architecture.md` :64 and :98-99 match the code. Reviewer approval of
> path C: _(reviewer to record here)_.

### 15.4 r1 placement + justification (CLAUDE.md §10 / §11)

No new service, interface, DI registration, endpoint, option, job, column or package. Source changes are in existing
methods of existing BFF classes (one catch, two eviction calls on existing failure paths, one condition) plus
comments; test changes extend existing test classes and the existing fixture.

## 16. Batch 4 integration residuals (f1, 2026-10-04, `task/uac-r2-132-f1` from `integ/uac-r2-batch4` @ `6b685f0d4`)

Two residuals found when 132 was merged into batch 4. Owner/main-session rounds 1-25 re-read from
`work/unified-access-control-r2`: none speaks to either beyond the standing directive (round 15, "fix it the correct way");
round 10 item 3 ANSWERED escalation 7 (the degraded RPA-fallback answer stays uncached, as built), so the POML status
moves from `completed-with-escalation` to `completed` with only the live manual gates G-1 / G-2 (§12) outstanding.
No live write was made.

### 16.1 Residual 1 — share-only changes must evict like owner changes: CLOSED ~~, by construction~~ (corrected in round f1-v1c-v1, §16.8: the build enforces exactly what §16.8 lists, nothing beyond it)

**What a POA share stales (trace, code-read 2026-10-04).** (a) `ImpersonatedRootSetSource` — the set is an impersonated
Dataverse query, which sees POA shares; a TEAM share changes every member's set. (b) `CachedAccessDataSource` — both
snapshots are RetrievePrincipalAccess answers, which include POA: the record snapshot (`auth-record-access`) and, for a
`sprk_documents` record, the document snapshot (`auth-access`, keyed WITHOUT the set). Not stale: membership resolution
(FetchXml over lookup columns + ownership; no `principalobjectaccess` read anywhere in `MembershipResolverService`),
identity, and the external grant set (`sprk_externalrecordaccess` rows, task 137's own invalidation).

**Census of POA writers (Grep 2026-10-04): 8 files, 23 write sites.** InternalShareEndpoints 3 (share grant/modify,
unshare revoke) · ProvisionProjectEndpoint 7 (share-first creator grant, its restore grant/modify/revoke, the resume /
unverified-move creator grant, the colleagues' shares) · UnsecureProjectEndpoint 1 · SecureChildShareSynchronizer 3
(task 149's child fan-out: grant / modify / revoke) · AssignedAccessMaterializer 4 · NoAccessShareEnforcer 1 ·
DirectThreadAccessService 2 · PlaybookSharingService 2. The integration named four paths; four more write shares too
(the Assigned-To materializer's root shares; the No Access enforcer's revoke — an over-grant if it is not evicted;
Direct-thread and playbook sharing).

**Decision — evict in the ONE POA seam, not at each writer.** Every one of the 23 sites goes through
`IDataverseRecordShareService` (task 060's consolidation), so `DataverseRecordShareService` now calls the new
`IMembershipCacheInvalidator.InvalidateRecordShareChangeAsync(entitySet, recordId, …)` after every
`GrantAccessAsync` / `ModifyAccessAsync` / `RevokeAccessAsync` — in a `finally` (a write that throws or is cancelled
may have committed), with `CancellationToken.None`, never throwing (the hook does not throw; a defect that made it
throw is caught and logged; the write's own return or exception is what the caller sees). Wiring each of 23 sites
would leave the next writer to remember; the seam makes it impossible to forget, and
`PoaShareClientSingletonGuardTests` fails the build on every route around the seam that exists in compiled code — an IL
scan of the BFF's assemblies (C1-C5 in §16.6: any reference to the client's POA writes outside the seam whatever the
receiver, the SDK messages, a POA action or write name loaded as a constant, and pins on the client's and the seam's write
methods) plus text rules for breadth; only a name the code computes or reads at run time is beyond it. *Corrected in f1-v1:
as first committed in f1 this sentence read "fails the build on a POA write that bypasses the seam", which the verifier
disproved — the f1 detector only saw a bare-identifier receiver (seeds S5 / S6 stayed green). §16.6.* *Strengthened in
f1-v1c-v1 (§16.7): f1-v1's C3 pinned method NAMES and its behaviour proof ran only a user principal (seeds N1 / N2
passed); C3 now proves the eviction on EVERY path through the seam's writes, and C6 / T5 read metadata, constant data and
configuration.* *Corrected in f1-v1c-v1 (§16.8): "only a name the code computes or reads at run time is beyond it" was
false — verifier seed U (an `[UnsafeAccessor]` extern that IS the client's write: compiled, no string, nothing computed)
and seed R (the write chosen by reflection on its SIGNATURE, no name at all) passed every rule. Both mechanisms are now
banned (C8 / T6, C7), and the claim is restated as exactly what the build enforces.*
`InternalShareEndpoints`' existing per-user `ImpersonatedRootSetSource.InvalidateAsync` is kept: it
is what works where the invalidator is the Null peer (in-memory cache, Development/Testing).

| Named path | Write sites → seam | Evicted after the write |
|---|---|---|
| InternalShareEndpoints share / unshare | `recordShare.GrantAccessAsync` / `ModifyAccessAsync` / `RevokeAccessAsync` (root) | every user's root set for the root type; every user's snapshot of the root |
| Provisioning share-first creator grant and its restore | `EnsureCreatorShareAsync` grant/modify, `RestoreCreatorShareAsync` revoke/grant/modify | same, for the record |
| Resume creator-share error paths | the unverified-move unconfirmed grant (and every grant / restore above on the resume flow) | same — including when the grant throws |
| SecureChildShareSynchronizer fan-out (task 149) | `_owner._recordShare.Grant/Modify/RevokeAccessAsync` per child | that child's snapshots (record-scoped; plus document-scoped for `sprk_documents`); no root-set pattern (a child is not a root) |

**Patterns** (`MembershipCacheInvalidator.RecordShareChangePatterns`, built from the readers' own builders): the root-set
pattern for the root type when the set is a root's (`ImpersonatedRootSetSource.TryGetEntityTypeForSet` — the seam knows
only the set; per TYPE, all users, because a team share reaches every member); the record-snapshot pattern; and for
`sprk_documents` the document-snapshot pattern. **Document id normalised:** the document snapshot key took the route
text verbatim (casing / braces vary), so no pattern could be sure to reach it — `CachedAccessDataSource.DocumentIdSegment`
now writes the `D` format and `CacheVersion` 1 → 2 so no unnormalised v1 entry is served after deploy. The owner-change
hook gained the document pattern too (an owner change on a document stales the same key).

**Cost.** One SCAN per pattern per write: a root share 2, a child share 1 (2 for a document), a thread / playbook share
1. A fan-out of N children costs N–2N SCANs over a keyspace measured ≤ 178 keys in dev (§6.5: one round trip each).
Escalation trigger 8 does not fire.

### 16.2 Residual 2 — child-restore evictions scanned for nothing: CLOSED, and the premise corrected

**Premise checked.** For the root-set pattern it was exactly right (`ImpersonatedRootSetSource` refuses any non-root type
before touching the cache). For the other two it was right only by observation: GET
`/api/users/me/memberships/{entityType}` accepts ANY entity, and the generic discovery includes `ownerid`, so a
membership entry for `sharepointdocumentlocation` could be written (and the forward Assign cascade, which no one evicts,
would leave it stale); and nothing stopped a record-snapshot caller from passing `sharepointdocumentlocations`.

**Fix — make "nothing to evict" true by construction, then evict only what can exist.** ONE declaration beside the
cascade's own table list: `AssignCascadeChildOwners.IsReownedByCascade` / `IsReownedByCascadeEntitySet` (the owner-bearing
tables of `TablesFor`; a future cascading relationship is covered the moment it is listed). Readers:
`MembershipResolverService.CachesEntityType` (both planes — systemuser and contact — never read or write the cache for
such a type), `CachedAccessDataSource.CachesRecordEntitySet` (the record snapshot reads live). The hook builds each
pattern only when its cache can hold the type (`MembershipResolverService.CachesEntityType`,
`ImpersonatedRootSetSource.CachesEntityType`, `CachedAccessDataSource.CachesRecordEntitySet` / `IsDocumentEntitySet`), and
`EvictAsync` returns before touching Redis when there is no pattern. A child restore now builds 0 patterns, 0 SCANs; a
root's owner change still builds its 3. `ProvisionProjectEndpoint.EvictRestoredChildrenAsync` keeps calling the hook (the
HOOK decides what an owner change can have made stale). Side benefit: the forward cascade's silent child re-owns can no
longer leave a stale entry anywhere.

### 16.3 Tests (KEEP paths; no transport mock, no DI / ctor / reflection test, no sleep)

`tests/integration/auth/UnifiedAccessControl/AccessCacheInvalidationTests.Shares.cs` (the class made `partial`; its
`AccessCacheWorld` gains a cascade-child discovery entry, an inner-read counter and a document-snapshot warmer):
- `ShareChangeEviction_RemovesEveryUsersRootSetForTheType_AndEverySnapshotOfTheRecord_Only` — criterion 23 for the new
  hook: every pattern reaches a key a production reader wrote; identity / membership / matter / other-record /
  other-set keys stay.
- `ShareChangeEviction_OnADocument_RemovesItsDocumentAndRecordSnapshots_HoweverTheIdWasSpelled` — upper-case and braced
  ids; no root-set pattern scanned.
- `PoaSeam_EveryShareWrite_EvictsThatRecord_AndTheWritesOwnOutcomeStands` ×6 (grant / modify / revoke × Dataverse 204 /
  500) — the PRODUCTION seam over the PRODUCTION `DataverseWebApiService` against an in-memory Web API server (ADR-038
  §7's named replacement for B1); a refused write still throws, after the eviction.
- `PoaSeam_WhenTheCallerCancels_TheEvictionStillRuns_AndTheCancellationPropagates`.
- `PoaSeam_WhenEvictionFails_TheShareWriteSucceeds_AndTheFailureIsLogged` (f1; it exercised the invalidator's own
  catch, not the seam's — f1-v1 added `PoaSeam_WhenEvictionThrows_…` ×6 for the seam's catch and renamed this case
  `PoaSeam_WhenRedisFails_TheInvalidatorLogsAndReturns_…`, §16.6).
- `CascadeChildOwnerChange_BuildsNoPattern_AndScansNothing_WhileARootStillScans` ×2.
- `CascadeChildTables_AreNeverCached_SoNeitherTheCascadeNorItsRestoreCanLeaveOneStale` (both membership planes + the
  record snapshot; a root snapshot still cached as the control).

`tests/Spaarke.ArchTests/PoaShareClientSingletonGuardTests.cs`: `EveryPoaShareWriteGoesThroughTheEvictingSeam`;
`EachNamedShareWriterWritesOnlyThroughTheSeam` ×8 (one per writer file — the per-path pin: at least one write visible,
every one through the seam); `NoServerFileSendsSdkPoaMessages`; `Detector_FlagsAWriteOnTheConcreteClient_AndPassesAWriteThroughTheSeam`
(negative + positive controls). Test doubles of the extended interface (`SpyMembershipCacheInvalidator`,
`ProvisionAssignCascadeChildOwnerTests.RecordingInvalidator`) gain the new member.

Beyond the closed set, justified: the cancellation and eviction-failure cases pin the two properties the integration
named ("CancellationToken.None, never fails the request"); the six seam cases are one per write method per outcome
because each method has its own `finally` (a seed that moves one out of its `finally` reddens only that method's refused
case).

### 16.4 Perturbations — each seeded in the production file, rebuilt, seen red, restored byte-identical (asserted) and touched

Seeded by a script (`git checkout` restore, `git diff --quiet` asserted per seed, file touched), run against
`AccessCacheInvalidationTests` (behaviour seeds) or `PoaShareClientSingletonGuardTests` (bypass seeds).

| # | Seed (production file) | Red |
|---|---|---|
| F1 | the seam's GRANT no longer evicts (`IDataverseRecordShareService.cs`) | **3** — `PoaSeam_EveryShareWrite_…(grant, ×2)`, `PoaSeam_WhenEvictionFails_…` |
| F2 | MODIFY evicts only after a successful write (moved out of its `finally`) | **1** — `PoaSeam_EveryShareWrite_…(modify, refused)` |
| F3 | REVOKE skips the eviction when the caller's token is cancelled | **1** — `PoaSeam_WhenTheCallerCancels_…` |
| F4 | the share-change patterns omit the root set (`MembershipCacheInvalidator.cs`) | **7** — all six `PoaSeam_EveryShareWrite_…` + `ShareChangeEviction_RemovesEveryUsersRootSet…` |
| F5 | no document-snapshot pattern | **1** — `ShareChangeEviction_OnADocument_…` |
| F6 | the document id written verbatim (`CachedAccessDataSource.DocumentIdSegment`) | **1** — `ShareChangeEviction_OnADocument_…` |
| F7 | the membership resolver caches every type (`CachesEntityType => true`) | **3** — `CascadeChildOwnerChange_…` ×2, `CascadeChildTables_AreNeverCached_…` |
| F8 | the record snapshot caches a cascade-child set (bypass removed) | **1** — `CascadeChildTables_AreNeverCached_…` |
| F9 | the root-set source claims every type (`CachesEntityType => true`) | **2** — `CascadeChildOwnerChange_…` ×2 |
| F10 | the CONTACT plane caches a cascade-child type (its gate removed) | **1** — `CascadeChildTables_AreNeverCached_…` |
| F11 | a compiling POA revoke on a `DataverseWebApiService` added to `InternalShareEndpoints.cs` | **2** — `EveryPoaShareWriteGoesThroughTheEvictingSeam`, `EachNamedShareWriterWritesOnlyThroughTheSeam(InternalShareEndpoints)` |
| F12 | the same bypass in `ProvisionProjectEndpoint.cs` (creator share / restore / resume paths) | **2** — the global rule + the provisioning case |
| F13 | the same bypass in `SecureChildShareSynchronizer.cs` (task 149's fan-out) | **2** — the global rule + the synchronizer case |
| F14 | the same bypass in `UnsecureProjectEndpoint.cs` | **2** — the global rule + the unsecure case |

All 14 restored byte-identical (asserted) and touched. Nothing stayed green.

### 16.5 Placement + justification (CLAUDE.md §10 / §11)

Placement: in the BFF, in place — the existing POA seam, the existing invalidator interface and its two
implementations, the existing caches. No new service, class, endpoint, option, job, column, PCF or package; no new DI
registration (`DataverseRecordShareService`'s existing registration resolves its two added singleton dependencies).
No Dataverse plugin (ADR-002); fail closed unchanged (ADR-003 — eviction only ever removes cached data).

Three-question justification for the one new member, `IMembershipCacheInvalidator.InvalidateRecordShareChangeAsync`:
(1) **Existing** — `InvalidateRecordOwnerChangeAsync` (owner changes) and `ImpersonatedRootSetSource.InvalidateAsync`
(per user, the CALLER's tenant only, called by InternalShareEndpoints only). (2) **Extension** — a member on the existing
interface + Null peer + real implementation; reusing the owner hook would also wipe every user's membership entries for
the type on every share although a share is not a membership term (needless Dataverse re-reads); extending
`ImpersonatedRootSetSource.InvalidateAsync` cannot reach the snapshots or other tenants. (3) **Cost of doing nothing** —
an unshare (or a No Access enforcer revoke) leaves the removed user's cached root set granting the record for up to
2 min and their snapshot for 60 s (over-grant); a new share leaves the sharee denied for the same windows; a child
fan-out leaves every child's snapshot stale. The new `internal` predicates (`IsReownedByCascade*`, `CachesEntityType`,
`CachesRecordEntitySet`, `IsDocumentEntitySet`, `TryGetEntityTypeForSet`, `DocumentIdSegment`) are members of the
existing classes whose rule they state; `DataverseAccessDataSource.DocumentEntitySetName` became `public` (additive;
Spaarke.Dataverse has no consumer outside the BFF — §5).

`.claude/**`: no edit needed.

### 16.5a Results

**f1 results (2026-10-04)**, after every seed was restored: build 0 warnings / 0 errors; affected classes first
(`AccessCacheInvalidationTests` 26 / 0 / 0 — 13 existing + 13 new; `PoaShareClientSingletonGuardTests` 14 / 0 / 0 — 3
existing + 11 new; the wider affected set incl. `ProvisionAssignCascadeChildOwnerTests`, `InternalUserShareTests`,
`DataverseRecordShareWireTests`, `ImpersonatedRootSetSourceTests`, `MembershipCacheInvalidatorTests`,
`SecureChildShare*`, `MembershipResolver*` 395 / 0 / 0 before the new tests); then once, in full: BFF unit suite
**15,430 passed / 0 failed / 54 skipped (15,484; 20 m 52 s)**; NetArchTest **606 / 0 / 0**;
`Sprk.Bff.Api.IntegrationTests` **104 / 0 / 0**; `Spe.Integration.Tests` **403 passed / 0 failed / 25 skipped (428)**.
No contention failure this run. No package change (no publish-size or CVE delta).

### 16.6 Verifier round f1-v1 (2026-10-04; started on `task/uac-r2-132-f1-v1` from `task/uac-r2-132-f1` @ `835b57472`, finished on `task/uac-r2-132-f1-v1c` from `wip/uac-r2-132-f1-v1-restart` @ `8f6a39d94`)

**The restart.** The first agent on this round was stopped mid-way by a machine restart; its partial work was saved
unverified as `8f6a39d94` ("WIP … saved before a machine restart"). Resumed on `task/uac-r2-132-f1-v1c`: the WIP was read
in full and kept — it built (0 warnings / 0 errors), its 18 guard tests and the 49 affected unit tests passed, and its
rework of the guard was sound — then the guard was extended where the WIP still left a route open (C2-C5 below, the
`ldtoken` read, the BFF-closure scan set, and the client's own exemption removed), and **every seed in the table below
was re-run against the FINAL code** (the WIP's seed table had not been verified).

**Verifier items (f1-v1 numbering).** 1 (share-only changes evict), 2 (no scan for a type no cache holds), 6 (behaviour
seeds), 7 (suites) and 8 (other checks) were met. **4 (blocking) and 10** — the build guard's false negative, and the
comments that claimed it had none — **CLOSED** below. **5 (minor)** — the seam's own catch untested — **CLOSED** below.
9 is an observation; 11 and 12 are the live gates and the PR-time approval (unchanged, end of this section).

**Items 4 / 10 — the guard: CLOSED.** The f1 text detector matched only `identifier.XAccessAsync(` and checked the
identifier's declaration file-wide, so verifier seed **S5** (`sp.GetRequiredService<DataverseWebApiService>()
.RevokeAccessAsync(…)` in `NoAccessShareEnforcer.cs`) and **S6** (a `DataverseWebApiService _recordShare` parameter in a
file that also declares `IDataverseRecordShareService _recordShare`) left all 14 guard tests green. The fix is a
COMPILED guard — the compiler has already resolved every receiver's type — plus a stricter text rule for breadth. In
`tests/Spaarke.ArchTests/PoaShareClientSingletonGuardTests.cs` and one new test helper,
`tests/Spaarke.ArchTests/IlCallScan.cs` (reads a type's IL: the method tokens of `call` / `callvirt` / `newobj` /
`ldftn` / `ldvirtftn` / `jmp`, an `ldtoken` of a method, and `ldstr` constants; closures and state machines attributed to
their outermost type; an async / iterator state machine mapped back to its source method; fails loud on an undecodable
stream or an unresolvable token):

| # | Route around the seam | Rule (test) | Precision |
|---|---|---|---|
| — | Which assemblies the compiled rules must read | `EveryBffOrClientReachingProjectIsScanned` — derived from the csproj graph: every `src/**` project in `Sprk.Bff.Api`'s ProjectReference closure (the process whose caches a share stales) plus every project that reaches `Spaarke.Dataverse` (project, binary or package reference); each must load, or the build fails. Today: Spaarke.Dataverse, Spaarke.Core, Spaarke.Scheduling, Sprk.Bff.Api | exact over `src/**` |
| C1 | A reference to the client's `Grant/Modify/RevokeAccessAsync` from ANY type but the seam — the client itself included — through any receiver expression (S5), a cast, an indexer, inside a lambda / local function / async method, as a method group, or as an expression tree's method handle | `EveryCompiledReferenceToTheClientsPoaWritesIsTheSeams` (a target counts if declared on the client, a subclass, or a base type / interface it implements; vacuity: it must see the seam's own three calls) | exact |
| C2 | A FOURTH POA write on the client, which C1 would not know by name | `TheClientsPoaWritesAreExactlyTheThreeTheGuardNames` — the client methods that load a POA action name are exactly the three | exact |
| C3 | A write ON the seam that does not evict | ~~`TheSeamWritesPoaOnlyFromItsThreeEvictingMethods` — the seam's references to the client's writes come only from its own three same-named methods, the three the `PoaSeam_*` behaviour tests prove evict~~ | ~~exact~~ **Corrected in f1-v1c-v1 (§16.7):** it compared method NAMES and the behaviour cases ran only a user principal, so verifier seeds N1 (a team-only early return inside `GrantAccessAsync`) and N2 (a same-name overload) passed. Replaced by C3 (a) the interface-map pin, (b) the every-path analysis of each write, (c) the every-path analysis of the eviction helper. |
| C4 | A POA action name loaded as a string constant outside the client (an SDK `OrganizationRequest` by name, a hand-built POST or `$batch` line, a name the compiler folded from constant pieces) and a POA write method's name loaded as a constant (reflection, `nameof`, a `dynamic` call's binder name) | `NoCompiledCodeOutsideTheClientNamesAPoaActionOrWrite` — action names matched as whole words (`\b(Grant\|Modify\|Revoke)Access\b`: conservative — a log line naming an action as a word is flagged too); method names exact; vacuity: it must see the client's three action loads | exact over IL constants |
| C5 | The SDK's `GrantAccessRequest` / `ModifyAccessRequest` / `RevokeAccessRequest`, however imported (a global using defeats T2) | `NoCompiledCodeUsesTheSdkPoaMessages` — by FULL type name (`typeof`-checked; the BFF's own DTOs share the short names) | exact |
| T1 | A `.XAccessAsync(` call in any `src/server` file whose receiver is not an identifier declared there ONLY as `IDataverseRecordShareService`: an expression receiver (`…)`, `…>`, `…]` — S5), a `var` / `dynamic` / undeclared name, a name also declared as another type (S6) | `EveryPoaShareWriteGoesThroughTheEvictingSeam` + `EachNamedShareWriterWritesOnlyThroughTheSeam` ×8 | conservative (may flag a legal call; C1 is the precise rule) |
| T2-T4 | The SDK messages; a second POA payload; a write method named as a string | `NoServerFileSendsSdkPoaMessages`; task 060's `PoaActionPayloadsAreBuiltInExactlyOnePlace`; `NoServerFileNamesTheClientsPoaWritesAsAString` | text |

**Out of reach, and now SAID so instead of claimed:** a method name or action URL the code computes or reads at RUN time
(built from non-constant pieces, or read from configuration or attribute metadata), then invoked by reflection or a raw
HTTP call. No static scan sees a value that does not exist until the code runs; that is review's to catch.
*Corrected in f1-v1c-v1 (§16.8): this disclosure was itself incomplete — a compiled route with no name at all (seed U,
`[UnsafeAccessor]`) and a reflective route with no name at all (seed R, selection by signature) were neither caught nor
disclosed. Both mechanisms are now banned; see §16.8 for exactly what the build enforces.*

**Controls.** `CompiledDetector_FlagsEveryBypassShape_AndPassesTheSeam` scans never-executed control types, one bypass
shape per type: the S5 inline-resolved receiver, an async lambda, a method group, an expression tree (C1); the SDK message
(C5); a constant-folded action name, an action URL, a `$batch` line, a `nameof`, a `dynamic` call (C4), with
`GrantAccessRequest` / `RevokeAccessAsyncHandler` / `ModifyAccessible` as non-matches; the source-method mapping the pins
use (a lambda does not borrow its enclosing method's name; an async method's state machine maps back); and the sanctioned
shape (all three writes through `IDataverseRecordShareService`) flagged by none. `Detector_…` (text) gains the S5 shape,
`(dv).`, `GetClient().`, `shares[0].`, S6, a `var` and a `dynamic` local and an undeclared receiver as negatives, and `x?.`
as a positive.

**Comments corrected** — each had claimed the build fails on ANY bypassing write: `IDataverseRecordShareService.cs`
(interface remarks point at the class; the class remarks list exactly what the compiled and text rules reject and what
no static guard can see), `IMembershipCacheInvalidator.InvalidateRecordShareChangeAsync` remarks, §16.1 above, the POML
f1 outcome (correction appended; the original wording kept for the record), the guard's section header, and the
`AccessCacheInvalidationTests.Shares.cs` summary.

**Item 5 — the seam's own catch was untested: CLOSED.** `PoaSeam_WhenEvictionFails_TheShareWriteSucceeds_AndTheFailureIsLogged`
asserted the INVALIDATOR's warning (the production invalidator never throws, so the seam's catch never ran). Now
`PoaSeam_WhenEvictionThrows_TheSeamCatchesAndLogs_AndTheWritesOwnOutcomeStands` ×6 (grant / modify / revoke × Dataverse
204 / 500): a `ThrowingInvalidator` double (every hook throws) under the production seam over the production client — a
successful write completes; a refused write surfaces ITS `HttpRequestException`, not the double's exception; the eviction
was attempted exactly once; the seam's own `[ACCESS-EVICT] The share-change eviction for … ({write})` warning is logged.
The old case keeps its assertions under its true name,
`PoaSeam_WhenRedisFails_TheInvalidatorLogsAndReturns_AndTheShareWriteSucceeds` (plus: the seam logged nothing — it had
nothing to catch).

**Seeds — all run against the final code by a script: seeded in the production file, rebuilt, seen red, restored by
`git checkout`, `git diff --quiet` asserted, touched; new files / directories removed.**

| # | Seed | Red | What it proves |
|---|---|---|---|
| S5 | verifier's `sp.GetRequiredService<Spaarke.Dataverse.DataverseWebApiService>().RevokeAccessAsync(…)` in `NoAccessShareEnforcer.cs` | **3** — C1, T1, `EachNamedShareWriter…(NoAccessShareEnforcer)` | the blocking finding |
| S6 | verifier's `DataverseWebApiService _recordShare` parameter calling `RevokeAccessAsync` in the same file | **3** — C1, T1, per-writer | the file-wide name check |
| S7 | a method group `=> client.RevokeAccessAsync;` on the client (no `(` for any text rule) | **1** — C1 only | the IL rule is needed, not just stricter |
| S8 | `typeof(DataverseWebApiService).GetMethod("RevokeAccessAsync")!.Invoke(…)` | **2** — C4, T4 | reflection by name |
| S9 | a new `src/server/shared/SeedProbe/SeedProbe.csproj` referencing only `Spaarke.Core` (reaches the client transitively), not loadable by the ArchTests | **4** — the scan-set rule (FileNotFoundException named) + C1, C4, C5 (cannot load the set) | a project outside the scan cannot pass unseen |
| S9b | a new project with NO reference to `Spaarke.Dataverse`, referenced by `Sprk.Bff.Api.csproj`, folding `"Grant" + "Access"` | **1** — C4 (`SeedProbe2.Probe: "GrantAccess"`) | the BFF-closure branch of the scan set |
| S10 | the seam's catch narrowed to `catch (Exception ex) when (ex is null)` (the verifier's item-5 seed) | **6** — every `PoaSeam_WhenEvictionThrows_…` case | item 5 |
| S11 | the seam's catch kept, its `LogWarning` removed | **6** — every `PoaSeam_WhenEvictionThrows_…` case | the log assertion bites on its own |
| S12 | a fourth client write, `ShareWithTeamAsync`, posting `GrantAccess` | **2** — C2, and C4's vacuity check | C2 |
| S13 | a client method `EnsureReadShareAsync` that wraps `GrantAccessAsync` | **1** — C1 | the client's own exemption is gone |
| S14 | `GrantWithoutEvictionAsync` on the seam calling `_dataverse.GrantAccessAsync` with no eviction | **1** — C3 | C3 |
| S15 | `global using Microsoft.Crm.Sdk.Messages;` in Spaarke.Dataverse + `new ModifyAccessRequest()` in another file | **1** — C5 only (T2 stays green: neither file has both the namespace and the message name) | C5 is needed |
| S16 | `const string SeedAct = "Revoke"; … SeedAct + "Access"` in `NoAccessShareEnforcer.cs` | **1** — C4 only (task 060's payload rule stays green: no quoted action name) | C4 is needed |
| S17 | an expression tree `() => c.RevokeAccessAsync(…)` over the client in `NoAccessShareEnforcer.cs` | **3** — C1 (by `ldtoken`: an expression tree has no call instruction), T1, per-writer | the `ldtoken` read |

(A first S15 attempt put the global using in the BFF; the BFF does not compile with it — `GrantAccessRequest`,
`RevokeAccessRequest` and `AccessRights` become ambiguous — so that shape cannot ship there; the Spaarke.Dataverse shape
can, and is what C5 exists for.)

**Not code (unchanged).** Item 9 — share writes cost 1-2 keyspace SCANs each; task 149's fan-out and the round-28
2-minute reconcile can issue them in bursts; trigger 8 was evaluated against the dev keyspace (≤ 178 keys) and does not
fire; watch a large production keyspace after deploy (an observation, not a gate). Items 11-12: G-1 / G-2 (§12) and the
criterion-17 ADR-009 path C reviewer approval at PR time (paragraph drafted in §15.3) — unchanged.

**Placement / justification (CLAUDE.md §10 / §11).** No production surface added: the production edits are comments only
(`IDataverseRecordShareService.cs`, `IMembershipCacheInvalidator.cs`). One new TEST helper, `IlCallScan`: (1) existing —
`SourceScan` (text) and NetArchTest (type-level dependencies: it cannot say which METHOD a type calls, read a string
constant, or attribute a closure to its owner); (2) extension — `SourceScan` cannot know a receiver's type (S5 / S6 are
exactly that), and NetArchTest's `HaveDependencyOn` would flag every type that merely holds a `DataverseWebApiService`
(dozens, legitimately); (3) cost of doing nothing — a share write through any non-identifier receiver, lambda, method
group, expression tree, SDK message or constant-named action ships without the access-cache eviction (an unshare that
keeps access for up to 2 min) and the build stays green. Inbox reflection only; no package (no publish-size or CVE
delta). No plugin (ADR-002); fail closed unchanged (ADR-003).

**Results (f1-v1c, 2026-10-04), after every seed was restored.** Build: BFF, ArchTests, BFF unit tests and
`Sprk.Bff.Api.IntegrationTests` 0 warnings / 0 errors; `Spe.Integration.Tests` 0 errors and 5 CA2024 warnings, all in
`AnalysisEndpointsIntegrationTests.cs` (untouched by this task, pre-existing). Affected first:
`PoaShareClientSingletonGuardTests` **22 / 0 / 0** (14 before the round + 8 new); `AccessCacheInvalidationTests` +
`DataverseRecordShare*` **49 / 0 / 0** (43 + 6 new). Then once, in full: BFF unit suite **15,436 passed / 0 failed /
54 skipped (15,490; 25 m 33 s)**; NetArchTest **614 / 0 / 0** (606 + 8; re-run after the last edit, a failure-message
wording change in the guard); `Sprk.Bff.Api.IntegrationTests` **104 / 0 / 0**; `Spe.Integration.Tests` **403 passed /
0 failed / 25 skipped (428)**. No contention failure. No package change (no publish-size or CVE delta).

`.claude/**`: no edit needed.

### 16.7 Verifier round f1-v1c (2026-10-05; `task/uac-r2-132-f1-v1c-v1` from `task/uac-r2-132-f1-v1c` @ `137b088f5`)

Owner and main-session rounds 1-43 re-read from `work/unified-access-control-r2`: none speaks to this guard beyond round
15's standing directive ("fix it the correct way; never defer"). No `NOTE-FROM-MAIN.md`. No live write; the only live
call was the new gate script's read-only `Preflight` (below).

**Verifier items (f1-v1c numbering).** 1 (the verifier's own read-only run), 2 (the restart WIP), 3 (items 2, 3, 6, 7, 8
of f1-v1), 4 (S5, routes AROUND the seam) and 5 (the seam's catch) were met; 8 (suites) and 9 (SCAN cost, an
observation) need no change — the suites are re-run below. **6 (blocking) and 10 — C3's false negatives and the wording
that denied them — CLOSED.** **7 (observation) — CLOSED** (hardened, not only disclosed). **11** — the live gates now ship
as a dry-run / `-Apply` / `-Verify` script with the exact commands; the gates themselves stay the main session's.
**12** — the criterion-17 reviewer approval is PR-time (unchanged).

**Items 6 / 10 — what was wrong.** f1-v1's C3 (`TheSeamWritesPoaOnlyFromItsThreeEvictingMethods`) compared source-method
NAMES (`"RevokeAccessAsync → RevokeAccessAsync"`), and the behaviour cases that were said to prove "each of the three
evicts" ran only `DataversePrincipalRef.User`. So it proved: *the seam calls the client's writes from methods named like
its three writes, and the user path through each evicts.* The verifier's seeds showed both gaps: **N1**, a team-only early
return in the seam's `GrantAccessAsync` before its `try` (a team share — what `PlaybookSharingService` writes — with no
eviction), left all 22 guard tests and all 49 behaviour tests green; **N2**, a same-name non-evicting `RevokeAccessAsync`
overload on the seam, left all 22 guard tests green. §16.6's C3 row, the guard header and the class remarks claimed
"exact" for what was neither.

**Items 6 / 10 — the fix: C3 proves the eviction on every path, structurally, and the behaviour cases cover every
principal kind.**

| # | Rule (test) | What it proves |
|---|---|---|
| C3 (a) | `TheSeamWritesPoaOnlyFromItsThreeInterfaceMethods` | The seam references the client's POA writes only from the three methods `typeof(DataverseRecordShareService).GetInterfaceMap(typeof(IDataverseRecordShareService))` binds to the interface's Grant/Modify/RevokeAccessAsync — compared by METADATA identity (`HasSameMetadataDefinitionAs`), so a same-name overload (N2), a helper, a local function or a lambda that writes is outside them — and each calls its own same-named write. |
| C3 (b) | `EveryPathThroughEachSeamWriteAwaitsItThenEvictsThatRecord` ×3 | For each of the three, an IL path analysis of the compiled body (new test helper `tests/Spaarke.ArchTests/IlPathScan.cs`) shows that EVERY path that makes the client write — returned, thrown, cancelled, suspended and resumed, through every `finally` — first awaits it (the write's own task completes), then calls the seam's eviction (C3 (c)) with the SAME `entitySetName` and `recordId` (the hoisted parameters, never re-assigned on the path), then awaits that, before the method completes. N1 is a path it rejects. |
| C3 (c) | `TheSeamsWritesCallAnEvictionThatInvalidatesThatRecordOnEveryPath` | What counts as the eviction is FOUND IN THE IL, not named (no string-bound reflection into the private helper — ADR-038 B8's concern): a seam method the three writes call that, on every path from entry, calls `IMembershipCacheInvalidator.InvalidateRecordShareChangeAsync` with its own `entitySetName` and `recordId` and awaits it (its catch then logs); one must exist. Today that is `EvictAfterShareWriteAsync`. A renamed helper is followed; a helper with a path that skips the invalidation is no eviction, so (b) fails for every write as well. |
| behaviour | `PoaSeam_EveryShareWrite_…` ×12, `PoaSeam_WhenEvictionThrows_…` ×12, `PoaSeam_WhenTheCallerCancels_…` ×6 | What the eviction DOES — the record's root sets and snapshots gone — for every write × every `DataversePrincipalKind` (derived from the enum: a new kind gets its cases) × Dataverse accepting / refusing, and cancellation for every write × kind; the in-memory Web API now records each request body and the case asserts it carried the principal's kind (`teams(` / `systemusers(`), so a "team" case cannot silently run as a user one. |

**The path analysis (`IlPathScan`)** is an abstract interpretation of one compiled body, explored to a fixed point. An
async method is analysed as the compiler built it — the state machine's `MoveNext`, whose state field and dispatch local
are tracked as constants so every dispatch branch is decided exactly; a `ret` in a non-negative state is a suspension
(resumed at entry in that state, keeping the state machine's fields — the parked awaiter included — and the obligation);
in state -1 / -2 it is completion; a `ret` whose state it lost is reported, not guessed. Exceptions leave every
instruction that can raise one (every call except the await plumbing, which fails only by running out of memory;
`throw`, `rethrow`; checked conversions, casts, array access, division; static fields; a field reached through anything
but `this` or the state machine's owner) to each enclosing handler, innermost first; `catch (object)` / `catch
(Exception)` stop the search, a filter is assumed to accept and to decline, a `finally` / `fault` runs and the search
continues after it; `leave` runs every `finally` it crosses. The obligation moves None → Issued (the write returned its
task) → Owed (that task's `GetResult` returned or threw; or the write call itself threw) → Discharging (the eviction was
called with equal keys) → None (the eviction's `GetResult` returned or threw); awaiters are followed through
`ConfigureAwait`, `GetAwaiter`, locals, the awaiter fields and suspension. An eviction while Issued, an eviction with
other keys, a second write while one is owed, and a completion in any phase but None are violations. It fails loud on an
unrecognised state-machine prologue, an `endfinally` it did not enter, `calli` / `jmp`, and a path explosion.

Verified in **Debug and Release** (the IL differs: Release keeps `this` in a local, Debug pads the dispatch with `br.s`
and `nop`): guard 30 / 0 / 0 in both. Controls (`PathAnalysis_FlagsEveryNonEvictingShape_AndPassesTheEvictingOnes`, over
`PoaEvictionPathControls` — compile-only fixtures): **pass** the seam's own shape, a caught failure that returns inside
the `try`, a real `finally` (`using`) around it, the helper's shape; **flag**, each by its own reason, N1's team-only early
return, a conditional eviction, an eviction before the write, a swallowed failure that returns before the eviction, a
write not awaited before the eviction, an eviction not awaited, an eviction of another record, a record id re-assigned
before the eviction, a non-async pass-through, two writes before one eviction, and — for the helper rule — an early
return for one entity set, an invalidation not awaited, an invalidation of another record. C3 (a)'s control
(`PoaSeamPinControl`) reports N2's overload and a write inside a lambda held by a pinned-name method, and passes the
bound method.

**What C3 does not claim:** what the invalidator then deletes for those arguments — that is the behaviour tests'
(`PoaSeam_*` above, end to end over the production seam, client and invalidator; `ShareChangeEviction_*` for the
patterns, criterion 23).

**Item 7 — the enum route, closed rather than only disclosed.** `enum PoaAction { GrantAccess, … }` +
`PostAsJsonAsync(action.ToString(), payload)` loads no constant, so C4 could not see it. New rules:

| # | Rule (test) | Closes |
|---|---|---|
| C6 | `NoCompiledMetadataOutsideTheClientNamesAPoaAction` | A POA action carried by METADATA in the scanned assemblies, outside the client: a type, member, **enum value** or parameter NAMED as one; a POA action or write-method name held as a const field's value, a default parameter value, a custom attribute argument (types, members, parameters, return values, assembly, modules), the text of an embedded resource, or constant DATA — a UTF-8 literal (`"…"u8`) or a byte / char array initializer, an RVA field's bytes, reported under the methods that load it. |
| C4 (widened) | `NoCompiledCodeOutsideTheClientNamesAPoaActionOrWrite` | The action-name match is now case-insensitive: `"revokeaccess".ToUpperInvariant()` is the action too. |
| T5 | `NoDeployedConfigurationNamesAPoaAction` | A POA action or write name in the configuration the BFF is deployed with or reads back: `src/server/**` settings files (appsettings and templates, XML / config / resx / YAML), everything under `infra/` (the Bicep and the Dataverse rows — playbooks, actions, tools — the BFF reads at run time), and the Bicep / ARM under `infrastructure/bicep/`. Precondition: the walk reaches `appsettings.template.json` and `customer.bicep`. |

Controls: `MetadataAndConfigurationDetectors_FlagEveryCarrier_AndPassOtherWords` (`PoaBypassControl_Metadata`: an enum
value, a const, an attribute argument, a default value, a member name, a camel-case parameter name, a UTF-8 literal and a
char-array initializer all flagged;
`GrantAccessAsync` and `"GrantAccessRequest"` not; configuration text in both cases flagged, other words not).

**The disclosure, narrowed to what no static scan can reach:** a value that exists only at RUN time — a name assembled
from pieces none of which is the name (fragments joined by a call, an enum value's name plus a runtime suffix, single
characters, a decoding) or read from a LIVE store no repository file holds (an App Service setting set by hand, Key Vault, Dataverse, an HTTP response). Also stated:
POA writes made OUTSIDE the BFF (MDA sharing, flows, operator scripts) are not this guard's job — their staleness is the
TTL-bounded out-of-band row signed off in §9 / caching-architecture.md (owner R3/R4).
*Corrected in f1-v1c-v1 (§16.8): "what no static scan can reach" was wrong twice over — verifier seed U (an
`[UnsafeAccessor]` extern, a COMPILED route with nothing computed at run time) and seed R (reflection that selects the
write by its signature, with no name at all) were both statically detectable and passed all 622 ArchTests. Owner round
48 closes the classes (C7, C8, T6) and requires the comments to state exactly what is enforced.*

**Wording corrected** (each had claimed more than C3 proved): the guard header's C3 entry (now (a)/(b)/(c) as above, plus
C6 / T5 and the narrowed disclosure); `DataverseRecordShareService`'s class remarks (now a "What the build guard proves"
paragraph: outside the type / inside the type / what the behaviour tests prove / what no static guard can see);
`IMembershipCacheInvalidator.InvalidateRecordShareChangeAsync` remarks; the `AccessCacheInvalidationTests.Shares.cs`
summary; §16.6's C3 row (struck through, the original kept for the record, with a pointer here); the f1-v1 POML outcome
(correction appended).

**`IlCallScan` changes:** a full instruction decoder (`Instructions`: offset, opcode, absolute branch targets, switch
targets, local / argument indexes, constants, tokens) that `TokenOperands` now builds on; `SourceMethod` (the MethodInfo
an async state machine was built from, `null` for a lambda) beside `SourceMethodName`; `CompiledBody` (a method's
`MoveNext` or itself). The decoder refactor is re-proved on the REAL scan by re-running f1-v1 seeds S5, S12, S16, S17
(below), not only by the control-type assertions.

**Seeds — all run against the final code by a script (`seeds132.py`): seeded in the real file, rebuilt, run, restored by
`git checkout HEAD`, `git status --porcelain` empty and `git diff --quiet HEAD` asserted, the files touched; created
files removed.** Guard = `PoaShareClientSingletonGuardTests` (30); behaviour = `AccessCacheInvalidationTests` +
`DataverseRecordShare*` (66).

| # | Seed | Guard red (of 30) | Behaviour red (of 66) | What it proves |
|---|---|---|---|---|
| N1 | the verifier's: a team-only early return in the seam's `GrantAccessAsync`, before the `try`, writing with no eviction | **1** — C3 (b) `GrantAccessAsync` | **5** — `EveryShareWrite` (grant, Team) ×2, `WhenEvictionThrows` (grant, Team) ×2, `WhenTheCallerCancels` (grant, Team) | item 6 (N1); the user-only cases could not see it |
| N2 | the verifier's: a same-name non-evicting `RevokeAccessAsync(…, bool quiet, …)` overload on the seam | **1** — C3 (a) | 0 | item 6 (N2): only the interface-map pin sees it |
| P1 | `ModifyAccessAsync`'s `finally` evicts only for a systemuser principal | **1** — C3 (b) `ModifyAccessAsync` | **5** — the Team modify cases | a conditional eviction |
| P2 | `RevokeAccessAsync` evicts `Guid.Empty` instead of the record | **1** — C3 (b) `RevokeAccessAsync` ("the eviction is given (entitySetName, ?)") | **6** — every revoke case but the eviction-throws ones | the eviction's keys must be the write's |
| P3 | `GrantAccessAsync` does not await the write before its `finally` evicts | **1** — C3 (b) `GrantAccessAsync` | **9** | the write completes before the eviction |
| P4 | the eviction helper returns early for `sprk_playbooks` — a set no behaviour case uses | **4** — C3 (c), and C3 (b) for all three writes (a helper with such a path is no eviction) | **0** | a branch on an input the cases do not take: only the structural rule sees it (the N1 class, generalised) |
| P5 | the eviction helper starts the invalidation and does not await it | **4** — C3 (c), and C3 (b) for all three writes | 3, timing-dependent (the un-awaited eviction races the assertion) | the structural rule is deterministic where the behaviour cases are a race |
| P6 | the verifier's item 7: `enum PoaSeedAction { GrantAccess, ModifyAccess, RevokeAccess }` + `PostAsJsonAsync(action.ToString(), payload)` in a BFF file | **1** — C6 (enum values) | — | item 7 |
| P7 | an embedded resource in the BFF naming `RevokeAccess` | **2** — C6 (resource), T5 | — | resources and src/server configuration |
| P8 | an unused `const string Route = "grantaccess"` in a BFF file (never loaded) | **1** — C6 (const, any case) | — | consts read by reflection |
| P9 | `"revokeaccess".ToUpperInvariant()` | **1** — C4 (any case) | — | case-insensitive constants |
| P10 | `"PoaSeed": "GrantAccess"` in `appsettings.template.json` | **1** — T5 | — | deployed configuration |
| P11 | a Dataverse row under `infra/dataverse/` naming `RevokeAccess` | **1** — T5 | — | data the BFF reads back |
| P12 | `new string(new[] { 'G', 'r', … })` posted as a route in a BFF file | **1** — C6 (constant data) | — | char-array initializers (no `ldstr`, no quoted name) |
| P13 | `Encoding.UTF8.GetString("RevokeAccess"u8)` in a BFF file | **2** — C6 (constant data), task 060's payload rule (the quoted name in source) | — | UTF-8 literals |
| S5 | f1-v1's S5 re-run (the client resolved inline) | **3** — C1, T1, per-writer | — | the decoder refactor kept C1 |
| S12 | f1-v1's S12 re-run (a fourth client write) | **2** — C2, C4 | — | … and C2 / C4 |
| S16 | f1-v1's S16 re-run (`"Revoke" + "Access"` folded) | **1** — C4 | — | … and the folded constant |
| S17 | f1-v1's S17 re-run (an expression tree over the client) | **3** — C1 (by `ldtoken`), T1, per-writer | — | … and the `ldtoken` read |
| P14 | **POSITIVE:** the eviction helper renamed everywhere in the seam (`EvictAfterShareWriteAsync` → `InvalidateAfterWriteAsync`) | **0** (30 / 30 green) | — | no helper name is bound — the eviction is found in the IL |

All 19 negative seeds red, the positive seed green; every one restored (`git status --porcelain` empty, `git diff --quiet`
HEAD` = 0, file touched). Seeds were re-run whenever the rule they test changed afterwards, and the table shows the run on the
FINAL code: P6, P7 and P8 before and after C6 learned constant data (red both times); N1-P5's guard runs after C3 (c)
stopped naming the helper (N1-P3 unchanged; P4 / P5 went from 1 red to 4). The behaviour columns are from the first run —
those tests did not change afterwards.

**Item 11 — the live gates, as a script.** `projects/unified-access-control-r2/notes/task-132-live-gate.ps1` (operator
tool, not product code): `Preflight` (read-only: the deploy-order precondition `systemuser.sprk_primarycontact`; the App
Service settings `Redis__Enabled`, `Membership__CacheInvalidator__Enabled`, `ExternalAccess__ImpersonatedRootSets__Enabled`
— criterion 21(c); `/healthz`; the site's last-modified time); `Deploy` (G-1: dry run prints the exact command; `-Apply`
refuses unless the precondition holds, records the start, runs `scripts/Deploy-BffApi.ps1` with the dev defaults;
`-Verify` checks `/healthz` 200 and that the site was modified after the start); `ReassignMatter` (21(a): dry run shows
the owner, the target team and the test user's `RetrievePrincipalAccess`; `-Apply` records the original owner and the
time, then reassigns the TEST matter; `-Verify` shows the owner, the minutes since, and Dataverse's own answer for the
test user); `RestoreMatter` (`-Apply` / `-Verify`); `VerifyProvision` (21(b), read-only: the project's owner, the
colleague's `RetrievePrincipalAccess`, and the `[ACCESS-EVICT]` traces for the project from Application Insights).
**`Preflight` was run read-only on 2026-10-05 against dev:** `systemuser.sprk_primarycontact` PRESENT;
`spaarke-bff-dev` `Redis__Enabled = true`; `Membership__CacheInvalidator__Enabled` and
`ExternalAccess__ImpersonatedRootSets__Enabled` not set (the appsettings default applies; no code on this branch reads
the latter — 036 is not merged here); `/healthz` 200. The exact commands for the main session are in §12.

**Item 12** — criterion 17's ADR-009 path C reviewer approval is cited at PR time; the paragraph is drafted in §15.3.
An executor cannot give a reviewer's approval; nothing else is owed.

**Placement / justification (CLAUDE.md §10 / §11).** No production surface added: the production edits are doc comments
only (`IDataverseRecordShareService.cs`, `IMembershipCacheInvalidator.cs`) — no BFF IL change, so no publish-size or CVE
delta. New TEST helper `IlPathScan`: (1) existing — `IlCallScan` (which methods a body REFERENCES, which strings it loads)
and `SourceScan` (text); neither can say what happens on every PATH from one call; (2) extension — it reuses
`IlCallScan`'s decoder, token resolution and state-machine mapping (extended, not copied), but path analysis is a
different responsibility (control flow, exception regions, abstract values) from a reference scan, so it is its own
class (§11.5: decompose where the reason to change diverges); (3) cost of doing nothing — a share write on any path the
behaviour cases do not take (N1's team branch; a branch on an entity set no case uses) ships without the access-cache
eviction — an unshare that keeps access for up to 2 minutes — with the build green. New test file
`PoaShareClientSingletonGuardControls.cs`: the guard's compile-only fixtures (the existing ones moved, the new ones
added), kept apart from the rules they feed. New operator script `notes/task-132-live-gate.ps1`: (1) existing —
task 133's `notes/task-133-live-gate.ps1` (its own gate's steps) and `scripts/Deploy-BffApi.ps1` (which the `Deploy` step
calls rather than re-implements); (2) extension — 133's script is that task's gate; one script per gate is the
established shape; (3) cost of doing nothing — criterion 21's live writes stay hand-typed, with no dry run and no
recorded original owner to restore. No package, no plugin (ADR-002); fail closed unchanged (ADR-003).

**Step 9.5 — code-review + adr-check over the round's diff (FULL / TEST-MODIFYING rigor).** Fixed during the round:
(1) ADR-038 **B8** — C3 (c) as first written bound the seam's PRIVATE eviction helper by name through reflection
(`GetMethods(NonPublic).Single(m => m.Name == …)`), the string binding B8 exists to stop; now the eviction is found in the
IL (P14 proves no name is bound). Reading the compiled IL of non-public members is the existing arch-fitness practice
(`IlCallScan`, accepted in f1-v1), not a behaviour test of a private; the control fixtures bind their own helpers by
`nameof`. (2) C6 missed constant DATA (RVA fields) — added, seeds P12 / P13. (3) The disclosure said "computed from
non-constant pieces", which `string.Join("", "Grant", "Access")` (constant pieces joined at run time) contradicts —
reworded. Checked, no finding: ADR-038 B1 (no transport mock — the behaviour cases use the in-memory Web API server),
B3 / B4 (no DI-registration or constructor tests); ADR-010 (no new interface); ADR-002 (no plugin); ADR-003 (no production
logic change — fail-closed unchanged); §11.5 (`IlPathScan`, ~1,000 lines, has one reason to change — path analysis of a
compiled body; the guard's compile-only fixtures moved to their own file). Accepted, with reason: the path analysis
treats the await plumbing, `CancellationToken.None` and `string.Concat` as non-throwing (they fail only by running out of
memory, which the model excludes and says so); a filter is assumed both to accept and to decline (over-approximation —
it can only add paths).
**Results (f1-v1c-v1, 2026-10-05), after every seed was restored.** Build: ArchTests 0 warnings / 0 errors (a
no-incremental rebuild); BFF, BFF unit tests and `Sprk.Bff.Api.IntegrationTests` 0 / 0; `Spe.Integration.Tests` 0 errors
and 5 CA2024 warnings, all in the untouched `AnalysisEndpointsIntegrationTests.cs` (pre-existing). Affected first:
`PoaShareClientSingletonGuardTests` **30 / 0 / 0 in Debug and in Release** (22 before the round: the name-pinned C3 test
replaced, 9 added); `AccessCacheInvalidationTests` + `DataverseRecordShare*` **66 / 0 / 0** (49 + 17 new cases). Then once,
in full: NetArchTest **622 / 0 / 0** (614 + 8; run on the final code, after the last guard edit); BFF unit suite
**15,453 passed / 0 failed / 54 skipped (15,507 = 15,490 + 17; 23 m 24 s)**; `Sprk.Bff.Api.IntegrationTests`
**104 / 0 / 0**; `Spe.Integration.Tests` **403 passed / 0 failed / 25 skipped (428)**. The three non-ArchTests suites ran
on `6808d587c`, whose production code the later commits do not change (ArchTests and notes only). No contention failure
this run. Production code since `137b088f5`: doc comments only — no IL change, no package, no publish-size or CVE delta.

`.claude/**`: no edit needed.

### 16.8 Verifier round f1-v1c-v1 (2026-10-05; `task/uac-r2-132-f1-v1c-v2` from `task/uac-r2-132-f1-v1c-v1` @ `c8e1c299d`)

Owner and main-session rounds re-read from `work/unified-access-control-r2` (through round 50). **Round 48 is this
round's binding decision** (main session under round 15): (a) no `[UnsafeAccessor]` anywhere in `src/server`, an
ArchTest seeded with seed U; (b) no reflection over the Dataverse service types — `GetMethod(s)` / `GetMember(s)` /
`InvokeMember` on them or on a Type obtained from them, no `MethodInfo.Invoke` / `CreateDelegate` against them — through
the existing IL-scan infrastructure, seeded with seed R; (c) the doc comments, the guard header and §16.6 / §16.7 state
exactly what is enforced, never "by construction" beyond it; (d) the gate script's `ReassignMatter -Apply` never
overwrites a recorded `OriginalOwner`, tested. No other round after 10 decides anything for task 132. No
`NOTE-FROM-MAIN.md`. No live call and no live write this round.

**Verifier items (f1-v1c-v1 numbering).** **1 (blocking, seed U), 2 (blocking, seed R) and 9 (the "by construction"
claim) — CLOSED** below, per round 48 (a)-(c). **7 (minor, gate script) — CLOSED** per round 48 (d). 3 (seeds A-C), 4
(the behaviour tests are not tautological), 5 (suites), 6 (other checks) — met, no change. 8 (earlier items) — as the
verifier states; the one it found not met (the earlier item 10, the "by construction" guarantee) is this round's item 9,
closed here. **10 / 11** — G-1 / G-2 (live writes: `notes/task-132-live-gate.ps1`, §12) and criterion 17's reviewer
approval (PR time, §15.3): unchanged.

**What was wrong.** Both seeds reach `DataverseWebApiService.RevokeAccessAsync` around the evicting seam with all 622
ArchTests green, and the comments said neither could exist:

| Seed | Shape | Why every rule missed it |
|---|---|---|
| U | `[UnsafeAccessor(UnsafeAccessorKind.Method)] private static extern Task RevokeAccessAsync(DataverseWebApiService client, …)` on a new BFF type, called unqualified. Proven live by the verifier: one RevokeAccess POST reached the fake Dataverse with no eviction. | C1 saw a call to a method declared on the PROBE type, not the client; C4 found no string; C6 matches member names only against the action words (`RevokeAccessAsync` is another word); T1 needs a `.` before the name. A compiled route with nothing computed at run time — contradicting "only a name the code computes at run time … is beyond it". |
| R | `typeof(DataverseWebApiService).GetMethods().First(x => <4 parameters, the third a DataversePrincipalRef, returns Task>).Invoke(dv, …)` | No method token for the write, no name, no constant. The disclosure covered only a name "assembled at run time … or read from a live store"; selecting by signature is neither. |

**The fix — ban the MECHANISMS, on any type (round 48 (a)/(b)).** In
`tests/Spaarke.ArchTests/PoaShareClientSingletonGuardTests.cs`, over the same compiled scan set as C1 (every `src`
assembly the BFF runs or that can name the client; each must load):

> **[CORRECTED in round g, main-session rounds 55 / 56 — §16.9.]** C7 bans the LIST of APIs in its row below, not the
> mechanism. Verifier seed V (`Microsoft.VisualBasic.Interaction.CallByName` with a run-time name) and seed M
> (`ModuleHandle.ResolveMethodHandle` + `MethodBase.GetMethodFromHandle`, run by `EnumerableQuery` compiling a hand-built
> `Expression.Call`) reached `DataverseWebApiService.RevokeAccessAsync` with all 625 ArchTests green. Round 55 moved the
> eviction into the write itself, so those routes now evict (behaviour tests V and M); C7 stays as defence in depth and
> claims only its list. C7's test is renamed `NoCompiledCodeReferencesTheListedReflectionApis`. The original text is kept
> below for the record.

| # | Rule (test) | What it enforces |
|---|---|---|
| C7 | `NoCompiledCodeChoosesOrInvokesAMethodByReflection` | No reference (`call` / `callvirt` / `newobj` / `ldftn` / `ldvirtftn` / `jmp` / `ldtoken`, so lambdas, method groups and expression trees too) to an API that **chooses a method by reflection** — a method or member lookup on `Type` / `TypeInfo` / `IReflect` (`GetMethod(s)`, `GetMember(s)`, `GetDefaultMembers`, `FindMembers`, `GetMemberWithSameMetadataDefinitionAs`, `GetInterfaceMap`, `DeclaredMethods` / `DeclaredMembers`, `GetDeclaredMethod(s)`, `DeclaringMethod`), their extension spellings (`RuntimeReflectionExtensions`, `TypeExtensions`, `PropertyInfoExtensions`, `EventInfoExtensions`), a `Module` lookup by name or metadata token, a property's or event's accessor methods, a name-based `Expression.Call`, `RuntimeMethodHandle.FromIntPtr`; that **invokes reflectively** — `MethodBase.Invoke` (methods and constructors), `InvokeMember`, `Delegate.CreateDelegate`, `MethodInfo.CreateDelegate`, `MethodInvoker` / `ConstructorInvoker`, `RuntimeMethodHandle.GetFunctionPointer`, `Marshal.GetDelegateForFunctionPointer`, compiling an expression tree, a `dynamic` member access (the C# run-time binder); or that **runs code the compiled scan cannot read** — `System.Reflection.Emit`, an assembly loaded at run time (`Assembly.Load*` / `LoadFrom` / `LoadFile` / `UnsafeLoadFrom`, `AssemblyLoadContext.LoadFrom*`, `AppDomain.Load` / `ExecuteAssembly*` / `CreateInstance*`, `Activator.CreateInstanceFrom` / `CreateInstance(string assembly, …)`). On ANY type — so on the Dataverse service types and on any Type obtained from them, however obtained (`typeof`, `GetType()` on the client, a type parameter, a type found by enumeration): where the Type came from no longer matters. **Not banned**, because none of it reaches a method: reading properties, fields and attributes, a type's name, constructing by type (`Activator.CreateInstance(Type)`), embedded resources, an expression tree a LINQ provider translates. Today the scan set makes **zero** banned references (the BFF's reflection is `GetProperties` / `GetValue`, `GetCustomAttributes`, `Assembly.GetTypes`, `Activator.CreateInstance(Type)`), so no exemption exists; a future need is a review decision named in the guard, and the reflected type must not be a Dataverse service type. |
| C8 | `NoCompiledCodeCarriesAnUnsafeAccessor` | No `[UnsafeAccessor]` / `[UnsafeAccessorType]` (.NET 10) in the compiled metadata of the scan set — on a type, member, parameter, return value, generic parameter, the assembly or a module — however the attribute was spelled or aliased, generated code included. |
| T6 | `NoServerSourceNamesUnsafeAccessor` | No `UnsafeAccessor` (attribute, kind enum, type companion, an alias's target) outside a whole-line `//` comment in any `.cs` / `.csproj` / `.props` / `.targets` under `src/server` — the projects outside the compiled scan set included (the L2 control plane) — or in the `Directory.Build.props` / `.targets` above it (where a global `using` alias could hide the name from the source that uses it). Conservative: a block comment or a string naming it is flagged too. Precondition: the walk reaches the BFF project, the client, the L2 Worker project and the root `Directory.Build.props`. |

Why bans and not a provenance analysis: a rule that follows where a `Type` came from (`typeof`, `GetType()`, a type
parameter, a field, a collection, DI's `GetService(Type)`) can always be routed around one more hop; a ban on the
mechanism cannot. It costs nothing today (zero references) and makes round 48 (b)'s "or on a Type obtained from them"
true without having to trace anything. **[CORRECTED in round g: "a ban on the mechanism cannot" is false — a list of APIs
is not the mechanism, and seeds V and M went around it. That is round 55's reason for moving the eviction into the
write (§16.9).]**

**Controls** (`CompiledDetector_FlagsEveryBypassShape_AndPassesTheSeam`; compile-only fixtures in
`PoaShareClientSingletonGuardControls.cs`): **seed U verbatim** — the earlier rules C1, C4 and C6 asserted to PASS it
(the finding), C8 to flag it; a field accessor that lifts the seam's private client, and `[UnsafeAccessorType]` on a
parameter — flagged. **Seed R verbatim** — C1 and C4 asserted to pass it, C7 to flag `Type.GetMethods` and
`MethodBase.Invoke`. 19 more C7 shapes, each asserted by the API it reaches (`GetMember` on `obj.GetType()`, `IReflect`,
`DeclaredMethods`, `GetRuntimeMethods`, `GetInterfaceMap`, `Module.ResolveMethod` by token, a property's `GetMethod`,
`InvokeMember`, both `CreateDelegate`s, `MethodInvoker`, a function pointer, a compiled tree, a name-based
`Expression.Call`, `dynamic`, `DynamicMethod`, `Assembly.Load(byte[])`, `AssemblyLoadContext.LoadFromStream`, a
`typeof(T)` helper). A positive control of the reflection the BFF does use (property values, a type's name,
`Activator.CreateInstance(Type)`, attributes, resource names, a compiler-built expression tree) and the sanctioned shape:
flagged by neither. Text (`Detector_…`): seed U's line, the fully qualified attribute, a `global using` alias, an MSBuild
`<Using … Alias>`, a `//` inside a string on the line — flagged; a doc comment and a whole-line comment that mention it —
not.

**Seeds — the real tree** (`seeds132v2.ps1`: each written into the real files, built, the guard run from a TRX, then
restored by `git checkout HEAD` / removal, `git status --porcelain` empty and `git diff --quiet HEAD` asserted, restored
files touched). Guard = `PoaShareClientSingletonGuardTests` (33).

| # | Seed | Red (of 33) | Offender reported | What it proves |
|---|---|---|---|---|
| U | the verifier's seed U, verbatim, as `Services/Access/ProbeU.cs` | **2** — C8, T6 | `ProbeU.RevokeAccessAsync: [UnsafeAccessorAttribute]`; `…/ProbeU.cs` | item 1 |
| R | the verifier's seed R, verbatim, as `Services/Access/ProbeR.cs` | **1** — C7 | `ProbeR → MethodBase.Invoke`, `→ Type.GetMethods` | item 2 |
| U2 | a field `[UnsafeAccessor]` in **Spaarke.Dataverse** (the client's own project) | **2** — C8, T6 | `Spaarke.Dataverse.ProbeU2.HttpOf` | the client project is scanned |
| U3 | **aliases** in the root `Directory.Build.props` (`<Using … UnsafeAccessorAttribute Alias="FastAttribute">`, `… UnsafeAccessorKind Alias="FastKind">`) + `[Fast(FastKind.Method)] extern … RevokeAccessAsync(DataverseWebApiService …)` in a BFF file that never spells the word | **2** — T6, C8 | `Directory.Build.props`; `ProbeU3.RevokeAccessAsync` | an alias defeats neither rule |
| U4 | an `[UnsafeAccessor]` in the **L2 Worker** project (outside the compiled scan set) | **1** — T6 | `…/Sprk.Provisioning.ControlPlane.Worker/ProbeU4.cs` | "anywhere in src/server" |
| R2 | `object o = dv; ((MethodInfo)o.GetType().GetMember("Revoke" + suffix)[0]).Invoke(o, null)` | **1** — C7 | `→ Type.GetMember`, `→ MethodBase.Invoke` | an `object`-typed holder, a run-time name |
| R3 | `Of<T>() => typeof(T).GetMethods()`, called `Of<DataverseWebApiService>()` | **1** — C7 | `→ Type.GetMethods` | a type parameter |
| R4 | `Delegate.CreateDelegate(typeof(Func<…>), dv, "RevokeAccessAsync")` | **3** — C7, C4, T4 | `→ Delegate.CreateDelegate`; `"RevokeAccessAsync"` | by-name binding |
| R5 | `((dynamic)typeof(DataverseWebApiService)).GetMethods()` | **1** — C7 | `→ Binder.InvokeMember` | reflection through `dynamic` |
| R6 | `AssemblyLoadContext.Default.LoadFromStream(image)` | **1** — C7 | `→ AssemblyLoadContext.LoadFromStream` | code loaded at run time |
| R7 | `Expression.Lambda(Expression.Call(Expression.Constant(dv), "Revoke" + suffix, null)).Compile()` | **1** — C7 | `→ Expression.Call`, `→ Expression`1.Compile` | name-based lookup + compile |
| R8 | `((IReflect)typeof(DataverseWebApiService)).GetMethods(flags)` | **1** — C7 | `→ IReflect.GetMethods` | the `IReflect` spelling |
| R9 | `MethodInvoker.Create(dv.GetType().GetTypeInfo().DeclaredMethods.Single(<by signature>)).Invoke(dv, …)` | **1** — C7 | `→ TypeInfo.get_DeclaredMethods`, `→ MethodInvoker.Create`, `→ MethodInvoker.Invoke` | the .NET 8+ invoker |

All 13 seeds red, every one restored. C1-C6 and T1-T5 are unchanged this round; their controls still pass, and seed R4
shows C4 / T4 still bite.

**The gate script (round 48 (d)).** `ReassignMatter -Apply` recorded the matter's current owner unconditionally: run
twice, the second run recorded the first run's TARGET TEAM as the "original", and `RestoreMatter` could no longer restore
the real owner of the test matter. Now the state holds a record PER MATTER; `-Apply` records the original owner only when
the matter has no record or its record was restored (otherwise it prints "already recorded … KEPT" and only refreshes the
reassign time); `RestoreMatter -Apply` marks the record restored only when the owner reads back as the original (else it
keeps the record so the restore can be retried); a state file of the earlier single-matter shape is read as that
matter's record; new `-StatePath` (default unchanged, `%TEMP%\task-132-live-gate.state.json`). **Tested:**
`notes/task-132-live-gate.Tests.ps1` (Pester 6.2; `az` and `Invoke-RestMethod` mocked, a catch-all mock fails any
unexpected call, the fake owner changes only through the mocked PATCH) — **7 / 7**: the first `-Apply` records the real
owner; a second `-Apply` keeps it (the reported defect); two reassigns then a restore put the REAL owner back; after a
verified restore the next reassign records afresh; two matters keep their own originals; the legacy state shape; a dry
run patches and records nothing. **Seeded** (same restore discipline): G1 the original re-read on every `-Apply` (the
reported defect) **3 / 7 red**; G2 a restore never marked **2 / 7**; G3 the legacy shape ignored **1 / 7**; restored 7 / 7.

**Wording (round 48 (c)) — exactly what is enforced, nothing beyond it.** Rewritten: the guard header ("WHAT THIS GUARD
ENFORCES — exactly the rules below", C7 / C8 / T6 added, a "NOT ENFORCED" paragraph in place of "OUT OF REACH, by
construction"); `DataverseRecordShareService`'s class remarks ("What the build guard enforces": (1) every compiled call
path, (2) no UnsafeAccessor, (3) no method chosen or invoked by reflection nor code the scan cannot read; inside the type,
the every-path proof; "Not enforced"), and its "no share writer can be born without it" sentence; the interface remark
that points at them; the `IMembershipCacheInvalidator.InvalidateRecordShareChangeAsync` remarks ("covered by
construction" removed; the same three things; "a route outside those … is review's"); the
`AccessCacheInvalidationTests.Shares.cs` summary ("any compiled route" → the three things); `IlCallScan`'s remarks ("out
of its reach by construction" → what a reference scan cannot see, and which mechanisms the guard bans instead); §16.1
(heading and claim), §16.6 and §16.7 (corrections appended, the original wording kept for the record); the f1-v1c POML
outcome (correction appended).

**What the build enforces now — the statement every place above repeats.** Over every `src` assembly the BFF runs or
that can name `DataverseWebApiService`: (1) every compiled call path to the client's POA writes (C1-C6; C3 inside the
seam: only the three interface methods write, and every path awaits the write and then evicts that record), plus the
text and configuration rules T1-T5; (2) no `[UnsafeAccessor]` (C8, and T6 over all of `src/server`); (3) no method chosen
or invoked by reflection, on any type, and no code the scan cannot read (C7). **Not enforced** (review's): a POA write by
a route that uses none of those mechanisms — above all a raw HTTP call whose action URL exists only at run time
(assembled from pieces none of which is the action name, or read from a live store no repository file holds); native
code; code outside the scan set (it cannot name the client and does not run in the BFF). POA writes made outside the BFF
(MDA, flows, scripts) are the TTL-bounded out-of-band row (§9, owner R3/R4). **[CORRECTED in round g: (3) is false as
written — C7 bans a list of APIs, not every way to choose or invoke a method by reflection (seeds V, M); the L2 projects'
"T6 only" coverage missed a Unicode-escaped attribute (seed U5); and since round 55 the eviction is made by
`DataverseWebApiService`'s own share writes, not the seam, so C3 analyses the client. The true statement is §16.9's.]**

**Placement / justification (CLAUDE.md §10 / §11).** No production surface: the production edits are doc comments only
(`IDataverseRecordShareService.cs`, `IMembershipCacheInvalidator.cs`; the diff is `///` lines only), so no IL change, no
publish-size or CVE delta, no package. **New test rules C7 / C8 / T6** — members of the existing guard class on the
existing `IlCallScan.MethodReferences` and `SourceScan` (no new helper): (1) existing — C1-C6 read method tokens, string
constants and metadata NAMES; none reads the attribute that makes a method an accessor, or a reflective call; (2)
extension — they extend the same guard over the same scan set (one reason to change: the routes by which a POA write can
reach Dataverse around the evicting seam; §11.5 — the file grows to ~1,760 lines, cohesive, one invariant); (3) cost of
doing nothing — seeds U and R ship an unshare that keeps access for up to 2 min with the build green. **New Pester test
file** `notes/task-132-live-gate.Tests.ps1`: (1) existing — none (the repository has no Pester tests; the script's own
`-Verify` reads live state and cannot exercise its bookkeeping without live writes); (2) extension — it is the gate
script's own test, beside it; (3) cost of doing nothing — round 48 (d) requires the fix tested, and a regression would
leave `RestoreMatter` unable to put a live test matter's real owner back. **New script parameter** `-StatePath`: the
seam that keeps the tests' state out of the operator's real state file (default unchanged). No plugin (ADR-002); fail
closed unchanged (ADR-003). `.claude/**`: no edit needed.

**Step 9.5 — code-review + adr-check over the round's diff (FULL / TEST-MODIFYING rigor).** Checked, no finding: ADR-038
B1 (no transport double), B3 / B4 (no DI-registration or constructor tests), B8 (no string-bound reflection into a
private: the rules read IL references and attribute metadata; the controls are the guard's own compile-only fixtures,
never executed — `Adr038TestBanGuardTests` passes over them); ADR-010 (no new interface); ADR-002; ADR-003 (no production
logic change). Accepted, with reason: C7 is deliberately broader than round 48 (b)'s wording (any type, not only the
Dataverse service types) — that breadth is what makes "a Type obtained from them" enforceable without a provenance
analysis, and it costs nothing today; T6 is conservative (a block comment or string that names UnsafeAccessor is flagged —
reword it); the L2 projects outside the compiled scan set are covered by T6's text only (they cannot name the client and
do not run in the BFF, so C8's compiled check adds nothing there).

**Results (round f1-v1c-v1, 2026-10-05), after every seed was restored.** Build: ArchTests, BFF, BFF unit tests and
`Sprk.Bff.Api.IntegrationTests` 0 warnings / 0 errors; `Spe.Integration.Tests` 0 errors and the 5 pre-existing CA2024
warnings in the untouched `AnalysisEndpointsIntegrationTests.cs`. Affected first: `PoaShareClientSingletonGuardTests`
**33 / 0 / 0** in Debug and in Release (30 + C7, C8, T6; `CompiledDetector_…` and `Detector_…` extended);
`task-132-live-gate.Tests.ps1` **7 / 7** (Pester 6.2). Then once in full, on `f80836182` (this round's production and
test code; the later commit is notes and the POML only): NetArchTest **625 / 0 / 0** (622 + 3);
`Sprk.Bff.Api.IntegrationTests` **104 / 0 / 0**; `Spe.Integration.Tests` **403 passed / 0 failed / 25 skipped (428)**;
BFF unit suite — first run **15,434 passed / 19 failed / 54 skipped** (15,507; 30 m 46 s): all 19 were
WebApplicationFactory requests cancelled after 3-4 minutes ("Error while copying content to a stream"; one 19-minute
eval harness) while the machine ran 87 `dotnet` processes at 88 % CPU (other agents' suites) — the 19 re-run in
isolation **19 / 0 / 0**, and a second full run **15,453 passed / 0 failed / 54 skipped (15,507; 20 m 52 s)**: contention,
not this round (its production edits are doc comments only). No package change: no publish-size or CVE delta.

### 16.9 Round g — the eviction moves into the share write (2026-10-05; `task/uac-r2-132-g` from `task/uac-r2-132-f1-v1c-v2` @ `837220b9d`)

**Binding.** Main-session rounds 48 and **55** (work/unified-access-control-r2 note, read through round 58), and **owner
round 56** (relayed in `NOTE-FROM-MAIN.md`, not committed): fix (a) runtime, (b) compounding-maintainability and (c)
performance defects; record (d) adversarial-only guard bypasses, (e) rare fail-closed edges and (f) minor seeding requests
as Known limits; build no new machinery for (d)-(f); keep rounds 1-55; **this is the lane's last fix round.** Round 55
is the decision for this round: (1) move the eviction INTO the share-write primitive; (2) the IL guards stay as defence in
depth, and every comment says only what is TRUE; (3) the Unicode-escaped `[UnsafeAccessor]` outside the scan set (seed U5)
is caught by scanning the compiled IL of EVERY `src/server` project. No live call and no live write this round.

**Items (verifier f1-v1c-v2 numbering) — class (owner round 56) and what was done.**

| # | Item | Class | Done |
|---|---|---|---|
| 1 | Rounds 48 + 55 owed | — | Round 55 (1)-(3) implemented below; round 48 (a)-(d) stand (UnsafeAccessor ban now over every `src/server` assembly; the reflection ban is C7's list; claims corrected; gate script unchanged). |
| 2 | C7 false negative, seed V (VB late binder) | (d) as a guard gap; the **eviction** gap behind it was (a) | Closed at the root (round 55): a late-bound call of the client's write now evicts — **behaviour test** `ShareWrite_InvokedByALateBinderFromARunTimeName_StillEvictsThatRecord` (seed V adapted). No new ban (round 55 "not more bans"; round 56 (d)); recorded in Known limits. |
| 3 | C7 false negative, seed M (token + LINQ provider) | as item 2 | Closed at the root: **behaviour test** `ShareWrite_ResolvedFromItsMetadataTokenAndRunByALinqProvider_StillEvictsThatRecord` (seed M adapted). Known limit for C7. |
| 4 | Comments contradict code | (b) | Fixed everywhere the claim lived: the guard header (rewritten: where the eviction lives, then exactly what each rule checks, then what is not enforced), the C7 section / doc / test name (`NoCompiledCodeReferencesTheListedReflectionApis`) / message, `IlCallScan` remarks, `DataverseRecordShareService` remarks (it no longer evicts; points at the guard header for the list), `IMembershipCacheInvalidator` remarks, the `AccessCacheInvalidationTests.Shares` summary, the controls' C7 summary, §16.8 (corrections appended, original kept), the f1-v1c-v1 POML outcome (correction appended); plus every other comment/doc that said "the POA seam evicts": `caching-architecture.md` (write-path eviction row, residual row 4), `uac-access-control.md`, `membership-resolution-pattern.md`, `CachedAccessDataSource`, `MembershipCacheInvalidator`, `ImpersonatedRootSetSource`, two test-double remarks. |
| 5 | Recommended bans (late binders, handle/token APIs, non-ldtoken Expression.Call) | (d) | Not built — superseded by round 55 (the structural fix) and owner round 56 (no new machinery for (d)). Known limit. |
| 6 | Seed U5 (Unicode-escaped UnsafeAccessor in the L2 Worker) | (d) | Round 55 item 3 decided the fix, kept per round 56: **C8 now reads the compiled custom-attribute table of every `src/server` project's assembly** (8 projects; the L2 Worker and L2 test project are built for it). Seed U5 re-run: C8 red, T6 green (T6's claim corrected to "the text as written"). |
| 7 | Gate script | met | No change. |
| 8 | UnsafeAccessor ban inside the scan set | met | C8 re-implemented (one mechanism over every assembly, replacing the reflection walk); seed U and the two other shapes stay as controls. |
| 9 | Only `///` changed last round | info | This round changes production IL (below): publish size measured (§16.9 results). |
| 10, 11 | Suites, other checks | met | Re-run in full below. |
| 12, 13, 14 | Criteria not met (= items 2-4, 6) | as above | As above. |
| 15 | Criterion 21 (G-2) + G-1 | live | Unchanged: `notes/task-132-live-gate.ps1`, commands in §12, a manual gate for the main session. |
| 16 | Criterion 17 reviewer approval | PR time | Unchanged: paragraph in §15.3. |

**The fix (round 55 item 1).**
- `Spaarke.Dataverse` gains ONE hook, `IRecordShareWriteObserver` (`OnRecordShareWrittenAsync(entitySetName, recordId,
  RecordShareWrite write, ct)`), and the `RecordShareWrite` kind (Grant / Modify / Revoke) it is told about — one file,
  `IRecordShareWriteObserver.cs`.
- `DataverseWebApiService`: `GrantAccessAsync` / `ModifyAccessAsync` / `RevokeAccessAsync` build their payloads as before
  and send through ONE private method, `SendShareWriteAsync`, which makes the POST in a `try` and, in the `finally`, calls
  `NotifyShareWrittenAsync(entitySetName, recordId, write)` — on every path (returned, refused, thrown, cancelled), on
  `CancellationToken.None`; an observer that throws is caught and logged (`[ACCESS-EVICT] The share-write observer threw
  …`) and never changes the write's outcome. The observer is a **required** constructor argument (public and protected
  constructors), so no host gets a client that silently tells nobody.
- BFF: `IMembershipCacheInvalidator : IRecordShareWriteObserver`, with a **default interface member** forwarding the
  notification to `InvalidateRecordShareChangeAsync(entitySetName, recordId, "share:{grant|modify|revoke}", ct)` — so the
  real invalidator, the Null peer and every test double are observers with no new class. `MembershipModule` registers
  `IRecordShareWriteObserver` → the registered `IMembershipCacheInvalidator`, **unconditionally** (real with Redis, the Null
  peer with the in-memory cache — ADR-032 symmetric, §F.1); `GraphModule` passes it to the client.
- `DataverseRecordShareService` stops evicting (a pure pass-through; its constructor takes only the client) — **one
  eviction path, one eviction per write.**

**The guards (round 55 item 2).** They stay as defence in depth; the guard header now states, in order, where the
eviction lives, exactly what each rule checks, and what is not enforced. Changes: **C3** re-targeted from the seam (which
no longer evicts) to the client — the client methods the three writes reach that make an HTTP send are its share-write
senders, and an `IlPathScan` every-path analysis proves each awaits the send and then calls the notification for its own
`entitySetName` / `recordId` and awaits it; the notification is found in the IL (a client method that calls
`IRecordShareWriteObserver.OnRecordShareWrittenAsync` for its own record on every path). `IlPathScan` gains one option,
`OpeningKeysFromSource` (the send carries no key parameter; the obligation is keyed by the sender's own). The seam pin
(old C3 (a)) and its control are removed (nothing in the seam to pin). **C1, C2, T1** keep task 060's single seam (their
messages no longer claim eviction). **C4-C6, T2-T5** reject a POA write around `DataverseWebApiService` named by a
constant, metadata, constant data or configuration. **C7** bans exactly its list. **C8** / **T6** below.

**C8 over every `src/server` project (round 55 item 3).** `UnsafeAccessorsIn(dll)` reads each assembly's custom-attribute
table with `System.Reflection.Metadata` (no load, no dependency resolution): every row — type, member, parameter, return
value, generic parameter, assembly, module — whose attribute type is `System.Runtime.CompilerServices.UnsafeAccessor*`,
whatever the source spelled. `ServerProjectAssemblies()` finds each `src/server` csproj's newest
`bin/{configuration}/{tfm}/**/{AssemblyName}.dll` (the Web SDK projects build to `linux-x64/`; ref / publish excluded) and
the test FAILS, naming the project, if one is not built. The new `BuildServerProjectsForCompiledScans` target in
`Spaarke.ArchTests.csproj` builds the L2 Worker and L2 test project (the ProjectReferences and the Cosmos target build the
rest). The controls (seed U and two other shapes) read the ArchTests assembly's own DLL the same way. This REPLACES the
reflection-based `CompiledUnsafeAccessors` (one mechanism, not two). Side effect, verified: `CosmosProvisioningSecretGuardTests`
now also finds the built Worker and scans it — green.

**Over-engineering check (owner round 56 item 2).**
- *Built and REVERTED:* a refusal in the client's private generic POST of any share-write URL (and its 10-spelling test
  theory) — it closed only a reflection-into-a-private route (class (d)); recorded as a Known limit instead.
- *Not built:* more C7 bans (item 5); escape decoding in T6 (round 55 item 3 puts U5 on the compiled scan).
- *New surface, each against CLAUDE.md §11:* **`IRecordShareWriteObserver` + `RecordShareWrite`** — (1) existing:
  `IMembershipCacheInvalidator.InvalidateRecordShareChangeAsync` (BFF) — `Spaarke.Dataverse` cannot reference the BFF, and
  the seam that called it evicted only for its own callers; (2) extension: the smallest inversion point — the BFF's existing
  invalidator implements it through a default member (no BFF class); the enum names the write for the correlation id; (3)
  cost of doing nothing: any call of the client's writes that skips the seam (seeds S5, U, R, V, M) leaves the record's root
  sets (2 min) and snapshots (60 s) stale — an unshare that keeps access. **One DI registration** (forwarding,
  unconditional) and **one required constructor parameter**: the wiring itself. **`IlPathScan.OpeningKeysFromSource`**
  (8 lines): the existing analyser cannot key an HTTP send. **The metadata scan** replaces the reflection walk (not
  additive). **The ArchTests build target**: C8 must read assemblies that are not referenced.
- No package, no new service class, no endpoint, option, job or column.

**Seeds (real tree; each written into the real file, built, run, restored by `git checkout HEAD` / removal, `git diff
--quiet HEAD` asserted, restored file touched).** Behaviour = `AccessCacheInvalidationTests` (58); guard =
`PoaShareClientSingletonGuardTests` (30).

| # | Seed | Behaviour red | Guard red | Proves |
|---|---|---|---|---|
| S1 | **the observer call removed** from the client's `finally` (round 55's named seed) | **40 / 58** (every write x kind x outcome, cancellation, observer fault, Redis fault, exactly-once, composition, V, M) | **2 / 30** (C3 a, C3 b) | the eviction lives in the write |
| S2 | notify BEFORE the send | 6 (exactly-once-after-the-write) | 1 (C3 a) | ordering |
| S3 | notify twice | 19 (12 observer-told-once, 6 exactly-once, 1 composition) | 0 — by design (a second notification is not a missing one) | exactly one eviction per write |
| S4 | the client's `catch` around the observer removed | 12 (observer fault) | 0 | an observer fault never fails the write |
| S6 | `GraphModule` passes a Null invalidator instead of the registered observer | 1 (the BFF's own composition) | 0 | the production wiring |
| U5 | verifier seed U5's shape: `[System.Runtime.CompilerServices.Unsafe\u0041ccessor(…Unsafe\u0041ccessorKind.Field, Name = "_value")] internal static extern ref int ValueOf(Box box);` in the L2 Worker | — | 1 (C8: `Sprk.Provisioning.ControlPlane.Worker.dll: …ProbeU5.ValueOf: [UnsafeAccessorAttribute]`); T6 green, as documented | round 55 item 3 |
| S8 | seed N1's shape in the client: a `sprk_playbooks` branch that sends and returns before the `try` | **0** (no case takes it) | 1 (C3 a) | why the structural rule stays |
| S9 | the notification returns early for one entity set | 0 | 2 (C3 a, C3 b) | the notification is checked on every path |
| S10 | a fourth client write `ShareReadAsync` naming GrantAccess | — | 2 (C2; C4's vacuity) | C1 knows every POA write by name |

Not seeded (class f): the seam re-adding an eviction of its own (it has no invalidator to call; S3 turns the same
exactly-once test red for both routes).

**Step 9.5 (code-review + adr-check; FULL / TEST-MODIFYING).** ADR-002 no plugin. ADR-003: the write's outcome is
unchanged on every path; an eviction failure falls back to the TTLs, as before. ADR-010: the new interface is a genuine
seam (dependency inversion across the shared-library boundary); the BFF's 1:1-interface ceiling counts BFF-assembly
interfaces only (green). ADR-032: symmetric (the observer registration is unconditional; the invalidator behind it is real
or Null by `Redis:Enabled`). ADR-038: no transport double (an in-memory TestServer); no DI-registration test — the
composition test performs a share write through the BFF-composed client and asserts the eviction it causes; no reflection
into a private (seeds V and M call the PUBLIC write; the IL guard's controls are test-owned fixtures). CLAUDE.md §11.5: the
guard file stays one cohesive class (one invariant: how a POA write reaches Dataverse and evicts). Pre-existing,
outside this task (reported, not fixed): `CosmosProvisioningSecretGuardTests` resolves each L2 DLL by its DIRECTORY name,
so the L2 Api (assembly `Sprk.Provisioning.ControlPlane`) is never loaded by it.

**Placement (CLAUDE.md §10).** In place: `Spaarke.Dataverse` (the client + one hook file) and the BFF's existing
invalidator / DI modules / seam. No new endpoint, service class, option, job, column, PCF or package; no plugin.

**Results (round g).** Build: BFF, ArchTests, BFF unit tests and Sprk.Bff.Api.IntegrationTests 0 warnings / 0 errors; Spe.Integration.Tests 0 errors and the 5 pre-existing CA2024 warnings in the untouched AnalysisEndpointsIntegrationTests.cs. Affected first: AccessCacheInvalidationTests 58 / 0 / 0 (its share-write section is 40 cases — the client's 31 replacing the seam's 31, plus exactly-once x6, the composition case, seeds V and M); with the seven construction sites' classes and MembershipOptionsTests 125 / 0 / 0; PoaShareClientSingletonGuardTests 30 / 0 / 0 (33 minus the old C3's 5 cases plus the new C3's 2) — 39 / 0 / 0 with CosmosProvisioningSecretGuardTests, and 52 / 0 / 0 with Adr038TestBanGuardTests and the ADR-010 tests. Then ONCE in full, on 36d232967 (this round's production and test code; d7245d9b0 only fixes one Spe test's construction): NetArchTest 622 / 0 / 0 (625 - 5 + 2); Sprk.Bff.Api.IntegrationTests 104 / 0 / 0; BFF unit suite 15,343 passed / 119 failed / 54 skipped (15,516 = 15,507 + 9; 1 h 2 m) — every failure a WebApplicationFactory request cancelled after 3-6 minutes ("Error while copying content to a stream") while the machine ran about 108 dotnet processes (other agents' suites); the 113 failing methods re-run in isolation 168 / 0 / 0 (the name filter also matches their sibling theory cases): contention, as in round f1-v1c-v1. Spe.Integration.Tests: the full run stopped on a compile error in DataverseRecordShareRoundTripTests (a target-typed construction the sweep had missed), fixed in d7245d9b0, then 403 passed / 0 failed / 25 skipped (428). Publish size (CLAUDE.md §10): base 837220b9d vs head d7245d9b0, each exported fresh with git archive to a short path (C:\w132gb, C:\w132gh), dotnet publish -c Release, zipped with Compress-Archive (Optimal): 46.01 MB / 212 files vs 46.01 MB / 212 files, delta -26 bytes, PDBs included. No package change, so no CVE delta.

## Known limits (owner round 56 — recorded, not fixed)

- **(d) C7 is a list.** A late binder (seed V), a method resolved from a handle or metadata token and run by a LINQ provider
  (seed M), and any reflection API not on C7's list pass it. The eviction no longer depends on it (round 55).
- **(d) A POA write that does not go through the three share writes** — a raw HTTP call whose action name exists only at
  run time, including one assembled from the client's private members reached by reflection (its generic POST helper,
  request builder or `HttpClient`) — is not notified. The guards stop such a call only when its action name is a constant,
  metadata, constant data or deployed configuration (C4-C6, T2-T5).
- **(d) Code that replaces the client's observer by reflection** (writing its private field) silences the notification.
- **(d) A direct reflective call of the private `SendShareWriteAsync`** can pass a payload addressed to a record other than
  the one it notifies for.
- **(d) C3 follows method bodies**: a send moved into a lambda the sender hands elsewhere is not analysed (a client whose
  only send moved there fails C3's precondition; one among others would not).
- **(d) T6 reads the text as written**: a Unicode-escaped identifier, or an alias from a package's build files or a source
  generator, is not text. C8 reads the compiled attribute in every `src/server` assembly.
- **(d) C1 / T1 / per-writer rules know the client's writes by name**; a share write through a fourth public client method
  that does not name an action itself would be outside the single-seam rule — it would still evict (it sends through the
  notifying sender) and C3 would analyse it.
- **(f) Verifier item 5's bans** (VB late binders, `MethodBase.GetMethodFromHandle`, `ModuleHandle.ResolveMethodHandle`,
  `GetRuntimeMethodHandleFromMetadataToken`, an `Expression.Call` built from a non-`ldtoken` `MethodInfo`) — not built.
- **(f)** No seed re-adds an eviction to the seam (see S3).
- **(f) The share-write observer's `CancellationToken.None` contract is not pinned by a test** (main-session round 63 item
  1, option A). The current invalidators ignore the token, so this has no runtime effect. The L2 guard gap found beside
  it (`CosmosProvisioningSecretGuardTests` resolves DLLs by directory name and skips the L2 Api assembly) belongs to the
  customer-provisioning lane and is filed as #1310 (round 63 item 2).
- **Batch-4 integration (148 × 132).** Every child OWNER write of `SecureChildReconciler` is now evicted through
  `InvalidateRecordOwnerChangeAsync` on `CancellationToken.None`, once per write attempt, never failing the pass. That
  covers provisioning's child pass, the unsecure's Step 3.5 and its completion branch, a re-file, and
  `SecureChildReconciliationJob`. Pinned by four tests in `SecureChildTransitionTests` (148×132). The integration merge
  also unified the IL readers: task 167's `CompiledIl` became `IlCallScan.FileUses`, which uses the same decoder.
