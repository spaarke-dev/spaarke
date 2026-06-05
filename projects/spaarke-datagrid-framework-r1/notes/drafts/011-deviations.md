# 011 — Deviations from Task 010 Design

> **Status**: Recorded at task 011 completion (2026-06-01)
> **Filer**: task 011 sub-agent
> **Audience**: project owner + sibling agents (012, 013, 014) + reviewer at PR time

---

## D1. `EntitySource.FromSavedQueryEntity` — privilege check moved into the handler (not the filter)

### Task 010 design intent (`010-authorization-filter-shape.md §4 Step 2 FromSavedQueryEntity`)
The filter resolves `savedQueryId → entityLogicalName` via an injected `IDataversePrivilegeChecker.GetSavedQueryEntityAsync(...)` method, then performs the privilege check inside the filter, same as every other endpoint.

### What 011 actually shipped
The filter is **not applied** to `GET /api/dataverse/savedquery/{savedQueryId}`. Instead the handler (`SavedQueryEndpoints.GetSavedQueryByIdAsync`) performs the full privilege check after the cached `SavedQueryDto` is hydrated. The handler:

1. Extracts the caller's Azure AD `oid` from claims.
2. Calls `SavedQueryService.GetSavedQueryAsync(savedQueryId, ct)` (cached, 1-hour TTL).
3. If the savedquery is missing or has no `EntityName`, returns 404 (`DV_SAVEDQUERY_NOT_FOUND`).
4. Calls `IDataversePrivilegeChecker.HasReadPrivilegeAsync(userOid, dto.EntityName, ct)` and returns 403 (`DV_PRIVILEGE_DENIED`) on deny.

### Why
The filter shape requires a synchronous `Get → entityLogicalName` resolution to keep the filter's `InvokeAsync` cohesive. Adding a `GetSavedQueryEntityAsync` to `IDataversePrivilegeChecker` would have either:

1. Required a second cache key (`sdap:dv:savedquery-entity:{savedQueryId}`) that duplicates the data already in the `SavedQueryDto` cache (`sdap:dv:savedquery:{savedQueryId}`), violating the "single source of truth" cache principle, OR
2. Required two cache hits per request (the filter's mapping lookup + the handler's `SavedQueryService` lookup), undercutting the 1-hour-cache rationale and adding a third Dataverse round-trip on cold misses.

Moving the check into the handler:

- Uses the **same** cached `SavedQueryDto` for both the entity-name resolution and the response payload (one lookup, not two).
- Keeps the privilege-check contract identical (same `IDataversePrivilegeChecker.HasReadPrivilegeAsync`, same `DV_PRIVILEGE_DENIED` error code, same log shape).
- Preserves the security guarantee: the handler validates privilege **before** returning the payload to the caller. 403 is identical in shape to the filter-path 403.

### Impact on the filter contract
- `EntitySource.FromSavedQueryEntity` is **defined in the enum** but the filter implementation throws `InvalidOperationException` if it ever sees that value, because the design now expects the handler to short-circuit before the filter runs. Tasks 012-014 should NOT use `FromSavedQueryEntity`; future endpoints that need this pattern should follow the same handler-side check approach.

