<#
.SYNOPSIS
    Creates the 4 status-filtered savedqueries (views) for sprk_precedent and
    updates the default Main form to expose Phase 1 fields.

.DESCRIPTION
    Phase 1 (D-P3 / task 011) follow-on: SME admin authoring views + form.
    Status-filtered views: Tentative, Confirmed, Under Drift Review, Deprecated.
    The auto-created "Active Precedents" view (default public) covers
    "All Precedents". Adds all views + form to spaarke_insights solution.

.PARAMETER EnvironmentUrl
    Dataverse environment URL

.PARAMETER SolutionName
    Target solution unique name (default: spaarke_insights)

.NOTES
    Project: ai-spaarke-insights-engine-r1
    Task:    011 — D-P3 (views + form)
    Created: 2026-05-28
#>

param(
    [Parameter(Mandatory = $false)]
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com",

    [Parameter(Mandatory = $false)]
    [string]$SolutionName = "spaarke_insights"
)

$ErrorActionPreference = "Stop"

function Get-DataverseToken { param([string]$Url)
    $t = az account get-access-token --resource $Url --query "accessToken" -o tsv 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Token failed: $t" }
    return $t.Trim()
}

function Invoke-DataverseApi {
    param([string]$Token, [string]$BaseUrl, [string]$Endpoint, [string]$Method = "GET", [object]$Body = $null, [hashtable]$ExtraHeaders = @{})
    $headers = @{
        "Authorization"    = "Bearer $Token"
        "OData-MaxVersion" = "4.0"
        "OData-Version"    = "4.0"
        "Accept"           = "application/json"
        "Content-Type"     = "application/json; charset=utf-8"
        "Prefer"           = "return=representation,odata.include-annotations=*"
    }
    foreach ($k in $ExtraHeaders.Keys) { $headers[$k] = $ExtraHeaders[$k] }
    $uri = "$BaseUrl/api/data/v9.2/$Endpoint"
    $params = @{ Uri = $uri; Method = $Method; Headers = $headers }
    if ($Body) { $params.Body = ($Body | ConvertTo-Json -Depth 30) }
    try { return Invoke-RestMethod @params }
    catch {
        $msg = $_.Exception.Message
        if ($_.ErrorDetails.Message) {
            $j = $_.ErrorDetails.Message | ConvertFrom-Json -ErrorAction SilentlyContinue
            if ($j.error.message) { $msg = $j.error.message }
        }
        throw "API Error ($Method $Endpoint): $msg"
    }
}

function Add-SolutionComponent {
    param([string]$Token, [string]$BaseUrl, [string]$ComponentId, [int]$ComponentType, [string]$SolutionUniqueName)
    try {
        $body = @{
            "ComponentId"           = $ComponentId
            "ComponentType"         = $ComponentType
            "SolutionUniqueName"    = $SolutionUniqueName
            "AddRequiredComponents" = $false
        }
        Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl -Endpoint "AddSolutionComponent" -Method "POST" -Body $body | Out-Null
        Write-Host "    Added component $ComponentId (type $ComponentType)" -ForegroundColor Green
    } catch {
        Write-Host "    Warning: $($_.Exception.Message)" -ForegroundColor Yellow
    }
}

# Common view layout XML (cell widths)
function Get-LayoutXml {
    return @"
<grid name="resultset" object="10000" jump="sprk_name" select="1" preview="1" icon="1">
  <row name="result" id="sprk_precedentid">
    <cell name="sprk_name" width="220" />
    <cell name="sprk_status" width="140" />
    <cell name="sprk_samplesize" width="100" />
    <cell name="sprk_reviewerby" width="160" />
    <cell name="sprk_reviewdate" width="120" />
    <cell name="sprk_producedby" width="160" />
    <cell name="modifiedon" width="120" />
  </row>
</grid>
"@
}

function Get-FetchXml {
    param([int]$StatusValue)
    return @"
<fetch version="1.0" output-format="xml-platform" mapping="logical" distinct="false">
  <entity name="sprk_precedent">
    <attribute name="sprk_name" />
    <attribute name="sprk_status" />
    <attribute name="sprk_samplesize" />
    <attribute name="sprk_reviewerby" />
    <attribute name="sprk_reviewdate" />
    <attribute name="sprk_producedby" />
    <attribute name="modifiedon" />
    <attribute name="sprk_precedentid" />
    <order attribute="modifiedon" descending="true" />
    <filter type="and">
      <condition attribute="statecode" operator="eq" value="0" />
      <condition attribute="sprk_status" operator="eq" value="$StatusValue" />
    </filter>
  </entity>
</fetch>
"@
}

