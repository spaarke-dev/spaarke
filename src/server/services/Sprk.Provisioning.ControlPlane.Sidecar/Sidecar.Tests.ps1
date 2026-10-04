# Pester 5 tests for SidecarCore.psm1 (task 251). Run: Invoke-Pester ./Sidecar.Tests.ps1
# CI: .github/workflows/build-provisioning-sidecar.yml runs these before the image build.
BeforeAll { Import-Module (Join-Path $PSScriptRoot 'SidecarCore.psm1') -Force }

Describe 'Get-SidecarSettings' {
    It 'reports nothing missing when the shared secret is set' {
        $s = Get-SidecarSettings -Environment @{ SIDECAR_SHARED_SECRET = 'abc' }
        $s.Missing | Should -BeNullOrEmpty
        $s.SharedSecret | Should -Be 'abc'
        $s.ListenPrefix | Should -Be 'http://127.0.0.1:8091/'
    }
    It 'names the shared secret when it is missing, without throwing' {
        $s = Get-SidecarSettings -Environment @{}
        $s.Missing | Should -Be @('SIDECAR_SHARED_SECRET')
    }
    It 'treats an unresolved Key Vault reference as missing' {
        $s = Get-SidecarSettings -Environment @{ SIDECAR_SHARED_SECRET = '@Microsoft.KeyVault(VaultName=v;SecretName=s)' }
        $s.Missing[0] | Should -BeLike 'SIDECAR_SHARED_SECRET*did not resolve*'
        $s.SharedSecret | Should -Be ''
    }
}

Describe 'Get-ExchangeConnectParameters' {
    It 'connects with the caller''s access token and nothing else' {
        $p = Get-ExchangeConnectParameters -AccessToken 'tok' -Organization 'contoso.onmicrosoft.com'
        $p.AccessToken | Should -Be 'tok'
        $p.Organization | Should -Be 'contoso.onmicrosoft.com'
        $p.Keys | Should -Not -Contain 'Certificate'
        $p.Keys | Should -Not -Contain 'ManagedIdentity'
        $p.Keys | Should -Not -Contain 'AppId'
    }
    It 'refuses to connect without a token (no fallback credential)' {
        { Get-ExchangeConnectParameters -AccessToken '' -Organization 'contoso.onmicrosoft.com' } | Should -Throw '*X-Exchange-Access-Token*'
    }
}

# Beyond the task's test scope: these two rules decide Drift (H14a's T4 silent-fail trap).
Describe 'Assignment matching' {
    It 'matches an assignment to the app by any of its Exchange identities' {
        $sp = [pscustomobject]@{ Identity = 'sp-1'; Name = 'sp-1'; ObjectId = 'oid'; AppId = 'app' }
        Test-AssigneeIs ([pscustomobject]@{ RoleAssignee = 'sp-1' }) $sp | Should -BeTrue
        Test-AssigneeIs ([pscustomobject]@{ RoleAssignee = 'someone-else' }) $sp | Should -BeFalse
    }
    It 'treats an unscoped assignment as out of scope' {
        $g = [pscustomobject]@{ Identity = 'grp'; Name = 'grp'; DistinguishedName = 'CN=grp' }
        Test-AssignmentInScope ([pscustomobject]@{ RecipientGroupScope = 'CN=grp' }) $g | Should -BeTrue
        Test-AssignmentInScope ([pscustomobject]@{ RecipientGroupScope = '' }) $g | Should -BeFalse
    }
}
