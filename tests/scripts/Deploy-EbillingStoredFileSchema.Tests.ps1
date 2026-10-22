# tests/scripts/Deploy-EbillingStoredFileSchema.Tests.ps1
# ---------------------------------------------------------------------------
# ebilling-r1 task 088: scripts/ebilling/Deploy-EbillingStoredFileSchema.ps1. These tests prove, with no live call:
#   1. the plan (Get-StoredFilePlan): exactly the six lookups of F2 with their targets and delete behaviours (the three parents
#      RemoveLink, the two sources Restrict, the creator person RemoveLink; nothing Cascade), the relationship names, every other
#      F2 column and F4 with its type and length; the F3 secured set is every F2 column except sprk_name and sprk_createdbyperson,
#      plus the marker; only sprk_blobpath has no reader row (Get-Grants);
#   2. the F5 decision (Get-StatusDecision): Insert on exactly the seven task-012 values, Skip once Superseded is there, and an
#      ESCALATION (exit 2, nothing written) when 659490005 or the label exists with another meaning, a task-012 value is missing
#      or relabelled, or statuscode holds a value the task does not know;
#   3. the F1 values and the cascade comparison the verify uses;
#   4. the script text: no form, view, role, profile-member, invoice-row or document-row write, and no PublishAllXml;
#   4a. (task 088 pre-run fixes) the SchemaNames (table sprk_StoredFile, primary sprk_Name, every other column PascalCase after the
#      prefix; logical names unchanged), AssociatedMenuConfiguration DoNotDisplay on all six relationship bodies (the module's
#      Add-Lookup / Add-Table bodies are captured with Invoke-Dv mocked), the publish entity list (seven entities + the choice, no
#      PublishAllXml) and the order (the big integer columns and their securable check come before any lookup or other column);
#   5. the profile pre-flight (Test-ProfilePreconditions, Dataverse reads stubbed): the reader profile is checked against the NAMED
#      business units only (-ReaderBusinessUnits), never every default team (UAC 191's External Collaborators BU must never hold it).
# The script's constants are taken from the script itself (its top-level assignments), not retyped here.
#
# Pester 3.4:  Import-Module Pester -RequiredVersion 3.4.0 ; Invoke-Pester tests/scripts/Deploy-EbillingStoredFileSchema.Tests.ps1
# ---------------------------------------------------------------------------

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$script:ScriptPath = Join-Path $repoRoot 'scripts/ebilling/Deploy-EbillingStoredFileSchema.ps1'
$script:Text = Get-Content -LiteralPath $script:ScriptPath -Raw

# The script's EscalationException normally comes from the module (`using module`); a stand-in with the same name is enough here.
if (-not ('EscalationException' -as [type])) {
    Add-Type -TypeDefinition 'public class EscalationException : System.Exception { public EscalationException(string m) : base(m) {} }'
}

$errs = $null; $tokens = $null
$script:Ast = [System.Management.Automation.Language.Parser]::ParseFile($script:ScriptPath, [ref]$tokens, [ref]$errs)

# The script's own constants (top-level assignments), so a change there is what the tests see.
$constants = 'StoredFile', 'Document', 'Invoice', 'Version', 'Export', 'Communication', 'CreatorColumn', 'MarkerColumn', 'BlobPathColumn', 'PurposeChoice', 'FileStatusLabels', 'KnownStatus', 'SupersededStatus', 'StoredFileSchema', 'StoredFileNameSchema', 'RelatedMenuBehavior', 'BigIntColumns'
foreach ($st in $script:Ast.EndBlock.Statements) {
    if ($st -is [System.Management.Automation.Language.AssignmentStatementAst] -and $st.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and $st.Left.VariablePath.UserPath -in $constants) {
        Invoke-Expression $st.Extent.Text
    }
}

# The functions under test (the script cannot be dot-sourced: it runs on load).
foreach ($name in 'New-LookupSpec', 'Get-NewTableRel', 'Get-ExpectedCascade', 'Get-CascadeMismatches', 'Get-StoredFilePlan', 'Get-Grants', 'ConvertTo-LabelKey', 'Get-StatusDecision', 'Get-ChoiceOptions', 'Get-PublishEntities', 'Get-PublishXml', 'Get-AssociatedMenuProblem', 'Test-ProfilePreconditions') {
    $fn = $script:Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name }, $true) | Select-Object -First 1
    if (-not $fn) { throw "function $name not found in the script" }
    Invoke-Expression $fn.Extent.Text
}

# Variables Get-Grants reads from the script scope.
$WriterProfileName = 'Writers'; $ReaderProfileName = 'Readers'; $script:WriterId = 'w-id'; $script:ReaderId = 'r-id'

