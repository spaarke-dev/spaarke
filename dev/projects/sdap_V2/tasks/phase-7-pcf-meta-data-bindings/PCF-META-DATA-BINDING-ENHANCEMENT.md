# PCT-META-DATA-BINDING-ENHANCEMENT

**Scope:** Implement a robust, host-agnostic way for the SDAP SPE Quick Create solution to resolve the correct Dataverse navigation property and entity set names for `@odata.bind`, without relying on PowerShell or PCF metadata calls that are blocked or brittle.
**Target:** Model-driven apps, Custom Pages, and future hosts.
**Current org:** `https://spaarkedev1.crm.dynamics.com`
**Concrete example:**

* Parent table: `sprk_matter` (entity set typically `sprk_matters`)
* Child table: `sprk_document`
* Child lookup to parent: `sprk_matter`
* Relationship schema (parent→child): `sprk_matter_document`

---

## 1) Objectives

* Eliminate “undeclared property” errors during `sprk_document` creation by always using the correct left-hand property in `@odata.bind`.
* Avoid PCF calls to `EntityDefinitions`/metadata endpoints and avoid PowerShell at build time.
* Provide a **single source of truth** for parent → `{ entitySet, lookupAttribute, navProperty }`.
* Keep pane load time fast by caching results in the client.
* Remain flexible for new parents (Project, Invoice, etc.) without code churn.

---

## 2) Design Summary

* **Primary approach:** A small **server-side helper** hosted inside **Spe.Bff.Api** that returns a compact “navigation map” per parent.

  * PCF (or any client) requests `/api/pcf/dataverse-navmap?v=1`.
  * Response includes for each parent table:

    * `entitySet`: plural OData set name for the **parent** table.
    * `lookupAttribute`: child lookup attribute logical name on `sprk_document`.
    * `navProperty`: correct left-hand property to use in `@odata.bind` (often equals lookup attribute logical name; sometimes schema-cased).
* **Client-side caching:** Store the response in-memory for the session. Optionally persist in `sessionStorage` keyed by `envUrl+version`.
* **Fallbacks (no PowerShell):**

  * A small, **checked-in JSON** file `navmap.json` maintained by devs for the current release (used if server helper is unavailable).
  * As a last resort, **hardcoded defaults** for Matter remain in code to prevent blockers.

> Rationale: This avoids blocked PCF metadata routes, avoids PowerShell complexity, and scales cleanly to additional parents. Hosting in Spe.Bff.Api piggybacks on the existing API’s authentication, observability, and deployment processes.

---

## 3) API Contract (Server-Side Helper in Spe.Bff.Api)

**Endpoint (example):**

```
GET /api/pcf/dataverse-navmap?v=1
```

**200 OK**
## 4) Server Implementation (Spe.Bff.Api)

`Spe.Bff.Api` already fronts SharePoint Embedded and Dataverse interactions, so extend it with a dedicated nav-map endpoint. This avoids new infrastructure while respecting the ADR that forbids Dataverse plugins.

### 4.1 Endpoint sketch

```csharp
[ApiController]
[Route("api/pcf/dataverse-navmap")]
public sealed class NavMapController : ControllerBase
{
  private readonly INavigationMetadataService _metadata;

  public NavMapController(INavigationMetadataService metadata)
  {
    _metadata = metadata;
  }

  [HttpGet]
  public async Task<ActionResult<IDictionary<string, NavEntry>>> Get([FromQuery] string? v = "1", CancellationToken ct = default)
  {
    var env = HttpContext.Request.Headers["x-environment"].FirstOrDefault();
    var map = await _metadata.GetNavMapAsync(v ?? "1", env, ct);
    return Ok(map);
  }
}
```

### 4.2 Metadata service sketch

