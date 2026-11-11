# Pester 5.7.1. Run: Import-Module Pester -RequiredVersion 5.7.1 -Force; Invoke-Pester tests/scripts/Set-ThreadFlagFieldSecurity.Tests.ps1
# CI: .github/workflows/ci-standalone-checks.yml, job owner-teams-script-tests (ubuntu-latest).
# field-mapping-server-write-path-r1 task 054b: scripts/Set-ThreadFlagFieldSecurity.ps1 over a mocked Dataverse (every write
# is a test failure). Pins precondition (p0): each of the five columns must exist before anything else is read or written,
# in every mode; a missing sprk_directrecipient names scripts/Set-DirectRecipientSchema.ps1 -Apply and exits 1.
# Must not fire: with all five columns present the script passes (p0) and goes on to the profile reads.

BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:Script = Join-Path $script:RepoRoot 'scripts' 'Set-ThreadFlagFieldSecurity.ps1'
    $script:Url = 'https://example.crm.dynamics.com'
    $script:BffAppId = '00000000-0000-4000-8000-0000000000bf'
    # Mock needs the command to exist; a runner without the az CLI gets a stub (the mock replaces it in every test).
    if (-not (Get-Command az -ErrorAction SilentlyContinue)) { function global:az { throw 'Test: az is never called unmocked.' } }

    # The marker the mock throws on the first read after (p0): reaching it proves (p0) passed.
    $global:SetThreadFlagFieldSecurityTest_PastP0 = 'TEST-MARKER: past the column check'

    function Reset-World {
        $global:SetThreadFlagFieldSecurityTest_Columns = @{
            sprk_communicationthread = @('sprk_communicationthreadid', 'sprk_name', 'sprk_threadtype', 'sprk_isdefaultthread')
            sprk_communication       = @('sprk_communicationid', 'sprk_name', 'sprk_sentby', 'sprk_directrecipient', 'sprk_communicationthread')
        }
        $global:SetThreadFlagFieldSecurityTest_Reads = [System.Collections.Generic.List[string]]::new()
    }

    function Invoke-Script([hashtable]$Extra = @{}) {
        $arguments = @{ EnvironmentUrl = $script:Url; BffApplicationIds = @($script:BffAppId) }
        foreach ($k in $Extra.Keys) { $arguments[$k] = $Extra[$k] }
        $global:LASTEXITCODE = 0
        $thrown = $null
        try { $output = & $script:Script @arguments 6>&1 2>&1 | Out-String }
        catch { $thrown = $_.Exception.Message; $output = '' }
        [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output; Thrown = $thrown }
    }
}

AfterAll {
    Remove-Variable -Scope Global -Name 'SetThreadFlagFieldSecurityTest_*' -ErrorAction SilentlyContinue
}

Describe 'Set-ThreadFlagFieldSecurity.ps1 precondition (p0): every target column exists (mocked Dataverse, no writes)' {
    BeforeEach {
        Reset-World
        Mock az { 'test-token' }
        Mock Invoke-RestMethod {
            if ($Method -ne 'Get') { throw "Test: the script wrote ($Method $Uri)." }
            $u = [string]$Uri
            $global:SetThreadFlagFieldSecurityTest_Reads.Add($u)
            if ($u -match '/organizations\?') { return @{ value = @([pscustomobject]@{ name = 'test' }) } }
            if ($u -match "EntityDefinitions\(LogicalName='([^']+)'\)/Attributes\?") {
                return @{ value = @($global:SetThreadFlagFieldSecurityTest_Columns[$Matches[1]] | ForEach-Object { [pscustomobject]@{ LogicalName = $_ } }) }
            }
            throw $global:SetThreadFlagFieldSecurityTest_PastP0
        }
    }

    It 'refuses with exit 1 and names Set-DirectRecipientSchema.ps1 -Apply when sprk_directrecipient is missing (<Mode>)' -ForEach @(
        @{ Mode = 'dry run'; Extra = @{} }
        @{ Mode = 'Apply'; Extra = @{ Apply = $true } }
        @{ Mode = 'Verify'; Extra = @{ Verify = $true } }
    ) {
        $global:SetThreadFlagFieldSecurityTest_Columns['sprk_communication'] = @('sprk_communicationid', 'sprk_sentby', 'sprk_communicationthread')

        $r = Invoke-Script $Extra

        $r.Thrown | Should -BeNullOrEmpty
        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match 'sprk_communication\.sprk_directrecipient does not exist'
        $r.Output | Should -Match 'Set-DirectRecipientSchema\.ps1 -Apply'
        $r.Output | Should -Match 'Nothing was written'
        # Nothing past (p0) was read: no profile, view, form or column-security read.
        @($global:SetThreadFlagFieldSecurityTest_Reads | Where-Object { $_ -notmatch '/organizations\?|/Attributes\?' }) | Should -BeNullOrEmpty
    }

    It 'refuses with exit 1 when one of the solution columns is missing, naming it' {
        $global:SetThreadFlagFieldSecurityTest_Columns['sprk_communicationthread'] = @('sprk_communicationthreadid', 'sprk_isdefaultthread')

        $r = Invoke-Script @{ Apply = $true }

        $r.Thrown | Should -BeNullOrEmpty
        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match 'sprk_communicationthread\.sprk_threadtype does not exist'
        $r.Output | Should -Not -Match 'Set-DirectRecipientSchema'
    }

    It 'passes (p0) and goes on to the profile reads when all five columns exist (must not fire)' {
        $r = Invoke-Script @{ Verify = $true }

        $r.Thrown | Should -Be $global:SetThreadFlagFieldSecurityTest_PastP0
        @($global:SetThreadFlagFieldSecurityTest_Reads | Where-Object { $_ -match 'fieldsecurityprofiles' }) | Should -HaveCount 1
    }
}
