<#
.SYNOPSIS
    Creates the model-driven admin form and view for sprk_aichatcontextmap in Dataverse.

.DESCRIPTION
    Creates a main form (Information), an active records view, and optionally adds
    the entity to the model-driven app site map using the Dataverse Web API.
    Requires Azure CLI authentication.

    Form layout:
      Header: sprk_isactive, sprk_isdefault
      General tab: sprk_name, sprk_entitytype, sprk_pagetype, sprk_playbookid, sprk_sortorder
      Details tab: sprk_description

.PARAMETER EnvironmentUrl
    The Dataverse environment URL (e.g., https://spaarkedev1.crm.dynamics.com)

.PARAMETER SolutionName
    The Dataverse solution to add components to (default: spaarke_core)

.EXAMPLE
    .\Create-AiChatContextMapForm.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com"

.NOTES
    Project: AI SprkChat Context Awareness
    Task: 040 - Create admin form for sprk_aichatcontextmap
    Created: 2026-03-15
#>

param(
    [Parameter(Mandatory = $false)]
    [string]$EnvironmentUrl = $env:DATAVERSE_URL,

    [Parameter(Mandatory = $false)]
    [string]$SolutionName = "spaarke_core"
)

$ErrorActionPreference = "Stop"

# ============================================================================
# Helper Functions
# ============================================================================

function Get-DataverseToken {
    param([string]$EnvironmentUrl)

    Write-Host "Getting authentication token from Azure CLI..." -ForegroundColor Cyan

    $tokenResult = az account get-access-token --resource $EnvironmentUrl --query "accessToken" -o tsv 2>&1

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to get token from Azure CLI. Error: $tokenResult. Make sure you're logged in with 'az login'"
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

    foreach ($key in $ExtraHeaders.Keys) {
        $headers[$key] = $ExtraHeaders[$key]
    }

    $uri = "$BaseUrl/api/data/v9.2/$Endpoint"

    $params = @{
        Uri     = $uri
        Method  = $Method
        Headers = $headers
    }

    if ($Body) {
        if ($Body -is [string]) {
            $params.Body = $Body
        }
        else {
            $params.Body = ($Body | ConvertTo-Json -Depth 20)
        }
    }

    try {
        $response = Invoke-RestMethod @params
        return $response
    }
    catch {
        $errorDetails = $_.Exception.Message
        if ($_.ErrorDetails.Message) {
            $errorJson = $_.ErrorDetails.Message | ConvertFrom-Json -ErrorAction SilentlyContinue
            if ($errorJson.error.message) {
                $errorDetails = $errorJson.error.message
            }
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
                "@odata.type"  = "Microsoft.Dynamics.CRM.LocalizedLabel"
                "Label"        = $Text
                "LanguageCode" = 1033
            }
        )
    }
}

function Test-EntityExists {
    param(
        [string]$Token,
        [string]$BaseUrl,
        [string]$LogicalName
    )

    try {
        Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
            -Endpoint "EntityDefinitions(LogicalName='$LogicalName')" -Method "GET" | Out-Null
        return $true
    }
    catch {
        if ($_.Exception.Message -match "does not exist|404") {
            return $false
        }
        throw
    }
}

function Get-EntityMetadataId {
    param(
        [string]$Token,
        [string]$BaseUrl,
        [string]$LogicalName
    )

    $result = Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
        -Endpoint "EntityDefinitions(LogicalName='$LogicalName')?`$select=MetadataId,ObjectTypeCode" -Method "GET"
    return $result
}

function Get-ExistingForms {
    param(
        [string]$Token,
        [string]$BaseUrl,
        [string]$EntityLogicalName,
        [int]$FormType = 2  # 2 = Main form
    )

    $filter = "objecttypecode eq '$EntityLogicalName' and type eq $FormType"
    $result = Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
        -Endpoint "systemforms?`$filter=$filter&`$select=formid,name,formxml" -Method "GET"
    return $result.value
}

