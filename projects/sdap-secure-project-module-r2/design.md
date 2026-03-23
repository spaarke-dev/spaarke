# SDAP Secure Project Module R2 — Design

> **Status**: Design
> **Created**: 2026-03-23
> **Extends**: `projects/sdap-secure-project-module` (R1)
> **Prerequisite**: R1 must be fully deployed and operational

---

## Executive Summary

R1 established the Secure Project & External Access Platform for `sprk_project` — a Power Pages React 18 SPA backed by a BFF API with three-plane access control (Dataverse participation record + SPE container membership + Redis caching). External users authenticate via MSAL B2B and can view/collaborate on project documents and events.

**R2 extends the same architecture to three additional entities**: Matter (`sprk_matter`), Invoice (`sprk_invoice`), and Work Assignment (`sprk_workassignment`). The two fields marking an entity as secure (`sprk_issecure`, `sprk_scuritybu`) have already been created on all three tables. This document specifies everything else required.

---

## Scope

### In Scope

- Extend `sprk_externalrecordaccess` to hold matter, invoice, and work assignment grants
- Add `sprk_externalaccountid` field to `sprk_matter`; provision BU + SPE container + Account per secure matter
- Invoice and Work Assignment inherit their parent matter's security context — no independent provisioning
- Extend `ExternalParticipationService` and `ExternalCallerContext` to be entity-type-aware
- New BFF API data endpoints for matters, invoices, and work assignments
- Extend grant/revoke/close endpoints to handle the new entity types
- Power Pages Web API site settings and Table Permissions for the three new entities
- New SPA pages: Matter List, Matter Detail, Invoice Detail, Work Assignment Detail
- Update SPA `WorkspaceHomePage` to display both Projects and Matters

### Out of Scope

- Separate SPE containers for Invoice or Work Assignment — they use the parent Matter's container
- Independent provisioning of Invoice or Work Assignment — security context always flows from Matter
- Matter-level AI toolbar or semantic search (deferred to R3)
- Mobile-specific UI optimizations

### Affected Files

| Area | File / Path | Change Type |
|------|-------------|-------------|
| Dataverse | `sprk_externalrecordaccess` table | Schema extension |
| Dataverse | `sprk_matter` table | New fields |
| BFF API | `ExternalParticipationService.cs` | Entity-type-aware loading |
| BFF API | `ExternalCallerContext.cs` | Generic access methods |
| BFF API | `ExternalParticipation` record | Add EntityType discriminator |
| BFF API | `ExternalCallerAuthorizationFilter.cs` | No change (already generic) |
| BFF API | `GrantExternalAccessEndpoint.cs` | Support new entity types |
| BFF API | `RevokeExternalAccessEndpoint.cs` | Support new entity types |
| BFF API | `ProjectClosureEndpoint.cs` | Add `CloseMatterEndpoint.cs` |
| BFF API | `ProvisionProjectEndpoint.cs` | Add `ProvisionMatterEndpoint.cs` |
| BFF API | `ExternalDataService.cs` | New matter/invoice/WA methods |
| BFF API | `ExternalAccessEndpoints.cs` | New route registrations |
| BFF API | `ExternalAccessModule.cs` | No change |
| SPA | `src/client/external-spa/src/types/index.ts` | New interfaces |
| SPA | `src/client/external-spa/src/pages/` | New pages |
| SPA | `src/client/external-spa/src/pages/WorkspaceHomePage.tsx` | Show matters |
| SPA | `src/client/external-spa/src/App.tsx` | New routes |
| Power Pages | Web API site settings | 3 new tables |
| Power Pages | Table Permissions | 3 new permission scopes |

---

## Architecture

### How R2 Extends R1

R1's architecture is deliberately extensible. The `sprk_externalrecordaccess` junction table already has a nullable `sprk_matterid` column. The BFF filter (`ExternalCallerAuthorizationFilter`) and cache strategy are entity-agnostic. R2 fills in the gaps without restructuring anything.

```
External User (MSAL B2B)
  │
  ▼
Power Pages SPA (React 18 + Vite)
  │
  ├── /projects → Projects (R1)          /matters → Matters (R2 NEW)
  │    └── /projects/:id                  └── /matters/:id
  │         ├── Documents                      ├── Documents (matter container)
  │         ├── Events                         ├── Invoices (R2 NEW)
  │         └── Contacts                       ├── Work Assignments (R2 NEW)
  │                                            └── Contacts
  ▼
BFF API
  ├── GET /api/v1/external/me                 ← unchanged
  ├── GET /api/v1/external/projects           ← unchanged
  ├── GET /api/v1/external/matters            ← NEW
  ├── GET /api/v1/external/matters/:id        ← NEW
  ├── GET /api/v1/external/matters/:id/documents     ← NEW
  ├── GET /api/v1/external/matters/:id/invoices      ← NEW
  ├── GET /api/v1/external/matters/:id/workassignments ← NEW
  ├── GET /api/v1/external/invoices/:id       ← NEW
  ├── GET /api/v1/external/workassignments/:id ← NEW
  ├── POST /api/v1/external-access/grant      ← extended (entity-type aware)
  ├── POST /api/v1/external-access/revoke     ← extended (entity-type aware)
  ├── POST /api/v1/external-access/provision-matter  ← NEW
  └── POST /api/v1/external-access/close-matter      ← NEW
  │
  ├── ExternalParticipationService            ← extended
  │     Queries sprk_externalrecordaccesses
  │     Loads project + matter + invoice + WA grants into ExternalCallerContext
  │     60s Redis cache per contact
  │
  └── ExternalDataService                     ← extended
        New methods for matter/invoice/WA OData queries
```

