# Task 7.2: Create NavMapController in Spe.Bff.Api

**Phase:** 7 - Navigation Property Metadata Service
**Duration:** 2-4 hours
**Prerequisites:** Task 7.1 complete (IDataverseService extended with metadata methods)
**Owner:** Backend Developer (C#/.NET)

---

## Task Prompt (For AI Agent or Developer)

```
You are working on Task 7.2 of Phase 7: Create NavMapController in Spe.Bff.Api

BEFORE starting work:
1. Read this entire task document carefully
2. Verify Task 7.1 is complete (IDataverseService has metadata methods)
3. Read src/api/Spe.Bff.Api/Api/OBOEndpoints.cs (existing pattern reference)
4. Read src/api/Spe.Bff.Api/Program.cs (DI registration)
5. Check appsettings.json structure for configuration
6. Review existing controller patterns (auth, error handling, observability)
7. Update this task document status section with current state

DURING work:
1. Create NavigationMetadataService with caching
2. Create NavMapController with GET endpoint
3. Add configuration for parent entity list
4. Register services in Program.cs
5. Add error handling and logging
6. Verify compilation succeeds
7. Test endpoint manually (Postman/curl)
8. Update checklist as you complete each step

AFTER completing work:
1. Test endpoint returns correct NavMap for all configured parents
2. Verify cache is working (check logs for cache hits)
3. Verify no breaking changes to existing endpoints
4. Test with missing/invalid entities
5. Fill in actual results vs expected results
6. Mark task as Complete
7. Commit changes with provided commit message template

Your goal: Create a server endpoint that returns navigation property metadata
for all configured parent entities, with caching and error handling.
```

---

## Objective

Create a RESTful API endpoint in Spe.Bff.Api that queries Dataverse metadata for navigation properties and returns a cached NavMap to PCF clients. This eliminates manual PowerShell validation and enables scalable multi-parent support.

---

## Current State Analysis

### Existing Spe.Bff.Api Structure

**Existing Controllers:**
- `OBOEndpoints.cs` - Upload file operations
- (Other controllers as exist in your API)

**Existing Services:**
- `IDataverseService` - Now has metadata methods (from Task 7.1)
- Authentication/Authorization middleware
- Logging and observability

**Need to Add:**
- `NavigationMetadataService` - Queries metadata and caches results
- `NavMapController` - Exposes GET endpoint
- Configuration for parent entity list

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────┐
│  HTTP GET /api/pcf/dataverse-navmap?v=1                │
│  Authorization: Bearer {JWT}                            │
└─────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────┐
│  NavMapController                                       │
│  [HttpGet("api/pcf/dataverse-navmap")]                 │
│  ├─ Validate query params (version)                    │
│  ├─ Call INavigationMetadataService                    │
│  └─ Return NavMap JSON                                 │
└─────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────┐
│  NavigationMetadataService                              │
│  ├─ Check Memory Cache (5 min TTL)                     │
│  │  ├─ Hit: Return cached NavMap                       │
│  │  └─ Miss: Query metadata                            │
│  ├─ For each parent in config:                         │
│  │  ├─ GetEntitySetNameAsync                           │
│  │  ├─ GetLookupNavigationAsync                        │
│  │  └─ GetCollectionNavigationAsync                    │
│  ├─ Build NavMap                                       │
│  ├─ Cache result                                       │
│  └─ Return NavMap                                      │
└─────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────┐
│  IDataverseService (from Task 7.1)                      │
│  ├─ GetEntitySetNameAsync                              │
│  ├─ GetLookupNavigationAsync                           │
│  └─ GetCollectionNavigationAsync                       │
└─────────────────────────────────────────────────────────┘
```

---

## Implementation Steps

### Step 1: Create Data Models

**File:** `src/api/Spe.Bff.Api/Models/NavMapModels.cs` (NEW)

```csharp
namespace Spe.Bff.Api.Models;

/// <summary>
/// Navigation metadata entry for a single parent entity
/// </summary>
public record NavEntry
{
    /// <summary>
    /// Entity set name (plural OData collection name)
    /// Example: "sprk_matters"
    /// </summary>
    public required string EntitySet { get; init; }

    /// <summary>
    /// Lookup attribute logical name on child entity
    /// Example: "sprk_matter"
    /// </summary>
    public required string LookupAttribute { get; init; }

    /// <summary>
    /// Navigation property name for @odata.bind (CASE-SENSITIVE!)
    /// Example: "sprk_Matter" (capital M)
    /// This is from ReferencingEntityNavigationPropertyName
    /// </summary>
    public required string NavProperty { get; init; }

    /// <summary>
    /// Collection navigation property for relationship URL (optional, for future Option B)
    /// Example: "sprk_matter_document"
    /// This is from ReferencedEntityNavigationPropertyName
    /// </summary>
    public string? CollectionNavProperty { get; init; }
}

/// <summary>
/// Navigation map response containing entries for all configured parent entities
/// </summary>
public record NavMapResponse
{
    /// <summary>
    /// Map of parent entity logical name to navigation metadata
    /// Key: Parent entity logical name (e.g., "sprk_matter")
    /// Value: Navigation metadata entry
    /// </summary>
    public required Dictionary<string, NavEntry> Parents { get; init; }

    /// <summary>
    /// API version
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// When this map was generated (for cache debugging)
    /// </summary>
    public required DateTime GeneratedAt { get; init; }

    /// <summary>
    /// Environment URL this map is for
    /// </summary>
    public string? Environment { get; init; }
}
```

---

### Step 2: Create Configuration Model

**File:** `src/api/Spe.Bff.Api/Configuration/NavigationMetadataOptions.cs` (NEW)

```csharp
namespace Spe.Bff.Api.Configuration;

/// <summary>
/// Configuration for navigation metadata service
/// </summary>
public class NavigationMetadataOptions
{
    public const string SectionName = "NavigationMetadata";

    /// <summary>
    /// Child entity logical name (always sprk_document)
    /// </summary>
    public string ChildEntity { get; set; } = "sprk_document";

    /// <summary>
    /// List of parent entity logical names to include in NavMap
    /// Example: ["sprk_matter", "sprk_project", "account", "contact"]
    /// </summary>
    public List<string> Parents { get; set; } = new();

    /// <summary>
    /// Cache duration in minutes (default: 5)
    /// </summary>
    public int CacheDurationMinutes { get; set; } = 5;
}
```

---

### Step 3: Create NavigationMetadataService

**File:** `src/api/Spe.Bff.Api/Services/NavigationMetadataService.cs` (NEW)

```csharp
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Spe.Bff.Api.Configuration;
using Spe.Bff.Api.Models;
using Spaarke.Dataverse;

namespace Spe.Bff.Api.Services;

public interface INavigationMetadataService
{
    Task<NavMapResponse> GetNavMapAsync(string version, string? environment, CancellationToken ct = default);
}

public class NavigationMetadataService : INavigationMetadataService
{
    private readonly IDataverseService _dataverse;
    private readonly IMemoryCache _cache;
    private readonly ILogger<NavigationMetadataService> _logger;
    private readonly NavigationMetadataOptions _options;

    public NavigationMetadataService(
        IDataverseService dataverse,
        IMemoryCache cache,
        ILogger<NavigationMetadataService> logger,
        IOptions<NavigationMetadataOptions> options)
    {
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<NavMapResponse> GetNavMapAsync(string version, string? environment, CancellationToken ct = default)
    {
        // Build cache key
        var cacheKey = $"navmap::{environment ?? "default"}::{version}";

        // Check cache first
        if (_cache.TryGetValue<NavMapResponse>(cacheKey, out var cached))
        {
            _logger.LogDebug("NavMap cache hit for key: {CacheKey}", cacheKey);
            return cached;
        }

        _logger.LogInformation(
            "NavMap cache miss for key: {CacheKey}. Querying metadata for {ParentCount} parents.",
            cacheKey,
            _options.Parents.Count);

        // Query metadata for all parents
        var parents = new Dictionary<string, NavEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var parentEntity in _options.Parents)
        {
            try
            {
                _logger.LogDebug("Querying metadata for parent: {ParentEntity}", parentEntity);

                // Query entity set name
                var entitySet = await _dataverse.GetEntitySetNameAsync(parentEntity, ct);

                // Query lookup navigation metadata
                // Relationship schema name follows pattern: {parent}_{child}
                var relationshipSchemaName = $"{parentEntity}_document";
                var lookupNav = await _dataverse.GetLookupNavigationAsync(
                    _options.ChildEntity,
                    relationshipSchemaName,
                    ct);

                // Query collection navigation property (for future Option B)
                var collectionNav = await _dataverse.GetCollectionNavigationAsync(
                    parentEntity,
                    relationshipSchemaName,
                    ct);

                // Build entry
                var entry = new NavEntry
                {
                    EntitySet = entitySet,
                    LookupAttribute = lookupNav.LogicalName,
                    NavProperty = lookupNav.NavigationPropertyName,  // CASE-SENSITIVE!
                    CollectionNavProperty = collectionNav
                };

                parents[parentEntity] = entry;

                _logger.LogInformation(
                    "Resolved metadata for {ParentEntity}: EntitySet={EntitySet}, NavProperty={NavProperty}",
                    parentEntity,
                    entitySet,
                    lookupNav.NavigationPropertyName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to query metadata for parent entity: {ParentEntity}. " +
                    "This entity will be excluded from NavMap.",
                    parentEntity);
                // Continue with other parents - don't fail entire request
            }
        }

        // Build response
        var response = new NavMapResponse
        {
            Parents = parents,
            Version = version,
            GeneratedAt = DateTime.UtcNow,
            Environment = environment
        };

        // Cache result
        var cacheOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_options.CacheDurationMinutes)
        };
        _cache.Set(cacheKey, response, cacheOptions);

        _logger.LogInformation(
            "NavMap generated and cached: {ParentCount} parents, Cache TTL: {CacheDuration} minutes",
            parents.Count,
            _options.CacheDurationMinutes);

        return response;
    }
}
```

---

### Step 4: Create NavMapController

**File:** `src/api/Spe.Bff.Api/Api/NavMapController.cs` (NEW)

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Spe.Bff.Api.Models;
using Spe.Bff.Api.Services;

namespace Spe.Bff.Api.Api;

/// <summary>
/// Navigation property metadata endpoint for PCF controls
/// </summary>
[ApiController]
[Route("api/pcf")]
[Authorize] // Reuse existing auth policy
public class NavMapController : ControllerBase
{
    private readonly INavigationMetadataService _metadataService;
    private readonly ILogger<NavMapController> _logger;

    public NavMapController(
        INavigationMetadataService metadataService,
        ILogger<NavMapController> logger)
    {
        _metadataService = metadataService ?? throw new ArgumentNullException(nameof(metadataService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Get navigation property metadata map for all configured parent entities
    /// </summary>
    /// <param name="v">API version (default: "1")</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>NavMap with navigation metadata for each parent entity</returns>
    /// <response code="200">Successfully retrieved navigation map</response>
    /// <response code="401">Unauthorized - invalid or missing JWT</response>
    /// <response code="500">Server error querying metadata</response>
    [HttpGet("dataverse-navmap")]
    [ProducesResponseType(typeof(NavMapResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<NavMapResponse>> GetNavMap(
        [FromQuery] string v = "1",
        CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("NavMap requested: Version={Version}, User={User}",
                v, User?.Identity?.Name ?? "Anonymous");

            // Get environment URL from header (optional)
            var environment = Request.Headers["X-Environment"].FirstOrDefault();

            // Get navigation map
            var navMap = await _metadataService.GetNavMapAsync(v, environment, ct);

            _logger.LogInformation(
                "NavMap returned successfully: {ParentCount} parents, Version={Version}",
                navMap.Parents.Count,
                navMap.Version);

            return Ok(navMap);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve navigation map: {ErrorMessage}", ex.Message);

            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new { error = "Failed to retrieve navigation metadata. Please try again later." });
        }
    }
}
```

