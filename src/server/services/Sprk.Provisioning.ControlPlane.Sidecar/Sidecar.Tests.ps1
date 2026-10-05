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

# Beyond the task's test scope (code review W5): the apply logic decides H14a's T4 outcome —
# Drift must create nothing, and success must be proven by read-back. Exchange creates these
# cmdlets only after Connect-ExchangeOnline, so empty global stubs stand in for Pester to mock.
Describe 'Invoke-MailboxAccessApply' {
    BeforeAll {
        function global:Get-Recipient { [CmdletBinding()] param($Filter) }
        function global:Get-Group { [CmdletBinding()] param($Identity) }
        function global:Get-ServicePrincipal { [CmdletBinding()] param($Identity) }
        function global:New-ServicePrincipal { [CmdletBinding()] param($AppId, $ObjectId, $DisplayName) }
        function global:Get-ManagementRoleAssignment { [CmdletBinding()] param($Identity, $RoleAssignee, $Role) }
        function global:New-ManagementRoleAssignment { [CmdletBinding()] param($Name, $App, $Role, $RecipientGroupScope) }
        $script:App = '11111111-2222-3333-4444-555555555555'
        $script:Oid = '99999999-8888-7777-6666-555555555555'
        $script:Request = [pscustomobject]@{
            tenantId = 't'; appId = $script:App; servicePrincipalObjectId = $script:Oid; displayName = 'stamp'
            scopeGroupId = '77777777-8888-9999-0000-111111111111'; correlationId = 'run-1'
            assignments = @([pscustomobject]@{ name = 'Spaarke-acme-MailSend'; role = 'Application Mail.Send' },
                            [pscustomobject]@{ name = 'Spaarke-acme-MailRead'; role = 'Application Mail.Read' })
        }
        # Shape Exchange returns (live, 2026-10-04): a -RecipientGroupScope assignment reads back as
        # RecipientWriteScope 'Group' with the group's Name in CustomResourceScope.
        function script:Assignment($Name, $Role, $Scope = 'grp', $Kind = 'Group') {
            if (-not $Scope) { $Kind = 'Organization' }
            [pscustomobject]@{ Name = $Name; Role = $Role; RoleAssignee = $script:Oid; RecipientWriteScope = $Kind; CustomResourceScope = $Scope }
        }
    }
    BeforeEach {
        $global:T251Held = @()
        $global:T251SpExists = $true
        Mock -ModuleName SidecarCore Get-Recipient { @([pscustomobject]@{ DistinguishedName = 'CN=grp' }) }
        Mock -ModuleName SidecarCore Get-Group { [pscustomobject]@{ Name = 'grp'; DistinguishedName = 'CN=grp'; Guid = 'g'; RecipientTypeDetails = 'MailUniversalSecurityGroup' } }
        Mock -ModuleName SidecarCore Get-ServicePrincipal { if ($global:T251SpExists) { [pscustomobject]@{ ObjectId = $script:Oid; AppId = $script:App; Identity = $script:Oid } } }
        Mock -ModuleName SidecarCore New-ServicePrincipal { $global:T251SpExists = $true; [pscustomobject]@{ ObjectId = $script:Oid; AppId = $script:App; Identity = $script:Oid } }
        Mock -ModuleName SidecarCore Get-ManagementRoleAssignment -ParameterFilter { $RoleAssignee } { $global:T251Held }
        Mock -ModuleName SidecarCore Get-ManagementRoleAssignment -ParameterFilter { $Identity } { $global:T251Held | Where-Object Name -eq $Identity }
        Mock -ModuleName SidecarCore New-ManagementRoleAssignment { $global:T251Held += Assignment $Name $Role ($RecipientGroupScope -replace '^CN=([^,]+).*$', '$1') }
    }

    It 'registers the identity and creates every missing assignment in scope' {
        $global:T251SpExists = $false

        $r = Invoke-MailboxAccessApply -Request $script:Request

        $r.outcome | Should -Be 'Success'
        $r.createdCount | Should -Be 2
        Should -Invoke -ModuleName SidecarCore New-ServicePrincipal -Times 1 -Exactly
        Should -Invoke -ModuleName SidecarCore New-ManagementRoleAssignment -Times 2 -Exactly -ParameterFilter { $RecipientGroupScope -eq 'CN=grp' }
    }

    It 'is AlreadyCompliant and changes nothing when everything is in place' {
        $global:T251Held = @((Assignment 'Spaarke-acme-MailSend' 'Application Mail.Send'), (Assignment 'Spaarke-acme-MailRead' 'Application Mail.Read'))

        (Invoke-MailboxAccessApply -Request $script:Request).outcome | Should -Be 'AlreadyCompliant'
        Should -Invoke -ModuleName SidecarCore New-ManagementRoleAssignment -Times 0 -Exactly
    }

    It 'is Drift, creating nothing, when a named assignment has another scope' {
        $global:T251Held = @(Assignment 'Spaarke-acme-MailSend' 'Application Mail.Send' 'other-customer')
        $global:T251SpExists = $true

        $r = Invoke-MailboxAccessApply -Request $script:Request

        $r.outcome | Should -Be 'Drift'
        ($r.conflicts -join ' ') | Should -BeLike '*Group:other-customer*'
        Should -Invoke -ModuleName SidecarCore New-ManagementRoleAssignment -Times 0 -Exactly
        Should -Invoke -ModuleName SidecarCore New-ServicePrincipal -Times 0 -Exactly
    }

    It 'is Drift when a named assignment uses a management scope that shares the group''s name' {
        $global:T251Held = @(Assignment 'Spaarke-acme-MailSend' 'Application Mail.Send' 'grp' 'CustomRecipientScope')

        $r = Invoke-MailboxAccessApply -Request $script:Request

        $r.outcome | Should -Be 'Drift'
        ($r.conflicts -join ' ') | Should -BeLike '*CustomRecipientScope:grp*'
        Should -Invoke -ModuleName SidecarCore New-ManagementRoleAssignment -Times 0 -Exactly
    }

    It 'is Drift when the identity holds any other application role' {
        $global:T251Held = @(Assignment 'manual-grant' 'Application Exchange Full Access' '')

        $r = Invoke-MailboxAccessApply -Request $script:Request

        $r.outcome | Should -Be 'Drift'
        ($r.conflicts -join ' ') | Should -BeLike '*Application Exchange Full Access*'
        Should -Invoke -ModuleName SidecarCore New-ManagementRoleAssignment -Times 0 -Exactly
    }

    It 'fails when the scope group cannot be found' {
        Mock -ModuleName SidecarCore Get-Recipient { @() }
        Mock -ModuleName SidecarCore Get-Group { }

        (Invoke-MailboxAccessApply -Request $script:Request).outcome | Should -Be 'Failure'
        Should -Invoke -ModuleName SidecarCore New-ServicePrincipal -Times 0 -Exactly
    }

    It 'fails when the read-back does not show an assignment in scope' {
        Mock -ModuleName SidecarCore New-ManagementRoleAssignment { }

        $r = Invoke-MailboxAccessApply -Request $script:Request

        $r.outcome | Should -Be 'Failure'
        $r.diagnostic | Should -BeLike '*Read-back*'
    }
}

