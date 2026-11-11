# Pester 5. Run: Import-Module Pester -RequiredVersion 5.7.1; Invoke-Pester tests/scripts/Set-EbillingInvoiceMoneyFieldSecurity.Tests.ps1
# ebilling-r1 task 166 fix round (EB-D-200, adversarial review K1 + K2). The text-only InvoiceMoneyFieldSecurityAgreementTests pin the script's
# constants and its verify checks; they cannot see how the script behaves when a step fails or when the operator declines a step. These tests
# run the script's own functions (lifted out of its syntax tree, as Deploy-EbillingInvoiceReviewDetailColumn.Tests.ps1 does) against a test
# double of the Dataverse calls: no environment, no network.
#   K1  the column definition is read and written through its typed path (Money / Lookup cast), and a write that moves the precision, the
#       range or the lookup targets fails loudly.
#   K2  a failure on column N of an apply leaves the earlier columns secured and granted, publishes what was written, and names what
#       completed; -Revert declining to un-secure a column keeps that column's permission rows (no column left secured with no grants).

BeforeAll {
    $script:ScriptPath = Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path 'scripts' 'ebilling' 'Set-EbillingInvoiceMoneyFieldSecurity.ps1'

    $tokens = $null; $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($script:ScriptPath, [ref]$tokens, [ref]$errors)
    if ($errors.Count -gt 0) { throw "The script does not parse: $($errors[0].Message)" }

    $wantedVariables = 'Table', 'Columns', 'LookupColumns', 'WriterProfileName', 'ReaderProfileName'
    $wantedFunctions = 'Report', 'Get-AttrPath', 'Get-TypedAttrPath', 'Format-Number', 'Get-ColumnShape', 'Get-Permission', 'Grant-Permission',
        'Set-ColumnSecured', 'Grant-MissingPermissions', 'Write-MaskedRecovery', 'Invoke-RevertColumns', 'Invoke-SecureColumns'

    $pieces = [System.Collections.Generic.List[string]]::new()
    $found = [System.Collections.Generic.List[string]]::new()
    foreach ($statement in $ast.EndBlock.Statements) {
        if ($statement -is [System.Management.Automation.Language.AssignmentStatementAst] -and
            $statement.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and
            $statement.Left.VariablePath.UserPath -in $wantedVariables) {
            $pieces.Add($statement.Extent.Text); $found.Add($statement.Left.VariablePath.UserPath)
        }
        elseif ($statement -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $statement.Name -in $wantedFunctions) {
            $pieces.Add($statement.Extent.Text); $found.Add($statement.Name)
        }
    }
    $missing = @($wantedVariables + $wantedFunctions | Where-Object { $_ -notin $found })
    if ($missing.Count -gt 0) { throw "Not found in the script: $($missing -join ', ')" }

    # ── The Dataverse calls, as a test double ────────────────────────────────────────────────────────────────
    function Start-Sleep { }
    function Invoke-DvGet([string]$Path) {
        $script:Dv.Reads.Add($Path)
        $bare = ($Path -split '\?')[0]
        if ($bare -match "Attributes\(LogicalName='(?<col>\w+)'\)(/Microsoft\.Dynamics\.CRM\.(?<cast>\w+))?$") {
            $a = $script:Dv.Attrs[$Matches.col]
            return [pscustomobject]@{ IsSecured = $a.IsSecured; Precision = $a.Precision; MinValue = $a.MinValue; MaxValue = $a.MaxValue; Targets = $a.Targets }
        }
        if ($bare -eq 'fieldpermissions') {
            $col = [regex]::Match($Path, "attributelogicalname eq '(\w+)'").Groups[1].Value
            $profile = [regex]::Match($Path, '_fieldsecurityprofileid_value eq ([\w-]+)')
            $rows = @($script:Dv.Perms | Where-Object { $_.attributelogicalname -eq $col -and ((-not $profile.Success) -or $_.profileid -eq $profile.Groups[1].Value) })
            return [pscustomobject]@{ value = @($rows | ForEach-Object {
                        [pscustomobject]@{ fieldpermissionid = $_.id; canread = 4; cancreate = $_.create; canupdate = $_.create
                            fieldsecurityprofileid = [pscustomobject]@{ name = $_.profilename } } }) }
        }
        throw "unexpected GET $Path"
    }
    function Invoke-DvWrite([string]$Method, [string]$Path, $Body, [hashtable]$Extra = @{}) {
        $script:Dv.Writes.Add("$Method $Path")
        if ($Method -eq 'PUT' -and $Path -match "Attributes\(LogicalName='(?<col>\w+)'\)") {
            $col = $Matches.col
            if ($script:Dv.OnPut) { & $script:Dv.OnPut $col $Body $Path }
            $script:Dv.Attrs[$col].IsSecured = [bool]$Body.IsSecured
            foreach ($k in 'Precision', 'MinValue', 'MaxValue', 'Targets') { if ($script:Dv.Drift -and $script:Dv.Drift.ContainsKey("$col.$k")) { $script:Dv.Attrs[$col][$k] = $script:Dv.Drift["$col.$k"] } }
            return
        }
        if ($Method -eq 'POST' -and $Path -eq 'fieldpermissions') {
            if ($script:Dv.OnGrant) { & $script:Dv.OnGrant $Body }
            $profileId = ($Body['fieldsecurityprofileid@odata.bind'] -replace '^/fieldsecurityprofiles\((.*)\)$', '$1')
            $script:Dv.Perms.Add([pscustomobject]@{ id = [guid]::NewGuid().ToString(); attributelogicalname = $Body.attributelogicalname; profileid = $profileId
                    profilename = $(if ($profileId -eq 'w') { 'Spaarke BFF-Managed Field Writers' } else { 'Spaarke BFF-Managed Field Readers' }); create = $Body.cancreate })
            return
        }
        if ($Method -eq 'DELETE' -and $Path -match '^fieldpermissions\((?<id>[\w-]+)\)$') {
            if ($script:Dv.OnDelete) { & $script:Dv.OnDelete $Matches.id }
            $id = $Matches.id
            $script:Dv.Perms.RemoveAll({ param($p) $p.id -eq $id }) | Out-Null
            return
        }
        if ($Method -eq 'POST' -and $Path -eq 'PublishXml') { return }
        throw "unexpected $Method $Path"
    }

    . ([scriptblock]::Create($pieces -join "`n"))

    # $Cmdlet as the script sees it ($Cmdlet = $PSCmdlet in the script): the operator's answer to each ShouldProcess question comes from $script:Decline (action|target).
    $script:Cmdlet = New-Object psobject
    $script:Cmdlet | Add-Member -MemberType ScriptMethod -Name ShouldProcess -Value {
        param($Target, $Action)
        $script:Asked.Add("$Action|$Target")
        return -not ($script:Decline -contains "$Action|$Target")
    }

    function New-Dv([string[]]$SecuredColumns = @()) {
        $attrs = @{}
        foreach ($c in $script:AllColumns) {
            $a = @{ IsSecured = ($c -in $SecuredColumns); Precision = 2; MinValue = -922337203685477; MaxValue = 1000000000; Targets = $null }
            if ($c -eq 'sprk_vendororg') { $a.Precision = $null; $a.MinValue = $null; $a.MaxValue = $null; $a.Targets = @('sprk_organization') }
            $attrs[$c] = $a
        }
        @{ Attrs = $attrs; Perms = [System.Collections.Generic.List[object]]::new(); Writes = [System.Collections.Generic.List[string]]::new()
            Reads = [System.Collections.Generic.List[string]]::new(); OnPut = $null; OnGrant = $null; OnDelete = $null; Drift = @{} }
    }
    function Grant-Both([string]$Col) {
        foreach ($p in @(@{ id = 'w'; name = 'Spaarke BFF-Managed Field Writers'; create = 4 }, @{ id = 'r'; name = 'Spaarke BFF-Managed Field Readers'; create = 0 })) {
            $script:Dv.Perms.Add([pscustomobject]@{ id = [guid]::NewGuid().ToString(); attributelogicalname = $Col; profileid = $p.id; profilename = $p.name; create = $p.create })
        }
    }
    function Get-Writes([string]$Prefix) { @($script:Dv.Writes | Where-Object { $_ -like "$Prefix*" }) }

    function Reset-State {
        $script:Dv = New-Dv
        $script:Asked = [System.Collections.Generic.List[string]]::new()
        $script:Decline = @()
        $script:Wrote = $false
        $script:gaps = [System.Collections.Generic.List[string]]::new()
        $script:specs = @(@{ Id = 'w'; Name = 'Spaarke BFF-Managed Field Writers'; Create = 4 }, @{ Id = 'r'; Name = 'Spaarke BFF-Managed Field Readers'; Create = 0 })
        $script:IsDryRun = $false; $script:IsWrite = $true; $script:Verify = $false
    }

    $script:AllColumns = @($Columns)
    $script:publishBody = @{ ParameterXml = '<publish/>' }
}

