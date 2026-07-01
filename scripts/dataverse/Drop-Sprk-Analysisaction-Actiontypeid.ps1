<#
.SYNOPSIS
    Drops the `sprk_actiontypeid` lookup column from `sprk_analysisaction` in
    Dataverse. R7 task 043 / FR-03.

.DESCRIPTION
    spaarke-ai-platform-unification-r7 — Wave 4 schema cleanup.

    After R7 collapses dispatch to single-hop `node.sprk_executortype`
    (Wave 2 tasks 024-028) and deletes `ExecuteAnalysisAsync` (Wave 4 task
    042), the `sprk_actiontypeid` lookup from `sprk_analysisaction` to
    `sprk_analysisactiontype` has no runtime reader. Per FR-03, drop the
    field. The `sprk_analysisactiontype` lookup TABLE itself is preserved
    per FR-05 (decorative maker categorization).

    Pattern mirrors `Add-EntityNameValidatorNodeTypeOption.ps1` (auth + Web
    API call shape), inverted for deletion. PAC CLI does NOT support column
    deletion, so we go through Web API EntityDefinitions(...)/Attributes(...)
    DELETE.

    Idempotency: retrieves the attribute via Web API before deletion; if 404,
    logs "already deleted" and exits 0. Safe to re-run.

.PARAMETER DataverseUrl
    Base Dataverse URL (e.g. `https://spaarkedev1.crm.dynamics.com`).
    Defaults to the `DATAVERSE_URL` env var.

.PARAMETER DryRun
    Preview the operation without making the DELETE call.

.EXAMPLE
    .\Drop-Sprk-Analysisaction-Actiontypeid.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com

.NOTES
    Prerequisites:
      - PowerShell 7+
      - Azure CLI installed and authenticated (`az login`)
      - Access to the target Dataverse environment as System Customizer / Administrator
#>

param(
    [string]$DataverseUrl = $env:DATAVERSE_URL,

    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

$EntityLogicalName    = 'sprk_analysisaction'
$AttributeLogicalName = 'sprk_actiontypeid'

if (-not $DataverseUrl) {
    Write-Error "DataverseUrl is required. Set DATAVERSE_URL env var or pass -DataverseUrl parameter."
    exit 1
}

$EnvironmentUrl = $DataverseUrl.TrimEnd('/')
$ApiBase = "$EnvironmentUrl/api/data/v9.2"

Write-Host ''
Write-Host '=== Drop sprk_analysisaction.sprk_actiontypeid (R7 task 043, FR-03) ===' -ForegroundColor Cyan
Write-Host "Environment      : $EnvironmentUrl"
Write-Host "Entity           : $EntityLogicalName"
Write-Host "Attribute        : $AttributeLogicalName (LOOKUP -> sprk_analysisactiontype)"
if ($DryRun) {
    Write-Host 'Mode             : DRY RUN' -ForegroundColor Yellow
} else {
    Write-Host 'Mode             : LIVE' -ForegroundColor Green
}
Write-Host ''

Write-Host '=== Step 1: Authenticate ===' -ForegroundColor Cyan
try {
    $tokenJson = az account get-access-token --resource $EnvironmentUrl 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        Write-Error "az account get-access-token failed:`n$tokenJson"
        exit 1
    }
    $token = ($tokenJson | ConvertFrom-Json).accessToken
    if (-not $token) {
        Write-Error 'Failed to extract access token from az output.'
        exit 1
    }
    Write-Host '  OK - token acquired' -ForegroundColor Green
} catch {
    Write-Error "Authentication failed: $($_.Exception.Message)"
    exit 1
}

$headers = @{
    'Authorization'    = "Bearer $token"
    'Accept'           = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
}

Write-Host ''
Write-Host '=== Step 2: Verify attribute exists (idempotency pre-check) ===' -ForegroundColor Cyan
$attributeUri = "$ApiBase/EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes(LogicalName='$AttributeLogicalName')"
$attributeExists = $true
try {
    $response = Invoke-WebRequest -Uri $attributeUri -Headers $headers -Method GET -UseBasicParsing
    if ($response.StatusCode -eq 200) {
        $attr = $response.Content | ConvertFrom-Json
        Write-Host "  OK - attribute present (AttributeType=$($attr.AttributeType), MetadataId=$($attr.MetadataId))" -ForegroundColor Green
    }
} catch {
    if ($_.Exception.Response.StatusCode.Value__ -eq 404) {
        Write-Host '  Attribute already absent (404). Nothing to do. Exiting 0.' -ForegroundColor Yellow
        $attributeExists = $false
    } else {
        Write-Error "Pre-check failed: $($_.Exception.Message)"
        exit 1
    }
}