---

### Step 5: Update Configuration

**File:** `src/api/Spe.Bff.Api/appsettings.json`

Add configuration section:

```json
{
  // ... existing config ...

  "NavigationMetadata": {
    "ChildEntity": "sprk_document",
    "Parents": [
      "sprk_matter",
      "sprk_project",
      "sprk_invoice",
      "account",
      "contact"
    ],
    "CacheDurationMinutes": 5
  }
}
```

**File:** `src/api/Spe.Bff.Api/appsettings.Development.json`

```json
{
  "NavigationMetadata": {
    "ChildEntity": "sprk_document",
    "Parents": [
      "sprk_matter"  // Start with just Matter in dev
    ],
    "CacheDurationMinutes": 1  // Shorter cache for dev/testing
  }
}
```

---

### Step 6: Register Services

**File:** `src/api/Spe.Bff.Api/Program.cs`

Add service registrations:

```csharp
// ... existing code ...

// Add configuration
builder.Services.Configure<NavigationMetadataOptions>(
    builder.Configuration.GetSection(NavigationMetadataOptions.SectionName));

// Add navigation metadata service
builder.Services.AddScoped<INavigationMetadataService, NavigationMetadataService>();

// Add memory cache (if not already registered)
builder.Services.AddMemoryCache();

// ... rest of existing code ...
```

