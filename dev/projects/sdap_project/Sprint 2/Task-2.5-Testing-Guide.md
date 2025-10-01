# Task 2.5 - SPE Container & File API Testing Guide

## Overview
This guide provides comprehensive testing procedures for the newly implemented SPE Container and File APIs in Task 2.5.

## Prerequisites

### 1. Environment Setup
- Azure Service Principal with SharePoint Embedded permissions
- Container Type registered in Azure AD
- Local development environment configured with:
  - `appsettings.Development.json` with valid credentials
  - Power Platform CLI authenticated
  - .NET 8 SDK installed

### 2. Required Configuration Values
From your environment, you'll need:
- **ContainerTypeId**: Your SPE Container Type GUID (from Azure AD app registration)
- **TenantId**: Your Azure AD tenant ID
- **ClientId**: Service principal client ID
- **ClientSecret**: Service principal secret

## Testing Methods

### Method 1: PowerShell Script Testing (Recommended)

Create a PowerShell test script to call the APIs directly:

```powershell
# Test-SpeApis.ps1
$baseUrl = "https://localhost:7123"  # Adjust to your local port
$containerTypeId = "YOUR-CONTAINER-TYPE-GUID"

# Get access token (if using authentication)
# For local testing, you may need to disable auth or get a valid token

function Test-CreateContainer {
    Write-Host "Testing: Create Container" -ForegroundColor Cyan

    $body = @{
        containerTypeId = $containerTypeId
        displayName = "Test Container $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
        description = "Created via API test"
    } | ConvertTo-Json

    try {
        $response = Invoke-RestMethod -Uri "$baseUrl/api/containers" `
            -Method POST `
            -Body $body `
            -ContentType "application/json" `
            -SkipCertificateCheck

        Write-Host "✓ Container created: $($response.id)" -ForegroundColor Green
        return $response.id
    }
    catch {
        Write-Host "✗ Failed: $($_.Exception.Message)" -ForegroundColor Red
        return $null
    }
}

function Test-ListContainers {
    Write-Host "Testing: List Containers" -ForegroundColor Cyan

    try {
        $response = Invoke-RestMethod -Uri "$baseUrl/api/containers?containerTypeId=$containerTypeId" `
            -Method GET `
            -SkipCertificateCheck

        Write-Host "✓ Found $($response.Count) containers" -ForegroundColor Green
        return $response
    }
    catch {
        Write-Host "✗ Failed: $($_.Exception.Message)" -ForegroundColor Red
        return $null
    }
}

function Test-GetContainerDrive {
    param([string]$containerId)

    Write-Host "Testing: Get Container Drive" -ForegroundColor Cyan

    try {
        $response = Invoke-RestMethod -Uri "$baseUrl/api/containers/$containerId/drive" `
            -Method GET `
            -SkipCertificateCheck

        Write-Host "✓ Drive retrieved: $($response.id)" -ForegroundColor Green
        return $response.id
    }
    catch {
        Write-Host "✗ Failed: $($_.Exception.Message)" -ForegroundColor Red
        return $null
    }
}

function Test-UploadSmallFile {
    param(
        [string]$driveId,
        [string]$fileName = "test.txt"
    )

    Write-Host "Testing: Upload Small File" -ForegroundColor Cyan

    # Create a temporary test file
    $tempFile = New-TemporaryFile
    "Test content - $(Get-Date)" | Out-File $tempFile -Encoding UTF8

    try {
        $response = Invoke-RestMethod -Uri "$baseUrl/api/drives/$driveId/items/root:/$fileName" `
            -Method PUT `
            -InFile $tempFile `
            -ContentType "text/plain" `
            -SkipCertificateCheck

        Write-Host "✓ File uploaded: $($response.id)" -ForegroundColor Green
        Remove-Item $tempFile
        return $response.id
    }
    catch {
        Write-Host "✗ Failed: $($_.Exception.Message)" -ForegroundColor Red
        Remove-Item $tempFile
        return $null
    }
}

function Test-ListDriveChildren {
    param([string]$driveId)

    Write-Host "Testing: List Drive Children" -ForegroundColor Cyan

    try {
        $response = Invoke-RestMethod -Uri "$baseUrl/api/drives/$driveId/children" `
            -Method GET `
            -SkipCertificateCheck

        Write-Host "✓ Found $($response.Count) items" -ForegroundColor Green
        $response | ForEach-Object { Write-Host "  - $($_.name)" -ForegroundColor Gray }
        return $response
    }
    catch {
        Write-Host "✗ Failed: $($_.Exception.Message)" -ForegroundColor Red
        return $null
    }
}

function Test-GetFileMetadata {
    param(
        [string]$driveId,
        [string]$itemId
    )

    Write-Host "Testing: Get File Metadata" -ForegroundColor Cyan

    try {
        $response = Invoke-RestMethod -Uri "$baseUrl/api/drives/$driveId/items/$itemId" `
            -Method GET `
            -SkipCertificateCheck

        Write-Host "✓ Metadata retrieved: $($response.name)" -ForegroundColor Green
        return $response
    }
    catch {
        Write-Host "✗ Failed: $($_.Exception.Message)" -ForegroundColor Red
        return $null
    }
}

function Test-DownloadFile {
    param(
        [string]$driveId,
        [string]$itemId
    )

    Write-Host "Testing: Download File" -ForegroundColor Cyan

    try {
        $outFile = "downloaded-test.txt"
        Invoke-RestMethod -Uri "$baseUrl/api/drives/$driveId/items/$itemId/content" `
            -Method GET `
            -OutFile $outFile `
            -SkipCertificateCheck

        $content = Get-Content $outFile -Raw
        Write-Host "✓ File downloaded: $(($content -split "`n").Count) lines" -ForegroundColor Green
        Remove-Item $outFile
        return $true
    }
    catch {
        Write-Host "✗ Failed: $($_.Exception.Message)" -ForegroundColor Red
        return $false
    }
}

function Test-DeleteFile {
    param(
        [string]$driveId,
        [string]$itemId
    )

    Write-Host "Testing: Delete File" -ForegroundColor Cyan

    try {
        Invoke-RestMethod -Uri "$baseUrl/api/drives/$driveId/items/$itemId" `
            -Method DELETE `
            -SkipCertificateCheck

        Write-Host "✓ File deleted successfully" -ForegroundColor Green
        return $true
    }
    catch {
        Write-Host "✗ Failed: $($_.Exception.Message)" -ForegroundColor Red
        return $false
    }
}

# Run all tests
Write-Host "`n=== SPE API Test Suite ===" -ForegroundColor Yellow
Write-Host ""