# The script's default reader business units (its param default), so a change there is what the tests see.
$rbu = $script:Ast.ParamBlock.Parameters | Where-Object { $_.Name.VariablePath.UserPath -eq 'ReaderBusinessUnits' }
$DefaultReaderBusinessUnits = @(& ([scriptblock]::Create($rbu.DefaultValue.Extent.Text)))

function New-Status([int]$Value, [int]$State, [string]$Label) { [pscustomobject]@{ Value = $Value; State = $State; Label = $Label } }
function Get-SevenStatuses { @($KnownStatus | ForEach-Object { New-Status $_.Value $_.State $_.Label }) }

Describe 'the constants taken from the script' {
    It 'were all found (the tests read the script, not a copy)' {
        $StoredFile | Should Be 'sprk_storedfile'
        $MarkerColumn | Should Be 'sprk_restrictedbysubmission'
        @($KnownStatus).Count | Should Be 7
        $SupersededStatus.Value | Should Be 659490005
    }
}

Describe 'Get-StoredFilePlan (F2, F4 and the F3 secured set)' {
    $plan = Get-StoredFilePlan
    $lookups = @($plan.Lookups)

    It 'has exactly the six F2 lookups, each on sprk_storedfile, with its target' {
        $lookups.Count | Should Be 6
        (($lookups | ForEach-Object { "$($_.Table).$($_.Column)->$($_.Target)" } | Sort-Object) -join ',') |
            Should Be 'sprk_storedfile.sprk_billingexport->sprk_billingexport,sprk_storedfile.sprk_createdbyperson->systemuser,sprk_storedfile.sprk_invoice->sprk_invoice,sprk_storedfile.sprk_procedureversion->sprk_procedureversion,sprk_storedfile.sprk_sourcecommunication->sprk_communication,sprk_storedfile.sprk_sourcedocument->sprk_document'
    }
    It 'makes the three parent lookups RemoveLink (orphan reconcile, model 4.2)' {
        foreach ($c in 'sprk_procedureversion', 'sprk_invoice', 'sprk_billingexport') {
            ($lookups | Where-Object { $_.Column -eq $c }).Delete | Should Be 'RemoveLink'
        }
    }
    It 'makes exactly the two source lookups Restrict (uac-r2 answer B)' {
        (($lookups | Where-Object { $_.Delete -eq 'Restrict' } | ForEach-Object { $_.Column } | Sort-Object) -join ',') | Should Be 'sprk_sourcecommunication,sprk_sourcedocument'
    }
    It 'never makes a lookup Cascade (no relationship of sprk_storedfile is parental) (must-fire if one comes back)' {
        @($lookups | Where-Object { $_.Delete -notin 'RemoveLink', 'Restrict' }).Count | Should Be 0
    }
    It 'names the relationships {table}_{column}_{target} and the creator by the submission-schema shape; unique, at most 100 characters' {
        (($lookups | ForEach-Object { $_.Rel } | Sort-Object) -join ',') |
            Should Be 'sprk_storedfile_billingexport_billingexport,sprk_storedfile_invoice_invoice,sprk_storedfile_procedureversion_procedureversion,sprk_storedfile_sourcecommunication_communication,sprk_storedfile_sourcedocument_document,sprk_systemuser_sprk_storedfile_createdbyperson'
        @($lookups | ForEach-Object { $_.Rel } | Select-Object -Unique).Count | Should Be 6
        @($lookups | Where-Object { $_.Rel.Length -gt 100 }).Count | Should Be 0
    }
    It 'lists every other F2 column and F4 with its type and length' {
        $cols = (@($plan.Columns) | ForEach-Object { "$($_.Table).$($_.Column):$($_.Type)$(if ($_.MaxLength) { "/$($_.MaxLength)" })" } | Sort-Object) -join ','
        $cols | Should Be 'sprk_document.sprk_restrictedbysubmission:Boolean,sprk_storedfile.sprk_blobpath:String/1024,sprk_storedfile.sprk_contenttype:String/255,sprk_storedfile.sprk_filestatus:Picklist,sprk_storedfile.sprk_firmvisible:Boolean,sprk_storedfile.sprk_name:String/400,sprk_storedfile.sprk_purpose:Picklist,sprk_storedfile.sprk_sha256:String/64,sprk_storedfile.sprk_sizebytes:BigInt,sprk_storedfile.sprk_sourceattachmentidentity:String/1000,sprk_storedfile.sprk_sourcesha256:String/64,sprk_storedfile.sprk_sourceversion:BigInt'
    }
    It 'secures every F2 column except sprk_name and sprk_createdbyperson, plus the marker (16 columns)' {
        $sec = @($plan.Secured)
        $sec.Count | Should Be 16
        (($sec | ForEach-Object { "$($_.Table).$($_.Column)" } | Sort-Object) -join ',') |
            Should Be 'sprk_document.sprk_restrictedbysubmission,sprk_storedfile.sprk_billingexport,sprk_storedfile.sprk_blobpath,sprk_storedfile.sprk_contenttype,sprk_storedfile.sprk_filestatus,sprk_storedfile.sprk_firmvisible,sprk_storedfile.sprk_invoice,sprk_storedfile.sprk_procedureversion,sprk_storedfile.sprk_purpose,sprk_storedfile.sprk_sha256,sprk_storedfile.sprk_sizebytes,sprk_storedfile.sprk_sourceattachmentidentity,sprk_storedfile.sprk_sourcecommunication,sprk_storedfile.sprk_sourcedocument,sprk_storedfile.sprk_sourcesha256,sprk_storedfile.sprk_sourceversion'
    }
    It 'does not secure sprk_name or sprk_createdbyperson (task 086 secures the creator) (must-fire if one comes back)' {
        @($plan.Secured | Where-Object { $_.Column -in 'sprk_name', 'sprk_createdbyperson' }).Count | Should Be 0
    }
    It 'gives every secured column the metadata kind its column has' {
        foreach ($s in $plan.Secured) {
            $want = if ($lookups | Where-Object { $_.Column -eq $s.Column -and $_.Table -eq $s.Table }) { 'Lookup' } else { ($plan.Columns | Where-Object { $_.Column -eq $s.Column -and $_.Table -eq $s.Table }).Type }
            $s.Kind | Should Be $want
        }
    }
    It 'leaves the reader row off sprk_blobpath only (server-only)' {
        (($plan.Secured | Where-Object { -not $_.Reader } | ForEach-Object { $_.Column }) -join ',') | Should Be 'sprk_blobpath'
    }
}

