<#
.SYNOPSIS
    Adds sprk_regardingrecordurl to sprk_analysis, so every child table carries the full ADR-024 regarding pair
    (owner round 19 item 4). DRY RUN by default; -Verify checks it at any time.

.DESCRIPTION
    The RegardingResolver writes sprk_regardingrecordurl on every pick (applyResolverFields,
    PolymorphicResolverService.ts) and nulls it on every clear (ResolverWriteHandler.clearRegarding). sprk_todo,
    sprk_event and sprk_communication carry the column; sprk_analysis does not (live metadata, 2026-10-03), so the
    picker could not be placed on the analysis form: every re-file and clear would fail with "Invalid property".
    Owner round 19 item 4 (2026-10-04, binding) chose to add the column (option (a)), then the picker, then the
    lock (Add-RegardingFilingPickerToForms.ps1, then Lock-CoreAncestorStampColumnsOnForms.ps1).

    The column copies the sprk_todo definition: StringAttributeMetadata, SchemaName sprk_RegardingRecordURL,
    Format/FormatName Url, MaxLength 500, RequiredLevel None, audited, display name "Regarding Record URL".

    Solution membership: the column ships to customers only through SpaarkeMaster. The script REQUIRES that
    sprk_analysis is an entity component (componenttype 1) of SpaarkeMaster with rootcomponentbehavior = 0
    ("include all subcomponents"), so a new column is part of the solution without its own component row.

    Fail closed (ADR-003). REFUSES (exit 2, nothing written):
      SOLUTION_MEMBERSHIP  SpaarkeMaster is missing, or sprk_analysis is not in it with rootcomponentbehavior 0;
      COLUMN_MISMATCH      a sprk_regardingrecordurl already exists on sprk_analysis but is not the definition
                           above (another type, format or length) — never altered silently.
    -Verify: exit 0 only when the column exists with the definition above AND the solution check passes; any gap
    or read fault exits 1.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Default: https://spaarkedev1.crm.dynamics.com

.PARAMETER Apply
    Create the column and publish sprk_analysis. Without a mode switch: a read-only dry run.

.PARAMETER Verify
    Read-only check (exit 0 / 1).

.PARAMETER SelfTest
    Offline: runs the pure column and solution checks over inline cases; exits 1 on any mismatch.

.PARAMETER Table
    The table to check. Default sprk_analysis. -Apply refuses any other table.

.EXAMPLE
    .\Add-AnalysisRegardingRecordUrlColumn.ps1            # dry run
    .\Add-AnalysisRegardingRecordUrlColumn.ps1 -SelfTest  # offline
    .\Add-AnalysisRegardingRecordUrlColumn.ps1 -Apply     # create + publish + read back
    .\Add-AnalysisRegardingRecordUrlColumn.ps1 -Verify    # exit 0 / 1

.NOTES
    Project : unified-access-control-r2
    Task    : 168 (#1107) r1 — owner round 19 item 4
    Created : 2026-10-04
    Docs    : projects/unified-access-control-r2/notes/task-168-lock-root-columns-on-forms.md

    OPERATOR-RUN ONLY: -Apply is the main session's manual gate. Run it BEFORE Add-RegardingFilingPickerToForms.ps1,
    which refuses the analysis form (PAIR_INCOMPLETE) until the column exists. Requires Azure CLI (`az login`).

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
    [string]$Table = 'sprk_analysis'
)

$ErrorActionPreference = "Stop"

$modeCount = @($Apply.IsPresent, $Verify.IsPresent, $SelfTest.IsPresent) | Where-Object { $_ } | Measure-Object | Select-Object -ExpandProperty Count
if ($modeCount -gt 1) { throw "-Apply, -Verify and -SelfTest are separate modes; pass at most one." }

$Column = 'sprk_regardingrecordurl'
$SolutionName = 'SpaarkeMaster'
$Expected = @{ AttributeType = 'String'; FormatName = 'Url'; MaxLength = 500 }

# ============================================================================
# Pure checks
# ============================================================================

<#
    $Attr: $null (absent) or @{ AttributeType; FormatName; MaxLength }. Returns 'absent', 'match' or a mismatch text.
#>
function Get-ColumnState([object]$Attr) {
    if ($null -eq $Attr) { return 'absent' }
    $diff = @()
    if ("$($Attr.AttributeType)" -ne $Expected.AttributeType) { $diff += "type $($Attr.AttributeType) (want $($Expected.AttributeType))" }
    if ("$($Attr.FormatName)" -ne $Expected.FormatName) { $diff += "format $($Attr.FormatName) (want $($Expected.FormatName))" }
    if ([int]$Attr.MaxLength -lt $Expected.MaxLength) { $diff += "max length $($Attr.MaxLength) (want at least $($Expected.MaxLength))" }
    if ($diff.Count -eq 0) { return 'match' }
    return "mismatch: $($diff -join '; ')"
}

