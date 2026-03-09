# Sync-RecordsToIndex.ps1
# Syncs Dataverse records (Matters, Projects, Invoices) to the spaarke-records-index AI Search index.
#
# Bypasses BFF API auth by reading directly from Dataverse and writing to AI Search.
# Generates embeddings via Azure OpenAI for vector search support.
#
# Usage:
#   .\Sync-RecordsToIndex.ps1                          # Sync all record types
#   .\Sync-RecordsToIndex.ps1 -RecordTypes matter      # Sync matters only
#   .\Sync-RecordsToIndex.ps1 -RecordTypes matter,project  # Sync matters + projects
#   .\Sync-RecordsToIndex.ps1 -DryRun                  # Preview without indexing
#   .\Sync-RecordsToIndex.ps1 -IncludeEmbeddings       # Generate contentVector embeddings
#
# Prerequisites:
#   - az login (authenticated to Azure)
#   - Azure AI Search admin key access
#   - Dataverse access via current Azure AD session

param(
    [string[]]$RecordTypes = @("matter", "project", "invoice"),
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com",
    [string]$SearchServiceName = "spaarke-search-dev",
    [string]$SearchIndexName = "spaarke-records-index",
    [string]$OpenAiEndpoint = "https://spaarke-openai-dev.openai.azure.com/",
    [string]$EmbeddingModel = "text-embedding-3-large",
    [string]$EmbeddingApiVersion = "2024-06-01",
    [switch]$IncludeEmbeddings,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

# ─────────────────────────────────────────────────────────────────────────────
# Entity configuration — mirrors DataverseIndexSyncService.cs
# ─────────────────────────────────────────────────────────────────────────────

$EntityConfigs = @{
    "matter" = @{
        EntityLogicalName = "sprk_matter"
        EntitySetName     = "sprk_matters"
        IdField           = "sprk_matterid"
        NameField         = "sprk_mattername"
        DescriptionField  = "sprk_matterdescription"
        ReferenceField    = "sprk_matternumber"
        SelectFields      = "sprk_matterid,sprk_mattername,sprk_matterdescription,sprk_matternumber,modifiedon"
    }
    "project" = @{
        EntityLogicalName = "sprk_project"
        EntitySetName     = "sprk_projects"
        IdField           = "sprk_projectid"
        NameField         = "sprk_projectname"
        DescriptionField  = "sprk_projectdescription"
        ReferenceField    = "sprk_projectnumber"
        SelectFields      = "sprk_projectid,sprk_projectname,sprk_projectdescription,sprk_projectnumber,modifiedon"
    }
    "invoice" = @{
        EntityLogicalName = "sprk_invoice"
        EntitySetName     = "sprk_invoices"
        IdField           = "sprk_invoiceid"
        NameField         = "sprk_invoicename"
        DescriptionField  = "sprk_invoicedescription"
        ReferenceField    = "sprk_invoicenumber"
        SelectFields      = "sprk_invoiceid,sprk_invoicename,sprk_invoicedescription,sprk_invoicenumber,modifiedon"
    }
}

# ─────────────────────────────────────────────────────────────────────────────
# Helper functions
# ─────────────────────────────────────────────────────────────────────────────

function Get-DataverseToken {
    param([string]$EnvironmentUrl)
    $token = az account get-access-token --resource $EnvironmentUrl --query accessToken -o tsv 2>$null
    if (-not $token) {
        Write-Error "Failed to get Dataverse access token. Run 'az login' first."
        exit 1
    }
    return $token
}

function Get-DataverseRecords {
    param(
        [string]$EnvironmentUrl,
        [string]$Token,
        [string]$EntitySetName,
        [string]$SelectFields
    )

    $allRecords = @()
    $url = "${EnvironmentUrl}/api/data/v9.2/${EntitySetName}?`$select=${SelectFields}&`$count=true"

    while ($url) {
        $headers = @{
            'Authorization' = "Bearer $Token"
            'Accept'        = 'application/json'
            'Prefer'        = 'odata.include-annotations=OData.Community.Display.V1.FormattedValue,odata.maxpagesize=500'
        }

        $response = Invoke-RestMethod -Uri $url -Headers $headers -Method Get
        $allRecords += $response.value

        # Handle paging
        $url = $response.'@odata.nextLink'
    }

    return $allRecords
}

function Get-Embedding {
    param(
        [string]$Text,
        [string]$Endpoint,
        [string]$Model,
        [string]$ApiKey,
        [string]$ApiVersion
    )

    $url = "${Endpoint}openai/deployments/${Model}/embeddings?api-version=${ApiVersion}"
    $body = @{
        input = $Text
    } | ConvertTo-Json

    $headers = @{
        'api-key'      = $ApiKey
        'Content-Type' = 'application/json'
    }

    $response = Invoke-RestMethod -Uri $url -Headers $headers -Method Post -Body $body
    return $response.data[0].embedding
}

function Transform-ToIndexDocument {
    param(
        [PSObject]$Record,
        [hashtable]$Config
    )

    $entityName = $Config.EntityLogicalName
    $recordId = $Record.($Config.IdField)
    $name = $Record.($Config.NameField)
    $description = $Record.($Config.DescriptionField)
    $reference = $Record.($Config.ReferenceField)
    $modifiedOn = $Record.modifiedon

    # Build index document matching spaarke-records-index schema
    $doc = @{
        "@search.action"    = "mergeOrUpload"
        id                  = "${entityName}_${recordId}"
        recordType          = $entityName
        dataverseEntityName = $entityName
        dataverseRecordId   = $recordId
        recordName          = if ($name) { $name } else { "" }
        recordDescription   = if ($description) { $description } else { "" }
        lastModified        = $modifiedOn
    }

    # Reference numbers
    if ($reference) {
        $doc.referenceNumbers = @($reference)
    } else {
        $doc.referenceNumbers = @()
    }

    # Keywords: combine name + reference for keyword search
    $keywordParts = @()
    if ($name) { $keywordParts += $name }
    if ($reference) { $keywordParts += $reference }
    $doc.keywords = ($keywordParts -join " ")

    # organizations/people left empty for now (TODO: expand lookups)
    $doc.organizations = @()
    $doc.people = @()

    return $doc
}

function Upload-ToSearchIndex {
    param(
        [array]$Documents,
        [string]$SearchServiceName,
        [string]$IndexName,
        [string]$AdminKey
    )

    $batchSize = 1000
    $totalIndexed = 0

    for ($i = 0; $i -lt $Documents.Count; $i += $batchSize) {
        $batch = $Documents[$i..([Math]::Min($i + $batchSize - 1, $Documents.Count - 1))]

        $body = @{
            value = $batch
        } | ConvertTo-Json -Depth 10

        $url = "https://${SearchServiceName}.search.windows.net/indexes/${IndexName}/docs/index?api-version=2024-07-01"
        $headers = @{
            'api-key'      = $AdminKey
            'Content-Type' = 'application/json'
        }

        $response = Invoke-RestMethod -Uri $url -Headers $headers -Method Post -Body $body

        $succeeded = ($response.value | Where-Object { $_.status -eq $true -or $_.statusCode -eq 200 -or $_.statusCode -eq 201 }).Count
        $failed = ($response.value | Where-Object { $_.status -eq $false -or ($_.statusCode -ne 200 -and $_.statusCode -ne 201 -and $null -ne $_.statusCode) })

        if ($failed.Count -gt 0) {
            foreach ($f in $failed | Select-Object -First 3) {
                Write-Warning "  Failed to index $($f.key): $($f.errorMessage)"
            }
        }

        $totalIndexed += $succeeded
    }

    return $totalIndexed
}

# ─────────────────────────────────────────────────────────────────────────────
# Main execution
# ─────────────────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "=== Dataverse Records → AI Search Sync ===" -ForegroundColor Cyan
Write-Host "  Index: $SearchIndexName" -ForegroundColor Gray
Write-Host "  Record types: $($RecordTypes -join ', ')" -ForegroundColor Gray
Write-Host "  Embeddings: $(if ($IncludeEmbeddings) { 'Yes' } else { 'No (use -IncludeEmbeddings to generate)' })" -ForegroundColor Gray
if ($DryRun) {
    Write-Host "  Mode: DRY RUN (no changes)" -ForegroundColor Yellow
}
Write-Host ""

# Step 1: Authenticate to Dataverse
Write-Host "[1/4] Authenticating to Dataverse..." -ForegroundColor Cyan
$dvToken = Get-DataverseToken -EnvironmentUrl $EnvironmentUrl
Write-Host "  Authenticated" -ForegroundColor Green

# Step 2: Fetch records from Dataverse
Write-Host "[2/4] Fetching records from Dataverse..." -ForegroundColor Cyan
$allDocuments = @()
$recordCounts = @{}

foreach ($type in $RecordTypes) {
    $typeLower = $type.ToLower()
    if (-not $EntityConfigs.ContainsKey($typeLower)) {
        Write-Warning "  Unknown record type: $type (valid: matter, project, invoice)"
        continue
    }

    $config = $EntityConfigs[$typeLower]
    Write-Host "  Fetching $($config.EntitySetName)..." -ForegroundColor Gray -NoNewline

    try {
        $records = Get-DataverseRecords `
            -EnvironmentUrl $EnvironmentUrl `
            -Token $dvToken `
            -EntitySetName $config.EntitySetName `
            -SelectFields $config.SelectFields

        Write-Host " $($records.Count) records" -ForegroundColor Green

        foreach ($record in $records) {
            $doc = Transform-ToIndexDocument -Record $record -Config $config
            $allDocuments += $doc
        }

        $recordCounts[$typeLower] = $records.Count
    } catch {
        Write-Warning "  Failed to fetch $($config.EntitySetName): $_"
        $recordCounts[$typeLower] = 0
    }
}

Write-Host "  Total: $($allDocuments.Count) records to index" -ForegroundColor Cyan

if ($allDocuments.Count -eq 0) {
    Write-Host "`nNo records found. Nothing to index." -ForegroundColor Yellow
    exit 0
}

# Step 3: Generate embeddings (optional)
if ($IncludeEmbeddings) {
    Write-Host "[3/4] Generating embeddings ($($allDocuments.Count) records)..." -ForegroundColor Cyan

    $openAiKey = az cognitiveservices account keys list `
        --name "spaarke-openai-dev" `
        --resource-group "spe-infrastructure-westus2" `
        --query 'key1' -o tsv

    if (-not $openAiKey) {
        Write-Error "Failed to get Azure OpenAI API key"
        exit 1
    }

    for ($i = 0; $i -lt $allDocuments.Count; $i++) {
        $doc = $allDocuments[$i]
        # Build embedding text from name + description + keywords
        $embeddingText = @($doc.recordName, $doc.recordDescription, $doc.keywords) |
            Where-Object { $_ } |
            Join-String -Separator "`n"

        if (-not $embeddingText) {
            Write-Host "  Record $($i + 1)/$($allDocuments.Count): skipped (no text)" -ForegroundColor DarkGray
            continue
        }

        Write-Host "  Record $($i + 1)/$($allDocuments.Count): $($doc.recordName ?? $doc.id)..." -ForegroundColor Gray -NoNewline

        if (-not $DryRun) {
            $embedding = Get-Embedding `
                -Text $embeddingText `
                -Endpoint $OpenAiEndpoint `
                -Model $EmbeddingModel `
                -ApiKey $openAiKey `
                -ApiVersion $EmbeddingApiVersion

            $doc.contentVector = $embedding
            Write-Host " done ($($embedding.Count) dims)" -ForegroundColor DarkGray
        } else {
            Write-Host " skipped (dry run)" -ForegroundColor DarkGray
        }
    }
} else {
    Write-Host "[3/4] Skipping embeddings (use -IncludeEmbeddings to generate)" -ForegroundColor Yellow
}

# Step 4: Upload to AI Search
if ($DryRun) {
    Write-Host "[4/4] DRY RUN — would index $($allDocuments.Count) records:" -ForegroundColor Yellow
    foreach ($type in $recordCounts.Keys) {
        Write-Host "  $type`: $($recordCounts[$type])" -ForegroundColor Gray
    }
    Write-Host ""
    Write-Host "Sample document:" -ForegroundColor Gray
    $allDocuments[0] | ConvertTo-Json -Depth 3 | Write-Host -ForegroundColor DarkGray
    exit 0
}

Write-Host "[4/4] Uploading to AI Search index..." -ForegroundColor Cyan

$searchKey = az search admin-key show `
    --service-name $SearchServiceName `
    --resource-group "spe-infrastructure-westus2" `
    --query 'primaryKey' -o tsv

if (-not $searchKey) {
    Write-Error "Failed to get AI Search admin key"
    exit 1
}

$indexed = Upload-ToSearchIndex `
    -Documents $allDocuments `
    -SearchServiceName $SearchServiceName `
    -IndexName $SearchIndexName `
    -AdminKey $searchKey

# Report
Write-Host ""
Write-Host "=== Sync Complete ===" -ForegroundColor Green
foreach ($type in $recordCounts.Keys) {
    Write-Host "  $type`: $($recordCounts[$type]) records" -ForegroundColor Gray
}
Write-Host "  Total indexed: $indexed / $($allDocuments.Count)" -ForegroundColor $(if ($indexed -eq $allDocuments.Count) { "Green" } else { "Yellow" })
if ($IncludeEmbeddings) {
    Write-Host "  Embeddings: generated (3072 dimensions)" -ForegroundColor Gray
} else {
    Write-Host "  Embeddings: skipped (run with -IncludeEmbeddings)" -ForegroundColor Gray
}
Write-Host ""