### Access Hierarchy and Inheritance Rules

```
sprk_matter (secure = true)
  │  ├─ has own BU + SPE container + Account (provisioned on creation)
  │  └─ external access grant in sprk_externalrecordaccess
  │
  ├── sprk_invoice (linked to matter)
  │     ├─ sprk_issecure = true → requires parent matter also secure
  │     ├─ NO independent provisioning (uses matter's BU/Account)
  │     └─ grant in sprk_externalrecordaccess (sprk_invoiceid lookup)
  │
  └── sprk_workassignment (linked to matter)
        ├─ sprk_issecure = true → requires parent matter also secure
        ├─ NO independent provisioning (uses matter's BU/Account)
        └─ grant in sprk_externalrecordaccess (sprk_workassignmentid lookup)
```

**Key rule**: An external user with Matter access can see the matter's documents (in the matter's SPE container). Invoice and Work Assignment grants give access to the specific record and any documents linked to it within the same SPE container. There is **no automatic inheritance** — matter access does NOT automatically grant access to child invoices or work assignments. Each entity requires its own explicit grant.

---

## Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Provisioning scope | Matter only; Invoice/WA inherit | Invoice and WA are always in the context of a matter. Separate BU/container per invoice would create unmanageable infrastructure sprawl. |
| SPE container | One per matter | Documents linked to invoices/WAs are stored in the matter's SPE container. Access scoping is at the matter level in SPE. |
| Access inheritance | Explicit grants only (no cascade) | Simpler permission audit trail. External user must be explicitly granted access to each entity. Match R1 project model. |
| Entity type discriminator | New `sprk_entitytype` choice field on `sprk_externalrecordaccess` | Avoids deriving entity type from which lookup is non-null. Makes queries and cache deserialization unambiguous. |
| `ExternalParticipation` refactor | Generalize with `EntityType` + `EntityId` discriminated pattern | Avoids 4 parallel code paths. The existing `ProjectId`-specific methods become wrappers. |
| Invoice/WA provisioning endpoint | None — use matter's BU/Account | When marking an invoice/WA as secure, UI must ensure parent matter is already provisioned. Validation in grant endpoint. |
| `sprk_scuritybu` field name | Accept as-is (user-created field with typo) | Do not rename existing field. Reference it exactly as `sprk_scuritybu` throughout. |

---

## Component 1: Dataverse Schema

### 1.1 — `sprk_externalrecordaccess` Extensions

Add the following to the existing table:

| Field | Display Name | Type | Notes |
|-------|-------------|------|-------|
| `sprk_entitytype` | Entity Type | Choice | Project=100000001, Matter=100000002, Invoice=100000003, WorkAssignment=100000004. **Required.** |
| `sprk_invoiceid` | Invoice | Lookup → sprk_invoice | Nullable. Populated only when `sprk_entitytype = Invoice`. |
| `sprk_workassignmentid` | Work Assignment | Lookup → sprk_workassignment | Nullable. Populated only when `sprk_entitytype = WorkAssignment`. |

> `sprk_matterid` already exists as a nullable lookup to `sprk_matter`. No change needed.
> `sprk_projectid` should be made nullable if it is currently required (it should be, since not all grants are project-scoped).

**Existing unchanged fields**: `sprk_contactid`, `sprk_projectid`, `sprk_matterid`, `sprk_accesslevel`, `sprk_grantedby`, `sprk_granteddate`, `sprk_expirydate`, `sprk_accountid`, `statecode`.

### 1.2 — `sprk_matter` Additional Fields

The two security fields (`sprk_issecure`, `sprk_scuritybu`) are already created. Add:

| Field | Display Name | Type | Notes |
|-------|-------------|------|-------|
| `sprk_externalaccountid` | External Access Account | Lookup → Account | Set by `provision-matter` endpoint after provisioning. |
| `sprk_specontainerid` | SPE Container ID | String (100) | SPE container ID string. Set by `provision-matter` endpoint. |

**Business Rule (Dataverse)**: Lock `sprk_issecure` to read-only after first save (same pattern as `sprk_project`). Rule: `IF(sprk_issecure == true, LOCK sprk_issecure)`.

### 1.3 — `sprk_invoice` and `sprk_workassignment`

No additional fields beyond what is already created (`sprk_issecure`, `sprk_scuritybu`). These entities rely on their parent matter for provisioned infrastructure.

**Validation rule (enforced in BFF grant endpoint)**: Before creating an invoice or work assignment grant, verify the parent matter record has `sprk_issecure = true` and `sprk_specontainerid` is not null. Return 422 if matter is not provisioned.

### 1.4 — Power Pages Web API Site Settings

Add one site setting per new entity to enable OData access from the Power Pages portal session:

```
Webapi/sprk_matter/enabled             = true
Webapi/sprk_matter/fields              = sprk_matterid,sprk_name,sprk_issecure,sprk_status,createdon,modifiedon,sprk_description

Webapi/sprk_invoice/enabled            = true
Webapi/sprk_invoice/fields             = sprk_invoiceid,sprk_name,sprk_issecure,sprk_invoiceamount,sprk_invoicedate,sprk_status,createdon,_sprk_matterid_value

Webapi/sprk_workassignment/enabled     = true
Webapi/sprk_workassignment/fields      = sprk_workassignmentid,sprk_name,sprk_issecure,sprk_status,sprk_startdate,sprk_enddate,createdon,_sprk_matterid_value
```