<#
    $SolutionFound: bool. $How: the shared helper's answer for the column (scripts/common/DataverseSolutionMembership.ps1,
    Test-DvInSolution: 'Direct', 'ViaTable' or $null) — for a column not yet created, asked of the TABLE (does it ship
    with all subcomponents?). Returns $null when the column ships in the solution, otherwise the reason.
    (Batch-4 integration: membership is decided ONE way, through the helper — SchemaScriptSolutionMembershipGuardTests.)
#>
function Get-SolutionProblem([bool]$SolutionFound, $How) {
    if (-not $SolutionFound) { return "solution $SolutionName not found" }
    if ($How -in 'Direct', 'ViaTable') { return $null }
    return "$Column is not in ${SolutionName}: neither its own component row nor $Table with rootcomponentbehavior 0 (include all subcomponents) — a new column would not ship"
}

. (Join-Path $PSScriptRoot 'common/DataverseSolutionMembership.ps1')

if ($SelfTest) {
    # An in-memory membership in the helper's own shape (Get-DvSolutionMembership's return value).
    $SelfTestTableRcb0 = [guid]::NewGuid(); $SelfTestTableRcb1 = [guid]::NewGuid()
    $SelfTestColumnId = [guid]::NewGuid(); $SelfTestDirectColumnId = [guid]::NewGuid()
    $ids = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    [void]$ids.Add("$SelfTestTableRcb0"); [void]$ids.Add("$SelfTestTableRcb1"); [void]$ids.Add("$SelfTestDirectColumnId")
    $withSub = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    [void]$withSub.Add("$SelfTestTableRcb0")
    $SelfTestMembership = [pscustomobject]@{ ObjectIds = $ids; TablesWithSubcomponents = $withSub }
    Write-Host "Add-AnalysisRegardingRecordUrlColumn  —  SELF-TEST (offline)" -ForegroundColor White
    $cases = @(
        @{ Name = 'column absent'; Got = (Get-ColumnState $null); Want = 'absent' },
        @{ Name = 'column matches'; Got = (Get-ColumnState @{ AttributeType = 'String'; FormatName = 'Url'; MaxLength = 500 }); Want = 'match' },
        @{ Name = 'column longer is fine'; Got = (Get-ColumnState @{ AttributeType = 'String'; FormatName = 'Url'; MaxLength = 2000 }); Want = 'match' },
        @{ Name = 'column as plain text'; Got = ((Get-ColumnState @{ AttributeType = 'String'; FormatName = 'Text'; MaxLength = 500 }) -like 'mismatch*'); Want = $true },
        @{ Name = 'column too short'; Got = ((Get-ColumnState @{ AttributeType = 'String'; FormatName = 'Url'; MaxLength = 200 }) -like 'mismatch*'); Want = $true },
        @{ Name = 'column of another type'; Got = ((Get-ColumnState @{ AttributeType = 'Memo'; FormatName = 'Url'; MaxLength = 500 }) -like 'mismatch*'); Want = $true },
        @{ Name = 'solution: table with rcb 0 (ViaTable)'; Got = (Get-SolutionProblem $true (Test-DvInSolution -Membership $SelfTestMembership -ComponentId $SelfTestColumnId -TableMetadataId $SelfTestTableRcb0)); Want = $null },
        @{ Name = 'solution: the column''s own row (Direct)'; Got = (Get-SolutionProblem $true (Test-DvInSolution -Membership $SelfTestMembership -ComponentId $SelfTestDirectColumnId -TableMetadataId $SelfTestTableRcb1)); Want = $null },
        @{ Name = 'solution: table with rcb 1, no column row'; Got = ($null -ne (Get-SolutionProblem $true (Test-DvInSolution -Membership $SelfTestMembership -ComponentId $SelfTestColumnId -TableMetadataId $SelfTestTableRcb1))); Want = $true },
        @{ Name = 'solution: table absent'; Got = ($null -ne (Get-SolutionProblem $true (Test-DvInSolution -Membership $SelfTestMembership -ComponentId $SelfTestColumnId -TableMetadataId ([guid]::NewGuid())))); Want = $true },
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
    param([string]$Endpoint, [string]$Method = "GET", [object]$Body = $null, [switch]$AllowNotFound)
    $headers = @{
        "Authorization"    = "Bearer $Token"
        "OData-MaxVersion" = "4.0"
        "OData-Version"    = "4.0"
        "Accept"           = "application/json"
        "Content-Type"     = "application/json; charset=utf-8"
    }
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

function Read-State {
    $attr = Invoke-Dv -Endpoint "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')?`$select=LogicalName,AttributeType,MetadataId" -AllowNotFound
    $col = $null
    if ($null -ne $attr) {
        $col = @{ AttributeType = [string]$attr.AttributeType; FormatName = ''; MaxLength = 0 }
        if ($col.AttributeType -eq 'String') {
            $s = Invoke-Dv -Endpoint "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.StringAttributeMetadata?`$select=MaxLength,FormatName"
            $col.FormatName = [string]$s.FormatName.Value
            $col.MaxLength = [int]$s.MaxLength
        }
    }
    $entity = Invoke-Dv -Endpoint "EntityDefinitions(LogicalName='$Table')?`$select=MetadataId"
    $sol = Invoke-Dv -Endpoint "solutions?`$select=solutionid&`$filter=uniquename eq '$SolutionName'"
    $found = @($sol.value).Count -gt 0
    $how = $null
    if ($found) {
        $dvHeaders = @{ Authorization = "Bearer $Token"; Accept = 'application/json'; 'OData-MaxVersion' = '4.0'; 'OData-Version' = '4.0' }
        $membership = Get-DvSolutionMembership -Api "$BaseUrl/api/data/v9.2" -Headers $dvHeaders -SolutionId $sol.value[0].solutionid
        # A column not created yet has no id of its own: ask whether its table ships with all subcomponents.
        $componentId = if ($null -ne $attr -and $attr.MetadataId) { $attr.MetadataId } else { [guid]::Empty }
        $how = Test-DvInSolution -Membership $membership -ComponentId $componentId -TableMetadataId $entity.MetadataId
    }
    return @{ ColumnState = (Get-ColumnState $col); SolutionProblem = (Get-SolutionProblem $found $how) }
}

$Mode = if ($Apply) { "APPLY" } elseif ($Verify) { "VERIFY (read-only)" } else { "DRY RUN (read-only; pass -Apply to perform the write)" }
Write-Host "Add-AnalysisRegardingRecordUrlColumn  —  $Mode" -ForegroundColor White
Write-Host "Environment: $BaseUrl"
Write-Host "Column     : $Table.$Column (String, Url, 500)"

if ($Apply -and $Table -ne 'sprk_analysis') { Stop-Refused "-Apply adds the column to sprk_analysis only (round 19 item 4), not $Table." }

try {
    $Token = Get-DataverseToken
    $state = Read-State
}
catch {
    if ($Verify) { Write-Host "`nVERIFY FAIL: read fault — $($_.Exception.Message)" -ForegroundColor Red; exit 1 }
    throw
}
Write-Host "Column state    : $($state.ColumnState)"
Write-Host "Solution check  : $(if ($state.SolutionProblem) { $state.SolutionProblem } else { "$Table ships in $SolutionName with all subcomponents" })"

if ($Verify) {
    if ($state.ColumnState -eq 'match' -and -not $state.SolutionProblem) { Write-Host "`nVERIFY PASS" -ForegroundColor Green; exit 0 }
    Write-Host "`nVERIFY FAIL: column $($state.ColumnState)$(if ($state.SolutionProblem) { "; $($state.SolutionProblem)" })" -ForegroundColor Red
    exit 1
}
if ($state.SolutionProblem) { Stop-Refused "SOLUTION_MEMBERSHIP: $($state.SolutionProblem)" }
if ($state.ColumnState -like 'mismatch*') { Stop-Refused "COLUMN_MISMATCH: $Table.$Column exists but is a $($state.ColumnState); it is never altered silently." }
if ($state.ColumnState -eq 'match') { Write-Host "`nNothing to do: $Table.$Column exists with the expected definition." -ForegroundColor Green; exit 0 }
if (-not $Apply) { Write-Host "`nDRY RUN complete — would create $Table.$Column and publish $Table. Re-run with -Apply." -ForegroundColor White; exit 0 }

# ---- Apply ----
$definition = @{
    "@odata.type"    = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
    "SchemaName"     = "sprk_RegardingRecordURL"
    "RequiredLevel"  = @{ "Value" = "None" }
    "MaxLength"      = 500
    "FormatName"     = @{ "Value" = "Url" }
    "IsAuditEnabled" = @{ "Value" = $true }
    "DisplayName"    = New-Label "Regarding Record URL"
    "Description"    = New-Label "Resolver: clickable link to regarding record."
}
Invoke-Dv -Endpoint "EntityDefinitions(LogicalName='$Table')/Attributes" -Method POST -Body $definition | Out-Null
Write-Host "   DONE  created $Table.$Column" -ForegroundColor Green
Invoke-Dv -Endpoint "PublishXml" -Method POST -Body @{ ParameterXml = "<importexportxml><entities><entity>$Table</entity></entities></importexportxml>" } | Out-Null
Write-Host "   DONE  published $Table" -ForegroundColor Green
$after = Read-State
if ($after.ColumnState -ne 'match') { throw "Read-back: $Table.$Column is $($after.ColumnState) after the create." }
Write-Host "`nCreated $Table.$Column. Next: -Verify (exit 0), then Add-RegardingFilingPickerToForms.ps1." -ForegroundColor Green
exit 0