---

## Error Handling

### Scenario 1: Parent Entity Not Found

**Behavior:** Log error, exclude from NavMap, continue with other parents

```csharp
catch (EntityNotFoundException ex)
{
    _logger.LogWarning(ex,
        "Parent entity {ParentEntity} not found in Dataverse. " +
        "Verify entity exists and is spelled correctly.",
        parentEntity);
    // Continue - don't fail entire request
}
```

### Scenario 2: Relationship Not Found

**Behavior:** Log error, exclude from NavMap

```csharp
catch (RelationshipNotFoundException ex)
{
    _logger.LogWarning(ex,
        "Relationship {RelationshipSchemaName} not found for {ParentEntity}. " +
        "Verify relationship exists between {ParentEntity} and sprk_document.",
        relationshipSchemaName,
        parentEntity);
}
```

### Scenario 3: Permission Denied

**Behavior:** Log error, return 500 (this affects all parents)

```csharp
catch (UnauthorizedAccessException ex)
{
    _logger.LogError(ex,
        "Insufficient permissions to query EntityDefinitions metadata. " +
        "Application user needs 'Read' permission on Entity Definitions.");

    throw; // Fail request - auth issue affects all queries
}
```

### Scenario 4: Dataverse Unavailable

**Behavior:** Log error, return 500, client falls back to cache/hardcoded

