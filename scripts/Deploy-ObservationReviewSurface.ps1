<#
.SYNOPSIS
    Deploys task 052 (D-P11 view) Observation review surface to Dataverse (Spaarke Dev).

.DESCRIPTION
    Adds three columns to existing sprk_analysis entity + a new global option set
    + a model-driven view filtered to the Insights-Observation discriminator
    (sprk_searchprofile = 'insights-observation@v1', task 051) AND
    sprk_disposition = PendingReview. Reviewers see only sampled, unprocessed
    Observations in the "Insights Observations - Review Queue" view.

    Schema additions (sprk_analysis):
      - sprk_disposition  Picklist  -> global option set sprk_observationdisposition
      - sprk_dispositionnote Memo 2000 (optional)
      - sprk_reviewdate   DateTime (DateAndTime, optional)

    Global option set (sprk_observationdisposition):
      - 100000000 = Pending Review (default for sampled Observations)
      - 100000001 = Correct
      - 100000002 = Incorrect
      - 100000003 = Unclear

    Model-driven view:
      - Name:   Insights Observations - Review Queue
      - Entity: sprk_analysis
      - Filter: sprk_searchprofile = 'insights-observation@v1'
                AND sprk_disposition = 100000000 (PendingReview)
      - Sort:   createdon descending
      - Columns: createdon, sprk_name, sprk_documentid, sprk_workingdocument,
                 sprk_disposition, sprk_reviewdate, ownerid

    All components added to spaarke_insights solution.

    The solution is then exported and unpacked to src/solutions/spaarke_insights/
    via a separate "pac solution" step (handled by Step 9 of this script).

    Per task 052 (D-P11 view) of the Spaarke Insights Engine Phase 1 project.

.PARAMETER EnvironmentUrl
    The Dataverse environment URL (default: https://spaarkedev1.crm.dynamics.com)

.PARAMETER SolutionName
    The Dataverse solution unique name (default: spaarke_insights)

.PARAMETER SkipSolutionExport
    Skip the final pac solution export + unpack step.

.EXAMPLE
    .\Deploy-ObservationReviewSurface.ps1

.NOTES
    Project: ai-spaarke-insights-engine-r1
    Task:    052 - D-P11 (view) Observation review surface
    Created: 2026-05-28
#>

param(
    [Parameter(Mandatory = $false)]
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com",

    [Parameter(Mandatory = $false)]
    [string]$SolutionName = "spaarke_insights",

    [Parameter(Mandatory = $false)]
    [switch]$SkipSolutionExport
)

$ErrorActionPreference = "Stop"

# ============================================================================
# Helper Functions (mirror task 011 Deploy-PrecedentEntity.ps1 patterns)
# ============================================================================

function Get-DataverseToken {
    param([string]$EnvironmentUrl)
    Write-Host "Getting authentication token from Azure CLI..." -ForegroundColor Cyan
    $tokenResult = az account get-access-token --resource $EnvironmentUrl --query "accessToken" -o tsv 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to get token from Azure CLI. Error: $tokenResult. Run 'az login' first."
    }
    return $tokenResult.Trim()
}

function Invoke-DataverseApi {
    param(
        [string]$Token,
        [string]$BaseUrl,
        [string]$Endpoint,
        [string]$Method = "GET",
        [object]$Body = $null,
        [hashtable]$ExtraHeaders = @{}
    )

    $headers = @{
        "Authorization"    = "Bearer $Token"
        "OData-MaxVersion" = "4.0"
        "OData-Version"    = "4.0"
        "Accept"           = "application/json"
        "Content-Type"     = "application/json; charset=utf-8"
        "Prefer"           = "odata.include-annotations=*"
    }
    foreach ($k in $ExtraHeaders.Keys) { $headers[$k] = $ExtraHeaders[$k] }

    $uri = "$BaseUrl/api/data/v9.2/$Endpoint"
    $params = @{ Uri = $uri; Method = $Method; Headers = $headers }
    if ($Body) { $params.Body = ($Body | ConvertTo-Json -Depth 25) }

    try {
        return Invoke-RestMethod @params
    }
    catch {
        $errorDetails = $_.Exception.Message
        if ($_.ErrorDetails.Message) {
            $errorJson = $_.ErrorDetails.Message | ConvertFrom-Json -ErrorAction SilentlyContinue
            if ($errorJson.error.message) { $errorDetails = $errorJson.error.message }
        }
        throw "API Error ($Method $Endpoint): $errorDetails"
    }
}

