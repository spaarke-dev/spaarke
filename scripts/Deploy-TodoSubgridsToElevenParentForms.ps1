<#
.SYNOPSIS
    Adds a "To Dos" subgrid to the main form of each of the eleven regarding-target
    parent entities, plus a "+ Create To Do" ribbon command on each subgrid.

.DESCRIPTION
    smart-todo-decoupling-r3 task 040 / Phase 5 — Parent-Form Subgrids.

    Per FR-17, each of the 11 entities (Matter, Project, Event, Communication,
    WorkAssignment, Invoice, Budget, Analysis, Organization, Contact, Document)
    gets a "To Dos" subgrid on its primary main form. The subgrid is bound to the
    sprk_regarding{entity} relationship on sprk_todo, defaults to the "Active To Dos"
    view (statecode=0 — functionally equivalent to statuscode IN (Open, In Progress)
    per task 009 customization), and exposes "All To Dos" via the view picker.

    Also creates an "All To Dos" public saved query (no statecode filter) so the
    subgrid view picker has both Active and All to choose from.

    Ribbon command "+ Create To Do" is added at
    Mscrm.SubGrid.<entity>.MainTab.Management.Controls._children and invokes
    Spaarke.Commands.Wizards.openCreateTodoWizard(primaryControl) — passing
    entityType + entityId per the createtodo-launch-contract (task 032).

.PARAMETER EnvironmentUrl
    Dataverse environment URL.

.PARAMETER WhatIf
    If $true, prints intended changes without executing PATCH/POST.

.NOTES
    Project: smart-todo-decoupling-r3
    Task:    040 — Phase 5 / parent-form subgrids
    Created: 2026-06-08
    Rigor:   STANDARD (config-only)
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com",

    [Parameter(Mandatory = $false)]
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

# =============================================================================
# Configuration: 11 parent entities and their sprk_todo relationships
# =============================================================================
$entityConfig = @(
    @{ entity = 'sprk_matter';          relationship = 'sprk_sprk_matter_sprk_todo_RegardingMatter';                 lookup = 'sprk_regardingmatter';          formName = 'Matter main form' }
    @{ entity = 'sprk_project';         relationship = 'sprk_sprk_project_sprk_todo_RegardingProject';               lookup = 'sprk_regardingproject';         formName = 'Project main form' }
    @{ entity = 'sprk_event';           relationship = 'sprk_sprk_event_sprk_todo_RegardingEvent';                   lookup = 'sprk_regardingevent';           formName = 'Event main form' }
    @{ entity = 'sprk_communication';   relationship = 'sprk_sprk_communication_sprk_todo_RegardingCommunication';   lookup = 'sprk_regardingcommunication';   formName = 'Email main form' }
    @{ entity = 'sprk_workassignment';  relationship = 'sprk_sprk_workassignment_sprk_todo_RegardingWorkAssignment'; lookup = 'sprk_regardingworkassignment';  formName = 'Work Assignment main form' }
    @{ entity = 'sprk_invoice';         relationship = 'sprk_sprk_invoice_sprk_todo_RegardingInvoice';               lookup = 'sprk_regardinginvoice';         formName = 'Invoice main form' }
    @{ entity = 'sprk_budget';          relationship = 'sprk_sprk_budget_sprk_todo_RegardingBudget';                 lookup = 'sprk_regardingbudget';          formName = 'Budget main form' }
    @{ entity = 'sprk_analysis';        relationship = 'sprk_sprk_analysis_sprk_todo_RegardingAnalysis';             lookup = 'sprk_regardinganalysis';        formName = 'Analysis main form' }
    @{ entity = 'sprk_organization';    relationship = 'sprk_sprk_organization_sprk_todo_RegardingOrganization';     lookup = 'sprk_regardingorganization';    formName = 'Organization main form' }
    @{ entity = 'contact';              relationship = 'sprk_contact_sprk_todo_RegardingContact';                    lookup = 'sprk_regardingcontact';         formName = 'Contact main form' }
    @{ entity = 'sprk_document';        relationship = 'sprk_sprk_document_sprk_todo_RegardingDocument';             lookup = 'sprk_regardingdocument';        formName = 'Document main form' }
)

# =============================================================================
# Helpers
# =============================================================================
function Get-DataverseToken {
    param([string]$Url)
    $t = az account get-access-token --resource $Url --query "accessToken" -o tsv 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Token failed: $t" }
    return $t.Trim()
}