```csharp
catch (HttpRequestException ex)
{
    _logger.LogError(ex,
        "Dataverse service unavailable. Client will use fallback navigation map.");

    throw; // Let controller return 500, PCF will use fallback
}
```

---

## Testing

### Manual Testing with Postman/curl

**1. Test Basic Request:**

```bash
# Get JWT token (adjust for your auth)
TOKEN="your-jwt-token"

# Request NavMap
curl -X GET "https://localhost:5001/api/pcf/dataverse-navmap?v=1" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Accept: application/json"
```

**Expected Response:**
```json
{
  "parents": {
    "sprk_matter": {
      "entitySet": "sprk_matters",
      "lookupAttribute": "sprk_matter",
      "navProperty": "sprk_Matter",
      "collectionNavProperty": "sprk_matter_document"
    },
    "sprk_project": {
      "entitySet": "sprk_projects",
      "lookupAttribute": "sprk_project",
      "navProperty": "sprk_Project",
      "collectionNavProperty": "sprk_project_document"
    }
  },
  "version": "1",
  "generatedAt": "2025-10-19T22:30:00Z",
  "environment": null
}
```

**2. Test Cache Hit:**

```bash
# First request - cache miss (check logs: "cache miss")
curl -X GET "https://localhost:5001/api/pcf/dataverse-navmap?v=1" \
  -H "Authorization: Bearer $TOKEN"

# Second request within 5 min - cache hit (check logs: "cache hit")
curl -X GET "https://localhost:5001/api/pcf/dataverse-navmap?v=1" \
  -H "Authorization: Bearer $TOKEN"
```

**Expected:** Second request returns instantly, logs show "cache hit"

**3. Test Without Auth:**

```bash
# Request without token
curl -X GET "https://localhost:5001/api/pcf/dataverse-navmap?v=1"
```

**Expected:** HTTP 401 Unauthorized

**4. Test with Invalid Parent:**

