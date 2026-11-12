# Pester 5. Run: Import-Module Pester -RequiredVersion 5.7.1; Invoke-Pester tests/scripts/EbillingDefaultTeamExclusion.Tests.ps1
# ebilling-r1: a reserved business unit's default team ("Spaarke Ontology Writer", scripts/common/OntologyWriterUnit.ps1) is left out of the
# reader profile by the six scripts that check or add "every default team". These tests run each script's own function (lifted out of its
# syntax tree) against a test double of the Dataverse calls: no environment, no network, no script is run.
#   Set-Ebilling{FirmReason,InvoiceMoney,InvoiceFirmHold,ResubmissionOf}*  ->  Test-ReaderOnDefaultTeam            (reads and reports; adds nothing)
#   Deploy-EbillingProcedureSchema                                          ->  Test-ProfilePreconditions (-CreateProfiles adds default teams)
#   Deploy-EbillingStoredFileSchema                                         ->  Test-ProfilePreconditions (reuse only)
# Must-fire : the reserved team is never in the apply set; the reserved team on the reader profile fails (every mode); two matching
#             units refuse; a failed unit read refuses.
# Must-not-fire: no reserved unit -> as before; the reserved unit exists and its team holds nothing -> passes; another team missing from
#             the profile still fails.

BeforeAll {
    $script:Repo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    . (Join-Path $script:Repo 'scripts' 'common' 'OntologyWriterUnit.ps1')

    if (-not ('EscalationException' -as [type])) {
        Add-Type -TypeDefinition 'public class EscalationException : System.Exception { public EscalationException(string m) : base(m) {} }'
    }

    function script:Get-ScriptFunction([string]$File, [string]$Name) {
        $errs = $null; $tokens = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $script:Repo 'scripts' 'ebilling' $File), [ref]$tokens, [ref]$errs)
        $fn = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $Name }, $true) | Select-Object -First 1
        if ($fn) { return $fn.Extent.Text }
        return $null
    }

    # ── test double ──────────────────────────────────────────────────────────────────────────────────────────
    # World: Teams (Id, Name, Bu = business unit id, BuName), ReaderTeamIds, Units (businessunits rows), UnitsError.
    $script:World = $null
    $script:Reports = [System.Collections.Generic.List[string]]::new()
    $script:Api = 'https://example.invalid/api/data/v9.2'
    $script:headers = @{ Authorization = 'Bearer x' }
    $script:WriterUnitId = '11111111-1111-1111-1111-111111111111'
    $script:WriterTeamId = '22222222-2222-2222-2222-222222222222'

    function script:Get-TeamRows {
        @($script:World.Teams | ForEach-Object {
                [pscustomobject]@{ teamid = $_.Id; name = $_.Name; _businessunitid_value = $_.Bu; businessunitid = [pscustomobject]@{ name = $_.BuName; isdisabled = $false; businessunitid = $_.Bu } } })
    }
    function Invoke-RestMethod {
        param([string]$Uri, $Headers, $Method)
        $script:World.RestReads.Add($Uri)
        if ($Uri -like '*/businessunits?*') {
            if ($script:World.UnitsError) { throw $script:World.UnitsError }
            return [pscustomobject]@{ value = @($script:World.Units | ForEach-Object { [pscustomobject]@{ businessunitid = $_; name = 'Spaarke Ontology Writer' } }) }
        }
        if ($Uri -like '*/fieldsecurityprofiles(*)/teamprofiles_association?*') {
            return [pscustomobject]@{ value = @($script:World.ReaderTeamIds | ForEach-Object { [pscustomobject]@{ teamid = $_ } }) }
        }
        throw "unexpected REST read $Uri"
    }
    function Report([string]$State, [string]$What) { $script:Reports.Add("$State|$What") }
    function Invoke-DvGet([string]$Path) {
        if ($Path -like 'teams?*') { return [pscustomobject]@{ value = @(Get-TeamRows) } }
        throw "unexpected GET $Path"
    }
    function Start-Sleep { }

    # Dev as it will be once the unit exists: three ordinary units and the reserved one.
    function script:New-World([bool]$WithWriterUnit = $true, [bool]$WriterTeamOnReader = $false, [string[]]$MissingFromReader = @(), $Units = $null, $UnitsError = $null) {
        $teams = @(
            @{ Id = 'a0000000-0000-0000-0000-00000000000a'; Name = 'Spaarke'; Bu = 'b0000000-0000-0000-0000-00000000000a'; BuName = 'Spaarke' }
            @{ Id = 'a0000000-0000-0000-0000-00000000000b'; Name = 'Spaarke Demo'; Bu = 'b0000000-0000-0000-0000-00000000000b'; BuName = 'Spaarke Demo' }
        )
        if ($WithWriterUnit) { $teams += @{ Id = $script:WriterTeamId; Name = 'Spaarke Ontology Writer'; Bu = $script:WriterUnitId; BuName = 'Spaarke Ontology Writer' } }
        $onReader = @($teams | Where-Object { $_.Id -ne $script:WriterTeamId -and $_.Name -notin $MissingFromReader } | ForEach-Object { $_.Id })
        if ($WriterTeamOnReader) { $onReader += $script:WriterTeamId }
        if ($null -eq $Units) { $Units = if ($WithWriterUnit) { @($script:WriterUnitId) } else { @() } }
        $script:World = @{ Teams = $teams; ReaderTeamIds = $onReader; Units = $Units; UnitsError = $UnitsError; RestReads = [System.Collections.Generic.List[string]]::new() }
        $script:Reports = [System.Collections.Generic.List[string]]::new()
    }
    function script:Get-Fails { @($script:Reports | Where-Object { $_ -like 'FAIL|*' }) }
}