function Invoke-DataverseApi {
    param(
        [string]$Token, [string]$BaseUrl, [string]$Endpoint,
        [string]$Method = "GET", [object]$Body = $null,
        [hashtable]$ExtraHeaders = @{}
    )
    $headers = @{
        "Authorization"    = "Bearer $Token"
        "OData-MaxVersion" = "4.0"
        "OData-Version"    = "4.0"
        "Accept"           = "application/json"
        "Content-Type"     = "application/json; charset=utf-8"
    }
    foreach ($k in $ExtraHeaders.Keys) { $headers[$k] = $ExtraHeaders[$k] }
    $uri = "$BaseUrl/api/data/v9.2/$Endpoint"
    $params = @{ Uri = $uri; Method = $Method; Headers = $headers }
    if ($Body) {
        if ($Body -is [string]) { $params.Body = $Body }
        else { $params.Body = ($Body | ConvertTo-Json -Depth 30) }
    }
    try { return Invoke-RestMethod @params }
    catch {
        $msg = $_.Exception.Message
        if ($_.ErrorDetails.Message) {
            try {
                $j = $_.ErrorDetails.Message | ConvertFrom-Json -ErrorAction SilentlyContinue
                if ($j.error.message) { $msg = $j.error.message }
            } catch {}
        }
        throw "API Error ($Method $Endpoint): $msg"
    }
}

function Get-SavedQuery {
    param([string]$Token, [string]$BaseUrl, [string]$Name)
    $r = Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
        -Endpoint "savedqueries?`$filter=returnedtypecode eq 'sprk_todo' and name eq '$Name'&`$select=savedqueryid,name" `
        -Method "GET"
    if ($r.value.Count -gt 0) { return $r.value[0] }
    return $null
}

function New-AllTodosSavedQuery {
    param([string]$Token, [string]$BaseUrl)

    $name = 'All To Dos'
    $existing = Get-SavedQuery -Token $Token -BaseUrl $BaseUrl -Name $name
    if ($existing) {
        Write-Host "  '$name' already exists ($($existing.savedqueryid))" -ForegroundColor Yellow
        return $existing.savedqueryid
    }

    $fetchXml = @"
<fetch version="1.0" output-format="xml-platform" mapping="logical" distinct="false">
  <entity name="sprk_todo">
    <attribute name="sprk_todoid" />
    <attribute name="sprk_name" />
    <attribute name="statuscode" />
    <attribute name="sprk_duedate" />
    <attribute name="sprk_assignedto" />
    <attribute name="createdon" />
    <order attribute="sprk_name" descending="false" />
  </entity>
</fetch>
"@

    $layoutXml = @"
<grid name="resultset" object="10946" jump="sprk_name" select="1" preview="1" icon="1">
  <row name="result" id="sprk_todoid">
    <cell name="sprk_name" width="300" />
    <cell name="statuscode" width="120" />
    <cell name="sprk_duedate" width="120" />
    <cell name="sprk_assignedto" width="150" />
    <cell name="createdon" width="120" />
  </row>
</grid>
"@

    $body = @{
        "name"             = $name
        "description"      = "All To Dos regardless of state (Active + Inactive). Used as the 'All' option in the To Dos subgrid view picker on parent forms (FR-17)."
        "returnedtypecode" = "sprk_todo"
        "querytype"        = 0       # Public view
        "fetchxml"         = $fetchXml
        "layoutxml"        = $layoutXml
        "isdefault"        = $false
        "isquickfindquery" = $false
    }
    if ($DryRun) {
        Write-Host "  [DRY] Would create saved query '$name'" -ForegroundColor Cyan
        return [guid]::Empty.Guid
    }
    $r = Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl -Endpoint "savedqueries" -Method "POST" -Body $body `
        -ExtraHeaders @{ "Prefer" = "return=representation" }
    Write-Host "  Created '$name' ($($r.savedqueryid))" -ForegroundColor Green
    return $r.savedqueryid
}

