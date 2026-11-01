<#
.SYNOPSIS
    Test fixture for tests/scripts/Deploy-EbillingStoredFileSchema.Tests.ps1 (ebilling-r1 task 088): runs
    scripts/ebilling/Deploy-EbillingStoredFileSchema.ps1 in THIS process against a fake Dataverse. No network call is made.

.DESCRIPTION
    Invoke-RestMethod (and `az`, Start-Sleep) are replaced by global functions, so the module's own Invoke-Dv reaches the fake. A GET
    is answered from a state that mirrors dev after run 1 of task 088 (2026-10-22, after both BigInt columns were deleted):
    option set, table, every column except sprk_sourceversion and sprk_sizebytes, all six lookups, the marker (secured), Superseded;
    nine sprk_storedfile columns secured with their Readers / Writers / System Administrator rows (not sourcesha256, sha256,
    contenttype, blobpath). Any call that is not a GET is recorded; any GET the fake does not know throws, so a new read in the script
    fails the test loudly instead of being answered with a guess.

    Prints the script's output, then "RUNNER-EXIT: n", "RUNNER-NONGET: n" and one "RUNNER-WRITE: METHOD uri" line per non-GET.

.PARAMETER ExistingBigInt
    Logical names (sprk_storedfile columns) that exist as BigInt, unsecured, instead of being absent (a delete still resolving).

.PARAMETER Apply
    Run without -WhatIf (the fake still never leaves the process; a write is recorded and answered with an empty body).
#>
param(
    [Parameter(Mandatory)][string]$ScriptPath,
    [string[]]$ExistingBigInt = @(),
    [switch]$Apply
)

$global:FakeWrites = [System.Collections.Generic.List[string]]::new()
$global:FakeBigInt = @($ExistingBigInt | ForEach-Object { $_ -split ',' } | Where-Object { $_ })

function global:az { $global:LASTEXITCODE = 0; 'fake-token' }
function global:Start-Sleep { }

$readerTeamBus = 'Secure Record', 'Spaarke Business Unit 1', 'Spaarke Demo', 'Spaarke Dev 1', 'Spaarke Test 1', 'Spaarke'
$bffApps = @('5967251e-171c-46fe-a6c2-ef843c90309d', '1e40baad-e065-4aea-a8d4-4b7ab273458c')

function New-FakeAttr([string]$Type, $Secured = $false, [hashtable]$More = @{}) {
    $a = @{ Type = $Type; Secured = $Secured }
    foreach ($k in $More.Keys) { $a[$k] = $More[$k] }
    return $a
}

$sf = 'sprk_storedfile'
# table.column -> definition. sprk_sourceversion and sprk_sizebytes are absent (deleted).
$global:FakeAttrs = @{
    "$sf.sprk_name"                             = New-FakeAttr 'String' $false @{ MaxLength = 400; Required = 'ApplicationRequired' }
    "$sf.sprk_purpose"                          = New-FakeAttr 'Picklist' $true @{ Global = $true }
    "$sf.sprk_firmvisible"                      = New-FakeAttr 'Boolean' $true
    "$sf.sprk_filestatus"                       = New-FakeAttr 'Picklist' $true @{ Global = $false }
    "$sf.sprk_sourceattachmentidentity"         = New-FakeAttr 'String' $true @{ MaxLength = 1000 }
    "$sf.sprk_sourcesha256"                     = New-FakeAttr 'String' $false @{ MaxLength = 64 }
    "$sf.sprk_sha256"                           = New-FakeAttr 'String' $false @{ MaxLength = 64 }
    "$sf.sprk_contenttype"                      = New-FakeAttr 'String' $false @{ MaxLength = 255 }
    "$sf.sprk_blobpath"                         = New-FakeAttr 'String' $false @{ MaxLength = 1024 }
    "$sf.sprk_procedureversion"                 = New-FakeAttr 'Lookup' $true @{ Target = 'sprk_procedureversion'; Rel = 'sprk_storedfile_procedureversion_procedureversion'; Delete = 'RemoveLink' }
    "$sf.sprk_invoice"                          = New-FakeAttr 'Lookup' $true @{ Target = 'sprk_invoice'; Rel = 'sprk_storedfile_invoice_invoice'; Delete = 'RemoveLink' }
    "$sf.sprk_billingexport"                    = New-FakeAttr 'Lookup' $true @{ Target = 'sprk_billingexport'; Rel = 'sprk_storedfile_billingexport_billingexport'; Delete = 'RemoveLink' }
    "$sf.sprk_sourcedocument"                   = New-FakeAttr 'Lookup' $true @{ Target = 'sprk_document'; Rel = 'sprk_storedfile_sourcedocument_document'; Delete = 'Restrict' }
    "$sf.sprk_sourcecommunication"              = New-FakeAttr 'Lookup' $true @{ Target = 'sprk_communication'; Rel = 'sprk_storedfile_sourcecommunication_communication'; Delete = 'Restrict' }
    "$sf.sprk_createdbyperson"                  = New-FakeAttr 'Lookup' $false @{ Target = 'systemuser'; Rel = 'sprk_systemuser_sprk_storedfile_createdbyperson'; Delete = 'RemoveLink' }
    'sprk_document.sprk_restrictedbysubmission' = New-FakeAttr 'Boolean' $true
}
foreach ($c in $global:FakeBigInt) { $global:FakeAttrs["$sf.$c"] = New-FakeAttr 'BigInt' $false }