Describe 'Get-Grants (F3 permission rows)' {
    It 'grants the writer (read/create/update) then the reader (read only) on an ordinary secured column' {
        $g = @(Get-Grants @{ Table = 'sprk_storedfile'; Column = 'sprk_purpose'; Kind = 'Picklist'; Reader = $true })
        $g.Count | Should Be 2
        $g[0].Name | Should Be 'Writers'; $g[0].Write | Should Be 4; $g[0].Id | Should Be 'w-id'
        $g[1].Name | Should Be 'Readers'; $g[1].Write | Should Be 0; $g[1].Id | Should Be 'r-id'
    }
    It 'grants the writer ONLY on a server-only column (must-fire: no reader row on sprk_blobpath)' {
        $g = @(Get-Grants @{ Table = 'sprk_storedfile'; Column = 'sprk_blobpath'; Kind = 'String'; Reader = $false })
        $g.Count | Should Be 1
        $g[0].Name | Should Be 'Writers'
    }
}

# Test-ProfilePreconditions with the Dataverse reads stubbed (no call, no write). Teams: name == its BU's name, as on dev.
function Write-Report([string]$StateText, [string]$What) { $script:Reports += ,@($StateText, $What) }
function Get-ProfileByName([string]$Name) { if ($Name -eq 'Readers') { @([pscustomobject]@{ fieldsecurityprofileid = 'r-id' }) } else { @([pscustomobject]@{ fieldsecurityprofileid = 'w-id' }) } }
function Invoke-Dv {
    param([string]$Path)
    if ($Path -like 'teams?*') { return [pscustomobject]@{ value = @($script:DefaultTeams | ForEach-Object { [pscustomobject]@{ teamid = $_.Id; name = $_.Bu; businessunitid = [pscustomobject]@{ name = $_.Bu; isdisabled = $false } } }) } }
    if ($Path -like 'fieldsecurityprofiles(r-id)/teamprofiles_association*') { return [pscustomobject]@{ value = @($script:ReaderTeamIds | ForEach-Object { [pscustomobject]@{ teamid = $_; name = 'x' } }) } }
    if ($Path -like 'fieldsecurityprofiles(w-id)/systemuserprofiles_association*') { return [pscustomobject]@{ value = @([pscustomobject]@{ systemuserid = 'u-bff'; fullname = 'bff'; applicationid = 'app-1' }) } }
    if ($Path -like 'fieldsecurityprofiles(w-id)/teamprofiles_association*') { return [pscustomobject]@{ value = @() } }
    if ($Path -like 'systemusers?*') { return [pscustomobject]@{ value = @([pscustomobject]@{ systemuserid = 'u-bff'; fullname = 'bff' }) } }
    throw "unexpected read: $Path"
}
function Set-PreflightWorld([string[]]$Bus, [string[]]$BusWithReader) {
    $script:DefaultTeams = @($Bus | ForEach-Object { [pscustomobject]@{ Id = "team-$_"; Bu = $_ } })
    $script:ReaderTeamIds = @($BusWithReader | ForEach-Object { "team-$_" })
    $script:Reports = @()
}