# Test 1: Create Container
$containerId = Test-CreateContainer
if (-not $containerId) { exit 1 }

Start-Sleep -Seconds 2

# Test 2: List Containers
$containers = Test-ListContainers
if (-not $containers) { exit 1 }

Start-Sleep -Seconds 2

# Test 3: Get Container Drive
$driveId = Test-GetContainerDrive -containerId $containerId
if (-not $driveId) { exit 1 }

Start-Sleep -Seconds 2

# Test 4: Upload Small File
$itemId = Test-UploadSmallFile -driveId $driveId -fileName "test-$(Get-Date -Format 'HHmmss').txt"
if (-not $itemId) { exit 1 }

Start-Sleep -Seconds 2

# Test 5: List Drive Children
$items = Test-ListDriveChildren -driveId $driveId
if (-not $items) { exit 1 }

Start-Sleep -Seconds 2

# Test 6: Get File Metadata
$metadata = Test-GetFileMetadata -driveId $driveId -itemId $itemId
if (-not $metadata) { exit 1 }

Start-Sleep -Seconds 2

# Test 7: Download File
$downloaded = Test-DownloadFile -driveId $driveId -itemId $itemId
if (-not $downloaded) { exit 1 }

Start-Sleep -Seconds 2

# Test 8: Delete File
$deleted = Test-DeleteFile -driveId $driveId -itemId $itemId
if (-not $deleted) { exit 1 }

Write-Host "`n=== All Tests Passed! ===" -ForegroundColor Green
```

**Usage:**
```powershell
# Update the script with your ContainerTypeId
# Run from PowerShell:
cd C:\code_files\spaarke
.\Test-SpeApis.ps1
```

### Method 2: REST Client Testing (VS Code Extension)

Create a `.http` file for REST Client extension:

```http
### Task-2.5-API-Tests.http
@baseUrl = https://localhost:7123
@containerTypeId = YOUR-CONTAINER-TYPE-GUID

### 1. Create Container
POST {{baseUrl}}/api/containers
Content-Type: application/json

{
  "containerTypeId": "{{containerTypeId}}",
  "displayName": "Test Container",
  "description": "Created via REST Client"
}

### 2. List Containers
GET {{baseUrl}}/api/containers?containerTypeId={{containerTypeId}}

### 3. Get Container Drive
# Replace with actual containerId from step 1
GET {{baseUrl}}/api/containers/YOUR_CONTAINER_ID/drive

### 4. List Drive Children
# Replace with actual driveId from step 3
GET {{baseUrl}}/api/drives/YOUR_DRIVE_ID/children

### 5. Get File Metadata
# Replace with actual driveId and itemId
GET {{baseUrl}}/api/drives/YOUR_DRIVE_ID/items/YOUR_ITEM_ID

### 6. Download File
# Replace with actual driveId and itemId
GET {{baseUrl}}/api/drives/YOUR_DRIVE_ID/items/YOUR_ITEM_ID/content