```csharp
public record NavEntry(string EntitySet, string LookupAttribute, string NavProperty, string? CollectionNavProperty);

public interface INavigationMetadataService
{
  Task<IDictionary<string, NavEntry>> GetNavMapAsync(string version, string? environment, CancellationToken ct);
}

public sealed class NavigationMetadataService : INavigationMetadataService
{
  private readonly IDataverseService _dataverse;
  private readonly IMemoryCache _cache;
  private readonly ILogger<NavigationMetadataService> _logger;
  private readonly string[] _parents;
  private readonly string _child;

  public NavigationMetadataService(IDataverseService dataverse, IMemoryCache cache, ILogger<NavigationMetadataService> logger, IOptions<NavigationMetadataOptions> options)
  {
    _dataverse = dataverse;
    _cache = cache;
    _logger = logger;
    _parents = options.Value.Parents;
    _child = options.Value.ChildEntity;
  }

  public async Task<IDictionary<string, NavEntry>> GetNavMapAsync(string version, string? environment, CancellationToken ct)
  {
    var cacheKey = $"navmap::{environment ?? "default"}::{version}";
    if (_cache.TryGetValue(cacheKey, out IDictionary<string, NavEntry> existing))
    {
      return existing;
    }

    var map = new Dictionary<string, NavEntry>(StringComparer.OrdinalIgnoreCase);

    foreach (var parent in _parents)
    {
      var entitySet = await _dataverse.GetEntitySetNameAsync(parent, ct);
      var lookup = await _dataverse.GetLookupAttributeAsync(_child, parent, ct);
      var navProperty = lookup.NavProperty;
      var collectionNavProperty = await _dataverse.GetCollectionNavigationAsync(parent, _child, ct);
      map[parent] = new NavEntry(entitySet, lookup.LogicalName, navProperty, collectionNavProperty);
    }

    _cache.Set(cacheKey, map, TimeSpan.FromMinutes(5));
    return map;
  }
}
```

**Key points**

* Reuse existing azure ad JWT + policy infrastructure. Create a policy (e.g., `canreadmetadata`) mapping to the new requirement.
* `IDataverseService` already wraps authenticated calls—extend it with methods to fetch entity metadata (`EntitySetName`, lookup attributes, collection nav properties).
* Cache results for a few minutes in memory. Optionally write-through to Redis if multi-instance cache coherence is required.
* Drive parent/child lists from configuration (`NavigationMetadata:Parents`, `NavigationMetadata:ChildEntity`).

### 4.3 Deployment and configuration

* Expose endpoint alongside existing API modules; include in Spe.Bff.Api solution packaging.
* Add environment variable that PCF controls read to discover the nav-map URL.
* Include health checks/logging using the existing Observability stack.

### 4.4 Why Spe.Bff.Api instead of plugins/Functions

* ADR prohibits Dataverse plugins/custom APIs.
* No new hosting footprint; leverage existing CI/CD and monitoring.
* Shared auth and telemetry simplifies compliance reviews.
* Easier to evolve: the BFF already references Spaarke.Dataverse libraries.
    const entitySet = eDef.EntitySetName;

    // You can decide the lookup attribute name policy:
    const lookupAttr = (p === "sprk_matter") ? "sprk_matter" : `<your-policy>`;
    const attr = await dget(`${org}/api/data/v9.2/EntityDefinitions(LogicalName='${child}')/Attributes(LogicalName='${lookupAttr}')?$select=LogicalName,SchemaName`, token);

    // Choose which to use for LHS. Most orgs bind with logical name; keep SchemaName if you’ve seen case-sensitive issues.
    const navProperty = attr.LogicalName; // or attr.SchemaName if required

    out[p] = { entitySet, lookupAttribute: lookupAttr, navProperty };
  }

  res.set("Cache-Control", "public, max-age=300").json(out);
}

async function dget(url: string, token: string) {
  const r = await fetch(url, { headers: { Authorization: `Bearer ${token}` }});
  if (!r.ok) throw new Error(await r.text());
  return r.json();
}
```

**Security**

* Use **AAD app registration** + **application user** in Dataverse with minimum metadata read permissions.
* Optionally restrict by IP or API Management.
* Do not expose secrets to clients; function returns only the minimal mapping.

---

## 5) Client (PCF/Custom Page) Integration

### 5.1 Client NavMap cache

```ts
type NavEntry = { entitySet: string; lookupAttribute: string; navProperty: string };
type NavMap = Record<string, NavEntry>;

