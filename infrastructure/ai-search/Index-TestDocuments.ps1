<#
.SYNOPSIS
    Indexes test documents for E2E testing of the visualization and RAG services.

.DESCRIPTION
    Creates a set of test documents with 3072-dim vectors for:
    - Regular documents (with Dataverse record)
    - Orphan files (SPE file only, no Dataverse record)
    - Various file types (pdf, docx, xlsx, msg)

.PARAMETER SearchServiceName
    Name of the Azure AI Search service.

.PARAMETER IndexName
    Name of the index. Defaults to 'spaarke-knowledge-index-v2'.

.PARAMETER TenantId
    Tenant ID for the test documents.

.EXAMPLE
    .\Index-TestDocuments.ps1 -SearchServiceName "spaarke-search-dev" -TenantId "test-tenant-001"
#>

param(
    [Parameter(Mandatory = $false)]
    [string]$SearchServiceName = "spaarke-search-dev",

    [Parameter(Mandatory = $false)]
    [string]$IndexName = "spaarke-knowledge-index-v2",

    [Parameter(Mandatory = $false)]
    [string]$TenantId = "test-tenant-e2e",

    [Parameter(Mandatory = $false)]
    [string]$ResourceGroup = "spe-infrastructure-westus2"
)

$ErrorActionPreference = "Stop"

Write-Host "=== Index Test Documents for E2E Testing ===" -ForegroundColor Cyan
Write-Host "Search Service: $SearchServiceName"
Write-Host "Index: $IndexName"
Write-Host "Tenant: $TenantId"
Write-Host ""

