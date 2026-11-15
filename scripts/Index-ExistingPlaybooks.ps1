<#
.SYNOPSIS
    Indexes all existing playbooks into the playbook-embeddings AI Search index.

.DESCRIPTION
    Queries Dataverse for all active sprk_analysisplaybook records, generates embeddings
    via Azure OpenAI (text-embedding-3-large), and upserts documents into the
    playbook-embeddings AI Search index.

    This script bypasses the BFF API and directly calls Azure OpenAI and AI Search APIs,
    making it suitable for initial seeding or re-indexing without needing BFF auth tokens.

    Prerequisites:
    - Azure CLI installed and authenticated (az login)
    - The playbook-embeddings AI Search index must exist (run Create-PlaybookEmbeddingsIndex.ps1 first)
    - Azure OpenAI text-embedding-3-large deployment must be available

.PARAMETER DataverseUrl
    Dataverse environment URL. Default: https://spaarkedev1.crm.dynamics.com

.PARAMETER SearchServiceName
    Azure AI Search service name. Default: spaarke-search-dev

.PARAMETER OpenAiEndpoint
    Azure OpenAI endpoint URL. Default: https://spaarke-openai-dev.openai.azure.com/

.PARAMETER EmbeddingDeployment
    Azure OpenAI embedding deployment name. Default: text-embedding-3-large

.PARAMETER DryRun
    List playbooks that would be indexed without actually indexing.

.EXAMPLE
    # Dry run - list playbooks
    .\Index-ExistingPlaybooks.ps1 -DryRun

.EXAMPLE
    # Index all playbooks
    .\Index-ExistingPlaybooks.ps1

.NOTES
    Project: AI Analysis Workspace + SprkChat Integration R1
    Replicates the logic in PlaybookEmbeddingService.cs and PlaybookIndexingService.cs
    for one-time bulk indexing from script.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$DataverseUrl = "https://spaarkedev1.crm.dynamics.com",

    [Parameter(Mandatory = $false)]
    [string]$SearchServiceName = "spaarke-search-dev",

    [Parameter(Mandatory = $false)]
    [string]$OpenAiEndpoint = "https://spaarke-openai-dev.openai.azure.com/",

    [Parameter(Mandatory = $false)]
    [string]$EmbeddingDeployment = "text-embedding-3-large",

    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$env:Path = "C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin;" + $env:Path

$IndexName = "playbook-embeddings"
$SearchApiVersion = "2024-07-01"
$OpenAiApiVersion = "2024-06-01"

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host ' Index Existing Playbooks into playbook-embeddings' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host "  Dataverse       : $DataverseUrl"
Write-Host "  AI Search       : $SearchServiceName"
Write-Host "  OpenAI Endpoint : $OpenAiEndpoint"
Write-Host "  Embedding Model : $EmbeddingDeployment"
if ($DryRun) {
    Write-Host "  Mode            : DRY RUN" -ForegroundColor Yellow
} else {
    Write-Host "  Mode            : LIVE" -ForegroundColor Green
}
Write-Host ''

# ============================================================================
# Step 1: Acquire tokens
# ============================================================================
Write-Host '[1/5] Acquiring tokens...' -ForegroundColor Yellow

# Dataverse token
$dvToken = az account get-access-token --resource $DataverseUrl --query 'accessToken' -o tsv 2>$null
if (-not $dvToken) {
    Write-Error "Failed to acquire Dataverse token. Run 'az login' first."
    exit 1
}
Write-Host '       Dataverse token acquired.' -ForegroundColor Green

if (-not $DryRun) {
    # Azure OpenAI API key from Key Vault
    $openAiKey = az keyvault secret show --vault-name spaarke-spekvcert --name ai-openai-key --query value -o tsv 2>$null
    if (-not $openAiKey) {
        Write-Error "Failed to get Azure OpenAI API key from Key Vault. Ensure you have access to spaarke-spekvcert."
        exit 1
    }
    Write-Host '       OpenAI API key acquired from Key Vault.' -ForegroundColor Green

    # AI Search admin key (admin key required for index operations)
    $searchAdminKey = az search admin-key show --resource-group spe-infrastructure-westus2 --service-name $SearchServiceName --query primaryKey -o tsv 2>$null
    if (-not $searchAdminKey) {
        Write-Error "Failed to get AI Search admin key."
        exit 1
    }
    Write-Host '       AI Search admin key acquired.' -ForegroundColor Green
}
Write-Host ''

# ============================================================================
# Step 2: Query all playbook records from Dataverse
# ============================================================================
Write-Host '[2/5] Querying playbook records from Dataverse...' -ForegroundColor Yellow