Describe 'the script text' -ForEach @(
    @{ File = 'Set-EbillingFirmReasonFieldSecurity.ps1' }, @{ File = 'Set-EbillingInvoiceMoneyFieldSecurity.ps1' }, @{ File = 'Set-EbillingInvoiceFirmHoldColumn.ps1' },
    @{ File = 'Set-EbillingResubmissionOfFieldSecurity.ps1' }, @{ File = 'Deploy-EbillingProcedureSchema.ps1' }, @{ File = 'Deploy-EbillingStoredFileSchema.ps1' }
) {
    It '<File> dot-sources the shared helper and splits the default teams through it (must-fire if a site goes back to the raw query)' {
        $text = Get-Content -LiteralPath (Join-Path $script:Repo 'scripts' 'ebilling' $File) -Raw
        $text | Should -Match "OntologyWriterUnit\.ps1"
        $text | Should -Match 'Split-DvDefaultTeamsByOntologyWriterUnit'
        $text | Should -Match 'Get-DvOntologyWriterUnitTeamsOnProfile'
        $text | Should -Match 'select=teamid,name,_businessunitid_value'
    }
}

# ── The four verify-only scripts (they add no team to any profile; the reserved team is not expected on the reader) ─────────────────
Describe 'Test-ReaderOnDefaultTeam in <File>' -ForEach @(
    @{ File = 'Set-EbillingFirmReasonFieldSecurity.ps1' }, @{ File = 'Set-EbillingInvoiceMoneyFieldSecurity.ps1' },
    @{ File = 'Set-EbillingInvoiceFirmHoldColumn.ps1' }, @{ File = 'Set-EbillingResubmissionOfFieldSecurity.ps1' }
) {
    BeforeAll {
        $text = Get-ScriptFunction $File 'Test-ReaderOnDefaultTeam'
        if ($text) { Invoke-Expression $text }
        $script:ReaderProfileName = 'Spaarke BFF-Managed Field Readers'
        $script:ReaderId = '33333333-3333-3333-3333-333333333333'
    }
    It 'passes with no reserved unit, and says "none" (must-not-fire: as before)' {
        New-World -WithWriterUnit $false
        Test-ReaderOnDefaultTeam $script:World.ReaderTeamIds $script:ReaderId
        @(Get-Fails) | Should -HaveCount 0
        @($script:Reports | Where-Object { $_ -like 'OK|*' }) | Should -HaveCount 2
        ($script:Reports | Where-Object { $_ -like 'INFO|*' }) | Should -Match 'left out of the reader profile: none'
    }
    It 'passes when the reserved unit exists and its team holds nothing, and names the team left out (must-not-fire)' {
        New-World
        Test-ReaderOnDefaultTeam $script:World.ReaderTeamIds $script:ReaderId
        @(Get-Fails) | Should -HaveCount 0
        @($script:Reports | Where-Object { $_ -like 'OK|*' }) | Should -HaveCount 2
        ($script:Reports | Where-Object { $_ -like 'INFO|*' }) | Should -Match "'Spaarke Ontology Writer' \($script:WriterTeamId\)"
    }
    It 'does not demand the reserved team on the reader profile (must-fire: the old code FAILs it as "NOT on the reader profile")' {
        New-World
        Test-ReaderOnDefaultTeam $script:World.ReaderTeamIds $script:ReaderId
        ($script:Reports | Where-Object { $_ -like '*NOT on the reader profile*' }) | Should -BeNullOrEmpty
        ($script:Reports | Where-Object { $_ -like 'OK|*Ontology Writer*' }) | Should -BeNullOrEmpty
    }
    It 'FAILS, with a clear line, when the reserved unit''s team holds the reader profile (must-fire)' {
        New-World -WriterTeamOnReader $true
        Test-ReaderOnDefaultTeam $script:World.ReaderTeamIds $script:ReaderId
        $fails = @(Get-Fails)
        $fails | Should -HaveCount 1
        $fails[0] | Should -Match "ontology writer business unit's default team 'Spaarke Ontology Writer' holds the reader profile"
    }
    It 'still FAILS for another team that is missing from the reader profile (must-not-fire for the others)' {
        New-World -MissingFromReader @('Spaarke Demo')
        Test-ReaderOnDefaultTeam $script:World.ReaderTeamIds $script:ReaderId
        $fails = @(Get-Fails)
        $fails | Should -HaveCount 1
        $fails[0] | Should -Match "default team 'Spaarke Demo' .* is NOT on the reader profile"
    }
    It 'refuses (throws) when two business units carry the reserved name (must-fire)' {
        New-World -Units @('c0000000-0000-0000-0000-000000000001', 'c0000000-0000-0000-0000-000000000002')
        { Test-ReaderOnDefaultTeam $script:World.ReaderTeamIds $script:ReaderId } | Should -Throw '*Found 2 business units named*'
    }
    It 'refuses (throws) when the business unit read fails; it is not treated as "no such unit" (must-fire)' {
        New-World -UnitsError 'HTTP 403 Forbidden'
        { Test-ReaderOnDefaultTeam $script:World.ReaderTeamIds $script:ReaderId } | Should -Throw '*403*'
    }
}