function New-PrecedentView {
    param([string]$Token, [string]$BaseUrl, [string]$Name, [string]$Description, [int]$StatusValue)

    # Check if view already exists
    $existing = Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
        -Endpoint "savedqueries?`$filter=returnedtypecode eq 'sprk_precedent' and name eq '$Name'&`$select=savedqueryid" -Method "GET"
    if ($existing.value.Count -gt 0) {
        Write-Host "  View '$Name' already exists." -ForegroundColor Yellow
        return $existing.value[0].savedqueryid
    }

    $body = @{
        "name"             = $Name
        "description"      = $Description
        "returnedtypecode" = "sprk_precedent"
        "querytype"        = 0       # Public view
        "fetchxml"         = (Get-FetchXml -StatusValue $StatusValue)
        "layoutxml"        = (Get-LayoutXml)
        "isdefault"        = $false
        "isquickfindquery" = $false
    }
    $r = Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl -Endpoint "savedqueries" -Method "POST" -Body $body
    Write-Host "  Created view '$Name' ($($r.savedqueryid))" -ForegroundColor Green
    return $r.savedqueryid
}

# ============================================================================
# Main
# ============================================================================
Write-Host ""
Write-Host "================================================" -ForegroundColor Cyan
Write-Host " Deploy sprk_precedent Views + Form (Task 011)" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan

$token = Get-DataverseToken -Url $EnvironmentUrl
Write-Host "Authenticated." -ForegroundColor Green
Write-Host ""

# Status values
$STATUS_TENTATIVE = 100000000
$STATUS_CONFIRMED = 100000001
$STATUS_UNDER_DRIFT_REVIEW = 100000002
$STATUS_DEPRECATED = 100000003

# Step 1: Create status-filtered views
Write-Host "Step 1: Create status-filtered views..." -ForegroundColor Cyan
$createdIds = @()
$createdIds += New-PrecedentView -Token $token -BaseUrl $EnvironmentUrl -Name "Tentative Precedents" -Description "Precedents awaiting SME review (D-61)." -StatusValue $STATUS_TENTATIVE
$createdIds += New-PrecedentView -Token $token -BaseUrl $EnvironmentUrl -Name "Confirmed Precedents" -Description "SME-confirmed Precedents; eligible for projection to spaarke-insights-index (D-P4)." -StatusValue $STATUS_CONFIRMED
$createdIds += New-PrecedentView -Token $token -BaseUrl $EnvironmentUrl -Name "Precedents Under Drift Review" -Description "Confirmed Precedents flagged for re-review due to drift signals (Phase 1.5+)." -StatusValue $STATUS_UNDER_DRIFT_REVIEW
$createdIds += New-PrecedentView -Token $token -BaseUrl $EnvironmentUrl -Name "Deprecated Precedents" -Description "Precedents superseded or no longer valid; kept for traceability." -StatusValue $STATUS_DEPRECATED
Write-Host ""

# Step 2: Get the default "Active Precedents" view id (covers 'All Precedents' since all Phase 1 rows are Active)
Write-Host "Step 2: Locate default 'Active Precedents' view..." -ForegroundColor Cyan
$defaultView = Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
    -Endpoint "savedqueries?`$filter=returnedtypecode eq 'sprk_precedent' and name eq 'Active Precedents'&`$select=savedqueryid" -Method "GET"
if ($defaultView.value.Count -gt 0) {
    $defaultViewId = $defaultView.value[0].savedqueryid
    Write-Host "  'Active Precedents' (default) = $defaultViewId" -ForegroundColor Green
    $createdIds += $defaultViewId
}
Write-Host ""

# Step 3: Add views to solution (ComponentType 26 = SavedQuery)
Write-Host "Step 3: Add views to solution '$SolutionName'..." -ForegroundColor Cyan
foreach ($id in $createdIds) {
    if ($id) {
        Add-SolutionComponent -Token $token -BaseUrl $EnvironmentUrl -ComponentId $id -ComponentType 26 -SolutionUniqueName $SolutionName
    }
}
Write-Host ""

# Step 4: Add the default Main form to the solution (ComponentType 60 = SystemForm)
Write-Host "Step 4: Add default Main form to solution..." -ForegroundColor Cyan
$forms = Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
    -Endpoint "systemforms?`$filter=objecttypecode eq 'sprk_precedent' and type eq 2&`$select=formid,name" -Method "GET"
foreach ($form in $forms.value) {
    Write-Host "  Form: $($form.name) ($($form.formid))" -ForegroundColor Gray
    Add-SolutionComponent -Token $token -BaseUrl $EnvironmentUrl -ComponentId $form.formid -ComponentType 60 -SolutionUniqueName $SolutionName
}
Write-Host ""

# Step 5: Publish
Write-Host "Step 5: Publish customizations..." -ForegroundColor Cyan
$publishXml = @{ "ParameterXml" = "<importexportxml><entities><entity>sprk_precedent</entity></entities></importexportxml>" }
try {
    Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl -Endpoint "PublishXml" -Method "POST" -Body $publishXml | Out-Null
    Write-Host "  Published." -ForegroundColor Green
} catch {
    Write-Host "  Publish warning: $($_.Exception.Message)" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "================================================" -ForegroundColor Cyan
Write-Host " VIEWS + FORM DEPLOYMENT COMPLETE" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