Update appsettings.json temporarily:
```json
{
  "NavigationMetadata": {
    "Parents": ["sprk_matter", "invalid_entity"]
  }
}
```

**Expected:**
- Log warning about invalid_entity
- Response includes sprk_matter only
- HTTP 200 OK (partial success)

---

### Unit Tests

**File:** `tests/unit/Spe.Bff.Api.Tests/Services/NavigationMetadataServiceTests.cs` (NEW)

```csharp
public class NavigationMetadataServiceTests
{
    [Fact]
    public async Task GetNavMapAsync_ValidParents_ReturnsCompleteMap()
    {
        // Arrange
        var mockDataverse = CreateMockDataverseService();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var options = CreateOptions(new[] { "sprk_matter", "sprk_project" });
        var service = new NavigationMetadataService(mockDataverse, cache, Mock.Of<ILogger>(), options);

        // Act
        var result = await service.GetNavMapAsync("1", null);

        // Assert
        Assert.Equal(2, result.Parents.Count);
        Assert.Contains("sprk_matter", result.Parents.Keys);
        Assert.Contains("sprk_project", result.Parents.Keys);
        Assert.Equal("sprk_Matter", result.Parents["sprk_matter"].NavProperty); // Capital M!
    }

    [Fact]
    public async Task GetNavMapAsync_SecondCall_ReturnsCachedResult()
    {
        // Arrange
        var mockDataverse = CreateMockDataverseService();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var options = CreateOptions(new[] { "sprk_matter" });
        var service = new NavigationMetadataService(mockDataverse, cache, Mock.Of<ILogger>(), options);

        // Act
        var result1 = await service.GetNavMapAsync("1", null);
        var result2 = await service.GetNavMapAsync("1", null);

        // Assert
        Assert.Same(result1, result2); // Same instance = cached
        Mock.Get(mockDataverse).Verify(d => d.GetEntitySetNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetNavMapAsync_InvalidParent_ExcludesFromMap()
    {
        // Arrange
        var mockDataverse = Mock.Of<IDataverseService>();
        Mock.Get(mockDataverse)
            .Setup(d => d.GetEntitySetNameAsync("invalid_entity", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new EntityNotFoundException("Entity not found"));

        var cache = new MemoryCache(new MemoryCacheOptions());
        var options = CreateOptions(new[] { "invalid_entity" });
        var service = new NavigationMetadataService(mockDataverse, cache, Mock.Of<ILogger>(), options);

        // Act
        var result = await service.GetNavMapAsync("1", null);

        // Assert
        Assert.Empty(result.Parents); // Invalid entity excluded
    }
}
```

---

## Integration Testing

### Test Against Real Dataverse

**File:** `tests/integration/Spe.Bff.Api.Tests/NavMapIntegrationTests.cs`

```csharp
[Trait("Category", "Integration")]
public class NavMapIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public NavMapIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetNavMap_WithAuth_ReturnsValidResponse()
    {
        // Arrange
        var client = _factory.CreateClient();
        var token = await GetAuthToken(); // Your auth helper
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync("/api/pcf/dataverse-navmap?v=1");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var navMap = JsonSerializer.Deserialize<NavMapResponse>(content);

        Assert.NotNull(navMap);
        Assert.NotEmpty(navMap.Parents);
        Assert.Equal("1", navMap.Version);

        // Verify sprk_matter entry
        Assert.Contains("sprk_matter", navMap.Parents.Keys);
        var matter = navMap.Parents["sprk_matter"];
        Assert.Equal("sprk_matters", matter.EntitySet);
        Assert.Equal("sprk_Matter", matter.NavProperty); // Verify case!
    }
}
```

---

## Validation Checklist

**BEFORE Starting:**
- [ ] Task 7.1 complete (IDataverseService extended)
- [ ] Reviewed existing controller patterns (OBOEndpoints.cs)
- [ ] Reviewed existing DI registration (Program.cs)
- [ ] Understand appsettings.json structure

**DURING Implementation:**
- [ ] Created NavEntry and NavMapResponse models
- [ ] Created NavigationMetadataOptions configuration
- [ ] Created NavigationMetadataService with caching
- [ ] Created NavMapController with GET endpoint
- [ ] Added configuration to appsettings.json
- [ ] Registered services in Program.cs
- [ ] Added error handling for all scenarios
- [ ] Added logging statements
- [ ] No compilation errors
- [ ] No breaking changes to existing endpoints

