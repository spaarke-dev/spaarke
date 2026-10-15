<#
.SYNOPSIS
    Brings sprk_document.sprk_accesspermission into source: the column exists, is bound to the SAME global choice as the
    roots' Access Permission, and ships in SpaarkeCore (owner round 81). DRY RUN by default; -Verify checks it at any time.

.DESCRIPTION
    Owner round 81 (2026-10-08, binding): a Document WITH a parent shows the parent's Access Permission; one WITHOUT a
    parent keeps its own value. The owner added sprk_accesspermission (the global choice) to sprk_document live on
    2026-10-08 (spaarkedev1, MetadataId 4b469c31-1ac3-f111-a05a-7c1e520a989f). This script makes that column reproducible
    in every other environment and checks it in this one:

      - the column is a Choice (Picklist) bound to the global choice the ROOTS use - the one sprk_matter.sprk_accesspermission
        is bound to (sprk_accesspermission: Standard 100000000 / Limited 100000001 / Restricted 100000002), so a value copied
        from a matter, project or work assignment means the same thing on the document;
      - it ships in the SpaarkeCore solution: its own component row, or sprk_document in SpaarkeCore with
        rootcomponentbehavior 0 (include all subcomponents). Decided ONE way, through scripts/common/
        DataverseSolutionMembership.ps1 (Test-DvInSolution; SchemaScriptSolutionMembershipGuardTests).

    -Apply creates the column when it is absent (SchemaName sprk_AccessPermission, display name "Access Permission",
    default Standard, not required, audited, created under the SpaarkeCore solution header), and adds an existing column to
    SpaarkeCore when it is not in it. It never alters an existing column.

    Fail closed (ADR-003). REFUSES (exit 2, nothing written):
      ROOT_CHOICE_UNKNOWN  sprk_matter.sprk_accesspermission is missing or not bound to a global choice (nothing to bind to);
      COLUMN_MISMATCH      sprk_document.sprk_accesspermission exists but is not a Choice bound to the roots' global choice
                           (task 173 escalation trigger: never altered silently);
      SOLUTION_MISSING     the SpaarkeCore solution is not in the environment.
    -Verify: exit 0 only when the column exists, matches, and ships in SpaarkeCore; any gap or read fault exits 1.

    The global choice itself: -Verify reports whether it is a component of SpaarkeCore (informational - the roots' columns
    have the same dependency; on spaarkedev1 2026-10-08 it was in SpaarkeMaster and the Default solution only).

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Default: https://spaarkedev1.crm.dynamics.com

.PARAMETER Apply
    Create the column (or add it to SpaarkeCore) and publish sprk_document. Without a mode switch: a read-only dry run.

.PARAMETER Verify
    Read-only check (exit 0 / 1).

.PARAMETER SelfTest
    Offline: runs the pure column and solution checks over inline cases; exits 1 on any mismatch.

.PARAMETER SolutionUniqueName
    The unmanaged solution the column ships in. Default SpaarkeCore.

.EXAMPLE
    .\Set-DocumentAccessPermissionSchema.ps1            # dry run
    .\Set-DocumentAccessPermissionSchema.ps1 -SelfTest  # offline
    .\Set-DocumentAccessPermissionSchema.ps1 -Apply     # create / add to solution + publish + read back
    .\Set-DocumentAccessPermissionSchema.ps1 -Verify    # exit 0 / 1

.NOTES
    Project : unified-access-control-r2
    Task    : 173 (#1423) - owner round 81
    Created : 2026-10-08
    Docs    : docs/data-model/child-access-permission.md; projects/unified-access-control-r2/notes/task-173-child-access-permission.md

    OPERATOR-RUN ONLY: -Apply is the main session's manual gate. Run it BEFORE Set-InheritedAccessPermissionFormLock.ps1
    (which refuses COLUMN_MISSING until the column exists) and before a BFF with task 173 writes the column in a new
    environment (the BFF skips a table that lacks it, so the order is safe either way). Requires Azure CLI (`az login`).

    Exit codes: 0 = done / nothing to do / dry run complete / VERIFY PASS / SELF-TEST PASS;
                2 = refused (nothing was written); 1 = VERIFY FAIL, SELF-TEST FAIL, or an unexpected error.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com",

    [Parameter(Mandatory = $false)]
    [switch]$Apply,

    [Parameter(Mandatory = $false)]
    [switch]$Verify,

    [Parameter(Mandatory = $false)]
    [switch]$SelfTest,

    [Parameter(Mandatory = $false)]
    [string]$SolutionUniqueName = 'SpaarkeCore'
)

$ErrorActionPreference = "Stop"

$modeCount = @($Apply.IsPresent, $Verify.IsPresent, $SelfTest.IsPresent) | Where-Object { $_ } | Measure-Object | Select-Object -ExpandProperty Count
if ($modeCount -gt 1) { throw "-Apply, -Verify and -SelfTest are separate modes; pass at most one." }

