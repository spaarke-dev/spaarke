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

# Task 263 (H14m): the customer's shared mailbox. Get-before-set — every Drift creates nothing; a second ensure writes
# nothing; the read route never writes.
Describe 'Customer shared mailbox (H14m)' {
    BeforeAll {
        function global:Get-Recipient { [CmdletBinding()] param($Filter) }
        function global:Get-Group { [CmdletBinding()] param($Identity) }
        function global:New-Mailbox { [CmdletBinding()] param([switch]$Shared, $Name, $Alias, $DisplayName, $PrimarySmtpAddress) }
        function global:Add-DistributionGroupMember { [CmdletBinding()] param($Identity, $Member, [switch]$BypassSecurityGroupManagerCheck) }
        function global:Test-ServicePrincipalAuthorization { [CmdletBinding()] param($Identity, $Resource) }
        $global:T263Roles = @('Application Mail.Read', 'Application Mail.ReadWrite', 'Application Mail.Send')
        function global:T263Request($Name = 'sprk-acme-mail', $Group = 'Spaarke-AppAccess-acme') {
            [pscustomobject]@{ tenantId = 't'; organization = 'contoso.onmicrosoft.com'; appId = '11111111-2222-3333-4444-555555555555'
                               scopeGroupId = '77777777-8888-9999-0000-111111111111'; expectedScopeGroupName = $Group; name = $Name
                               displayName = 'Acme Corp'; primarySmtpAddress = 'acme@contoso.com'; roles = $global:T263Roles; correlationId = 'run-1' }
        }
        function global:T263Mbx($Name = 'sprk-acme-mail', $Type = 'SharedMailbox', $Address = 'acme@contoso.com') {
            [pscustomobject]@{ Name = $Name; Alias = $Name; PrimarySmtpAddress = $Address; RecipientTypeDetails = $Type; DistinguishedName = "CN=$Name"; WhenCreatedUTC = '2026-10-10T10:00:00' }
        }
        function global:T263Grp($Name) { [pscustomobject]@{ Name = $Name; DistinguishedName = "CN=$Name"; RecipientTypeDetails = 'MailUniversalSecurityGroup' } }
    }
    BeforeEach {
        $global:T263GroupName = 'Spaarke-AppAccess-acme'
        $global:T263Mailboxes = @()                       # what the name/alias/address filter finds
        $global:T263MemberOf = @()                        # the groups the mailbox is a direct member of
        $global:T263InScope = $true
        Mock -ModuleName SidecarCore Get-Recipient -ParameterFilter { $Filter -like 'ExternalDirectoryObjectId*' } { @([pscustomobject]@{ DistinguishedName = "CN=$global:T263GroupName" }) }
        Mock -ModuleName SidecarCore Get-Recipient -ParameterFilter { $Filter -like 'Alias -eq*' } { $global:T263Mailboxes }
        Mock -ModuleName SidecarCore Get-Recipient -ParameterFilter { $Filter -like 'Members -eq*' } { $global:T263MemberOf }
        # MemberOfGroup -eq '<scope group DN>' -and Alias -eq '<name>': the mailbox, when the scope group is among its groups.
        Mock -ModuleName SidecarCore Get-Recipient -ParameterFilter { $Filter -like 'MemberOfGroup -eq*' } {
            if (@($global:T263MemberOf | Where-Object { $Filter -like "*'$($_.DistinguishedName)'*" }).Count -gt 0) { $global:T263Mailboxes }
        }
        Mock -ModuleName SidecarCore Get-Group { T263Grp $global:T263GroupName }
        # Test-CustomerMailboxWritePermission is NOT mocked: it reads the stubs above, which carry -Shared and
        # -BypassSecurityGroupManagerCheck, so every test here runs the real PRQ-E-16 probe (T186 L60).
        # Nor is Test-CustomerMailboxWriteAuthorization (T186 L62): its calls on the never-accepted probe address get
        # Exchange's validation answers below — 'not an accepted domain' and "Couldn't find object" mean authorized.
        Mock -ModuleName SidecarCore New-Mailbox {
            if ($PrimarySmtpAddress -like '*@authz-probe.invalid') { throw "You can't use the domain authz-probe.invalid because it's not an accepted domain for your organization." }
            $m = T263Mbx $Name; $global:T263Mailboxes = @($m); $m
        }
        Mock -ModuleName SidecarCore Add-DistributionGroupMember {
            if ($Member -like '*@authz-probe.invalid') { Write-Error "Couldn't find object ""$Member"". Please make sure that it was spelled correctly or specify a different object."; return }
            $global:T263MemberOf = @(T263Grp $global:T263GroupName)
        }
        Mock -ModuleName SidecarCore Test-ServicePrincipalAuthorization {
            $global:T263Roles | ForEach-Object { [pscustomobject]@{ RoleName = $_; InScope = $global:T263InScope } }
        }
    }

    It 'creates the shared mailbox, adds it to the scope group and verifies every role' {
        $r = Invoke-CustomerMailboxEnsure -Request (T263Request)

        $r.outcome | Should -Be 'Success'
        $r.created | Should -BeTrue
        $r.verified | Should -BeTrue
        Should -Invoke -ModuleName SidecarCore New-Mailbox -Times 1 -Exactly -ParameterFilter { $Shared -and $Name -eq 'sprk-acme-mail' -and $Alias -eq 'sprk-acme-mail' -and $PrimarySmtpAddress -eq 'acme@contoso.com' -and $DisplayName -eq 'Acme Corp' }
        Should -Invoke -ModuleName SidecarCore Add-DistributionGroupMember -Times 1 -Exactly -ParameterFilter { $Identity -eq 'CN=Spaarke-AppAccess-acme' -and $Member -eq 'CN=sprk-acme-mail' -and $BypassSecurityGroupManagerCheck }
    }

    It 'writes nothing on a second ensure when the mailbox is already in place' {
        $global:T263Mailboxes = @(T263Mbx)
        $global:T263MemberOf = @(T263Grp 'Spaarke-AppAccess-acme')

        $r = Invoke-CustomerMailboxEnsure -Request (T263Request)

        $r.outcome | Should -Be 'AlreadyCompliant'
        $r.verified | Should -BeTrue
        Should -Invoke -ModuleName SidecarCore New-Mailbox -Times 0 -Exactly
        Should -Invoke -ModuleName SidecarCore Add-DistributionGroupMember -Times 0 -Exactly
    }

    It 'is Drift, writing nothing, when a same-named mailbox exists outside the scope group' {
        $global:T263Mailboxes = @(T263Mbx)
        $global:T263MemberOf = @()

        $r = Invoke-CustomerMailboxEnsure -Request (T263Request)

        $r.outcome | Should -Be 'Drift'
        ($r.conflicts -join ' ') | Should -BeLike '*not a member of the scope group*'
        Should -Invoke -ModuleName SidecarCore New-Mailbox -Times 0 -Exactly
        Should -Invoke -ModuleName SidecarCore Add-DistributionGroupMember -Times 0 -Exactly
    }

    It 'is Drift when the mailbox is also in another customer''s group' {
        $global:T263Mailboxes = @(T263Mbx)
        $global:T263MemberOf = @((T263Grp 'Spaarke-AppAccess-acme'), (T263Grp 'Spaarke-AppAccess-other'))

        $r = Invoke-CustomerMailboxEnsure -Request (T263Request)

        $r.outcome | Should -Be 'Drift'
        ($r.conflicts -join ' ') | Should -BeLike '*Spaarke-AppAccess-other*'
    }

    It 'is Drift, writing nothing, when the address belongs to a foreign mailbox (another name, a user mailbox)' {
        $global:T263Mailboxes = @(T263Mbx -Name 'sprk-other-mail' -Type 'UserMailbox')

        $r = Invoke-CustomerMailboxEnsure -Request (T263Request)

        $r.outcome | Should -Be 'Drift'
        ($r.conflicts -join ' ') | Should -BeLike '*UserMailbox*'
        ($r.conflicts -join ' ') | Should -BeLike '*not this customer''s mailbox*'
        Should -Invoke -ModuleName SidecarCore New-Mailbox -Times 0 -Exactly
    }

    It 'is Drift, writing nothing, when the scope group is not this customer''s' {
        $global:T263GroupName = 'Spaarke-AppAccess-other'

        $r = Invoke-CustomerMailboxEnsure -Request (T263Request)

        $r.outcome | Should -Be 'Drift'
        ($r.conflicts -join ' ') | Should -BeLike '*not this customer''s group*'
        Should -Invoke -ModuleName SidecarCore New-Mailbox -Times 0 -Exactly
    }

    It 'accepts the scope group when Exchange appended its 14-digit creation time to the Name (T186 L59)' {
        # Live: a group created in the admin center 2026-10-14 19:48:47 UTC is named 'Spaarke-AppAccess-acme20261014194847'.
        $global:T263GroupName = 'Spaarke-AppAccess-acme20261014194847'

        $r = Invoke-CustomerMailboxEnsure -Request (T263Request)

        $r.outcome | Should -Be 'Success'
        Should -Invoke -ModuleName SidecarCore Add-DistributionGroupMember -Times 1 -Exactly -ParameterFilter { $Identity -eq 'CN=Spaarke-AppAccess-acme20261014194847' -and $Member -eq 'CN=sprk-acme-mail' }
    }

    It 'still refuses a group whose Name only resembles this customer''s (T186 L59 must-fire)' {
        foreach ($other in 'Spaarke-AppAccess-acme2', 'Spaarke-AppAccess-acme2026101419484', 'Spaarke-AppAccess-acme202610141948471', 'Spaarke-AppAccess-acme2x0261014194847') {
            InModuleScope SidecarCore -Parameters @{ Other = $other } {
                param($Other)
                Test-ScopeGroupName -Actual $Other -Expected 'Spaarke-AppAccess-acme' | Should -BeFalse -Because $Other
            }
        }
        InModuleScope SidecarCore {
            Test-ScopeGroupName -Actual 'spaarke-appaccess-ACME' -Expected 'Spaarke-AppAccess-acme' | Should -BeTrue
            Test-ScopeGroupName -Actual '' -Expected 'Spaarke-AppAccess-acme' | Should -BeFalse
        }
    }

    It 'retries the join while a new mailbox replicates, then succeeds' {
        InModuleScope SidecarCore { $script:JoinRetryDelaySeconds = 0 }
        $global:T263JoinFailures = 2
        Mock -ModuleName SidecarCore Add-DistributionGroupMember -ParameterFilter { $Member -eq 'CN=sprk-acme-mail' } {
            if ($global:T263JoinFailures-- -gt 0) { throw "Couldn't find object 'CN=sprk-acme-mail'." }
            $global:T263MemberOf = @(T263Grp $global:T263GroupName)
        }

        $r = Invoke-CustomerMailboxEnsure -Request (T263Request)

        $r.outcome | Should -Be 'Success'
        Should -Invoke -ModuleName SidecarCore Add-DistributionGroupMember -Times 3 -Exactly -ParameterFilter { $Member -eq 'CN=sprk-acme-mail' }
    }

    It 'reports Failure with created = true when the join never succeeds' {
        InModuleScope SidecarCore { $script:JoinRetryDelaySeconds = 0 }
        Mock -ModuleName SidecarCore Add-DistributionGroupMember -ParameterFilter { $Member -eq 'CN=sprk-acme-mail' } { throw 'Access denied' }

        $r = Invoke-CustomerMailboxEnsure -Request (T263Request)

        $r.outcome | Should -Be 'Failure'
        $r.created | Should -BeTrue
        $r.diagnostic | Should -BeLike '*could not add it to the scope group*Access denied*'
    }

    It 'joins the scope group WITHOUT -ErrorAction and creates the mailbox WITH it (T186 L62)' {
        # Exchange checks the common parameters against the role entry. 'Spaarke Scope Group Membership' cannot be granted
        # any (its parent role holds none for Add-DistributionGroupMember), so -ErrorAction Stop there is a 403.
        $r = Invoke-CustomerMailboxEnsure -Request (T263Request)

        $r.outcome | Should -Be 'Success'
        Should -Invoke -ModuleName SidecarCore Add-DistributionGroupMember -Times 0 -Exactly -ParameterFilter { $PesterBoundParameters.ContainsKey('ErrorAction') }
        Should -Invoke -ModuleName SidecarCore Add-DistributionGroupMember -Times 1 -Exactly -ParameterFilter { $Member -eq 'CN=sprk-acme-mail' }
        Should -Invoke -ModuleName SidecarCore New-Mailbox -Times 1 -Exactly -ParameterFilter { $Name -eq 'sprk-acme-mail' -and $PesterBoundParameters.ErrorAction -eq 'Stop' }
    }

    It 'a join error written to the error stream (not thrown) is a failed join, never a success (T186 L62 must-fire)' {
        InModuleScope SidecarCore { $script:JoinRetryDelaySeconds = 0 }
        Mock -ModuleName SidecarCore Add-DistributionGroupMember -ParameterFilter { $Member -eq 'CN=sprk-acme-mail' } { Write-Error 'Access denied for this group' }

        $r = Invoke-CustomerMailboxEnsure -Request (T263Request)

        $r.outcome | Should -Be 'Failure'
        $r.created | Should -BeTrue
        $r.diagnostic | Should -BeLike '*could not add it to the scope group*Access denied for this group*'
    }

    It 'an "already a member" answer counts as joined' {
        Mock -ModuleName SidecarCore Add-DistributionGroupMember -ParameterFilter { $Member -eq 'CN=sprk-acme-mail' } {
            $global:T263MemberOf = @(T263Grp $global:T263GroupName)
            Write-Error "The recipient ""CN=sprk-acme-mail"" is already a member of the group ""CN=$global:T263GroupName""."
        }

        $r = Invoke-CustomerMailboxEnsure -Request (T263Request)

        $r.outcome | Should -Be 'Success'
        Should -Invoke -ModuleName SidecarCore Add-DistributionGroupMember -Times 1 -Exactly -ParameterFilter { $Member -eq 'CN=sprk-acme-mail' }
    }

    It 'probes both writes on a never-accepted address before creating anything (T186 L62)' {
        $r = Invoke-CustomerMailboxEnsure -Request (T263Request)

        $r.outcome | Should -Be 'Success'
        Should -Invoke -ModuleName SidecarCore New-Mailbox -Times 1 -Exactly -ParameterFilter { $Shared -and $PrimarySmtpAddress -eq 'sprk-authz-probe@authz-probe.invalid' -and $PesterBoundParameters.ErrorAction -eq 'Stop' }
        Should -Invoke -ModuleName SidecarCore Add-DistributionGroupMember -Times 1 -Exactly -ParameterFilter { $Identity -eq 'CN=Spaarke-AppAccess-acme' -and $Member -eq 'sprk-authz-probe@authz-probe.invalid' -and $BypassSecurityGroupManagerCheck }
    }

    It 'fails BEFORE creating anything when Exchange refuses New-Mailbox -ErrorAction (T186 L62 must-fire)' {
        # Live 2026-10-20: the role entry without the common parameters made the module report only this.
        Mock -ModuleName SidecarCore New-Mailbox -ParameterFilter { $PrimarySmtpAddress -like '*@authz-probe.invalid' } {
            throw 'A server side error has occurred because of which the operation could not be completed. Please try again after some time.'
        }

        $r = Invoke-CustomerMailboxEnsure -Request (T263Request)

        $r.outcome | Should -Be 'Failure'
        $r.created | Should -BeFalse
        $r.diagnostic | Should -BeLike "*cannot run New-Mailbox -Shared -ErrorAction (role entry 'Spaarke Shared Mailbox Create\New-Mailbox' needs the common parameters*server side error*PRQ-E-16*"
        Should -Invoke -ModuleName SidecarCore New-Mailbox -Times 0 -Exactly -ParameterFilter { $Name -eq 'sprk-acme-mail' }
    }

    It 'fails BEFORE creating anything when Exchange refuses the scope-group join (T186 L62 must-fire)' {
        Mock -ModuleName SidecarCore Add-DistributionGroupMember -ParameterFilter { $Member -like '*@authz-probe.invalid' } {
            Write-Error 'A server side error has occurred because of which the operation could not be completed.'
        }

        $r = Invoke-CustomerMailboxEnsure -Request (T263Request)

        $r.outcome | Should -Be 'Failure'
        $r.created | Should -BeFalse
        $r.diagnostic | Should -BeLike '*cannot run Add-DistributionGroupMember -BypassSecurityGroupManagerCheck (Exchange answered*server side error*'
        Should -Invoke -ModuleName SidecarCore New-Mailbox -Times 0 -Exactly -ParameterFilter { $Name -eq 'sprk-acme-mail' }
    }

    It 'fails BEFORE creating anything when PRQ-E-16 is not applied' {
        Mock -ModuleName SidecarCore Test-CustomerMailboxWritePermission { @('Add-DistributionGroupMember -BypassSecurityGroupManagerCheck') }

        $r = Invoke-CustomerMailboxEnsure -Request (T263Request)

        $r.outcome | Should -Be 'Failure'
        $r.diagnostic | Should -BeLike '*cannot run Add-DistributionGroupMember -BypassSecurityGroupManagerCheck — prerequisite PRQ-E-16*'
        Should -Invoke -ModuleName SidecarCore New-Mailbox -Times 0 -Exactly
    }

    It 'the PRQ-E-16 probe finds nothing missing when both write parameters are loaded (T186 L60)' {
        InModuleScope SidecarCore { @(Test-CustomerMailboxWritePermission).Count | Should -Be 0 }
    }

    It 'the PRQ-E-16 probe names exactly what the session lacks (T186 L60 must-fire)' {
        # Exchange Online loads a cmdlet without the parameters the caller's roles do not grant.
        Mock -ModuleName SidecarCore Get-Command -ParameterFilter { $Name -eq 'New-Mailbox' } {
            [pscustomobject]@{ Parameters = @{ Name = $null } }
        }
        Mock -ModuleName SidecarCore Get-Command -ParameterFilter { $Name -eq 'Add-DistributionGroupMember' } { $null }

        $r = Invoke-CustomerMailboxEnsure -Request (T263Request)

        $r.outcome | Should -Be 'Failure'
        $r.diagnostic | Should -BeLike '*cannot run New-Mailbox -Shared / Add-DistributionGroupMember -BypassSecurityGroupManagerCheck — *'
        $r.diagnostic | Should -Not -BeLike '*System.Object*'
        Should -Invoke -ModuleName SidecarCore New-Mailbox -Times 0 -Exactly
    }

    It 'reports created but not verified when a role is not in scope yet' {
        $global:T263InScope = $false

        $r = Invoke-CustomerMailboxEnsure -Request (T263Request)

        $r.outcome | Should -Be 'Success'
        $r.verified | Should -BeFalse
        @($r.authorization | Where-Object { -not $_.inScope }).Count | Should -Be 3
    }

    It 'fails when the scope group cannot be found' {
        Mock -ModuleName SidecarCore Get-Recipient -ParameterFilter { $Filter -like 'ExternalDirectoryObjectId*' } { @() }
        Mock -ModuleName SidecarCore Get-Group { }

        (Invoke-CustomerMailboxEnsure -Request (T263Request)).outcome | Should -Be 'Failure'
        Should -Invoke -ModuleName SidecarCore New-Mailbox -Times 0 -Exactly
    }

    It 'reads state without writing anything' {
        $global:T263Mailboxes = @(T263Mbx)
        $global:T263MemberOf = @(T263Grp 'Spaarke-AppAccess-acme')

        $r = Get-CustomerMailboxState -Request (T263Request)

        $r.outcome | Should -Be 'Success'
        $r.exists | Should -BeTrue
        @($r.conflicts).Count | Should -Be 0
        @($r.authorization | Where-Object { $_.inScope }).Count | Should -Be 3
        Should -Invoke -ModuleName SidecarCore New-Mailbox -Times 0 -Exactly
        Should -Invoke -ModuleName SidecarCore Add-DistributionGroupMember -Times 0 -Exactly
    }

    It 'reads a missing mailbox as exists = false (not a failure)' {
        $r = Get-CustomerMailboxState -Request (T263Request)

        $r.outcome | Should -Be 'Success'
        $r.exists | Should -BeFalse
    }
}

