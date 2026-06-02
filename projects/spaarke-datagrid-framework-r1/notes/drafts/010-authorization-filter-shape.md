# 010 — Canonical `DataverseAuthorizationFilter` Shape (Phase B)

> **Source ADRs**: ADR-008 (endpoint filters), ADR-019 (ProblemDetails), ADR-028 (Spaarke Auth v2)
> **Reference implementation** (model on this): `src/server/api/Sprk.Bff.Api/Api/Filters/SemanticSearchAuthorizationFilter.cs`
> **Reference implementation #2** (per-operation pattern): `src/server/api/Sprk.Bff.Api/Api/Filters/DocumentAuthorizationFilter.cs`
> **Scope**: 5 endpoints under `/api/dataverse/*` per FR-BFF-01..05
> **Consumers**: tasks 011, 012, 013, 014 (each endpoint will `.AddDataverseAuthorizationFilter(...)`); task 016 (integration test surface)
> **Status**: Draft (ready for review; once approved, becomes the implementation contract for tasks 011-014)

---

## 1. Goal

A single endpoint filter class — `DataverseAuthorizationFilter` — that:

1. Validates the caller has `Read` privilege on **every** Dataverse entity referenced by the request (primary entity AND every `<link-entity>` inside FetchXML payloads).
2. Returns 403 ProblemDetails with stable `errorCode` on any privilege deny (per ADR-019).
3. Caches privilege metadata aggressively to avoid per-request Dataverse round-trips.
4. Is configured per-endpoint with a strategy for resolving WHICH entity(ies) to check (route value, request body, FetchXML parse).