Also ensure the existing access record table allows reads from the portal session:
```
Webapi/sprk_externalrecordaccess/enabled = true  (already set in R1)
Webapi/sprk_externalrecordaccess/fields  = (extend to include sprk_entitytype, sprk_invoiceid, sprk_workassignmentid)
```

### 1.5 — Power Pages Table Permissions

Create Table Permission records in Power Pages for each new entity. These scope Dataverse Web API reads through the external user's participation records.

| Table | Permission Type | Scope | Filter |
|-------|-----------------|-------|--------|
| `sprk_matter` | Read | Related (Parental via `sprk_externalrecordaccess`) | Active access record where `sprk_contactid = current contact` and `sprk_entitytype = Matter` and `statecode = 0` |
| `sprk_invoice` | Read | Related (Parental via `sprk_externalrecordaccess`) | Active access record where `sprk_contactid = current contact` and `sprk_entitytype = Invoice` and `statecode = 0` |
| `sprk_workassignment` | Read | Related (Parental via `sprk_externalrecordaccess`) | Active access record where `sprk_contactid = current contact` and `sprk_entitytype = WorkAssignment` and `statecode = 0` |

---

## Component 2: BFF API

All files are in `src/server/api/Sprk.Bff.Api/`.

### 2.1 — New: `ExternalEntityType` Enum

**Create**: `Infrastructure/ExternalAccess/ExternalEntityType.cs`

```csharp
namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>
/// Discriminates which entity type an ExternalParticipation record covers.
/// Values match the sprk_entitytype option set on sprk_externalrecordaccess.
/// </summary>
public enum ExternalEntityType
{
    Project         = 100000001,
    Matter          = 100000002,
    Invoice         = 100000003,
    WorkAssignment  = 100000004,
}
```

### 2.2 — Modify: `ExternalParticipation` Record

**File**: `Infrastructure/ExternalAccess/ExternalParticipationService.cs` (the record is declared inline or nearby)

Replace the current project-only record:
```csharp
// BEFORE (R1)
public sealed class ExternalParticipation
{
    public required Guid ProjectId { get; init; }
    public required ExternalAccessLevel AccessLevel { get; init; }
}
```

With an entity-type-aware record:
```csharp
// AFTER (R2)
public sealed record ExternalParticipation
{
    public required ExternalEntityType EntityType { get; init; }
    public required Guid EntityId { get; init; }
    public required ExternalAccessLevel AccessLevel { get; init; }
}
```

### 2.3 — Modify: `ExternalParticipationService`

**File**: `Infrastructure/ExternalAccess/ExternalParticipationService.cs`

The `GetParticipationsAsync` method currently queries:
```
sprk_externalrecordaccesses?$filter=_sprk_contact_value eq {contactId} and statecode eq 0
&$select=_sprk_project_value,sprk_accesslevel
```

**Update** the `$select` and result mapping to read all entity type fields:

```csharp
// Updated $select:
$select=sprk_entitytype,_sprk_project_value,_sprk_matter_value,_sprk_invoice_value,_sprk_workassignment_value,sprk_accesslevel

// Updated result mapping (per record):
var entityType = (ExternalEntityType)(int)record["sprk_entitytype"];
var entityId = entityType switch
{
    ExternalEntityType.Project        => ParseGuid(record, "_sprk_project_value"),
    ExternalEntityType.Matter         => ParseGuid(record, "_sprk_matter_value"),
    ExternalEntityType.Invoice        => ParseGuid(record, "_sprk_invoice_value"),
    ExternalEntityType.WorkAssignment => ParseGuid(record, "_sprk_workassignment_value"),
    _                                 => (Guid?)null,
};

if (entityId is null) continue; // Skip malformed records

results.Add(new ExternalParticipation
{
    EntityType   = entityType,
    EntityId     = entityId.Value,
    AccessLevel  = (ExternalAccessLevel)(int)record["sprk_accesslevel"],
});
```

The Redis cache key and TTL remain unchanged: `sdap:external:access:{contactId}`, 60s TTL.

### 2.4 — Modify: `ExternalCallerContext`

**File**: `Infrastructure/ExternalAccess/ExternalCallerContext.cs`

Replace all project-specific methods with generic methods plus project/matter/invoice/WA convenience wrappers:

```csharp
public sealed class ExternalCallerContext
{
    public static readonly object HttpContextItemsKey = new();

    public required Guid ContactId { get; init; }
    public required string Email { get; init; }
    public required IReadOnlyList<ExternalParticipation> Participations { get; init; }
    public bool FromCache { get; init; }

    // Generic accessors (primary API)
    public bool HasAccess(ExternalEntityType type, Guid entityId)
        => Participations.Any(p => p.EntityType == type && p.EntityId == entityId);

    public ExternalAccessLevel? GetAccessLevel(ExternalEntityType type, Guid entityId)
        => Participations.FirstOrDefault(p => p.EntityType == type && p.EntityId == entityId)?.AccessLevel;

    public AccessRights GetEffectiveRights(ExternalEntityType type, Guid entityId)
    {
        var level = GetAccessLevel(type, entityId);
        return level switch
        {
            ExternalAccessLevel.ViewOnly    => AccessRights.Read,
            ExternalAccessLevel.Collaborate => AccessRights.Read | AccessRights.Create | AccessRights.Write,
            ExternalAccessLevel.FullAccess  => AccessRights.Read | AccessRights.Create | AccessRights.Write | AccessRights.Delete,
            _                               => AccessRights.None,
        };
    }

    public IEnumerable<Guid> GetAccessibleIds(ExternalEntityType type)
        => Participations.Where(p => p.EntityType == type).Select(p => p.EntityId);

    // Convenience wrappers (preserve R1 call sites)
    public bool HasProjectAccess(Guid projectId)           => HasAccess(ExternalEntityType.Project, projectId);
    public ExternalAccessLevel? GetProjectAccessLevel(Guid id) => GetAccessLevel(ExternalEntityType.Project, id);
    public IEnumerable<Guid> GetAccessibleProjectIds()     => GetAccessibleIds(ExternalEntityType.Project);

    public bool HasMatterAccess(Guid matterId)             => HasAccess(ExternalEntityType.Matter, matterId);
    public ExternalAccessLevel? GetMatterAccessLevel(Guid id)  => GetAccessLevel(ExternalEntityType.Matter, id);
    public IEnumerable<Guid> GetAccessibleMatterIds()      => GetAccessibleIds(ExternalEntityType.Matter);

    public bool HasInvoiceAccess(Guid invoiceId)           => HasAccess(ExternalEntityType.Invoice, invoiceId);
    public bool HasWorkAssignmentAccess(Guid id)           => HasAccess(ExternalEntityType.WorkAssignment, id);
}
```

