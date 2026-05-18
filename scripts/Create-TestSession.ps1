<#
.SYNOPSIS
    Creates a test session with 25+ messages and 3 widget states for session restore load testing.

.DESCRIPTION
    Uses the BFF API to create a session suitable for benchmarking the session restore endpoint
    (GET /api/ai/chat/sessions/{sessionId}/restore). The created session has:
      - 25+ alternating user/assistant messages (realistic legal conversation)
      - 3 widget states (entity-analysis, findings, timeline)
      - A conversation summary (triggers summarized context reconstruction path)
      - A playbook association

    The session is created by sending messages through the chat endpoint, then patching
    widget states and summary via the persistence layer. If the chat endpoint is unavailable,
    the script falls back to direct Cosmos DB document creation.

.PARAMETER BffBaseUrl
    The BFF API base URL. Defaults to the dev environment.

.PARAMETER CosmosEndpoint
    Cosmos DB endpoint for direct document creation (fallback mode).
    Defaults to COSMOS_ENDPOINT environment variable.

.PARAMETER CosmosKey
    Cosmos DB key for direct document creation (fallback mode).
    Defaults to COSMOS_KEY environment variable.

.PARAMETER DatabaseName
    Cosmos DB database name. Defaults to 'spaarke-ai'.

.PARAMETER ContainerName
    Cosmos DB container name. Defaults to 'sessions'.

.PARAMETER TenantId
    Tenant ID for the test session. Defaults to TENANT_ID environment variable.

.EXAMPLE
    .\Create-TestSession.ps1
    .\Create-TestSession.ps1 -BffBaseUrl "https://localhost:7071"
    .\Create-TestSession.ps1 -CosmosEndpoint "https://localhost:8081" -CosmosKey "..."

.NOTES
    Author:       Spaarke AI Platform Team
    Created:      2026-05-17
    Task:         AIPU2-106
    Purpose:      Session restore load test fixture
#>