### 7. Delete File
# Replace with actual driveId and itemId
DELETE {{baseUrl}}/api/drives/YOUR_DRIVE_ID/items/YOUR_ITEM_ID
```

### Method 3: xUnit Integration Tests

Create integration tests in the test project:

```csharp
// SpeFileStoreIntegrationTests.cs
public class SpeFileStoreIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private readonly IConfiguration _config;
    private readonly Guid _containerTypeId;

    public SpeFileStoreIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
        _config = factory.Services.GetRequiredService<IConfiguration>();
        _containerTypeId = Guid.Parse(_config["SharePointEmbedded:ContainerTypeId"]!);
    }

    [Fact]
    public async Task CreateContainer_ReturnsSuccess()
    {
        // Arrange
        var request = new CreateContainerRequest
        {
            ContainerTypeId = _containerTypeId,
            DisplayName = $"Test Container {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}",
            Description = "Integration test container"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/containers", request);

        // Assert
        response.EnsureSuccessStatusCode();
        var container = await response.Content.ReadFromJsonAsync<ContainerDto>();
        Assert.NotNull(container);
        Assert.NotEmpty(container.Id);
    }

    [Fact]
    public async Task ListContainers_ReturnsContainers()
    {
        // Act
        var response = await _client.GetAsync($"/api/containers?containerTypeId={_containerTypeId}");

        // Assert
        response.EnsureSuccessStatusCode();
        var containers = await response.Content.ReadFromJsonAsync<List<ContainerDto>>();
        Assert.NotNull(containers);
    }

    [Fact]
    public async Task UploadAndDownloadFile_RoundTrip()
    {
        // Arrange - Create container first
        var createResponse = await _client.PostAsJsonAsync("/api/containers", new CreateContainerRequest
        {
            ContainerTypeId = _containerTypeId,
            DisplayName = $"Test {DateTime.UtcNow:HHmmss}"
        });
        var container = await createResponse.Content.ReadFromJsonAsync<ContainerDto>();

        var driveResponse = await _client.GetAsync($"/api/containers/{container!.Id}/drive");
        var drive = await driveResponse.Content.ReadFromJsonAsync<ContainerDto>();

        // Upload file
        var content = "Test file content"u8.ToArray();
        var uploadResponse = await _client.PutAsync(
            $"/api/drives/{drive!.Id}/items/root:/test.txt",
            new ByteArrayContent(content));
        var uploadedFile = await uploadResponse.Content.ReadFromJsonAsync<FileHandleDto>();

        // Download file
        var downloadResponse = await _client.GetAsync(
            $"/api/drives/{drive.Id}/items/{uploadedFile!.Id}/content");
        var downloadedContent = await downloadResponse.Content.ReadAsByteArrayAsync();

        // Assert
        Assert.Equal(content, downloadedContent);
    }
}
```

## Manual Testing Checklist

### Phase 1: Container Operations
- [ ] Start the API: `dotnet run --project src/api/Spe.Bff.Api`
- [ ] Create a new container via POST /api/containers
- [ ] Verify container appears in Azure Portal (SharePoint Embedded admin)
- [ ] List containers via GET /api/containers
- [ ] Get container drive via GET /api/containers/{id}/drive

### Phase 2: File Operations
- [ ] Upload small file (<4MB) to container
- [ ] List drive children to see uploaded file
- [ ] Get file metadata via GET /api/drives/{driveId}/items/{itemId}
- [ ] Download file via GET /api/drives/{driveId}/items/{itemId}/content
- [ ] Verify downloaded content matches uploaded content
- [ ] Delete file via DELETE /api/drives/{driveId}/items/{itemId}
- [ ] Verify file no longer appears in listing

### Phase 3: Large File Upload
- [ ] Create upload session for file >4MB
- [ ] Upload file in chunks using session URL
- [ ] Verify file appears in container after complete upload

### Phase 4: Error Handling
- [ ] Test with invalid container type ID (expect 400/404)
- [ ] Test with non-existent container ID (expect 404)
- [ ] Test with non-existent file ID (expect 404)
- [ ] Test upload without permissions (expect 403)
- [ ] Test rate limiting behavior (many rapid requests)

## Troubleshooting

### Issue: "Authentication required" errors
**Solution:** Temporarily disable auth in Program.cs for local testing:
```csharp
// Comment out authorization requirements
// .RequireAuthorization("canmanagecontainers");
```

### Issue: "Container type not found"
**Solution:** Verify your Container Type is properly registered:
```powershell
# List your SPE container types
$token = (Get-AzAccessToken -ResourceUrl "https://graph.microsoft.com").Token
Invoke-RestMethod -Uri "https://graph.microsoft.com/v1.0/storage/fileStorage/containers" `
    -Headers @{Authorization = "Bearer $token"}
```

### Issue: "Drive not found for container"
**Solution:** Wait a few seconds after container creation for drive provisioning:
```powershell
Start-Sleep -Seconds 5  # Add delay between create and get drive
```

### Issue: Build errors about ServiceException
**Solution:** Ensure you're using Graph SDK v5 properties:
- Use `ex.ResponseStatusCode` (not `ex.StatusCode`)
- Cast to int: `(int)HttpStatusCode.NotFound`

## Success Criteria

✅ **All tests pass when:**
1. Container creation returns 201 with valid container ID
2. Container appears in list operations
3. Files can be uploaded, listed, downloaded, and deleted
4. Error responses include proper status codes and problem details
5. Telemetry logs show all operations with trace IDs
6. No unhandled exceptions in API logs

## Next Steps After Testing

Once all tests pass:
1. Commit changes with message: "feat: Implement Task 2.5 SPE Container & File APIs"
2. Update Sprint 2 README with Task 2.5 completion status
3. Proceed to Task 2.1 (Thin Plugin) or Task 3.1 (Model-Driven App)
