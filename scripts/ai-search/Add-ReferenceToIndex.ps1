# Add-ReferenceToIndex.ps1
# Indexes a golden reference document into the spaarke-rag-references AI Search index.
#
# Supports .md, .docx, and .pdf file formats.
# Creates a Dataverse catalog record (type=RagIndex) and indexes chunks with embeddings.
#
# Usage:
#   .\Add-ReferenceToIndex.ps1 -FilePath "path\to\KNW-001-contract-terms-glossary.md"
#   .\Add-ReferenceToIndex.ps1 -FilePath "path\to\document.docx" -Name "My Document" -Domain "legal"
#   .\Add-ReferenceToIndex.ps1 -FilePath "path\to\document.pdf" -Name "My PDF" -DryRun
#   .\Add-ReferenceToIndex.ps1 -FilePath "path\to\doc.md" -SkipDataverse  # Index only, no Dataverse record
#
# Prerequisites:
#   - az login (authenticated to Azure)
#   - Azure AI Search admin key access
#   - Azure OpenAI API access

param(
    [Parameter(Mandatory)][string]$FilePath,
    [string]$KnowledgeSourceId,
    [string]$Name,
    [string]$Domain = "legal",
    [string[]]$Tags = @(),
    [string[]]$Keywords = @(),
    [string]$KnowledgeTypeId,                # GUID of the content category lookup (Standards, Regulations, etc.)
    [string]$Version = "1.0",
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com",
    [string]$SearchServiceName = "spaarke-search-dev",
    [string]$SearchIndexName = "spaarke-rag-references",
    [string]$OpenAiEndpoint = "https://spaarke-openai-dev.openai.azure.com/",
    [string]$EmbeddingModel = "text-embedding-3-large",
    [string]$EmbeddingApiVersion = "2024-06-01",
    [int]$ChunkSize = 2048,
    [int]$ChunkOverlap = 400,
    [switch]$SkipDataverse,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

# ─────────────────────────────────────────────────────────────────────────────
# Helper functions
# ─────────────────────────────────────────────────────────────────────────────

function Get-TextFromMarkdown {
    param([string]$Path)
    return Get-Content $Path -Raw -Encoding UTF8
}

function Get-TextFromDocx {
    param([string]$Path)

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $docEntry = $zip.Entries | Where-Object { $_.FullName -eq "word/document.xml" }
        if (-not $docEntry) {
            throw "Not a valid .docx file: word/document.xml not found"
        }
        $stream = $docEntry.Open()
        $reader = New-Object System.IO.StreamReader($stream)
        $xml = [xml]$reader.ReadToEnd()
        $reader.Close()
        $stream.Close()

        # Extract text from w:t elements
        $ns = New-Object System.Xml.XmlNamespaceManager($xml.NameTable)
        $ns.AddNamespace("w", "http://schemas.openxmlformats.org/wordprocessingml/2006/main")
        $paragraphs = $xml.SelectNodes("//w:p", $ns)

        $text = @()
        foreach ($p in $paragraphs) {
            $runs = $p.SelectNodes(".//w:t", $ns)
            $paraText = ($runs | ForEach-Object { $_.InnerText }) -join ""
            if ($paraText.Trim()) {
                $text += $paraText
            }
        }
        return $text -join "`n"
    } finally {
        $zip.Dispose()
    }
}