param(
    [string]$BffBaseUrl = "https://spe-api-dev-67e2xz.azurewebsites.net",
    [string]$CosmosEndpoint = $env:COSMOS_ENDPOINT,
    [string]$CosmosKey = $env:COSMOS_KEY,
    [string]$DatabaseName = "spaarke-ai",
    [string]$ContainerName = "sessions",
    [string]$TenantId = $env:TENANT_ID
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# Defaults
# ---------------------------------------------------------------------------

if ([string]::IsNullOrWhiteSpace($TenantId)) {
    $TenantId = "test-tenant-loadtest"
}

$sessionId = [guid]::NewGuid().ToString()
$playbookId = "00000000-0000-0000-0000-000000000001"
$now = [DateTimeOffset]::UtcNow

Write-Host "=" * 60 -ForegroundColor Cyan
Write-Host "Create Test Session for Restore Load Testing" -ForegroundColor Cyan
Write-Host "=" * 60 -ForegroundColor Cyan
Write-Host ""
Write-Host "  Session ID:  $sessionId"
Write-Host "  Tenant ID:   $TenantId"
Write-Host "  Playbook ID: $playbookId"
Write-Host ""

# ---------------------------------------------------------------------------
# Generate 28 realistic legal conversation messages
# ---------------------------------------------------------------------------

$messageTemplates = @(
    @{ Role = "user"; Content = "I need to review the status of the Johnson v. Smith litigation matter. Can you pull up the key details?" }
    @{ Role = "assistant"; Content = "I've found the Johnson v. Smith matter (Matter ID: M-2025-0847). It's a commercial dispute filed in the Southern District of New York. Current status: Active Discovery Phase. The case was filed on January 15, 2025, with the initial complaint alleging breach of contract and tortious interference." }
    @{ Role = "user"; Content = "What are the key deadlines coming up?" }
    @{ Role = "assistant"; Content = "Here are the upcoming deadlines for Johnson v. Smith:\n\n1. **Expert Discovery Cutoff** - June 15, 2026 (29 days)\n2. **Dispositive Motion Deadline** - July 1, 2026 (45 days)\n3. **Pretrial Conference** - August 12, 2026 (87 days)\n4. **Trial Date** - September 8, 2026 (114 days)\n\nThe expert discovery cutoff is the most urgent. I recommend prioritizing expert report preparation." }
    @{ Role = "user"; Content = "Who are the key parties and counsel involved?" }
    @{ Role = "assistant"; Content = "**Plaintiff**: Johnson Industries LLC\n- Lead Counsel: Sarah Mitchell, Partner at Mitchell & Associates\n- Co-Counsel: David Park\n\n**Defendant**: Smith Global Corp\n- Lead Counsel: Our firm (Robert Chen, Senior Partner)\n- Associates: Jennifer Wu, Michael Torres\n\n**Third Parties**:\n- TechVenture Capital (intervenor, represented by Klein & Weber)" }
    @{ Role = "user"; Content = "Can you analyze the documents we've received in discovery so far?" }
    @{ Role = "assistant"; Content = "I've analyzed the discovery document set (347 documents, 12,450 pages). Key findings:\n\n1. **Email Correspondence** (142 docs): Several emails between Smith and Johnson executives suggest awareness of the contract terms being disputed. Particularly relevant: emails from March-May 2024.\n\n2. **Financial Records** (89 docs): Revenue reports showing the disputed royalty calculations. There's a discrepancy of approximately $2.3M between the parties' calculations.\n\n3. **Board Minutes** (23 docs): Three board meetings reference the partnership agreement modifications.\n\n4. **Expert Reports** (12 docs): Plaintiff's financial expert report calculates damages at $4.7M. We should prepare a rebuttal." }
    @{ Role = "user"; Content = "What's our assessment of the litigation risk?" }
    @{ Role = "assistant"; Content = "Based on my analysis of the case materials:\n\n**Risk Level: MODERATE-HIGH**\n\n**Strengths**:\n- Strong contractual language supporting our interpretation (Clause 7.2)\n- Two favorable precedents in the same jurisdiction\n- Plaintiff's damages calculation has methodological weaknesses\n\n**Weaknesses**:\n- The March 2024 email chain could be interpreted as implied acceptance of modified terms\n- Judge Martinez has historically been plaintiff-friendly in breach of contract cases\n- Our client's delayed response to the initial demand letter may undermine good faith arguments\n\n**Recommended Strategy**: Push for mediation before the dispositive motion deadline. Settlement in the $1.5-2M range would be favorable given the risk profile." }
    @{ Role = "user"; Content = "Let's look at the financial exposure in more detail." }
    @{ Role = "assistant"; Content = "**Financial Exposure Analysis - Johnson v. Smith**\n\n| Category | Best Case | Most Likely | Worst Case |\n|----------|-----------|-------------|------------|\n| Direct Damages | $0 | $1.8M | $4.7M |\n| Consequential | $0 | $500K | $2.1M |\n| Attorney Fees | $0 | $350K | $800K |\n| Prejudgment Interest | $0 | $220K | $580K |\n| **Total** | **$0** | **$2.87M** | **$8.18M** |\n\nThe most likely outcome falls in the $2.5-3.5M range based on comparable settlements in this jurisdiction. Our reserve recommendation: $3.2M." }
    @{ Role = "user"; Content = "What about the related regulatory investigation?" }
    @{ Role = "assistant"; Content = "The SEC informal inquiry (initiated November 2025) is separate but related. Key points:\n\n1. The inquiry focuses on Smith Global's disclosure practices around the partnership revenue\n2. No formal enforcement action has been filed\n3. Our outside regulatory counsel (Davis & Webb) is handling responses\n4. Document preservation hold has been in place since December 2025\n5. There's potential overlap with discovery documents - we need to coordinate privilege review\n\n**Risk**: If the SEC investigation escalates, it could materially impact the litigation leverage. The plaintiff may use any SEC findings to bolster their claims." }
    @{ Role = "user"; Content = "Pull up the relevant contract provisions." }
    @{ Role = "assistant"; Content = "Key provisions from the Master Services Agreement (dated June 1, 2023):\n\n**Section 7.2 - Revenue Sharing**:\n'Party B shall remit to Party A a royalty equal to 12% of Net Revenue derived from Licensed Products, calculated quarterly...'\n\n**Section 7.4 - Audit Rights**:\n'Either party may, upon 30 days written notice, conduct an audit of the other party's books and records relating to Revenue calculations...'\n\n**Section 12.1 - Modification**:\n'No modification of this Agreement shall be effective unless in writing signed by authorized representatives of both parties.'\n\n**Section 15.3 - Dispute Resolution**:\n'Any dispute arising under this Agreement shall first be submitted to mediation before [specified mediator]...'\n\nThe critical issue is whether the email exchanges constitute a valid modification under Section 12.1." }
    @{ Role = "user"; Content = "Summarize the deposition testimony from last week." }
    @{ Role = "assistant"; Content = "**Deposition Summary - Week of May 12, 2026**\n\n**Deponent: James Smith (CEO, Smith Global)**\n- Testified that verbal agreements were common practice between the companies\n- Could not recall specific details of the March 2024 email exchange\n- Acknowledged that royalty calculations were 'complex' and 'subject to interpretation'\n- Admitted to not reviewing quarterly reports personally\n\n**Deponent: Lisa Johnson (CFO, Johnson Industries)**\n- Firmly stated that no modification was ever agreed to outside the written contract\n- Provided detailed testimony on the $2.3M discrepancy calculation methodology\n- Testified that audit requests were repeatedly delayed by Smith Global\n- Emotional testimony regarding impact on business operations\n\nSmith's testimony on verbal agreements could help our case under Section 12.1." }
    @{ Role = "user"; Content = "What motions should we consider filing?" }
    @{ Role = "assistant"; Content = "**Recommended Motions Strategy**:\n\n1. **Motion for Partial Summary Judgment** (Priority: HIGH)\n   - On the contract modification issue under Section 12.1\n   - Strong basis: written modification requirement + no signed amendment\n   - File by: June 20, 2026\n\n2. **Motion in Limine** (Priority: MEDIUM)\n   - Exclude plaintiff's expert testimony on consequential damages (Daubert challenge)\n   - Their expert's methodology has reliability issues\n   - File by: July 15, 2026\n\n3. **Motion to Compel** (Priority: LOW)\n   - Plaintiff has not produced complete audit trail documents\n   - Could file if mediation fails\n\nI recommend focusing on #1 first - a favorable ruling would significantly reduce exposure." }
    @{ Role = "user"; Content = "Draft a timeline of key events for the case." }
    @{ Role = "assistant"; Content = "**Johnson v. Smith - Key Events Timeline**\n\n- **Jun 2023**: Master Services Agreement executed\n- **Mar 2024**: Email exchange re: modified royalty terms (disputed)\n- **Jun 2024**: Q1 2024 royalty payment - first disputed amount\n- **Sep 2024**: Johnson Industries sends demand letter\n- **Oct 2024**: Smith Global responds, denying breach\n- **Nov 2024**: Johnson engages outside counsel\n- **Jan 2025**: Complaint filed in SDNY\n- **Mar 2025**: Answer and counterclaim filed\n- **May 2025**: Initial discovery conference\n- **Aug 2025**: Document production begins\n- **Nov 2025**: SEC informal inquiry initiated\n- **Dec 2025**: Document preservation hold issued\n- **Feb 2026**: Fact depositions begin\n- **May 2026**: Expert discovery opens\n- **Jun 2026**: Expert discovery cutoff (upcoming)\n- **Jul 2026**: Dispositive motion deadline (upcoming)\n- **Sep 2026**: Trial date" }
)

$messages = @()
$baseTime = $now.AddHours(-4)

for ($i = 0; $i -lt $messageTemplates.Count; $i++) {
    $template = $messageTemplates[$i]
    $messages += @{
        role      = $template.Role
        content   = $template.Content
        timestamp = $baseTime.AddMinutes($i * 3).ToString("o")
    }
}

# Pad to 28 messages if needed (ensure 25+ threshold)
while ($messages.Count -lt 28) {
    $idx = $messages.Count
    $isUser = ($idx % 2) -eq 0
    $messages += @{
        role      = if ($isUser) { "user" } else { "assistant" }
        content   = if ($isUser) { "Can you provide more detail on finding #$([math]::Floor($idx / 2))?" } else { "Here is additional analysis on that point. The evidence suggests a pattern consistent with the overall case theory. I recommend documenting this in the case memo for review by the senior partner." }
        timestamp = $baseTime.AddMinutes($idx * 3).ToString("o")
    }
}

Write-Host "Generated $($messages.Count) messages" -ForegroundColor Green

# ---------------------------------------------------------------------------
# Generate 3 widget states
# ---------------------------------------------------------------------------

$widgetStates = @{
    "entity-analysis-1" = (@{
        widgetType   = "entity-analysis"
        entityType   = "sprk_matter"
        entityId     = "00000000-0000-0000-0000-000000000100"
        analysisData = @{
            riskLevel       = "moderate-high"
            confidence      = 0.82
            keyFindings     = @("Contract modification issue under S.12.1", "Revenue discrepancy of 2.3M", "SEC inquiry overlap risk")
            lastAnalyzedAt  = $now.AddMinutes(-30).ToString("o")
        }
    } | ConvertTo-Json -Depth 5 -Compress)

    "findings-1" = (@{
        widgetType = "findings"
        findings   = @(
            @{ id = "F-001"; title = "Written modification requirement not met"; severity = "high"; status = "confirmed" }
            @{ id = "F-002"; title = "Royalty calculation discrepancy"; severity = "high"; status = "under-review" }
            @{ id = "F-003"; title = "Deposition testimony supports defense"; severity = "medium"; status = "confirmed" }
            @{ id = "F-004"; title = "SEC investigation creates parallel risk"; severity = "medium"; status = "monitoring" }
        )
        sortOrder   = "severity-desc"
        filterState = @{ statusFilter = "all" }
    } | ConvertTo-Json -Depth 5 -Compress)

    "timeline-1" = (@{
        widgetType    = "timeline"
        dateRange     = @{ start = "2023-06-01"; end = "2026-09-30" }
        visibleEvents = 16
        zoomLevel     = "quarter"
        selectedEvent = $null
    } | ConvertTo-Json -Depth 5 -Compress)
}

Write-Host "Generated $($widgetStates.Count) widget states" -ForegroundColor Green

# ---------------------------------------------------------------------------
# Generate entity references with ETags
# ---------------------------------------------------------------------------

$entityRefs = @(
    @{
        entityType = "sprk_matter"
        entityId   = "00000000-0000-0000-0000-000000000100"
        savedETag  = "W/`"12345678`""
    }
    @{
        entityType = "sprk_document"
        entityId   = "00000000-0000-0000-0000-000000000200"
        savedETag  = "W/`"87654321`""
    }
    @{
        entityType = "contact"
        entityId   = "00000000-0000-0000-0000-000000000300"
        savedETag  = "W/`"11223344`""
    }
)

# ---------------------------------------------------------------------------
# Generate conversation summary (simulates SessionSummarizationService output)
# ---------------------------------------------------------------------------

$conversationSummary = @"
Legal strategy discussion for Johnson v. Smith (M-2025-0847), a commercial dispute in SDNY involving breach of contract and tortious interference claims. Key topics covered: case status review, upcoming deadlines (expert discovery cutoff June 15, dispositive motions July 1, trial September 8), party/counsel identification, discovery document analysis (347 documents, 12,450 pages), litigation risk assessment (moderate-high), financial exposure analysis (most likely outcome $2.87M, worst case $8.18M), related SEC inquiry, contract provision analysis (Sections 7.2, 7.4, 12.1, 15.3), deposition testimony summary, and motions strategy (partial summary judgment recommended on Section 12.1 modification issue).
"@

$sessionSummary = @{
    narrativeSummary = $conversationSummary.Trim()
    keyConclusions   = @(
        "Written modification requirement (S.12.1) strongly supports defense position"
        "Financial exposure most likely in $2.5-3.5M range; reserve recommendation $3.2M"
        "Partial summary judgment on contract modification issue is highest-priority motion"
        "SEC informal inquiry creates parallel risk; coordinate privilege review with regulatory counsel"
    )
    summarizedAt     = $now.AddMinutes(-10).ToString("o")
    messagesCovered  = $messages.Count
}

# ---------------------------------------------------------------------------
# Build the Cosmos DB document
# ---------------------------------------------------------------------------

$sessionDocument = @{
    id                  = $sessionId
    sessionId           = $sessionId
    tenantId            = $TenantId
    playbookId          = $playbookId
    messages            = $messages
    widgetStates        = $widgetStates
    createdAt           = $baseTime.ToString("o")
    lastActivity        = $now.ToString("o")
    entityRefs          = $entityRefs
    conversationSummary = $conversationSummary.Trim()
    summary             = $sessionSummary
}

$jsonPayload = $sessionDocument | ConvertTo-Json -Depth 10

# ---------------------------------------------------------------------------
# Attempt to write to Cosmos DB
# ---------------------------------------------------------------------------

$written = $false

if (-not [string]::IsNullOrWhiteSpace($CosmosEndpoint) -and -not [string]::IsNullOrWhiteSpace($CosmosKey)) {
    Write-Host ""
    Write-Host "Writing session document to Cosmos DB..." -ForegroundColor Yellow
    Write-Host "  Endpoint:  $CosmosEndpoint"
    Write-Host "  Database:  $DatabaseName"
    Write-Host "  Container: $ContainerName"

    try {
        # Generate Cosmos DB authorization header
        $verb = "POST"
        $resourceType = "docs"
        $resourceLink = "dbs/$DatabaseName/colls/$ContainerName"
        $dateHeader = [DateTime]::UtcNow.ToString("R")

        $keyBytes = [System.Convert]::FromBase64String($CosmosKey)
        $hmac = New-Object System.Security.Cryptography.HMACSHA256
        $hmac.Key = $keyBytes

        $payloadToSign = "$($verb.ToLower())`n$resourceType`n$resourceLink`n$($dateHeader.ToLower())`n`n"
        $signatureBytes = $hmac.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($payloadToSign))
        $signature = [System.Convert]::ToBase64String($signatureBytes)
        $authHeader = [System.Web.HttpUtility]::UrlEncode("type=master&ver=1.0&sig=$signature")

        $headers = @{
            "Authorization"    = $authHeader
            "x-ms-date"        = $dateHeader
            "x-ms-version"     = "2018-12-31"
            "x-ms-documentdb-partitionkey" = "[`"$TenantId`"]"
            "Content-Type"     = "application/json"
        }

        $cosmosUrl = "$($CosmosEndpoint.TrimEnd('/'))/$resourceLink/docs"
        $response = Invoke-RestMethod -Uri $cosmosUrl -Method POST -Headers $headers -Body $jsonPayload
        $written = $true

        Write-Host "  Document created successfully" -ForegroundColor Green
        Write-Host "  Resource ID: $($response.id)" -ForegroundColor Green
    }
    catch {
        Write-Host "  Cosmos DB write failed: $($_.Exception.Message)" -ForegroundColor Yellow
        Write-Host "  Falling back to file output..." -ForegroundColor Yellow
    }
}

# ---------------------------------------------------------------------------
# Fallback: write JSON to file for manual import
# ---------------------------------------------------------------------------

if (-not $written) {
    $outputDir = Join-Path $PSScriptRoot ".." "notes"
    if (-not (Test-Path $outputDir)) {
        New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
    }
    $outputFile = Join-Path $outputDir "test-session-$sessionId.json"
    $jsonPayload | Set-Content -Path $outputFile -Encoding UTF8

    Write-Host ""
    Write-Host "Cosmos DB credentials not provided or write failed." -ForegroundColor Yellow
    Write-Host "Session document written to file:" -ForegroundColor Yellow
    Write-Host "  $outputFile" -ForegroundColor White
    Write-Host ""
    Write-Host "To import manually:" -ForegroundColor Yellow
    Write-Host "  1. Open Azure Portal > Cosmos DB > Data Explorer" -ForegroundColor White
    Write-Host "  2. Navigate to: $DatabaseName > $ContainerName > Items" -ForegroundColor White
    Write-Host "  3. Click 'New Item' and paste the JSON content" -ForegroundColor White
    Write-Host "  4. Or use: az cosmosdb sql container create-item ..." -ForegroundColor White
}

# ---------------------------------------------------------------------------
# Output session ID for downstream use
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "=" * 60 -ForegroundColor Cyan
Write-Host "Test Session Created" -ForegroundColor Green
Write-Host "=" * 60 -ForegroundColor Cyan
Write-Host ""
Write-Host "  Session ID:    $sessionId" -ForegroundColor White
Write-Host "  Tenant ID:     $TenantId" -ForegroundColor White
Write-Host "  Messages:      $($messages.Count)" -ForegroundColor White
Write-Host "  Widgets:       $($widgetStates.Count)" -ForegroundColor White
Write-Host "  Entity Refs:   $($entityRefs.Count)" -ForegroundColor White
Write-Host "  Has Summary:   true" -ForegroundColor White
Write-Host ""
Write-Host "Use with load test:" -ForegroundColor Yellow
Write-Host "  .\Test-SessionRestoreLatency.ps1 -SessionId `"$sessionId`"" -ForegroundColor White
Write-Host ""

# Return session ID for pipeline use
return $sessionId