Describe 'Customer mailbox request validation' {
    BeforeAll {
        function global:T263Body($Group = 'Spaarke-AppAccess-acme', $Name = 'sprk-acme-mail', $Role = 'Application Mail.Send') {
            [pscustomobject]@{ tenantId = 't'; organization = 'contoso.onmicrosoft.com'; appId = '11111111-2222-3333-4444-555555555555'
                               scopeGroupId = 'grp@contoso.com'; expectedScopeGroupName = $Group; name = $Name
                               displayName = 'Acme'; primarySmtpAddress = 'acme@contoso.com'; roles = @($Role); correlationId = 'c' }
        }
    }
    It 'accepts a complete request' {
        (Test-CustomerMailboxRequest -Body (T263Body)).Count | Should -Be 0
    }
    It 'refuses a mailbox name and a scope group that name different customers' {
        (Test-CustomerMailboxRequest -Body (T263Body -Group 'Spaarke-AppAccess-other')) -join ' ' | Should -BeLike '*different customers*'
    }
    It 'refuses a name outside the sprk-{customerId}-mail pattern and an unknown role' {
        $errors = (Test-CustomerMailboxRequest -Body (T263Body -Name 'ceo' -Role 'Application Exchange Full Access')) -join ' '
        $errors | Should -BeLike '*sprk-{customerId}-mail*'
        $errors | Should -BeLike '*not one the sidecar knows*'
    }
}

