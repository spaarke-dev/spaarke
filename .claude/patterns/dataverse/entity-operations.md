# Entity Operations Pattern

> **Domain**: Dataverse Entity CRUD
> **Last Validated**: 2025-12-19
> **Source ADRs**: ADR-002, ADR-007

---

## Canonical Implementations

| File | Purpose |
|------|---------|
| `src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs` | ServiceClient CRUD |
| `src/server/shared/Spaarke.Dataverse/Models.cs` | DTOs and request models |
| `tests/unit/Sprk.Bff.Api.Tests/Integration/DataverseEntitySchemaTests.cs` | Field mapping validation |

---

## Late-Bound Entity Pattern

This codebase uses **late-bound entities** (no early-bound code generation):

```csharp
// Create entity with string-based attribute access
var document = new Entity("sprk_document");
document["sprk_documentname"] = request.Name;
document["statuscode"] = new OptionSetValue(1);  // Draft
document["statecode"] = new OptionSetValue(0);   // Active

var documentId = await _serviceClient.CreateAsync(document, ct);
```

---

## Create Operation

DataverseServiceClientImpl lines 76-90:

```csharp
public async Task<string> CreateDocumentAsync(CreateDocumentRequest request, CancellationToken ct = default)
{
    var document = new Entity("sprk_document");
    document["sprk_documentname"] = request.Name;

    if (!string.IsNullOrEmpty(request.Description))
        document["sprk_documentdescription"] = request.Description;

    document["statuscode"] = new OptionSetValue(1); // Draft
    document["statecode"] = new OptionSetValue(0);  // Active

    var documentId = await _serviceClient.CreateAsync(document, ct);
    _logger.LogInformation("Document created with ID: {DocumentId}", documentId);
    return documentId.ToString();
}
```

---

## Retrieve Operation

With ColumnSet for select (lines 92-107):

```csharp
public async Task<DocumentEntity?> GetDocumentAsync(string id, CancellationToken ct = default)
{
    var entity = await _serviceClient.RetrieveAsync(
        "sprk_document",
        Guid.Parse(id),
        new ColumnSet("sprk_documentname", "sprk_documentdescription", "sprk_containerid",
                     "sprk_hasfile", "sprk_filename", "sprk_filesize", "sprk_mimetype",
                     "sprk_graphitemid", "sprk_graphdriveid", "sprk_filepath",
                     "statuscode", "statecode", "createdon", "modifiedon"),
        ct);

    return entity == null ? null : MapToDocumentEntity(entity);
}
```

---

## Update Operation

Sparse update pattern (lines 109-229):

```csharp
public async Task UpdateDocumentAsync(string id, UpdateDocumentRequest request, CancellationToken ct = default)
{
    var document = new Entity("sprk_document", Guid.Parse(id));

    // Only set fields that are provided (sparse update)
    if (request.Name != null)
        document["sprk_documentname"] = request.Name;

    if (request.Status.HasValue)
        document["statuscode"] = new OptionSetValue((int)request.Status.Value);

    // OptionSet fields require OptionSetValue wrapper
    if (request.SummaryStatus.HasValue)
        document["sprk_filesummarystatus"] = new OptionSetValue(request.SummaryStatus.Value);

    // Lookup fields require EntityReference
    if (request.ParentDocumentLookup.HasValue)
        document["sprk_parentdocument"] = new EntityReference("sprk_document", request.ParentDocumentLookup.Value);

    await _serviceClient.UpdateAsync(document, ct);
    _logger.LogInformation("Document updated: {DocumentId} ({FieldCount} fields)", id, document.Attributes.Count);
}
```

---

## Query with QueryExpression

Filter by lookup value (lines 237-255):

```csharp
public async Task<IEnumerable<DocumentEntity>> GetDocumentsByContainerAsync(string containerId, CancellationToken ct = default)
{
    var query = new QueryExpression("sprk_document")
    {
        ColumnSet = new ColumnSet("sprk_documentname", "sprk_containerid", "sprk_hasfile",
                                 "sprk_filename", "sprk_graphitemid", "sprk_graphdriveid",
                                 "createdon", "modifiedon"),
        Criteria = new FilterExpression
        {
            Conditions =
            {
                new ConditionExpression("sprk_containerid", ConditionOperator.Equal, Guid.Parse(containerId))
            }
        }
    };

    var results = await _serviceClient.RetrieveMultipleAsync(query, ct);
    return results.Entities.Select(MapToDocumentEntity).ToList();
}
```

---

## Entity Mapping Pattern

Map Entity to strongly-typed DTO (lines 525-543):

```csharp
private DocumentEntity MapToDocumentEntity(Entity entity)
{
    return new DocumentEntity
    {
        Id = entity.Id.ToString(),
        Name = entity.GetAttributeValue<string>("sprk_documentname") ?? "Untitled",
        Description = entity.GetAttributeValue<string>("sprk_documentdescription"),
        ContainerId = entity.GetAttributeValue<EntityReference>("sprk_containerid")?.Id.ToString(),
        HasFile = entity.GetAttributeValue<bool>("sprk_hasfile"),
        FileName = entity.GetAttributeValue<string>("sprk_filename"),
        FileSize = entity.Contains("sprk_filesize") ? (long?)entity.GetAttributeValue<int>("sprk_filesize") : null,
        Status = (DocumentStatus)(entity.GetAttributeValue<OptionSetValue>("statuscode")?.Value ?? 1),
        CreatedOn = entity.GetAttributeValue<DateTime>("createdon"),
        ModifiedOn = entity.GetAttributeValue<DateTime>("modifiedon")
    };
}
```

---

## Field Type Patterns

| .NET Type | Dataverse Type | Example |
|-----------|---------------|---------|
| `string` | Single/Multi-line text | `entity["sprk_name"] = "value"` |
| `bool` | Yes/No | `entity["sprk_hasfile"] = true` |
| `int` | Whole number | `entity["sprk_filesize"] = 1024` |
| `DateTime` | Date/DateTime | `entity["createdon"]` (read-only) |
| `OptionSetValue` | Choice | `new OptionSetValue((int)status)` |
| `EntityReference` | Lookup | `new EntityReference("entity", guid)` |

---

## Key Points

1. **Late-bound only** - No CrmSvcUtil code generation
2. **Sparse updates** - Only include changed fields
3. **OptionSetValue wrapper** - Required for choice fields
4. **EntityReference wrapper** - Required for lookups
5. **DTO mapping** - Map at service boundary

---

## Related Patterns

- [Relationship Navigation](relationship-navigation.md) - Lookup patterns
- [Web API Client](web-api-client.md) - REST alternative

---

**Lines**: ~115