Describe 'K1: the column definition goes through its typed path, and a changed shape fails loudly' {
    BeforeEach { Reset-State }

    It 'reads and writes each Money column through MoneyAttributeMetadata and the firm lookup through LookupAttributeMetadata' {
        foreach ($c in $script:AllColumns) {
            $script:Dv = New-Dv
            Set-ColumnSecured $c $true
            $cast = if ($c -eq 'sprk_vendororg') { 'LookupAttributeMetadata' } else { 'MoneyAttributeMetadata' }
            $put = @(Get-Writes 'PUT')
            $put.Count | Should -Be 1 -Because $c
            $put[0] | Should -Be "PUT EntityDefinitions(LogicalName='sprk_invoice')/Attributes(LogicalName='$c')/Microsoft.Dynamics.CRM.$cast"
            @($script:Dv.Reads | Where-Object { $_ -notlike "*Microsoft.Dynamics.CRM.$cast" }) | Should -BeNullOrEmpty -Because "$c is only read through the typed path"
        }
    }

    It 'sets IsSecured and nothing else (precision, range and targets read back unchanged)' {
        Set-ColumnSecured 'sprk_feestotal' $true
        $script:Dv.Attrs['sprk_feestotal'].IsSecured | Should -BeTrue
        $script:Dv.Attrs['sprk_feestotal'].Precision | Should -Be 2
        $script:Dv.Attrs['sprk_feestotal'].MinValue | Should -Be -922337203685477
        $script:Dv.Attrs['sprk_feestotal'].MaxValue | Should -Be 1000000000
    }

    It 'fails loudly when the write moves a Money column''s <Property>' -ForEach @(
        @{ Property = 'Precision'; Value = 4 }
        @{ Property = 'MinValue'; Value = 0 }
        @{ Property = 'MaxValue'; Value = 1000 }
    ) {
        $script:Dv.Drift["sprk_approvedtotal.$Property"] = $Value
        { Set-ColumnSecured 'sprk_approvedtotal' $true } | Should -Throw '*changed shape*'
    }

    It 'fails loudly when the write moves the firm lookup''s targets' {
        $script:Dv.Drift['sprk_vendororg.Targets'] = @('account')
        { Set-ColumnSecured 'sprk_vendororg' $true } | Should -Throw '*changed shape*'
    }

    It 'the same check guards un-securing' {
        $script:Dv = New-Dv -SecuredColumns @('sprk_totalamount')
        $script:Dv.Drift['sprk_totalamount.Precision'] = 0
        { Set-ColumnSecured 'sprk_totalamount' $false } | Should -Throw '*changed shape*'
    }
}