The filter is the **only** layer that prevents privilege bypass via crafted FetchXML — making cross-entity privilege check correctness security-critical (acceptance criterion #2 of task 010).

---

## 2. Class Signature & DI Surface

```csharp
namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// Authorization filter for Dataverse projection endpoints (FR-BFF-07).
/// Validates the caller has Read privilege on every entity referenced by the request,
/// including all link-entities in FetchXML payloads to prevent privilege bypass.
/// </summary>
/// <remarks>
/// Follows ADR-008 (endpoint-filter authorization), ADR-019 (ProblemDetails), ADR-028 (Spaarke Auth v2).
/// </remarks>
internal sealed class DataverseAuthorizationFilter : IEndpointFilter
{
    private readonly IDataversePrivilegeChecker _privilegeChecker;
    private readonly IFetchXmlEntityExtractor _fetchXmlExtractor;
    private readonly DataversePrivilegeCache _cache;
    private readonly ILogger<DataverseAuthorizationFilter> _logger;
    private readonly EntitySource _entitySource;
    private readonly Privilege _requiredPrivilege;

    // Constructor receives the per-endpoint configuration (entitySource + requiredPrivilege)
    // plus DI-injected dependencies. NOT instantiated by DI directly — wired by the
    // AddDataverseAuthorizationFilter() extension method (see §3).
    public DataverseAuthorizationFilter(
        IDataversePrivilegeChecker privilegeChecker,
        IFetchXmlEntityExtractor fetchXmlExtractor,
        DataversePrivilegeCache cache,
        ILogger<DataverseAuthorizationFilter> logger,
        EntitySource entitySource,
        Privilege requiredPrivilege = Privilege.Read);

    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next);
}
```

### Supporting types

```csharp
/// <summary>
/// Strategy for resolving which entity(ies) the filter must privilege-check on this endpoint.
/// </summary>
internal enum EntitySource
{
    /// <summary>Single entity from a route value (e.g., {entityLogicalName}).</summary>
    FromRouteValue,

    /// <summary>Single entity derived by looking up a SavedQuery (e.g., {savedQueryId} → ReturnedTypeCode).</summary>
    FromSavedQueryEntity,

    /// <summary>Primary entity + every link-entity inside the FetchXML body (FR-BFF-04).</summary>
    FromFetchXmlBody,

    /// <summary>Single entity from a route value with the record ID also validated for existence (FR-BFF-05).</summary>
    FromRouteValueWithRecord
}

internal enum Privilege { Read, Write, Delete, Create, Append, AppendTo, Assign, Share }

/// <summary>
/// Encapsulates the per-call privilege check.
/// Implementation lives in Services/Dataverse/DataversePrivilegeChecker.cs (task 011 designs).
/// </summary>
internal interface IDataversePrivilegeChecker
{
    /// <summary>
    /// Returns true if the caller (identified by their oid) has the specified privilege on the entity.
    /// Honors the cache; cache miss triggers a Dataverse RetrievePrincipalAccessRequest or equivalent.
    /// </summary>
    Task<bool> HasPrivilegeAsync(
        Guid userObjectId,
        string entityLogicalName,
        Privilege privilege,
        CancellationToken ct);
}

/// <summary>
/// Parses FetchXML and returns the set of distinct entity logical names referenced
/// (primary entity + every link-entity, recursively for nested link-entity).
/// Implementation lives in Services/Dataverse/FetchXml/FetchXmlEntityExtractor.cs (task 011 designs).
/// </summary>
internal interface IFetchXmlEntityExtractor
{
    /// <summary>
    /// Returns the distinct set of entity logical names in document order
    /// (primary first, then link-entities in tree order).
    /// Throws FetchXmlParseException on malformed XML — filter maps that to 400 ProblemDetails.
    /// </summary>
    IReadOnlyList<string> ExtractEntities(string fetchXml);
}
```

---

## 3. Extension Method (per-endpoint wiring)

Modeled on `SemanticSearchAuthorizationFilterExtensions` lines 1-24:

```csharp
namespace Sprk.Bff.Api.Api.Filters;

internal static class DataverseAuthorizationFilterExtensions
{
    /// <summary>
    /// Adds the Dataverse authorization filter to an endpoint.
    /// </summary>
    /// <param name="entitySource">Where the filter finds the entity(ies) to privilege-check.</param>
    /// <param name="requiredPrivilege">The privilege to check (default Read for R1).</param>
    public static TBuilder AddDataverseAuthorizationFilter<TBuilder>(
        this TBuilder builder,
        EntitySource entitySource,
        Privilege requiredPrivilege = Privilege.Read) where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var sp = context.HttpContext.RequestServices;
            var filter = new DataverseAuthorizationFilter(
                sp.GetRequiredService<IDataversePrivilegeChecker>(),
                sp.GetRequiredService<IFetchXmlEntityExtractor>(),
                sp.GetRequiredService<DataversePrivilegeCache>(),
                sp.GetRequiredService<ILogger<DataverseAuthorizationFilter>>(),
                entitySource,
                requiredPrivilege);
            return await filter.InvokeAsync(context, next);
        });
    }
}
```

### Endpoint usage (task 011-014 will instantiate)

```csharp
// FR-BFF-01 — GET /api/dataverse/savedquery/{savedQueryId}
group.MapGet("/savedquery/{savedQueryId:guid}", GetSavedQuery)
    .AddDataverseAuthorizationFilter(EntitySource.FromSavedQueryEntity);

// FR-BFF-02 — GET /api/dataverse/savedqueries/{entityLogicalName}
group.MapGet("/savedqueries/{entityLogicalName}", ListSavedQueries)
    .AddDataverseAuthorizationFilter(EntitySource.FromRouteValue);

// FR-BFF-03 — GET /api/dataverse/metadata/{entityLogicalName}
group.MapGet("/metadata/{entityLogicalName}", GetEntityMetadata)
    .AddDataverseAuthorizationFilter(EntitySource.FromRouteValue);

// FR-BFF-04 — POST /api/dataverse/fetch  (security-critical: cross-entity check)
group.MapPost("/fetch", ExecuteFetch)
    .AddDataverseAuthorizationFilter(EntitySource.FromFetchXmlBody);

// FR-BFF-05 — GET /api/dataverse/record/{entityLogicalName}/{id}
group.MapGet("/record/{entityLogicalName}/{id:guid}", GetRecord)
    .AddDataverseAuthorizationFilter(EntitySource.FromRouteValueWithRecord);
```

---

## 4. `InvokeAsync` Flow (canonical)

```text
┌──────────────────────────────────────────────────────────────────────────────────┐
│ Step 1. Identity extraction                                                     │
│   userOid = HttpContext.User.FindFirst("oid")?.Value                             │
│           ?? HttpContext.User.FindFirst("http://.../objectidentifier")?.Value    │
│           ?? HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value        │
│   tenantId = HttpContext.User.FindFirst("tid")?.Value (multi-tenant audit)      │
│                                                                                  │
│   IF userOid is null OR not a Guid → return 401 ProblemDetails                  │
│     errorCode: "DV_NO_USER_IDENTITY"                                            │
│     title: "Unauthorized"                                                       │
│     detail: "User identity not found in authentication token"                   │
│                                                                                  │
│ Step 2. Resolve entity(ies) to check (strategy depends on EntitySource)         │
│   switch (_entitySource) {                                                      │
│     FromRouteValue:                                                              │
│       entityLogicalName = RouteValues["entityLogicalName"]?.ToString()          │
│       entities = [entityLogicalName]                                            │
│     FromRouteValueWithRecord:                                                    │
│       entityLogicalName = RouteValues["entityLogicalName"]?.ToString()          │
│       // record id is checked by the handler; filter only validates entity      │
│       entities = [entityLogicalName]                                            │
│     FromSavedQueryEntity:                                                        │
│       savedQueryId = RouteValues["savedQueryId"]?.ToString()                    │
│       entityLogicalName =                                                       │
│         await _privilegeChecker.GetSavedQueryEntityAsync(savedQueryId)          │
│         (the service caches savedquery→entity mapping; same cache as FR-BFF-01) │
│       IF null → return 404 ProblemDetails errorCode: "DV_SAVEDQUERY_NOT_FOUND"  │
│       entities = [entityLogicalName]                                            │
│     FromFetchXmlBody:                                                            │
│       request = ExtractFetchRequest(context) // shape: { entityName, fetchXml } │
│       IF request is null → let endpoint handle (will 400 from model binding)    │
│       try {                                                                      │
│         allEntities = _fetchXmlExtractor.ExtractEntities(request.FetchXml)      │
│       } catch (FetchXmlParseException ex) {                                     │
│         return 400 ProblemDetails errorCode: "DV_FETCHXML_MALFORMED"            │
│         detail: ex.Message (sanitized — no raw user input echoed back)          │
│       }                                                                          │
│       // Validate primary entity in body matches first extracted entity         │
│       IF !string.Equals(allEntities[0], request.EntityName,                     │
│                          StringComparison.OrdinalIgnoreCase)                    │
│         → return 400 ProblemDetails errorCode: "DV_FETCHXML_ENTITY_MISMATCH"    │
│       entities = allEntities  // PRIMARY + every link-entity                    │
│   }                                                                              │
│                                                                                  │
│   IF entities is empty OR contains null/whitespace                               │
│     → return 400 ProblemDetails errorCode: "DV_NO_TARGET_ENTITY"                │
│                                                                                  │
│ Step 3. Privilege check (cached) — one round-trip per distinct entity, in       │
│         parallel via Task.WhenAll. Cache-hit short-circuits to in-process.       │
│   IReadOnlyList<string> distinct = entities.Distinct(OrdinalIgnoreCase).ToList()│
│   bool[] results = await Task.WhenAll(distinct.Select(e =>                      │
│       _privilegeChecker.HasPrivilegeAsync(userOid, e, _requiredPrivilege, ct)   │
│   ))                                                                             │
│                                                                                  │
│   // Find ALL denied entities (not just first) — log them all for debugging     │
│   var denied = distinct.Zip(results, (entity, allowed) => (entity, allowed))    │
│                        .Where(t => !t.allowed)                                  │
│                        .Select(t => t.entity)                                   │
│                        .ToList();                                               │
│                                                                                  │
│   IF denied.Count > 0:                                                          │
│     _logger.LogWarning(                                                          │
│       "Dataverse authorization denied: user={UserOid}, tenant={TenantId},       │
│        privilege={Privilege}, deniedEntities={DeniedEntities}",                 │
│       userOid, tenantId, _requiredPrivilege, denied)                            │
│                                                                                  │
│     // 403 ProblemDetails — list ONLY the first denied entity in `detail` to    │
│     // avoid information disclosure about other entities in the query           │
│     return Results.Problem(                                                      │
│       statusCode: 403,                                                          │
│       title: "Forbidden",                                                       │
│       detail: $"Read privilege denied on entity '{denied[0]}'",                 │
│       extensions: new Dictionary<string, object?> {                             │
│         ["errorCode"] = "DV_PRIVILEGE_DENIED",                                  │
│         ["correlationId"] = context.HttpContext.TraceIdentifier                 │
│       })                                                                         │
│                                                                                  │
│ Step 4. Log success + delegate                                                  │
│   _logger.LogInformation(                                                        │
│     "Dataverse authorization granted: user={UserOid}, tenant={TenantId},        │
│      privilege={Privilege}, entities={Entities}, entitySource={EntitySource}",  │
│     userOid, tenantId, _requiredPrivilege, distinct, _entitySource)             │
│                                                                                  │
│   return await next(context)                                                    │
└──────────────────────────────────────────────────────────────────────────────────┘
```

---

## 5. Cross-Entity Privilege Check — Security-Critical

**Why this matters (acceptance criterion #2 of task 010):**

A caller with Read privilege on `sprk_matter` could otherwise craft this FetchXML against `/api/dataverse/fetch`:

```xml
<fetch>
  <entity name="sprk_matter">
    <attribute name="sprk_matterid" />
    <link-entity name="sprk_financialdetail" from="sprk_matterid" to="sprk_matterid">
      <attribute name="sprk_totalamount" />     <!-- caller has NO read on financial -->
      <attribute name="sprk_profitmargin" />
    </link-entity>
  </entity>
</fetch>
```

…and read every financial detail row for every matter they're allowed to see. **Dataverse server-side privilege enforcement does NOT cascade through `<link-entity>`** — the BFF MUST enforce it.

**The filter's mitigation:**

`EntitySource.FromFetchXmlBody` → calls `IFetchXmlEntityExtractor.ExtractEntities(fetchXml)` → returns `["sprk_matter", "sprk_financialdetail"]` → privilege check is run on BOTH → 403 if either denies.

**Recursion**: `IFetchXmlEntityExtractor` MUST walk nested `<link-entity>` (link-entity inside link-entity), not just top-level. Task 011's implementation will use `XDocument.Descendants("link-entity").Select(e => e.Attribute("name")?.Value)` (the namespace-agnostic XLinq pattern) to ensure depth-N traversal.

**Negative-case examples** the unit tests MUST cover (see §8):

1. `<link-entity name="sprk_financialdetail">` 2 levels deep inside primary entity — MUST be detected.
2. `<link-entity name="sprk_financialdetail" intersect="true">` (many-to-many bridge) — MUST be detected; the bridge entity name AND the linked entity name both need privilege check.
3. FetchXML with `<link-entity link-type="outer">` — privilege check still required; LEFT OUTER join still leaks rows.

---

## 6. Cache Strategy

### Privilege metadata cache

| Key shape | Value | TTL | Eviction |
|---|---|---|---|
| `sdap:dv:privilege:{userOid}:{entityLogicalName}:{privilege}` | `bool` (allowed) | **6 hours** | Sliding (refreshed on hit) within a per-key 24h absolute max |

**Rationale**:
- 6-hour TTL matches the entity-metadata cache from FR-BFF-03 (same admin-action cadence — solution import, security role change).
- Per-user keying (not per-team) — privilege grants flow through team membership but the effective per-user privilege is what we cache. Avoids the complexity of invalidating on team membership change.
- Boolean value — minimal cache size; ~thousands of users × ~50 entities × ~8 privileges = ~400k entries; at <1 KB per entry, well within Redis budget.

### Entity-metadata cache (FR-BFF-03)

| Key shape | Value | TTL |
|---|---|---|
| `sdap:dv:entitymetadata:{entityLogicalName}:v{globalMetadataVersion}` | projected `EntityMetadataDto` JSON | 6h |

Version-pinned via `globalMetadataVersion` (Dataverse `RetrieveAllEntities` metadata `ServerVersionStamp`). Stale data is never served because the key changes when the stamp changes (same pattern as `GraphMetadataCache.cs` ETag-versioned keys, lines 53-58 of that file).

### SavedQuery cache (FR-BFF-01/02)

| Key shape | Value | TTL |
|---|---|---|
| `sdap:dv:savedquery:{savedQueryId}` | `SavedQueryDto` JSON (entityName, fetchXml, layoutXml, name) | 1h |
| `sdap:dv:savedqueries:{entityLogicalName}` | `SavedQueryListItem[]` JSON | 1h |
| `sdap:dv:savedquery-entity:{savedQueryId}` | `string` (entityLogicalName) — for `EntitySource.FromSavedQueryEntity` lookup | 1h |

The third key (`savedquery-entity`) supports `EntitySource.FromSavedQueryEntity` in `InvokeAsync` Step 2 without a round-trip when the filter precedes the handler.

### Cache backend

Use the existing `IDistributedCache` Redis instance registered at app startup (`Microsoft.Extensions.Caching.StackExchangeRedis` 10.0.1 — already in `Sprk.Bff.Api.csproj`). No separate Redis instance per `azure-deployment.md`. The same pattern that backs `GraphMetadataCache.cs` already in production.

**Failure mode**: cache is graceful — if Redis is unreachable, the privilege checker MUST fall back to a direct Dataverse call AND log a warning (cache failures MUST NOT block the request, per the same pattern as `GraphMetadataCache.cs`). This is conservative correctness: slower, but never returns wrong privilege answers.

### Cache invalidation

- Privilege cache: TTL-driven (6h). Security-role changes propagate within 6h; acceptable for R1 (no real-time invalidation required).
- Entity-metadata cache: version-pinned via `globalMetadataVersion` — instant on solution import (Dataverse stamps the new version; next request misses).
- SavedQuery cache: TTL-driven (1h). Saved-query edits propagate within 1h; acceptable for R1.

---

## 7. ProblemDetails Error Code Catalog

| `errorCode` extension | HTTP | When |
|---|---|---|
| `DV_NO_USER_IDENTITY` | 401 | Token has no `oid` / `tid` claim or `oid` is not a Guid |
| `DV_NO_TARGET_ENTITY` | 400 | Filter could not resolve an entity to check (e.g., missing route value) |
| `DV_FETCHXML_MALFORMED` | 400 | XLinq parse failed or schema violation |
| `DV_FETCHXML_ENTITY_MISMATCH` | 400 | `entityName` in request body does not match primary entity in FetchXML |
| `DV_SAVEDQUERY_NOT_FOUND` | 404 | `savedQueryId` route value does not resolve to a savedquery row |
| `DV_PRIVILEGE_DENIED` | 403 | Caller lacks required privilege on one or more referenced entities |
| `DV_UPSTREAM_TIMEOUT` | 504 | Dataverse RetrievePrincipalAccess timed out (>5s) |
| `DV_INTERNAL_ERROR` | 500 | Unexpected exception in filter; logged with full stack at Error level |

All responses include `extensions["correlationId"] = HttpContext.TraceIdentifier` per ADR-019.

---

## 8. Unit Test Points (task 016 — or sibling unit-test task — owns these)

The filter shape implies these test cases. Each MUST exist before tasks 011-014 endpoints ship to PR.

| # | Test | Expected |
|---|---|---|
| 1 | **Happy path — single entity, privilege granted** | Filter invokes `next(context)` once; cache hit; no 403 returned |
| 2 | **403 deny — single entity, privilege missing** | Returns `Results.Problem(403, errorCode=DV_PRIVILEGE_DENIED)`; `next` NOT invoked; warning logged with user oid + denied entity |
| 3 | **403 deny — cross-entity bypass attempt via `<link-entity>`** | FetchXML body declares `sprk_matter` as primary but includes `<link-entity name="sprk_financialdetail">`; caller has Read on matter but NOT financial; filter MUST return 403 with `errorCode=DV_PRIVILEGE_DENIED` and denied entity = financial in the log |
| 4 | **403 deny — nested `<link-entity>` 2 levels deep** | FetchXML has `<link-entity name="sprk_invoice"><link-entity name="sprk_vendor"></link-entity></link-entity>`; caller lacks Read on vendor; filter MUST detect vendor via depth-N traversal and return 403 |
| 5 | **400 — malformed FetchXML** | Body contains invalid XML; filter MUST return `Results.Problem(400, errorCode=DV_FETCHXML_MALFORMED)`; `next` NOT invoked |
| 6 | **400 — entity mismatch** | Body's `entityName=sprk_matter` but FetchXML's primary `<entity name="sprk_invoice">`; filter MUST return 400 `errorCode=DV_FETCHXML_ENTITY_MISMATCH` |
| 7 | **401 — no user identity in token** | Token has no `oid` claim; filter MUST return 401 `errorCode=DV_NO_USER_IDENTITY` |
| 8 | **Cache hit short-circuits Dataverse call** | First call populates cache; second call within TTL MUST NOT call `IDataversePrivilegeChecker.HasPrivilegeAsync` against the real service (verified via Mock `Verify(..., Times.Once)` after 2 invocations) |
| 9 | **Cache miss fallback when Redis unreachable** | Inject failing `IDistributedCache`; filter MUST still issue direct Dataverse call; warning logged; correct allow/deny result still returned |
| 10 | **Parallel privilege checks for distinct entities in FetchXML** | FetchXML with 5 distinct entities; filter MUST issue 5 `HasPrivilegeAsync` calls in parallel (verified via Mock concurrent-invocation counter) — not serial — to meet <500ms p50 budget |

**Tag every test** `[Trait("status", "repaired")]` per `bff-extensions.md §F.4`. Use the canonical `IntegrationTestFixture` (NOT a one-off `WebApplicationFactory`) per §F.3.

---

## 9. What this filter does NOT do

- **Does NOT validate the FetchXML schema beyond entity extraction** — that's the handler's responsibility (it'll surface as 400 from the Dataverse SDK on a malformed query at execution time).
- **Does NOT enforce row-level access** — Dataverse RBAC enforces row-level access server-side at query time via the OBO/CallerId. The filter only validates entity-level Read privilege exists.
- **Does NOT cache 403 deny decisions for the same `(user, entity, privilege)` separately** — the boolean result is cached uniformly; deny is just `false`. Acceptable for R1; if denied-result thrashing emerges as a metric problem, revisit in R2.
- **Does NOT enforce field-level security** — out of scope for R1. If a caller has Read on the entity, the BFF projection layer returns the fields. Field-level security enforcement happens server-side via the OBO context, not in the filter.
- **Does NOT validate per-record access** for `/api/dataverse/record/{entity}/{id}` — the handler relies on OBO/CallerId returning 404 from Dataverse if the caller cannot read the specific row. Filter only validates entity-level Read.

---

## 10. Implementation Notes for Task 011 (the owner who writes this filter)

1. **Internal sealed class** — per the `SemanticSearchAuthorizationFilter` precedent (line 51 of that file uses `public class` but our additions per ADR-010 / spec.md preference favor `internal sealed` to minimize public API surface and prevent unintended subclassing). Confirm with project owner if you have a different style preference.
2. **DI registration** — the filter itself is NOT registered (it's `new`'d up inside the extension method). The dependencies it needs (`IDataversePrivilegeChecker`, `IFetchXmlEntityExtractor`, `DataversePrivilegeCache`, logger) ARE registered in `AddDataverseProjectionModule()`.
3. **No `try/catch` swallow** — let unexpected exceptions bubble; the standard `ExceptionHandler` middleware returns 500 ProblemDetails. The filter only catches `FetchXmlParseException` (expected) and timeouts (expected).
4. **Logging level discipline** — `Information` for granted (audit trail; high volume but searchable); `Warning` for denied (security signal; low volume in normal operation); `Error` only for unexpected exceptions.
5. **Do NOT log the FetchXML body** — it may contain sensitive data (entity IDs, filter values). Log only the extracted entity list.
6. **Set `EndpointFilterDelegate` short-circuit correctly** — returning a non-null `IResult` from `InvokeAsync` short-circuits without calling `next`. The `SemanticSearchAuthorizationFilter` pattern (lines 64-110) is the canonical reference.

---

## 11. Pseudocode Skeleton (for task 011 starting point)

```csharp
public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
{
    var httpContext = context.HttpContext;
    var ct = httpContext.RequestAborted;

    // Step 1: Identity
    var userOidStr = httpContext.User.FindFirst("oid")?.Value
        ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
        ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (!Guid.TryParse(userOidStr, out var userOid))
    {
        return DataverseProblem(401, "Unauthorized", "User identity not found", "DV_NO_USER_IDENTITY", httpContext);
    }
    var tenantId = httpContext.User.FindFirst("tid")?.Value;

    // Step 2: Resolve entities
    IReadOnlyList<string> entities;
    try
    {
        entities = await ResolveEntitiesAsync(context, ct);
    }
    catch (FetchXmlParseException ex)
    {
        return DataverseProblem(400, "Bad Request", ex.SanitizedMessage, "DV_FETCHXML_MALFORMED", httpContext);
    }
    catch (SavedQueryNotFoundException)
    {
        return DataverseProblem(404, "Not Found", "Saved query not found", "DV_SAVEDQUERY_NOT_FOUND", httpContext);
    }

    if (entities is null || entities.Count == 0 || entities.Any(string.IsNullOrWhiteSpace))
    {
        return DataverseProblem(400, "Bad Request", "Target entity not resolvable from request", "DV_NO_TARGET_ENTITY", httpContext);
    }

    // Step 3: Parallel privilege checks
    var distinct = entities.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    var results = await Task.WhenAll(distinct.Select(e =>
        _privilegeChecker.HasPrivilegeAsync(userOid, e, _requiredPrivilege, ct)));

    var denied = distinct.Zip(results, (e, ok) => (entity: e, ok))
                         .Where(t => !t.ok)
                         .Select(t => t.entity)
                         .ToList();

    if (denied.Count > 0)
    {
        _logger.LogWarning(
            "Dataverse authorization denied: user={UserOid}, tenant={TenantId}, privilege={Privilege}, deniedEntities={DeniedEntities}",
            userOid, tenantId, _requiredPrivilege, denied);
        return DataverseProblem(403, "Forbidden", $"Read privilege denied on entity '{denied[0]}'", "DV_PRIVILEGE_DENIED", httpContext);
    }

    // Step 4: Log + delegate
    _logger.LogInformation(
        "Dataverse authorization granted: user={UserOid}, tenant={TenantId}, privilege={Privilege}, entities={Entities}, entitySource={EntitySource}",
        userOid, tenantId, _requiredPrivilege, distinct, _entitySource);

    return await next(context);
}

private static IResult DataverseProblem(int status, string title, string detail, string errorCode, HttpContext ctx) =>
    Results.Problem(
        statusCode: status,
        title: title,
        detail: detail,
        extensions: new Dictionary<string, object?>
        {
            ["errorCode"] = errorCode,
            ["correlationId"] = ctx.TraceIdentifier
        });
```

---

## 12. Acceptance Mapping

| Task 010 acceptance criterion | Where addressed in this doc |
|---|---|
| Cross-entity FetchXML privilege check explicitly addressed | §5 (in full), §4 Step 2 `FromFetchXmlBody`, §8 tests #3 + #4 |
| 403 ProblemDetails (not raw 403) | §4 Step 3 + §7 catalog + §11 helper |
| Filter pattern matches ADR-008 + ADR-019 | §2-3 (class signature + extension), §11 pseudocode |
| Cache strategy documented | §6 (privilege + metadata + savedquery + invalidation) |
| Unit test points listed (4-6 minimum) | §8 (10 cases) |

---

**Lines**: ~370 (acceptable for a security-critical design doc; comparable to ADR-008 + ADR-019 combined).