function New-Label {
    param([string]$Text)
    return @{
        "@odata.type"     = "Microsoft.Dynamics.CRM.Label"
        "LocalizedLabels" = @(
            @{
                "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                "Label"       = $Text
                "LanguageCode" = 1033
            }
        )
    }
}

function Test-AttributeExists {
    param([string]$Token, [string]$BaseUrl, [string]$EntityLogicalName, [string]$AttributeLogicalName)
    try {
        Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
            -Endpoint "EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes(LogicalName='$AttributeLogicalName')" -Method "GET" | Out-Null
        return $true
    } catch { return $false }
}

function Get-GlobalOptionSet {
    param([string]$Token, [string]$BaseUrl, [string]$Name)
    try {
        return Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
            -Endpoint "GlobalOptionSetDefinitions(Name='$Name')" -Method "GET"
    } catch { return $null }
}

function Test-SavedQueryExists {
    param([string]$Token, [string]$BaseUrl, [string]$Name, [string]$EntityLogicalName)
    try {
        # Get entity object type code first
        $entityMeta = Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
            -Endpoint "EntityDefinitions(LogicalName='$EntityLogicalName')?`$select=ObjectTypeCode" -Method "GET"
        $otc = $entityMeta.ObjectTypeCode

        $q = Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
            -Endpoint "savedqueries?`$filter=name eq '$Name' and returnedtypecode eq $otc&`$select=savedqueryid,name" -Method "GET"
        if ($q.value.Count -gt 0) { return $q.value[0] }
        return $null
    } catch { return $null }
}

function Add-SolutionComponent {
    param(
        [string]$Token, [string]$BaseUrl,
        [string]$ComponentId,
        [int]$ComponentType,
        [string]$SolutionUniqueName,
        [bool]$AddRequiredComponents = $false
    )
    try {
        $body = @{
            "ComponentId"           = $ComponentId
            "ComponentType"         = $ComponentType
            "SolutionUniqueName"    = $SolutionUniqueName
            "AddRequiredComponents" = $AddRequiredComponents
        }
        Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl -Endpoint "AddSolutionComponent" -Method "POST" -Body $body | Out-Null
        Write-Host "    Added component $ComponentId (type $ComponentType) to $SolutionUniqueName" -ForegroundColor Green
    } catch {
        Write-Host "    Warning: AddSolutionComponent failed for $ComponentId (type $ComponentType): $($_.Exception.Message)" -ForegroundColor Yellow
    }
}

# ============================================================================
# Main
# ============================================================================

Write-Host ""
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host " Deploy Observation Review Surface (Task 052 / D-P11)" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl" -ForegroundColor Yellow
Write-Host "Solution   : $SolutionName" -ForegroundColor Yellow
Write-Host ""

$token = Get-DataverseToken -EnvironmentUrl $EnvironmentUrl
Write-Host "Authentication successful" -ForegroundColor Green
Write-Host ""