**AFTER Completion:**
- [ ] Manual test: Request NavMap with Postman/curl
- [ ] Manual test: Verify cache hit on second request
- [ ] Manual test: Verify 401 without auth
- [ ] Manual test: Verify partial success with invalid parent
- [ ] Unit tests written and passing
- [ ] Integration test against real Dataverse passing
- [ ] Logs show expected messages (cache hit/miss, parent count)
- [ ] Committed changes with proper message

---

## Expected Results

### NavMap Response for sprk_matter

```json
{
  "parents": {
    "sprk_matter": {
      "entitySet": "sprk_matters",
      "lookupAttribute": "sprk_matter",
      "navProperty": "sprk_Matter",
      "collectionNavProperty": "sprk_matter_document"
    }
  },
  "version": "1",
  "generatedAt": "2025-10-19T22:30:00.123Z",
  "environment": null
}
```

### Cache Behavior

**First Request:**
```
[INFO] NavMap cache miss for key: navmap::default::1. Querying metadata for 5 parents.
[INFO] Resolved metadata for sprk_matter: EntitySet=sprk_matters, NavProperty=sprk_Matter
[INFO] NavMap generated and cached: 5 parents, Cache TTL: 5 minutes
```

**Second Request (within 5 min):**
```
[DEBUG] NavMap cache hit for key: navmap::default::1
```

---

## Actual Results

**Completion Date:** _______________
**Implemented By:** _______________

**Endpoint Test:**
```
GET /api/pcf/dataverse-navmap?v=1
Status: _______________
Response: _______________
```

**Cache Test:**
```
First Request Time: _______________ ms
Second Request Time: _______________ ms (should be <10ms)
Cache Hit Logged: [ ] Yes / [ ] No
```

**Test Results:**
```
Unit Tests: ___/___  passed
Integration Tests: [ ] Pass / [ ] Fail
Manual Tests: [ ] Pass / [ ] Fail
```

**Notes/Issues:**
```
(Document any issues, workarounds, or deviations from plan)
```

---

## Commit Message Template

```
feat(bff): Add NavMapController for dynamic navigation property discovery

Create server-side navigation metadata service with caching:
- NavMapController: GET /api/pcf/dataverse-navmap?v=1
- NavigationMetadataService: Queries metadata and caches results (5 min TTL)
- Configuration: List of parent entities in appsettings.json

Components:
- Models: NavEntry, NavMapResponse (navigation metadata structures)
- Configuration: NavigationMetadataOptions (parent list, cache TTL)
- Service: NavigationMetadataService (metadata querying + caching)
- Controller: NavMapController (REST endpoint)
- DI registration in Program.cs

Features:
- Memory caching with 5-minute TTL (configurable)
- Queries all configured parents in parallel
- Excludes invalid parents (partial success, not fail-all)
- Returns case-sensitive navigation properties (e.g., "sprk_Matter")
- Error handling for entity not found, relationship not found, permissions
- Comprehensive logging (cache hit/miss, parent count, errors)

Testing:
- Unit tests: Service caching, error handling
- Integration tests: Real Dataverse queries
- Manual tests: Postman validation

Protection:
- New endpoint only (no changes to existing upload API)
- Authorization required (reuses existing auth policy)
- Graceful degradation (excludes invalid entities)

Benefits:
- Eliminates manual PowerShell validation
- Enables scalable multi-parent support
- Future-proof against schema changes
- Client can cache response for session

Task: 7.2 - Create NavMapController in Spe.Bff.Api
Phase: 7 - Navigation Property Metadata Service

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude <noreply@anthropic.com>
```

---

## Next Task

After completing Task 7.2, proceed to:
**[TASK-7.3-CREATE-NAVMAP-CLIENT.md](./TASK-7.3-CREATE-NAVMAP-CLIENT.md)**

---

**Task Owner:** _______________
**Completion Date:** _______________
**Reviewed By:** _______________
**Status:** ⬜ Not Started | ⬜ In Progress | ⬜ Blocked | ⬜ Complete
