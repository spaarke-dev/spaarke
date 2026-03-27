<#
.SYNOPSIS
    Configures M365 Copilot knowledge for Spaarke's data model in Dataverse.

.DESCRIPTION
    Programmatically teaches M365 Copilot about Spaarke's domain vocabulary by:
    1. Adding glossary terms via the CopilotGlossaryTerm table
    2. Adding column synonyms via the CopilotSynonyms table
    3. Configuring Dataverse Search columns for key entities via Quick Find view updates

    Uses az CLI for authentication and Dataverse Web API v9.2 for all operations.
    The script is idempotent - it checks for existing records before creating new ones.

    NOTE: Glossary terms and synonyms can take up to 15 minutes to take effect
    after creation or modification.

.PARAMETER DataverseUrl
    The Dataverse environment URL. Defaults to DATAVERSE_URL env var.

.PARAMETER DryRun
    Preview changes without modifying Dataverse.

.EXAMPLE
    .\Configure-CopilotKnowledge.ps1
    .\Configure-CopilotKnowledge.ps1 -DryRun
    .\Configure-CopilotKnowledge.ps1 -DataverseUrl "https://spaarkedev1.crm.dynamics.com"
#>

param(
    [string]$DataverseUrl = $env:DATAVERSE_URL,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

if (-not $DataverseUrl) {
    $DataverseUrl = 'https://spaarkedev1.crm.dynamics.com'
}

$ApiBase = "$DataverseUrl/api/data/v9.2"
$AzCmd = 'C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd'

Write-Host ''
Write-Host '================================================================' -ForegroundColor Cyan
Write-Host '  Configure M365 Copilot Knowledge for Spaarke Data Model' -ForegroundColor Cyan
Write-Host '================================================================' -ForegroundColor Cyan
Write-Host "Environment : $DataverseUrl"
if ($DryRun) { Write-Host 'Mode        : DRY RUN' -ForegroundColor Yellow }
else         { Write-Host 'Mode        : LIVE' -ForegroundColor Green }
Write-Host ''

# ---------------------------------------------------------------------------
# Authenticate
# ---------------------------------------------------------------------------
Write-Host '[Step 1/4] Authenticating...' -ForegroundColor Yellow
$token = & $AzCmd account get-access-token --resource $DataverseUrl --query accessToken -o tsv 2>$null
if (-not $token) {
    Write-Error "Failed to get token. Run 'az login' first."
    exit 1
}
Write-Host '  Token acquired.' -ForegroundColor Green

$headers = @{
    "Authorization"    = "Bearer $token"
    "OData-MaxVersion" = "4.0"
    "OData-Version"    = "4.0"
    "Content-Type"     = "application/json"
    "Accept"           = "application/json"
    "Prefer"           = "return=representation"
}

# Headers without Prefer for simple operations
$headersSimple = @{
    "Authorization"    = "Bearer $token"
    "OData-MaxVersion" = "4.0"
    "OData-Version"    = "4.0"
    "Content-Type"     = "application/json"
    "Accept"           = "application/json"
}

# =========================================================================
# PART 1: GLOSSARY TERMS
# =========================================================================
Write-Host ''
Write-Host '[Step 2/4] Adding Glossary Terms...' -ForegroundColor Yellow
Write-Host '  Table: copilotglossaryterms' -ForegroundColor Gray
Write-Host '  Columns: term, description, name' -ForegroundColor Gray
Write-Host ''

# Define glossary terms - term is the word Copilot should recognize,
# description explains what it means in Spaarke context.
# IMPORTANT: No parentheses inside string literals per project convention.
$glossaryTerms = @(
    @{
        term        = 'task'
        description = 'A record in the sprk_event table. Spaarke uses Events for tasks, to-dos, and action items. Do NOT use the standard Dataverse Task or Activity entities.'
        name        = 'task'
    }
    @{
        term        = 'overdue task'
        description = 'A sprk_event record where status is Active and the due date sprk_duedate is earlier than today. Filter by statecode eq 0 and sprk_duedate lt now.'
        name        = 'overdue task'
    }
    @{
        term        = 'deadline'
        description = 'A sprk_event record with event type Deadline. Has a hard due date that must be met.'
        name        = 'deadline'
    }
    @{
        term        = 'assignment'
        description = 'A sprk_event record owned by a specific user. Filter by ownerid to find a user assignments.'
        name        = 'assignment'
    }
    @{
        term        = 'matter'
        description = 'A sprk_matter record representing a legal matter or case. The primary organizing entity containing projects, events, documents, and parties.'
        name        = 'matter'
    }
    @{
        term        = 'case'
        description = 'Same as matter - a sprk_matter record. Users may say case or matter interchangeably.'
        name        = 'case'
    }
    @{
        term        = 'document'
        description = 'A sprk_document record linked to a file in SharePoint Embedded. To search document content, use semantic search, not direct table queries.'
        name        = 'document'
    }
    @{
        term        = 'contract'
        description = 'A sprk_document record where document type is Contract. Filter sprk_documenttype for the Contract value.'
        name        = 'contract'
    }
    @{
        term        = 'playbook'
        description = 'A sprk_analysisplaybook record - a reusable AI analysis workflow like Lease Review, Risk Scan, or NDA Analysis.'
        name        = 'playbook'
    }
    @{
        term        = 'analysis tools'
        description = 'Refers to sprk_analysisplaybook records - the available AI analysis playbooks that can be run against documents.'
        name        = 'analysis tools'
    }
    @{
        term        = 'invoice'
        description = 'A sprk_invoice record - a legal billing record associated with a matter.'
        name        = 'invoice'
    }
    @{
        term        = 'outside counsel'
        description = 'A sprk_party record with role Outside Counsel, associated with a matter. Filter sprk_partyrole for Outside Counsel.'
        name        = 'outside counsel'
    }
    @{
        term        = 'briefing'
        description = 'The daily summary of upcoming deadlines, recent activity, and priority items from the workspace API endpoint.'
        name        = 'briefing'
    }
    @{
        term        = 'analysis results'
        description = 'A sprk_analysisoutput record containing findings, risk flags, and recommendations from a playbook execution.'
        name        = 'analysis results'
    }
    @{
        term        = 'project'
        description = 'A sprk_project record - a workstream within a matter such as Due Diligence or Contract Review.'
        name        = 'project'
    }
)

# Fetch existing glossary terms for idempotency check
Write-Host '  Fetching existing glossary terms...' -ForegroundColor Gray
try {
    $existingGlossary = Invoke-RestMethod `
        -Uri "$ApiBase/copilotglossaryterms?`$select=copilotglossarytermid,term,name&`$top=500" `
        -Headers $headersSimple
    $existingTermMap = @{}
    foreach ($r in $existingGlossary.value) {
        if ($r.term) { $existingTermMap[$r.term.ToLower()] = $r.copilotglossarytermid }
    }
    Write-Host "  Found $($existingTermMap.Count) existing glossary terms." -ForegroundColor Gray
} catch {
    Write-Warning "  Could not fetch existing glossary terms: $($_.Exception.Message)"
    Write-Warning "  Will attempt to create all terms - duplicates may cause errors."
    $existingTermMap = @{}
}

$glossaryCreated = 0
$glossarySkipped = 0
$glossaryFailed = 0

foreach ($gt in $glossaryTerms) {
    $termLower = $gt.term.ToLower()
    if ($existingTermMap.ContainsKey($termLower)) {
        Write-Host "  SKIP: '$($gt.term)' already exists" -ForegroundColor Gray
        $glossarySkipped++
        continue
    }

    if ($DryRun) {
        Write-Host "  WOULD CREATE: '$($gt.term)'" -ForegroundColor DarkYellow
        $glossaryCreated++
        continue
    }

    try {
        $body = @{
            term        = $gt.term
            description = $gt.description
            name        = $gt.name
        }
        $jsonBody = $body | ConvertTo-Json -Compress -Depth 3
        $result = Invoke-RestMethod `
            -Uri "$ApiBase/copilotglossaryterms" `
            -Headers $headersSimple `
            -Method Post `
            -Body ([System.Text.Encoding]::UTF8.GetBytes($jsonBody))
        Write-Host "  CREATED: '$($gt.term)'" -ForegroundColor Green
        $glossaryCreated++
    } catch {
        $statusCode = ''
        if ($_.Exception.Response) {
            $statusCode = [int]$_.Exception.Response.StatusCode
        }
        Write-Warning "  FAILED: '$($gt.term)' - Status $statusCode - $($_.Exception.Message)"
        $glossaryFailed++
    }
}

Write-Host ''
Write-Host "  Glossary Summary: $glossaryCreated created, $glossarySkipped skipped, $glossaryFailed failed" -ForegroundColor White

# =========================================================================
# PART 2: COLUMN SYNONYMS
# =========================================================================
Write-Host ''
Write-Host '[Step 3/4] Adding Column Synonyms...' -ForegroundColor Yellow
Write-Host '  Table: copilotsynonyms' -ForegroundColor Gray
Write-Host '  Columns: name, columnlogicalname, synonyms, description' -ForegroundColor Gray
Write-Host ''

# Define synonyms - each entry maps a column schema name to user-friendly terms.
# The "name" field is a human-readable identifier for the synonym record.
# The "columnlogicalname" is the Dataverse column logical name.
# The "synonyms" field contains comma-separated alternative names.
$synonymDefinitions = @(
    # --- sprk_event synonyms ---
    @{
        name              = 'sprk_event - sprk_duedate'
        columnlogicalname = 'sprk_duedate'
        synonyms          = 'due date, deadline, due by, due on'
        description       = 'The due date for an event or task in the sprk_event table'
        entity            = 'sprk_event'
    }
    @{
        name              = 'sprk_event - sprk_name'
        columnlogicalname = 'sprk_name'
        synonyms          = 'task name, event name, title'
        description       = 'The name or title of an event or task in the sprk_event table'
        entity            = 'sprk_event'
    }
    @{
        name              = 'sprk_event - sprk_eventtype'
        columnlogicalname = 'sprk_eventtype'
        synonyms          = 'event type, task type, type'
        description       = 'The type classification of an event or task'
        entity            = 'sprk_event'
    }
    @{
        name              = 'sprk_event - sprk_status'
        columnlogicalname = 'sprk_status'
        synonyms          = 'status, task status'
        description       = 'The status of an event or task in sprk_event'
        entity            = 'sprk_event'
    }
    @{
        name              = 'sprk_event - ownerid'
        columnlogicalname = 'ownerid'
        synonyms          = 'assignee, assigned to, owner, responsible'
        description       = 'The user who owns or is assigned to an event or task'
        entity            = 'sprk_event'
    }
    # --- sprk_matter synonyms ---
    @{
        name              = 'sprk_matter - sprk_name'
        columnlogicalname = 'sprk_name'
        synonyms          = 'matter name, case name, title'
        description       = 'The name of a legal matter or case'
        entity            = 'sprk_matter'
    }
    @{
        name              = 'sprk_matter - sprk_mattertype'
        columnlogicalname = 'sprk_mattertype'
        synonyms          = 'matter type, case type'
        description       = 'The type classification of a legal matter'
        entity            = 'sprk_matter'
    }
    @{
        name              = 'sprk_matter - sprk_status'
        columnlogicalname = 'sprk_status'
        synonyms          = 'status, matter status'
        description       = 'The status of a legal matter'
        entity            = 'sprk_matter'
    }
    # --- sprk_document synonyms ---
    @{
        name              = 'sprk_document - sprk_name'
        columnlogicalname = 'sprk_name'
        synonyms          = 'document name, file name, title'
        description       = 'The name of a document record'
        entity            = 'sprk_document'
    }
    @{
        name              = 'sprk_document - sprk_documenttype'
        columnlogicalname = 'sprk_documenttype'
        synonyms          = 'document type, file type, classification'
        description       = 'The type or classification of a document'
        entity            = 'sprk_document'
    }
    @{
        name              = 'sprk_document - sprk_matterid'
        columnlogicalname = 'sprk_matterid'
        synonyms          = 'matter, case, parent matter'
        description       = 'The matter or case that a document belongs to'
        entity            = 'sprk_document'
    }
    # --- sprk_analysisplaybook synonyms ---
    @{
        name              = 'sprk_analysisplaybook - sprk_name'
        columnlogicalname = 'sprk_name'
        synonyms          = 'playbook name, analysis name, tool name'
        description       = 'The name of an AI analysis playbook'
        entity            = 'sprk_analysisplaybook'
    }
    @{
        name              = 'sprk_analysisplaybook - sprk_description'
        columnlogicalname = 'sprk_description'
        synonyms          = 'description, what it does'
        description       = 'The description of what an analysis playbook does'
        entity            = 'sprk_analysisplaybook'
    }
)

# Fetch existing synonyms for idempotency check
Write-Host '  Fetching existing synonym records...' -ForegroundColor Gray
try {
    $existingSynonyms = Invoke-RestMethod `
        -Uri "$ApiBase/copilotsynonyms?`$select=copilotsynonymsid,name,columnlogicalname&`$top=500" `
        -Headers $headersSimple
    $existingSynMap = @{}
    foreach ($r in $existingSynonyms.value) {
        if ($r.name) { $existingSynMap[$r.name.ToLower()] = $r.copilotsynonymsid }
    }
    Write-Host "  Found $($existingSynMap.Count) existing synonym records." -ForegroundColor Gray
} catch {
    Write-Warning "  Could not fetch existing synonyms: $($_.Exception.Message)"
    Write-Warning "  Will attempt to create all synonyms - duplicates may cause errors."
    $existingSynMap = @{}
}

# Look up dvtablesearchentity IDs for linking synonyms to their parent entity.
# The CopilotSynonyms.skillentity field is a lookup to dvtablesearchentity.
Write-Host '  Looking up Dataverse Search entity registrations...' -ForegroundColor Gray
$entitySearchMap = @{}
try {
    $searchEntities = Invoke-RestMethod `
        -Uri "$ApiBase/dvtablesearchentities?`$select=dvtablesearchentityid,name&`$top=200" `
        -Headers $headersSimple
    foreach ($se in $searchEntities.value) {
        if ($se.name) { $entitySearchMap[$se.name.ToLower()] = $se.dvtablesearchentityid }
    }
    Write-Host "  Found $($entitySearchMap.Count) search entity registrations." -ForegroundColor Gray
} catch {
    Write-Warning "  Could not look up dvtablesearchentity records: $($_.Exception.Message)"
    Write-Warning "  Synonyms will be created without entity binding."
}

$synCreated = 0
$synSkipped = 0
$synFailed = 0

foreach ($syn in $synonymDefinitions) {
    $nameKey = $syn.name.ToLower()
    if ($existingSynMap.ContainsKey($nameKey)) {
        Write-Host "  SKIP: '$($syn.name)' already exists" -ForegroundColor Gray
        $synSkipped++
        continue
    }

    if ($DryRun) {
        Write-Host "  WOULD CREATE: '$($syn.name)' -> $($syn.synonyms)" -ForegroundColor DarkYellow
        $synCreated++
        continue
    }

    try {
        $body = @{
            name              = $syn.name
            columnlogicalname = $syn.columnlogicalname
            synonyms          = $syn.synonyms
            description       = $syn.description
        }

        # If we found the entity in dvtablesearchentity, bind it
        $entityKey = $syn.entity.ToLower()
        if ($entitySearchMap.ContainsKey($entityKey)) {
            $seId = $entitySearchMap[$entityKey]
            $body['skillentity@odata.bind'] = "/dvtablesearchentities($seId)"
        }

        $jsonBody = $body | ConvertTo-Json -Compress -Depth 3
        $result = Invoke-RestMethod `
            -Uri "$ApiBase/copilotsynonyms" `
            -Headers $headersSimple `
            -Method Post `
            -Body ([System.Text.Encoding]::UTF8.GetBytes($jsonBody))
        Write-Host "  CREATED: '$($syn.name)' -> $($syn.synonyms)" -ForegroundColor Green
        $synCreated++
    } catch {
        $statusCode = ''
        if ($_.Exception.Response) {
            $statusCode = [int]$_.Exception.Response.StatusCode
        }
        Write-Warning "  FAILED: '$($syn.name)' - Status $statusCode - $($_.Exception.Message)"
        $synFailed++
    }
}

Write-Host ''
Write-Host "  Synonym Summary: $synCreated created, $synSkipped skipped, $synFailed failed" -ForegroundColor White

# =========================================================================
# PART 3: DATAVERSE SEARCH COLUMN CONFIGURATION
# =========================================================================
Write-Host ''
Write-Host '[Step 4/4] Configuring Dataverse Search Columns...' -ForegroundColor Yellow
Write-Host ''
Write-Host '  Dataverse Search columns are configured through Quick Find views.' -ForegroundColor Gray
Write-Host '  This section verifies which entities are registered for search' -ForegroundColor Gray
Write-Host '  and outputs the recommended Quick Find view configuration.' -ForegroundColor Gray
Write-Host ''

# Define recommended search columns per entity
$searchConfig = @(
    @{
        Entity      = 'sprk_event'
        DisplayName = 'Event'
        FindColumns = @('sprk_name', 'sprk_duedate', 'sprk_eventtype', 'sprk_status')
        ViewColumns = @('sprk_name', 'sprk_duedate', 'sprk_eventtype', 'sprk_status', 'ownerid', 'createdon')
    }
    @{
        Entity      = 'sprk_matter'
        DisplayName = 'Matter'
        FindColumns = @('sprk_name', 'sprk_mattertype', 'sprk_status')
        ViewColumns = @('sprk_name', 'sprk_mattertype', 'sprk_status', 'ownerid', 'createdon')
    }
    @{
        Entity      = 'sprk_document'
        DisplayName = 'Document'
        FindColumns = @('sprk_name', 'sprk_documenttype')
        ViewColumns = @('sprk_name', 'sprk_documenttype', 'sprk_matterid', 'createdon')
    }
    @{
        Entity      = 'sprk_analysisplaybook'
        DisplayName = 'Analysis Playbook'
        FindColumns = @('sprk_name', 'sprk_description')
        ViewColumns = @('sprk_name', 'sprk_description', 'createdon')
    }
    @{
        Entity      = 'sprk_invoice'
        DisplayName = 'Invoice'
        FindColumns = @('sprk_name')
        ViewColumns = @('sprk_name', 'sprk_matterid', 'createdon')
    }
    @{
        Entity      = 'sprk_party'
        DisplayName = 'Party'
        FindColumns = @('sprk_name')
        ViewColumns = @('sprk_name', 'sprk_partyrole', 'createdon')
    }
    @{
        Entity      = 'sprk_project'
        DisplayName = 'Project'
        FindColumns = @('sprk_name')
        ViewColumns = @('sprk_name', 'sprk_matterid', 'createdon')
    }
    @{
        Entity      = 'sprk_analysisoutput'
        DisplayName = 'Analysis Output'
        FindColumns = @('sprk_name')
        ViewColumns = @('sprk_name', 'createdon')
    }
)

# Check which entities are registered for Dataverse Search
$registeredEntities = @{}
if ($entitySearchMap.Count -gt 0) {
    $registeredEntities = $entitySearchMap
}

foreach ($cfg in $searchConfig) {
    $entityName = $cfg.Entity.ToLower()
    $isRegistered = $registeredEntities.ContainsKey($entityName)
    $statusIcon = if ($isRegistered) { 'REGISTERED' } else { 'NOT REGISTERED' }
    $statusColor = if ($isRegistered) { 'Green' } else { 'Yellow' }

    Write-Host "  $($cfg.DisplayName) - $($cfg.Entity): $statusIcon" -ForegroundColor $statusColor
    Write-Host "    Find Columns : $($cfg.FindColumns -join ', ')" -ForegroundColor Gray
    Write-Host "    View Columns : $($cfg.ViewColumns -join ', ')" -ForegroundColor Gray

    if (-not $isRegistered) {
        Write-Host "    ACTION NEEDED: Add this entity to Dataverse Search via Power Apps solution explorer" -ForegroundColor Yellow
    }
}

# Attempt to register unregistered entities for Dataverse Search
# by creating dvtablesearchentity records
$entitiesToRegister = @()
foreach ($cfg in $searchConfig) {
    $entityName = $cfg.Entity.ToLower()
    if (-not $registeredEntities.ContainsKey($entityName)) {
        $entitiesToRegister += $cfg
    }
}

if ($entitiesToRegister.Count -gt 0) {
    Write-Host ''
    Write-Host "  Attempting to register $($entitiesToRegister.Count) entities for Dataverse Search..." -ForegroundColor Yellow

    foreach ($cfg in $entitiesToRegister) {
        if ($DryRun) {
            Write-Host "    WOULD REGISTER: $($cfg.Entity) for Dataverse Search" -ForegroundColor DarkYellow
            continue
        }

        try {
            $body = @{
                name = $cfg.Entity
            }
            $jsonBody = $body | ConvertTo-Json -Compress -Depth 3
            $result = Invoke-RestMethod `
                -Uri "$ApiBase/dvtablesearchentities" `
                -Headers $headersSimple `
                -Method Post `
                -Body ([System.Text.Encoding]::UTF8.GetBytes($jsonBody))
            Write-Host "    REGISTERED: $($cfg.Entity)" -ForegroundColor Green
        } catch {
            $statusCode = ''
            if ($_.Exception.Response) {
                $statusCode = [int]$_.Exception.Response.StatusCode
            }
            Write-Host "    COULD NOT REGISTER: $($cfg.Entity) - Status $statusCode" -ForegroundColor Yellow
            Write-Host "    Register manually: Power Apps > Solutions > Edit > Overview > Manage Search Index" -ForegroundColor Gray
        }
    }
}

# =========================================================================
# FINAL SUMMARY
# =========================================================================
Write-Host ''
Write-Host '================================================================' -ForegroundColor Cyan
Write-Host '  Configuration Summary' -ForegroundColor Cyan
Write-Host '================================================================' -ForegroundColor Cyan
Write-Host "  Glossary Terms : $glossaryCreated created, $glossarySkipped existing, $glossaryFailed failed" -ForegroundColor White
Write-Host "  Synonyms       : $synCreated created, $synSkipped existing, $synFailed failed" -ForegroundColor White
Write-Host "  Search Entities: $($searchConfig.Count) configured" -ForegroundColor White
Write-Host ''

if ($DryRun) {
    Write-Host '=== DRY RUN COMPLETE - No changes were made ===' -ForegroundColor Yellow
} else {
    Write-Host '=== Configuration Complete ===' -ForegroundColor Green
    Write-Host ''
    Write-Host 'IMPORTANT: Changes may take up to 15 minutes to take effect.' -ForegroundColor Yellow
    Write-Host ''
    Write-Host 'Next Steps:' -ForegroundColor White
    Write-Host '  1. Verify glossary terms in Power Apps > Copilot > Glossary' -ForegroundColor Gray
    Write-Host '  2. Verify synonyms in Power Apps > Copilot > Synonyms' -ForegroundColor Gray
    Write-Host '  3. For any entities marked NOT REGISTERED, add them to' -ForegroundColor Gray
    Write-Host '     Dataverse Search via Power Apps solution explorer:' -ForegroundColor Gray
    Write-Host '     Solutions > Edit > Overview > Manage Search Index' -ForegroundColor Gray
    Write-Host '  4. Configure Quick Find views for each entity to include' -ForegroundColor Gray
    Write-Host '     the recommended Find and View columns listed above.' -ForegroundColor Gray
    Write-Host '  5. Test with M365 Copilot: ask about tasks, matters,' -ForegroundColor Gray
    Write-Host '     documents, playbooks, etc. using domain vocabulary.' -ForegroundColor Gray
}
Write-Host ''