# Get admin key
Write-Host "Retrieving admin key..."
$adminKey = az search admin-key show `
    --resource-group $ResourceGroup `
    --service-name $SearchServiceName `
    --query "primaryKey" -o tsv

if (-not $adminKey) {
    Write-Error "Failed to retrieve admin key. Make sure you're logged in to Azure CLI."
    exit 1
}

$searchEndpoint = "https://$SearchServiceName.search.windows.net"
$headers = @{
    "Content-Type" = "application/json"
    "api-key" = $adminKey
}

# Generate normalized random vector
function Get-RandomNormalizedVector($dimensions) {
    $vector = @()
    $random = New-Object System.Random
    for ($i = 0; $i -lt $dimensions; $i++) {
        $vector += ($random.NextDouble() * 2 - 1)
    }
    $magnitude = [Math]::Sqrt(($vector | ForEach-Object { $_ * $_ } | Measure-Object -Sum).Sum)
    if ($magnitude -gt 0) {
        $vector = $vector | ForEach-Object { $_ / $magnitude }
    }
    return $vector
}

# Define test documents
$testDocuments = @(
    # Regular documents (with Dataverse record)
    @{
        id = "e2e-doc-001-chunk-0"
        tenantId = $TenantId
        deploymentModel = "Shared"
        documentId = "dv-record-001"
        speFileId = "spe-file-001"
        fileName = "Contract-ABC-Corp-2024.pdf"
        fileType = "pdf"
        documentType = "Contract"
        knowledgeSourceId = "ks-001"
        knowledgeSourceName = "Legal Documents"
        chunkIndex = 0
        chunkCount = 2
        content = "This Master Services Agreement between ABC Corporation and Spaarke Inc. establishes terms for cloud document management services. The agreement includes provisions for data security, service level agreements, and intellectual property rights."
        tags = @("contract", "legal", "abc-corp")
    },
    @{
        id = "e2e-doc-001-chunk-1"
        tenantId = $TenantId
        deploymentModel = "Shared"
        documentId = "dv-record-001"
        speFileId = "spe-file-001"
        fileName = "Contract-ABC-Corp-2024.pdf"
        fileType = "pdf"
        documentType = "Contract"
        knowledgeSourceId = "ks-001"
        knowledgeSourceName = "Legal Documents"
        chunkIndex = 1
        chunkCount = 2
        content = "Payment terms require net 30 invoicing with automatic renewal clauses. The contract is governed by the laws of the State of Washington and includes mandatory arbitration for dispute resolution."
        tags = @("contract", "legal", "abc-corp")
    },
    @{
        id = "e2e-doc-002-chunk-0"
        tenantId = $TenantId
        deploymentModel = "Shared"
        documentId = "dv-record-002"
        speFileId = "spe-file-002"
        fileName = "Project-Proposal-Q1-2024.docx"
        fileType = "docx"
        documentType = "Proposal"
        knowledgeSourceId = "ks-002"
        knowledgeSourceName = "Project Documents"
        chunkIndex = 0
        chunkCount = 1
        content = "Project proposal for implementing AI-powered document analysis in the enterprise document management system. The solution leverages Azure OpenAI and Azure AI Search for semantic understanding and retrieval."
        tags = @("proposal", "project", "ai")
    },
    @{
        id = "e2e-doc-003-chunk-0"
        tenantId = $TenantId
        deploymentModel = "Shared"
        documentId = "dv-record-003"
        speFileId = "spe-file-003"
        fileName = "Financial-Report-2023.xlsx"
        fileType = "xlsx"
        documentType = "Report"
        knowledgeSourceId = "ks-003"
        knowledgeSourceName = "Financial Reports"
        chunkIndex = 0
        chunkCount = 1
        content = "Annual financial report showing revenue of $2.5M with 15% year-over-year growth. Operating expenses were $1.8M resulting in net income of $700K. The company maintains strong cash reserves of $500K."
        tags = @("financial", "report", "2023")
    },
    # Orphan files (SPE file only, no Dataverse record)
    @{
        id = "e2e-orphan-001-chunk-0"
        tenantId = $TenantId
        deploymentModel = "Shared"
        documentId = $null  # No Dataverse record - orphan file
        speFileId = "spe-orphan-001"
        fileName = "meeting-notes-archive.msg"
        fileType = "msg"
        documentType = $null
        knowledgeSourceId = "ks-001"
        knowledgeSourceName = "Legal Documents"
        chunkIndex = 0
        chunkCount = 1
        content = "Archived meeting notes from client call discussing contract terms and deliverables. Action items include reviewing SOW and sending updated timeline."
        tags = @("meeting", "archive", "orphan")
    },
    @{
        id = "e2e-orphan-002-chunk-0"
        tenantId = $TenantId
        deploymentModel = "Shared"
        documentId = $null  # No Dataverse record - orphan file
        speFileId = "spe-orphan-002"
        fileName = "legacy-data-backup.zip"
        fileType = "zip"
        documentType = $null
        knowledgeSourceId = "ks-002"
        knowledgeSourceName = "Project Documents"
        chunkIndex = 0
        chunkCount = 1
        content = "Legacy system backup containing historical project documentation and archived client correspondence from 2020-2022 migration project."
        tags = @("archive", "legacy", "orphan")
    }
)

Write-Host "Generating 3072-dim vectors for $($testDocuments.Count) documents..."

# Add vectors and timestamps to each document
$documentsToIndex = @()
foreach ($doc in $testDocuments) {
    $docWithVectors = $doc.Clone()
    $docWithVectors["contentVector3072"] = Get-RandomNormalizedVector 3072
    $docWithVectors["documentVector3072"] = Get-RandomNormalizedVector 3072
    $docWithVectors["contentVector"] = Get-RandomNormalizedVector 1536
    $docWithVectors["documentVector"] = Get-RandomNormalizedVector 1536
    $docWithVectors["createdAt"] = (Get-Date).AddDays(-30).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    $docWithVectors["updatedAt"] = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    $docWithVectors["@search.action"] = "upload"
    $documentsToIndex += $docWithVectors
}

# Index documents
$indexBody = @{ value = $documentsToIndex } | ConvertTo-Json -Depth 10 -Compress
$indexUrl = "$searchEndpoint/indexes/$IndexName/docs/index?api-version=2024-07-01"

Write-Host "Indexing $($testDocuments.Count) documents..."

try {
    $response = Invoke-RestMethod -Uri $indexUrl -Method Post -Headers $headers -Body $indexBody

    $successCount = ($response.value | Where-Object { $_.status -eq $true -or $_.statusCode -eq 200 -or $_.statusCode -eq 201 }).Count
    $failCount = $response.value.Count - $successCount

    Write-Host ""
    Write-Host "Indexing complete!" -ForegroundColor Green
    Write-Host "  Success: $successCount documents"
    if ($failCount -gt 0) {
        Write-Host "  Failed: $failCount documents" -ForegroundColor Yellow
    }
}
catch {
    Write-Error "Failed to index documents: $_"
    exit 1
}

# Verify document count
Write-Host ""
Write-Host "Verifying index..."
$countUrl = "$searchEndpoint/indexes/$IndexName/docs/`$count?api-version=2024-07-01"
$count = Invoke-RestMethod -Uri $countUrl -Headers $headers -Method GET
Write-Host "Total documents in index: $count" -ForegroundColor Green

# Summary
Write-Host ""
Write-Host "=== Test Documents Indexed ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Regular documents (with Dataverse record):"
Write-Host "  - Contract-ABC-Corp-2024.pdf (2 chunks)"
Write-Host "  - Project-Proposal-Q1-2024.docx (1 chunk)"
Write-Host "  - Financial-Report-2023.xlsx (1 chunk)"
Write-Host ""
Write-Host "Orphan files (no Dataverse record):"
Write-Host "  - meeting-notes-archive.msg (1 chunk)"
Write-Host "  - legacy-data-backup.zip (1 chunk)"
Write-Host ""
Write-Host "Ready for E2E testing!" -ForegroundColor Green