Describe 'Test-ProfilePreconditions (the reader profile is checked against the named business units only)' {
    $BffApplicationIds = @('app-1')

    It 'defaults to the six dev business units' {
        (($DefaultReaderBusinessUnits | Sort-Object) -join ',') | Should Be 'Secure Record,Spaarke,Spaarke Business Unit 1,Spaarke Demo,Spaarke Dev 1,Spaarke Test 1'
    }
    It 'passes when every named business unit default team holds the profile (must-not-fire)' {
        $ReaderBusinessUnits = $DefaultReaderBusinessUnits
        Set-PreflightWorld $DefaultReaderBusinessUnits $DefaultReaderBusinessUnits
        { Test-ProfilePreconditions } | Should Not Throw
        @($script:Reports | Where-Object { $_[0] -eq 'INFO' }).Count | Should Be 0
    }
    It 'escalates (exit 2) when a named business unit default team lacks the profile (must-fire)' {
        $ReaderBusinessUnits = $DefaultReaderBusinessUnits
        Set-PreflightWorld $DefaultReaderBusinessUnits @($DefaultReaderBusinessUnits | Where-Object { $_ -ne 'Spaarke Demo' })
        { Test-ProfilePreconditions } | Should Throw "default team 'Spaarke Demo' (BU 'Spaarke Demo') is NOT on 'Readers'"
        try { Test-ProfilePreconditions } catch { $_.Exception.GetType().Name | Should Be 'EscalationException' }
    }
    It 'does NOT fail on an extra business unit whose default team lacks the profile, and names it in one INFO line (must-not-fire)' {
        $ReaderBusinessUnits = $DefaultReaderBusinessUnits
        Set-PreflightWorld (@($DefaultReaderBusinessUnits) + 'External Collaborators') $DefaultReaderBusinessUnits
        { Test-ProfilePreconditions } | Should Not Throw
        $info = @($script:Reports | Where-Object { $_[0] -eq 'INFO' })
        $info.Count | Should Be 1
        $info[0][1] | Should Match "'External Collaborators'"
        @($script:Reports | Where-Object { $_[1] -match 'External Collaborators' -and $_[0] -eq 'OK' }).Count | Should Be 0
    }
    It 'escalates when a named business unit has no default team (a misspelt name must not pass silently) (must-fire)' {
        $ReaderBusinessUnits = @($DefaultReaderBusinessUnits) + 'Spaarke Dvo 1'
        Set-PreflightWorld $DefaultReaderBusinessUnits $DefaultReaderBusinessUnits
        { Test-ProfilePreconditions } | Should Throw "names 'Spaarke Dvo 1'"
    }
}

Describe 'Get-StatusDecision (F5)' {
    It 'inserts on exactly the seven task-012 values (must-not-fire)' {
        Get-StatusDecision (Get-SevenStatuses) | Should Be 'Insert'
    }
    It 'skips when Superseded is already 659490005 at statecode 1 (second run)' {
        Get-StatusDecision (@(Get-SevenStatuses) + @(New-Status 659490005 1 'Superseded')) | Should Be 'Skip'
    }
    It 'escalates when 659490005 is used by another label (must-fire)' {
        { Get-StatusDecision (@(Get-SevenStatuses) + @(New-Status 659490005 1 'On Hold')) } | Should Throw 'already used by'
    }
    It 'escalates when Superseded sits at 659490005 under statecode 0 (must-fire)' {
        { Get-StatusDecision (@(Get-SevenStatuses) + @(New-Status 659490005 0 'Superseded')) } | Should Throw 'already used by'
    }
    It 'escalates when the label exists at another value, case and spaces ignored (must-fire)' {
        { Get-StatusDecision (@(Get-SevenStatuses) + @(New-Status 659490009 1 'superseded')) } | Should Throw 'already exists as value 659490009'
    }
    It 'escalates when a task-012 value is relabelled or moved to the other state (must-fire)' {
        $s = @(Get-SevenStatuses); $s[4] = New-Status 659490002 1 'Settled'
        { Get-StatusDecision $s } | Should Throw "value 659490002 is 'Settled'"
        $s = @(Get-SevenStatuses); $s[2] = New-Status 659490000 1 'Approved'
        { Get-StatusDecision $s } | Should Throw 'value 659490000'
    }
    It 'escalates when a task-012 value is missing (must-fire)' {
        { Get-StatusDecision @(Get-SevenStatuses | Where-Object { $_.Value -ne 659490004 }) } | Should Throw 'is missing'
    }
    It 'escalates on a value the task does not know (must-fire)' {
        { Get-StatusDecision (@(Get-SevenStatuses) + @(New-Status 659490007 0 'Disputed')) } | Should Throw 'does not know'
    }
}