Describe 'K2: an apply that fails on column N' {
    BeforeEach {
        Reset-State
        $script:attrs = @{}
        foreach ($c in $script:AllColumns) { $script:attrs[$c] = [pscustomobject]@{ IsSecured = $false } }
    }

    It 'leaves the earlier columns secured and granted, publishes what was written, and names what completed' {
        $Columns = @('sprk_totalamount', 'sprk_feestotal', 'sprk_vendororg')
        $script:Dv.OnPut = { param($col, $body, $path) if ($col -eq 'sprk_feestotal') { throw 'the service refused the update' } }

        $r = Invoke-SecureColumns

        $r.Error | Should -Not -BeNullOrEmpty
        $r.FailedColumn | Should -Be 'sprk_feestotal'
        @($r.Completed) | Should -Be @('sprk_totalamount')
        $script:Dv.Attrs['sprk_totalamount'].IsSecured | Should -BeTrue
        @($script:Dv.Perms | Where-Object { $_.attributelogicalname -eq 'sprk_totalamount' }).Count | Should -Be 2 -Because 'writer and reader are granted on the completed column'
        $script:Dv.Attrs['sprk_feestotal'].IsSecured | Should -BeFalse
        @(Get-Writes 'PUT') | Should -Not -Contain "PUT EntityDefinitions(LogicalName='sprk_invoice')/Attributes(LogicalName='sprk_vendororg')/Microsoft.Dynamics.CRM.LookupAttributeMetadata" -Because 'the run stops at the failure'
        @(Get-Writes 'POST PublishXml').Count | Should -Be 1 -Because 'what was written is published'
        $r.Reverted | Should -BeFalse
    }

    It 'never leaves a column masked when the write was applied before the failure (a changed shape)' {
        $Columns = @('sprk_totalamount', 'sprk_feestotal')
        $script:Dv.Drift['sprk_feestotal.Precision'] = 4

        $r = Invoke-SecureColumns

        $r.FailedColumn | Should -Be 'sprk_feestotal'
        $r.Error.Exception.Message | Should -BeLike '*changed shape*'
        @($r.Completed) | Should -Be @('sprk_totalamount')
        $script:Dv.Attrs['sprk_feestotal'].IsSecured | Should -BeTrue -Because 'the PUT was applied'
        @($script:Dv.Perms | Where-Object { $_.attributelogicalname -eq 'sprk_feestotal' }).Count | Should -Be 2 -Because 'so it is granted, never left secured with no grants'
        @(Get-Writes 'POST PublishXml').Count | Should -Be 1
    }

    It 'still reverts every column this run secured when a grant fails (a masked column is never left behind)' {
        $Columns = @('sprk_totalamount', 'sprk_feestotal')
        $script:Dv.OnGrant = { param($body) if ($body.attributelogicalname -eq 'sprk_feestotal') { throw 'grant refused' } }

        $r = Invoke-SecureColumns

        $r.Reverted | Should -BeTrue
        $r.FailedColumn | Should -Be 'sprk_feestotal'
        @($r.Completed).Count | Should -Be 0
        $script:Dv.Attrs['sprk_totalamount'].IsSecured | Should -BeFalse
        $script:Dv.Attrs['sprk_feestotal'].IsSecured | Should -BeFalse
        @(Get-Writes 'POST PublishXml').Count | Should -Be 1 -Because 'the revert published once and the stop does not publish again'
    }

    It 'a run with no failure completes every column, reports no error and publishes nothing itself' {
        $Columns = @('sprk_totalamount', 'sprk_feestotal', 'sprk_vendororg')

        $r = Invoke-SecureColumns

        $r.Error | Should -BeNullOrEmpty
        @($r.Completed) | Should -Be $Columns
        foreach ($c in $Columns) { $script:Dv.Attrs[$c].IsSecured | Should -BeTrue }
        @(Get-Writes 'POST PublishXml').Count | Should -Be 0 -Because 'the main flow publishes once at the end'
    }
}