function Get-TextFromPdf {
    param(
        [string]$Path,
        [string]$DocIntelEndpoint = "https://westus2.api.cognitive.microsoft.com/"
    )

    Write-Host "  Extracting text from PDF via Azure Document Intelligence..." -ForegroundColor Gray

    # Get Document Intelligence key
    $docIntelKey = az cognitiveservices account keys list `
        --name "spaarke-docintel-dev" `
        --resource-group "spe-infrastructure-westus2" `
        --query 'key1' -o tsv

    if (-not $docIntelKey) {
        throw "Failed to get Document Intelligence key"
    }

    # Read file as base64
    $fileBytes = [System.IO.File]::ReadAllBytes($Path)
    $base64Content = [Convert]::ToBase64String($fileBytes)

    # Submit analysis request
    $analyzeUrl = "${DocIntelEndpoint}documentintelligence/documentModels/prebuilt-read:analyze?api-version=2024-11-30"
    $analyzeBody = @{
        base64Source = $base64Content
    } | ConvertTo-Json

    $analyzeHeaders = @{
        'Ocp-Apim-Subscription-Key' = $docIntelKey
        'Content-Type' = 'application/json'
    }

    $analyzeResponse = Invoke-WebRequest -Uri $analyzeUrl -Headers $analyzeHeaders -Method Post -Body $analyzeBody
    $operationUrl = $analyzeResponse.Headers['Operation-Location']

    if (-not $operationUrl) {
        throw "Failed to get operation URL from Document Intelligence"
    }

    # Poll for results
    $maxAttempts = 30
    $attempt = 0
    $resultHeaders = @{
        'Ocp-Apim-Subscription-Key' = $docIntelKey
    }

    do {
        Start-Sleep -Seconds 2
        $attempt++
        $statusResponse = Invoke-RestMethod -Uri $operationUrl -Headers $resultHeaders -Method Get
        Write-Host "    Poll attempt $attempt : $($statusResponse.status)" -ForegroundColor Gray
    } while ($statusResponse.status -eq "running" -and $attempt -lt $maxAttempts)

    if ($statusResponse.status -ne "succeeded") {
        throw "Document Intelligence analysis failed with status: $($statusResponse.status)"
    }

    return $statusResponse.analyzeResult.content
}

function Split-TextIntoChunks {
    param(
        [string]$Text,
        [int]$Size = 2048,
        [int]$Overlap = 400
    )

    $chunks = @()
    $pos = 0
    $textLength = $Text.Length

    while ($pos -lt $textLength) {
        $end = [Math]::Min($pos + $Size, $textLength)

        # Try to break at sentence boundary
        if ($end -lt $textLength) {
            $searchStart = [Math]::Max($end - 200, $pos)
            $searchRegion = $Text.Substring($searchStart, $end - $searchStart)

            # Find last sentence-ending punctuation
            $lastPeriod = $searchRegion.LastIndexOf(". ")
            $lastQuestion = $searchRegion.LastIndexOf("? ")
            $lastExclaim = $searchRegion.LastIndexOf("! ")
            $lastNewline = $searchRegion.LastIndexOf("`n")

            $breakPoints = @($lastPeriod, $lastQuestion, $lastExclaim, $lastNewline) | Where-Object { $_ -ge 0 }
            if ($breakPoints.Count -gt 0) {
                $bestBreak = ($breakPoints | Measure-Object -Maximum).Maximum
                $end = $searchStart + $bestBreak + 2  # +2 to include the punctuation and space
            }
        }

        $chunk = $Text.Substring($pos, $end - $pos).Trim()
        if ($chunk.Length -gt 0) {
            $chunks += $chunk
        }

        # Advance by overlap from chunk end, but ensure minimum progress
        $newPos = $end - $Overlap
        $minStep = [Math]::Max($Size - $Overlap, 1)
        if ($newPos -le $pos) {
            $newPos = $pos + $minStep
        }
        $pos = $newPos
        if ($pos -ge $textLength) { break }
    }

    return $chunks
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
        'api-key' = $ApiKey
        'Content-Type' = 'application/json'
    }

    $response = Invoke-RestMethod -Uri $url -Headers $headers -Method Post -Body $body
    return $response.data[0].embedding
}

function Parse-KnwMetadata {
    param([string]$Content)

    $metadata = @{}

    # Parse blockquote metadata headers
    if ($Content -match '>\s*\*\*External ID\*\*:\s*(\S+)') {
        $metadata['ExternalId'] = $Matches[1]
    }
    if ($Content -match '>\s*\*\*Content Type\*\*:\s*(.+)') {
        $metadata['ContentType'] = $Matches[1].Trim()
    }
    if ($Content -match '>\s*\*\*Domain\*\*:\s*(.+)') {
        $metadata['Domain'] = $Matches[1].Trim()
    }
    if ($Content -match '>\s*\*\*Keywords\*\*:\s*(.+)') {
        $metadata['Keywords'] = ($Matches[1].Trim() -split ',\s*')
    }
    # Also extract the title from first heading
    if ($Content -match '^#\s+(.+)$') {
        $metadata['Title'] = $Matches[1].Trim()
        # Strip KNW ID prefix from title if present
        if ($metadata['Title'] -match '^KNW-\d+\s*[-—]\s*(.+)$') {
            $metadata['Title'] = $Matches[1].Trim()
        }
    }

    return $metadata
}