Describe 'F1 values and the cascade comparison' {
    It 'numbers the purposes 100000000-100000003 in the task order' {
        ((Get-ChoiceOptions $PurposeChoice.Labels | ForEach-Object { "$($_.Value)=$($_.Label)" }) -join ',') |
            Should Be '100000000=Procedure source,100000001=Invoice PDF,100000002=Invoice supporting file,100000003=Export file'
    }
    It 'numbers the file statuses Not yet servable, Servable, Removed' {
        ($FileStatusLabels -join ',') | Should Be 'Not yet servable,Servable,Removed'
    }
    It 'is silent on the planned configuration and fires on another delete behaviour or a cascading member' {
        $spec = New-LookupSpec 'sprk_storedfile' 'sprk_sourcedocument' 'x' 'r' 'sprk_document' 'd' 'd' 'Restrict'
        $exact = [pscustomobject]@{ Delete = 'Restrict'; Assign = 'NoCascade'; Share = 'NoCascade'; Unshare = 'NoCascade'; Reparent = 'NoCascade'; Merge = 'NoCascade' }
        @(Get-CascadeMismatches $exact (Get-ExpectedCascade $spec)).Count | Should Be 0
        $removeLink = [pscustomobject]@{ Delete = 'RemoveLink'; Assign = 'NoCascade'; Share = 'NoCascade'; Unshare = 'NoCascade'; Reparent = 'NoCascade' }
        @(Get-CascadeMismatches $removeLink (Get-ExpectedCascade $spec)).Count | Should Be 1
        $shares = [pscustomobject]@{ Delete = 'Restrict'; Assign = 'NoCascade'; Share = 'Cascade'; Unshare = 'NoCascade'; Reparent = 'NoCascade' }
        @(Get-CascadeMismatches $shares (Get-ExpectedCascade $spec)).Count | Should Be 1
    }
    It 'publishes the three tables it changes and the four the relationships touch (K-c)' {
        ((Get-PublishEntities) -join ',') | Should Be 'sprk_storedfile,sprk_document,sprk_invoice,sprk_communication,sprk_procedureversion,sprk_billingexport,systemuser'
    }
}

Describe 'the SchemaNames (K-a): PascalCase after the prefix, logical names unchanged' {
    It 'names the table sprk_StoredFile and its primary column sprk_Name; both lowercase to the logical names' {
        $StoredFileSchema | Should BeExactly 'sprk_StoredFile'
        $StoredFileNameSchema | Should BeExactly 'sprk_Name'
        $StoredFileSchema.ToLowerInvariant() | Should BeExactly $StoredFile
        $StoredFileNameSchema.ToLowerInvariant() | Should BeExactly 'sprk_name'
        $StoredFile | Should BeExactly 'sprk_storedfile'
    }
    It 'passes both to Add-Table, so the create body carries them' {
        $script:Text.Contains('-SchemaName $StoredFileSchema -PrimarySchema $StoredFileNameSchema') | Should Be $true
    }
    It 'gives every lookup column a PascalCase SchemaName that lowercases to its logical name' {
        foreach ($l in @((Get-StoredFilePlan).Lookups)) {
            $l.Schema.ToLowerInvariant() | Should BeExactly $l.Column
            $l.Schema | Should Not BeExactly $l.Column
        }
    }
    It 'gives every other column a PascalCase SchemaName (11: ten sprk_storedfile columns and the marker), each lowercasing to a planned column' {
        $schemas = @([regex]::Matches($script:Text, "(?:New-\w+Def|-Schema|Add-GlobalChoiceColumn\s+'sprk_purpose')\s+'(sprk_\w+)'") | ForEach-Object { $_.Groups[1].Value })
        $schemas.Count | Should Be 11
        $logical = @((Get-StoredFilePlan).Columns | Where-Object { $_.Column -ne 'sprk_name' } | ForEach-Object { $_.Column } | Sort-Object)
        (($schemas | ForEach-Object { $_.ToLowerInvariant() } | Sort-Object) -join ',') | Should Be ($logical -join ',')
        foreach ($x in $schemas) { $x | Should Not BeExactly $x.ToLowerInvariant() }
    }
    It 'keeps the pre-flight and the verify on LOGICAL names (every EntityDefinitions read uses LogicalName=, except the audit PUT by metadata id; no SchemaName is compared)' {
        @([regex]::Matches($script:Text, 'EntityDefinitions\((?!LogicalName=|\$\(\$ent\.MetadataId\))')).Count | Should Be 0
        $script:Text -match "SchemaName='sprk_[A-Z]" | Should Be $false
    }
}