> **Note**: The existing call sites in R1 endpoints use `GetEffectiveRights(projectId)` (single-arg form). Update those callers to the two-arg form: `GetEffectiveRights(ExternalEntityType.Project, projectId)`.

### 2.5 — Modify: `GrantExternalAccessEndpoint`

**File**: `Api/ExternalAccess/GrantExternalAccessEndpoint.cs`

#### Request DTO
```csharp
// BEFORE (R1)
public record GrantAccessRequest(
    Guid ContactId,
    Guid ProjectId,
    ExternalAccessLevel AccessLevel,
    DateOnly? ExpiryDate,
    Guid? AccountId);

// AFTER (R2)
public record GrantAccessRequest(
    Guid ContactId,
    ExternalEntityType EntityType,   // NEW
    Guid EntityId,                   // NEW — replaces ProjectId
    ExternalAccessLevel AccessLevel,
    DateOnly? ExpiryDate,
    Guid? AccountId);
```

#### Dataverse Payload Update

The OData bind field for the entity reference is now driven by `EntityType`:

```csharp
var entityBind = request.EntityType switch
{
    ExternalEntityType.Project        => ($"sprk_projectid@odata.bind",  $"/sprk_projects({request.EntityId})"),
    ExternalEntityType.Matter         => ($"sprk_matterid@odata.bind",   $"/sprk_matters({request.EntityId})"),
    ExternalEntityType.Invoice        => ($"sprk_invoiceid@odata.bind",  $"/sprk_invoices({request.EntityId})"),
    ExternalEntityType.WorkAssignment => ($"sprk_workassignmentid@odata.bind", $"/sprk_workassignments({request.EntityId})"),
    _                                 => throw new ArgumentOutOfRangeException(nameof(request.EntityType)),
};

var payload = new Dictionary<string, object?>
{
    ["sprk_entitytype"]                 = (int)request.EntityType,       // NEW
    [entityBind.Item1]                  = entityBind.Item2,
    ["sprk_contactid@odata.bind"]       = $"/contacts({request.ContactId})",
    ["sprk_accesslevel"]                = (int)request.AccessLevel,
    ["sprk_granteddate"]                = DateTime.UtcNow.ToString("o"),
    // ... existing fields unchanged
};
```

#### Invoice/WA Validation
Before creating the grant for Invoice or Work Assignment, validate the parent matter is provisioned:

```csharp
if (request.EntityType is ExternalEntityType.Invoice or ExternalEntityType.WorkAssignment)
{
    // Resolve parent matter ID from the entity record
    // Query sprk_invoices or sprk_workassignments to get _sprk_matterid_value
    // Verify the matter has sprk_issecure=true and sprk_specontainerid is not null
    // Return 422 if not provisioned
}
```

#### SPE Container Membership
For Invoice and Work Assignment grants, add the contact to the **parent matter's** SPE container (not a new container). The `ContainerId` is resolved from the matter's `sprk_specontainerid`. SPE access level follows the same ViewOnly → reader / Collaborate → writer mapping as R1.

### 2.6 — Modify: `RevokeExternalAccessEndpoint`

**File**: `Api/ExternalAccess/RevokeExternalAccessEndpoint.cs`

#### Request DTO
```csharp
// AFTER (R2)
public record RevokeAccessRequest(
    Guid AccessRecordId,
    Guid ContactId,
    ExternalEntityType EntityType,   // NEW
    Guid EntityId,                   // NEW — replaces ProjectId
    Guid? ContainerId);              // still optional; resolved from matter for invoice/WA grants
```

The deactivation payload and SPE removal logic are unchanged. The `ContainerId` for invoice/WA revocations should be the parent matter's container (caller provides it).

### 2.7 — New: `ProvisionMatterEndpoint`

**Create**: `Api/ExternalAccess/ProvisionMatterEndpoint.cs`

**Endpoint**: `POST /api/v1/external-access/provision-matter`

Mirrors `ProvisionProjectEndpoint` exactly, with these differences:
- Queries `sprk_matters` instead of `sprk_projects`
- BU name pattern: `SM-{MatterRef}` (Secure Matter) instead of `SP-{ProjectRef}`
- SPE container description: `Isolated document container for Secure Matter: {matterName}`
- Account name: `External Access — {matterName}`
- Writes back to `sprk_matters({matterId})`:
  ```csharp
  new Dictionary<string, object?>
  {
      ["sprk_scuritybu@odata.bind"]         = $"/businessunits({buId})",  // note: typo field name
      ["sprk_specontainerid"]               = speContainerId,
      ["sprk_externalaccountid@odata.bind"] = $"/accounts({accountId})",
  }
  ```