$dvHeaders = @{
    'Authorization'    = "Bearer $dvToken"
    'Accept'           = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
}

# Query all active playbooks with trigger metadata fields
$selectFields = "sprk_analysisplaybookid,sprk_name,sprk_description,sprk_triggerphrases,sprk_recordtype,sprk_entitytype,sprk_tags,statecode"

# Try with trigger metadata fields first; fall back to basic fields if columns don't exist
$queryUrl = "$DataverseUrl/api/data/v9.2/sprk_analysisplaybooks?`$select=$selectFields&`$filter=statecode eq 0&`$orderby=sprk_name"
$hasTriggerFields = $true

try {
    $response = Invoke-RestMethod -Uri $queryUrl -Headers $dvHeaders -Method Get
    $playbooks = $response.value
} catch {
    # Trigger metadata fields may not exist yet - fall back to basic fields
    Write-Host '       Trigger metadata fields not found, using basic fields only.' -ForegroundColor Yellow
    $hasTriggerFields = $false
    $queryUrl = "$DataverseUrl/api/data/v9.2/sprk_analysisplaybooks?`$select=sprk_analysisplaybookid,sprk_name,sprk_description,statecode&`$filter=statecode eq 0&`$orderby=sprk_name"
    try {
        $response = Invoke-RestMethod -Uri $queryUrl -Headers $dvHeaders -Method Get
        $playbooks = $response.value
    } catch {
        $errMsg = $_.ErrorDetails.Message
        if (-not $errMsg) { $errMsg = $_.Exception.Message }
        Write-Error "Failed to query playbooks: $errMsg"
        exit 1
    }
}

$count = ($playbooks | Measure-Object).Count
Write-Host "       Found $count active playbook(s)." -ForegroundColor Green
Write-Host ''

if ($count -eq 0) {
    Write-Host 'No playbooks to index. Exiting.' -ForegroundColor Yellow
    exit 0
}

# List playbooks
Write-Host '  --- Playbooks ---' -ForegroundColor Cyan
$playbooks | ForEach-Object {
    $id = $_.sprk_analysisplaybookid
    $name = $_.sprk_name
    Write-Host "    $name ($id)"
}
Write-Host ''

if ($DryRun) {
    Write-Host "DRY RUN complete. $count playbook(s) would be indexed." -ForegroundColor Yellow
    exit 0
}

# ============================================================================
# Step 3: Helper function to generate embeddings
# ============================================================================

function Get-Embedding {
    param(
        [string]$Text,
        [string]$Endpoint,
        [string]$Deployment,
        [string]$ApiKey,
        [string]$ApiVersion
    )

    $url = "${Endpoint}openai/deployments/${Deployment}/embeddings?api-version=${ApiVersion}"
    $body = @{ input = $Text } | ConvertTo-Json -Compress

    $headers = @{
        'api-key'      = $ApiKey
        'Content-Type' = 'application/json'
    }

    $resp = Invoke-RestMethod -Uri $url -Method Post -Headers $headers -Body ([System.Text.Encoding]::UTF8.GetBytes($body))
    return $resp.data[0].embedding
}

# ============================================================================
# Step 4: Build embedding content for each playbook (mirrors PlaybookEmbeddingService.ComposeContentText)
# ============================================================================
Write-Host '[3/5] Generating embeddings and indexing...' -ForegroundColor Yellow

$searchEndpoint = "https://$SearchServiceName.search.windows.net"
$searchHeaders = @{
    'api-key'      = $searchAdminKey
    'Content-Type' = 'application/json'
}

$succeeded = 0
$failed = 0

