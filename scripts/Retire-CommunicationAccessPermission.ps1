<#
.SYNOPSIS
    Retires the dead `sprk_communication.sprk_accesspermission` column (owner decision Q6). DRY RUN by default.

.DESCRIPTION
    Owner decision Q6 (unified-access-control-r2, round 2, 2026-09-30, binding): a communication INHERITS
    its parent record's Access Permission, and its own copy of the field is retired. Nothing in the BFF ever
    read it (ExternalParticipationService's flag sources are the three roots only), and task 138 removed every
    client reader and writer. This script removes the column itself.

    It works strictly in this order, so the delete never fails half-way on a reference it could have removed:

      1. Resolve the column. ABSENT => "nothing to do" (the script is idempotent: a second run is a no-op).
      2. Refuse (exit 2) if the column, or any component that references it, is MANAGED — a managed layer
         cannot be edited here and must be removed by its owning solution (task 138 escalation trigger 4).
      3. Refuse (exit 2) if a workflow / business rule references the column (also trigger 4).
      4. Remove FORM references on sprk_communication forms: the cell holding a control bound to the column,
         and a TrackingFieldTrio `accessPermission` parameter binding (optional since PCF v1.0.32).
      5. Remove VIEW references (savedquery): the layout cell, the fetch <attribute> and any <order> on the
         column. A view that FILTERS on the column is refused (exit 2) — dropping a filter changes which rows
         the view returns, which is a product decision, not a cleanup.
      6. Personal views (userquery) get the SAME rule as system views: one that FILTERS on the column is
         refused (exit 2), and one that merely displays/sorts on it is stripped only when -IncludeUserViews
         is passed — without it the run is refused (exit 2) rather than deleting the column out from under
         a user's view. Only personal views visible to the running principal can be scanned.
      7. Delete the unmanaged Copilot form-fill opt-out (aiskillconfig) rows bound to the column.
      8. Re-check RetrieveDependenciesForDelete; anything still listed is reported and the run stops (exit 2).
      9. Delete the column and publish the table.

    Data note: every row's value is discarded with the column. In spaarkedev1 on 2026-10-01 the column held
    Standard (100000000) on 99 rows and nothing else — the value never governed access.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Default: https://spaarkedev1.crm.dynamics.com

.PARAMETER Apply
    Perform the writes. Without it the script is a READ-ONLY dry run that prints the full plan.

.PARAMETER IncludeUserViews
    Also strip the column from personal views (userquery) that display or sort on it. Off by default: those
    are users' own records, so without this switch any such reference REFUSES the run (exit 2) instead of
    leaving a view that would break when the column is deleted. A personal view that FILTERS on the column
    is refused either way (the same rule as a system view).

.EXAMPLE
    .\Retire-CommunicationAccessPermission.ps1                 # dry run (default): prints the plan, writes nothing
    .\Retire-CommunicationAccessPermission.ps1 -Apply          # performs the retirement
    .\Retire-CommunicationAccessPermission.ps1 -Apply          # second run: "nothing to do" (idempotent)

.NOTES
    Project : unified-access-control-r2
    Task    : 138 (#1061) — Access Permission levels made real; owner Q6
    Created : 2026-10-02
    Docs    : docs/data-model/sprk_communication.md (retired column + inherit-from-parent rule)

    OPERATOR-RUN ONLY. The task agent wrote this script and ran it with no -Apply against spaarkedev1; the
    real run is the manual gate (task 138 acceptance criterion 16e). Requires Azure CLI (`az login`) with a
    principal that can customize the environment. PowerShell 7+.

    Exit codes: 0 = done (or nothing to do / dry run complete), 2 = refused (a reference this script must not
    or cannot remove — see the printed reason), 1 = unexpected error.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com",

    [Parameter(Mandatory = $false)]
    [switch]$Apply,

    [Parameter(Mandatory = $false)]
    [switch]$IncludeUserViews
)

$ErrorActionPreference = "Stop"

$Table     = "sprk_communication"
$Column    = "sprk_accesspermission"
$BaseUrl   = $EnvironmentUrl.TrimEnd('/')
$Mode      = if ($Apply) { "APPLY" } else { "DRY RUN (read-only; pass -Apply to perform the writes)" }

# ============================================================================
# Helpers
# ============================================================================

function Get-DataverseToken {
    $tokenResult = az account get-access-token --resource $BaseUrl --query "accessToken" -o tsv 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to get a token from Azure CLI: $tokenResult. Run 'az login' first."
    }
    return $tokenResult.Trim()
}

$Token = $null