$global:FakeStatuses = @(
    @(1, 0, 'In Review'), @(2, 1, 'Cancelled'), @(659490000, 0, 'Approved'), @(659490001, 0, 'Sent to AP'), @(659490002, 1, 'Paid'),
    @(659490003, 1, 'Rejected'), @(659490004, 1, 'Returned to Firm'), @(659490005, 1, 'Superseded')
)
$global:FakeReaderBus = $readerTeamBus
$global:FakeBffApps = $bffApps

function global:New-FakeLabel([string]$t) { [pscustomobject]@{ UserLocalizedLabel = [pscustomobject]@{ Label = $t } } }
function global:New-FakeNotFound {
    $ex = [System.Exception]::new('Not Found')
    $ex | Add-Member -NotePropertyName Response -NotePropertyValue ([pscustomobject]@{ StatusCode = 404 })
    throw $ex
}

function global:Invoke-RestMethod {
    [CmdletBinding()]
    param([string]$Uri, [string]$Method = 'GET', $Headers, $Body, [string]$ContentType)
    if ($Method -ne 'GET') { $global:FakeWrites.Add("$Method $Uri"); return $null }
    $p = $Uri -replace '^https://[^/]+/api/data/v9\.2/', ''
    $attrs = $global:FakeAttrs

    if ($p -match "^EntityDefinitions\(LogicalName='(\w+)'\)/Attributes\(LogicalName='(\w+)'\)(?:/Microsoft\.Dynamics\.CRM\.(\w+)AttributeMetadata)?(\?.*)?$") {
        $t = $Matches[1]; $c = $Matches[2]
        if ($t -eq 'sprk_invoice' -and $c -eq 'statuscode') {
            return [pscustomobject]@{ OptionSet = [pscustomobject]@{ Options = @($global:FakeStatuses | ForEach-Object { [pscustomobject]@{ Value = $_[0]; State = $_[1]; Label = (New-FakeLabel $_[2]) } }) } }
        }
        $a = $attrs["$t.$c"]
        if (-not $a) { New-FakeNotFound }
        $o = [pscustomobject]@{
            LogicalName = $c; AttributeType = $a.Type; IsSecured = [bool]$a.Secured; MetadataId = "meta-$c"
            CanBeSecuredForRead = $true; CanBeSecuredForCreate = $true; CanBeSecuredForUpdate = $true
            MaxLength = $a.MaxLength; RequiredLevel = [pscustomobject]@{ Value = $(if ($a.Required) { $a.Required } else { 'None' }) }
            DefaultValue = $false; Targets = @($a.Target)
        }
        if ($a.Type -eq 'Picklist') {
            $opts = if ($a.Global) { @(@(100000000, 'Procedure source'), @(100000001, 'Invoice PDF'), @(100000002, 'Invoice supporting file'), @(100000003, 'Export file')) } else { @(@(100000000, 'Not yet servable'), @(100000001, 'Servable'), @(100000002, 'Removed')) }
            $o | Add-Member -NotePropertyName OptionSet -NotePropertyValue ([pscustomobject]@{ IsGlobal = [bool]$a.Global; Name = $(if ($a.Global) { 'sprk_storedfilepurpose' } else { 'x' }); Options = @($opts | ForEach-Object { [pscustomobject]@{ Value = $_[0]; Label = (New-FakeLabel $_[1]) } }) })
        }
        return $o
    }
    if ($p -match "^EntityDefinitions\(LogicalName='(\w+)'\)/ManyToOneRelationships\?.*ReferencingAttribute eq '(\w+)'") {
        $a = $attrs["$($Matches[1]).$($Matches[2])"]
        if (-not $a -or $a.Type -ne 'Lookup') { return [pscustomobject]@{ value = @() } }
        return [pscustomobject]@{ value = @([pscustomobject]@{
                    SchemaName = $a.Rel; ReferencedEntity = $a.Target
                    CascadeConfiguration = [pscustomobject]@{ Delete = $a.Delete; Assign = 'NoCascade'; Share = 'NoCascade'; Unshare = 'NoCascade'; Reparent = 'NoCascade' }
                    AssociatedMenuConfiguration = [pscustomobject]@{ Behavior = 'DoNotDisplay'; Group = 'Details'; Order = 10000 }
                }) }
    }
    if ($p -match "^EntityDefinitions\(LogicalName='(\w+)'\)(\?.*)?$") {
        $t = $Matches[1]
        if ($t -in 'sprk_storedfile', 'sprk_procedureversion', 'sprk_invoice', 'sprk_billingexport', 'sprk_document', 'sprk_communication', 'systemuser') {
            return [pscustomobject]@{ LogicalName = $t; OwnershipType = 'UserOwned'; PrimaryNameAttribute = 'sprk_name'; IsAuditEnabled = [pscustomobject]@{ Value = $true }; MetadataId = "meta-$t"; EntitySetName = "${t}s" }
        }
        New-FakeNotFound
    }
    if ($p -match "^RelationshipDefinitions\(SchemaName='") { New-FakeNotFound }
    if ($p -match "^GlobalOptionSetDefinitions\(Name='sprk_storedfilepurpose'\)") {
        return [pscustomobject]@{ MetadataId = 'os-1'; Options = @(@(@(100000000, 'Procedure source'), @(100000001, 'Invoice PDF'), @(100000002, 'Invoice supporting file'), @(100000003, 'Export file')) | ForEach-Object { [pscustomobject]@{ Value = $_[0]; Label = (New-FakeLabel $_[1]) } }) }
    }
    if ($p -like 'teams?*') {
        return [pscustomobject]@{ value = @($global:FakeReaderBus | ForEach-Object { [pscustomobject]@{ teamid = "team-$_"; name = $_; businessunitid = [pscustomobject]@{ name = $_; isdisabled = $false } } }) }
    }
    if ($p -match "^fieldsecurityprofiles\?.*name eq '([^']+)'") {
        $id = if ($Matches[1] -like '*Readers*') { 'p-reader' } else { 'p-writer' }
        return [pscustomobject]@{ value = @([pscustomobject]@{ fieldsecurityprofileid = $id; name = $Matches[1] }) }
    }
    if ($p -like 'fieldsecurityprofiles(p-reader)/teamprofiles_association*') { return [pscustomobject]@{ value = @($global:FakeReaderBus | ForEach-Object { [pscustomobject]@{ teamid = "team-$_"; name = $_ } }) } }
    if ($p -like 'fieldsecurityprofiles(p-writer)/teamprofiles_association*') { return [pscustomobject]@{ value = @() } }
    if ($p -like 'fieldsecurityprofiles(p-writer)/systemuserprofiles_association*') {
        return [pscustomobject]@{ value = @($global:FakeBffApps | ForEach-Object { [pscustomobject]@{ systemuserid = "u-$_"; fullname = "bff $_"; applicationid = $_ } }) }
    }
    if ($p -match 'systemusers\?.*applicationid eq (\S+)$') { return [pscustomobject]@{ value = @([pscustomobject]@{ systemuserid = "u-$($Matches[1])"; fullname = "bff $($Matches[1])" }) } }
    # The permission rows of a secured column: Readers (read only; none on sprk_blobpath), Writers, and the platform's own
    # System Administrator row, created when a column is secured.
    if ($p -match "^fieldpermissions\?.*_fieldsecurityprofileid_value eq (\S+) and entityname eq '(\w+)' and attributelogicalname eq '(\w+)'") {
        $a = $attrs["$($Matches[2]).$($Matches[3])"]
        if (-not $a -or -not $a.Secured) { return [pscustomobject]@{ value = @() } }
        if ($Matches[1] -eq 'p-writer') { return [pscustomobject]@{ value = @([pscustomobject]@{ fieldpermissionid = 'fp'; canread = 4; cancreate = 4; canupdate = 4 }) } }
        if ($Matches[1] -eq 'p-reader' -and $Matches[3] -ne 'sprk_blobpath') { return [pscustomobject]@{ value = @([pscustomobject]@{ fieldpermissionid = 'fp'; canread = 4; cancreate = 0; canupdate = 0 }) } }
        return [pscustomobject]@{ value = @() }
    }
    if ($p -match "^fieldpermissions\?.*expand=fieldsecurityprofileid.*entityname eq '(\w+)' and attributelogicalname eq '(\w+)'") {
        $a = $attrs["$($Matches[1]).$($Matches[2])"]
        if (-not $a -or -not $a.Secured) { return [pscustomobject]@{ value = @() } }
        $rows = @(
            [pscustomobject]@{ canread = 4; cancreate = 4; canupdate = 4; fieldsecurityprofileid = [pscustomobject]@{ name = 'Spaarke BFF-Managed Field Writers' } }
            [pscustomobject]@{ canread = 4; cancreate = 4; canupdate = 4; fieldsecurityprofileid = [pscustomobject]@{ name = 'System Administrator' } }
        )
        if ($Matches[2] -ne 'sprk_blobpath') { $rows += [pscustomobject]@{ canread = 4; cancreate = 0; canupdate = 0; fieldsecurityprofileid = [pscustomobject]@{ name = 'Spaarke BFF-Managed Field Readers' } } }
        return [pscustomobject]@{ value = $rows }
    }
    throw "FAKE: unexpected GET $p"
}

if ($Apply) { & $ScriptPath } else { & $ScriptPath -WhatIf }
"RUNNER-EXIT: $LASTEXITCODE"
"RUNNER-NONGET: $($global:FakeWrites.Count)"
$global:FakeWrites | ForEach-Object { "RUNNER-WRITE: $_" }
