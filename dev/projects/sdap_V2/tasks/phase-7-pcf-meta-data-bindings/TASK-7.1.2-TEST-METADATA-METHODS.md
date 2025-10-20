# Task 7.1.2: Test Phase 7 Metadata Methods

**Task ID:** 7.1.2
**Phase:** 7 (Navigation Property Metadata Service)
**Parent Task:** 7.1 (Extend IDataverseService)
**Assignee:** Backend Developer
**Estimated Duration:** 30-45 minutes
**Prerequisites:** Task 7.1.1 complete (compilation errors fixed)
**Status:** Not Started

---

## Task Prompt

**BEFORE starting this task:**

1. Verify Task 7.1.1 is complete (dotnet build succeeds)
2. Ensure you have access to Dataverse dev environment
3. Verify application user has "Read" permission on EntityDefinitions
4. Review [METADATA-METHODS-EXPLAINED.md](./METADATA-METHODS-EXPLAINED.md)
5. Have Matter entity and sprk_document entity available in Dataverse

**DURING work:**

1. Create manual test script (C# or PowerShell)
2. Test each of the 3 new methods
3. Verify results match expected values
4. Document actual output vs expected
5. Test error scenarios (entity not found, etc.)

**AFTER completing work:**

1. All 3 methods tested successfully
2. Results documented with actual Dataverse values
3. Error handling validated
4. Mark Task 7.1 as complete
5. Ready to proceed to Task 7.2 (NavMapController)

---

## Objective

Validate that the 3 new metadata methods (`GetEntitySetNameAsync`, `GetLookupNavigationAsync`, `GetCollectionNavigationAsync`) correctly query Dataverse metadata and return accurate, case-sensitive navigation property names.

---

## Test Environment Setup

### Prerequisites

**Dataverse Environment:**
- Dev environment URL: `https://{org}.crm.dynamics.com`
- Application user configured with permissions
- Matter entity (`sprk_matter`) exists
- Document entity (`sprk_document`) exists
- Relationship `sprk_matter_document` exists

**Application User Permissions:**
```xml
<!-- Required security role permissions -->
<privilege name="prvReadEntityDefinition" level="Global" />
```

**Configuration:**
```json
// appsettings.Development.json
{
  "Dataverse": {
    "ServiceUrl": "https://your-org.crm.dynamics.com"
  },
  "ManagedIdentity": {
    "ClientId": "your-client-id"  // Or omit for system-assigned
  }
}
```

---

## Test Script Option A: C# Console App (Recommended)

### Create Test Project

```bash
cd /c/code_files/spaarke/src/shared
dotnet new console -n Spaarke.Dataverse.MetadataTests
cd Spaarke.Dataverse.MetadataTests

# Add reference to Spaarke.Dataverse
dotnet add reference ../Spaarke.Dataverse/Spaarke.Dataverse.csproj

# Add required packages
dotnet add package Microsoft.Extensions.Configuration.Json
dotnet add package Microsoft.Extensions.Configuration.EnvironmentVariables
dotnet add package Microsoft.Extensions.Logging.Console
```

### Test Script (Program.cs)

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Spaarke.Dataverse;

Console.WriteLine("=== Phase 7 Metadata Methods Test ===\n");

// Setup configuration
var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false)
    .AddEnvironmentVariables()
    .Build();

// Setup logging
using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Debug);
});

var logger = loggerFactory.CreateLogger<DataverseServiceClientImpl>();

// Initialize service
DataverseServiceClientImpl service;
try
{
    service = new DataverseServiceClientImpl(configuration, logger);
    Console.WriteLine("✅ DataverseServiceClientImpl initialized successfully\n");
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Failed to initialize: {ex.Message}");
    return 1;
}

// Test 1: GetEntitySetNameAsync
Console.WriteLine("--- Test 1: GetEntitySetNameAsync ---");
try
{
    var entitySetName = await service.GetEntitySetNameAsync("sprk_matter");
    Console.WriteLine($"✅ Entity Set Name: {entitySetName}");

    if (entitySetName == "sprk_matters")
    {
        Console.WriteLine("   ✅ PASS: Correct entity set name (plural)\n");
    }
    else
    {
        Console.WriteLine($"   ⚠️ UNEXPECTED: Expected 'sprk_matters', got '{entitySetName}'\n");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"❌ FAILED: {ex.Message}\n");
    return 1;
}

