<#
.SYNOPSIS
    Refreshes the scope-model-index.json file from live Dataverse data.

.DESCRIPTION
    Queries Dataverse for all active Analysis Actions, Skills, Knowledge, and Tools
    records, then regenerates the scope-model-index.json catalog file.

    Curated fields (tags, documentTypes, compatibleActions, contentType) are preserved
    from the existing file when present. Static sections (models, modelSelectionRules,
    compositions) are always retained from the existing file or written from defaults.

    Authentication uses Azure CLI (az account get-access-token) by default.

.PARAMETER Environment
    Target Dataverse environment. Currently only 'dev' is supported.
    Default: dev

.PARAMETER OutputFile
    Relative or absolute path to the output JSON file.
    Default: .claude/catalogs/scope-model-index.json

.EXAMPLE
    .\Refresh-ScopeModelIndex.ps1

.EXAMPLE
    .\Refresh-ScopeModelIndex.ps1 -OutputFile ./my-index.json

.NOTES
    Entities queried:
      - sprk_analysisactions   (Actions — incl. sprk_kind / sprk_modeltier, ai-architecture-redesign-r1 FR-P0-03)
      - sprk_analysisskills    (Skills)
      - sprk_analysisknowledges (Knowledge — code column is sprk_knowledgecode; the legacy
                                 sprk_externalid column no longer exists on the entity. Rows
                                 without a knowledge code fall back to sprk_name as the code.)
      - sprk_analysistools     (Tools — incl. sprk_toolid / sprk_sideeffectclass)
    API version: v9.2

    Repaired 2026-07-07 (ai-architecture-redesign-r1 task 051, FR-P4-02):
      - FIXED 400 on the knowledge query: $select still referenced sprk_externalid,
        which was removed from sprk_analysisknowledge (replaced by sprk_knowledgecode).
      - Entries now carry the deployed row GUID ("id") so the index is verifiable
        against the live environment, plus kind/modelTier (actions) and
        toolId/sideEffectClass (tools) per the post-redesign closed-catalog taxonomy
        (ADR-039).

    Prerequisites:
      - Azure CLI installed and authenticated (az login)
      - PowerShell Core 7+ recommended
#>