$Table = 'sprk_document'
$Column = 'sprk_accesspermission'
$RootTable = 'sprk_matter'

# ============================================================================
# Pure checks
# ============================================================================

<#
    $Attr: $null (absent) or @{ AttributeType; OptionSetId } (OptionSetId: the bound GLOBAL choice's MetadataId, or $null
    for a local choice). $RootOptionSetId: the roots' global choice. Returns 'absent', 'match' or a mismatch text.
#>
function Get-ColumnState([object]$Attr, $RootOptionSetId) {
    if ($null -eq $Attr) { return 'absent' }
    if ("$($Attr.AttributeType)" -ne 'Picklist') { return "mismatch: type $($Attr.AttributeType) (want Picklist)" }
    if ($null -eq $Attr.OptionSetId) { return 'mismatch: a local choice (want the roots'' global choice)' }
    if ("$($Attr.OptionSetId)" -ine "$RootOptionSetId") { return "mismatch: bound to global choice $($Attr.OptionSetId), the roots use $RootOptionSetId" }
    return 'match'
}

<#
    $SolutionFound: bool. $How: Test-DvInSolution's answer ('Direct', 'ViaTable' or $null) - for a column not created yet,
    asked of the TABLE (does it ship with all subcomponents?). Returns $null when the column ships, otherwise the reason.
#>
function Get-SolutionProblem([bool]$SolutionFound, $How) {
    if (-not $SolutionFound) { return "solution $SolutionUniqueName not found" }
    if ($How -in 'Direct', 'ViaTable') { return $null }
    return "$Table.$Column is not in ${SolutionUniqueName}: neither its own component row nor $Table with rootcomponentbehavior 0"
}

. (Join-Path $PSScriptRoot 'common/DataverseSolutionMembership.ps1')

if ($SelfTest) {
    $root = [guid]::NewGuid(); $other = [guid]::NewGuid()
    $tableRcb0 = [guid]::NewGuid(); $tableRcb1 = [guid]::NewGuid(); $column = [guid]::NewGuid(); $directColumn = [guid]::NewGuid()
    $ids = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    [void]$ids.Add("$tableRcb0"); [void]$ids.Add("$tableRcb1"); [void]$ids.Add("$directColumn")
    $withSub = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    [void]$withSub.Add("$tableRcb0")
    $m = [pscustomobject]@{ ObjectIds = $ids; TablesWithSubcomponents = $withSub }
    Write-Host "Set-DocumentAccessPermissionSchema  -  SELF-TEST (offline)" -ForegroundColor White
    $cases = @(
        @{ Name = 'column absent'; Got = (Get-ColumnState $null $root); Want = 'absent' },
        @{ Name = 'column bound to the roots'' choice'; Got = (Get-ColumnState @{ AttributeType = 'Picklist'; OptionSetId = $root } $root); Want = 'match' },
        @{ Name = 'column bound to another global choice'; Got = ((Get-ColumnState @{ AttributeType = 'Picklist'; OptionSetId = $other } $root) -like 'mismatch*'); Want = $true },
        @{ Name = 'column with a local choice'; Got = ((Get-ColumnState @{ AttributeType = 'Picklist'; OptionSetId = $null } $root) -like 'mismatch*'); Want = $true },
        @{ Name = 'column of another type'; Got = ((Get-ColumnState @{ AttributeType = 'Integer'; OptionSetId = $null } $root) -like 'mismatch*'); Want = $true },
        @{ Name = 'solution: table with rcb 0 (ViaTable)'; Got = (Get-SolutionProblem $true (Test-DvInSolution -Membership $m -ComponentId $column -TableMetadataId $tableRcb0)); Want = $null },
        @{ Name = 'solution: the column''s own row (Direct)'; Got = (Get-SolutionProblem $true (Test-DvInSolution -Membership $m -ComponentId $directColumn -TableMetadataId $tableRcb1)); Want = $null },
        @{ Name = 'solution: table with rcb 1, no column row'; Got = ($null -ne (Get-SolutionProblem $true (Test-DvInSolution -Membership $m -ComponentId $column -TableMetadataId $tableRcb1))); Want = $true },
        @{ Name = 'solution: not found'; Got = ($null -ne (Get-SolutionProblem $false $null)); Want = $true }
    )
    $failures = 0
    foreach ($c in $cases) {
        if ($c.Got -eq $c.Want) { Write-Host ("  PASS  {0}" -f $c.Name) -ForegroundColor Green }
        else { $failures++; Write-Host ("  FAIL  {0} (got '{1}')" -f $c.Name, $c.Got) -ForegroundColor Red }
    }
    if ($failures -gt 0) { Write-Host "`nSELF-TEST FAIL: $failures case(s)." -ForegroundColor Red; exit 1 }
    Write-Host "`nSELF-TEST PASS: $($cases.Count) check(s)." -ForegroundColor Green
    exit 0
}