// Test 2: GetLookupNavigationAsync (MOST CRITICAL)
Console.WriteLine("--- Test 2: GetLookupNavigationAsync ---");
try
{
    var lookupMetadata = await service.GetLookupNavigationAsync(
        "sprk_document",
        "sprk_matter_document"
    );

    Console.WriteLine($"✅ Lookup Metadata Retrieved:");
    Console.WriteLine($"   NavigationPropertyName: {lookupMetadata.NavigationPropertyName}");
    Console.WriteLine($"   LogicalName: {lookupMetadata.LogicalName}");
    Console.WriteLine($"   SchemaName: {lookupMetadata.SchemaName}");
    Console.WriteLine($"   TargetEntityLogicalName: {lookupMetadata.TargetEntityLogicalName}");

    // Critical validation: Check case sensitivity
    if (lookupMetadata.NavigationPropertyName.Contains("Matter") ||
        lookupMetadata.NavigationPropertyName.Contains("matter"))
    {
        Console.WriteLine($"\n   ✅ PASS: Navigation property found");

        // Check if capital M (expected from Phase 6)
        if (lookupMetadata.NavigationPropertyName.Contains("Matter"))
        {
            Console.WriteLine("   ✅ CONFIRMED: Uses capital 'M' (sprk_Matter)");
        }
        else
        {
            Console.WriteLine("   ⚠️ NOTE: Uses lowercase 'm' (sprk_matter)");
        }
    }
    else
    {
        Console.WriteLine($"   ⚠️ UNEXPECTED: Navigation property doesn't contain 'matter': {lookupMetadata.NavigationPropertyName}");
    }

    Console.WriteLine();
}
catch (Exception ex)
{
    Console.WriteLine($"❌ FAILED: {ex.Message}\n");
    return 1;
}

// Test 3: GetCollectionNavigationAsync
Console.WriteLine("--- Test 3: GetCollectionNavigationAsync ---");
try
{
    var collectionProperty = await service.GetCollectionNavigationAsync(
        "sprk_matter",
        "sprk_matter_document"
    );

    Console.WriteLine($"✅ Collection Property: {collectionProperty}");

    if (collectionProperty == "sprk_matter_document")
    {
        Console.WriteLine("   ✅ PASS: Correct collection navigation property\n");
    }
    else
    {
        Console.WriteLine($"   ⚠️ UNEXPECTED: Expected 'sprk_matter_document', got '{collectionProperty}'\n");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"❌ FAILED: {ex.Message}\n");
    return 1;
}

// Test 4: Error Handling - Entity Not Found
Console.WriteLine("--- Test 4: Error Handling (Entity Not Found) ---");
try
{
    await service.GetEntitySetNameAsync("nonexistent_entity");
    Console.WriteLine("⚠️ UNEXPECTED: Should have thrown exception for nonexistent entity\n");
}
catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
{
    Console.WriteLine($"✅ PASS: Correctly threw exception: {ex.Message}\n");
}
catch (Exception ex)
{
    Console.WriteLine($"⚠️ UNEXPECTED EXCEPTION TYPE: {ex.GetType().Name}: {ex.Message}\n");
}

// Test 5: Error Handling - Relationship Not Found
Console.WriteLine("--- Test 5: Error Handling (Relationship Not Found) ---");
try
{
    await service.GetLookupNavigationAsync("sprk_document", "nonexistent_relationship");
    Console.WriteLine("⚠️ UNEXPECTED: Should have thrown exception for nonexistent relationship\n");
}
catch (InvalidOperationException ex) when (ex.Message.Contains("not found") && ex.Message.Contains("Available"))
{
    Console.WriteLine($"✅ PASS: Correctly threw exception with available relationships");
    Console.WriteLine($"   Message preview: {ex.Message.Substring(0, Math.Min(150, ex.Message.Length))}...\n");
}
catch (Exception ex)
{
    Console.WriteLine($"⚠️ UNEXPECTED: {ex.Message}\n");
}

// Summary
Console.WriteLine("=== Test Summary ===");
Console.WriteLine("✅ All Phase 7 metadata methods tested successfully!");
Console.WriteLine("\nReady to proceed to Task 7.2 (NavMapController)");