### Risk
- **Asymmetry across endpoints**: most `/api/dataverse/*` endpoints use the filter; `GET /api/dataverse/savedquery/{id}` uses handler-side enforcement. Mitigation: documented here + clear precondition logging at the deny site.
- **Test coverage**: task 016 must add a unit test for the savedquery handler that specifically verifies the 403 branch (caller without Read on the savedquery's entity gets 403 with `errorCode=DV_PRIVILEGE_DENIED`). This is the analogue of test case #2 in `010-authorization-filter-shape.md §8`.

---

## D2. `IDataversePrivilegeChecker` lives in `Sprk.Bff.Api/Services/Dataverse/Privileges/` (not `Spaarke.Dataverse`)

### Task 010 open question Q1 third bullet
> ☐ Confirm: where does the implementation live? `Services/Dataverse/DataversePrivilegeChecker.cs` (BFF) OR `Spaarke.Dataverse/PrivilegeService.cs` (shared, reusable by plugins later)?

### Decision
Lives in the BFF (`Sprk.Bff.Api/Services/Dataverse/Privileges/`). Rationale:

- The privilege checker depends on `IDistributedCache` (the BFF's Redis instance) — `Spaarke.Dataverse` would need a hard dependency on `Microsoft.Extensions.Caching.Abstractions` (already there at 10.0.1, so this isn't a blocker) AND on the BFF's Redis configuration patterns to actually be useful.
- No plugin consumer exists in R1. Plugins run inside the Dataverse sandbox and do not have access to the BFF's Redis. Moving the implementation to `Spaarke.Dataverse` now would speculate against a non-existing requirement.
- R2 can promote the implementation if a plugin consumer emerges. The interface contract is intentionally `internal` to `Sprk.Bff.Api` so this promotion would be a single move + scope widening (no API breakage).

---

## D3. `IFetchXmlEntityExtractor` — interface only; implementation deferred to task 013

### Task 010 design (§2 supporting types)
The filter declares `IFetchXmlEntityExtractor` and consumes it. The design says the implementation lives in `Services/Dataverse/FetchXml/FetchXmlEntityExtractor.cs`.

### What 011 shipped
The **interface and exception** at `Services/Dataverse/FetchXml/IFetchXmlEntityExtractor.cs`. The concrete `FetchXmlEntityExtractor.cs` is owned by task 013 per the task 011 prompt's "DO NOT" boundary and the task 010 open-question Q2 resolution ("Task 013 owns this since FetchService is the only runtime consumer").

### Runtime resolution
`DataverseAuthorizationFilter` resolves `IFetchXmlEntityExtractor` via `sp.GetService<>()` (NOT `GetRequiredService<>()`) so the filter constructs successfully even when task 013 has not yet wired its DI. When `EntitySource.FromFetchXmlBody` is invoked without the implementation registered, the filter throws `InvalidOperationException` → 400 `DV_NO_TARGET_ENTITY`. Task 013 will register the concrete in its own DI extension method and wire it onto `POST /api/dataverse/fetch`.

---

## D4. `Spaarke.Dataverse.Privileges` namespace (not `Sprk.Bff.Api.Api.Filters`)

### Task 010 design (§2)
> `namespace Sprk.Bff.Api.Api.Filters;`

### What 011 shipped
The filter lives in `Sprk.Bff.Api.Services.Dataverse` namespace (file path `Services/Dataverse/DataverseAuthorizationFilter.cs`). Rationale:

- Consistent with the task 011 prompt's file-path direction: "`src/server/api/Sprk.Bff.Api/Services/Dataverse/DataverseAuthorizationFilter.cs`".
- The filter shares its folder with `SavedQueryService.cs`, `IDataversePrivilegeChecker`, and `IFetchXmlEntityExtractor` — they are part of the same feature subsystem and cohabitation keeps the import graph local.
- The `Api/Filters/` folder is reserved for filters that consume domain-agnostic services (e.g., `DocumentAuthorizationFilter` uses `AuthorizationService` from `Spaarke.Core.Auth`); the Dataverse filter consumes a feature-specific privilege checker that lives next to the filter.

The supporting `DataverseAuthorizationFilterExtensions` class is co-located in the same file so the per-endpoint wiring stays one import away from the filter implementation.

---

## D5. ServiceClient access pattern — pattern-match on `DataverseServiceClientImpl`

### Design implication
The filter and `SavedQueryService` both need access to the raw `ServiceClient` (for `RetrieveUserPrivileges`, `RetrievePrincipalAccess`, FetchXML execution, etc.).

### What 011 shipped
Both services inject `IDataverseService` (the composite interface registered as a singleton in `GraphModule`) and then pattern-match it to `DataverseServiceClientImpl` to access the `OrganizationService` property:

```csharp
if (_dataverseService is not DataverseServiceClientImpl impl)
{
    _logger.LogError(...);
    return EmptySet(); // fail closed
}
var serviceClient = impl.OrganizationService;
```

This avoids registering a separate `ServiceClient` singleton in DI (no new lifetime to reason about), reuses the existing connection pool (singleton, same instance for the entire process per `GraphModule.cs:46-52`), and degrades gracefully if a future test substitutes a mock `IDataverseService` (returns empty set, logs the discriminator failure).

### Alternative considered
Register the raw `ServiceClient` in DI directly (`services.AddSingleton(sp => ((DataverseServiceClientImpl)sp.GetRequiredService<IDataverseService>()).OrganizationService)`). Rejected because it duplicates the singleton lifetime and adds a second resolution path to maintain.

---

## D6. `ServiceClient.Clone()` for CallerId impersonation

### Why
Setting `CallerId` directly on the singleton `ServiceClient` would create a race condition under concurrent requests — request B could observe request A's CallerId mid-flight. Microsoft documents `ServiceClient.Clone()` as the thread-safe per-call pattern.

### What 011 shipped
`UserPrivilegeChecker` clones the singleton, sets `CallerId = systemUserId` on the clone, executes the request, then disposes the clone. The singleton's `CallerId` is never mutated.

### Cost
Each privilege fetch performs one `Clone()` + one `Dispose()`. `ServiceClient.Clone()` is documented as cheap (shares the underlying connection pool). Privilege fetches are cache-misses only — at the 6h sliding TTL, this happens roughly once per user per shift.

---

## Notes for sibling agents (012, 013, 014)

### Contract reuse
- **`IDataversePrivilegeChecker`** (internal, `Sprk.Bff.Api.Services.Dataverse.Privileges`): use `HasReadPrivilegeAsync(userOid, entityLogicalName, ct)` for single-entity checks, `GetReadableEntitiesAsync(userOid, ct)` for multi-entity (FetchXML) checks. DI-registered as singleton in `AddDataverseSavedQueryServices()`.
- **`IFetchXmlEntityExtractor`** (internal, `Sprk.Bff.Api.Services.Dataverse.FetchXml`): task 013 implements; filter resolves via `GetService<>()` (optional dependency).
- **`DataverseAuthorizationFilter`** + **`DataverseAuthorizationFilterExtensions.AddDataverseAuthorizationFilter<TBuilder>(EntitySource, string routeKey = "entityLogicalName")`**: apply to your endpoint via `.AddDataverseAuthorizationFilter(EntitySource.FromRouteValue)` for metadata endpoint (012), `.AddDataverseAuthorizationFilter(EntitySource.FromFetchXmlBody)` for fetch endpoint (013), `.AddDataverseAuthorizationFilter(EntitySource.FromRouteValueWithRecord)` for record-by-id endpoint (014).

### DI registration aggregation
Each B-Wave-1 task ships its own `Add{Feature}Services` extension method. The main session will compose these in `Program.cs` after the wave completes. Task 011's extension is `services.AddDataverseSavedQueryServices()`.

### Error code catalog
Follow `010-authorization-filter-shape.md §7` strictly — DO NOT invent new error codes. Reuse: `DV_NO_USER_IDENTITY`, `DV_NO_TARGET_ENTITY`, `DV_FETCHXML_MALFORMED`, `DV_FETCHXML_ENTITY_MISMATCH`, `DV_SAVEDQUERY_NOT_FOUND`, `DV_PRIVILEGE_DENIED`, `DV_UPSTREAM_TIMEOUT`, `DV_INTERNAL_ERROR`.

### Cache key namespace
All Dataverse projection caches use the `sdap:dv:*` prefix. Task 011 owns `sdap:dv:privilege:*`, `sdap:dv:savedquery:*`, `sdap:dv:savedqueries:*`. Task 012 will own `sdap:dv:entitymetadata:*`. Task 013 has no cache (per-request execution).

---

## Sign-off

- [x] Task 011 POML status: `completed`
- [x] Build green (zero errors)
- [x] No new HIGH CVEs introduced (pre-existing `Microsoft.Kiota.Abstractions 1.21.2` HIGH remains — not in scope of task 011)
- [x] Deviations documented above for project owner review at PR time