Describe 'the Related menu (K-b): DoNotDisplay on all six relationships' {
    It 'is DoNotDisplay on every planned lookup spec' {
        $RelatedMenuBehavior | Should Be 'DoNotDisplay'
        $lookups = @((Get-StoredFilePlan).Lookups)
        $lookups.Count | Should Be 6
        @($lookups | Where-Object { $_.Menu -eq 'DoNotDisplay' }).Count | Should Be 6
    }
    It 'passes the spec menu to the one Add-Lookup call, which every lookup goes through' {
        @([regex]::Matches($script:Text, 'Add-Lookup\s[^\r\n]*-MenuBehavior \$s\.Menu')).Count | Should Be 1
        @([regex]::Matches($script:Text, '(?m)^\s*Add-Lookup\s')).Count | Should Be 1
    }
    It 'reads each relationship back and fails the verify when it is not DoNotDisplay (must-fire) and not otherwise (must-not-fire)' {
        Get-AssociatedMenuProblem ([pscustomobject]@{ Behavior = 'DoNotDisplay'; Group = 'Details' }) 'DoNotDisplay' | Should BeNullOrEmpty
        (Get-AssociatedMenuProblem ([pscustomobject]@{ Behavior = 'UseCollectionName' }) 'DoNotDisplay') | Should Match "'UseCollectionName', expected 'DoNotDisplay'"
        (Get-AssociatedMenuProblem $null 'DoNotDisplay') | Should Match 'expected'
        $script:Text.Contains('Get-AssociatedMenuProblem $rel.AssociatedMenuConfiguration $s.Menu') | Should Be $true
        $script:Text.Contains('CascadeConfiguration,AssociatedMenuConfiguration&') | Should Be $true
    }
}

# The module's create bodies, captured: Invoke-Dv is mocked inside the module, so nothing leaves the machine.
Describe 'the create bodies the module sends (Invoke-Dv mocked inside the module)' {
    Import-Module (Join-Path $repoRoot 'scripts/ebilling/EbillingSchema.psm1') -Force
    $null = Initialize-EbillingSchema -EnvironmentUrl 'https://example.invalid' -SolutionUniqueName 'S' -Entity 'sprk_storedfile'
    Mock -ModuleName EbillingSchema Start-Sleep { }
    Mock -ModuleName EbillingSchema Write-Host { }
    Mock -ModuleName EbillingSchema Invoke-Dv {
        if ($Method -eq 'POST') { $global:Sf088Posted += ,@{ Path = $Path; Body = $Body }; $global:Sf088Created = $true; return $null }
        # A read is "absent" until something was posted; the exception: a table that is not the one being created (a lookup target).
        if ($Path -match "^EntityDefinitions\(LogicalName='([a-z_]+)'\)\?" -and $Matches[1] -notin 'sprk_storedfile', 'sprk_other') { return [pscustomobject]@{ LogicalName = 'x' } }
        if ($global:Sf088Created) { return [pscustomobject]@{ LogicalName = 'x'; AttributeType = 'Lookup' } }
        $e = [System.Exception]::new('not found'); $e.Data['Status'] = 404; throw $e
    }
    BeforeEach { $global:Sf088Posted = @(); $global:Sf088Created = $false }

    It 'sends AssociatedMenuConfiguration DoNotDisplay (Behavior, Group, Order) in all six lookup bodies built from the plan (K-b)' {
        $bodies = @()
        foreach ($l in @((Get-StoredFilePlan).Lookups)) {
            $global:Sf088Posted = @(); $global:Sf088Created = $false
            Set-EbEntity $l.Table
            Add-Lookup -Column $l.Column -SchemaName $l.Schema -RelSchema $l.Rel -Target $l.Target -Display $l.Display -Description $l.Desc -DeleteBehavior $l.Delete -MenuBehavior $l.Menu
            $rels = @($global:Sf088Posted | Where-Object { $_.Path -eq 'RelationshipDefinitions' })
            $rels.Count | Should Be 1
            $bodies += ,$rels[0].Body
        }
        $bodies.Count | Should Be 6
        foreach ($b in $bodies) {
            $b.AssociatedMenuConfiguration.Behavior | Should Be 'DoNotDisplay'
            $b.AssociatedMenuConfiguration.Group | Should Be 'Details'
            $b.AssociatedMenuConfiguration.Order | Should Be 10000
        }
    }
    It 'sends no AssociatedMenuConfiguration when -MenuBehavior is not given (other scripts unchanged) (must-not-fire)' {
        Set-EbEntity 'sprk_storedfile'
        Add-Lookup -Column 'sprk_invoice' -SchemaName 'sprk_Invoice' -RelSchema 'r' -Target 'sprk_invoice' -Display 'd' -Description 'd'
        @($global:Sf088Posted)[0].Body.ContainsKey('AssociatedMenuConfiguration') | Should Be $false
    }
    It 'sends SchemaName sprk_StoredFile and primary SchemaName sprk_Name, with the lowercase logical names kept (K-a)' {
        Add-Table -Table 'sprk_storedfile' -Display 'Stored File' -Plural 'Stored Files' -Description 'd' -PrimaryColumn 'sprk_name' -PrimaryDisplay 'File Name' -PrimaryDescription 'p' -PrimaryLength 400 -Ownership UserOwned -SchemaName $StoredFileSchema -PrimarySchema $StoredFileNameSchema
        $b = @($global:Sf088Posted | Where-Object { $_.Path -eq 'EntityDefinitions' })[0].Body
        $b.SchemaName | Should BeExactly 'sprk_StoredFile'
        $b.PrimaryNameAttribute | Should BeExactly 'sprk_name'
        $b.Attributes[0].SchemaName | Should BeExactly 'sprk_Name'
    }
    It 'keeps SchemaName = the logical name when none is given (other scripts unchanged) (must-not-fire)' {
        Add-Table -Table 'sprk_other' -Display 'O' -Plural 'Os' -Description 'd' -PrimaryColumn 'sprk_name' -PrimaryDisplay 'N' -PrimaryDescription 'p' -PrimaryLength 100
        $b = @($global:Sf088Posted | Where-Object { $_.Path -eq 'EntityDefinitions' })[0].Body
        $b.SchemaName | Should BeExactly 'sprk_other'
        $b.Attributes[0].SchemaName | Should BeExactly 'sprk_name'
    }
    It 'refuses a SchemaName that does not lowercase to the logical name (must-fire)' {
        { Add-Table -Table 'sprk_storedfile' -Display 'x' -Plural 'x' -Description 'd' -PrimaryColumn 'sprk_name' -PrimaryDisplay 'N' -PrimaryDescription 'p' -PrimaryLength 100 -SchemaName 'sprk_StoredFiles' } | Should Throw 'does not lowercase'
    }
}