Describe 'Request validation' {
    BeforeAll {
        function script:ApplyBody($Organization = 'contoso.onmicrosoft.com', $Role = 'Application Mail.Read') {
            [pscustomobject]@{ tenantId = 't'; organization = $Organization; appId = '11111111-2222-3333-4444-555555555555'
                               servicePrincipalObjectId = '99999999-8888-7777-6666-555555555555'; scopeGroupId = 'group@contoso.com'; correlationId = 'c'
                               assignments = @([pscustomobject]@{ name = 'x'; role = $Role }) }
        }
    }
    It 'accepts a complete request' {
        (Test-ApplyRequest -Body (ApplyBody)).Count | Should -Be 0   # the function returns its list comma-wrapped
    }
    It 'refuses a role outside the allow-list' {
        (Test-ApplyRequest -Body (ApplyBody -Role 'Application Exchange Full Access')) -join ' ' | Should -BeLike '*not one the sidecar may grant*'
    }
    # Writes fail with "doesn't have write permission to target DC" when connected by tenant GUID (live, 2026-10-04).
    It 'refuses the tenant GUID as the organization' {
        (Test-ApplyRequest -Body (ApplyBody -Organization 'a221a95e-6abc-4434-aecc-e48338a1b2f2')) -join ' ' | Should -BeLike '*initial domain*'
    }
    It 'requires the organization on the read route too' {
        $body = [pscustomobject]@{ tenantId = 't'; appId = '11111111-2222-3333-4444-555555555555'; scopeGroupId = 'group@contoso.com'; roles = @('Application Mail.Read') }
        (Test-ReadRequest -Body $body) -join ' ' | Should -BeLike '*organization is required*'
    }
}