# ---------------------------------------------------------------------------
# Step 1: Create global option set sprk_observationdisposition
# ---------------------------------------------------------------------------
Write-Host "Step 1: Create global option set sprk_observationdisposition..." -ForegroundColor Cyan
$dispOptionSet = Get-GlobalOptionSet -Token $token -BaseUrl $EnvironmentUrl -Name "sprk_observationdisposition"
if ($dispOptionSet) {
    Write-Host "  Option set sprk_observationdisposition already exists." -ForegroundColor Green
} else {
    $opts = @(
        @{ "Value" = 100000000; "Label" = (New-Label "Pending Review") }
        @{ "Value" = 100000001; "Label" = (New-Label "Correct") }
        @{ "Value" = 100000002; "Label" = (New-Label "Incorrect") }
        @{ "Value" = 100000003; "Label" = (New-Label "Unclear") }
    )
    $osDef = @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.OptionSetMetadata"
        "Name"          = "sprk_observationdisposition"
        "DisplayName"   = (New-Label "Observation Disposition")
        "Description"   = (New-Label "QA disposition for Insights Observation review queue (task 052 / D-P11). PendingReview is set automatically by the sampling logic in DataverseObservationMirror; the remaining values are set by reviewers via the model-driven view.")
        "IsGlobal"      = $true
        "OptionSetType" = "Picklist"
        "Options"       = $opts
    }
    Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
        -Endpoint "GlobalOptionSetDefinitions" -Method "POST" -Body $osDef `
        -ExtraHeaders @{ "MSCRM.SolutionUniqueName" = $SolutionName } | Out-Null
    Write-Host "  Created sprk_observationdisposition (4 values)" -ForegroundColor Green
    $dispOptionSet = Get-GlobalOptionSet -Token $token -BaseUrl $EnvironmentUrl -Name "sprk_observationdisposition"
}
$dispOptionSetId = $dispOptionSet.MetadataId
Write-Host "  Option set MetadataId: $dispOptionSetId" -ForegroundColor Gray
Write-Host ""

# ---------------------------------------------------------------------------
# Step 2: Add disposition + note + review-date columns to sprk_analysis
# ---------------------------------------------------------------------------
Write-Host "Step 2: Add columns to sprk_analysis..." -ForegroundColor Cyan

$attributes = @(
    # sprk_disposition - Picklist (global option set sprk_observationdisposition)
    @{
        "name" = "sprk_disposition"
        "def"  = @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
            "SchemaName"    = "sprk_Disposition"
            "RequiredLevel" = @{ "Value" = "None" }
            "DisplayName"   = (New-Label "Disposition")
            "Description"   = (New-Label "QA disposition: PendingReview (set by sampling) / Correct / Incorrect / Unclear (set by reviewer). Filters the Insights Observations review queue view.")
            "GlobalOptionSet@odata.bind" = "/GlobalOptionSetDefinitions($dispOptionSetId)"
        }
    },
    # sprk_dispositionnote - Memo 2000 optional
    @{
        "name" = "sprk_dispositionnote"
        "def"  = @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
            "SchemaName"    = "sprk_DispositionNote"
            "RequiredLevel" = @{ "Value" = "None" }
            "MaxLength"     = 2000
            "DisplayName"   = (New-Label "Disposition Note")
            "Description"   = (New-Label "Reviewer free-text comment explaining the disposition decision (optional). Used for prompt-iteration loop and SME calibration.")
            "Format"        = "TextArea"
        }
    },
    # sprk_reviewdate - DateTime (DateAndTime, optional)
    @{
        "name" = "sprk_reviewdate"
        "def"  = @{
            "@odata.type"        = "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata"
            "SchemaName"         = "sprk_ReviewDate"
            "RequiredLevel"      = @{ "Value" = "None" }
            "DisplayName"        = (New-Label "Review Date")
            "Description"        = (New-Label "Timestamp when the reviewer set the disposition (UTC). Set by the model-driven form on save.")
            "Format"             = "DateAndTime"
            "DateTimeBehavior"   = @{ "Value" = "UserLocal" }
        }
    }
)

foreach ($a in $attributes) {
    if (Test-AttributeExists -Token $token -BaseUrl $EnvironmentUrl -EntityLogicalName "sprk_analysis" -AttributeLogicalName $a.name) {
        Write-Host "  Attribute $($a.name) already exists, skipping." -ForegroundColor Yellow
    } else {
        Write-Host "  Adding attribute: $($a.name)..." -ForegroundColor Gray
        Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
            -Endpoint "EntityDefinitions(LogicalName='sprk_analysis')/Attributes" -Method "POST" -Body $a.def `
            -ExtraHeaders @{ "MSCRM.SolutionUniqueName" = $SolutionName } | Out-Null
        Write-Host "    Added: $($a.name)" -ForegroundColor Green
    }
}
Write-Host ""

# ---------------------------------------------------------------------------
# Step 3: Add sprk_analysis to the spaarke_insights solution (idempotent)
# ---------------------------------------------------------------------------
Write-Host "Step 3: Add sprk_analysis to solution '$SolutionName'..." -ForegroundColor Cyan
$entityMeta = Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
    -Endpoint "EntityDefinitions(LogicalName='sprk_analysis')?`$select=MetadataId,ObjectTypeCode" -Method "GET"
Add-SolutionComponent -Token $token -BaseUrl $EnvironmentUrl `
    -ComponentId $entityMeta.MetadataId -ComponentType 1 -SolutionUniqueName $SolutionName -AddRequiredComponents $false
Add-SolutionComponent -Token $token -BaseUrl $EnvironmentUrl `
    -ComponentId $dispOptionSetId -ComponentType 9 -SolutionUniqueName $SolutionName -AddRequiredComponents $false
Write-Host ""

# ---------------------------------------------------------------------------
# Step 4: Create model-driven view "Insights Observations - Review Queue"
# ---------------------------------------------------------------------------
Write-Host "Step 4: Create model-driven view..." -ForegroundColor Cyan
$viewName = "Insights Observations - Review Queue"
$entityOtc = $entityMeta.ObjectTypeCode