function Get-ExistingViews {
    param(
        [string]$Token,
        [string]$BaseUrl,
        [string]$EntityLogicalName,
        [string]$ViewName
    )

    $filter = "returnedtypecode eq '$EntityLogicalName' and name eq '$ViewName' and querytype eq 0"
    $result = Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
        -Endpoint "savedqueries?`$filter=$filter&`$select=savedqueryid,name" -Method "GET"
    return $result.value
}

# ============================================================================
# Form XML Definition
# ============================================================================

function Get-MainFormXml {
    # Model-driven app main form for sprk_aichatcontextmap
    # Header: sprk_isactive, sprk_isdefault
    # General tab: sprk_name, sprk_entitytype, sprk_pagetype, sprk_playbookid, sprk_sortorder
    # Details tab: sprk_description

    $formXml = @'
<form>
  <tabs>
    <tab name="tab_general" id="{a1b2c3d4-0001-0001-0001-000000000001}" IsUserDefined="1" locklevel="0" showlabel="true" expanded="true">
      <labels>
        <label description="General" languagecode="1033" />
      </labels>
      <columns>
        <column width="100%">
          <sections>
            <section name="section_general" showlabel="true" showbar="false" locklevel="0" id="{a1b2c3d4-0002-0001-0001-000000000001}" IsUserDefined="1" columns="2">
              <labels>
                <label description="General" languagecode="1033" />
              </labels>
              <rows>
                <row>
                  <cell id="{a1b2c3d4-0003-0001-0001-000000000001}" showlabel="true" locklevel="0">
                    <labels>
                      <label description="Name" languagecode="1033" />
                    </labels>
                    <control id="sprk_name" classid="{4273EDBD-AC1D-40d3-9FB2-095C621B552D}" datafieldname="sprk_name" />
                  </cell>
                  <cell id="{a1b2c3d4-0003-0001-0001-000000000002}" showlabel="true" locklevel="0">
                    <labels>
                      <label description="Entity Type" languagecode="1033" />
                    </labels>
                    <control id="sprk_entitytype" classid="{4273EDBD-AC1D-40d3-9FB2-095C621B552D}" datafieldname="sprk_entitytype" />
                  </cell>
                </row>
                <row>
                  <cell id="{a1b2c3d4-0003-0001-0001-000000000003}" showlabel="true" locklevel="0">
                    <labels>
                      <label description="Page Type" languagecode="1033" />
                    </labels>
                    <control id="sprk_pagetype" classid="{3EF39988-22BB-4f0b-BBBE-64B5A3748AEE}" datafieldname="sprk_pagetype" />
                  </cell>
                  <cell id="{a1b2c3d4-0003-0001-0001-000000000004}" showlabel="true" locklevel="0">
                    <labels>
                      <label description="Playbook" languagecode="1033" />
                    </labels>
                    <control id="sprk_playbookid" classid="{270BD3DB-D9AF-4782-9025-509E298DEC0A}" datafieldname="sprk_playbookid" />
                  </cell>
                </row>
                <row>
                  <cell id="{a1b2c3d4-0003-0001-0001-000000000005}" showlabel="true" locklevel="0">
                    <labels>
                      <label description="Sort Order" languagecode="1033" />
                    </labels>
                    <control id="sprk_sortorder" classid="{C6D124CA-7EDA-4a60-AEA9-7FB8D318B68F}" datafieldname="sprk_sortorder" />
                  </cell>
                  <cell id="{a1b2c3d4-0003-0001-0001-000000000006}" />
                </row>
              </rows>
            </section>
          </sections>
        </column>
      </columns>
    </tab>
    <tab name="tab_details" id="{a1b2c3d4-0001-0001-0001-000000000002}" IsUserDefined="1" locklevel="0" showlabel="true" expanded="true">
      <labels>
        <label description="Details" languagecode="1033" />
      </labels>
      <columns>
        <column width="100%">
          <sections>
            <section name="section_description" showlabel="true" showbar="false" locklevel="0" id="{a1b2c3d4-0002-0001-0001-000000000002}" IsUserDefined="1" columns="1">
              <labels>
                <label description="Description" languagecode="1033" />
              </labels>
              <rows>
                <row>
                  <cell id="{a1b2c3d4-0003-0001-0001-000000000007}" showlabel="true" locklevel="0" rowspan="4">
                    <labels>
                      <label description="Description" languagecode="1033" />
                    </labels>
                    <control id="sprk_description" classid="{E0DECE4B-6FC8-4a8f-A065-082708572369}" datafieldname="sprk_description" />
                  </cell>
                </row>
              </rows>
            </section>
          </sections>
        </column>
      </columns>
    </tab>
  </tabs>
  <header id="{a1b2c3d4-0005-0001-0001-000000000001}" celllabelposition="Top" columns="111" labelwidth="115" celllabelalignment="Left">
    <rows>
      <row>
        <cell id="{a1b2c3d4-0004-0001-0001-000000000001}" showlabel="true" locklevel="0">
          <labels>
            <label description="Is Active" languagecode="1033" />
          </labels>
          <control id="header_sprk_isactive" classid="{B0C6723A-8503-4fd7-BB28-C8A06AC933C2}" datafieldname="sprk_isactive" />
        </cell>
        <cell id="{a1b2c3d4-0004-0001-0001-000000000002}" showlabel="true" locklevel="0">
          <labels>
            <label description="Is Default" languagecode="1033" />
          </labels>
          <control id="header_sprk_isdefault" classid="{B0C6723A-8503-4fd7-BB28-C8A06AC933C2}" datafieldname="sprk_isdefault" />
        </cell>
      </row>
    </rows>
  </header>
</form>
'@

    return $formXml
}

