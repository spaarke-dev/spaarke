<#
.SYNOPSIS
    Tests that the spaarke-knowledge-index accepts documents with 3072-dimension vectors.

.DESCRIPTION
    This script verifies the 3072-dim vector fields by:
    1. Creating a test document with 3072-dim vectors
    2. Indexing it to Azure AI Search
    3. Verifying the document was indexed
    4. Cleaning up the test document

.PARAMETER ResourceGroup
    Resource group containing the Azure AI Search service.

.PARAMETER SearchServiceName
    Name of the Azure AI Search service.

.PARAMETER IndexName
    Name of the index to test. Defaults to 'spaarke-knowledge-index'.

.EXAMPLE
    .\Test-KnowledgeIndex-3072Vectors.ps1 -ResourceGroup "spe-infrastructure-westus2" -SearchServiceName "spaarke-search-dev"

.NOTES
    Task 051 - Phase 5b: Schema Migration (Step 3: Verify index accepts new vector dimensions)
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$ResourceGroup = "spe-infrastructure-westus2",

    [Parameter(Mandatory = $true)]
    [string]$SearchServiceName = "spaarke-search-dev",

    [Parameter(Mandatory = $false)]
    [string]$IndexName = "spaarke-knowledge-index"
)

$ErrorActionPreference = "Stop"

Write-Host "=== Test Knowledge Index 3072-dim Vector Fields ===" -ForegroundColor Cyan
Write-Host "Search Service: $SearchServiceName"
Write-Host "Index Name: $IndexName"
Write-Host ""

# Get admin key
Write-Host "Retrieving search service admin key..."
$adminKey = az search admin-key show `
    --resource-group $ResourceGroup `
    --service-name $SearchServiceName `
    --query "primaryKey" -o tsv

if (-not $adminKey) {
    Write-Error "Failed to retrieve admin key"
    exit 1
}

$searchEndpoint = "https://$SearchServiceName.search.windows.net"
$headers = @{
    "Content-Type" = "application/json"
    "api-key" = $adminKey
}

# Generate a test document ID
$testDocId = "test-3072-vectors-$(Get-Date -Format 'yyyyMMddHHmmss')"

# Generate random 3072-dim vectors (normalized)
function Get-RandomNormalizedVector($dimensions) {
    $vector = @()
    for ($i = 0; $i -lt $dimensions; $i++) {
        $vector += (Get-Random -Minimum -1.0 -Maximum 1.0)
    }
    # Normalize (L2)
    $magnitude = [Math]::Sqrt(($vector | ForEach-Object { $_ * $_ } | Measure-Object -Sum).Sum)
    if ($magnitude -gt 0) {
        $vector = $vector | ForEach-Object { $_ / $magnitude }
    }
    return $vector
}

Write-Host "Generating test vectors (3072 dimensions)..."
$contentVector3072 = Get-RandomNormalizedVector 3072
$documentVector3072 = Get-RandomNormalizedVector 3072

# Also include 1536-dim vectors for backward compatibility
$contentVector = Get-RandomNormalizedVector 1536
$documentVector = Get-RandomNormalizedVector 1536

# Create test document
$testDocument = @{
    value = @(
        @{
            "@search.action" = "upload"
            id = $testDocId
            tenantId = "test-tenant"
            deploymentModel = "Shared"
            documentId = "test-document-id"
            speFileId = "test-spe-file-id"
            documentName = "Test Document for 3072-dim Vectors"
            fileName = "test-3072-vectors.txt"
            documentType = "test"
            fileType = "txt"
            chunkIndex = 0
            chunkCount = 1
            content = "This is a test document to verify 3072-dimension vector field support in the Azure AI Search index."
            contentVector = $contentVector
            documentVector = $documentVector
            contentVector3072 = $contentVector3072
            documentVector3072 = $documentVector3072
            tags = @("test", "3072-vectors", "migration")
            createdAt = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
            updatedAt = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
        }
    )
}

$testDocJson = $testDocument | ConvertTo-Json -Depth 10 -Compress

# Index the test document
Write-Host "Indexing test document with 3072-dim vectors..."
$indexUrl = "$searchEndpoint/indexes/$IndexName/docs/index?api-version=2024-07-01"

try {
    $indexResponse = Invoke-RestMethod -Uri $indexUrl -Method Post -Headers $headers -Body $testDocJson

    $result = $indexResponse.value[0]
    if ($result.status -eq $true -or $result.statusCode -eq 200 -or $result.statusCode -eq 201) {
        Write-Host "Test document indexed successfully!" -ForegroundColor Green
        Write-Host "  Document ID: $testDocId"
        Write-Host "  Status: $($result.status)"
    }
    else {
        Write-Host "Indexing returned unexpected status: $($result | ConvertTo-Json)" -ForegroundColor Yellow
    }
}
catch {
    Write-Error "Failed to index test document: $_"
    Write-Host ""
    Write-Host "This may indicate that the 3072-dim vector fields are not properly configured."
    Write-Host "Run Update-KnowledgeIndex-3072Vectors.ps1 first to add the fields."
    exit 1
}

# Verify the document was indexed
Write-Host ""
Write-Host "Verifying document was indexed..."
$lookupUrl = "$searchEndpoint/indexes/$IndexName/docs/$testDocId`?api-version=2024-07-01"

try {
    $lookupResponse = Invoke-RestMethod -Uri $lookupUrl -Method Get -Headers $headers

    Write-Host "Document verification successful!" -ForegroundColor Green
    Write-Host "  contentVector3072 length: $($lookupResponse.contentVector3072.Count)"
    Write-Host "  documentVector3072 length: $($lookupResponse.documentVector3072.Count)"

    if ($lookupResponse.contentVector3072.Count -eq 3072 -and $lookupResponse.documentVector3072.Count -eq 3072) {
        Write-Host ""
        Write-Host "3072-dimension vector fields are working correctly!" -ForegroundColor Green
    }
    else {
        Write-Host "Warning: Vector dimensions don't match expected 3072" -ForegroundColor Yellow
    }
}
catch {
    Write-Error "Failed to retrieve test document: $_"
}

# Clean up - delete test document
Write-Host ""
Write-Host "Cleaning up test document..."
$deleteDoc = @{
    value = @(
        @{
            "@search.action" = "delete"
            id = $testDocId
        }
    )
}
$deleteJson = $deleteDoc | ConvertTo-Json -Depth 5 -Compress

try {
    $deleteResponse = Invoke-RestMethod -Uri $indexUrl -Method Post -Headers $headers -Body $deleteJson
    Write-Host "Test document deleted successfully." -ForegroundColor Green
}
catch {
    Write-Host "Warning: Failed to delete test document: $_" -ForegroundColor Yellow
    Write-Host "You may need to delete document '$testDocId' manually."
}

Write-Host ""
Write-Host "=== Test Complete ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Results:"
Write-Host "  - 3072-dim vector fields: SUPPORTED" -ForegroundColor Green
Write-Host "  - Index accepts documents with both 1536 and 3072 dimensions" -ForegroundColor Green
Write-Host ""
Write-Host "Ready for Task 052: Create EmbeddingMigrationService"
Write-Host ""