Describe 'K2: -Revert' {
    BeforeEach {
        Reset-State
        $script:Dv = New-Dv -SecuredColumns $script:AllColumns
        foreach ($c in $script:AllColumns) { Grant-Both $c }
    }

    It 'un-secures every column and deletes both profiles'' rows on it when nothing is declined (must not fire)' {
        $r = Invoke-RevertColumns

        $r.Failures.Count | Should -Be 0
        $r.Wrote | Should -BeTrue
        foreach ($c in $script:AllColumns) { $script:Dv.Attrs[$c].IsSecured | Should -BeFalse }
        $script:Dv.Perms.Count | Should -Be 0
        (Get-Writes 'DELETE').Count | Should -Be 14
    }

    It 'keeps a column''s permission rows when un-securing it is declined, so the column is not left secured with no grants (must fire)' {
        $script:Decline = @('Revert: un-secure the column|sprk_invoice.sprk_feestotal')

        $r = Invoke-RevertColumns

        $script:Dv.Attrs['sprk_feestotal'].IsSecured | Should -BeTrue
        @($script:Dv.Perms | Where-Object { $_.attributelogicalname -eq 'sprk_feestotal' }).Count | Should -Be 2
        $script:Dv.Attrs['sprk_totalamount'].IsSecured | Should -BeFalse
        @($script:Dv.Perms | Where-Object { $_.attributelogicalname -eq 'sprk_totalamount' }).Count | Should -Be 0 -Because 'only the declined column keeps its rows'
        (Get-Writes 'DELETE').Count | Should -Be 12
    }

    It 'a failure to un-secure one column is recorded, keeps that column''s rows, and the other columns are still reverted' {
        $script:Dv.OnPut = { param($col, $body, $path) if ($col -eq 'sprk_expensestotal') { throw 'the service refused the update' } }

        $r = Invoke-RevertColumns

        $r.Failures.Count | Should -Be 1
        $r.Failures[0] | Should -BeLike '*sprk_expensestotal*'
        $r.Wrote | Should -BeTrue
        @($script:Dv.Perms | Where-Object { $_.attributelogicalname -eq 'sprk_expensestotal' }).Count | Should -Be 2
        foreach ($c in $script:AllColumns | Where-Object { $_ -ne 'sprk_expensestotal' }) { $script:Dv.Attrs[$c].IsSecured | Should -BeFalse -Because $c }
    }

    It 'a dry run writes nothing' {
        $script:IsDryRun = $true

        $r = Invoke-RevertColumns

        $r.Wrote | Should -BeFalse
        $script:Dv.Writes.Count | Should -Be 0
        foreach ($c in $script:AllColumns) { $script:Dv.Attrs[$c].IsSecured | Should -BeTrue }
    }
}