- Field names to read from `sprk_matters`: `sprk_matterid`, `sprk_name`, `sprk_issecure` (confirm exact field names match Dataverse schema)

```csharp
public record ProvisionMatterRequest(
    Guid MatterId,
    string? MatterRef,
    Guid? UmbrellaBuId);

public record ProvisionMatterResponse(
    Guid BusinessUnitId,
    string BusinessUnitName,
    string SpeContainerId,
    Guid AccountId,
    string AccountName,
    bool WasUmbrellaBu);
```

### 2.8 — New: `CloseMatterEndpoint`

**Create**: `Api/ExternalAccess/CloseMatterEndpoint.cs`

**Endpoint**: `POST /api/v1/external-access/close-matter`

```csharp
public record CloseMatterRequest(Guid MatterId, Guid? ContainerId);
public record CloseMatterResponse(int DeactivatedRecords, bool ContainerCleared);
```

**Steps**:
1. Deactivate all `sprk_externalrecordaccess` records where `_sprk_matter_value eq {matterId}` and `statecode eq 0`
2. Also deactivate all access records for invoices and work assignments linked to the matter (cascade): query `sprk_invoices` for `_sprk_matterid_value eq {matterId}`, then deactivate matching access records
3. If `ContainerId` provided, remove all SPE container permissions
4. Invalidate Redis cache for all affected `contactId` values

### 2.9 — Extend: `ExternalDataService`

**File**: `Infrastructure/ExternalAccess/ExternalDataService.cs`

Add the following new methods. Follow the exact same OData query pattern as the existing `GetProjectsAsync`:

```csharp
// Matter methods
Task<IReadOnlyList<ExternalMatterDto>> GetMattersAsync(IEnumerable<Guid> matterIds, CancellationToken ct);
Task<ExternalMatterDto?> GetMatterByIdAsync(Guid matterId, CancellationToken ct);
Task<IReadOnlyList<ExternalDocumentDto>> GetMatterDocumentsAsync(Guid matterId, CancellationToken ct);
Task<IReadOnlyList<ExternalInvoiceDto>> GetMatterInvoicesAsync(Guid matterId, CancellationToken ct);
Task<IReadOnlyList<ExternalWorkAssignmentDto>> GetMatterWorkAssignmentsAsync(Guid matterId, CancellationToken ct);
Task<IReadOnlyList<ExternalContactDto>> GetMatterContactsAsync(Guid matterId, CancellationToken ct);

// Invoice methods
Task<ExternalInvoiceDto?> GetInvoiceByIdAsync(Guid invoiceId, CancellationToken ct);

// Work Assignment methods
Task<ExternalWorkAssignmentDto?> GetWorkAssignmentByIdAsync(Guid workAssignmentId, CancellationToken ct);
```

#### New DTO Classes

```csharp
public sealed class ExternalMatterDto
{
    [JsonPropertyName("sprk_matterid")]     public required string SprkMatterid { get; init; }
    [JsonPropertyName("sprk_name")]         public required string SprkName { get; init; }
    [JsonPropertyName("sprk_issecure")]     public bool? SprkIssecure { get; init; }
    [JsonPropertyName("sprk_status")]       public int? SprkStatus { get; init; }
    [JsonPropertyName("sprk_description")] public string? SprkDescription { get; init; }
    [JsonPropertyName("createdon")]         public string? Createdon { get; init; }
    [JsonPropertyName("modifiedon")]        public string? Modifiedon { get; init; }
}

public sealed class ExternalInvoiceDto
{
    [JsonPropertyName("sprk_invoiceid")]      public required string SprkInvoiceid { get; init; }
    [JsonPropertyName("sprk_name")]           public required string SprkName { get; init; }
    [JsonPropertyName("sprk_invoiceamount")]  public decimal? SprkInvoiceamount { get; init; }
    [JsonPropertyName("sprk_invoicedate")]    public string? SprkInvoicedate { get; init; }
    [JsonPropertyName("sprk_status")]         public int? SprkStatus { get; init; }
    [JsonPropertyName("_sprk_matterid_value")] public string? SprkMatteridValue { get; init; }
    [JsonPropertyName("createdon")]           public string? Createdon { get; init; }
}

public sealed class ExternalWorkAssignmentDto
{
    [JsonPropertyName("sprk_workassignmentid")] public required string SprkWorkassignmentid { get; init; }
    [JsonPropertyName("sprk_name")]             public required string SprkName { get; init; }
    [JsonPropertyName("sprk_status")]           public int? SprkStatus { get; init; }
    [JsonPropertyName("sprk_startdate")]        public string? SprkStartdate { get; init; }
    [JsonPropertyName("sprk_enddate")]          public string? SprkEnddate { get; init; }
    [JsonPropertyName("_sprk_matterid_value")]  public string? SprkMatteridValue { get; init; }
    [JsonPropertyName("createdon")]             public string? Createdon { get; init; }
}
```

**OData table names** (Dataverse plural names — confirmed):
- Matters: `sprk_matters`
- Invoices: `sprk_invoices`
- Work Assignments: `sprk_workassignments`

### 2.10 — Extend: `ExternalAccessEndpoints.cs`

**File**: `Api/ExternalAccess/ExternalAccessEndpoints.cs`

In the external user route group (`/api/v1/external`), add matter data routes alongside the existing project routes:

```csharp
// Matter data routes (same filter as project routes)
group.MapGet("/matters",              GetAccessibleMattersAsync);
group.MapGet("/matters/{matterId:guid}", GetMatterByIdAsync);
group.MapGet("/matters/{matterId:guid}/documents",      GetMatterDocumentsAsync);
group.MapGet("/matters/{matterId:guid}/invoices",       GetMatterInvoicesAsync);
group.MapGet("/matters/{matterId:guid}/workassignments",GetMatterWorkAssignmentsAsync);
group.MapGet("/matters/{matterId:guid}/contacts",       GetMatterContactsAsync);
group.MapGet("/invoices/{invoiceId:guid}",              GetInvoiceByIdAsync);
group.MapGet("/workassignments/{workAssignmentId:guid}",GetWorkAssignmentByIdAsync);
```

In the internal management group (`/api/v1/external-access`):
```csharp
group.MapPost("/provision-matter", ProvisionMatterEndpoint.HandleAsync);
group.MapPost("/close-matter",     CloseMatterEndpoint.HandleAsync);
```

Each matter/invoice/WA endpoint handler follows the same pattern as project handlers:
1. Extract `ExternalCallerContext` from `HttpContext.Items`
2. Check `context.HasMatterAccess(matterId)` — return 403 if not authorized
3. Check access level for operation-specific gates
4. Call `ExternalDataService` method
5. Return result

---

## Component 3: External SPA

All files in `src/client/external-spa/src/`.

### 3.1 — Extend: `types/index.ts`

Add interfaces for the new entity types alongside the existing `Project`, `Document`, `AccessLevel`:

```typescript
export interface Matter {
  sprk_matterid: string;
  sprk_name: string;
  sprk_issecure: boolean;
  sprk_status?: number;
  sprk_description?: string;
  createdon?: string;
  modifiedon?: string;
  accessLevel?: AccessLevel;  // injected client-side from ExternalUserContext
}

export interface Invoice {
  sprk_invoiceid: string;
  sprk_name: string;
  sprk_invoiceamount?: number;
  sprk_invoicedate?: string;
  sprk_status?: number;
  _sprk_matterid_value?: string;
  createdon?: string;
}

export interface WorkAssignment {
  sprk_workassignmentid: string;
  sprk_name: string;
  sprk_status?: number;
  sprk_startdate?: string;
  sprk_enddate?: string;
  _sprk_matterid_value?: string;
  createdon?: string;
}
```

Extend `ExternalUserContext` to include matter/invoice/WA participation:

```typescript
// Existing participation structure (currently project-only)
export interface EntityAccess {
  entityType: 'project' | 'matter' | 'invoice' | 'workassignment';
  entityId: string;
  accessLevel: AccessLevel;
}

export interface ExternalUserContext {
  contactId: string;
  email: string;
  displayName: string;
  participations: EntityAccess[];  // replaces single projectId + accessLevel
}

// Convenience helpers used throughout the SPA
export function getMatterAccessLevel(ctx: ExternalUserContext, matterId: string): AccessLevel | undefined {
  return ctx.participations.find(p => p.entityType === 'matter' && p.entityId === matterId)?.accessLevel;
}
export function getAccessibleMatterIds(ctx: ExternalUserContext): string[] {
  return ctx.participations.filter(p => p.entityType === 'matter').map(p => p.entityId);
}
```

> **Note**: The BFF `GET /api/v1/external/me` endpoint must be updated to return the full `participations` array (entity type + entity ID + access level) rather than the current project-centric response. Update `ExternalUserContextResponse` in `ExternalAccessEndpoints.cs` accordingly.

### 3.2 — Extend: `auth/bff-client.ts`

Add typed API call methods for matter/invoice/WA routes, following the existing pattern for project calls:

```typescript
// Matter
export const getMatters = (): Promise<Matter[]>
export const getMatter = (matterId: string): Promise<Matter>
export const getMatterDocuments = (matterId: string): Promise<Document[]>
export const getMatterInvoices = (matterId: string): Promise<Invoice[]>
export const getMatterWorkAssignments = (matterId: string): Promise<WorkAssignment[]>
export const getMatterContacts = (matterId: string): Promise<Contact[]>

// Invoice
export const getInvoice = (invoiceId: string): Promise<Invoice>

// Work Assignment
export const getWorkAssignment = (workAssignmentId: string): Promise<WorkAssignment>
```

### 3.3 — Extend: `pages/WorkspaceHomePage.tsx`

Currently shows the "My Projects" grid. Extend to show both Projects and Matters as two sections (or a tabbed layout):

- **My Projects** section — existing `ProjectCard` grid (unchanged)
- **My Matters** section — new `MatterCard` grid with the same visual style
- `MatterCard` links to `/matters/:id`
- Fetch matters from `getMatters()` in parallel with the existing projects fetch (single `Promise.all`)

### 3.4 — New Pages

#### `pages/MatterDetailPage.tsx`

Mirrors `ProjectPage.tsx` in structure. Tabs:
- **Documents** — calls `getMatterDocuments(matterId)` → renders `DocumentLibrary` component (reuse existing, pass `matterId` instead of `projectId`)
- **Invoices** — calls `getMatterInvoices(matterId)` → renders `InvoiceList` component
- **Work Assignments** — calls `getMatterWorkAssignments(matterId)` → renders `WorkAssignmentList` component
- **Contacts** — calls `getMatterContacts(matterId)` → renders existing `ContactList` component