# ── The two Deploy scripts: Test-ProfilePreconditions ─────────────────────────────────────────────────────────────────────────────
Describe 'Test-ProfilePreconditions in <File>' -ForEach @(
    @{ File = 'Deploy-EbillingProcedureSchema.ps1' }, @{ File = 'Deploy-EbillingStoredFileSchema.ps1' }
) {
    BeforeAll {
        $text = Get-ScriptFunction $File 'Test-ProfilePreconditions'
        Invoke-Expression $text
        $script:ReaderProfileName = 'Readers'; $script:WriterProfileName = 'Writers'
        $script:BffApplicationIds = @('app-1')
        $script:ReaderBusinessUnits = @('Spaarke', 'Spaarke Demo')
        $script:CreateProfiles = $false
        $script:State = [pscustomobject]@{ ApiBase = $script:Api }
        $script:ReaderId = $null; $script:WriterId = $null
        $script:ProfilesToCreate = [System.Collections.Generic.List[string]]::new()
        $script:ReaderExists = $true

        function Write-Report([string]$S, [string]$What) { $script:Reports.Add("$S|$What") }
        function Get-DataverseToken { 'token' }
        function Get-ProfileByName([string]$Name) {
            if ($Name -eq 'Readers') { if ($script:ReaderExists) { return @([pscustomobject]@{ fieldsecurityprofileid = '33333333-3333-3333-3333-333333333333' }) } return @() }
            return @([pscustomobject]@{ fieldsecurityprofileid = 'w-id' })
        }
        function Invoke-Dv {
            param([string]$Path, [string]$Method = 'GET', $Body, $ExtraHeaders)
            if ($Path -like 'teams?*') { return [pscustomobject]@{ value = @(Get-TeamRows) } }
            if ($Path -like 'fieldsecurityprofiles(3333*)/teamprofiles_association*') { return [pscustomobject]@{ value = @($script:World.ReaderTeamIds | ForEach-Object { [pscustomobject]@{ teamid = $_; name = 'x' } }) } }
            if ($Path -like 'fieldsecurityprofiles(w-id)/systemuserprofiles_association*') { return [pscustomobject]@{ value = @([pscustomobject]@{ systemuserid = 'u-bff'; fullname = 'bff'; applicationid = 'app-1' }) } }
            if ($Path -like 'fieldsecurityprofiles(w-id)/teamprofiles_association*') { return [pscustomobject]@{ value = @() } }
            if ($Path -like 'systemusers?*') { return [pscustomobject]@{ value = @([pscustomobject]@{ systemuserid = 'u-bff'; fullname = 'bff' }) } }
            throw "unexpected read: $Path"
        }
        function Reset-State { $script:ProfilesToCreate = [System.Collections.Generic.List[string]]::new(); $script:ReaderExists = $true; $script:CreateProfiles = $false }
    }
    BeforeEach { Reset-State }

    It 'passes with no reserved unit, and says "none" (must-not-fire: as before)' {
        New-World -WithWriterUnit $false
        { Test-ProfilePreconditions } | Should -Not -Throw
        ($script:Reports | Where-Object { $_ -like 'INFO|*left out of the reader profile*' }) | Should -Match 'none$'
    }
    It 'passes when the reserved unit exists and its team holds nothing (must-not-fire: the old code threw "NOT on the reader profile")' {
        New-World
        { Test-ProfilePreconditions } | Should -Not -Throw
        ($script:Reports | Where-Object { $_ -like 'INFO|*left out of the reader profile*' }) | Should -Match "'Spaarke Ontology Writer'"
    }
    It 'throws an escalation, naming the team, when the reserved unit''s team holds the reader profile (must-fire)' {
        New-World -WriterTeamOnReader $true
        { Test-ProfilePreconditions } | Should -Throw "*ontology writer business unit's default team 'Spaarke Ontology Writer' holds 'Readers'*"
    }
    It 'still escalates for another team that is missing from the reader profile (must-not-fire for the others)' {
        New-World -MissingFromReader @('Spaarke Demo')
        { Test-ProfilePreconditions } | Should -Throw "*default team 'Spaarke Demo' (BU 'Spaarke Demo') is NOT on 'Readers'*"
    }
    It 'refuses when two business units carry the reserved name (must-fire)' {
        New-World -Units @('c0000000-0000-0000-0000-000000000001', 'c0000000-0000-0000-0000-000000000002')
        { Test-ProfilePreconditions } | Should -Throw '*Found 2 business units named*'
    }
    It 'refuses when the business unit read fails (must-fire)' {
        New-World -UnitsError 'HTTP 500'
        { Test-ProfilePreconditions } | Should -Throw '*500*'
    }
}