foreach ($pb in $playbooks) {
    $id = $pb.sprk_analysisplaybookid
    $name = $pb.sprk_name
    $description = if ($pb.sprk_description) { $pb.sprk_description } else { '' }

    # Build content text (same logic as PlaybookEmbeddingService.ComposeContentText)
    $contentParts = @($name, $description)

    $triggerPhrases = @()
    $recordType = ''
    $entityType = ''
    $tags = @()

    if ($hasTriggerFields) {
        if ($pb.sprk_triggerphrases) {
            $triggerPhrases = $pb.sprk_triggerphrases -split "`n" | Where-Object { $_.Trim() }
            $contentParts += ($triggerPhrases -join " | ")
        }
        $recordType = if ($pb.sprk_recordtype) { $pb.sprk_recordtype } else { '' }
        $entityType = if ($pb.sprk_entitytype) { $pb.sprk_entitytype } else { '' }
        if ($pb.sprk_tags) {
            $tags = $pb.sprk_tags -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ }
            $contentParts += ($tags -join ", ")
        }
    }

    # Also derive tags from recordType/entityType (matches PlaybookIndexingService.ParseTags)
    $derivedTags = @()
    if ($recordType) { $derivedTags += $recordType }
    if ($entityType) { $derivedTags += $entityType }
    $allTags = ($derivedTags + $tags) | Select-Object -Unique

    $contentText = ($contentParts | Where-Object { $_ }) -join "`n"
    # Truncate to 30K chars (matches PlaybookEmbeddingService.MaxContentLength)
    if ($contentText.Length -gt 30000) { $contentText = $contentText.Substring(0, 30000) }

    Write-Host "    Processing: $name..." -NoNewline

    try {
        # Step 4a: Generate embedding
        $embedding = Get-Embedding -Text $contentText -Endpoint $OpenAiEndpoint `
            -Deployment $EmbeddingDeployment -ApiKey $openAiKey -ApiVersion $OpenAiApiVersion

        # Step 4b: Build search document as JSON manually to handle the large embedding array
        # ConvertTo-Json can truncate large arrays, so we build the vector portion explicitly
        $triggerPhrasesJson = if ($triggerPhrases.Count -gt 0) { ($triggerPhrases | ConvertTo-Json -Compress) } else { '[]' }
        $tagsJson = if ($allTags.Count -gt 0) { ($allTags | ConvertTo-Json -Compress) } else { '[]' }
        # Embedding array: join float values directly to avoid ConvertTo-Json truncation
        $vectorJson = '[' + ($embedding -join ',') + ']'

        $docJson = @"
{"value":[{"@search.action":"mergeOrUpload","id":"$id","playbookId":"$id","playbookName":$(($name | ConvertTo-Json)),"description":$(($description | ConvertTo-Json)),"triggerPhrases":$triggerPhrasesJson,"recordType":"$recordType","entityType":"$entityType","tags":$tagsJson,"contentVector3072":$vectorJson}]}
"@

        # Step 4c: Upsert into AI Search index (MergeOrUpload)
        $indexUrl = "$searchEndpoint/indexes/$IndexName/docs/index?api-version=$SearchApiVersion"
        $indexResp = Invoke-RestMethod -Uri $indexUrl -Method Post -Headers $searchHeaders `
            -Body ([System.Text.Encoding]::UTF8.GetBytes($docJson))

        $result = $indexResp.value[0]
        if ($result.status -eq $true -or $result.statusCode -eq 200 -or $result.statusCode -eq 201) {
            Write-Host " OK (content: $($contentText.Length) chars)" -ForegroundColor Green
            $succeeded++
        } else {
            Write-Host " WARN: status=$($result.statusCode) $($result.errorMessage)" -ForegroundColor Yellow
            $succeeded++
        }
    } catch {
        $errMsg = $_.Exception.Message
        $errDetail = $_.ErrorDetails.Message
        if ($errDetail) { $errMsg = "$errMsg | $errDetail" }
        Write-Host " FAILED: $errMsg" -ForegroundColor Red
        $failed++
    }

    # Small delay between API calls
    Start-Sleep -Milliseconds 500
}

# ============================================================================
# Step 5: Verify index document count
# ============================================================================
Write-Host ''
Write-Host '[4/5] Verifying index document count...' -ForegroundColor Yellow

# Wait a moment for index to update
Start-Sleep -Seconds 2

$countUrl = "$searchEndpoint/indexes/$IndexName/docs/`$count?api-version=$SearchApiVersion"
try {
    $docCount = Invoke-RestMethod -Uri $countUrl -Method Get -Headers $searchHeaders
    Write-Host "       Index document count: $docCount" -ForegroundColor Green
} catch {
    Write-Host "       Could not verify document count: $($_.Exception.Message)" -ForegroundColor Yellow
}

# ============================================================================
# Summary
# ============================================================================
Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host ' Indexing Summary' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host "  Total playbooks : $count"
Write-Host "  Indexed         : $succeeded" -ForegroundColor Green
Write-Host "  Failed          : $failed" -ForegroundColor $(if ($failed -gt 0) { 'Red' } else { 'Green' })
Write-Host ''

if ($failed -gt 0) {
    Write-Host 'Some playbooks failed. Common issues:' -ForegroundColor Yellow
    Write-Host '  - OpenAI 429: Rate limit exceeded (wait and retry)'
    Write-Host '  - Search 403: Admin key invalid or expired'
    Write-Host '  - Embedding error: Content too long or invalid characters'
} else {
    Write-Host 'All playbooks indexed successfully.' -ForegroundColor Green
}
Write-Host ''