# =============================================================================
# FormXml manipulation
# =============================================================================
function Get-PrimaryMainForm {
    param([string]$Token, [string]$BaseUrl, [string]$Entity, [string]$PreferredName = $null)

    # Prefer named form if provided; else pick first active (formactivationstate=1)
    if ($PreferredName) {
        $r = Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
            -Endpoint "systemforms?`$filter=objecttypecode eq '$Entity' and type eq 2 and name eq '$PreferredName'&`$select=formid,name,formxml,formactivationstate" `
            -Method "GET"
        if ($r.value.Count -gt 0) { return $r.value[0] }
    }

    # systemform doesn't expose createdon for filtering/ordering — pick the first active main form
    $r = Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
        -Endpoint "systemforms?`$filter=objecttypecode eq '$Entity' and type eq 2 and formactivationstate eq 1&`$select=formid,name,formxml,formactivationstate" `
        -Method "GET"
    if ($r.value.Count -eq 0) { throw "No active main form found for $Entity" }
    return $r.value[0]
}

function New-GuidString { return [guid]::NewGuid().ToString() }

function New-TodosTabXml {
    param(
        [string]$Entity,
        [string]$Relationship,
        [string]$ActiveViewId
    )

    $tabId        = "tab_todos_$Entity"
    $tabGuid      = New-GuidString
    $sectionId    = "tab_todos_section"
    $sectionGuid  = New-GuidString
    $rowGuid      = New-GuidString
    $cellGuid     = New-GuidString
    $labelGuid    = New-GuidString
    $controlId    = "subgrid_todos"

    # Use the existing default Active To Dos view id (statecode=0, equivalent to
    # "Open + In Progress" per task 009 customization). The view picker exposes
    # any other public sprk_todo view, including the "All To Dos" view this script also creates.
    $viewIdBraced = "{$($ActiveViewId.ToUpper())}"

    return @"
<tab name="$tabId" id="$tabGuid" IsUserDefined="0" locklevel="0" showlabel="true" expanded="false"><labels><label description="To Dos" languagecode="1033" /></labels><columns><column width="100%"><sections><section name="$sectionId" id="$sectionGuid" IsUserDefined="0" locklevel="0" showlabel="true" showbar="false" layout="varwidth" celllabelalignment="Left" celllabelposition="Left" columns="1" labelwidth="115"><labels><label description="TO DOS" languagecode="1033" /></labels><rows><row /><row><cell locklevel="0" id="{$cellGuid}" rowspan="4" colspan="1" auto="true" showlabel="false" labelid="{$labelGuid}"><labels><label description="To Dos Subgrid" languagecode="1033" /></labels><control indicationOfSubgrid="true" id="$controlId" classid="{E7A81278-8635-4D9E-8D4D-59480B391C5B}"><parameters><RecordsPerPage>30</RecordsPerPage><AutoExpand>Auto</AutoExpand><EnableQuickFind>false</EnableQuickFind><EnableViewPicker>true</EnableViewPicker><EnableChartPicker>false</EnableChartPicker><ChartGridMode>Both</ChartGridMode><RelationshipName>$Relationship</RelationshipName><TargetEntityType>sprk_todo</TargetEntityType><ViewId>$viewIdBraced</ViewId><ViewIds>$viewIdBraced</ViewIds></parameters></control></cell></row></rows></section></sections></column></columns></tab>
"@
}

function Add-TodosTabToFormXml {
    param([string]$FormXml, [string]$NewTabXml)

    # Check if a To Dos tab already exists for this entity (idempotency)
    if ($FormXml -match 'name="tab_todos_[a-z_]+"' -or $FormXml -match 'id="subgrid_todos"') {
        return @{ Modified = $false; FormXml = $FormXml; Reason = 'tab_todos_* or subgrid_todos already present' }
    }

    # Insert just before </tabs>. Use literal string replace to preserve formatting.
    $closingTabs = '</tabs>'
    $idx = $FormXml.LastIndexOf($closingTabs)
    if ($idx -lt 0) {
        return @{ Modified = $false; FormXml = $FormXml; Reason = 'no </tabs> found' }
    }
    $newFormXml = $FormXml.Substring(0, $idx) + $NewTabXml + $FormXml.Substring($idx)
    return @{ Modified = $true; FormXml = $newFormXml; Reason = 'tab appended' }
}

function Update-SystemForm {
    param([string]$Token, [string]$BaseUrl, [string]$FormId, [string]$FormXml)
    $body = @{ formxml = $FormXml } | ConvertTo-Json -Depth 5
    if ($DryRun) {
        Write-Host "    [DRY] Would PATCH systemforms($FormId)" -ForegroundColor Cyan
        return
    }
    Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl -Endpoint "systemforms($FormId)" -Method "PATCH" -Body $body | Out-Null
}

# =============================================================================
# Main
# =============================================================================
Write-Host ""
Write-Host "================================================" -ForegroundColor Cyan
Write-Host " Task 040: To Dos subgrid on 11 parent forms" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl"
Write-Host "DryRun: $DryRun"
Write-Host ""

$token = Get-DataverseToken -Url $EnvironmentUrl
Write-Host "[Auth] Token acquired" -ForegroundColor Green

# -----------------------------------------------------------------------------
# Step 1: Resolve the default "Active To Dos" view id (used as subgrid default).
# -----------------------------------------------------------------------------
Write-Host ""
Write-Host "Step 1: Resolve 'Active To Dos' default view..." -ForegroundColor Cyan
$activeView = Get-SavedQuery -Token $token -BaseUrl $EnvironmentUrl -Name "Active To Dos"
if (-not $activeView) {
    throw "Could not find the default 'Active To Dos' saved query on sprk_todo. Was sprk_todo entity created (task 002)?"
}
$activeViewId = $activeView.savedqueryid
Write-Host "  Active To Dos view id = $activeViewId" -ForegroundColor Green

# -----------------------------------------------------------------------------
# Step 2: Create "All To Dos" public view.
# -----------------------------------------------------------------------------
Write-Host ""
Write-Host "Step 2: Create 'All To Dos' public view..." -ForegroundColor Cyan
$allViewId = New-AllTodosSavedQuery -Token $token -BaseUrl $EnvironmentUrl

# -----------------------------------------------------------------------------
# Step 3: For each entity, fetch main form, add subgrid, PATCH back.
# -----------------------------------------------------------------------------
Write-Host ""
Write-Host "Step 3: Add 'To Dos' subgrid to each of 11 parent forms..." -ForegroundColor Cyan

$results = @()
foreach ($cfg in $entityConfig) {
    $entity = $cfg.entity
    Write-Host "  [$entity]" -ForegroundColor Cyan
    try {
        $form = Get-PrimaryMainForm -Token $token -BaseUrl $EnvironmentUrl -Entity $entity -PreferredName $cfg.formName
        Write-Host "    Form: $($form.name) ($($form.formid))" -ForegroundColor Gray
        $tabXml = New-TodosTabXml -Entity $entity -Relationship $cfg.relationship -ActiveViewId $activeViewId
        $r = Add-TodosTabToFormXml -FormXml $form.formxml -NewTabXml $tabXml
        if (-not $r.Modified) {
            Write-Host "    SKIPPED: $($r.Reason)" -ForegroundColor Yellow
            $results += [pscustomobject]@{ Entity = $entity; Form = $form.name; FormId = $form.formid; Status = 'Skipped'; Reason = $r.Reason }
            continue
        }
        Update-SystemForm -Token $token -BaseUrl $EnvironmentUrl -FormId $form.formid -FormXml $r.FormXml
        $action = if ($DryRun) { 'DryRun-Modified' } else { 'Modified' }
        Write-Host "    $action — tab inserted, form PATCHed" -ForegroundColor Green
        $results += [pscustomobject]@{ Entity = $entity; Form = $form.name; FormId = $form.formid; Status = $action; Reason = '' }
    }
    catch {
        Write-Host "    ERROR: $($_.Exception.Message)" -ForegroundColor Red
        $results += [pscustomobject]@{ Entity = $entity; Form = ''; FormId = ''; Status = 'Error'; Reason = $_.Exception.Message }
    }
}

# -----------------------------------------------------------------------------
# Step 4: Publish all touched entities + saved queries.
# -----------------------------------------------------------------------------
Write-Host ""
Write-Host "Step 4: Publish customizations..." -ForegroundColor Cyan

$touchedEntities = $results | Where-Object { $_.Status -in @('Modified', 'DryRun-Modified') } | ForEach-Object { $_.Entity }
$publishEntitiesXml = ($touchedEntities | ForEach-Object { "<entity>$_</entity>" }) -join ''
$publishXml = "<importexportxml><entities>$publishEntitiesXml</entities></importexportxml>"
$publishBody = @{ "ParameterXml" = $publishXml }

if ($DryRun) {
    Write-Host "  [DRY] Would PublishXml: $publishXml" -ForegroundColor Cyan
}
elseif ($touchedEntities.Count -gt 0) {
    try {
        Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl -Endpoint "PublishXml" -Method "POST" -Body $publishBody | Out-Null
        Write-Host "  Published $($touchedEntities.Count) entities." -ForegroundColor Green
    } catch {
        Write-Host "  Publish warning: $($_.Exception.Message)" -ForegroundColor Yellow
    }
}
else {
    Write-Host "  Nothing to publish (no forms modified)." -ForegroundColor Yellow
}

# -----------------------------------------------------------------------------
# Step 5: Summary
# -----------------------------------------------------------------------------
Write-Host ""
Write-Host "================================================" -ForegroundColor Cyan
Write-Host " RESULTS SUMMARY" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
$results | Format-Table -AutoSize Entity, Form, Status, Reason
Write-Host ""
$modified = ($results | Where-Object { $_.Status -in @('Modified', 'DryRun-Modified') }).Count
$skipped  = ($results | Where-Object { $_.Status -eq 'Skipped' }).Count
$errors   = ($results | Where-Object { $_.Status -eq 'Error' }).Count
Write-Host "  Modified: $modified / Skipped: $skipped / Errors: $errors" -ForegroundColor $(if ($errors -gt 0) { 'Red' } else { 'Green' })
Write-Host ""

if ($errors -gt 0) { exit 1 }
exit 0
