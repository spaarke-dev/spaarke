# Deferred: migrate ad-hoc `oid ⇄ systemuserid` resolvers onto `ISystemUserIdentityResolver`

> Status: DEFERRED (out of scope for the notification-spine identity fix) · 2026-07-21
> Owner surface: `src/server/api/Sprk.Bff.Api` (+ `src/server/shared/Spaarke.Dataverse`)

## What was built now

The notification-spine identity fix introduced ONE shared, cached, bidirectional resolver:

- `ISystemUserIdentityResolver` / `SystemUserIdentityResolver`
  (`src/server/api/Sprk.Bff.Api/Services/Identity/SystemUserIdentityResolver.cs`)
  - `ResolveOidAsync(Guid systemUserId)` → `systemuser.azureactivedirectoryobjectid`
  - `ResolveSystemUserIdAsync(string oid)` → `systemuserid` (via the same column, disabled users excluded)
  - Both directions cached in `IDistributedCache` (10-min TTL); wraps the singleton `IDataverseService`.
  - Registered unconditionally (Singleton) in `Infrastructure/DI/NotificationsModule.cs`.

Its first consumer is `SignalRDeliveryService.PingUserAsync`. Task 022's poll endpoint will be its second
(the reverse direction). This is the CLAUDE.md §11 consolidation point that `BriefingService.cs:376`
explicitly anticipated ("A future R4 task may extract both into a shared resolver once a third consumer
needs the same lookup").

## What is deferred (do NOT migrate now — high blast radius)

The following pre-existing ad-hoc private copies of the same `oid ⇄ systemuserid` (and one
`oid → contact`) cross-reference SHOULD be migrated onto `ISystemUserIdentityResolver` in a later,
dedicated consolidation task. They were NOT touched by this fix — each sits on a hot, separately-owned
path, and rewiring all of them at once would be a large, cross-cutting change with real regression risk
(auth/identity is security-sensitive; several have bespoke caching + fail-open semantics that must be
preserved exactly).

| # | Site | Direction | Notes |
|---|------|-----------|-------|
| 1 | `Services/Workspace/BriefingService.cs` (~360–456, `ResolveSystemUserIdAsync`) | oid → systemuserid | Own `IDistributedCache` cache (`membership:briefing-currentuser:`), 10-min TTL, isdisabled filter. The file that flagged the extraction. |
| 2 | `Services/DocumentCheckoutService.cs` (~832–838, `LookupDataverseUserIdAsync`) | oid → systemuserid | Raw Web API (`_httpClient`) call — different Dataverse access path; migrating requires moving it onto `IDataverseService`. |
| 3 | `src/server/shared/Spaarke.Dataverse/DataverseAccessDataSource.cs` (~260) | oid → systemuserid | In the shared lib; Web API filter string. Consumed by the authorization/privilege path. |
| 4 | `Api/Ai/AnalysisEndpoints.cs` (~819–842, `ResolveDataverseUserIdAsync`) | oid → systemuserid | Inline `QueryExpression` helper. |
| 5 | `Services/Dataverse/Privileges/UserPrivilegeChecker.cs` (~221) | oid → systemuserid | Inline `ConditionExpression`; privilege-check hot path. |
| 6 | `Services/Registration/RegistrationDataverseService.cs` (~496–502, `ResolveSystemUserIdByAadObjectIdAsync`) | oid → systemuserid | Web API `$filter`; registration/provisioning path. |
| — | `Api/Membership/MembershipEndpoints.cs` (`ResolveSystemUserIdAsync`) | oid → systemuserid | Tenant-scoped `ITenantCache` cache; the original "first surface" copy. |
| — | `Services/Ai/Context/CallerSystemUserResolver.cs` | oid → systemuserid (uncached) | Already an injectable service (AI-context scoped, `ClaimsPrincipal`-based, no cache). Overlaps the reverse direction — a later task should decide whether it collapses into `ISystemUserIdentityResolver` or stays an AI-context-specific façade over it. |
| — | `Services/Ai/Context/CallerContactResolver.cs` | oid → **contact** | Different target entity (`contact`, not `systemuser`); related but not a direct duplicate. |

## Concrete behavior that fails without the consolidation

**Drift between the copies' caching/TTL and the resolved column produces inconsistent identity results
across BFF surfaces.** Today each site independently decides: which cache (Redis vs tenant-cache vs none),
what TTL, whether to exclude `isdisabled`, and whether the AAD column is queried as a Guid or a string.
When those choices diverge, the SAME user can be resolved to a `systemuserid` by one surface (e.g. the
briefing) and to "no match" by another (e.g. a privilege check) within the same request window — a
correctness/authorization hazard, not merely duplicated code. A single resolver makes the column, the
disabled-user policy, the type-coercion, and the cache TTL identical everywhere. (This is the §11
"name a concrete failure mode" justification for the future task; "reduce duplication" alone would not be.)

## Suggested follow-up

File via `/defer` (writes `notes/defer-issues.md` + a GitHub Issue) as: "Migrate the 6+ ad-hoc
oid⇄systemuserid resolvers onto `ISystemUserIdentityResolver`" — one PR per owning surface to keep blast
radius bounded, preserving each site's fail-open semantics and adding a regression test per migrated site.