const NAVMAP_FALLBACK: NavMap = {
  sprk_matter: { entitySet: "sprk_matters", lookupAttribute: "sprk_matter", navProperty: "sprk_matter" }
  // Add more if needed for this release
};

class NavMapClient {
  private static cache: NavMap | null = null;
  private static key(envUrl: string) { return `navmap::${envUrl}::v1`; }

  static async load(envUrl: string): Promise<NavMap> {
    if (this.cache) return this.cache;

    // 1) Try server helper
    try {
      const r = await fetch("/api/pcf/dataverse-navmap?v=1", { credentials: "include" });
      if (r.ok) {
        const m = await r.json();
        this.cache = m;
        sessionStorage.setItem(this.key(envUrl), JSON.stringify(m));
        return m;
      }
    } catch {}

    // 2) Try sessionStorage
    try {
      const raw = sessionStorage.getItem(this.key(envUrl));
      if (raw) {
        this.cache = JSON.parse(raw);
        return this.cache;
      }
    } catch {}

    // 3) Fallback JSON (checked-in with solution)
    this.cache = NAVMAP_FALLBACK;
    return this.cache;
  }
}
```

### 5.2 Building the child payload (Option A, client-side)

```ts
function buildDocumentPayload(
  nav: NavEntry,
  parentId: string,
  file: { name: string; id: string; size: number },
  extras?: { driveId?: string; displayName?: string }
) {
  const pid = parentId.replace(/[{}]/g, "").toLowerCase();
  const base = (extras?.displayName ?? file.name).replace(/\.[^/.]+$/, "");

  return {
    sprk_documentname: base,
    sprk_filename: file.name,
    sprk_graphitemid: file.id,
    sprk_graphdriveid: extras?.driveId,
    sprk_filesize: file.size,
    [`${nav.navProperty}@odata.bind`]: `/${nav.entitySet}(${pid})`
  };
}
```

**Create call (PCF):**

```ts
const map = await NavMapClient.load(context.page.getClientUrl());
const nav = map["sprk_matter"];
const payload = buildDocumentPayload(nav, parentId, file, { driveId });

