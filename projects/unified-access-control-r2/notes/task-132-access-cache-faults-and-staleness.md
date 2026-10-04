# Task 132 (#1056) — C12: access caches stop storing faults, BFF writes evict, TTLs cut to 2 minutes

**Status:** code complete on `task/uac-r2-132` (parent `task/uac-r2-137-b2`, merged with `work/unified-access-control-r2`
at `3a6b38cb1`); verifier round **r1** closed on `task/uac-r2-132-r1` (§15). POML status
**completed-with-escalation**: escalation 7 (§8) is unanswered by owner rounds 1-9, and the dev deploy and the manual
live gate (criterion 21) are pending manual gates (§12). Rigor FULL, Opus 5.5 @ xhigh.

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

### 16.1 Residual 1 — share-only changes must evict like owner changes: CLOSED, by construction

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
`PoaShareClientSingletonGuardTests` fails the build on the routes around the seam listed in §16.6 (a compiled reference
to the concrete client's POA writes from any type but the seam, whatever the receiver; a text-level call whose receiver
is not declared only as the seam; the SDK messages; a second payload; a write named as a string). *Corrected in f1-v1:
as first committed in f1 this sentence read "fails the build on a POA write that bypasses the seam", which the verifier
disproved — the f1 detector only saw a bare-identifier receiver (seeds S5 / S6 stayed green). §16.6.*
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
  catch, not the seam's — replaced in f1-v1 by `PoaSeam_WhenEvictionThrows_…` ×6 and renamed
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

### 16.6 Verifier round f1-v1 (2026-10-04, `task/uac-r2-132-f1-v1` from `task/uac-r2-132-f1` @ `835b57472`)

**Verifier findings acted on.** Items 1, 2 and 5 were met (the eviction itself, the cascade-child gate, the behaviour
seeds); item 3 (blocking) and item 4 (minor) were not. Items 6-8 and 10-11 are observations / gates (below).

**Item 3 — the build guard had a false negative, and five places claimed it had none: CLOSED.** The f1 text detector
matched only `identifier.XAccessAsync(` and checked the identifier's declaration file-wide. Verifier seed **S5**
(`sp.GetRequiredService<DataverseWebApiService>().RevokeAccessAsync(…)` in `NoAccessShareEnforcer.cs`) and **S6** (a
`DataverseWebApiService _recordShare` parameter in a file that also declares `IDataverseRecordShareService _recordShare`)
both left all 14 guard tests green. Fix, in `tests/Spaarke.ArchTests/PoaShareClientSingletonGuardTests.cs` plus one new
test helper `tests/Spaarke.ArchTests/IlCallScan.cs`:

| Route around the seam | Rule now | Precision |
|---|---|---|
| A compiled call to `DataverseWebApiService.Grant/Modify/RevokeAccessAsync` — any receiver expression, inside a lambda / local function / async state machine, or a method group (`ldftn`) | `EveryCompiledReferenceToTheClientsPoaWritesIsTheSeams` — reads the IL method tokens (`call`/`callvirt`/`newobj`/`ldftn`/`ldvirtftn`/`jmp`) of every type in every assembly that can name the client; closures attributed to their outermost type; only `DataverseRecordShareService` and the client itself may reference them. A target counts if declared on the client, a subclass, or a base type / interface the client implements (so widening one of the client's interfaces is still caught). Fails loud on an undecodable stream or unresolvable token. Vacuity check: it must see the seam's own three calls | exact (types resolved by the compiler) |
| Which assemblies "can name the client" | `EveryProjectThatCanSeeTheClientIsScanned` — derived from the csproj graph (every `src/**` project whose ProjectReference closure reaches `Spaarke.Dataverse`: today Spaarke.Dataverse, Spaarke.Core, Spaarke.Scheduling, Sprk.Bff.Api); a project the ArchTests cannot load fails the build | exact over `src/**` |
| The same call in a `src/server` file outside that closure | `EveryPoaShareWriteGoesThroughTheEvictingSeam` — every `.XAccessAsync(` is an offender unless its receiver is an identifier (`x.`, `a.b.x.`, `x?.`, `x!.`) declared in that file, and declared ONLY, as `IDataverseRecordShareService`; an expression receiver (`…)`, `…>`, `…]`), a `var`, an undeclared name and a name also declared as another type are offenders | conservative (may false-red; the compiled rule is the precise one) |
| Reflection by method name | `NoServerFileNamesTheClientsPoaWritesAsAString` — a `"…AccessAsync"` literal or a `nameof(…AccessAsync)` outside the client and the seam | text |
| SDK messages; a second payload | unchanged (`NoServerFileSendsSdkPoaMessages`; task 060's payload rule) | text |

Out of reach of any guard, and now SAID so in the comments rather than claimed: a method name assembled from fragments
to defeat a scan (review's to catch). Controls: `CompiledDetector_FlagsEveryBypassShape_AndPassesTheSeam` (three
never-executed bypass shapes — the S5 inline-resolved receiver, an async lambda, a method group — each flagged, and the
seam's three writes not flagged, with the scan seen to read them); `Detector_…` gains the S5 shape, `(dv).`,
`GetClient().`, `shares[0].`, S6, a `var` local and an undeclared receiver as negatives, and `x?.` as a positive.

Comments corrected (each had claimed the build fails on ANY bypassing write): `IDataverseRecordShareService.cs`
(interface remarks now point at the class; class remarks list exactly the routes closed and the one not), 
`IMembershipCacheInvalidator.InvalidateRecordShareChangeAsync` remarks, §16.1 above, the POML f1 outcome (correction
appended, original wording kept for the record) and the guard file's own section header.

**Item 4 — the seam's own catch was untested: CLOSED.** `PoaSeam_WhenEvictionFails_TheShareWriteSucceeds_AndTheFailureIsLogged`
asserted the INVALIDATOR's warning (the production invalidator never throws, so the seam's catch never ran). Replaced by
`PoaSeam_WhenEvictionThrows_TheSeamCatchesAndLogs_AndTheWritesOwnOutcomeStands` ×6 (grant / modify / revoke × Dataverse
204 / 500): a `ThrowingInvalidator` double (every hook throws) under the production seam over the production client; a
successful write completes, a refused write surfaces ITS `HttpRequestException` (not the double's exception), the eviction
was attempted exactly once, and the seam's own `[ACCESS-EVICT] The share-change eviction for … ({write})` warning is logged.
The old case is kept under its true name, `PoaSeam_WhenRedisFails_TheInvalidatorLogsAndReturns_AndTheShareWriteSucceeds`
(asserting also that the seam logged nothing — it had nothing to catch).

**Seeds (each seeded in the production file, rebuilt, seen red, restored by `git checkout`, `git diff --quiet` asserted,
touched).**

| # | Seed | Red |
|---|---|---|
| S5 | verifier's `sp.GetRequiredService<Spaarke.Dataverse.DataverseWebApiService>().RevokeAccessAsync(…)` in `NoAccessShareEnforcer.cs` | **3** — compiled rule, text rule, `EachNamedShareWriter…(NoAccessShareEnforcer)` |
| S6 | verifier's `static Task SeedS6(DataverseWebApiService _recordShare, …) => _recordShare.RevokeAccessAsync(…)` in the same file | **3** — same three |
| S7 | a method group `=> client.RevokeAccessAsync;` on a `DataverseWebApiService` (no `(` — invisible to any text rule) | **1** — compiled rule only (proves the IL rule is needed, not just stricter) |
| S8 | `typeof(DataverseWebApiService).GetMethod("RevokeAccessAsync")!.Invoke(client, args)` | **1** — `NoServerFileNamesTheClientsPoaWritesAsAString` |
| S9 | a new `src/server/shared/SeedProbe/SeedProbe.csproj` referencing only `Spaarke.Core` (transitive reach) | **2** — `EveryProjectThatCanSeeTheClientIsScanned` (FileNotFoundException named) + the compiled rule (cannot load it) |
| S10 | the seam's catch narrowed to `catch (Exception ex) when (ex is null)` (verifier's item-4 seed) | **6** — every `PoaSeam_WhenEvictionThrows_…` case |
| S11 | the seam's catch kept but its `LogWarning` removed | **6** — every `PoaSeam_WhenEvictionThrows_…` case (the log assertion bites on its own) |

All restored byte-identical (asserted); the probe directory removed.

**Not code (unchanged):** item 8 — share writes cost 1-2 keyspace SCANs each; task 149's fan-out and the round-28
2-minute reconcile can issue them in bursts; trigger 8 was evaluated against the dev keyspace (≤ 178 keys) and does not
fire; watch a large production keyspace after deploy (an observation, not a gate). Items 10-11: G-1 / G-2 (§12) and the
criterion-17 reviewer approval at PR time (§15.3) are unchanged.

`.claude/**`: no edit needed.