function Invoke-Dv {
    param(
        [string]$Endpoint,
        [string]$Method = "GET",
        [object]$Body = $null,
        [switch]$AllowNotFound
    )

    $headers = @{
        "Authorization"    = "Bearer $Token"
        "OData-MaxVersion" = "4.0"
        "OData-Version"    = "4.0"
        "Accept"           = "application/json"
        "Content-Type"     = "application/json; charset=utf-8"
    }
    $params = @{ Uri = "$BaseUrl/api/data/v9.2/$Endpoint"; Method = $Method; Headers = $headers }
    if ($null -ne $Body) { $params.Body = ($Body | ConvertTo-Json -Depth 20) }

    try {
        return Invoke-RestMethod @params
    }
    catch {
        $status = $_.Exception.Response.StatusCode.value__
        if ($AllowNotFound -and $status -eq 404) { return $null }
        $detail = $_.Exception.Message
        if ($_.ErrorDetails.Message) {
            $err = $_.ErrorDetails.Message | ConvertFrom-Json -ErrorAction SilentlyContinue
            if ($err.error.message) { $detail = $err.error.message }
        }
        throw "API error ($Method $Endpoint): $detail"
    }
}

function Write-Step([string]$Text) { Write-Host ""; Write-Host "== $Text" -ForegroundColor Cyan }
function Write-Plan([string]$Text) { Write-Host "   PLAN  $Text" -ForegroundColor Yellow }
function Write-Done([string]$Text) { Write-Host "   DONE  $Text" -ForegroundColor Green }
function Write-Info([string]$Text) { Write-Host "         $Text" }

function Stop-Refused([string]$Reason) {
    Write-Host ""
    Write-Host "REFUSED: $Reason" -ForegroundColor Red
    Write-Host "Nothing further was changed. Resolve the reference in its owning solution, then re-run." -ForegroundColor Red
    exit 2
}

# The removals are pure string transforms so a dry run shows exactly what -Apply would write.

function Remove-FormReferences([string]$FormXml) {
    $x = $FormXml
    # A TrackingFieldTrio (or any custom control) parameter binding to the column — optional since PCF v1.0.32.
    $x = [regex]::Replace($x, "<accessPermission\b[^>]*>\s*$Column\s*</accessPermission>", '')
    # Any form cell whose control is bound to the column (its <control datafieldname=...> and the cell around it).
    $x = [regex]::Replace(
        $x,
        "<cell\b[^>]*>(?:(?!</cell>).)*?datafieldname=""$Column""(?:(?!</cell>).)*?</cell>",
        '',
        [System.Text.RegularExpressions.RegexOptions]::Singleline)
    return $x
}

function Remove-ViewReferences([string]$LayoutXml, [string]$FetchXml) {
    $layout = [regex]::Replace($LayoutXml ?? '', "<cell\b[^>]*\bname=""$Column""[^>]*/>", '')
    $fetch = $FetchXml ?? ''
    $fetch = [regex]::Replace($fetch, "<attribute\b[^>]*\bname=""$Column""[^>]*/>", '')
    $fetch = [regex]::Replace($fetch, "<order\b[^>]*\battribute=""$Column""[^>]*/>", '')
    return @{ Layout = $layout; Fetch = $fetch }
}

function Test-FiltersOnColumn([string]$FetchXml) {
    return [regex]::IsMatch($FetchXml ?? '', "<condition\b[^>]*\battribute=""$Column""")
}

# ============================================================================
# Run
# ============================================================================

Write-Host "Retire $Table.$Column  —  $Mode" -ForegroundColor White
Write-Host "Environment: $BaseUrl"
$Token = Get-DataverseToken

# 1. The column ---------------------------------------------------------------
Write-Step "1. Column"
$attr = Invoke-Dv -Endpoint "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')?`$select=MetadataId,LogicalName,IsManaged,AttributeType" -AllowNotFound
if ($null -eq $attr) {
    Write-Done "$Table.$Column does not exist — nothing to do (already retired)."
    exit 0
}
Write-Info "Found: MetadataId $($attr.MetadataId), type $($attr.AttributeType), managed=$($attr.IsManaged)"
if ($attr.IsManaged) {
    Stop-Refused "$Table.$Column is MANAGED. Remove it from its managed solution instead (task 138 escalation trigger 4)."
}

# 2/3. Workflows and business rules -------------------------------------------
Write-Step "2. Workflows and business rules"
$wf = Invoke-Dv -Endpoint "workflows?`$select=workflowid,name,category,ismanaged,xaml,clientdata&`$filter=primaryentity eq '$Table'"
$wfHits = @($wf.value | Where-Object { ($_.xaml -match $Column) -or ($_.clientdata -match $Column) })
if ($wfHits.Count -gt 0) {
    $wfHits | ForEach-Object { Write-Info "workflow $($_.workflowid) '$($_.name)' category=$($_.category) managed=$($_.ismanaged)" }
    Stop-Refused "$($wfHits.Count) workflow(s)/business rule(s) reference the column (task 138 escalation trigger 4)."
}
Write-Info "None reference the column ($($wf.value.Count) scanned)."