Describe 'Deploy-EbillingProcedureSchema -CreateProfiles: the apply set' {
    BeforeAll {
        Invoke-Expression (Get-ScriptFunction 'Deploy-EbillingProcedureSchema.ps1' 'Test-ProfilePreconditions')
        $script:ReaderProfileName = 'Readers'; $script:WriterProfileName = 'Writers'
        $script:BffApplicationIds = @('app-1')
        $script:State = [pscustomobject]@{ ApiBase = $script:Api }
        function Write-Report([string]$S, [string]$What) { $script:Reports.Add("$S|$What") }
        function Get-DataverseToken { 'token' }
        function Get-ProfileByName([string]$Name) { if ($Name -eq 'Readers') { return @() } return @([pscustomobject]@{ fieldsecurityprofileid = 'w-id' }) }
        function Invoke-Dv {
            param([string]$Path)
            if ($Path -like 'teams?*') { return [pscustomobject]@{ value = @(Get-TeamRows) } }
            if ($Path -like 'fieldsecurityprofiles(w-id)/systemuserprofiles_association*') { return [pscustomobject]@{ value = @([pscustomobject]@{ systemuserid = 'u-bff'; fullname = 'bff'; applicationid = 'app-1' }) } }
            if ($Path -like 'fieldsecurityprofiles(w-id)/teamprofiles_association*') { return [pscustomobject]@{ value = @() } }
            if ($Path -like 'systemusers?*') { return [pscustomobject]@{ value = @([pscustomobject]@{ systemuserid = 'u-bff'; fullname = 'bff' }) } }
            throw "unexpected read: $Path"
        }
    }
    BeforeEach { $script:CreateProfiles = $true; $script:ProfilesToCreate = [System.Collections.Generic.List[string]]::new() }

    It 'never puts the reserved unit''s team in the set it adds to a new reader profile (must-fire)' {
        New-World
        $pre = Test-ProfilePreconditions
        @($pre.DefaultTeams | ForEach-Object { $_.teamid }) | Should -Not -Contain $script:WriterTeamId
        @($pre.DefaultTeams) | Should -HaveCount 2
    }
    It 'adds every other default team (must-not-fire: the others are unaffected)' {
        New-World
        $pre = Test-ProfilePreconditions
        @($pre.DefaultTeams | ForEach-Object { $_.name } | Sort-Object) -join ',' | Should -Be 'Spaarke,Spaarke Demo'
    }
    It 'adds all of them when no reserved unit exists (must-not-fire: as before)' {
        New-World -WithWriterUnit $false
        (Test-ProfilePreconditions).DefaultTeams | Should -HaveCount 2
    }
    It 'prints the excluded team next to the list it would add (dry run) and "none" when there is no reserved unit' {
        New-World
        Test-ProfilePreconditions | Out-Null
        ($script:Reports | Where-Object { $_ -like 'WOULD|*' }) | Should -Match "add every business unit default team \(2: 'Spaarke', 'Spaarke Demo'\); left out \(ontology writer business unit\): 'Spaarke Ontology Writer'"
        New-World -WithWriterUnit $false
        $script:ProfilesToCreate = [System.Collections.Generic.List[string]]::new()
        Test-ProfilePreconditions | Out-Null
        ($script:Reports | Where-Object { $_ -like 'WOULD|*' }) | Should -Match 'left out \(ontology writer business unit\): none$'
    }
}