$existingView = Test-SavedQueryExists -Token $token -BaseUrl $EnvironmentUrl -Name $viewName -EntityLogicalName "sprk_analysis"
if ($existingView) {
    Write-Host "  View '$viewName' already exists (savedqueryid=$($existingView.savedqueryid))" -ForegroundColor Yellow
    $viewId = $existingView.savedqueryid
} else {
    # FetchXML defining the review queue:
    #   Filter:   sprk_searchprofile = 'insights-observation@v1'
    #             AND sprk_disposition = 100000000 (PendingReview)
    #   Sort:     createdon DESC
    #   Columns:  createdon, sprk_name, sprk_documentid (link),
    #             sprk_workingdocument (verbatim quote), sprk_disposition,
    #             sprk_reviewdate, ownerid
    $fetchXml = @"
<fetch version="1.0" output-format="xml-platform" mapping="logical" distinct="false">
  <entity name="sprk_analysis">
    <attribute name="sprk_analysisid" />
    <attribute name="createdon" />
    <attribute name="sprk_name" />
    <attribute name="sprk_documentid" />
    <attribute name="sprk_workingdocument" />
    <attribute name="sprk_disposition" />
    <attribute name="sprk_reviewdate" />
    <attribute name="ownerid" />
    <order attribute="createdon" descending="true" />
    <filter type="and">
      <condition attribute="sprk_searchprofile" operator="eq" value="insights-observation@v1" />
      <condition attribute="sprk_disposition" operator="eq" value="100000000" />
    </filter>
  </entity>
</fetch>
"@

    # LayoutXML defining column widths + the lookup link rendering
    $layoutXml = @"
<grid name="resultset" object="$entityOtc" jump="sprk_name" select="1" preview="1" icon="1">
  <row name="result" id="sprk_analysisid">
    <cell name="createdon" width="140" />
    <cell name="sprk_name" width="220" />
    <cell name="sprk_documentid" width="200" />
    <cell name="sprk_workingdocument" width="280" />
    <cell name="sprk_disposition" width="120" />
    <cell name="sprk_reviewdate" width="140" />
    <cell name="ownerid" width="120" />
  </row>
</grid>
"@

    $viewDef = @{
        "name"             = $viewName
        "description"      = "Insights Observations review queue (task 052 / D-P11). Shows sampled Observations from the universal ingest pipeline awaiting SME disposition. Filtered to sprk_searchprofile='insights-observation@v1' AND sprk_disposition=PendingReview."
        "returnedtypecode" = "sprk_analysis"
        "querytype"        = 0  # 0 = Public view
        "fetchxml"         = $fetchXml
        "layoutxml"        = $layoutXml
    }

    $createResp = Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
        -Endpoint "savedqueries" -Method "POST" -Body $viewDef `
        -ExtraHeaders @{
            "MSCRM.SolutionUniqueName" = $SolutionName
            "Prefer"                   = "return=representation"
        }
    $viewId = $createResp.savedqueryid
    Write-Host "  Created view '$viewName' (savedqueryid=$viewId)" -ForegroundColor Green
}
Write-Host ""

# ---------------------------------------------------------------------------
# Step 5: Add view to solution explicitly (Component Type 26 = SavedQuery)
# ---------------------------------------------------------------------------
Write-Host "Step 5: Add view to solution..." -ForegroundColor Cyan
Add-SolutionComponent -Token $token -BaseUrl $EnvironmentUrl `
    -ComponentId $viewId -ComponentType 26 -SolutionUniqueName $SolutionName -AddRequiredComponents $false
Write-Host ""

# ---------------------------------------------------------------------------
# Step 6: Publish customizations
# ---------------------------------------------------------------------------
Write-Host "Step 6: Publish customizations..." -ForegroundColor Cyan
$publishXml = @{
    "ParameterXml" = "<importexportxml><entities><entity>sprk_analysis</entity></entities></importexportxml>"
}
try {
    Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl -Endpoint "PublishXml" -Method "POST" -Body $publishXml | Out-Null
    Write-Host "  Customizations published." -ForegroundColor Green
} catch {
    Write-Host "  Publish warning (may have timed out): $($_.Exception.Message)" -ForegroundColor Yellow
}
Write-Host ""

# ---------------------------------------------------------------------------
# Step 7: Verify the new columns + view via Web API
# ---------------------------------------------------------------------------
Write-Host "Step 7: Verify deployment via Web API..." -ForegroundColor Cyan
$verifyEntity = Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
    -Endpoint "EntityDefinitions(LogicalName='sprk_analysis')?`$expand=Attributes(`$filter=LogicalName eq 'sprk_disposition' or LogicalName eq 'sprk_dispositionnote' or LogicalName eq 'sprk_reviewdate';`$select=LogicalName,AttributeType)" -Method "GET"

Write-Host "  sprk_analysis attributes (new):" -ForegroundColor Green
foreach ($attr in ($verifyEntity.Attributes | Sort-Object LogicalName)) {
    Write-Host "    - $($attr.LogicalName) ($($attr.AttributeType))" -ForegroundColor Gray
}

$verifyView = Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
    -Endpoint "savedqueries($viewId)?`$select=name,returnedtypecode,querytype" -Method "GET"
Write-Host "  View   : $($verifyView.name)" -ForegroundColor Green
Write-Host "  Entity : $($verifyView.returnedtypecode)" -ForegroundColor Green
Write-Host "  Type   : $($verifyView.querytype) (0=Public)" -ForegroundColor Green
Write-Host ""

# ---------------------------------------------------------------------------
# Step 8: Optional - export solution + unpack to src/solutions/spaarke_insights
# ---------------------------------------------------------------------------
if (-not $SkipSolutionExport) {
    Write-Host "Step 8: Export + unpack solution to src/solutions/spaarke_insights/..." -ForegroundColor Cyan
    Write-Host "  Note: requires 'pac' CLI authenticated to $EnvironmentUrl" -ForegroundColor Gray

    $repoRoot = (Resolve-Path "$PSScriptRoot\..").Path
    $solutionDir = Join-Path $repoRoot "src\solutions\spaarke_insights"
    $tmpZip = Join-Path $env:TEMP "$SolutionName-task052-export.zip"

    if (-not (Test-Path $solutionDir)) {
        Write-Host "  Solution directory not found: $solutionDir" -ForegroundColor Red
        Write-Host "  Skipping unpack." -ForegroundColor Yellow
    } else {
        try {
            Write-Host "  Running: pac solution export --name $SolutionName --path $tmpZip --overwrite" -ForegroundColor Gray
            pac solution export --name $SolutionName --path $tmpZip --overwrite | Out-Null
            if ($LASTEXITCODE -ne 0) {
                Write-Host "  pac solution export failed (exit $LASTEXITCODE)" -ForegroundColor Yellow
            } else {
                Write-Host "  Exported to $tmpZip" -ForegroundColor Green

                Write-Host "  Running: pac solution unpack --zipfile $tmpZip --folder $solutionDir --allowDelete" -ForegroundColor Gray
                pac solution unpack --zipfile $tmpZip --folder $solutionDir --allowDelete | Out-Null
                if ($LASTEXITCODE -ne 0) {
                    Write-Host "  pac solution unpack failed (exit $LASTEXITCODE) - manual unpack required" -ForegroundColor Yellow
                } else {
                    Write-Host "  Unpacked to $solutionDir" -ForegroundColor Green
                    Write-Host "  Review with: git status src/solutions/spaarke_insights/" -ForegroundColor Gray
                }
            }
        } catch {
            Write-Host "  pac solution operation failed: $($_.Exception.Message)" -ForegroundColor Yellow
            Write-Host "  You can run pac solution export/unpack manually." -ForegroundColor Gray
        }
    }
    Write-Host ""
}

# ---------------------------------------------------------------------------
# Done
# ---------------------------------------------------------------------------
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host " DEPLOYMENT COMPLETE" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "Entity   : sprk_analysis (+3 columns)" -ForegroundColor Green
Write-Host "OptionSet: sprk_observationdisposition (4 values)" -ForegroundColor Green
Write-Host "View     : Insights Observations - Review Queue" -ForegroundColor Green
Write-Host "Solution : $SolutionName" -ForegroundColor Green
Write-Host ""
Write-Host "Next:" -ForegroundColor Yellow
Write-Host "  - Verify in Power Apps Maker Portal: $EnvironmentUrl" -ForegroundColor Gray
Write-Host "  - Open the view in any model-driven app referencing sprk_analysis" -ForegroundColor Gray
Write-Host "  - To configure sampling rate (default 100% Phase 1):" -ForegroundColor Gray
Write-Host "      Insights:Mirror:SamplingPercentage in appsettings.json / Key Vault" -ForegroundColor Gray
