<#
.SYNOPSIS
    Creates sprk_accessinheritance (Multiple lines of text) on sprk_workassignment and sprk_project and ships it in
    SpaarkeCore (task 175, owner round 87). DRY RUN by default; -Verify checks it at any time.

.DESCRIPTION
    Owner round 87 (2026-10-09, binding): the parent sets a FLOOR for a filed work assignment's or project's Secure
    designation and Access Permission; a user may make the child stricter, and a value set ON the child stays when the
    parent later loosens - only inherited values follow the parent down; a re-file never loosens a child.

    The BFF records, per work assignment and project, what its stored values were derived from - the floor, the parents it
    came from, and what was set on the record - as one small versioned JSON document in sprk_accessinheritance (written only
    by the BFF; never on a form). This script makes that column exist in an environment:

      - Multiple lines of text (Memo), MaxLength 4000, not required, audited, on both tables;
      - it ships in the SpaarkeCore solution: its own component row, or the table in SpaarkeCore with rootcomponentbehavior 0.
        Decided ONE way, through scripts/common/DataverseSolutionMembership.ps1 (Test-DvInSolution).

    -Apply creates the column where it is absent (under the solution header) and adds an existing column to SpaarkeCore when
    it is not in it, then publishes both tables. It never alters an existing column.

    Fail closed (ADR-003). REFUSES (exit 2, nothing written):
      COLUMN_MISMATCH   the column exists on a table but is not Multiple lines of text (never altered silently);
      SOLUTION_MISSING  the SpaarkeCore solution is not in the environment.
    -Verify: exit 0 only when the column exists on both tables, matches, and ships in SpaarkeCore; any gap or read fault exits 1.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Default: https://spaarkedev1.crm.dynamics.com

.PARAMETER Apply
    Create the column (or add it to SpaarkeCore) and publish. Without a mode switch: a read-only dry run.

.PARAMETER Verify
    Read-only check (exit 0 / 1).

.PARAMETER SelfTest
    Offline: runs the pure column and solution checks over inline cases; exits 1 on any mismatch.

.PARAMETER SolutionUniqueName
    The unmanaged solution the column ships in. Default SpaarkeCore.

.EXAMPLE
    .\Set-AccessInheritanceSchema.ps1            # dry run
    .\Set-AccessInheritanceSchema.ps1 -SelfTest  # offline
    .\Set-AccessInheritanceSchema.ps1 -Apply     # create / add to solution + publish + read back
    .\Set-AccessInheritanceSchema.ps1 -Verify    # exit 0 / 1