Describe 'the publish list (K-c)' {
    It 'names the seven entities and the purpose choice in the PublishXml, and nothing else' {
        $xml = Get-PublishXml
        $ents = @([regex]::Matches($xml, '<entity>([^<]+)</entity>') | ForEach-Object { $_.Groups[1].Value })
        (($ents | Sort-Object) -join ',') | Should Be (('sprk_storedfile', 'sprk_document', 'sprk_invoice', 'sprk_communication', 'sprk_procedureversion', 'sprk_billingexport', 'systemuser' | Sort-Object) -join ',')
        $ents.Count | Should Be 7
        $xml | Should Match '<optionsets><optionset>sprk_storedfilepurpose</optionset></optionsets>'
    }
    It 'is what Publish-Changes sends, and never PublishAllXml (must-fire if either comes back)' {
        $script:Text.Contains("-Path 'PublishXml' -Method POST -Body @{ ParameterXml = (Get-PublishXml) }") | Should Be $true
        $script:Text -match "(?i)-Path\s+['`"]PublishAllXml" | Should Be $false
        $script:Text -match '(?i)PublishAllXml\s*-Method|Invoke-Dv\s+[''"]PublishAllXml' | Should Be $false
    }
}

Describe 'the order (item 4): the big integer columns and their securable check come first' {
    It 'creates the two big integer columns once each, right after the audit and before any lookup or other column' {
        foreach ($c in $BigIntColumns) { @([regex]::Matches($script:Text, "Add-Column '$c'")).Count | Should Be 1 }
        ($BigIntColumns -join ',') | Should Be 'sprk_sourceversion,sprk_sizebytes'
        $audit = $script:Text.IndexOf('Set-TableAudit $StoredFile')
        $b1 = $script:Text.IndexOf("Add-Column 'sprk_sourceversion'")
        $b2 = $script:Text.IndexOf("Add-Column 'sprk_sizebytes'")
        $bigCheck = $script:Text.IndexOf('Assert-Securable @($script:Secured | Where-Object { $_.Table -eq $StoredFile -and $_.Column -in $BigIntColumns })')
        $lookup = $script:Text.IndexOf('Add-Lookup -Column')
        $purpose = $script:Text.IndexOf("Add-GlobalChoiceColumn 'sprk_purpose'")
        $string1 = $script:Text.IndexOf("Add-Column 'sprk_sourcesha256'")
        $fullCheck = $script:Text.IndexOf('Assert-Securable @($script:Secured | Where-Object { $_.Table -eq $StoredFile })')
        $marker = $script:Text.IndexOf('Add-Column $MarkerColumn')
        $audit | Should BeGreaterThan 0
        ($audit -lt $b1 -and $b1 -lt $b2 -and $b2 -lt $bigCheck) | Should Be $true
        ($bigCheck -lt $lookup -and $bigCheck -lt $purpose -and $bigCheck -lt $string1) | Should Be $true
        # the full pass and the marker rules are unchanged: full check, then the marker, and nothing is secured before either
        ($string1 -lt $fullCheck -and $fullCheck -lt $marker) | Should Be $true
        $firstSecure = $script:Text.IndexOf('Invoke-ColumnSecurity $spec')
        ($bigCheck -lt $firstSecure -and $marker -lt $firstSecure) | Should Be $true
    }
    It 'lists exactly the BigInt-typed columns of the plan as the early ones (must-fire if a BigInt column is added without being listed)' {
        $big = @((Get-StoredFilePlan).Columns | Where-Object { $_.Type -eq 'BigInt' } | ForEach-Object { $_.Column } | Sort-Object)
        ($big -join ',') | Should Be (($BigIntColumns | Sort-Object) -join ',')
    }
}