# ─────────────────────────────────────────────────────────────────────────────
# Main script
# ─────────────────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "=== Add Reference to Index ===" -ForegroundColor Cyan
Write-Host "File: $FilePath"
if ($DryRun) {
    Write-Host "Mode: DRY RUN" -ForegroundColor Yellow
} else {
    Write-Host "Mode: LIVE" -ForegroundColor Green
}
Write-Host ""

# Validate file exists
if (-not (Test-Path $FilePath)) {
    Write-Error "File not found: $FilePath"
    exit 1
}

$extension = [System.IO.Path]::GetExtension($FilePath).ToLower()
$fileName = [System.IO.Path]::GetFileNameWithoutExtension($FilePath)

# Step 1: Extract text based on file format
Write-Host "[1/6] Extracting text from $extension file..." -ForegroundColor Cyan

$content = switch ($extension) {
    ".md"   { Get-TextFromMarkdown -Path $FilePath }
    ".docx" { Get-TextFromDocx -Path $FilePath }
    ".pdf"  { Get-TextFromPdf -Path $FilePath }
    default {
        Write-Error "Unsupported file format: $extension (supported: .md, .docx, .pdf)"
        exit 1
    }
}

if ([string]::IsNullOrWhiteSpace($content)) {
    Write-Error "No text content extracted from file"
    exit 1
}

Write-Host "  Extracted $($content.Length) characters" -ForegroundColor Gray

# Step 2: Parse metadata (from file headers or parameters)
Write-Host "[2/6] Resolving metadata..." -ForegroundColor Cyan

$metadata = @{}
if ($extension -eq ".md") {
    $metadata = Parse-KnwMetadata -Content $content
}

# Apply parameter overrides (or use extracted values)
$sourceId = if ($KnowledgeSourceId) { $KnowledgeSourceId }
            elseif ($metadata['ExternalId']) { $metadata['ExternalId'] }
            else { $fileName }

$sourceName = if ($Name) { $Name }
              elseif ($metadata['Title']) { $metadata['Title'] }
              else { $fileName }

$sourceDomain = if ($PSBoundParameters.ContainsKey('Domain')) { $Domain }
                elseif ($metadata['Domain']) { $metadata['Domain'] }
                else { "legal" }

$sourceKeywords = if ($Keywords.Count -gt 0) { $Keywords }
                  elseif ($metadata['Keywords']) { $metadata['Keywords'] }
                  else { @() }

$allTags = @($Tags) + @($sourceKeywords) | Where-Object { $_ } | Select-Object -Unique

Write-Host "  Knowledge Source ID: $sourceId" -ForegroundColor Gray
Write-Host "  Name: $sourceName" -ForegroundColor Gray
Write-Host "  Domain: $sourceDomain" -ForegroundColor Gray
Write-Host "  Tags: $($allTags -join ', ')" -ForegroundColor Gray
Write-Host "  Version: $Version" -ForegroundColor Gray

# Step 3: Chunk text
Write-Host "[3/6] Chunking text ($ChunkSize chars, $ChunkOverlap overlap)..." -ForegroundColor Cyan

$chunks = Split-TextIntoChunks -Text $content -Size $ChunkSize -Overlap $ChunkOverlap
Write-Host "  Generated $($chunks.Count) chunks" -ForegroundColor Gray