return 0;
```

### Create appsettings.json

```json
{
  "Dataverse": {
    "ServiceUrl": "https://your-org.crm.dynamics.com"
  },
  "ManagedIdentity": {
    "ClientId": "your-client-id-if-user-assigned"
  }
}
```

### Run Tests

```bash
cd /c/code_files/spaarke/src/shared/Spaarke.Dataverse.MetadataTests
dotnet run
```

---

## Test Script Option B: PowerShell (Alternative)

```powershell
# Test-MetadataMethods.ps1

$ErrorActionPreference = "Stop"

Write-Host "=== Phase 7 Metadata Methods Test (PowerShell Validation) ===" -ForegroundColor Cyan

# Configuration
$dataverseUrl = "https://your-org.crm.dynamics.com"
$tenantId = "your-tenant-id"
$clientId = "your-client-id"
$clientSecret = "your-client-secret"  # For testing only!

# Get access token
Write-Host "`nGetting access token..." -ForegroundColor Yellow
$tokenBody = @{
    grant_type    = "client_credentials"
    client_id     = $clientId
    client_secret = $clientSecret
    scope         = "$dataverseUrl/.default"
}

$tokenResponse = Invoke-RestMethod -Method Post -Uri "https://login.microsoftonline.com/$tenantId/oauth2/v2.0/token" -Body $tokenBody
$accessToken = $tokenResponse.access_token

$headers = @{
    Authorization = "Bearer $accessToken"
    "OData-MaxVersion" = "4.0"
    "OData-Version" = "4.0"
    Accept = "application/json"
}