# T186 L61: an Exchange failure names the command that threw, the error id and the inner exception — Exchange's
# "A server side error has occurred" message alone named neither the cmdlet nor the cause.
Describe 'Exchange failure diagnostic (T186 L61)' {
    It 'names the command, the message, the error id, the category and the inner exception' {
        function global:New-Mailbox { [CmdletBinding()] param() throw [InvalidOperationException]::new('A server side error has occurred.', [System.Net.WebException]::new('BadRequest: proxy address in use')) }
        $record = $null
        try { New-Mailbox } catch { $record = $_ }

        $text = Format-ExchangeError $record

        $text | Should -BeLike 'New-Mailbox: A server side error has occurred.*'
        $text | Should -BeLike '*id: *'
        $text | Should -BeLike '*category: *'
        $text | Should -BeLike '*at: New-Mailbox <*'
        $text | Should -BeLike '*inner WebException: BadRequest: proxy address in use*'
    }

    It 'stays one bounded line' {
        $record = $null
        try { throw ("line one`nline two " + ('x' * 3000)) } catch { $record = $_ }

        $text = Format-ExchangeError $record

        $text | Should -Not -Match "`n"
        $text.Length | Should -BeLessOrEqual 1501
    }

    It 'every route reports Exchange failures through it' {
        $listener = Get-Content (Join-Path $PSScriptRoot 'Listener.ps1') -Raw
        ([regex]::Matches($listener, 'Exchange call failed: \$\(Format-ExchangeError \$_\)')).Count | Should -Be 4
        $listener | Should -Not -BeLike '*Exchange call failed: $($_.Exception.Message)*'
    }
}