for ($i = 0; $i -lt [Math]::Min($chunks.Count, 3); $i++) {
    $preview = $chunks[$i].Substring(0, [Math]::Min(80, $chunks[$i].Length))
    Write-Host "    Chunk $i : $($chunks[$i].Length) chars - `"$preview...`"" -ForegroundColor DarkGray
}
if ($chunks.Count -gt 3) {
    Write-Host "    ... ($($chunks.Count - 3) more chunks)" -ForegroundColor DarkGray
}

if ($DryRun) {
    Write-Host ""
    Write-Host "=== DRY RUN Summary ===" -ForegroundColor Yellow
    Write-Host "  Would index $($chunks.Count) chunks for '$sourceName' ($sourceId)"
    Write-Host "  Domain: $sourceDomain | Tags: $($allTags -join ', ')"
    if (-not $SkipDataverse) {
        Write-Host "  Would create Dataverse catalog record (delivery type: RAG Index)"
    }
    Write-Host ""
    exit 0
}

# Step 4: Create Dataverse catalog record (unless skipped)
if (-not $SkipDataverse) {
    Write-Host "[4/6] Creating Dataverse catalog record..." -ForegroundColor Cyan

    $dvToken = az account get-access-token --resource $EnvironmentUrl --query 'accessToken' -o tsv
    if (-not $dvToken) {
        Write-Error "Failed to get Dataverse access token"
        exit 1
    }

    $dvHeaders = @{
        'Authorization' = "Bearer $dvToken"
        'Accept'        = 'application/json'
        'Content-Type'  = 'application/json'
        'OData-MaxVersion' = '4.0'
        'OData-Version' = '4.0'
        'Prefer'        = 'return=representation'
    }

    # Check if record already exists
    $filter = "`$filter=sprk_name eq '$($sourceName.Replace("'", "''"))'&`$select=sprk_analysisknowledgeid,sprk_name"
    $existingUrl = "$EnvironmentUrl/api/data/v9.2/sprk_analysisknowledges?$filter"
    $existing = Invoke-RestMethod -Uri $existingUrl -Headers $dvHeaders -Method Get

    if ($existing.value.Count -gt 0) {
        $recordId = $existing.value[0].sprk_analysisknowledgeid
        Write-Host "  Record already exists (ID: $recordId) - updating delivery type" -ForegroundColor Yellow

        # Update to RAG Index delivery type
        $updateUrl = "$EnvironmentUrl/api/data/v9.2/sprk_analysisknowledges($recordId)"
        $updatePayload = @{
            sprk_knowledgedeliverytype = 100000002  # RAG Index
            sprk_content = $null                    # Content lives in AI Search
        } | ConvertTo-Json
        Invoke-RestMethod -Uri $updateUrl -Headers $dvHeaders -Method Patch -Body $updatePayload
        Write-Host "  Updated record delivery type to RAG Index" -ForegroundColor Green
    } else {
        # Create new record
        $payload = @{
            sprk_name = $sourceName
            sprk_description = "Golden reference document: $sourceName. Indexed into spaarke-rag-references for RAG retrieval."
            sprk_knowledgedeliverytype = 100000002  # RAG Index
        }

        # Bind to knowledge type lookup if provided
        if ($KnowledgeTypeId) {
            $payload['sprk_KnowledgeTypeId@odata.bind'] = "/sprk_analysisknowledgetypes($KnowledgeTypeId)"
        }

        $createUrl = "$EnvironmentUrl/api/data/v9.2/sprk_analysisknowledges"
        $created = Invoke-RestMethod -Uri $createUrl -Headers $dvHeaders -Method Post -Body ($payload | ConvertTo-Json)
        $recordId = $created.sprk_analysisknowledgeid
        Write-Host "  Created record (ID: $recordId)" -ForegroundColor Green
    }
} else {
    Write-Host "[4/6] Skipping Dataverse catalog record (--SkipDataverse)" -ForegroundColor Yellow
}

# Step 5: Generate embeddings and prepare index documents
Write-Host "[5/6] Generating embeddings ($($chunks.Count) chunks)..." -ForegroundColor Cyan

# Get OpenAI API key
$openAiKey = az cognitiveservices account keys list `
    --name "spaarke-openai-dev" `
    --resource-group "spe-infrastructure-westus2" `
    --query 'key1' -o tsv

if (-not $openAiKey) {
    Write-Error "Failed to get Azure OpenAI API key"
    exit 1
}

# Get AI Search admin key
$searchKey = az search admin-key show `
    --service-name $SearchServiceName `
    --resource-group "spe-infrastructure-westus2" `
    --query 'primaryKey' -o tsv

if (-not $searchKey) {
    Write-Error "Failed to get AI Search admin key"
    exit 1
}

$timestamp = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
$indexDocuments = @()