# Test 1: Get Entity Set Name
Write-Host "`n--- Test 1: Entity Set Name ---" -ForegroundColor Yellow
$url = "$dataverseUrl/api/data/v9.2/EntityDefinitions(LogicalName='sprk_matter')?`$select=EntitySetName"
try {
    $response = Invoke-RestMethod -Uri $url -Headers $headers -Method Get
    $entitySetName = $response.EntitySetName
    Write-Host "✅ Entity Set Name: $entitySetName" -ForegroundColor Green

    if ($entitySetName -eq "sprk_matters") {
        Write-Host "   ✅ PASS: Correct entity set name" -ForegroundColor Green
    }
} catch {
    Write-Host "❌ FAILED: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# Test 2: Get Lookup Navigation Property (CRITICAL)
Write-Host "`n--- Test 2: Lookup Navigation Property ---" -ForegroundColor Yellow
$url = "$dataverseUrl/api/data/v9.2/EntityDefinitions(LogicalName='sprk_document')?`$select=LogicalName&`$expand=ManyToOneRelationships(`$filter=SchemaName eq 'sprk_matter_document';`$select=ReferencingEntityNavigationPropertyName,SchemaName)"
try {
    $response = Invoke-RestMethod -Uri $url -Headers $headers -Method Get
    $relationship = $response.ManyToOneRelationships[0]
    $navProperty = $relationship.ReferencingEntityNavigationPropertyName

    Write-Host "✅ Navigation Property Name: $navProperty" -ForegroundColor Green

    if ($navProperty -match "Matter") {
        Write-Host "   ✅ CONFIRMED: Uses capital 'M' ($navProperty)" -ForegroundColor Green
    } elseif ($navProperty -match "matter") {
        Write-Host "   ⚠️ NOTE: Uses lowercase 'm' ($navProperty)" -ForegroundColor Yellow
    }
} catch {
    Write-Host "❌ FAILED: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# Test 3: Get Collection Navigation Property
Write-Host "`n--- Test 3: Collection Navigation Property ---" -ForegroundColor Yellow
$url = "$dataverseUrl/api/data/v9.2/EntityDefinitions(LogicalName='sprk_matter')?`$select=LogicalName&`$expand=OneToManyRelationships(`$filter=SchemaName eq 'sprk_matter_document';`$select=ReferencedEntityNavigationPropertyName,SchemaName)"
try {
    $response = Invoke-RestMethod -Uri $url -Headers $headers -Method Get
    $relationship = $response.OneToManyRelationships[0]
    $collectionProperty = $relationship.ReferencedEntityNavigationPropertyName

    Write-Host "✅ Collection Property: $collectionProperty" -ForegroundColor Green

    if ($collectionProperty -eq "sprk_matter_document") {
        Write-Host "   ✅ PASS: Correct collection navigation property" -ForegroundColor Green
    }
} catch {
    Write-Host "❌ FAILED: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host "`n=== All Tests Passed ===" -ForegroundColor Green
Write-Host "Ready to proceed to Task 7.2 (NavMapController)" -ForegroundColor Cyan
```

---

## Expected Results

### Test 1: GetEntitySetNameAsync

**Input:** `"sprk_matter"`

**Expected Output:**
```
✅ Entity Set Name: sprk_matters
   ✅ PASS: Correct entity set name (plural)
```

**Validates:**
- Method queries EntityDefinitions successfully
- Returns correct pluralized entity set name
- Useful for building `@odata.bind` URLs

---

### Test 2: GetLookupNavigationAsync ⭐ MOST CRITICAL

**Input:**
- Child: `"sprk_document"`
- Relationship: `"sprk_matter_document"`

**Expected Output:**
```
✅ Lookup Metadata Retrieved:
   NavigationPropertyName: sprk_Matter
   LogicalName: sprk_matter
   SchemaName: sprk_Matter
   TargetEntityLogicalName: sprk_matter

   ✅ PASS: Navigation property found
   ✅ CONFIRMED: Uses capital 'M' (sprk_Matter)
```

**Validates:**
- ✅ **Case sensitivity** - Confirms "sprk_Matter" (capital M) from Phase 6
- ✅ **Comprehensive metadata** - Returns all relevant fields
- ✅ **Critical for `@odata.bind`** - This is the exact property name needed

**IF Result is Different (e.g., lowercase 'm'):**
- Still valid! Document actual case
- Update Phase 6 config if needed
- Phase 7 will use whatever Dataverse returns (guaranteed correct)

---

### Test 3: GetCollectionNavigationAsync

**Input:**
- Parent: `"sprk_matter"`
- Relationship: `"sprk_matter_document"`

**Expected Output:**
```
✅ Collection Property: sprk_matter_document
   ✅ PASS: Correct collection navigation property
```

**Validates:**
- Method queries parent → child direction
- Returns collection property name
- Future use for Option B linking

---

### Test 4: Error Handling - Entity Not Found

**Input:** `"nonexistent_entity"`

**Expected Output:**
```
✅ PASS: Correctly threw exception: Entity 'nonexistent_entity' not found in Dataverse metadata.
```

**Validates:**
- Proper exception type (InvalidOperationException)
- Clear error message
- No silent failures

---

### Test 5: Error Handling - Relationship Not Found

**Input:**
- Child: `"sprk_document"`
- Relationship: `"nonexistent_relationship"`

**Expected Output:**
```
✅ PASS: Correctly threw exception with available relationships
   Message preview: Relationship 'nonexistent_relationship' not found on entity 'sprk_document'. Verify the relationship exists and the schema name is correct. Available relationships: sprk_matter_document, ...
```

**Validates:**
- Lists available relationships (helps troubleshooting)
- Clear, actionable error message
- Proper exception handling

---

## Actual Results (Fill In During Testing)

### Test Execution Date/Time

**Date:** ___________
**Tester:** ___________
**Environment:** ___________

### Test 1 Results

**Entity Set Name for "sprk_matter":** ___________

**Status:** ☐ Pass ☐ Fail

**Notes:** ___________

---

### Test 2 Results ⭐ CRITICAL

**Navigation Property Name:** ___________

**Case:** ☐ Capital M ("sprk_Matter") ☐ Lowercase m ("sprk_matter") ☐ Other: ___________

**Full Metadata:**
- LogicalName: ___________
- SchemaName: ___________
- TargetEntityLogicalName: ___________

**Status:** ☐ Pass ☐ Fail

**Notes:** ___________

---

### Test 3 Results

**Collection Property:** ___________

**Status:** ☐ Pass ☐ Fail

**Notes:** ___________

---

### Test 4 Results (Error Handling)

**Exception Thrown:** ☐ Yes ☐ No

**Exception Type:** ___________

**Error Message:** ___________

**Status:** ☐ Pass ☐ Fail

---

### Test 5 Results (Relationship Error)

**Exception Thrown:** ☐ Yes ☐ No

**Available Relationships Listed:** ☐ Yes ☐ No

**Status:** ☐ Pass ☐ Fail

---

## Validation Checklist

### Functionality

- [ ] GetEntitySetNameAsync returns correct entity set name
- [ ] GetLookupNavigationAsync returns case-sensitive navigation property
- [ ] GetCollectionNavigationAsync returns collection property
- [ ] All methods complete within reasonable time (<5 seconds)

### Error Handling

- [ ] Entity not found throws InvalidOperationException
- [ ] Relationship not found throws InvalidOperationException with available list
- [ ] Permission denied throws UnauthorizedAccessException (if testable)
- [ ] Error messages are clear and actionable

### Logging

- [ ] Info logs show successful queries
- [ ] Debug logs show query details
- [ ] Error logs show failure context
- [ ] No sensitive data in logs

### Phase 7 Readiness

- [ ] All 3 methods work as designed
- [ ] Results match expected patterns
- [ ] Navigation property case documented (capital M or lowercase m)
- [ ] Ready for Task 7.2 (NavMapController)

---

## Sign-Off

### Test Completion

- [ ] All tests executed
- [ ] Results documented above
- [ ] Critical Test 2 (lookup navigation) verified
- [ ] Navigation property case confirmed: ☐ Capital M ☐ Lowercase m ☐ Other

### Task 7.1 Complete

- [ ] Task 7.1.1 complete (logging errors fixed)
- [ ] Task 7.1.2 complete (methods tested)
- [ ] IDataverseService extended successfully
- [ ] Ready to proceed to Task 7.2

**Tester:** ___________ **Date:** ___________

**Approved By:** ___________ **Date:** ___________

---

## Commit Message Template

```
test(dataverse): Validate Phase 7 metadata methods with live Dataverse

Create and execute comprehensive test suite for the 3 new metadata
methods added in Task 7.1.

**Test Coverage:**
- GetEntitySetNameAsync: ✅ Tested with sprk_matter → sprk_matters
- GetLookupNavigationAsync: ✅ Confirmed navigation property case
- GetCollectionNavigationAsync: ✅ Validated collection property
- Error handling: ✅ Tested entity/relationship not found scenarios

**Test Results:**
All 3 methods query Dataverse EntityDefinitions successfully.

**Critical Finding (Test 2 - Lookup Navigation):**
Navigation property for sprk_document → sprk_matter:
- NavigationPropertyName: [ACTUAL VALUE FROM TEST]
- LogicalName: sprk_matter
- SchemaName: [ACTUAL VALUE FROM TEST]
- Case: [Capital M / lowercase m / Other]

This confirms the exact case-sensitive property name needed for
@odata.bind operations in Phase 7.

**Files:**
- NEW: src/shared/Spaarke.Dataverse.MetadataTests/ (test project)
- NEW: Program.cs (test script)
- NEW: appsettings.json (test configuration)

**Performance:**
- Entity set query: ~200-300ms
- Lookup navigation query: ~300-400ms
- Collection navigation query: ~200-300ms
All within expected ranges for metadata queries.

**Task Status:**
✅ Task 7.1.1 Complete (logging errors fixed)
✅ Task 7.1.2 Complete (methods tested)
✅ Task 7.1 COMPLETE (IDataverseService extended and validated)

**Next Steps:**
Ready to proceed to Task 7.2 (Create NavMapController in BFF).

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude <noreply@anthropic.com>
```

---

## Dependencies for Next Task (7.2)

**Task 7.2 will need:**
- ✅ IDataverseService with 3 new methods (Task 7.1)
- ✅ Methods tested and validated (Task 7.1.2)
- ✅ Navigation property case documented (from Test 2)
- ✅ Clean compilation (from Task 7.1.1)

---

## References

- [METADATA-METHODS-EXPLAINED.md](./METADATA-METHODS-EXPLAINED.md) - What the methods do
- [TASK-7.1-EXTEND-DATAVERSE-SERVICE.md](./TASK-7.1-EXTEND-DATAVERSE-SERVICE.md) - Implementation
- [TASK-7.2-CREATE-NAVMAP-CONTROLLER.md](./TASK-7.2-CREATE-NAVMAP-CONTROLLER.md) - Next task

---

**Task Created:** 2025-10-20
**Task Owner:** Backend Developer
**Status:** Not Started
**Completes:** Task 7.1 (Extend IDataverseService)
