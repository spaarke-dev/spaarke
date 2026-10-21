<#
.SYNOPSIS
    Brings sprk_document.sprk_accesspermission into source: the column exists, is bound to the SAME global choice as the
    roots' Access Permission, and ships in SpaarkeCore (owner round 81). Task 189 adds the document's own-narrowing record,
    sprk_document.sprk_accessinheritance (NOT field-secured). DRY RUN by default; -Verify checks both at any time.

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

    Task 189 (D7 T7a, GitHub #2126): the RECORD column sprk_document.sprk_accessinheritance - what the document's Access
    Permission was derived from, as task 175's v1 JSON (the parents, the floor they set, and the document's own
    narrowing). The BFF keeps a document at max(own narrowing, inherited) and never widens its own narrowing, so it must
    know which part is the document's own. Multiple lines of text (Memo), MaxLength 4000, not required, audited, created
    under SpaarkeCore, and NOT field-secured (decision D1, notes/task-189-own-narrowing.md section 2.4): the inherited part
    is always recomputed from the parents, never read from the record, so an edit of the record can only remove the
    user's own narrowing or add one - never take the document below its parents. The same column name IS field-secured on
    sprk_workassignment and sprk_project (scripts/Set-AccessInheritanceSchema.ps1); that script does not touch documents.
    -Apply creates it when absent, or adds it to SpaarkeCore; it never alters an existing one.

    Fail closed (ADR-003). REFUSES (exit 2, nothing written):
      ROOT_CHOICE_UNKNOWN  sprk_matter.sprk_accesspermission is missing or not bound to a global choice (nothing to bind to);
      COLUMN_MISMATCH      sprk_document.sprk_accesspermission exists but is not a Choice bound to the roots' global choice
                           (task 173 escalation trigger: never altered silently);
      SOLUTION_MISSING     the SpaarkeCore solution is not in the environment;
      RECORD_MISMATCH      sprk_document.sprk_accessinheritance exists but is not Memo (MaxLength >= 1000), or is
                           field-secured (task 189: never altered silently).
    -Verify: exit 0 only when BOTH columns exist, match, and ship in SpaarkeCore, and the record column is on no
    sprk_document form (it is the BFF's bookkeeping, not a field for users); any gap or read fault exits 1.

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
    Task    : 173 (#1423) - owner round 81; 189 (#2126) - the own-narrowing record column
    Created : 2026-10-08
    Docs    : docs/data-model/child-access-permission.md; docs/data-model/access-inheritance.md;
              projects/unified-access-control-r2/notes/task-173-child-access-permission.md;
              projects/unified-access-control-r2/notes/task-189-own-narrowing.md

    OPERATOR-RUN ONLY: -Apply is the main session's manual gate. Run it BEFORE Set-InheritedAccessPermissionFormLock.ps1
    (which refuses COLUMN_MISSING until the column exists) and before a BFF with task 173 writes the column in a new
    environment (the BFF skips a table that lacks it, so the order is safe either way). Task 189: run it BEFORE a BFF with
    task 189 - until the record column exists, that BFF only raises a document's Access Permission and never lowers it
    (the run reports sprk_document in tablesWithoutColumn), and a raise it keeps is recorded as the document's own on the
    first run after this script (SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md section 7.4 says how to find and widen those).
    Requires Azure CLI (`az login`).

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
$RecordColumn = 'sprk_accessinheritance'   # task 189
$RecordMaxLength = 4000

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
function Get-SolutionProblem([bool]$SolutionFound, $How, [string]$Name = $Column) {
    if (-not $SolutionFound) { return "solution $SolutionUniqueName not found" }
    if ($How -in 'Direct', 'ViaTable') { return $null }
    return "$Table.$Name is not in ${SolutionUniqueName}: neither its own component row nor $Table with rootcomponentbehavior 0"
}

<#
    Task 189. $Attr: $null (absent) or @{ AttributeType; MaxLength; IsSecured }. Returns 'absent', 'match' or a mismatch
    text. Field-secured is a MISMATCH: the document's record is deliberately not secured (D1), and a secured column the BFF
    cannot read answers empty, which would drop a real own narrowing.
#>
function Get-RecordColumnState([object]$Attr) {
    if ($null -eq $Attr) { return 'absent' }
    if ("$($Attr.AttributeType)" -ne 'Memo') { return "mismatch: type $($Attr.AttributeType) (want Memo)" }
    if ($null -ne $Attr.MaxLength -and [int]$Attr.MaxLength -lt 1000) { return "mismatch: MaxLength $($Attr.MaxLength) (want at least 1000)" }
    if ($Attr.IsSecured) { return 'mismatch: field-secured (want NOT secured on sprk_document, task 189 D1)' }
    return 'match'
}

<#
    Task 189. $Forms: @(@{ Name; FormXml }) - the sprk_document forms. Returns the names of the forms that bind the record
    column (none expected: it is the BFF's bookkeeping), or an empty array.
#>
function Get-FormsWithRecord([object[]]$Forms) {
    $pattern = 'datafieldname="' + $RecordColumn + '"'
    return @($Forms | Where-Object { "$($_.FormXml)" -match [regex]::Escape($pattern) } | ForEach-Object { "$($_.Name)" })
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
        @{ Name = 'solution: not found'; Got = ($null -ne (Get-SolutionProblem $false $null)); Want = $true },
        @{ Name = 'record: absent'; Got = (Get-RecordColumnState $null); Want = 'absent' },
        @{ Name = 'record: memo 4000, not secured'; Got = (Get-RecordColumnState @{ AttributeType = 'Memo'; MaxLength = 4000; IsSecured = $false }); Want = 'match' },
        @{ Name = 'record: field-secured'; Got = ((Get-RecordColumnState @{ AttributeType = 'Memo'; MaxLength = 4000; IsSecured = $true }) -like 'mismatch*'); Want = $true },
        @{ Name = 'record: memo too short'; Got = ((Get-RecordColumnState @{ AttributeType = 'Memo'; MaxLength = 100; IsSecured = $false }) -like 'mismatch*'); Want = $true },
        @{ Name = 'record: single line of text'; Got = ((Get-RecordColumnState @{ AttributeType = 'String'; MaxLength = 4000; IsSecured = $false }) -like 'mismatch*'); Want = $true },
        @{ Name = 'record: solution problem names the record column'; Got = ((Get-SolutionProblem $true $null $RecordColumn) -like "*$RecordColumn*"); Want = $true },
        @{ Name = 'record: on no form'; Got = (@(Get-FormsWithRecord @(@{ Name = 'Information'; FormXml = '<form><control datafieldname="sprk_accesspermission" /></form>' })).Count); Want = 0 },
        @{ Name = 'record: on a form'; Got = ((Get-FormsWithRecord @(@{ Name = 'Information'; FormXml = '<form><control datafieldname="sprk_accessinheritance" /></form>' })) -join ','); Want = 'Information' }
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

function Get-RecordColumn {
    $attr = Invoke-Dv -Endpoint "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$RecordColumn')?`$select=LogicalName,AttributeType,MetadataId,IsSecured" -AllowNotFound
    if ($null -eq $attr) { return $null }
    $max = $null
    if ("$($attr.AttributeType)" -eq 'Memo') {
        $memo = Invoke-Dv -Endpoint "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$RecordColumn')/Microsoft.Dynamics.CRM.MemoAttributeMetadata?`$select=MaxLength"
        $max = $memo.MaxLength
    }
    return @{ AttributeType = [string]$attr.AttributeType; MaxLength = $max; MetadataId = [string]$attr.MetadataId; IsSecured = [bool]$attr.IsSecured }
}

function Get-DocumentForms {
    $forms = Invoke-Dv -Endpoint "systemforms?`$select=name,formxml&`$filter=objecttypecode eq '$Table'"
    return @($forms.value | ForEach-Object { @{ Name = [string]$_.name; FormXml = [string]$_.formxml } })
}

function Read-State {
    $rootAttr = Get-ChoiceColumn $RootTable
    $col = Get-ChoiceColumn $Table
    $record = Get-RecordColumn
    $formsWithRecord = @(Get-FormsWithRecord (Get-DocumentForms))
    $entity = Invoke-Dv -Endpoint "EntityDefinitions(LogicalName='$Table')?`$select=MetadataId"
    $sol = Invoke-Dv -Endpoint "solutions?`$select=solutionid&`$filter=uniquename eq '$SolutionUniqueName'"
    $found = @($sol.value).Count -gt 0
    $how = $null
    $recordHow = $null
    $choiceIn = $null
    if ($found) {
        $dvHeaders = @{ Authorization = "Bearer $Token"; Accept = 'application/json'; 'OData-MaxVersion' = '4.0'; 'OData-Version' = '4.0' }
        $membership = Get-DvSolutionMembership -Api "$BaseUrl/api/data/v9.2" -Headers $dvHeaders -SolutionId $sol.value[0].solutionid
        $componentId = if ($null -ne $col -and $col.MetadataId) { $col.MetadataId } else { [guid]::Empty }
        $how = Test-DvInSolution -Membership $membership -ComponentId $componentId -TableMetadataId $entity.MetadataId
        $recordId = if ($null -ne $record -and $record.MetadataId) { $record.MetadataId } else { [guid]::Empty }
        $recordHow = Test-DvInSolution -Membership $membership -ComponentId $recordId -TableMetadataId $entity.MetadataId
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
        Record          = $record
        RecordState     = (Get-RecordColumnState $record)
        RecordSolutionProblem = (Get-SolutionProblem $found $recordHow $RecordColumn)
        FormsWithRecord = $formsWithRecord
    }
}

$Mode = if ($Apply) { "APPLY" } elseif ($Verify) { "VERIFY (read-only)" } else { "DRY RUN (read-only; pass -Apply to perform the write)" }
Write-Host "Set-DocumentAccessPermissionSchema  -  $Mode" -ForegroundColor White
Write-Host "Environment: $BaseUrl"
Write-Host "Column     : $Table.$Column (Choice, bound to the global choice of $RootTable.$Column), solution $SolutionUniqueName"
Write-Host "Record     : $Table.$RecordColumn (Multiple lines of text, $RecordMaxLength, NOT field-secured; task 189), solution $SolutionUniqueName"

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
$recordText = if ($state.Record) { "$($state.RecordState) (type $($state.Record.AttributeType), MaxLength $($state.Record.MaxLength), IsSecured $($state.Record.IsSecured))" } else { $state.RecordState }
Write-Host "Record state    : $recordText"
Write-Host "Record solution : $(if ($state.RecordSolutionProblem) { $state.RecordSolutionProblem } else { "ships in $SolutionUniqueName" })"
Write-Host "Record on forms : $(if ($state.FormsWithRecord.Count -gt 0) { $state.FormsWithRecord -join ', ' } else { 'none' })"

$columnDone = $state.ColumnState -eq 'match' -and -not $state.SolutionProblem
$recordDone = $state.RecordState -eq 'match' -and -not $state.RecordSolutionProblem
if ($Verify) {
    if ($columnDone -and $recordDone -and $state.FormsWithRecord.Count -eq 0) { Write-Host "`nVERIFY PASS" -ForegroundColor Green; exit 0 }
    $gaps = @()
    if (-not $columnDone) { $gaps += "column $($state.ColumnState)$(if ($state.SolutionProblem) { "; $($state.SolutionProblem)" })" }
    if (-not $recordDone) { $gaps += "record $($state.RecordState)$(if ($state.RecordSolutionProblem) { "; $($state.RecordSolutionProblem)" })" }
    if ($state.FormsWithRecord.Count -gt 0) { $gaps += "record on form(s) $($state.FormsWithRecord -join ', ')" }
    Write-Host "`nVERIFY FAIL: $($gaps -join ' | ')" -ForegroundColor Red
    exit 1
}
if ($state.ColumnState -eq 'root-unknown') { Stop-Refused "ROOT_CHOICE_UNKNOWN: $RootTable.$Column is missing or not bound to a global choice." }
if (-not $state.SolutionFound) { Stop-Refused "SOLUTION_MISSING: $SolutionUniqueName is not in the environment." }
if ($state.ColumnState -like 'mismatch*') { Stop-Refused "COLUMN_MISMATCH: $Table.$Column exists but is a $($state.ColumnState); it is never altered silently." }
if ($state.RecordState -like 'mismatch*') { Stop-Refused "RECORD_MISMATCH: $Table.$RecordColumn exists but is a $($state.RecordState); it is never altered silently." }
if ($columnDone -and $recordDone) { Write-Host "`nNothing to do: $Table.$Column and $Table.$RecordColumn exist, match and ship in $SolutionUniqueName." -ForegroundColor Green; exit 0 }

$plan = @()
if (-not $columnDone) { $plan += if ($state.ColumnState -eq 'absent') { "create $Table.$Column under $SolutionUniqueName" } else { "add $Table.$Column to $SolutionUniqueName" } }
if (-not $recordDone) { $plan += if ($state.RecordState -eq 'absent') { "create $Table.$RecordColumn (not secured) under $SolutionUniqueName" } else { "add $Table.$RecordColumn to $SolutionUniqueName" } }
if (-not $Apply) { Write-Host "`nDRY RUN complete - would $($plan -join '; ') and publish $Table. Re-run with -Apply." -ForegroundColor White; exit 0 }

# ---- Apply ----
if (-not $columnDone -and $state.ColumnState -eq 'absent') {
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
elseif (-not $columnDone) {
    Invoke-Dv -Endpoint "AddSolutionComponent" -Method POST -Body @{
        ComponentId = $state.Column.MetadataId; ComponentType = 2; SolutionUniqueName = $SolutionUniqueName; AddRequiredComponents = $false
    } | Out-Null
    Write-Host "   DONE  added $Table.$Column to $SolutionUniqueName" -ForegroundColor Green
}
# Task 189: the own-narrowing record column - created NOT secured (D1); never altered when it exists.
if (-not $recordDone -and $state.RecordState -eq 'absent') {
    $recordDefinition = @{
        "@odata.type"    = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
        "SchemaName"     = "sprk_AccessInheritance"
        "RequiredLevel"  = @{ "Value" = "None" }
        "IsAuditEnabled" = @{ "Value" = $true }
        "IsSecured"      = $false
        "MaxLength"      = $RecordMaxLength
        "Format"         = "TextArea"
        "DisplayName"    = New-Label "Access Inheritance (system)"
        "Description"    = New-Label "Written by the Spaarke BFF: what this document's Access Permission was derived from (its parents, the value they give it, and its own narrowing). A document holds the stricter of its own narrowing and its parents' value. Task 189."
    }
    Invoke-Dv -Endpoint "EntityDefinitions(LogicalName='$Table')/Attributes" -Method POST -Body $recordDefinition -ExtraHeaders @{ 'MSCRM.SolutionUniqueName' = $SolutionUniqueName } | Out-Null
    Write-Host "   DONE  created $Table.$RecordColumn (not secured) under $SolutionUniqueName" -ForegroundColor Green
}
elseif (-not $recordDone) {
    Invoke-Dv -Endpoint "AddSolutionComponent" -Method POST -Body @{
        ComponentId = $state.Record.MetadataId; ComponentType = 2; SolutionUniqueName = $SolutionUniqueName; AddRequiredComponents = $false
    } | Out-Null
    Write-Host "   DONE  added $Table.$RecordColumn to $SolutionUniqueName" -ForegroundColor Green
}
Invoke-Dv -Endpoint "PublishXml" -Method POST -Body @{ ParameterXml = "<importexportxml><entities><entity>$Table</entity></entities></importexportxml>" } | Out-Null
Write-Host "   DONE  published $Table" -ForegroundColor Green
$after = Read-State
if ($after.ColumnState -ne 'match' -or $after.SolutionProblem) { throw "Read-back: $Table.$Column is $($after.ColumnState); $($after.SolutionProblem)" }
if ($after.RecordState -ne 'match' -or $after.RecordSolutionProblem) { throw "Read-back: $Table.$RecordColumn is $($after.RecordState); $($after.RecordSolutionProblem)" }
Write-Host "`nDone. Next: -Verify (exit 0)." -ForegroundColor Green
exit 0