# 4. Forms --------------------------------------------------------------------
Write-Step "3. Forms ($Table)"
$forms = Invoke-Dv -Endpoint "systemforms?`$select=formid,name,type,ismanaged,formxml&`$filter=objecttypecode eq '$Table'"
$formEdits = @()
foreach ($f in $forms.value) {
    if ($f.formxml -notmatch $Column) { continue }
    if ($f.ismanaged) { Stop-Refused "Form '$($f.name)' ($($f.formid)) is MANAGED and references the column." }
    $newXml = Remove-FormReferences $f.formxml
    if ($newXml -match $Column) {
        Stop-Refused "Form '$($f.name)' ($($f.formid)) references the column in a shape this script does not recognise."
    }
    $formEdits += @{ Id = $f.formid; Name = $f.name; Xml = $newXml }
    Write-Plan "remove the column's control / binding from form '$($f.name)' ($($f.formid))"
}
if ($formEdits.Count -eq 0) { Write-Info "No form references ($($forms.value.Count) forms scanned)." }

# 5. System views ---------------------------------------------------------------
Write-Step "4. System views (savedquery)"
$views = Invoke-Dv -Endpoint "savedqueries?`$select=savedqueryid,name,ismanaged,layoutxml,fetchxml&`$filter=returnedtypecode eq '$Table'"
$viewEdits = @()
foreach ($v in $views.value) {
    if (($v.layoutxml -notmatch $Column) -and ($v.fetchxml -notmatch $Column)) { continue }
    if ($v.ismanaged) { Stop-Refused "View '$($v.name)' ($($v.savedqueryid)) is MANAGED and references the column." }
    if (Test-FiltersOnColumn $v.fetchxml) {
        Stop-Refused "View '$($v.name)' ($($v.savedqueryid)) FILTERS on the column; removing the filter changes its rows (owner decision)."
    }
    $r = Remove-ViewReferences $v.layoutxml $v.fetchxml
    $viewEdits += @{ Id = $v.savedqueryid; Name = $v.name; Layout = $r.Layout; Fetch = $r.Fetch }
    Write-Plan "remove the column from view '$($v.name)' ($($v.savedqueryid))"
}
if ($viewEdits.Count -eq 0) { Write-Info "No system-view references ($($views.value.Count) views scanned)." }

# 6. Personal views -----------------------------------------------------------
Write-Step "5. Personal views (userquery)"
$userViewEdits = @()
$userViewRefusal = $null
try {
    $uq = Invoke-Dv -Endpoint "userqueries?`$select=userqueryid,name,layoutxml,fetchxml&`$filter=returnedtypecode eq '$Table'"
    foreach ($v in $uq.value) {
        if (($v.layoutxml -notmatch $Column) -and ($v.fetchxml -notmatch $Column)) { continue }
        # Same rule as a system view (step 4): a filter is a product decision, never stripped silently. And a
        # reference this run will not strip is refused, never left behind to break when the column goes.
        if (Test-FiltersOnColumn $v.fetchxml) {
            $userViewRefusal = "Personal view '$($v.name)' ($($v.userqueryid)) FILTERS on the column; removing the filter changes its rows (its owner's decision)."
            break
        }
        if (-not $IncludeUserViews) {
            $userViewRefusal = "Personal view '$($v.name)' ($($v.userqueryid)) references the column; pass -IncludeUserViews to strip it, or have its owner remove the column first."
            break
        }
        $r = Remove-ViewReferences $v.layoutxml $v.fetchxml
        $userViewEdits += @{ Id = $v.userqueryid; Name = $v.name; Layout = $r.Layout; Fetch = $r.Fetch }
        Write-Plan "remove the column from personal view '$($v.name)' ($($v.userqueryid))"
    }
    if ($userViewEdits.Count -eq 0) { Write-Info "No personal-view references visible to this principal ($($uq.value.Count) scanned)." }
}
catch {
    Write-Info "Personal views could not be read by this principal: $($_.Exception.Message)"
}
# Refused OUTSIDE the try: Stop-Refused exits, and the catch above must not swallow the refusal.
if ($userViewRefusal) { Stop-Refused $userViewRefusal }