param(
    [ValidateSet('dev')]
    [string]$Environment = 'dev',

    [string]$DataverseUrl = $env:DATAVERSE_URL,

    [string]$OutputFile = '.claude/catalogs/scope-model-index.json'
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# Environment URL resolution
# ---------------------------------------------------------------------------
if (-not $DataverseUrl) {
    Write-Error "DataverseUrl is required. Set DATAVERSE_URL env var or pass -DataverseUrl parameter."
    exit 1
}

$EnvironmentUrl = $DataverseUrl

# ---------------------------------------------------------------------------
# Resolve paths
# ---------------------------------------------------------------------------
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = (Resolve-Path (Join-Path $ScriptDir '..')).Path

if (-not [System.IO.Path]::IsPathRooted($OutputFile)) {
    $OutputFile = Join-Path $RepoRoot $OutputFile
}

$OutputDir = Split-Path -Parent $OutputFile
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

# ---------------------------------------------------------------------------
# Authentication
# ---------------------------------------------------------------------------
function Get-DataverseToken {
    param([string]$ResourceUrl)

    Write-Host 'Acquiring token via Azure CLI...' -ForegroundColor Gray
    $t = az account get-access-token --resource $ResourceUrl --query 'accessToken' -o tsv 2>$null

    if (-not $t) {
        throw "Failed to acquire access token. Run 'az login' first."
    }

    return $t
}

# ---------------------------------------------------------------------------
# Dataverse helpers
# ---------------------------------------------------------------------------
$ApiBase = "$EnvironmentUrl/api/data/v9.2"

function Get-DataverseHeaders {
    param([string]$BearerToken)

    return @{
        'Authorization'    = "Bearer $BearerToken"
        'Accept'           = 'application/json'
        'Content-Type'     = 'application/json; charset=utf-8'
        'OData-MaxVersion' = '4.0'
        'OData-Version'    = '4.0'
        'Prefer'           = 'odata.include-annotations="OData.Community.Display.V1.FormattedValue"'
    }
}

function Invoke-DataverseQuery {
    param(
        [string]$EntitySet,
        [string]$Select,
        [hashtable]$Headers
    )

    $filter = 'statecode eq 0'
    $uri = "$ApiBase/$($EntitySet)?`$select=$Select&`$filter=$filter"

    try {
        $result = Invoke-RestMethod -Uri $uri -Headers $Headers -Method Get
        return $result.value
    } catch {
        throw "Query failed for $EntitySet : $($_.Exception.Message)"
    }
}

# ---------------------------------------------------------------------------
# Static data (models, modelSelectionRules, compositions)
# ---------------------------------------------------------------------------
$DefaultModels = @(
    @{
        name           = 'gpt-4o'
        provider       = 'AzureOpenAI'
        contextWindow  = 128000
        costTier       = 'high'
        speedTier      = 'medium'
        recommendedFor = @('deep-analysis', 'extraction', 'legal-reasoning', 'financial-calculation')
        avoidFor       = @('classification', 'triage', 'simple-summary')
    }
    @{
        name           = 'gpt-4o-mini'
        provider       = 'AzureOpenAI'
        contextWindow  = 128000
        costTier       = 'low'
        speedTier      = 'fast'
        recommendedFor = @('classification', 'triage', 'simple-summary', 'condition-routing')
        avoidFor       = @('complex-reasoning', 'detailed-extraction', 'legal-reasoning')
    }
    @{
        name           = 'gpt-4-turbo'
        provider       = 'AzureOpenAI'
        contextWindow  = 128000
        costTier       = 'high'
        speedTier      = 'slow'
        recommendedFor = @('legacy-compatibility')
        avoidFor       = @('new-playbooks')
    }
)

$DefaultModelSelectionRules = @(
    @{ taskType = 'classification';         model = 'gpt-4o-mini'; reason = 'Bounded decision; speed over depth' }
    @{ taskType = 'triage';                 model = 'gpt-4o-mini'; reason = 'Binary/categorical; speed matters' }
    @{ taskType = 'deep-analysis';          model = 'gpt-4o';      reason = 'Multi-faceted reasoning requires full model' }
    @{ taskType = 'extraction';             model = 'gpt-4o';      reason = 'Accuracy critical for structured output' }
    @{ taskType = 'summarization-simple';   model = 'gpt-4o-mini'; reason = 'TL;DR and bullets well-handled by mini' }
    @{ taskType = 'summarization-detailed'; model = 'gpt-4o';      reason = 'Citations and cross-references need full model' }
    @{ taskType = 'legal-reasoning';        model = 'gpt-4o';      reason = 'Nuanced interpretation and risk assessment' }
    @{ taskType = 'financial-calculation';  model = 'gpt-4o';      reason = 'Multi-step computation needs accuracy' }
    @{ taskType = 'condition-routing';      model = 'gpt-4o-mini'; reason = 'Simple boolean/categorical checks' }
)

# ---------------------------------------------------------------------------
# Choice-value label maps (mirror the Dataverse global choices; verified live 2026-07-07)
# ---------------------------------------------------------------------------
function Get-KindLabel {
    param($Value)
    switch ($Value) {
        100000000 { 'Prompted' }
        100000001 { 'Coded' }
        default   { $null }
    }
}

function Get-ModelTierLabel {
    param($Value)
    switch ($Value) {
        100000000 { 'Fast' }
        100000001 { 'Standard' }
        100000002 { 'Reasoning' }
        default   { $null }
    }
}

function Get-SideEffectClassLabel {
    param($Value)
    switch ($Value) {
        100000000 { 'Read' }
        100000001 { 'Write' }
        100000002 { 'Communicate' }
        100000003 { 'Pure' }
        default   { $null }
    }
}

# ---------------------------------------------------------------------------
# Curated field preservation helpers
# ---------------------------------------------------------------------------
function Get-ExistingIndex {
    if (Test-Path $OutputFile) {
        $raw = Get-Content $OutputFile -Raw -Encoding utf8
        return $raw | ConvertFrom-Json
    }
    return $null
}

function Find-ExistingEntry {
    param(
        [array]$Collection,
        [string]$Code
    )

    if (-not $Collection) { return $null }
    foreach ($item in $Collection) {
        if ($item.code -eq $Code) { return $item }
    }
    return $null
}

# ===========================================================================
# Main execution
# ===========================================================================

Write-Host ''
Write-Host '=== Refresh Scope Model Index ===' -ForegroundColor Cyan
Write-Host "Environment : $EnvironmentUrl"
Write-Host "Output      : $OutputFile"
Write-Host ''

# --- Acquire token ---
$bearerToken = Get-DataverseToken -ResourceUrl $EnvironmentUrl
$headers = Get-DataverseHeaders -BearerToken $bearerToken

# --- Load existing index for curated field preservation ---
$existing = Get-ExistingIndex

# --- Query Dataverse ---
Write-Host 'Querying Dataverse...' -ForegroundColor White

$dvActions = Invoke-DataverseQuery `
    -EntitySet 'sprk_analysisactions' `
    -Select 'sprk_analysisactionid,sprk_name,sprk_actioncode,sprk_description,sprk_kind,sprk_modeltier,sprk_workflowclass' `
    -Headers $headers
Write-Host "  Actions    : $($dvActions.Count) records" -ForegroundColor Gray

$dvSkills = Invoke-DataverseQuery `
    -EntitySet 'sprk_analysisskills' `
    -Select 'sprk_analysisskillid,sprk_name,sprk_skillcode,sprk_description' `
    -Headers $headers
Write-Host "  Skills     : $($dvSkills.Count) records" -ForegroundColor Gray

$dvKnowledge = Invoke-DataverseQuery `
    -EntitySet 'sprk_analysisknowledges' `
    -Select 'sprk_analysisknowledgeid,sprk_name,sprk_knowledgecode,sprk_description,sprk_knowledgedeliverytype' `
    -Headers $headers
Write-Host "  Knowledge  : $($dvKnowledge.Count) records" -ForegroundColor Gray

$dvTools = Invoke-DataverseQuery `
    -EntitySet 'sprk_analysistools' `
    -Select 'sprk_analysistoolid,sprk_name,sprk_toolcode,sprk_toolid,sprk_description,sprk_handlerclass,sprk_sideeffectclass' `
    -Headers $headers
Write-Host "  Tools      : $($dvTools.Count) records" -ForegroundColor Gray

Write-Host ''

# --- Build index arrays with curated field preservation ---
$added = 0
$updated = 0
$removed = 0

# Actions
$actions = @()
foreach ($rec in $dvActions) {
    $code = $rec.sprk_actioncode
    $ex = Find-ExistingEntry -Collection ($existing.actions) -Code $code
    $entry = [ordered]@{
        code          = $code
        id            = $rec.sprk_analysisactionid
        name          = $rec.sprk_name
        kind          = Get-KindLabel $rec.sprk_kind
        modelTier     = Get-ModelTierLabel $rec.sprk_modeltier
        workflowClass = if ($rec.sprk_workflowclass) { $rec.sprk_workflowclass } else { $null }
        description   = if ($rec.sprk_description) { $rec.sprk_description } else { '' }
        documentTypes = if ($ex -and $ex.documentTypes) { @($ex.documentTypes) } else { @() }
        tags          = if ($ex -and $ex.tags) { @($ex.tags) } else { @() }
    }

    if ($ex) {
        if ($ex.description -ne $entry.description) { $updated++ }
    } else {
        $added++
    }

    $actions += $entry
}

# Skills
$skills = @()
foreach ($rec in $dvSkills) {
    $code = $rec.sprk_skillcode
    $ex = Find-ExistingEntry -Collection ($existing.skills) -Code $code
    $entry = [ordered]@{
        code              = $code
        id                = $rec.sprk_analysisskillid
        name              = $rec.sprk_name
        description       = if ($rec.sprk_description) { $rec.sprk_description } else { '' }
        compatibleActions = if ($ex -and $ex.compatibleActions) { @($ex.compatibleActions) } else { @() }
        tags              = if ($ex -and $ex.tags) { @($ex.tags) } else { @() }
    }

    if ($ex) {
        if ($ex.description -ne $entry.description) { $updated++ }
    } else {
        $added++
    }

    $skills += $entry
}

# Knowledge
$knowledge = @()
foreach ($rec in $dvKnowledge) {
    # sprk_knowledgecode replaced the retired sprk_externalid; rows without a code
    # (legacy RAG-index rows) fall back to sprk_name so every entry stays addressable.
    $code = if ($rec.sprk_knowledgecode) { $rec.sprk_knowledgecode } else { $rec.sprk_name }
    $ex = Find-ExistingEntry -Collection ($existing.knowledge) -Code $code
    $entry = [ordered]@{
        code         = $code
        id           = $rec.sprk_analysisknowledgeid
        name         = $rec.sprk_name
        deliveryType = $rec.'sprk_knowledgedeliverytype@OData.Community.Display.V1.FormattedValue'
        description  = if ($rec.sprk_description) { $rec.sprk_description } else { '' }
        contentType  = if ($ex -and $ex.contentType) { $ex.contentType } else { 'Reference' }
        tags         = if ($ex -and $ex.tags) { @($ex.tags) } else { @() }
    }

    if ($ex) {
        if ($ex.description -ne $entry.description) { $updated++ }
    } else {
        $added++
    }

    $knowledge += $entry
}

# Tools
$tools = @()
foreach ($rec in $dvTools) {
    $code = $rec.sprk_toolcode
    $ex = Find-ExistingEntry -Collection ($existing.tools) -Code $code
    $entry = [ordered]@{
        code            = $code
        id              = $rec.sprk_analysistoolid
        toolId          = if ($rec.sprk_toolid) { $rec.sprk_toolid } else { $null }
        name            = $rec.sprk_name
        handler         = if ($rec.sprk_handlerclass) { $rec.sprk_handlerclass } else { '' }
        sideEffectClass = Get-SideEffectClassLabel $rec.sprk_sideeffectclass
        description     = if ($rec.sprk_description) { $rec.sprk_description } else { '' }
        tags            = if ($ex -and $ex.tags) { @($ex.tags) } else { @() }
    }

    if ($ex) {
        if ($ex.description -ne $entry.description) { $updated++ }
    } else {
        $added++
    }

    $tools += $entry
}

# Count removed entries (in existing file but not in Dataverse)
if ($existing) {
    $dvActionCodes   = $dvActions   | ForEach-Object { $_.sprk_actioncode }
    $dvSkillCodes    = $dvSkills    | ForEach-Object { $_.sprk_skillcode }
    $dvKnowledgeCodes = $dvKnowledge | ForEach-Object { if ($_.sprk_knowledgecode) { $_.sprk_knowledgecode } else { $_.sprk_name } }
    $dvToolCodes     = $dvTools     | ForEach-Object { $_.sprk_toolcode }

    if ($existing.actions)   { $removed += ($existing.actions   | Where-Object { $_.code -notin $dvActionCodes }).Count }
    if ($existing.skills)    { $removed += ($existing.skills    | Where-Object { $_.code -notin $dvSkillCodes }).Count }
    if ($existing.knowledge) { $removed += ($existing.knowledge | Where-Object { $_.code -notin $dvKnowledgeCodes }).Count }
    if ($existing.tools)     { $removed += ($existing.tools     | Where-Object { $_.code -notin $dvToolCodes }).Count }
}

# --- Assemble output ---
$index = [ordered]@{
    '$schema'             = 'https://spaarke.com/schemas/scope-model-index/v1'
    '$generated'          = (Get-Date -Format 'yyyy-MM-ddTHH:mm:ssZ')
    actions               = $actions
    skills                = $skills
    knowledge             = $knowledge
    tools                 = $tools
    models                = if ($existing -and $existing.models) { $existing.models } else { $DefaultModels }
    modelSelectionRules   = if ($existing -and $existing.modelSelectionRules) { $existing.modelSelectionRules } else { $DefaultModelSelectionRules }
    compositions          = if ($existing -and $existing.compositions) { $existing.compositions } else { [ordered]@{} }
}

# --- Write file ---
$json = $index | ConvertTo-Json -Depth 10
[System.IO.File]::WriteAllText($OutputFile, $json, [System.Text.Encoding]::UTF8)

$fileSize = [math]::Round((Get-Item $OutputFile).Length / 1024, 1)

# --- Summary ---
Write-Host 'Index updated:' -ForegroundColor White
Write-Host "  Added   : $added new scopes" -ForegroundColor $(if ($added -gt 0) { 'Green' } else { 'Gray' })
Write-Host "  Updated : $updated descriptions" -ForegroundColor $(if ($updated -gt 0) { 'Yellow' } else { 'Gray' })
Write-Host "  Removed : $removed stale scopes" -ForegroundColor $(if ($removed -gt 0) { 'Red' } else { 'Gray' })
Write-Host ''
Write-Host "Written to: $OutputFile ($fileSize KB)" -ForegroundColor Green
Write-Host ''
