# Relationship Navigation Pattern

> **Domain**: Dataverse Lookups and Relationships
> **Last Validated**: 2026-02-19
> **Source ADRs**: ADR-007

---

## Canonical Implementations

| File | Purpose |
|------|---------|
| `src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs` | EntityReference usage |
| `src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs` | @odata.bind usage |
| `src/server/shared/Spaarke.Dataverse/Models.cs` | LookupNavigationMetadata |

---

## Lookup Patterns by Context

### ServiceClient (SDK)

Use EntityReference for lookup fields (lines 215-226):

```csharp
// Parent document lookup
if (request.ParentDocumentLookup.HasValue)
    document["sprk_parentdocument"] = new EntityReference("sprk_document", request.ParentDocumentLookup.Value);

// Record association lookups
if (request.MatterLookup.HasValue)
    document["sprk_matter"] = new EntityReference("sprk_matter", request.MatterLookup.Value);

if (request.ProjectLookup.HasValue)
    document["sprk_project"] = new EntityReference("sprk_project", request.ProjectLookup.Value);

if (request.InvoiceLookup.HasValue)
    document["sprk_invoice"] = new EntityReference("sprk_invoice", request.InvoiceLookup.Value);
```

### Web API (REST)

Use @odata.bind for lookup fields:

```csharp
var payload = new Dictionary<string, object>
{
    ["sprk_documentname"] = request.Name
};

// Parent document lookup
if (request.ParentDocumentLookup.HasValue)
    payload["sprk_ParentDocument@odata.bind"] = $"/sprk_documents({request.ParentDocumentLookup.Value})";

// Record associations
if (request.MatterLookup.HasValue)
    payload["sprk_Matter@odata.bind"] = $"/sprk_matters({request.MatterLookup.Value})";
```

---

## Metadata Query Pattern

Get lookup navigation property name (lines 349-447):

```csharp
public async Task<LookupNavigationMetadata> GetLookupNavigationAsync(
    string childEntityLogicalName,
    string relationshipSchemaName,
    CancellationToken ct = default)
{
    var request = new RetrieveEntityRequest
    {
        LogicalName = childEntityLogicalName,
        EntityFilters = EntityFilters.Relationships | EntityFilters.Attributes
    };

    var response = (RetrieveEntityResponse)await Task.Run(() =>
        _serviceClient.Execute(request), ct);

    // Find the relationship
    var relationship = response.EntityMetadata.ManyToOneRelationships
        .FirstOrDefault(r => r.SchemaName == relationshipSchemaName);

    if (relationship == null)
    {
        var available = response.EntityMetadata.ManyToOneRelationships.Select(r => r.SchemaName);
        throw new InvalidOperationException($"Relationship '{relationshipSchemaName}' not found. Available: {string.Join(", ", available)}");
    }

    // Find lookup attribute
    var attribute = response.EntityMetadata.Attributes
        .OfType<LookupAttributeMetadata>()
        .FirstOrDefault(a => a.LogicalName == relationship.ReferencingAttribute);

    return new LookupNavigationMetadata
    {
        LogicalName = attribute.LogicalName,
        SchemaName = attribute.SchemaName,
        NavigationPropertyName = relationship.ReferencingEntityNavigationPropertyName,
        TargetEntityLogicalName = relationship.ReferencedEntity
    };
}
```

---

## Collection Navigation Pattern

Get child records via one-to-many relationship (lines 449-523):

```csharp
public async Task<string> GetCollectionNavigationAsync(
    string parentEntityLogicalName,
    string relationshipSchemaName,
    CancellationToken ct = default)
{
    var request = new RetrieveEntityRequest
    {
        LogicalName = parentEntityLogicalName,
        EntityFilters = EntityFilters.Relationships
    };

    var response = (RetrieveEntityResponse)await Task.Run(() =>
        _serviceClient.Execute(request), ct);

    var relationship = response.EntityMetadata.OneToManyRelationships
        .FirstOrDefault(r => r.SchemaName == relationshipSchemaName);

    return relationship.ReferencedEntityNavigationPropertyName;
}
```

---

## OData Filter by Lookup

Query using _fieldname_value format:

```csharp
// Filter by lookup GUID value
var filter = $"_sprk_containerid_value eq {containerId}";
var url = $"sprk_documents?$filter={Uri.EscapeDataString(filter)}";
```

---

## Lookup Metadata Model

```csharp
public class LookupNavigationMetadata
{
    public string LogicalName { get; set; }           // sprk_matter
    public string SchemaName { get; set; }            // sprk_Matter
    public string NavigationPropertyName { get; set; } // sprk_Matter
    public string TargetEntityLogicalName { get; set; } // sprk_matter
}
```

---

## Pattern Summary

