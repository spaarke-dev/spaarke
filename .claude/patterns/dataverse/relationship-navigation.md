# Relationship Navigation Pattern

> **Domain**: Dataverse Lookups and Relationships
> **Last Validated**: 2025-12-19
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
    document["sprk_parentdocumentname"] = new EntityReference("sprk_document", request.ParentDocumentLookup.Value);

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
    payload["sprk_ParentDocumentName@odata.bind"] = $"/sprk_documents({request.ParentDocumentLookup.Value})";

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

## Key Points

1. **EntityReference for SDK** - Wrap GUID with entity logical name
2. **@odata.bind for REST** - Use navigation property with entity set path
3. **Underscore prefix for filters** - `_fieldname_value` not `fieldname`
4. **Metadata discovery** - Query relationships dynamically if needed
5. **SchemaName vs LogicalName** - Web API uses SchemaName, SDK uses LogicalName

---

## Related Patterns

- [Entity Operations](entity-operations.md) - CRUD patterns
- [Web API Client](web-api-client.md) - REST patterns

---

**Lines**: ~100