.NOTES
    Project : unified-access-control-r2
    Task    : 175 (#1478) - owner rounds 84 and 87
    Created : 2026-10-09
    Docs    : docs/data-model/access-inheritance.md; projects/unified-access-control-r2/notes/task-175-child-access-cascade.md

    OPERATOR-RUN ONLY: -Apply is the main session's manual gate. Run it BEFORE deploying a BFF with task 175: the BFF reads
    the column (a BFF whose reads of it fail decides nothing about loosening: the follow-parents pass fails its run, and the
    inline follow reports the record as unreadable). Requires Azure CLI (`az login`).

    Backfill: none by script. The BFF writes the column the first time its job (or an inline follow) sees each record, by
    the backfill rule (a value equal to the parents' floor is inherited; a value stricter than it is set on the record).

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

$Tables = @('sprk_workassignment', 'sprk_project')
$Column = 'sprk_accessinheritance'
$MaxLength = 4000

# ============================================================================
# Pure checks
# ============================================================================

<# $Attr: $null (absent) or @{ AttributeType; MaxLength }. Returns 'absent', 'match' or a mismatch text. #>
function Get-ColumnState([object]$Attr) {
    if ($null -eq $Attr) { return 'absent' }
    if ("$($Attr.AttributeType)" -ne 'Memo') { return "mismatch: type $($Attr.AttributeType) (want Memo)" }
    if ($null -ne $Attr.MaxLength -and [int]$Attr.MaxLength -lt 1000) { return "mismatch: MaxLength $($Attr.MaxLength) (want at least 1000)" }
    return 'match'
}

<# $SolutionFound: bool. $How: Test-DvInSolution's answer ('Direct', 'ViaTable' or $null). #>
function Get-SolutionProblem([string]$Table, [bool]$SolutionFound, $How) {
    if (-not $SolutionFound) { return "solution $SolutionUniqueName not found" }
    if ($How -in 'Direct', 'ViaTable') { return $null }
    return "$Table.$Column is not in ${SolutionUniqueName}: neither its own component row nor $Table with rootcomponentbehavior 0"
}

. (Join-Path $PSScriptRoot 'common/DataverseSolutionMembership.ps1')

if ($SelfTest) {
    $tableRcb0 = [guid]::NewGuid(); $tableRcb1 = [guid]::NewGuid(); $column = [guid]::NewGuid(); $directColumn = [guid]::NewGuid()
    $ids = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    [void]$ids.Add("$tableRcb0"); [void]$ids.Add("$tableRcb1"); [void]$ids.Add("$directColumn")
    $withSub = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    [void]$withSub.Add("$tableRcb0")
    $m = [pscustomobject]@{ ObjectIds = $ids; TablesWithSubcomponents = $withSub }
    Write-Host "Set-AccessInheritanceSchema  -  SELF-TEST (offline)" -ForegroundColor White
    $cases = @(
        @{ Name = 'column absent'; Got = (Get-ColumnState $null); Want = 'absent' },
        @{ Name = 'memo column, 4000'; Got = (Get-ColumnState @{ AttributeType = 'Memo'; MaxLength = 4000 }); Want = 'match' },
        @{ Name = 'memo column too short'; Got = ((Get-ColumnState @{ AttributeType = 'Memo'; MaxLength = 100 }) -like 'mismatch*'); Want = $true },
        @{ Name = 'single line of text'; Got = ((Get-ColumnState @{ AttributeType = 'String'; MaxLength = 4000 }) -like 'mismatch*'); Want = $true },
        @{ Name = 'solution: table with rcb 0 (ViaTable)'; Got = (Get-SolutionProblem 't' $true (Test-DvInSolution -Membership $m -ComponentId $column -TableMetadataId $tableRcb0)); Want = $null },
        @{ Name = 'solution: the column''s own row (Direct)'; Got = (Get-SolutionProblem 't' $true (Test-DvInSolution -Membership $m -ComponentId $directColumn -TableMetadataId $tableRcb1)); Want = $null },
        @{ Name = 'solution: table with rcb 1, no column row'; Got = ($null -ne (Get-SolutionProblem 't' $true (Test-DvInSolution -Membership $m -ComponentId $column -TableMetadataId $tableRcb1))); Want = $true },
        @{ Name = 'solution: not found'; Got = ($null -ne (Get-SolutionProblem 't' $false $null)); Want = $true }
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

function Get-Column([string]$OnTable) {
    $attr = Invoke-Dv -Endpoint "EntityDefinitions(LogicalName='$OnTable')/Attributes(LogicalName='$Column')?`$select=LogicalName,AttributeType,MetadataId" -AllowNotFound
    if ($null -eq $attr) { return $null }
    $max = $null
    if ("$($attr.AttributeType)" -eq 'Memo') {
        $memo = Invoke-Dv -Endpoint "EntityDefinitions(LogicalName='$OnTable')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.MemoAttributeMetadata?`$select=MaxLength"
        $max = $memo.MaxLength
    }
    return @{ AttributeType = [string]$attr.AttributeType; MaxLength = $max; MetadataId = [string]$attr.MetadataId }
}

function Read-State {
    $sol = Invoke-Dv -Endpoint "solutions?`$select=solutionid&`$filter=uniquename eq '$SolutionUniqueName'"
    $found = @($sol.value).Count -gt 0
    $membership = $null
    if ($found) {
        $dvHeaders = @{ Authorization = "Bearer $Token"; Accept = 'application/json'; 'OData-MaxVersion' = '4.0'; 'OData-Version' = '4.0' }
        $membership = Get-DvSolutionMembership -Api "$BaseUrl/api/data/v9.2" -Headers $dvHeaders -SolutionId $sol.value[0].solutionid
    }
    $states = @()
    foreach ($table in $Tables) {
        $col = Get-Column $table
        $entity = Invoke-Dv -Endpoint "EntityDefinitions(LogicalName='$table')?`$select=MetadataId"
        $how = $null
        if ($found) {
            $componentId = if ($null -ne $col -and $col.MetadataId) { $col.MetadataId } else { [guid]::Empty }
            $how = Test-DvInSolution -Membership $membership -ComponentId $componentId -TableMetadataId $entity.MetadataId
        }
        $states += @{
            Table           = $table
            Column          = $col
            ColumnState     = (Get-ColumnState $col)
            SolutionProblem = (Get-SolutionProblem $table $found $how)
            How             = $how
        }
    }
    return @{ SolutionFound = $found; Tables = $states }
}

$Mode = if ($Apply) { "APPLY" } elseif ($Verify) { "VERIFY (read-only)" } else { "DRY RUN (read-only; pass -Apply to perform the write)" }
Write-Host "Set-AccessInheritanceSchema  -  $Mode" -ForegroundColor White
Write-Host "Environment: $BaseUrl"
Write-Host "Column     : $Column (Multiple lines of text, $MaxLength) on $($Tables -join ', '), solution $SolutionUniqueName"

try {
    $Token = Get-DataverseToken
    $state = Read-State
}
catch {
    if ($Verify) { Write-Host "`nVERIFY FAIL: read fault - $($_.Exception.Message)" -ForegroundColor Red; exit 1 }
    throw
}

foreach ($t in $state.Tables) {
    Write-Host ("{0,-22}: column {1}; {2}" -f $t.Table, $t.ColumnState,
        $(if ($t.SolutionProblem) { $t.SolutionProblem } else { "ships in $SolutionUniqueName ($($t.How))" }))
}

$allMatch = @($state.Tables | Where-Object { $_.ColumnState -ne 'match' -or $_.SolutionProblem }).Count -eq 0
if ($Verify) {
    if ($allMatch) { Write-Host "`nVERIFY PASS" -ForegroundColor Green; exit 0 }
    Write-Host "`nVERIFY FAIL" -ForegroundColor Red
    exit 1
}
if (-not $state.SolutionFound) { Stop-Refused "SOLUTION_MISSING: $SolutionUniqueName is not in the environment." }
$mismatch = @($state.Tables | Where-Object { $_.ColumnState -like 'mismatch*' })
if ($mismatch.Count -gt 0) { Stop-Refused "COLUMN_MISMATCH: $(($mismatch | ForEach-Object { "$($_.Table): $($_.ColumnState)" }) -join '; '); never altered silently." }
if ($allMatch) { Write-Host "`nNothing to do: $Column exists on both tables and ships in $SolutionUniqueName." -ForegroundColor Green; exit 0 }

$todo = @($state.Tables | Where-Object { $_.ColumnState -ne 'match' -or $_.SolutionProblem })
if (-not $Apply) {
    foreach ($t in $todo) {
        Write-Host ("  would {0} {1}.{2}" -f $(if ($t.ColumnState -eq 'absent') { 'create' } else { "add to $SolutionUniqueName" }), $t.Table, $Column)
    }
    Write-Host "`nDRY RUN complete - re-run with -Apply." -ForegroundColor White
    exit 0
}

# ---- Apply ----
foreach ($t in $todo) {
    if ($t.ColumnState -eq 'absent') {
        $definition = @{
            "@odata.type"    = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
            "SchemaName"     = "sprk_AccessInheritance"
            "RequiredLevel"  = @{ "Value" = "None" }
            "IsAuditEnabled" = @{ "Value" = $true }
            "MaxLength"      = $MaxLength
            "Format"         = "TextArea"
            "DisplayName"    = New-Label "Access Inheritance (system)"
            "Description"    = New-Label "Written by the Spaarke BFF only: what this record's Secure designation and Access Permission were derived from (the floor its parents set, the parents, and what was set on this record). Owner round 87."
        }
        Invoke-Dv -Endpoint "EntityDefinitions(LogicalName='$($t.Table)')/Attributes" -Method POST -Body $definition -ExtraHeaders @{ 'MSCRM.SolutionUniqueName' = $SolutionUniqueName } | Out-Null
        Write-Host "   DONE  created $($t.Table).$Column under $SolutionUniqueName" -ForegroundColor Green
    }
    else {
        Invoke-Dv -Endpoint "AddSolutionComponent" -Method POST -Body @{
            ComponentId = $t.Column.MetadataId; ComponentType = 2; SolutionUniqueName = $SolutionUniqueName; AddRequiredComponents = $false
        } | Out-Null
        Write-Host "   DONE  added $($t.Table).$Column to $SolutionUniqueName" -ForegroundColor Green
    }
}
$entitiesXml = ($Tables | ForEach-Object { "<entity>$_</entity>" }) -join ''
Invoke-Dv -Endpoint "PublishXml" -Method POST -Body @{ ParameterXml = "<importexportxml><entities>$entitiesXml</entities></importexportxml>" } | Out-Null
Write-Host "   DONE  published $($Tables -join ', ')" -ForegroundColor Green
$after = Read-State
$left = @($after.Tables | Where-Object { $_.ColumnState -ne 'match' -or $_.SolutionProblem })
if ($left.Count -gt 0) { throw "Read-back: $(($left | ForEach-Object { "$($_.Table): $($_.ColumnState) $($_.SolutionProblem)" }) -join '; ')" }
Write-Host "`nDone. Next: -Verify (exit 0)." -ForegroundColor Green
exit 0