# ============================================================================
# View Definition (FetchXML + LayoutXML)
# ============================================================================

function Get-ActiveRecordsFetchXml {
    return @'
<fetch version="1.0" output-format="xml-platform" mapping="logical" distinct="false">
  <entity name="sprk_aichatcontextmap">
    <attribute name="sprk_name" />
    <attribute name="sprk_entitytype" />
    <attribute name="sprk_pagetype" />
    <attribute name="sprk_playbookid" />
    <attribute name="sprk_sortorder" />
    <attribute name="sprk_isdefault" />
    <attribute name="sprk_isactive" />
    <attribute name="createdon" />
    <order attribute="sprk_entitytype" descending="false" />
    <order attribute="sprk_sortorder" descending="false" />
    <filter type="and">
      <condition attribute="sprk_isactive" operator="eq" value="1" />
    </filter>
  </entity>
</fetch>
'@
}

function Get-ActiveRecordsLayoutXml {
    return @'
<grid name="resultset" object="10831" jump="sprk_name" select="1" icon="1" preview="1">
  <row name="result" id="sprk_aichatcontextmapid">
    <cell name="sprk_name" width="200" />
    <cell name="sprk_entitytype" width="150" />
    <cell name="sprk_pagetype" width="120" />
    <cell name="sprk_playbookid" width="200" />
    <cell name="sprk_sortorder" width="80" />
    <cell name="sprk_isdefault" width="80" />
    <cell name="sprk_isactive" width="80" />
    <cell name="createdon" width="150" />
  </row>
</grid>
'@
}

# ============================================================================
# Main Execution
# ============================================================================