Access level gates (same pattern as R1):
```typescript
const accessLevel = getMatterAccessLevel(userContext, matterId);
const canDownload = accessLevel !== AccessLevel.ViewOnly;
const canUpload = accessLevel === AccessLevel.Collaborate || accessLevel === AccessLevel.FullAccess;
const canInviteUsers = accessLevel === AccessLevel.FullAccess;
```

#### `pages/InvoiceDetailPage.tsx`

Read-only view (invoices are not editable by external users). Shows invoice metadata and any linked documents within the matter's container.

Route: `/matters/:matterId/invoices/:invoiceId`

Access check: `context.HasInvoiceAccess(invoiceId)` — redirect to `/access-denied` if false.

#### `pages/WorkAssignmentDetailPage.tsx`

Shows work assignment details, status, dates, assigned contacts.

Route: `/matters/:matterId/workassignments/:workAssignmentId`

Access check: `context.HasWorkAssignmentAccess(workAssignmentId)`.

### 3.5 — Extend: `App.tsx` Routing

Add the new routes using the same `HashRouter` pattern:

```tsx
<Route path="/matters" element={<WorkspaceHomePage activeTab="matters" />} />
<Route path="/matters/:matterId" element={<MatterDetailPage />} />
<Route path="/matters/:matterId/invoices/:invoiceId" element={<InvoiceDetailPage />} />
<Route path="/matters/:matterId/workassignments/:workAssignmentId" element={<WorkAssignmentDetailPage />} />
```

---

## Component 4: Internal Dynamics App (Grant/Revoke UI)

The existing internal grant/revoke subgrid on the Project form needs equivalent controls on the Matter form. This is Dataverse model-driven app configuration — no code required.

| Form | Change |
|------|--------|
| Matter Main Form | Add `sprk_externalrecordaccess` subgrid filtered to `sprk_matterid = current matter` |
| Matter Main Form | Add **Grant External Access** and **Revoke Access** ribbon buttons (same as Project form) |
| Invoice Main Form | Add **Grant External Access** ribbon button; calls `POST /api/v1/external-access/grant` with `EntityType = Invoice` |
| Work Assignment Main Form | Add **Grant External Access** ribbon button; calls grant endpoint with `EntityType = WorkAssignment` |
| Matter Main Form | Add **Provision Secure Matter** button (calls `provision-matter` endpoint) — only visible when `sprk_issecure = true` and `sprk_specontainerid` is null |

The ribbon button implementation follows the existing pattern in `src/solutions/` — JavaScript web resource calling BFF API, same auth flow.

---

## Development Instructions

Follow this sequence. Each phase can be verified independently before proceeding.

### Phase 1: Dataverse Schema (Prerequisites)

1. In Power Apps maker (`make.powerapps.com`), open the **SpaarkeCore** solution
2. On `sprk_externalrecordaccess`:
   - Add **Choice** column `sprk_entitytype` (display: "Entity Type") with values: Project=100000001, Matter=100000002, Invoice=100000003, WorkAssignment=100000004. Make it Required.
   - Add **Lookup** column `sprk_invoiceid` → `sprk_invoice`. Make it Optional.
   - Add **Lookup** column `sprk_workassignmentid` → `sprk_workassignment`. Make it Optional.
   - Make `sprk_projectid` optional if currently required
3. On `sprk_matter`:
   - Add **Lookup** column `sprk_externalaccountid` → Account. Optional.
   - Add **Text** column `sprk_specontainerid` (max 100 chars). Optional.
   - Add Business Rule: lock `sprk_issecure` after save if value is true
4. Dataverse plural entity names (confirmed):
   - `sprk_matters`, `sprk_invoices`, `sprk_workassignments`
5. Publish all customizations
6. Export and check into `src/solutions/SpaarkeCore/`

### Phase 2: Power Pages Configuration

1. In Power Pages studio (`make.powerpages.microsoft.com`), open the SDAP portal
2. Add Site Settings (Settings → Site Settings → New for each):
   - `Webapi/sprk_matter/enabled = true`
   - `Webapi/sprk_matter/fields = sprk_matterid,sprk_name,sprk_issecure,sprk_status,createdon,modifiedon`
   - `Webapi/sprk_invoice/enabled = true`
   - `Webapi/sprk_invoice/fields = sprk_invoiceid,sprk_name,sprk_invoiceamount,sprk_invoicedate,sprk_status,createdon,_sprk_matterid_value`
   - `Webapi/sprk_workassignment/enabled = true`
   - `Webapi/sprk_workassignment/fields = sprk_workassignmentid,sprk_name,sprk_status,sprk_startdate,sprk_enddate,createdon,_sprk_matterid_value`
3. Create Table Permissions for each entity (Security → Table Permissions → New):
   - Scope: **Related** (filtered via parent access record)
   - See Section 1.5 for filter definitions
   - Assign to **External User** web role
4. Test with a logged-in external user session that the OData queries return data

### Phase 3: BFF API — Core Types

1. Create `Infrastructure/ExternalAccess/ExternalEntityType.cs` (Section 2.1)
2. Update `ExternalParticipation` record (Section 2.2) — entity-type-aware
3. Update `ExternalParticipationService.GetParticipationsAsync` (Section 2.3) — new `$select` + mapping
4. Update `ExternalCallerContext` with generic methods (Section 2.4) — add convenience wrappers, keep R1 call sites working
5. **Build**: `dotnet build src/server/api/Sprk.Bff.Api/` — must compile with zero errors
6. **Run unit tests**: any existing tests for `ExternalParticipationService` or `ExternalCallerContext` — update as needed

### Phase 4: BFF API — Endpoints