for ($i = 0; $i -lt $chunks.Count; $i++) {
    $chunkText = $chunks[$i]
    Write-Host "  Embedding chunk $($i + 1)/$($chunks.Count)..." -ForegroundColor Gray -NoNewline

    $embedding = Get-Embedding `
        -Text $chunkText `
        -Endpoint $OpenAiEndpoint `
        -Model $EmbeddingModel `
        -ApiKey $openAiKey `
        -ApiVersion $EmbeddingApiVersion

    Write-Host " done ($($embedding.Count) dimensions)" -ForegroundColor DarkGray

    $doc = @{
        "@search.action" = "upload"
        id = "${sourceId}_ref_${i}"
        tenantId = "system"
        knowledgeSourceId = $sourceId
        knowledgeSourceName = $sourceName
        domain = $sourceDomain
        content = $chunkText
        contentVector3072 = $embedding
        tags = $allTags
        version = $Version
        chunkIndex = $i
        chunkCount = $chunks.Count
        createdAt = $timestamp
        updatedAt = $timestamp
    }
    $indexDocuments += $doc
}

# Step 6: Delete existing chunks and upload to AI Search
Write-Host "[6/6] Uploading to AI Search index..." -ForegroundColor Cyan

$searchUrl = "https://${SearchServiceName}.search.windows.net"
$searchHeaders = @{
    'api-key' = $searchKey
    'Content-Type' = 'application/json'
}

# Delete existing chunks for this source
Write-Host "  Deleting existing chunks for $sourceId..." -ForegroundColor Gray
$searchQuery = @{
    search = "*"
    filter = "knowledgeSourceId eq '$sourceId'"
    select = "id"
    top = 1000
} | ConvertTo-Json

$existingChunks = Invoke-RestMethod `
    -Uri "$searchUrl/indexes/$SearchIndexName/docs/search?api-version=2024-07-01" `
    -Headers $searchHeaders `
    -Method Post `
    -Body $searchQuery

$deleteCount = $existingChunks.value.Count
if ($deleteCount -gt 0) {
    $deleteBatch = @{
        value = $existingChunks.value | ForEach-Object {
            @{
                "@search.action" = "delete"
                id = $_.id
            }
        }
    } | ConvertTo-Json -Depth 5

    Invoke-RestMethod `
        -Uri "$searchUrl/indexes/$SearchIndexName/docs/index?api-version=2024-07-01" `
        -Headers $searchHeaders `
        -Method Post `
        -Body $deleteBatch | Out-Null

    Write-Host "  Deleted $deleteCount existing chunk(s)" -ForegroundColor Gray
}

# Upload new chunks (batch of 1000 max per AI Search limit)
$batchSize = 1000
for ($batch = 0; $batch -lt $indexDocuments.Count; $batch += $batchSize) {
    $batchEnd = [Math]::Min($batch + $batchSize, $indexDocuments.Count)
    $batchDocs = $indexDocuments[$batch..($batchEnd - 1)]

    $uploadPayload = @{
        value = $batchDocs
    } | ConvertTo-Json -Depth 5 -Compress

    $uploadResult = Invoke-RestMethod `
        -Uri "$searchUrl/indexes/$SearchIndexName/docs/index?api-version=2024-07-01" `
        -Headers $searchHeaders `
        -Method Post `
        -Body $uploadPayload

    $succeeded = ($uploadResult.value | Where-Object { $_.status }).Count
    Write-Host "  Uploaded batch: $succeeded/$($batchDocs.Count) chunks succeeded" -ForegroundColor Gray
}

# Summary
Write-Host ""
Write-Host "=== Indexing Complete ===" -ForegroundColor Green
Write-Host "  Source: $sourceName ($sourceId)" -ForegroundColor Gray
Write-Host "  Chunks: $($chunks.Count)" -ForegroundColor Gray
Write-Host "  Dimensions: 3072" -ForegroundColor Gray
Write-Host "  Index: $SearchIndexName" -ForegroundColor Gray
Write-Host "  Domain: $sourceDomain" -ForegroundColor Gray
if (-not $SkipDataverse) {
    Write-Host "  Dataverse Record: $recordId" -ForegroundColor Gray
    Write-Host "  Delivery Type: RAG Index (100000002)" -ForegroundColor Gray
}
Write-Host ""