# ============================================================================
# Live
# ============================================================================

$BaseUrl = $EnvironmentUrl.TrimEnd('/')
$Token = $null

function Get-DataverseToken {
    $tokenResult = az account get-access-token --resource $BaseUrl --query "accessToken" -o tsv 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Failed to get a token from Azure CLI: $tokenResult. Run 'az login' first." }
    return "$tokenResult".Trim()
}

function Invoke-Dv {
    param([string]$Endpoint, [string]$Method = "GET", [object]$Body = $null, [switch]$AllowNotFound, [hashtable]$ExtraHeaders = @{})
    $headers = @{
        "Authorization"    = "Bearer $Token"
        "OData-MaxVersion" = "4.0"
        "OData-Version"    = "4.0"
        "Accept"           = "application/json"
        "Content-Type"     = "application/json; charset=utf-8"
    }
    foreach ($k in $ExtraHeaders.Keys) { $headers[$k] = $ExtraHeaders[$k] }
    $params = @{ Uri = "$BaseUrl/api/data/v9.2/$Endpoint"; Method = $Method; Headers = $headers }
    if ($null -ne $Body) { $params.Body = ($Body | ConvertTo-Json -Depth 20) }
    try { return Invoke-RestMethod @params }
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

function New-Label([string]$Text) {
    return @{
        "@odata.type"     = "Microsoft.Dynamics.CRM.Label"
        "LocalizedLabels" = @(@{ "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"; "Label" = $Text; "LanguageCode" = 1033 })
    }
}

function Stop-Refused([string]$Reason) {
    Write-Host ""
    Write-Host "REFUSED: $Reason" -ForegroundColor Red
    Write-Host "Nothing was changed." -ForegroundColor Red
    exit 2
}

function Get-ChoiceColumn([string]$OnTable) {
    $attr = Invoke-Dv -Endpoint "EntityDefinitions(LogicalName='$OnTable')/Attributes(LogicalName='$Column')?`$select=LogicalName,AttributeType,MetadataId" -AllowNotFound
    if ($null -eq $attr) { return $null }
    $optionSetId = $null
    if ("$($attr.AttributeType)" -eq 'Picklist') {
        $p = Invoke-Dv -Endpoint "EntityDefinitions(LogicalName='$OnTable')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?`$select=MetadataId&`$expand=GlobalOptionSet(`$select=MetadataId,Name,IsGlobal)"
        if ($p.GlobalOptionSet -and $p.GlobalOptionSet.IsGlobal) { $optionSetId = [string]$p.GlobalOptionSet.MetadataId; $optionSetName = [string]$p.GlobalOptionSet.Name }
    }
    return @{ AttributeType = [string]$attr.AttributeType; OptionSetId = $optionSetId; OptionSetName = $optionSetName; MetadataId = [string]$attr.MetadataId }
}

function Read-State {
    $rootAttr = Get-ChoiceColumn $RootTable
    $col = Get-ChoiceColumn $Table
    $entity = Invoke-Dv -Endpoint "EntityDefinitions(LogicalName='$Table')?`$select=MetadataId"
    $sol = Invoke-Dv -Endpoint "solutions?`$select=solutionid&`$filter=uniquename eq '$SolutionUniqueName'"
    $found = @($sol.value).Count -gt 0
    $how = $null
    $choiceIn = $null
    if ($found) {
        $dvHeaders = @{ Authorization = "Bearer $Token"; Accept = 'application/json'; 'OData-MaxVersion' = '4.0'; 'OData-Version' = '4.0' }
        $membership = Get-DvSolutionMembership -Api "$BaseUrl/api/data/v9.2" -Headers $dvHeaders -SolutionId $sol.value[0].solutionid
        $componentId = if ($null -ne $col -and $col.MetadataId) { $col.MetadataId } else { [guid]::Empty }
        $how = Test-DvInSolution -Membership $membership -ComponentId $componentId -TableMetadataId $entity.MetadataId
        if ($rootAttr -and $rootAttr.OptionSetId) { $choiceIn = Test-DvInSolution -Membership $membership -ComponentId $rootAttr.OptionSetId }
    }
    return @{
        Root            = $rootAttr
        Column          = $col
        ColumnState     = if ($rootAttr -and $rootAttr.OptionSetId) { Get-ColumnState $col $rootAttr.OptionSetId } else { 'root-unknown' }
        SolutionFound   = $found
        How             = $how
        SolutionProblem = (Get-SolutionProblem $found $how)
        ChoiceInSolution = $choiceIn
    }
}

$Mode = if ($Apply) { "APPLY" } elseif ($Verify) { "VERIFY (read-only)" } else { "DRY RUN (read-only; pass -Apply to perform the write)" }
Write-Host "Set-DocumentAccessPermissionSchema  -  $Mode" -ForegroundColor White
Write-Host "Environment: $BaseUrl"
Write-Host "Column     : $Table.$Column (Choice, bound to the global choice of $RootTable.$Column), solution $SolutionUniqueName"

try {
    $Token = Get-DataverseToken
    $state = Read-State
}
catch {
    if ($Verify) { Write-Host "`nVERIFY FAIL: read fault - $($_.Exception.Message)" -ForegroundColor Red; exit 1 }
    throw
}

$rootText = if ($state.Root -and $state.Root.OptionSetId) { "$($state.Root.OptionSetName) ($($state.Root.OptionSetId))" } else { 'UNKNOWN' }
Write-Host "Roots' choice   : $rootText"
Write-Host "Column state    : $($state.ColumnState)"
Write-Host "Solution check  : $(if ($state.SolutionProblem) { $state.SolutionProblem } else { "ships in $SolutionUniqueName ($($state.How))" })"
Write-Host "Global choice   : $(if ($state.ChoiceInSolution) { "in $SolutionUniqueName ($($state.ChoiceInSolution))" } else { "not a component of $SolutionUniqueName (informational: the roots' columns depend on it the same way)" })"

if ($Verify) {
    if ($state.ColumnState -eq 'match' -and -not $state.SolutionProblem) { Write-Host "`nVERIFY PASS" -ForegroundColor Green; exit 0 }
    Write-Host "`nVERIFY FAIL: column $($state.ColumnState)$(if ($state.SolutionProblem) { "; $($state.SolutionProblem)" })" -ForegroundColor Red
    exit 1
}
if ($state.ColumnState -eq 'root-unknown') { Stop-Refused "ROOT_CHOICE_UNKNOWN: $RootTable.$Column is missing or not bound to a global choice." }
if (-not $state.SolutionFound) { Stop-Refused "SOLUTION_MISSING: $SolutionUniqueName is not in the environment." }
if ($state.ColumnState -like 'mismatch*') { Stop-Refused "COLUMN_MISMATCH: $Table.$Column exists but is a $($state.ColumnState); it is never altered silently." }
if ($state.ColumnState -eq 'match' -and -not $state.SolutionProblem) { Write-Host "`nNothing to do: $Table.$Column exists, is bound to the roots' choice and ships in $SolutionUniqueName." -ForegroundColor Green; exit 0 }

$plan = if ($state.ColumnState -eq 'absent') { "create $Table.$Column under $SolutionUniqueName" } else { "add $Table.$Column to $SolutionUniqueName" }
if (-not $Apply) { Write-Host "`nDRY RUN complete - would $plan and publish $Table. Re-run with -Apply." -ForegroundColor White; exit 0 }

# ---- Apply ----
if ($state.ColumnState -eq 'absent') {
    $definition = @{
        "@odata.type"                = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
        "SchemaName"                 = "sprk_AccessPermission"
        "RequiredLevel"              = @{ "Value" = "None" }
        "IsAuditEnabled"             = @{ "Value" = $true }
        "DefaultFormValue"           = 100000000
        "DisplayName"                = New-Label "Access Permission"
        "Description"                = New-Label "Inherited from the records this document is filed under (most restrictive); a document with no parent keeps its own value. Display only - access is decided by the parent record."
        "GlobalOptionSet@odata.bind" = "/GlobalOptionSetDefinitions($($state.Root.OptionSetId))"
    }
    Invoke-Dv -Endpoint "EntityDefinitions(LogicalName='$Table')/Attributes" -Method POST -Body $definition -ExtraHeaders @{ 'MSCRM.SolutionUniqueName' = $SolutionUniqueName } | Out-Null
    Write-Host "   DONE  created $Table.$Column under $SolutionUniqueName" -ForegroundColor Green
}
else {
    Invoke-Dv -Endpoint "AddSolutionComponent" -Method POST -Body @{
        ComponentId = $state.Column.MetadataId; ComponentType = 2; SolutionUniqueName = $SolutionUniqueName; AddRequiredComponents = $false
    } | Out-Null
    Write-Host "   DONE  added $Table.$Column to $SolutionUniqueName" -ForegroundColor Green
}
Invoke-Dv -Endpoint "PublishXml" -Method POST -Body @{ ParameterXml = "<importexportxml><entities><entity>$Table</entity></entities></importexportxml>" } | Out-Null
Write-Host "   DONE  published $Table" -ForegroundColor Green
$after = Read-State
if ($after.ColumnState -ne 'match' -or $after.SolutionProblem) { throw "Read-back: $Table.$Column is $($after.ColumnState); $($after.SolutionProblem)" }
Write-Host "`nDone. Next: -Verify (exit 0)." -ForegroundColor Green
exit 0