if (-not $attributeExists) {
    exit 0
}

if ($DryRun) {
    Write-Host ''
    Write-Host '=== DRY RUN — would DELETE the attribute now ===' -ForegroundColor Yellow
    Write-Host "  DELETE $attributeUri"
    exit 0
}

Write-Host ''
Write-Host '=== Step 3: Resolve OneToMany relationship (Lookup attributes cascade via RelationshipDefinitions) ===' -ForegroundColor Cyan
# Dataverse Lookup columns are owned by their parent OneToManyRelationship. DELETE on
# /Attributes(...) returns 400 — the supported path is DELETE on
# /EntityDefinitions(...)/ManyToOneRelationships(SchemaName='...'). The cascade removes
# the underlying lookup column.
$relsUri = "$ApiBase/EntityDefinitions(LogicalName='$EntityLogicalName')/ManyToOneRelationships?`$filter=ReferencingAttribute eq '$AttributeLogicalName'&`$select=SchemaName,MetadataId,ReferencedEntity"
try {
    $relsResp = Invoke-RestMethod -Uri $relsUri -Headers $headers -Method GET
    if ($relsResp.value.Count -eq 0) {
        Write-Error "No ManyToOneRelationship found for $EntityLogicalName.$AttributeLogicalName — cannot proceed."
        exit 1
    }
    if ($relsResp.value.Count -gt 1) {
        Write-Error "Multiple relationships matched (count=$($relsResp.value.Count)). Aborting for safety."
        exit 1
    }
    $relSchema = $relsResp.value[0].SchemaName
    $relRefEnt = $relsResp.value[0].ReferencedEntity
    $relMetaId = $relsResp.value[0].MetadataId
    Write-Host "  OK - relationship resolved: SchemaName=$relSchema, ReferencedEntity=$relRefEnt, MetadataId=$relMetaId" -ForegroundColor Green
} catch {
    Write-Error "Relationship lookup failed: $($_.Exception.Message)"
    exit 1
}

Write-Host ''
Write-Host '=== Step 3b: DELETE relationship (cascades to lookup column) ===' -ForegroundColor Cyan
# Use top-level RelationshipDefinitions(MetadataId) — Dataverse rejects DELETE on the
# navigation-property form with "Delete on Navigation Property is only supported on
# Contained Metadata Entities" (0x80060888). The supported form is the direct entity-set
# DELETE keyed by MetadataId.
$relDeleteUri = "$ApiBase/RelationshipDefinitions($relMetaId)"
try {
    Invoke-WebRequest -Uri $relDeleteUri -Headers $headers -Method DELETE -UseBasicParsing | Out-Null
    Write-Host '  OK - DELETE succeeded' -ForegroundColor Green
} catch {
    $body = ''
    try { if ($_.ErrorDetails.Message) { $body = $_.ErrorDetails.Message } } catch {}
    Write-Error "DELETE failed: $($_.Exception.Message)`nBody: $body"
    exit 1
}

Write-Host ''
Write-Host '=== Step 4: PublishXml (refresh entity metadata) ===' -ForegroundColor Cyan
$publishUri = "$ApiBase/PublishXml"
$publishBody = @{
    ParameterXml = "<importexportxml><entities><entity>$EntityLogicalName</entity></entities></importexportxml>"
} | ConvertTo-Json -Depth 3
$publishHeaders = $headers + @{ 'Content-Type' = 'application/json; charset=utf-8' }
try {
    Invoke-WebRequest -Uri $publishUri -Headers $publishHeaders -Method POST -Body $publishBody -UseBasicParsing | Out-Null
    Write-Host '  OK - PublishXml succeeded' -ForegroundColor Green
} catch {
    Write-Warning "PublishXml warning (non-fatal): $($_.Exception.Message)"
}

Write-Host ''
Write-Host '=== Step 5: Verify attribute absent (post-check) ===' -ForegroundColor Cyan
try {
    Invoke-WebRequest -Uri $attributeUri -Headers $headers -Method GET -UseBasicParsing | Out-Null
    Write-Error 'POST-CHECK FAILED: attribute still present after DELETE.'
    exit 1
} catch {
    if ($_.Exception.Response.StatusCode.Value__ -eq 404) {
        Write-Host '  OK - attribute absent (404 as expected)' -ForegroundColor Green
    } else {
        Write-Error "Post-check failed unexpectedly: $($_.Exception.Message)"
        exit 1
    }
}

Write-Host ''
Write-Host '=== DONE — sprk_actiontypeid dropped from sprk_analysisaction ===' -ForegroundColor Green
Write-Host '  FR-03 acceptance signal satisfied.'
Write-Host '  Next: task 044 (drop sprk_executoractiontype INT column).'