| Context | Pattern | Example |
|---------|---------|---------|
| ServiceClient set | `EntityReference` | `new EntityReference("entity", guid)` |
| ServiceClient get | `GetAttributeValue<EntityReference>` | `entity.GetAttributeValue<EntityReference>("field")?.Id` |
| Web API set | `@odata.bind` | `"field@odata.bind": "/entities(guid)"` |
| Web API filter | `_field_value` | `_sprk_matter_value eq {guid}` |
| Metadata query | `RetrieveEntityRequest` | Query relationship schema names |

---

## Name Casing Rules: SDK vs Web API vs Xrm.WebApi

**This is a critical distinction that causes silent save failures if violated.**

### The Rule

| API Layer | Scalar Fields | Lookup Fields (Set) | Lookup Fields (Read/Filter) |
|-----------|--------------|--------------------|-----------------------------|
| **SDK (ServiceClient)** | Logical name (lowercase) | Logical name via `EntityReference` | Logical name via `GetAttributeValue<EntityReference>` |
| **Web API (REST)** | Logical name (case-insensitive) | **SchemaName (CASE-SENSITIVE)** via `@odata.bind` | `_fieldname_value` (logical, lowercase) |
| **Xrm.WebApi (client JS/TS)** | Logical name (case-insensitive) | **SchemaName (CASE-SENSITIVE)** via `@odata.bind` | `_fieldname_value` (logical, lowercase) |

### Why This Matters

- The **SDK** handles name resolution internally — you always use logical names (lowercase)
- The **Web API** is OData-based — navigation properties are defined by SchemaName in the EDMX model and are **case-sensitive**
- **Xrm.WebApi** (used in Custom Pages, form scripts, PCF) is a thin wrapper around the Web API — it inherits the same case sensitivity for `@odata.bind`

### When We Use Each in Spaarke

| Context | Technology | Name Casing for Lookups |
|---------|-----------|------------------------|
| BFF API via `DataverseServiceClientImpl.cs` | SDK (ServiceClient) | Logical name: `document["sprk_matter"] = new EntityReference(...)` |
| BFF API via `DataverseWebApiService.cs` | Web API (REST) | SchemaName: `payload["sprk_Matter@odata.bind"] = "/sprk_matters(guid)"` |
| Plugins (server C#) | SDK only | Logical name always |
| Custom Pages (client TS) | Xrm.WebApi | SchemaName: `"sprk_CompletedBy@odata.bind": "/contacts(guid)"` |
| PCF Controls (client TS) | Xrm.WebApi | SchemaName for `@odata.bind` |

### Example: Same Lookup, Three Contexts

```
Column logical name:    sprk_completedby     (always lowercase)
Column schema name:     sprk_CompletedBy     (PascalCase after prefix)
Navigation property:    sprk_CompletedBy     (matches SchemaName)
```

```csharp
// SDK (server) — logical name, EntityReference handles the rest
entity["sprk_completedby"] = new EntityReference("contact", contactGuid);

// Web API REST (server) — SchemaName for @odata.bind key
payload["sprk_CompletedBy@odata.bind"] = $"/contacts({contactGuid})";
```

```typescript
// Xrm.WebApi (client) — SchemaName for @odata.bind key
await Xrm.WebApi.updateRecord("sprk_event", eventId, {
  "sprk_CompletedBy@odata.bind": `/contacts(${contactGuid})`
});
```

### How to Find the SchemaName

1. **make.powerapps.com** → Tables → Entity → Columns → Click column → "Schema name" field
2. **Metadata API**: `RetrieveEntityRequest` → `Attributes.OfType<LookupAttributeMetadata>().SchemaName`
3. **Convention**: Publisher prefix + PascalCase (e.g., `sprk_` + `CompletedBy`)

### For Approach A Dynamic Form Configs

Lookup fields in `sprk_fieldconfigjson` MUST include `navigationProperty` with the correct SchemaName:

```json
{
  "name": "sprk_completedby",
  "type": "lookup",
  "label": "Completed By",
  "targets": ["contact"],
  "navigationProperty": "sprk_CompletedBy"
}
```

The `name` field (logical name) is used for reading values. The `navigationProperty` (SchemaName) is used for saving via `@odata.bind`.

---

## Key Points

1. **EntityReference for SDK** - Wrap GUID with entity logical name (lowercase)
2. **@odata.bind for Web API / Xrm.WebApi** - Use **SchemaName** (CASE-SENSITIVE) with entity set path
3. **Underscore prefix for filters** - `_fieldname_value` not `fieldname`
4. **Metadata discovery** - Query relationships dynamically if needed
5. **SchemaName vs LogicalName** - Web API / Xrm.WebApi use SchemaName for `@odata.bind`; SDK uses LogicalName for everything

---

## Related Patterns

- [Entity Operations](entity-operations.md) - CRUD patterns
- [Web API Client](web-api-client.md) - REST patterns

---

**Lines**: ~180