await context.webAPI.createRecord("sprk_document", payload);
```

### 5.3 Server-side batch creation (Option B, recommended for high volume)

* Use the **relationship URL** on the server/BFF:

  ```
  POST /sprk_matters(<id>)/sprk_matter_document
  { /* child payload without lookup fields */ }
  ```
* This avoids any `@odata.bind` nav-property logic; simpler and faster for many rows.

---

## 6) Error Handling and Diagnostics

* **Input validation:** Ensure `parentId` is a GUID (strip braces).
* **Unknown parent:** If the parent logical name isn’t in the nav map, show a clear error with the parent name.
* **Create failures:** Log the HTTP status and the request’s correlation ID. Retry only idempotent operations (not record creates) unless using `$batch` with atomicity set appropriately.
* **Telemetry:** Log `pane_open`, `navmap_source` (server/sessions/fallback), `create_success_count`, `create_error_count`.

---

## 7) Security and Permissions

* **Server helper (Custom API):** Secure with a model-driven security role that grants access to the Custom API.
* **Server helper (Azure Function):** Secure with AAD; use **application permissions** to call Dataverse; restrict origins, IP, or front it with APIM.
* **Data exposure:** The API returns only table names and navigation hints; no PII or business data.

---

## 8) Deployment & ALM

* Package the **Custom API** (or the front-door URL for the Function) in your managed solution.
* Include `NAVMAP_FALLBACK` in the client bundle for resilience.
* Configure the **server helper endpoint URL** via Environment Variables so you can switch environments without code changes.
* Use Power Platform Pipelines or your DevOps pipeline to deploy the solution and, separately, the Azure Function.

---

## 9) Testing Plan

* **Happy path (Matter):**

  * Fetch nav map → returns `sprk_matter` entry.
  * Create 1 and 5 documents via PCF; confirm all succeed; verify `sprk_matter@odata.bind` and `/sprk_matters(<guid>)`.
* **Server helper down:**

  * Simulate 500; client should use session cache or fallback JSON; creation still succeeds.
* **Unknown parent (negative):**

  * Attempt with an unsupported parent; PCF should show a clear error and not attempt creation.
* **Batch path:**

  * On server, create multiple children via relationship URL; ensure atomic or partial commit as per your design.
* **Performance:**

  * Open/close the pane several times; ensure no redundant nav-map calls (cache hit).
* **Telemetry:**

  * Confirm events emitted with counts and sources.

---

## 10) Rollback

* Keep the **hardcoded Matter values** in code so a control can always create a child record for Matter even without the nav map.
* If the server helper has a defect, set its environment variable to empty; client uses session cache or fallback JSON.

---

## 11) Definition of Done

* PCF creates `sprk_document` using `sprk_matter@odata.bind` to `/sprk_matters(<guid>)` without “undeclared property” errors.
* Client loads nav map from **server helper** and caches it; supports **fallback JSON** and **hardcoded defaults**.
* Server helper is secured and returns correct entries for all supported parents.
* Batch path (relationship URL) is implemented on the server for high-volume creates.
* Tests, telemetry, and runbooks are in place.

---

## 12) Claude Code Prompts

**Prompt A — Build server helper (Custom API, C#):**
“Create an unbound Dataverse Custom API named `sprk_NavMapGet` that returns a JSON map `{ parentLogicalName: { entitySet, lookupAttribute, navProperty } }` for parents `[sprk_matter]`. Use `RetrieveEntityRequest` for parent `EntitySetName` and the `sprk_document` lookup attribute metadata to derive `navProperty` (use attribute logical name). Output JSON on the Custom API’s string parameter `returnjson`. Add solution packaging artifacts.”

**Prompt B — Client cache + integration (TypeScript):**
“Build `NavMapClient` with `load(envUrl)` to fetch `/api/pcf/dataverse-navmap?v=1`, cache in memory and `sessionStorage`, and fallback to `NAVMAP_FALLBACK`. Integrate into `DocumentRecordService` to build `@odata.bind` payloads with `${nav.navProperty}@odata.bind` to `/${nav.entitySet}(<parentId>)` and call `context.webAPI.createRecord`.”

**Prompt C — Batch creation on server (Option B):**
“Create a server endpoint to perform `$batch` creation via `POST /sprk_matters(<id>)/sprk_matter_document` for each child. Do not include lookup fields in child payloads. Return a summary of successes/failures. Add retries only for transient HTTP 429/5xx.”

**Prompt D — Tests:**
“Add unit tests and integration tests: (1) nav map load with server, (2) nav map fallback to session, (3) payload builder produces correct `@odata.bind`, (4) creation succeeds for single and multiple files, (5) error surfaces cleanly with correlation id.”

---

## 13) Minimal Working Snippets

**PCF create (client)**

```ts
const map = await NavMapClient.load(context.page.getClientUrl());
const nav = map["sprk_matter"]; // validated at runtime
const pid = parentId.replace(/[{}]/g, "").toLowerCase();

const doc = {
  sprk_documentname: fileBase,
  sprk_filename: fileName,
  sprk_graphitemid: speItemId,
  sprk_graphdriveid: driveId,
  sprk_filesize: size,
  [`${nav.navProperty}@odata.bind`]: `/${nav.entitySet}(${pid})`
};

await context.webAPI.createRecord("sprk_document", doc);
```

**Server batch (relationship URL)**

```http
POST /api/data/v9.2/sprk_matters(<PARENT_GUID>)/sprk_matter_document
Content-Type: application/json

{
  "sprk_documentname": "A",
  "sprk_filename": "A.pdf",
  "sprk_graphitemid": "01K...",
  "sprk_graphdriveid": "drive-1",
  "sprk_filesize": 12345
}
```

---

**Conclusion:**
Ship with the **working hardcoded** Matter mapping now. In parallel, implement the **server-side nav map helper** and client cache. This avoids PCF metadata pitfalls and PowerShell overhead, keeps navigation names accurate, and scales across new parents with minimal friction.
