# Task 7.1: Extend IDataverseService with Metadata Methods

**Phase:** 7 - Navigation Property Metadata Service
**Duration:** 4-6 hours
**Prerequisites:** Phase 6 complete, current codebase reviewed
**Owner:** Backend Developer (C#/.NET)

---

## Task Prompt (For AI Agent or Developer)

```
You are working on Task 7.1 of Phase 7: Extend IDataverseService with Metadata Methods

BEFORE starting work:
1. Read this entire task document carefully
2. Read C:\code_files\spaarke\src\shared\Spaarke.Dataverse\IDataverseService.cs
3. Read C:\code_files\spaarke\src\shared\Spaarke.Dataverse\DataverseWebApiService.cs
4. Verify ServiceClient or Web API client is available for metadata queries
5. Review Phase 6 PowerShell validation scripts (know what queries work)
6. Compare current IDataverseService to required interface below
7. Update this task document status section with current state

DURING work:
1. Add three new methods to IDataverseService interface
2. Implement methods in DataverseWebApiService (or DataverseServiceClientImpl)
3. Add error handling for metadata query failures
4. Add logging for metadata operations
5. Write unit tests for each new method
6. Verify compilation succeeds
7. Update checklist as you complete each step

AFTER completing work:
1. Run all unit tests (existing + new)
2. Verify no breaking changes to existing methods
3. Document any assumptions or limitations
4. Fill in actual results vs expected results
5. Mark task as Complete
6. Commit changes with provided commit message template

Your goal: Enable server-side metadata queries so NavMapController can discover
navigation properties without manual PowerShell validation.
```

---

## Objective

Extend the `IDataverseService` interface with methods to query Dataverse metadata for entity set names, lookup navigation properties, and collection navigation properties. This enables the NavMapController to dynamically discover the correct navigation property names for @odata.bind operations.

---

## Current State Analysis

### Existing IDataverseService Interface

**File:** `src/shared/Spaarke.Dataverse/IDataverseService.cs`

**Current Methods (v2.2.0):**
```csharp
public interface IDataverseService
{
    Task<string> CreateDocumentAsync(CreateDocumentRequest request, CancellationToken ct = default);
    Task<DocumentEntity?> GetDocumentAsync(string id, CancellationToken ct = default);
    Task UpdateDocumentAsync(string id, UpdateDocumentRequest request, CancellationToken ct = default);
    Task DeleteDocumentAsync(string id, CancellationToken ct = default);
    Task<IEnumerable<DocumentEntity>> GetDocumentsByContainerAsync(string containerId, CancellationToken ct = default);
    Task<DocumentAccessLevel> GetUserAccessAsync(string userId, string documentId, CancellationToken ct = default);
    Task<bool> TestConnectionAsync();
    Task<bool> TestDocumentOperationsAsync();
}
```

**Analysis:**
- ✅ Has CRUD operations for documents
- ✅ Has test/health check methods
- ❌ Missing: Metadata query methods
- ❌ Missing: Entity definition queries
- ❌ Missing: Relationship queries

---

## Required Interface Extension

### New Methods to Add

```csharp
public interface IDataverseService
{
    // ... existing methods ...

    /// <summary>
    /// Get the EntitySetName (plural collection name) for an entity logical name.
    /// Example: "sprk_matter" → "sprk_matters"
    /// </summary>
    /// <param name="entityLogicalName">Entity logical name (e.g., "sprk_matter")</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Entity set name (e.g., "sprk_matters")</returns>
    Task<string> GetEntitySetNameAsync(string entityLogicalName, CancellationToken ct = default);

    /// <summary>
    /// Get lookup navigation property metadata for a child → parent relationship.
    /// This is the property name used in @odata.bind (case-sensitive!).
    /// Example: sprk_document → sprk_matter returns "sprk_Matter" (capital M)
    /// </summary>
    /// <param name="childEntityLogicalName">Child entity (e.g., "sprk_document")</param>
    /// <param name="relationshipSchemaName">Relationship schema name (e.g., "sprk_matter_document")</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Lookup metadata with navigation property name</returns>
    Task<LookupNavigationMetadata> GetLookupNavigationAsync(
        string childEntityLogicalName,
        string relationshipSchemaName,
        CancellationToken ct = default);

    /// <summary>
    /// Get collection navigation property for a parent → child relationship.
    /// This is used for relationship URL creation (Option B).
    /// Example: sprk_matter → sprk_document returns "sprk_matter_document"
    /// </summary>
    /// <param name="parentEntityLogicalName">Parent entity (e.g., "sprk_matter")</param>
    /// <param name="relationshipSchemaName">Relationship schema name</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Collection navigation property name</returns>
    Task<string> GetCollectionNavigationAsync(
        string parentEntityLogicalName,
        string relationshipSchemaName,
        CancellationToken ct = default);
}
```

---

## Supporting Data Structures

### LookupNavigationMetadata

**File:** `src/shared/Spaarke.Dataverse/Models/LookupNavigationMetadata.cs` (NEW)

```csharp
namespace Spaarke.Dataverse.Models;

/// <summary>
/// Metadata for a lookup navigation property on a child entity
/// </summary>
public record LookupNavigationMetadata
{
    /// <summary>
    /// Logical name of the lookup attribute (e.g., "sprk_matter")
    /// </summary>
    public required string LogicalName { get; init; }

    /// <summary>
    /// Schema name of the lookup attribute (may differ in case, e.g., "sprk_Matter")
    /// </summary>
    public required string SchemaName { get; init; }

    /// <summary>
    /// Navigation property name for @odata.bind (CASE-SENSITIVE!)
    /// Example: "sprk_Matter" (capital M)
    /// This is ReferencingEntityNavigationPropertyName from metadata
    /// </summary>
    public required string NavigationPropertyName { get; init; }

    /// <summary>
    /// Target entity logical name (e.g., "sprk_matter")
    /// </summary>
    public required string TargetEntityLogicalName { get; init; }
}
```

---

## Implementation Approach

### Option A: Using ServiceClient (Recommended if available)

**When to use:** If `IOrganizationService` or `ServiceClient` is already available in the implementation

```csharp
// In DataverseServiceClientImpl.cs
public async Task<string> GetEntitySetNameAsync(string entityLogicalName, CancellationToken ct)
{
    var request = new RetrieveEntityRequest
    {
        LogicalName = entityLogicalName,
        EntityFilters = EntityFilters.Entity
    };

    var response = (RetrieveEntityResponse)await Task.Run(() =>
        _service.Execute(request), ct);

    return response.EntityMetadata.EntitySetName;
}

public async Task<LookupNavigationMetadata> GetLookupNavigationAsync(
    string childEntityLogicalName,
    string relationshipSchemaName,
    CancellationToken ct)
{
    var request = new RetrieveEntityRequest
    {
        LogicalName = childEntityLogicalName,
        EntityFilters = EntityFilters.Relationships
    };

    var response = (RetrieveEntityResponse)await Task.Run(() =>
        _service.Execute(request), ct);

    var relationship = response.EntityMetadata.ManyToOneRelationships
        .FirstOrDefault(r => r.SchemaName == relationshipSchemaName);

    if (relationship == null)
    {
        throw new InvalidOperationException(
            $"Relationship '{relationshipSchemaName}' not found on entity '{childEntityLogicalName}'");
    }

    var attribute = response.EntityMetadata.Attributes
        .OfType<LookupAttributeMetadata>()
        .FirstOrDefault(a => a.LogicalName == relationship.ReferencingAttribute);

    if (attribute == null)
    {
        throw new InvalidOperationException(
            $"Lookup attribute '{relationship.ReferencingAttribute}' not found");
    }

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

### Option B: Using Web API (If ServiceClient not available)

**When to use:** If only HTTP client available

```csharp
// In DataverseWebApiService.cs
public async Task<string> GetEntitySetNameAsync(string entityLogicalName, CancellationToken ct)
{
    var query = $"EntityDefinitions(LogicalName='{entityLogicalName}')?$select=EntitySetName";
    var response = await _httpClient.GetAsync(query, ct);
    response.EnsureSuccessStatusCode();

    var json = await response.Content.ReadFromJsonAsync<EntityDefinitionResponse>(ct);
    return json.EntitySetName;
}

public async Task<LookupNavigationMetadata> GetLookupNavigationAsync(
    string childEntityLogicalName,
    string relationshipSchemaName,
    CancellationToken ct)
{
    var query = $"EntityDefinitions(LogicalName='{childEntityLogicalName}')" +
                $"?$expand=ManyToOneRelationships(" +
                $"$select=SchemaName,ReferencingEntityNavigationPropertyName,ReferencingAttribute,ReferencedEntity;" +
                $"$filter=SchemaName eq '{relationshipSchemaName}')";

    var response = await _httpClient.GetAsync(query, ct);
    response.EnsureSuccessStatusCode();

    var json = await response.Content.ReadFromJsonAsync<EntityDefinitionWithRelationshipsResponse>(ct);

    var relationship = json.ManyToOneRelationships.FirstOrDefault();
    if (relationship == null)
    {
        throw new InvalidOperationException(
            $"Relationship '{relationshipSchemaName}' not found on entity '{childEntityLogicalName}'");
    }

    // Query attribute metadata for logical/schema names
    var attrQuery = $"EntityDefinitions(LogicalName='{childEntityLogicalName}')" +
                    $"/Attributes(LogicalName='{relationship.ReferencingAttribute}')" +
                    $"?$select=LogicalName,SchemaName";

    var attrResponse = await _httpClient.GetAsync(attrQuery, ct);
    attrResponse.EnsureSuccessStatusCode();

    var attrJson = await attrResponse.Content.ReadFromJsonAsync<AttributeMetadataResponse>(ct);

    return new LookupNavigationMetadata
    {
        LogicalName = attrJson.LogicalName,
        SchemaName = attrJson.SchemaName,
        NavigationPropertyName = relationship.ReferencingEntityNavigationPropertyName,
        TargetEntityLogicalName = relationship.ReferencedEntity
    };
}
```

---

## Error Handling

### Required Error Scenarios

1. **Entity Not Found**
```csharp
if (response.StatusCode == HttpStatusCode.NotFound)
{
    throw new EntityNotFoundException(
        $"Entity '{entityLogicalName}' not found in Dataverse metadata.");
}
```

2. **Relationship Not Found**
```csharp
if (relationship == null)
{
    _logger.LogWarning(
        "Relationship {RelationshipSchemaName} not found on {EntityLogicalName}. " +
        "Available relationships: {AvailableRelationships}",
        relationshipSchemaName,
        childEntityLogicalName,
        string.Join(", ", relationships.Select(r => r.SchemaName)));

    throw new RelationshipNotFoundException(
        $"Relationship '{relationshipSchemaName}' not found on entity '{childEntityLogicalName}'. " +
        $"Verify the relationship exists and the schema name is correct.");
}
```

3. **Permission Denied**
```csharp
if (response.StatusCode == HttpStatusCode.Forbidden)
{
    throw new UnauthorizedAccessException(
        "Insufficient permissions to query EntityDefinitions metadata. " +
        "Ensure the application user has 'Read' permission on Entity Definitions.");
}
```

4. **Network/Timeout Errors**
```csharp
catch (HttpRequestException ex)
{
    _logger.LogError(ex, "HTTP error querying metadata for {EntityLogicalName}", entityLogicalName);
    throw new DataverseMetadataException(
        $"Failed to query metadata for entity '{entityLogicalName}': {ex.Message}", ex);
}
catch (TaskCanceledException ex) when (ex.CancellationToken == ct)
{
    _logger.LogWarning("Metadata query cancelled for {EntityLogicalName}", entityLogicalName);
    throw;
}
```

---

## Logging

### Log Templates

```csharp
// Success
_logger.LogInformation(
    "Retrieved entity set name for {EntityLogicalName}: {EntitySetName}",
    entityLogicalName,
    entitySetName);

// Query start
_logger.LogDebug(
    "Querying lookup navigation metadata: Child={ChildEntity}, Relationship={Relationship}",
    childEntityLogicalName,
    relationshipSchemaName);

// Cache hit (if caching added)
_logger.LogDebug(
    "Cache hit for entity set name: {EntityLogicalName}",
    entityLogicalName);

// Error
_logger.LogError(ex,
    "Failed to retrieve metadata for {EntityLogicalName}: {ErrorMessage}",
    entityLogicalName,
    ex.Message);
```

---

## Testing

### Unit Tests Required

**File:** `tests/unit/Spaarke.Dataverse.Tests/DataverseServiceMetadataTests.cs` (NEW)

```csharp
public class DataverseServiceMetadataTests
{
    [Fact]
    public async Task GetEntitySetNameAsync_ValidEntity_ReturnsCorrectSetName()
    {
        // Arrange
        var service = CreateMockService();

        // Act
        var result = await service.GetEntitySetNameAsync("sprk_matter");

        // Assert
        Assert.Equal("sprk_matters", result);
    }

    [Fact]
    public async Task GetLookupNavigationAsync_ValidRelationship_ReturnsNavigationProperty()
    {
        // Arrange
        var service = CreateMockService();

        // Act
        var result = await service.GetLookupNavigationAsync(
            "sprk_document",
            "sprk_matter_document");

        // Assert
        Assert.Equal("sprk_Matter", result.NavigationPropertyName); // Capital M!
        Assert.Equal("sprk_matter", result.LogicalName);
    }

    [Fact]
    public async Task GetLookupNavigationAsync_InvalidRelationship_ThrowsException()
    {
        // Arrange
        var service = CreateMockService();

        // Act & Assert
        await Assert.ThrowsAsync<RelationshipNotFoundException>(() =>
            service.GetLookupNavigationAsync("sprk_document", "invalid_relationship"));
    }

    [Fact]
    public async Task GetCollectionNavigationAsync_ValidRelationship_ReturnsCollectionProperty()
    {
        // Arrange
        var service = CreateMockService();

        // Act
        var result = await service.GetCollectionNavigationAsync(
            "sprk_matter",
            "sprk_matter_document");

        // Assert
        Assert.Equal("sprk_matter_document", result);
    }
}
```

---

## Integration Testing

### Manual Validation Script

**File:** `scripts/Test-DataverseMetadata.ps1`

```powershell
# Test metadata queries directly against Dataverse
$token = az account get-access-token --resource https://spaarkedev1.crm.dynamics.com --query accessToken -o tsv
$headers = @{ Authorization = "Bearer $token"; Accept = "application/json" }
$base = "https://spaarkedev1.crm.dynamics.com/api/data/v9.2"

# Test 1: Entity Set Name
Write-Host "Test 1: Get Entity Set Name for sprk_matter"
$query = "EntityDefinitions(LogicalName='sprk_matter')?`$select=EntitySetName"
$result = Invoke-RestMethod -Uri "$base/$query" -Headers $headers
Write-Host "  Result: $($result.EntitySetName)" -ForegroundColor Green

# Test 2: Lookup Navigation Property
Write-Host "`nTest 2: Get Lookup Navigation for sprk_document → sprk_matter"
$query = "EntityDefinitions(LogicalName='sprk_document')?`$expand=ManyToOneRelationships(`$select=SchemaName,ReferencingEntityNavigationPropertyName;`$filter=SchemaName eq 'sprk_matter_document')"
$result = Invoke-RestMethod -Uri "$base/$query" -Headers $headers
$navProp = $result.ManyToOneRelationships[0].ReferencingEntityNavigationPropertyName
Write-Host "  Result: $navProp" -ForegroundColor Green

# Test 3: Collection Navigation Property
Write-Host "`nTest 3: Get Collection Navigation for sprk_matter → sprk_document"
$query = "EntityDefinitions(LogicalName='sprk_matter')?`$expand=OneToManyRelationships(`$select=SchemaName,ReferencedEntityNavigationPropertyName;`$filter=SchemaName eq 'sprk_matter_document')"
$result = Invoke-RestMethod -Uri "$base/$query" -Headers $headers
$colNavProp = $result.OneToManyRelationships[0].ReferencedEntityNavigationPropertyName
Write-Host "  Result: $colNavProp" -ForegroundColor Green

Write-Host "`nAll tests passed! ✅" -ForegroundColor Green
```

---

## Validation Checklist

**BEFORE Starting:**
- [ ] Read entire task document
- [ ] Reviewed current IDataverseService interface
- [ ] Reviewed implementation class (ServiceClient or WebApi)
- [ ] Understand Phase 6 metadata validation approach
- [ ] Confirmed ServiceClient/HttpClient availability

**DURING Implementation:**
- [ ] Added GetEntitySetNameAsync to interface
- [ ] Added GetLookupNavigationAsync to interface
- [ ] Added GetCollectionNavigationAsync to interface
- [ ] Created LookupNavigationMetadata record
- [ ] Implemented GetEntitySetNameAsync
- [ ] Implemented GetLookupNavigationAsync
- [ ] Implemented GetCollectionNavigationAsync
- [ ] Added error handling (EntityNotFound, RelationshipNotFound, Unauthorized)
- [ ] Added logging statements (Debug, Info, Error)
- [ ] Wrote unit tests for all three methods
- [ ] Wrote unit tests for error scenarios
- [ ] All tests pass
- [ ] No compilation errors
- [ ] No breaking changes to existing methods

**AFTER Completion:**
- [ ] Ran all unit tests (existing + new) - all pass
- [ ] Ran manual validation script against dev environment
- [ ] Documented any limitations or assumptions
- [ ] Filled in actual results section below
- [ ] Committed changes with proper message
- [ ] Notified next task owner (Task 7.2)

---

## Expected Results

### GetEntitySetNameAsync
- Input: `"sprk_matter"`
- Output: `"sprk_matters"`

### GetLookupNavigationAsync
- Input: Child=`"sprk_document"`, Relationship=`"sprk_matter_document"`
- Output:
  ```csharp
  new LookupNavigationMetadata {
      LogicalName = "sprk_matter",
      SchemaName = "sprk_Matter",  // May differ in case
      NavigationPropertyName = "sprk_Matter",  // ⚠️ CAPITAL M - critical!
      TargetEntityLogicalName = "sprk_matter"
  }
  ```

### GetCollectionNavigationAsync
- Input: Parent=`"sprk_matter"`, Relationship=`"sprk_matter_document"`
- Output: `"sprk_matter_document"`

---

## Actual Results

**Completion Date:** _______________
**Implemented By:** _______________

**GetEntitySetNameAsync:**
```
Input: _______________
Output: _______________
Status: [ ] Pass / [ ] Fail
```

**GetLookupNavigationAsync:**
```
Input: _______________
Output: _______________
Status: [ ] Pass / [ ] Fail
```

**GetCollectionNavigationAsync:**
```
Input: _______________
Output: _______________
Status: [ ] Pass / [ ] Fail
```

**Test Results:**
```
Unit Tests: ___/___  passed
Integration Tests: [ ] Pass / [ ] Fail
Manual Validation: [ ] Pass / [ ] Fail
```

**Notes/Issues:**
```
(Document any issues, workarounds, or deviations from plan)
```

---

## Commit Message Template

```
feat(dataverse): Add metadata query methods to IDataverseService

Add three new methods to support dynamic navigation property discovery:
- GetEntitySetNameAsync: Returns entity set name (e.g., "sprk_matters")
- GetLookupNavigationAsync: Returns navigation property for @odata.bind
- GetCollectionNavigationAsync: Returns collection property for relationship URLs

Changes:
- Updated IDataverseService interface with metadata methods
- Implemented methods in DataverseWebApiService (or DataverseServiceClientImpl)
- Created LookupNavigationMetadata record for lookup metadata
- Added error handling (EntityNotFound, RelationshipNotFound, Unauthorized)
- Added logging for metadata operations
- Created unit tests for all methods
- Created manual validation script

Benefits:
- Enables server-side navigation property discovery (no PowerShell needed)
- Supports multi-parent entities without hardcoding
- Case-sensitive navigation properties correctly resolved
- Future-proof against schema changes

Testing:
- Unit tests: All pass
- Integration tests: Verified against spaarkedev1
- Manual validation: PowerShell script confirms correct results

Task: 7.1 - Extend IDataverseService with Metadata Methods
Phase: 7 - Navigation Property Metadata Service

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude <noreply@anthropic.com>
```

---

## Next Task

After completing Task 7.1, proceed to:
**[TASK-7.2-CREATE-NAVMAP-CONTROLLER.md](./TASK-7.2-CREATE-NAVMAP-CONTROLLER.md)**

---

**Task Owner:** _______________
**Completion Date:** _______________
**Reviewed By:** _______________
**Status:** ⬜ Not Started | ⬜ In Progress | ⬜ Blocked | ⬜ Complete