# 7. Copilot form-fill opt-out rows -------------------------------------------
Write-Step "6. Copilot form-fill configuration (aiskillconfig)"
$aiRows = @()
try {
    $ai = Invoke-Dv -Endpoint "aiskillconfigs?`$select=aiskillconfigid,uniquename,aiskill,ismanaged&`$filter=_attribute_value eq $($attr.MetadataId)"
    foreach ($a in $ai.value) {
        if ($a.ismanaged) { Stop-Refused "aiskillconfig '$($a.uniquename)' is MANAGED and depends on the column." }
        $aiRows += $a
        Write-Plan "delete aiskillconfig '$($a.uniquename)' ($($a.aiskill)) — $($a.aiskillconfigid)"
    }
    if ($aiRows.Count -eq 0) { Write-Info "None." }
}
catch {
    Write-Info "aiskillconfig not readable here (table absent or no privilege): $($_.Exception.Message)"
}

# 8. Remaining dependencies -----------------------------------------------------
Write-Step "7. Dependencies for delete"
$deps = Invoke-Dv -Endpoint "RetrieveDependenciesForDelete(ObjectId=$($attr.MetadataId),ComponentType=2)"
$handledIds = @($aiRows | ForEach-Object { "$($_.aiskillconfigid)".ToLowerInvariant() }) +
              @($formEdits | ForEach-Object { "$($_.Id)".ToLowerInvariant() }) +
              @($viewEdits | ForEach-Object { "$($_.Id)".ToLowerInvariant() })
$unhandled = @($deps.value | Where-Object { $handledIds -notcontains "$($_.dependentcomponentobjectid)".ToLowerInvariant() })
foreach ($d in $deps.value) {
    $handled = $handledIds -contains "$($d.dependentcomponentobjectid)".ToLowerInvariant()
    Write-Info ("dependency: component type {0}, object {1}, dependency type {2} — {3}" -f
        $d.dependentcomponenttype, $d.dependentcomponentobjectid, $d.dependencytype, $(if ($handled) { "removed by this script" } else { "NOT handled" }))
}
if ($unhandled.Count -gt 0) {
    Stop-Refused "$($unhandled.Count) dependency(ies) this script does not remove (see above; task 138 escalation trigger 4)."
}
if ($deps.value.Count -eq 0) { Write-Info "None." }

Write-Step "8. Delete"
Write-Plan "delete column $Table.$Column, then publish $Table"

if (-not $Apply) {
    Write-Host ""
    Write-Host "DRY RUN complete — no writes were made. Re-run with -Apply to perform the plan above." -ForegroundColor White
    exit 0
}

# ============================================================================
# Apply (in dependency order)
# ============================================================================

foreach ($e in $formEdits) {
    Invoke-Dv -Endpoint "systemforms($($e.Id))" -Method PATCH -Body @{ formxml = $e.Xml } | Out-Null
    Write-Done "form '$($e.Name)' updated"
}
foreach ($e in $viewEdits) {
    Invoke-Dv -Endpoint "savedqueries($($e.Id))" -Method PATCH -Body @{ layoutxml = $e.Layout; fetchxml = $e.Fetch } | Out-Null
    Write-Done "view '$($e.Name)' updated"
}
# $userViewEdits is non-empty only with -IncludeUserViews (without it, any reference refused the run above).
foreach ($e in $userViewEdits) {
    Invoke-Dv -Endpoint "userqueries($($e.Id))" -Method PATCH -Body @{ layoutxml = $e.Layout; fetchxml = $e.Fetch } | Out-Null
    Write-Done "personal view '$($e.Name)' updated"
}
if ($formEdits.Count -gt 0 -or $viewEdits.Count -gt 0) {
    $publish = "<importexportxml><entities><entity>$Table</entity></entities></importexportxml>"
    Invoke-Dv -Endpoint "PublishXml" -Method POST -Body @{ ParameterXml = $publish } | Out-Null
    Write-Done "published $Table (form/view edits)"
}
foreach ($a in $aiRows) {
    Invoke-Dv -Endpoint "aiskillconfigs($($a.aiskillconfigid))" -Method DELETE | Out-Null
    Write-Done "aiskillconfig '$($a.uniquename)' deleted"
}

Invoke-Dv -Endpoint "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')" -Method DELETE | Out-Null
Write-Done "column $Table.$Column deleted"

$publish = "<importexportxml><entities><entity>$Table</entity></entities></importexportxml>"
Invoke-Dv -Endpoint "PublishXml" -Method POST -Body @{ ParameterXml = $publish } | Out-Null
Write-Done "published $Table"

# Verify
$again = Invoke-Dv -Endpoint "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')?`$select=LogicalName" -AllowNotFound
if ($null -ne $again) { throw "Verification failed: $Table.$Column still exists after the delete." }
Write-Host ""
Write-Host "Retired $Table.$Column. A second run reports 'nothing to do'." -ForegroundColor Green
exit 0