1. Update `GrantExternalAccessEndpoint` (Section 2.5):
   - New `GrantAccessRequest` DTO
   - Entity-type-aware OData bind
   - Invoice/WA parent matter validation
2. Update `RevokeExternalAccessEndpoint` (Section 2.6) — new request DTO
3. Create `ProvisionMatterEndpoint` (Section 2.7) — mirrors provision-project
4. Create `CloseMatterEndpoint` (Section 2.8) — cascade deactivation
5. Add matter data methods to `ExternalDataService` (Section 2.9) — new DTOs + OData queries
6. Register all new routes in `ExternalAccessEndpoints.cs` (Section 2.10)
7. **Build and test**: `dotnet build && dotnet test`
8. **Deploy**: `.\scripts\Deploy-BffApi.ps1`
9. **Smoke test new endpoints** (expect 401 = route registered, needs auth):
   ```bash
   curl -s -o /dev/null -w "%{http_code}" https://spe-api-dev-67e2xz.azurewebsites.net/api/v1/external/matters
   # Expected: 401
   curl -s -o /dev/null -w "%{http_code}" https://spe-api-dev-67e2xz.azurewebsites.net/api/v1/external-access/provision-matter
   # Expected: 401
   ```

### Phase 5: External SPA

1. Extend `types/index.ts` (Section 3.1) — new interfaces and helpers
2. Add API calls to `auth/bff-client.ts` (Section 3.2)
3. Update `WorkspaceHomePage.tsx` (Section 3.3) — add Matters section
4. Create `pages/MatterDetailPage.tsx` (Section 3.4)
5. Create `pages/InvoiceDetailPage.tsx` (Section 3.4)
6. Create `pages/WorkAssignmentDetailPage.tsx` (Section 3.4)
7. Add routes in `App.tsx` (Section 3.5)
8. **Build**: `cd src/client/external-spa && npm run build`
9. **Deploy**: `.\scripts\Deploy-PowerPages.ps1`

### Phase 6: Internal App (Ribbon Buttons)

1. Add `sprk_externalrecordaccess` subgrid to Matter Main Form
2. Create **Grant External Access** and **Provision Secure Matter** ribbon buttons on Matter form
3. Create **Grant External Access** buttons on Invoice and Work Assignment forms
4. Deploy forms and ribbon via solution export/import (`dataverse-deploy` skill)

### Phase 7: End-to-End Validation

| Test | Steps | Expected |
|------|-------|----------|
| Provision secure matter | Set `sprk_issecure=true` on a matter, click Provision button | BU + SPE container + Account created; fields stamped on matter record |
| Grant matter access | On provisioned matter, grant access to external contact | `sprk_externalrecordaccess` record created with `sprk_entitytype=Matter`; SPE container membership added |
| External user sees matter | Log in to Power Pages portal | Matter appears in "My Matters" section on home page |
| External user views matter detail | Navigate to matter | Documents, Invoices, Work Assignments tabs load |
| Grant invoice access | On an invoice linked to the secure matter, grant access to same contact | Access record created; contact can see invoice in portal |
| Revoke matter access | Revoke the contact's matter access grant | `sprk_externalrecordaccess` deactivated; SPE membership removed; portal hides matter on next page load |
| Close matter | Click Close Matter button | All access records deactivated; SPE container cleared |
| Participation cache | Grant/revoke | Redis cache invalidated; next portal request reflects updated access within 60s |

---

## Open Questions

| Question | Impact | Recommended Default |
|----------|--------|---------------------|
| Should matter access cascade to child invoices/WAs? | If yes, query logic in `ExternalParticipationService` must expand parent grants | **No cascade** — explicit grants only. Simpler audit trail. |
| Can an invoice or WA be marked secure without its parent matter being secure? | If yes, provisioning is more complex | **No** — BFF validates parent matter is provisioned before allowing grant |
| What fields exist on `sprk_invoice` and `sprk_workassignment`? | Determines `$select` clause in OData queries and SPA display fields | Confirm field names in Power Apps maker before implementing `ExternalDataService` methods |
| Do invoice/WA documents have their own document type/subtype, or are they all in the matter container with a filter? | Determines document query filter in `GetMatterDocumentsAsync` vs. separate document lists | Confirm document-matter-invoice relationship model |
| What is the exact Dataverse plural name for each entity? | Required for all OData URLs | Run the EntityDefinitions query in Phase 1 Step 4 |

---

## ADR Compliance

| ADR | Requirement | This Design |
|-----|-------------|-------------|
| ADR-001 | Minimal API, no Azure Functions | All new endpoints follow minimal API pattern |
| ADR-002 | Thin plugins, no HTTP in plugins | No plugin changes required |
| ADR-006 | Code Pages for standalone dialogs | External SPA is a Power Pages Code Site (Vite single-file build) |
| ADR-008 | Endpoint filters for auth | `ExternalCallerAuthorizationFilter` unchanged; all new routes use the same filter |
| ADR-009 | Redis-first caching | Participation cache unchanged; covers all entity types via single cache key per contact |
| ADR-010 | ≤15 DI registrations | No new DI registrations required; `ExternalAccessModule` unchanged |
| ADR-012 | Shared component library | New SPA pages use `FileUploadZone`, `UploadedFileList`, `AiSummaryPopover` from `@spaarke/ui-components` |
| ADR-021 | Fluent UI v9, no hard-coded colors, dark mode | All new SPA components use Fluent v9 tokens exclusively |

---

*Design complete. Proceed with `/project-pipeline projects/sdap-secure-project-module-r2` to generate tasks once design decisions in the Open Questions section are resolved.*