Describe 'the script text (what it never does)' {
    It 'creates only the sprk_storedfile table' {
        @([regex]::Matches($script:Text, 'Add-Table\s+-Table\s+\$(\w+)') | ForEach-Object { $_.Groups[1].Value }) -join ',' | Should Be 'StoredFile'
    }
    It 'creates no form, view, chart or sitemap entry (forms and views are only read, for the verify inventory)' {
        $script:Text -match '(?i)savedqueryvisualizations|sitemaps|FormXml' | Should Be $false
        $uses = @([regex]::Matches($script:Text, '(?i)systemforms|savedqueries'))
        $reads = @([regex]::Matches($script:Text, '(?i)Invoke-Dv\s+"(systemforms|savedqueries)\?`\$select='))
        $uses.Count | Should Be 2
        $reads.Count | Should Be 2
    }
    It 'checks the Stored File columns are securable BEFORE the marker is created, and secures the marker right after' {
        $prePass = $script:Text.IndexOf('Assert-Securable @($script:Secured | Where-Object { $_.Table -eq $StoredFile })')
        $marker = $script:Text.IndexOf('Add-Column $MarkerColumn')
        $markerCheck = $script:Text.IndexOf('Assert-Securable $markerSpec')
        $markerSecure = $script:Text.IndexOf('foreach ($spec in $markerSpec) { Invoke-ColumnSecurity $spec }')
        $prePass | Should BeGreaterThan 0
        ($prePass -lt $marker -and $marker -lt $markerCheck -and $markerCheck -lt $markerSecure) | Should Be $true
        # step 5 then secures only the Stored File columns (the plan test above pins Secured = those 15 + the marker)
        $step5 = $script:Text.IndexOf('foreach ($spec in @($script:Secured | Where-Object { $_.Table -eq $StoredFile })) { Invoke-ColumnSecurity $spec }')
        $step5 | Should BeGreaterThan $markerSecure
        $script:Text.Contains('foreach ($spec in $script:Secured) { Invoke-ColumnSecurity $spec }') | Should Be $false
    }
    It 'publishes what a stopped run changed, on both the escalation and the error path' {
        @([regex]::Matches($script:Text, '(?s)catch \[EscalationException\] \{[^}]*Publish-AfterStop[^}]*exit 2')).Count | Should Be 1
        @([regex]::Matches($script:Text, '(?s)catch \{\s*Write-Host ""[^}]*Publish-AfterStop[^}]*exit 1')).Count | Should Be 1
    }
    It 'adds no role privilege' {
        $script:Text -match '(?i)AddPrivilegesRole|ReplacePrivilegesRole|roleprivileges|RemovePrivilegeRole' | Should Be $false
    }
    It 'never adds or removes a profile member (reads the associations only, never posts a $ref)' {
        $script:Text -match '(?i)_association/`?\$ref' | Should Be $false
    }
    It 'never writes an invoice or a document ROW (no record path: no invoice is set to Superseded, no marker is written)' {
        $script:Text -match "(?i)-Path\s+[`"']sprk_(invoices|documents)" | Should Be $false
        $script:Text -match '(?i)-Method\s+(PATCH|DELETE)' | Should Be $false
    }
    It 'publishes entity-scoped only (no PublishAllXml)' {
        $script:Text -match "(?i)-Path\s+['`"]PublishAllXml" | Should Be $false
        $script:Text -match "(?i)-Path\s+'PublishXml'" | Should Be $true
    }
    It 'never checks the reader profile against every default team (the check is over -ReaderBusinessUnits; no loop over $defaultTeams adds a problem)' {
        $script:Text -match '(?s)foreach \(\$t in \$defaultTeams\)' | Should Be $false
        $script:Text.Contains('-in $ReaderBusinessUnits') | Should Be $true
    }
    It 'never assigns the reader profile to a team (no $ref post, no associate call)' {
        $script:Text -match '(?i)teamprofiles_association/`?\$ref|/\$ref' | Should Be $false
    }
    It 'refuses any environment but dev before the first read' {
        $script:Text -match "if \(\`$EnvironmentUrl -ne \`$DevUrl\)" | Should Be $true
    }
    It 'refuses (escalation) when sprk_procedureversion does not exist (task 080 first)' {
        $script:Text -match "throw \[EscalationException\]::new\(`"\`$Version does not exist" | Should Be $true
    }
}