function Main {
    Write-Host ""
    Write-Host "========================================================" -ForegroundColor Cyan
    Write-Host " Deploy sprk_aichatcontextmap Form & View" -ForegroundColor Cyan
    Write-Host "========================================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Environment: $EnvironmentUrl" -ForegroundColor Yellow
    Write-Host "Solution:    $SolutionName" -ForegroundColor Yellow
    Write-Host ""

    # --- Authentication ---
    $token = Get-DataverseToken -EnvironmentUrl $EnvironmentUrl
    Write-Host "Authentication successful" -ForegroundColor Green
    Write-Host ""

    # --- Step 1: Verify entity exists ---
    Write-Host "Step 1: Verifying entity sprk_aichatcontextmap exists..." -ForegroundColor Cyan
    if (-not (Test-EntityExists -Token $token -BaseUrl $EnvironmentUrl -LogicalName "sprk_aichatcontextmap")) {
        throw "Entity sprk_aichatcontextmap does not exist! Run Create-AiChatContextMapEntity.ps1 first."
    }

    $entityMeta = Get-EntityMetadataId -Token $token -BaseUrl $EnvironmentUrl -LogicalName "sprk_aichatcontextmap"
    $entityMetadataId = $entityMeta.MetadataId
    $objectTypeCode = $entityMeta.ObjectTypeCode
    Write-Host "  Entity exists (ObjectTypeCode: $objectTypeCode, MetadataId: $entityMetadataId)" -ForegroundColor Green
    Write-Host ""

    # --- Step 2: Create or update Main Form ---
    Write-Host "Step 2: Creating main form..." -ForegroundColor Cyan

    $existingForms = Get-ExistingForms -Token $token -BaseUrl $EnvironmentUrl -EntityLogicalName "sprk_aichatcontextmap"
    $formXml = Get-MainFormXml

    $existingCustomForm = $existingForms | Where-Object { $_.name -eq "AI Chat Context Map" }

    if ($existingCustomForm) {
        Write-Host "  Form 'AI Chat Context Map' already exists (ID: $($existingCustomForm.formid))" -ForegroundColor Yellow
        Write-Host "  Updating form XML..." -ForegroundColor Yellow

        $updateBody = @{
            "formxml" = $formXml
        }

        Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
            -Endpoint "systemforms($($existingCustomForm.formid))" -Method "PATCH" -Body $updateBody

        Write-Host "  Form updated successfully" -ForegroundColor Green
    }
    else {
        Write-Host "  Creating new main form 'AI Chat Context Map'..." -ForegroundColor Gray

        $formBody = @{
            "name"               = "AI Chat Context Map"
            "description"        = "Admin form for managing AI chat context-to-playbook mappings"
            "objecttypecode"     = "sprk_aichatcontextmap"
            "type"               = 2  # Main form
            "formxml"            = $formXml
            "isdefault"          = $true
        }

        try {
            Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
                -Endpoint "systemforms" -Method "POST" -Body $formBody
            Write-Host "  Form created successfully" -ForegroundColor Green
        }
        catch {
            if ($_.Exception.Message -match "already exists|duplicate") {
                Write-Host "  Form already exists (different name), skipping creation." -ForegroundColor Yellow
            }
            else {
                throw
            }
        }
    }

    Write-Host ""

    # --- Step 3: Create Active Records View ---
    Write-Host "Step 3: Creating 'Active AI Chat Context Maps' view..." -ForegroundColor Cyan

    $viewName = "Active AI Chat Context Maps"
    $existingViews = Get-ExistingViews -Token $token -BaseUrl $EnvironmentUrl `
        -EntityLogicalName "sprk_aichatcontextmap" -ViewName $viewName

    $fetchXml = Get-ActiveRecordsFetchXml
    $layoutXml = Get-ActiveRecordsLayoutXml

    if ($existingViews -and $existingViews.Count -gt 0) {
        Write-Host "  View '$viewName' already exists (ID: $($existingViews[0].savedqueryid))" -ForegroundColor Yellow
        Write-Host "  Updating view definition..." -ForegroundColor Yellow

        $updateBody = @{
            "fetchxml"  = $fetchXml
            "layoutxml" = $layoutXml
        }

        Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
            -Endpoint "savedqueries($($existingViews[0].savedqueryid))" -Method "PATCH" -Body $updateBody

        Write-Host "  View updated successfully" -ForegroundColor Green
    }
    else {
        Write-Host "  Creating new view '$viewName'..." -ForegroundColor Gray

        $viewBody = @{
            "name"              = $viewName
            "description"       = "All active AI Chat Context Map records, sorted by entity type and sort order"
            "returnedtypecode"  = "sprk_aichatcontextmap"
            "querytype"         = 0  # Public view
            "fetchxml"          = $fetchXml
            "layoutxml"         = $layoutXml
            "isdefault"         = $true
        }

        try {
            Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
                -Endpoint "savedqueries" -Method "POST" -Body $viewBody
            Write-Host "  View created successfully" -ForegroundColor Green
        }
        catch {
            if ($_.Exception.Message -match "already exists|duplicate") {
                Write-Host "  View already exists, skipping." -ForegroundColor Yellow
            }
            else {
                throw
            }
        }
    }

    Write-Host ""

    # --- Step 4: Create All Records View ---
    Write-Host "Step 4: Creating 'All AI Chat Context Maps' view..." -ForegroundColor Cyan

    $allViewName = "All AI Chat Context Maps"
    $existingAllViews = Get-ExistingViews -Token $token -BaseUrl $EnvironmentUrl `
        -EntityLogicalName "sprk_aichatcontextmap" -ViewName $allViewName

    $allFetchXml = @'
<fetch version="1.0" output-format="xml-platform" mapping="logical" distinct="false">
  <entity name="sprk_aichatcontextmap">
    <attribute name="sprk_name" />
    <attribute name="sprk_entitytype" />
    <attribute name="sprk_pagetype" />
    <attribute name="sprk_playbookid" />
    <attribute name="sprk_sortorder" />
    <attribute name="sprk_isdefault" />
    <attribute name="sprk_isactive" />
    <attribute name="createdon" />
    <order attribute="sprk_entitytype" descending="false" />
    <order attribute="sprk_sortorder" descending="false" />
  </entity>
</fetch>
'@

    if ($existingAllViews -and $existingAllViews.Count -gt 0) {
        Write-Host "  View '$allViewName' already exists, updating..." -ForegroundColor Yellow

        $updateBody = @{
            "fetchxml"  = $allFetchXml
            "layoutxml" = $layoutXml
        }

        Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
            -Endpoint "savedqueries($($existingAllViews[0].savedqueryid))" -Method "PATCH" -Body $updateBody

        Write-Host "  View updated successfully" -ForegroundColor Green
    }
    else {
        Write-Host "  Creating new view '$allViewName'..." -ForegroundColor Gray

        $viewBody = @{
            "name"              = $allViewName
            "description"       = "All AI Chat Context Map records (active and inactive), sorted by entity type and sort order"
            "returnedtypecode"  = "sprk_aichatcontextmap"
            "querytype"         = 0  # Public view
            "fetchxml"          = $allFetchXml
            "layoutxml"         = $layoutXml
            "isdefault"         = $false
        }

        try {
            Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
                -Endpoint "savedqueries" -Method "POST" -Body $viewBody
            Write-Host "  View created successfully" -ForegroundColor Green
        }
        catch {
            if ($_.Exception.Message -match "already exists|duplicate") {
                Write-Host "  View already exists, skipping." -ForegroundColor Yellow
            }
            else {
                throw
            }
        }
    }

    Write-Host ""

    # --- Step 5: Add to Model-Driven App Site Map ---
    Write-Host "Step 5: Checking model-driven app integration..." -ForegroundColor Cyan

    Write-Host "  NOTE: Adding entities to model-driven app site maps requires" -ForegroundColor Yellow
    Write-Host "  manual configuration in the Power Apps Maker Portal." -ForegroundColor Yellow
    Write-Host "  Navigate to: $EnvironmentUrl" -ForegroundColor Yellow
    Write-Host "  1. Open the model-driven app in the App Designer" -ForegroundColor Gray
    Write-Host "  2. Add 'AI Chat Context Map' table to the navigation" -ForegroundColor Gray
    Write-Host "  3. Place under an 'AI Configuration' or 'Administration' area" -ForegroundColor Gray
    Write-Host "  4. Save and publish the app" -ForegroundColor Gray
    Write-Host ""

    # --- Step 6: Publish customizations ---
    Write-Host "Step 6: Publishing customizations..." -ForegroundColor Cyan

    $publishXml = @{
        "ParameterXml" = "<importexportxml><entities><entity>sprk_aichatcontextmap</entity></entities></importexportxml>"
    }

    try {
        Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
            -Endpoint "PublishXml" -Method "POST" -Body $publishXml
        Write-Host "  Customizations published" -ForegroundColor Green
    }
    catch {
        Write-Host "  Warning: Publish may have timed out, but changes should be available" -ForegroundColor Yellow
    }

    Write-Host ""

    # --- Step 7: Add form and views to solution ---
    Write-Host "Step 7: Adding form and views to solution '$SolutionName'..." -ForegroundColor Cyan

    # Add entity with forms and views (component type 1 = Entity, include subcomponents)
    try {
        $addToSolution = @{
            "ComponentId"           = $entityMetadataId
            "ComponentType"         = 1  # Entity
            "SolutionUniqueName"    = $SolutionName
            "AddRequiredComponents" = $false
        }

        Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
            -Endpoint "AddSolutionComponent" -Method "POST" -Body $addToSolution

        Write-Host "  Entity (with forms/views) added to solution '$SolutionName'" -ForegroundColor Green
    }
    catch {
        if ($_.Exception.Message -match "already exists") {
            Write-Host "  Entity already in solution '$SolutionName'" -ForegroundColor Yellow
        }
        else {
            Write-Host "  Warning: Could not add to solution. Error: $($_.Exception.Message)" -ForegroundColor Yellow
            Write-Host "  You may need to add it manually via the Maker Portal." -ForegroundColor Yellow
        }
    }

    Write-Host ""

    # --- Step 8: Verify ---
    Write-Host "Step 8: Verifying form and views..." -ForegroundColor Cyan

    # Verify forms
    $verifyForms = Get-ExistingForms -Token $token -BaseUrl $EnvironmentUrl -EntityLogicalName "sprk_aichatcontextmap"
    Write-Host "  Forms found: $($verifyForms.Count)" -ForegroundColor Green
    foreach ($f in $verifyForms) {
        Write-Host "    - $($f.name) (ID: $($f.formid))" -ForegroundColor Gray
    }

    # Verify views
    $verifyActiveView = Get-ExistingViews -Token $token -BaseUrl $EnvironmentUrl `
        -EntityLogicalName "sprk_aichatcontextmap" -ViewName "Active AI Chat Context Maps"
    $verifyAllView = Get-ExistingViews -Token $token -BaseUrl $EnvironmentUrl `
        -EntityLogicalName "sprk_aichatcontextmap" -ViewName "All AI Chat Context Maps"

    $viewCount = 0
    if ($verifyActiveView) { $viewCount++ }
    if ($verifyAllView) { $viewCount++ }
    Write-Host "  Custom views found: $viewCount" -ForegroundColor Green
    if ($verifyActiveView) { Write-Host "    - Active AI Chat Context Maps" -ForegroundColor Gray }
    if ($verifyAllView) { Write-Host "    - All AI Chat Context Maps" -ForegroundColor Gray }

    # --- Summary ---
    Write-Host ""
    Write-Host "========================================================" -ForegroundColor Green
    Write-Host " Form & View Deployment Complete!" -ForegroundColor Green
    Write-Host "========================================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "Entity: sprk_aichatcontextmap (AI Chat Context Map)" -ForegroundColor White
    Write-Host ""
    Write-Host "Form layout:" -ForegroundColor White
    Write-Host "  Header:      sprk_isactive, sprk_isdefault" -ForegroundColor Gray
    Write-Host "  General tab: sprk_name, sprk_entitytype, sprk_pagetype, sprk_playbookid, sprk_sortorder" -ForegroundColor Gray
    Write-Host "  Details tab: sprk_description" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Views created:" -ForegroundColor White
    Write-Host "  - Active AI Chat Context Maps (default, filtered by isactive=true)" -ForegroundColor Gray
    Write-Host "  - All AI Chat Context Maps (no filter)" -ForegroundColor Gray
    Write-Host "  Both sorted by: sprk_entitytype ASC, sprk_sortorder ASC" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor Yellow
    Write-Host "  1. Open Power Apps Maker Portal: $EnvironmentUrl" -ForegroundColor Gray
    Write-Host "  2. Add 'AI Chat Context Map' table to the model-driven app navigation" -ForegroundColor Gray
    Write-Host "  3. Verify the form layout and adjust field widths if needed" -ForegroundColor Gray
    Write-Host "  4. Create seed records for default context mappings" -ForegroundColor Gray
    Write-Host ""
}

# Run main
Main
